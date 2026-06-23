using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Services.Overlays;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Subliminal;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Services.Subliminal;

/// <summary>
/// Avalonia implementation of the subliminal-message effect engine.
/// Shows brief, centered text flashes on topmost transparent full-screen windows,
/// driven by a scheduler and honoring the user's phrase pool, duration and opacity settings.
/// </summary>
public sealed class AvaloniaSubliminalService : ISubliminalService, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IScreenProvider _screens;
    private readonly IProgressionService _progression;
    private readonly ISessionService? _session;
    private readonly ILogger<AvaloniaSubliminalService>? _logger;
    private readonly Random _random = new();
    private readonly object _sync = new();
    private readonly Dictionary<string, SubliminalWindow> _screenWindows = new();

    private CancellationTokenSource? _cts;
    private DispatcherTimer? _scheduledTimer;
    private bool _disposed;

    public AvaloniaSubliminalService(
        ISettingsService settings,
        IScreenProvider screens,
        IProgressionService progression,
        ISessionService? session = null,
        ILogger<AvaloniaSubliminalService>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _screens = screens ?? throw new ArgumentNullException(nameof(screens));
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        _session = session;
        _logger = logger;
    }

    public bool IsRunning { get; private set; }

    public event EventHandler? SubliminalDisplayed;

    public void Start()
    {
        if (IsRunning) return;
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
        {
            _logger?.LogDebug("AvaloniaSubliminalService: overlays are not supported on mobile; Start is a no-op");
            return;
        }

        IsRunning = true;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        ScheduleNext();
        _logger?.LogInformation("AvaloniaSubliminalService started");
    }

    public void FlashSubliminalCustom(string text, int? overrideDurationMs = null, bool suppressHaptic = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        FlashSubliminalCustom(text, opacity: null, overrideDurationMs: overrideDurationMs);
    }

    public void SetEnabled(bool on)
    {
        var s = _settings.Current;
        if (s == null) return;

        if (s.SubliminalEnabled != on)
            s.SubliminalEnabled = on;

        if (_session?.State == SessionState.Running)
        {
            if (on && !IsRunning) Start();
            else if (!on && IsRunning) Stop();
        }

        _settings.Save();
        _logger?.LogInformation("AvaloniaSubliminalService: subliminals toggled: {Enabled}", on);
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;

        _scheduledTimer?.Stop();
        _scheduledTimer = null;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        lock (_sync)
        {
            foreach (var window in _screenWindows.Values)
            {
                try
                {
                    window.ActiveCts?.Cancel();
                    window.Opacity = 0;
                    window.Content = null;
                    window.Hide();
                }
                catch { }
            }
        }

        _logger?.LogInformation("AvaloniaSubliminalService stopped");
    }

    public void FlashSubliminal()
    {
        var settings = _settings.Current;
        if (settings == null) return;

        var pool = settings.SubliminalPool;
        var activeTexts = pool.Where(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();
        if (activeTexts.Count == 0)
        {
            _logger?.LogDebug("AvaloniaSubliminalService: no active subliminal texts");
            return;
        }

        var text = activeTexts[_random.Next(activeTexts.Count)];
        ShowSubliminalVisuals(text);
        _progression.AddXP(10, XPSource.Subliminal);
    }

    public void FlashSubliminalCustom(string text, int? opacity = null, int? overrideDurationMs = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        text = text.Trim();
        if (text.Length > 200) text = text.Substring(0, 200);
        text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]*>", "");
        ShowSubliminalVisuals(text, opacity, overrideDurationMs);
        _progression.AddXP(10, XPSource.Subliminal);
    }

    private void ShowSubliminalVisuals(string text, int? opacity = null, int? overrideDurationMs = null)
    {
        var settings = _settings.Current;
        if (settings == null) return;

        var durationMs = overrideDurationMs.HasValue
            ? Math.Max(100, overrideDurationMs.Value)
            : Math.Max(100, settings.SubliminalDuration * 17);
        var targetOpacity = (opacity ?? settings.SubliminalOpacity) / 100.0;

        var bgColor = ParseColor(settings.SubBackgroundColor, Colors.Black);
        var textColor = ParseColor(settings.SubTextColor, Colors.Magenta);
        var bgTransparent = settings.SubBackgroundTransparent;

        var screens = GetScreens(settings.DualMonitorEnabled);
        var stealsFocus = settings.SubliminalStealsFocus;

        foreach (var screen in screens)
        {
            var win = GetOrCreateScreenWindow(screen);
            win.BuildContent(text, bgColor, textColor, bgTransparent);
            if (!win.IsVisible) win.Show();
            ApplyWindowStyles(win, screen, stealsFocus);
            if (stealsFocus) { try { win.Activate(); } catch { } }
            AnimateWindow(win, targetOpacity, durationMs);
        }

        SubliminalDisplayed?.Invoke(this, EventArgs.Empty);
    }

    private void ScheduleNext()
    {
        if (!IsRunning) return;

        var settings = _settings.Current;
        if (settings == null || !settings.SubliminalEnabled) return;

        _scheduledTimer?.Stop();

        var freq = Math.Max(1, settings.SubliminalFrequency);
        var baseInterval = 60.0 / freq;
        var variance = baseInterval * 0.3;
        var interval = baseInterval + (_random.NextDouble() * variance * 2 - variance);
        interval = Math.Max(1, interval);

        _scheduledTimer = StartOneShotTimer(TimeSpan.FromSeconds(interval), () =>
        {
            if (!IsRunning) return;
            var s = _settings.Current;
            if (s == null || !s.SubliminalEnabled) return;

            try { FlashSubliminal(); }
            catch (Exception ex) { _logger?.LogWarning(ex, "AvaloniaSubliminalService: FlashSubliminal failed"); }
            ScheduleNext();
        });
    }

    private SubliminalWindow GetOrCreateScreenWindow(ScreenInfo screen)
    {
        var key = screen.Name;
        lock (_sync)
        {
            if (_screenWindows.TryGetValue(key, out var cached) && cached.IsLoaded)
                return cached;

            var win = new SubliminalWindow(screen);
            win.Show();
            OverlayZ.Register(win, OverlayZ.Layer.Subliminal);
            _screenWindows[key] = win;
            _logger?.LogDebug("AvaloniaSubliminalService: keep-alive window created for {Screen}", key);
            return win;
        }
    }

    private IReadOnlyList<ScreenInfo> GetScreens(bool dualMonitor)
    {
        try
        {
            var all = _screens.GetAllScreens();
            if (all.Count == 0)
                return new[] { new ScreenInfo("fallback", new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), 1.0) };

            if (!dualMonitor)
            {
                var primary = _screens.GetPrimaryScreen() ?? all[0];
                return new[] { primary };
            }
            return all;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("AvaloniaSubliminalService: could not enumerate screens: {Error}", ex.Message);
            return new[] { new ScreenInfo("fallback", new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), 1.0) };
        }
    }

    private static void AnimateWindow(SubliminalWindow window, double targetOpacity, int holdMs)
    {
        window.ActiveCts?.Cancel();
        window.ActiveCts?.Dispose();
        window.ActiveCts = new CancellationTokenSource();
        var token = window.ActiveCts.Token;
        _ = AnimateAsync(window, targetOpacity, holdMs, token);
    }

    private static async Task AnimateAsync(SubliminalWindow window, double targetOpacity, int holdMs, CancellationToken token)
    {
        try
        {
            await FadeAsync(window, 0, targetOpacity, 50, token);
            await Task.Delay(holdMs, token);
            await FadeAsync(window, targetOpacity, 0, 50, token);

            if (!token.IsCancellationRequested)
            {
                // Keep the window shown (transparent, blank content) between flashes.
                // Hiding a transparent layered window preserves its last layered bitmap,
                // so the next Show() can re-present the previous phrase for a frame —
                // the "previous-then-next" double flash. Staying shown keeps the surface live.
                window.Opacity = 0;
                window.Content = null;
            }
        }
        catch (OperationCanceledException)
        {
            // A newer show took over; do not blank the window.
        }
    }

    private static async Task FadeAsync(Window window, double from, double to, int durationMs, CancellationToken token)
    {
        var steps = Math.Max(1, durationMs / 16);
        for (int i = 0; i <= steps; i++)
        {
            token.ThrowIfCancellationRequested();
            window.Opacity = from + (to - from) * i / steps;
            if (i < steps)
                await Task.Delay(16, token);
        }
    }

    private static void ApplyWindowStyles(SubliminalWindow window, ScreenInfo screen, bool stealsFocus)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero) return;

            var exStyle = (uint)GetWindowLong(hwnd, GWL_EXSTYLE).ToInt64();
            exStyle |= WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_LAYERED;
            if (!stealsFocus) exStyle |= WS_EX_NOACTIVATE;
            else exStyle &= ~WS_EX_NOACTIVATE;
            SetWindowLong(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));

            // Keep z-order untouched (SWP_NOZORDER) — relative depth is owned by OverlayZ.
            SetWindowPos(hwnd, IntPtr.Zero,
                (int)screen.Bounds.X, (int)screen.Bounds.Y, (int)screen.Bounds.Width, (int)screen.Bounds.Height,
                SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOZORDER);
        }
        catch { }
    }

    private static Color ParseColor(string? hex, Color fallback)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return Color.Parse(hex);
        }
        catch
        {
            return fallback;
        }
    }

    private static DispatcherTimer StartOneShotTimer(TimeSpan dueTime, Action callback)
    {
        var timer = new DispatcherTimer { Interval = dueTime };
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            timer.Stop();
            timer.Tick -= handler;
            callback();
        };
        timer.Tick += handler;
        timer.Start();
        return timer;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        lock (_sync)
        {
            foreach (var window in _screenWindows.Values)
            {
                try { window.Close(); } catch { }
            }
            _screenWindows.Clear();
        }
    }

    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOZORDER = 0x0004;

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

    private sealed class SubliminalWindow : Window
    {
        public CancellationTokenSource? ActiveCts;

        public SubliminalWindow(ScreenInfo screen)
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
            Opacity = 0;
        }

        public void BuildContent(string text, Color bgColor, Color textColor, bool bgTransparent)
        {
            var grid = new Grid
            {
                Background = bgTransparent ? Brushes.Transparent : new SolidColorBrush(bgColor),
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch
            };

            var textBlock = new TextBlock
            {
                Text = text,
                FontSize = 120,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(textColor),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
            };

            grid.Children.Add(textBlock);
            Content = grid;
        }
    }
}
