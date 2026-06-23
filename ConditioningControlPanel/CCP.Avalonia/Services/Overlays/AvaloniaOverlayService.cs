using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Services.Mod;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Settings;
using LibVLCSharp.Shared;

namespace ConditioningControlPanel.Avalonia.Services.Overlays;

/// <summary>
/// Avalonia implementation of the screen-overlay subsystem.
/// Supports pink filter, spiral (GIF/static image), brain-drain darkening/pulsing overlay,
/// and ad-hoc sustained/timed overlays.
/// </summary>
public sealed class AvaloniaOverlayService : IOverlayService, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IScreenProvider _screens;
    private readonly IAppEnvironment _environment;
    private readonly LibVLC _libVlc;
    private readonly ILogger<AvaloniaOverlayService>? _logger;
    private readonly object _sync = new();
    private readonly List<OverlayWindow> _pinkFilterWindows = new();
    private readonly List<SpiralOverlayWindow> _spiralWindows = new();
    private readonly List<BrainDrainOverlayWindow> _brainDrainWindows = new();
    private readonly Dictionary<string, SustainedOverlayState> _sustainedOverlays = new();
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _brainDrainPulseTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };

    private bool _isRunning;
    private bool _isDisposed;
    private int _lastAppliedPinkOpacity = -1;
    private int _lastAppliedSpiralOpacity = -1;
    private int _lastAppliedBrainDrainIntensity = -1;
    private int? _adHocPinkOpacity;
    private int? _adHocSpiralOpacity;
    private int? _adHocBrainDrainIntensity;
    private SpiralCache? _spiralCache;
    private string _lastSpiralCacheKey = "";
    private double _brainDrainPulsePhase;

    public AvaloniaOverlayService(
        ISettingsService settings,
        IScreenProvider screens,
        IAppEnvironment environment,
        LibVLC libVlc,
        ILogger<AvaloniaOverlayService>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _screens = screens ?? throw new ArgumentNullException(nameof(screens));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _libVlc = libVlc ?? throw new ArgumentNullException(nameof(libVlc));
        _logger = logger;
        _updateTimer.Tick += UpdateOverlays;
        _brainDrainPulseTimer.Tick += BrainDrainPulseTick;
        _screens.ScreensChanged += (_, _) => RefreshForDualMonitorChange();
    }

    public bool IsRunning => _isRunning;
    public bool BypassLevelCheck { get; set; }

    public void Start()
    {
        if (_isRunning || _isDisposed) return;
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
        {
            _logger?.LogDebug("AvaloniaOverlayService: overlays are not supported on mobile; Start is a no-op");
            return;
        }

        _isRunning = true;
        Dispatcher.UIThread.Invoke(RefreshOverlays);
        _updateTimer.Start();
        _logger?.LogInformation("AvaloniaOverlayService started");
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;
        _updateTimer.Stop();
        Dispatcher.UIThread.Invoke(() =>
        {
            StopPinkFilter();
            StopSpiral();
            StopBrainDrain();
            StopAllSustainedOverlays();
        });
        _logger?.LogInformation("AvaloniaOverlayService stopped");
    }

    public void RefreshOverlays()
    {
        if (!_isRunning) return;
        Dispatcher.UIThread.Invoke(() =>
        {
            var settings = _settings.Current;
            if (settings == null) return;

            // Render the visual stack bottom-to-top so later windows sit above earlier
            // ones within the topmost layer: brain-drain -> spiral -> pink filter.
            var brainDrainWanted = settings.BrainDrainEnabled || _adHocBrainDrainIntensity.HasValue;
            if (brainDrainWanted)
            {
                var intensity = _adHocBrainDrainIntensity ?? settings.BrainDrainIntensity;
                if (_brainDrainWindows.Count == 0)
                    StartBrainDrain(intensity);
                else
                    UpdateBrainDrainIntensity(intensity);
            }
            else
            {
                StopBrainDrain();
            }

            var spiralPath = GetSpiralPath();
            var spiralWanted = (settings.SpiralEnabled || _adHocSpiralOpacity.HasValue) && !string.IsNullOrEmpty(spiralPath);
            if (spiralWanted)
            {
                if (_spiralWindows.Count == 0 || !string.Equals(spiralPath, _lastSpiralCacheKey, StringComparison.OrdinalIgnoreCase))
                    StartSpiral(spiralPath, _adHocSpiralOpacity ?? settings.SpiralOpacity);
                else
                    UpdateSpiralOpacity();
            }
            else
            {
                StopSpiral();
            }

            var pinkWanted = settings.PinkFilterEnabled || _adHocPinkOpacity.HasValue;
            if (pinkWanted)
            {
                if (_pinkFilterWindows.Count == 0)
                    StartPinkFilter(_adHocPinkOpacity ?? settings.PinkFilterOpacity);
                else
                    UpdatePinkFilterOpacity();
            }
            else
            {
                StopPinkFilter();
            }
        });
    }

    public void PulseOverlays()
    {
        if (!_isRunning) return;
        Dispatcher.UIThread.Invoke(() =>
        {
            var settings = _settings.Current;
            if (settings == null || !_pinkFilterWindows.Any() && !_spiralWindows.Any() && !_brainDrainWindows.Any()) return;

            var hasPink = settings.PinkFilterEnabled || _adHocPinkOpacity.HasValue;
            var hasSpiral = (settings.SpiralEnabled || _adHocSpiralOpacity.HasValue) && _spiralWindows.Count > 0;
            var hasBrainDrain = settings.BrainDrainEnabled || _adHocBrainDrainIntensity.HasValue;

            if (hasPink && _pinkFilterWindows.Count > 0)
            {
                var boosted = Math.Min((_adHocPinkOpacity ?? settings.PinkFilterOpacity) * 2, 100);
                var color = GetFilterColor(boosted);
                foreach (var window in _pinkFilterWindows)
                    window.UpdateColor(color);

                _lastAppliedPinkOpacity = -1;
            }

            if (hasSpiral)
            {
                var boostedOpacity = Math.Min((_adHocSpiralOpacity ?? settings.SpiralOpacity) * 2, 100);
                foreach (var window in _spiralWindows)
                    window.UpdateOpacity(boostedOpacity);

                _lastAppliedSpiralOpacity = -1;
            }

            if (hasBrainDrain && _brainDrainWindows.Count > 0)
            {
                var boostedIntensity = Math.Min((_adHocBrainDrainIntensity ?? settings.BrainDrainIntensity) * 2, 100);
                UpdateBrainDrainIntensity(boostedIntensity);
                _lastAppliedBrainDrainIntensity = -1;
            }

            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                Dispatcher.UIThread.Invoke(() =>
                {
                    if (hasPink) UpdatePinkFilterOpacity();
                    if (hasSpiral) UpdateSpiralOpacity();
                    if (hasBrainDrain) UpdateBrainDrainIntensity();
                });
            });
        });
    }

    public void RefreshForDualMonitorChange()
    {
        if (!_isRunning) return;
        Dispatcher.UIThread.Invoke(() =>
        {
            var settings = _settings.Current;
            if (settings == null) return;

            StopPinkFilter();
            StopSpiral();
            StopBrainDrain();

            if (settings.PinkFilterEnabled || _adHocPinkOpacity.HasValue)
                StartPinkFilter(_adHocPinkOpacity ?? settings.PinkFilterOpacity);

            var spiralPath = GetSpiralPath();
            if ((settings.SpiralEnabled || _adHocSpiralOpacity.HasValue) && !string.IsNullOrEmpty(spiralPath))
                StartSpiral(spiralPath, _adHocSpiralOpacity ?? settings.SpiralOpacity);

            if (settings.BrainDrainEnabled || _adHocBrainDrainIntensity.HasValue)
                StartBrainDrain(_adHocBrainDrainIntensity ?? settings.BrainDrainIntensity);
        });
    }

    public void ShowOverlayTimed(string kind, int durationMs, double opacity)
    {
        if (_isDisposed) return;

        var normalizedKind = NormalizeOverlayKind(kind);
        if (normalizedKind == null)
        {
            _logger?.LogDebug("AvaloniaOverlayService.ShowOverlayTimed: unsupported kind {Kind}", kind);
            return;
        }

        var safeDurationMs = Math.Max(50, durationMs);
        var clampedOpacity = Math.Clamp(opacity, 0.0, 1.0);

        Dispatcher.UIThread.Invoke(() =>
        {
            if (!_isRunning && normalizedKind != "braindrain") return; // ad-hoc brain-drain can work while service stopped

            ShowSustainedOverlayInternal(normalizedKind, clampedOpacity);

            // Stop any existing timer for this kind first.
            if (_sustainedOverlays.TryGetValue(normalizedKind, out var existing))
            {
                existing.AutoHideTimer?.Stop();
                existing.AutoHideTimer = null;
            }

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(safeDurationMs) };
            var state = new SustainedOverlayState(normalizedKind) { AutoHideTimer = timer };
            _sustainedOverlays[normalizedKind] = state;

            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Dispatcher.UIThread.Invoke(() => HideOverlaySustained(normalizedKind));
            };
            timer.Start();
        });
    }

    public void ShowOverlaySustained(string kind, double opacity)
    {
        if (_isDisposed) return;

        var normalizedKind = NormalizeOverlayKind(kind);
        if (normalizedKind == null)
        {
            _logger?.LogDebug("AvaloniaOverlayService.ShowOverlaySustained: unsupported kind {Kind}", kind);
            return;
        }

        var clampedOpacity = Math.Clamp(opacity, 0.0, 1.0);

        Dispatcher.UIThread.Invoke(() =>
        {
            if (!_isRunning && normalizedKind != "braindrain") return;

            // Idempotent: if already active, leave it to SetSustainedOverlayOpacity to change opacity.
            if (_sustainedOverlays.ContainsKey(normalizedKind)) return;

            ShowSustainedOverlayInternal(normalizedKind, clampedOpacity);
            _sustainedOverlays[normalizedKind] = new SustainedOverlayState(normalizedKind);
        });
    }

    public void HideOverlaySustained(string kind)
    {
        if (_isDisposed) return;

        var normalizedKind = NormalizeOverlayKind(kind);
        if (normalizedKind == null) return;

        Dispatcher.UIThread.Invoke(() =>
        {
            if (_sustainedOverlays.TryGetValue(normalizedKind, out var state))
            {
                state.AutoHideTimer?.Stop();
                _sustainedOverlays.Remove(normalizedKind);
            }

            switch (normalizedKind)
            {
                case "pink":
                    _adHocPinkOpacity = null;
                    StopPinkFilter();
                    break;
                case "spiral":
                    _adHocSpiralOpacity = null;
                    StopSpiral();
                    break;
                case "braindrain":
                    _adHocBrainDrainIntensity = null;
                    StopBrainDrain();
                    break;
            }
        });
    }

    public void SetSustainedOverlayOpacity(string kind, double opacity)
    {
        if (_isDisposed) return;

        var normalizedKind = NormalizeOverlayKind(kind);
        if (normalizedKind == null) return;

        var clampedOpacity = Math.Clamp(opacity, 0.0, 1.0);

        Dispatcher.UIThread.Invoke(() =>
        {
            switch (normalizedKind)
            {
                case "pink":
                    _adHocPinkOpacity = (int)Math.Round(clampedOpacity * 100);
                    if (_pinkFilterWindows.Count > 0)
                        UpdatePinkFilterOpacity();
                    else if (_isRunning)
                        StartPinkFilter(_adHocPinkOpacity.Value);
                    break;
                case "spiral":
                    _adHocSpiralOpacity = (int)Math.Round(clampedOpacity * 100);
                    if (_spiralWindows.Count > 0)
                        UpdateSpiralOpacity();
                    else if (_isRunning)
                    {
                        var spiralPath = GetSpiralPath();
                        if (!string.IsNullOrEmpty(spiralPath))
                            StartSpiral(spiralPath, _adHocSpiralOpacity.Value);
                    }
                    break;
                case "braindrain":
                    _adHocBrainDrainIntensity = Math.Max(1, (int)Math.Round(clampedOpacity * 100));
                    if (_brainDrainWindows.Count > 0)
                        UpdateBrainDrainIntensity(_adHocBrainDrainIntensity.Value);
                    else if (_isRunning)
                        StartBrainDrain(_adHocBrainDrainIntensity.Value);
                    break;
            }
        });
    }

    public void WarmSpiralCache()
    {
        var path = GetSpiralPath();
        if (string.IsNullOrEmpty(path)) return;
        _ = Task.Run(() => LoadSpiralCache(path));
    }

    private void UpdateOverlays(object? sender, EventArgs e)
    {
        if (!_isRunning) return;
        RefreshOverlays();
        // Relative z-order is owned by OverlayZ (the shared coordinator); no per-service re-pinning.
    }

    private void ShowSustainedOverlayInternal(string kind, double opacity)
    {
        var opacityPercent = (int)Math.Round(opacity * 100);
        var settings = _settings.Current;

        switch (kind)
        {
            case "pink":
                _adHocPinkOpacity = opacityPercent;
                if (_pinkFilterWindows.Count == 0)
                    StartPinkFilter(opacityPercent);
                else
                    UpdatePinkFilterOpacity();
                break;
            case "spiral":
                _adHocSpiralOpacity = opacityPercent;
                var spiralPath = GetSpiralPath();
                if (string.IsNullOrEmpty(spiralPath))
                {
                    _logger?.LogDebug("AvaloniaOverlayService.ShowOverlaySustained: no spiral path configured");
                    return;
                }
                if (_spiralWindows.Count == 0 || !string.Equals(spiralPath, _lastSpiralCacheKey, StringComparison.OrdinalIgnoreCase))
                    StartSpiral(spiralPath, opacityPercent);
                else
                    UpdateSpiralOpacity();
                break;
            case "braindrain":
                _adHocBrainDrainIntensity = Math.Max(1, opacityPercent);
                if (_brainDrainWindows.Count == 0)
                    StartBrainDrain(_adHocBrainDrainIntensity.Value);
                else
                    UpdateBrainDrainIntensity(_adHocBrainDrainIntensity.Value);
                break;
        }
    }

    private static string? NormalizeOverlayKind(string kind)
    {
        return kind?.ToLowerInvariant() switch
        {
            "pink" or "pink_filter" => "pink",
            "spiral" => "spiral",
            "braindrain" or "blur" => "braindrain",
            _ => null
        };
    }

    private void StopAllSustainedOverlays()
    {
        foreach (var state in _sustainedOverlays.Values.ToList())
            state.AutoHideTimer?.Stop();
        _sustainedOverlays.Clear();
        _adHocPinkOpacity = null;
        _adHocSpiralOpacity = null;
        _adHocBrainDrainIntensity = null;
    }

    private void StartPinkFilter(int opacityPercent)
    {
        StopPinkFilter();
        var screens = GetScreens();
        foreach (var screen in screens)
        {
            var window = new OverlayWindow(screen, GetFilterColor(opacityPercent));
            window.Show();
            ApplyWindowStyles(window);
            OverlayZ.Register(window, OverlayZ.Layer.PinkTint);
            lock (_sync) { _pinkFilterWindows.Add(window); }
        }
        _lastAppliedPinkOpacity = opacityPercent;
    }

    private void StopPinkFilter()
    {
        lock (_sync)
        {
            foreach (var window in _pinkFilterWindows)
            {
                try { window.Close(); } catch { }
            }
            _pinkFilterWindows.Clear();
        }
        _lastAppliedPinkOpacity = -1;
    }

    private void UpdatePinkFilterOpacity()
    {
        var settings = _settings.Current;
        if (settings == null) return;

        var opacity = _adHocPinkOpacity ?? settings.PinkFilterOpacity;
        if (opacity == _lastAppliedPinkOpacity) return;

        var color = GetFilterColor(opacity);
        lock (_sync)
        {
            foreach (var window in _pinkFilterWindows)
                window.UpdateColor(color);
        }
        _lastAppliedPinkOpacity = opacity;
    }

    private static Color GetFilterColor(int opacityPercent)
    {
        var alpha = (byte)Math.Clamp(opacityPercent / 100.0 * 255, 0, 255);
        var accent = AppColor("PinkColor", new Color(0xFF, 0xFF, 0x69, 0xB4));
        return new Color(alpha, accent.R, accent.G, accent.B);
    }

    private static Color AppColor(string key, Color fallback)
    {
        if (global::Avalonia.Application.Current?.TryGetResource(key, global::Avalonia.Styling.ThemeVariant.Default, out var v) == true && v is Color c)
            return c;
        return fallback;
    }

    private string GetSpiralPath()
    {
        var settings = _settings.Current;
        if (settings == null) return "";

        if (!string.IsNullOrEmpty(settings.SpiralPath) && File.Exists(settings.SpiralPath))
            return settings.SpiralPath;

        // Prefer an active-mod override, then the shipped Spirals folder, then the assets folder.
        var resolver = App.Services?.GetService<AvaloniaModResourceResolver>();
        var modUri = resolver?.ResolveUri("spiral.gif");
        if (!string.IsNullOrEmpty(modUri) && modUri.StartsWith("file://", StringComparison.Ordinal))
        {
            var modPath = modUri.Substring(7);
            if (File.Exists(modPath)) return modPath;
        }

        var fallback = Path.Combine(_environment.BaseDirectory, "Spirals", "spiral.gif");
        if (File.Exists(fallback)) return fallback;

        var assetsFallback = Path.Combine(_environment.EffectiveAssetsPath, "spiral.gif");
        if (File.Exists(assetsFallback)) return assetsFallback;

        return "";
    }

    private void StartSpiral(string path, int opacityPercent)
    {
        StopSpiral();
        var cache = LoadSpiralCache(path);
        if (cache == null) return;

        var screens = GetScreens();
        foreach (var screen in screens)
        {
            SpiralOverlayWindow window;
            if (cache.Kind == SpiralCacheKind.Video)
            {
                window = new SpiralOverlayWindow(screen, _libVlc, path, opacityPercent);
            }
            else if (cache.Kind == SpiralCacheKind.AnimatedGif)
            {
                var animator = AvaloniaAnimatedGif.TryCreate(path);
                window = new SpiralOverlayWindow(screen, cache, opacityPercent, animator);
            }
            else
            {
                window = new SpiralOverlayWindow(screen, cache, opacityPercent);
            }
            window.Show();
            ApplyWindowStyles(window);
            OverlayZ.Register(window, OverlayZ.Layer.Spiral);
            lock (_sync) { _spiralWindows.Add(window); }
        }
        _lastAppliedSpiralOpacity = opacityPercent;
        _lastSpiralCacheKey = path;
    }

    private void StopSpiral()
    {
        lock (_sync)
        {
            foreach (var window in _spiralWindows)
            {
                try { window.Close(); } catch { }
            }
            _spiralWindows.Clear();
        }
        _lastAppliedSpiralOpacity = -1;
        _lastSpiralCacheKey = "";
    }

    private void UpdateSpiralOpacity()
    {
        var settings = _settings.Current;
        if (settings == null) return;

        var opacity = _adHocSpiralOpacity ?? settings.SpiralOpacity;
        if (opacity == _lastAppliedSpiralOpacity) return;

        lock (_sync)
        {
            foreach (var window in _spiralWindows)
                window.UpdateOpacity(opacity);
        }
        _lastAppliedSpiralOpacity = opacity;
    }

    private void StartBrainDrain(int intensity)
    {
        StopBrainDrain();
        var screens = GetScreens();
        foreach (var screen in screens)
        {
            var window = new BrainDrainOverlayWindow(screen, intensity);
            window.Show();
            ApplyWindowStyles(window);
            OverlayZ.Register(window, OverlayZ.Layer.BrainDrain);
            lock (_sync) { _brainDrainWindows.Add(window); }
        }
        _lastAppliedBrainDrainIntensity = intensity;
        _brainDrainPulsePhase = 0;
        _brainDrainPulseTimer.Start();
    }

    private void StopBrainDrain()
    {
        _brainDrainPulseTimer.Stop();
        lock (_sync)
        {
            foreach (var window in _brainDrainWindows)
            {
                try { window.Close(); } catch { }
            }
            _brainDrainWindows.Clear();
        }
        _lastAppliedBrainDrainIntensity = -1;
        _brainDrainPulsePhase = 0;
    }

    private void UpdateBrainDrainIntensity(int? intensityOverride = null)
    {
        var settings = _settings.Current;
        if (settings == null) return;

        var intensity = intensityOverride ?? _adHocBrainDrainIntensity ?? settings.BrainDrainIntensity;
        if (intensity == _lastAppliedBrainDrainIntensity) return;

        lock (_sync)
        {
            foreach (var window in _brainDrainWindows)
                window.UpdateIntensity(intensity);
        }
        _lastAppliedBrainDrainIntensity = intensity;
    }

    private void BrainDrainPulseTick(object? sender, EventArgs e)
    {
        _brainDrainPulsePhase += 0.15;
        lock (_sync)
        {
            foreach (var window in _brainDrainWindows)
                window.UpdatePulsePhase(_brainDrainPulsePhase);
        }
    }

    private SpiralCache? LoadSpiralCache(string path)
    {
        if (_spiralCache != null && string.Equals(_spiralCache.Path, path, StringComparison.OrdinalIgnoreCase))
            return _spiralCache;

        _spiralCache?.Dispose();
        _spiralCache = null;

        try
        {
            if (!File.Exists(path)) return null;

            if (path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            {
                // Do not create the animator here and share it across spiral windows:
                // SKCodec is not thread-safe, and each window runs its own timer. Create
                // a private animator per window in StartSpiral instead.
                if (AvaloniaAnimatedGif.TryCreate(path) != null)
                {
                    _spiralCache = new SpiralCache(path, SpiralCacheKind.AnimatedGif, null, null, null, null);
                    return _spiralCache;
                }

                var bitmap = new Bitmap(path);
                _spiralCache = new SpiralCache(path, SpiralCacheKind.StaticImage, null, bitmap, null, null);
                return _spiralCache;
            }

            if (IsStaticImageExtension(path))
            {
                var bitmap = new Bitmap(path);
                _spiralCache = new SpiralCache(path, SpiralCacheKind.StaticImage, null, bitmap, null, null);
                return _spiralCache;
            }

            if (IsVideoExtension(path))
            {
                // Video spirals are created per-window in StartSpiral because a LibVLC player
                // cannot be shared across multiple transparent overlay windows.
                _spiralCache = new SpiralCache(path, SpiralCacheKind.Video, null, null, null);
                return _spiralCache;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AvaloniaOverlayService: failed to load spiral cache for {Path}", path);
            return null;
        }
    }

    private static bool IsStaticImageExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".webp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".bmp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVideoExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return string.Equals(ext, ".mp4", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".webm", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".mov", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".avi", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".mkv", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<ScreenInfo> GetScreens()
    {
        try
        {
            var all = _screens.GetAllScreens();
            if (all.Count == 0)
                return new[] { new ScreenInfo("fallback", new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), 1.0) };

            if (_settings.Current?.DualMonitorEnabled != true)
            {
                var primary = _screens.GetPrimaryScreen() ?? all[0];
                return new[] { primary };
            }
            return all;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("AvaloniaOverlayService: could not enumerate screens: {Error}", ex.Message);
            return new[] { new ScreenInfo("fallback", new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), 1.0) };
        }
    }

    private static void ApplyWindowStyles(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            // Make sure the platform handle exists before styling. If Show() has not
            // yet created it, register a one-shot Opened handler and try again.
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero)
            {
                window.Opened += OnWindowOpenedForStyling;
                return;
            }

            ApplyWindowStylesCore(window, hwnd);
        }
        catch { }
    }

    private static void OnWindowOpenedForStyling(object? sender, EventArgs e)
    {
        if (sender is not Window window) return;
        window.Opened -= OnWindowOpenedForStyling;
        ApplyWindowStyles(window);
    }

    private static void ApplyWindowStylesCore(Window window, IntPtr hwnd)
    {
        var exStyle = (uint)GetWindowLong(hwnd, GWL_EXSTYLE).ToInt64();
        // Match the WPF overlay service: layered + transparent + tool-window + no-activate.
        // WS_EX_LAYERED is required for per-pixel alpha; WS_EX_TRANSPARENT passes input
        // through to windows below.
        exStyle |= WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE;
        SetWindowLong(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
        _updateTimer.Tick -= UpdateOverlays;
        _brainDrainPulseTimer.Tick -= BrainDrainPulseTick;
        _brainDrainPulseTimer.Stop();
    }

    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLong64(IntPtr hWnd, int nIndex);
    private static IntPtr GetWindowLong(IntPtr hWnd, int nIndex)
        => IntPtr.Size == 4 ? GetWindowLong32(hWnd, nIndex) : GetWindowLong64(hWnd, nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLong64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    private static IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        => IntPtr.Size == 4 ? SetWindowLong32(hWnd, nIndex, dwNewLong) : SetWindowLong64(hWnd, nIndex, dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    private sealed class OverlayWindow : Window
    {
        private readonly Border _border;

        public OverlayWindow(ScreenInfo screen, Color color)
        {
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            CanResize = false;
            ShowActivated = false;
            Focusable = false;
            IsHitTestVisible = false;

            this.ConstrainToScreen(screen);
            Opacity = 1;

            _border = new Border
            {
                Background = new SolidColorBrush(color),
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch
            };

            Content = _border;
        }

        public void UpdateColor(Color color)
        {
            _border.Background = new SolidColorBrush(color);
        }
    }

    private sealed class BrainDrainOverlayWindow : Window
    {
        private readonly Border _border;
        private int _baseIntensity;

        public BrainDrainOverlayWindow(ScreenInfo screen, int intensity)
        {
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            CanResize = false;
            ShowActivated = false;
            Focusable = false;
            IsHitTestVisible = false;

            this.ConstrainToScreen(screen);
            Opacity = 1;

            _baseIntensity = intensity;
            _border = new Border
            {
                Background = CreateBrush(intensity, 0),
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch
            };

            Content = _border;
        }

        public void UpdateIntensity(int intensity)
        {
            _baseIntensity = intensity;
            _border.Background = CreateBrush(intensity, 0);
        }

        public void UpdatePulsePhase(double phase)
        {
            _border.Background = CreateBrush(_baseIntensity, phase);
        }

        private static IBrush CreateBrush(int intensity, double phase)
        {
            var baseAlpha = Math.Clamp(intensity / 100.0, 0, 1);
            var pulse = 0.85 + 0.15 * Math.Sin(phase); // subtle 85%-100% pulse
            var alpha = (byte)Math.Clamp(baseAlpha * pulse * 255, 0, 255);
            return new SolidColorBrush(new Color(alpha, 20, 0, 40)); // dark violet distortion
        }
    }

    private enum SpiralCacheKind
    {
        AnimatedGif,
        StaticImage,
        Video
    }

    private sealed class SpiralCache : IDisposable
    {
        public string Path { get; }
        public SpiralCacheKind Kind { get; }
        public AvaloniaAnimatedGif? Animation { get; }
        public Bitmap? StaticBitmap { get; }
        public AvaloniaInlineLoopVideo? Video { get; }

        public SpiralCache(string path, SpiralCacheKind kind, AvaloniaAnimatedGif? animation, Bitmap? staticBitmap, AvaloniaInlineLoopVideo? video, object? unused = null)
        {
            Path = path;
            Kind = kind;
            Animation = animation;
            StaticBitmap = staticBitmap;
            Video = video;
        }

        public void Dispose()
        {
            Animation?.Dispose();
            StaticBitmap?.Dispose();
            Video?.Dispose();
        }
    }

    private sealed class SpiralOverlayWindow : Window
    {
        private readonly Image? _image;
        private readonly AvaloniaAnimatedGif? _animation;
        private readonly AvaloniaInlineLoopVideo? _video;

        public SpiralOverlayWindow(ScreenInfo screen, SpiralCache cache, int opacityPercent, AvaloniaAnimatedGif? animator = null)
        {
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            CanResize = false;
            ShowActivated = false;
            Focusable = false;
            IsHitTestVisible = false;

            this.ConstrainToScreen(screen);
            Opacity = Math.Clamp(opacityPercent / 100.0, 0.0, 1.0);

            _animation = animator;
            _image = new Image
            {
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch,
                Stretch = Stretch.UniformToFill,
                Source = _animation?.Source ?? cache.StaticBitmap
            };

            if (_animation != null)
                _animation.FrameRendered += (_, _) => _image?.InvalidateVisual();

            Content = new Grid { Children = { _image } };

            Opened += OnOpened;
        }

        public SpiralOverlayWindow(ScreenInfo screen, LibVLC libVlc, string videoPath, int opacityPercent)
        {
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            CanResize = false;
            ShowActivated = false;
            Focusable = false;
            IsHitTestVisible = false;

            this.ConstrainToScreen(screen);
            Opacity = Math.Clamp(opacityPercent / 100.0, 0.0, 1.0);

            _video = new AvaloniaInlineLoopVideo(libVlc, videoPath, (uint)screen.Bounds.Width, (uint)screen.Bounds.Height);
            Content = new Grid { Children = { _video.Surface } };

            Opened += OnOpened;
        }

        private void OnOpened(object? sender, EventArgs e)
        {
            _animation?.Start();
            _video?.Resume();
        }

        public void UpdateOpacity(int opacityPercent)
        {
            Opacity = Math.Clamp(opacityPercent / 100.0, 0.0, 1.0);
        }

        protected override void OnClosed(EventArgs e)
        {
            _video?.Dispose();
            // Do not dispose the shared animation here; the service owns the cache.
            base.OnClosed(e);
        }
    }

    private sealed class SustainedOverlayState
    {
        public string Kind { get; }
        public DispatcherTimer? AutoHideTimer { get; set; }

        public SustainedOverlayState(string kind)
        {
            Kind = kind;
        }
    }
}
