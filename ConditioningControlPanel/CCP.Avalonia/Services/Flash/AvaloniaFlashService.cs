using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Services.Flash;

/// <summary>
/// Avalonia implementation of the flash-image effect engine.
/// Spawns topmost transparent overlay windows at a configurable frequency, loads
/// images from the user's assets folder, supports click-to-close, and implements
/// the hydra multiplication mode from the WPF engine.
/// </summary>
public sealed class AvaloniaFlashService : IFlashService, IDisposable
{
    private const double FADE_PER_SEC = 2.4;
    private const int MAX_CONCURRENT_FLASH = 10;
    private const int CACHE_EXPIRY_SECONDS = 60;
    private static readonly string[] IMAGE_EXTENSIONS = { ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".heic", ".avif", ".ico" };

    private readonly ISettingsService _settings;
    private readonly IAppEnvironment _environment;
    private readonly IScreenProvider _screens;
    private readonly IScheduler _scheduler;
    private readonly IUiDispatcher _dispatcher;
    private readonly IAchievementService _achievements;
    private readonly IProgressionService _progression;
    private readonly IAppLogger? _logger;
    private readonly Random _random = new();
    private readonly object _sync = new();
    private readonly List<FlashOverlayWindow> _activeWindows = new();
    private readonly Dictionary<string, (List<string> files, DateTime lastScan)> _fileCache = new();

    private string _imagesPath = "";
    private CancellationTokenSource? _cts;
    private IDisposable? _scheduledTimer;
    private bool _isBusy;
    private bool _noImagesWarningShown;

    public AvaloniaFlashService(
        ISettingsService settings,
        IAppEnvironment environment,
        IScreenProvider screens,
        IScheduler scheduler,
        IUiDispatcher dispatcher,
        IAchievementService achievements,
        IProgressionService progression,
        IAppLogger? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _screens = screens ?? throw new ArgumentNullException(nameof(screens));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _achievements = achievements ?? throw new ArgumentNullException(nameof(achievements));
        _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        _logger = logger;

        RefreshImagesPath();
    }

    public bool IsRunning { get; private set; }

    public event EventHandler? FlashAboutToDisplay;
    public event EventHandler? FlashDisplayed;
    public event EventHandler? FlashClicked;

    public void Start()
    {
        if (IsRunning) return;
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
        {
            _logger?.Debug("AvaloniaFlashService: overlays are not supported on mobile; Start is a no-op");
            return;
        }

        IsRunning = true;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        RefreshImagesPath();
        ScheduleNextFlash();
        _logger?.Information("AvaloniaFlashService started, images path: {Path}", _imagesPath);
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;

        _scheduledTimer?.Dispose();
        _scheduledTimer = null;

        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
        _cts?.Dispose();
        _cts = null;

        CloseAllWindows();
        _logger?.Information("AvaloniaFlashService stopped");
    }

    public void RefreshSchedule()
    {
        if (!IsRunning) return;
        ScheduleNextFlash();
    }

    public void RefreshImagesPath()
    {
        _imagesPath = Path.Combine(_environment.EffectiveAssetsPath, "images");
        try { Directory.CreateDirectory(_imagesPath); } catch { /* best effort */ }
        ClearFileCache();
        _logger?.Information("AvaloniaFlashService: images path refreshed to {Path}", _imagesPath);
    }

    public void ClearFileCache()
    {
        lock (_fileCache) { _fileCache.Clear(); }
    }

    public void LoadAssets()
    {
        ClearFileCache();
        _logger?.Information("AvaloniaFlashService: assets reloaded");
    }

    public void TriggerFlash()
    {
        if (!IsRunning || _isBusy) return;
        _isBusy = true;
        _ = Task.Run(LoadAndShowImages);
    }

    private void ScheduleNextFlash()
    {
        if (!IsRunning) return;

        var settings = _settings.Current;
        if (settings == null || !settings.FlashEnabled)
        {
            _logger?.Debug("AvaloniaFlashService: flashes disabled or settings unavailable");
            return;
        }

        _scheduledTimer?.Dispose();

        var baseFreq = Math.Max(1, settings.FlashFrequency);
        var baseInterval = 3600.0 / baseFreq;
        var variance = baseInterval * 0.3;
        var interval = baseInterval + (_random.NextDouble() * variance * 2 - variance);
        interval = Math.Max(3, interval);

        _scheduledTimer = _scheduler.StartOneShotTimer(TimeSpan.FromSeconds(interval), () =>
        {
            if (IsRunning && !_isBusy)
            {
                TriggerFlash();
            }
            ScheduleNextFlash();
        });
    }

    private async Task LoadAndShowImages()
    {
        try
        {
            var settings = _settings.Current;
            if (settings == null) { _isBusy = false; return; }

            var imagePaths = GetNextImages(settings.SimultaneousImages);
            if (imagePaths.Count == 0)
            {
                if (!_noImagesWarningShown)
                {
                    _logger?.Warning("AvaloniaFlashService: no images found in {Path}", _imagesPath);
                    _noImagesWarningShown = true;
                }
                _isBusy = false;
                return;
            }

            _logger?.Information("AvaloniaFlashService: displaying {Count} flash image(s)", imagePaths.Count);
            FlashAboutToDisplay?.Invoke(this, EventArgs.Empty);

            await Task.Delay(1000, _cts?.Token ?? default);

            var scale = settings.ImageScale / 100.0;
            var tasks = imagePaths.Select(p => LoadImageAsync(p)).ToArray();
            var results = await Task.WhenAll(tasks);
            var loaded = results.Where(r => r != null).Cast<LoadedImageData>().ToList();

            if (loaded.Count == 0) { _isBusy = false; return; }

            _dispatcher.Invoke(() => ShowImages(loaded, false, 0));
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "AvaloniaFlashService: error loading flash images");
            _isBusy = false;
        }
    }

    private async Task<LoadedImageData?> LoadImageAsync(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return await Task.Run(() =>
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    var bitmap = new Bitmap(stream);
                    return new LoadedImageData
                    {
                        FilePath = path,
                        Bitmap = bitmap,
                        Width = bitmap.PixelSize.Width,
                        Height = bitmap.PixelSize.Height
                    };
                }
                catch (Exception ex)
                {
                    _logger?.Debug("AvaloniaFlashService: could not load image {Path}: {Error}", path, ex.Message);
                    return null;
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.Debug("AvaloniaFlashService: could not load image {Path}: {Error}", path, ex.Message);
            return null;
        }
    }

    private void ShowImages(List<LoadedImageData> images, bool isMultiplication, int hydraGeneration)
    {
        if (!IsRunning) { if (!isMultiplication) _isBusy = false; return; }

        var settings = _settings.Current;
        if (settings == null) { if (!isMultiplication) _isBusy = false; return; }

        var lifetimeMs = settings.FlashDuration * 1000 + 1000;
        var maxOpacity = Math.Clamp(settings.FlashOpacity, 10, 100) / 100.0;

        for (int i = 0; i < images.Count; i++)
        {
            var data = images[i];
            SpawnFlashWindow(data, settings, lifetimeMs, hydraGeneration, maxOpacity);
        }

        FlashDisplayed?.Invoke(this, EventArgs.Empty);
        _achievements.TrackFlashImage();
        if (!isMultiplication)
        {
            _progression.AddXP(4, XPSource.Flash);
            _isBusy = false;
        }
    }

    private void SpawnFlashWindow(LoadedImageData data, AppSettings settings, int lifetimeMs, int hydraGeneration, double maxOpacity)
    {
        if (!IsRunning) return;

        lock (_sync)
        {
            if (_activeWindows.Count >= MAX_CONCURRENT_FLASH) return;
        }

        var geom = data.Geometry;
        var monitor = data.Monitor;

        // Avoid overlap with existing windows
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (!IsOverlapping(geom.X, geom.Y, geom.Width, geom.Height)) break;
            geom = new ImageGeometry
            {
                X = monitor.X + _random.Next(0, Math.Max(1, monitor.Width - geom.Width)),
                Y = monitor.Y + _random.Next(0, Math.Max(1, monitor.Height - geom.Height)),
                Width = geom.Width,
                Height = geom.Height
            };
        }

        var window = new FlashOverlayWindow(
            geom.X, geom.Y, geom.Width, geom.Height,
            data.Bitmap, settings.FlashClickable, lifetimeMs, hydraGeneration, monitor,
            maxOpacity, OnFlashClicked);

        try
        {
            window.Show();
            ApplyWindowStyles(window, settings.FlashClickable);
            ForceTopmost(window);

            lock (_sync) { _activeWindows.Add(window); }
        }
        catch (Exception ex)
        {
            _logger?.Debug("AvaloniaFlashService: failed to show flash window: {Error}", ex.Message);
            window.Close();
        }
    }

    private void OnFlashClicked(FlashOverlayWindow window)
    {
        if (!IsRunning) return;

        var settings = _settings.Current;
        if (settings == null) return;

        lock (_sync)
        {
            if (!_activeWindows.Contains(window)) return;
            _activeWindows.Remove(window);
        }

        window.Close();
        FlashClicked?.Invoke(this, EventArgs.Empty);

        if (settings.CorruptionMode)
        {
            var maxHydra = Math.Min(settings.HydraLimit, 20);
            int currentCount;
            lock (_sync) { currentCount = _activeWindows.Count; }

            if (currentCount + 1 < maxHydra)
            {
                var remainingMs = Math.Max(1000, (int)(window.ExpiresAt - DateTime.Now).TotalMilliseconds);
                TriggerMultiplication(maxHydra, currentCount, window.OriginalLifetimeMs, remainingMs, window.HydraGeneration, window.Monitor);
            }
        }
    }

    private async void TriggerMultiplication(int maxHydra, int currentCount, int parentLifetimeMs, int parentRemainingMs, int parentGeneration, MonitorInfo parentMonitor)
    {
        try
        {
            if (!IsRunning) return;
            var spaceAvailable = maxHydra - currentCount;
            var numToSpawn = Math.Min(2, spaceAvailable);
            if (numToSpawn <= 0) return;

            var settings = _settings.Current;
            if (settings == null) return;

            var imagePaths = GetNextImages(numToSpawn);
            if (imagePaths.Count == 0) return;

            var scale = settings.ImageScale / 100.0;
            var hydraLifetimeMs = settings.HydraLinkedTiming ? parentRemainingMs : parentLifetimeMs;
            var childGeneration = parentGeneration + 1;

            var tasks = imagePaths.Select(p => LoadImageAsync(p)).ToArray();
            var results = await Task.WhenAll(tasks);
            var loaded = new List<LoadedImageData>();
            foreach (var result in results)
            {
                if (result == null) continue;
                var monitor = PickMonitor(settings, parentMonitor);
                var geometry = CalculateGeometry(result.Width, result.Height, monitor, scale);
                result.Geometry = geometry;
                result.Monitor = monitor;
                loaded.Add(result);
            }

            if (loaded.Count > 0)
            {
                _dispatcher.Invoke(() => ShowImages(loaded, true, childGeneration));
            }
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "AvaloniaFlashService: TriggerMultiplication failed");
        }
    }

    private void CloseAllWindows()
    {
        List<FlashOverlayWindow> copy;
        lock (_sync)
        {
            copy = _activeWindows.ToList();
            _activeWindows.Clear();
        }
        foreach (var window in copy)
        {
            try { window.Close(); } catch { }
        }
    }

    private List<string> GetNextImages(int count)
    {
        lock (_fileCache)
        {
            var files = GetMediaFiles(_imagesPath, IMAGE_EXTENSIONS);
            if (files.Count == 0) return new List<string>();
            var result = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                result.Add(files[_random.Next(files.Count)]);
            }
            return result;
        }
    }

    private List<string> GetMediaFiles(string folder, string[] extensions)
    {
        if (!Directory.Exists(folder)) return new List<string>();

        var cacheKey = $"{folder}|{string.Join(",", extensions)}";
        lock (_fileCache)
        {
            if (_fileCache.TryGetValue(cacheKey, out var cached))
            {
                var age = (DateTime.UtcNow - cached.lastScan).TotalSeconds;
                if (age < CACHE_EXPIRY_SECONDS)
                    return new List<string>(cached.files);
            }
        }

        var files = new List<string>();
        foreach (var ext in extensions)
        {
            try
            {
                foreach (var file in Directory.GetFiles(folder, $"*{ext}", SearchOption.AllDirectories))
                {
                    if (IsPathSafe(file, _environment.EffectiveAssetsPath) || IsPathSafe(file, _environment.UserDataPath))
                        files.Add(file);
                }
            }
            catch { /* ignore unreadable directories */ }
        }

        if (_settings.Current?.DisabledAssetPaths.Count > 0)
        {
            var basePath = _environment.EffectiveAssetsPath;
            static string Norm(string p) => p.Replace('\\', '/');
            var disabled = new HashSet<string>(_settings.Current.DisabledAssetPaths.Select(Norm), StringComparer.OrdinalIgnoreCase);
            files = files.Where(f =>
            {
                var relative = Norm(Path.GetRelativePath(basePath, f));
                return !disabled.Contains(relative);
            }).ToList();
        }

        lock (_fileCache)
        {
            _fileCache[cacheKey] = (new List<string>(files), DateTime.UtcNow);
        }
        return files;
    }

    private static bool IsPathSafe(string path, string allowedBasePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var fullPath = Path.GetFullPath(path);
            var basePath = Path.GetFullPath(allowedBasePath);
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()) && !basePath.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                basePath += Path.DirectorySeparatorChar;
            return fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private MonitorInfo PickMonitor(AppSettings settings, MonitorInfo? preferred = null)
    {
        var candidates = GetMonitors(settings.DualMonitorEnabled);
        if (preferred != null)
        {
            foreach (var m in candidates)
            {
                if (m.X == preferred.X && m.Y == preferred.Y && m.Width == preferred.Width && m.Height == preferred.Height)
                    return m;
            }
        }
        return candidates[_random.Next(candidates.Count)];
    }

    private List<MonitorInfo> GetMonitors(bool dualMonitor)
    {
        var monitors = new List<MonitorInfo>();
        try
        {
            var primary = _screens.GetPrimaryScreen();
            foreach (var screen in _screens.GetAllScreens())
            {
                monitors.Add(new MonitorInfo
                {
                    X = (int)screen.Bounds.X,
                    Y = (int)screen.Bounds.Y,
                    Width = (int)screen.Bounds.Width,
                    Height = (int)screen.Bounds.Height,
                    IsPrimary = screen == primary
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.Debug("AvaloniaFlashService: could not enumerate monitors: {Error}", ex.Message);
        }

        if (monitors.Count == 0)
        {
            monitors.Add(new MonitorInfo { X = 0, Y = 0, Width = 1920, Height = 1080, IsPrimary = true });
        }

        if (!dualMonitor)
        {
            var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
            return new List<MonitorInfo> { primary };
        }
        return monitors;
    }

    private ImageGeometry CalculateGeometry(int origWidth, int origHeight, MonitorInfo monitor, double scale)
    {
        var baseWidth = monitor.Width * 0.4;
        var baseHeight = monitor.Height * 0.4;
        var ratio = Math.Min(baseWidth / origWidth, baseHeight / origHeight) * scale;
        var targetWidth = Math.Max(50, (int)(origWidth * ratio));
        var targetHeight = Math.Max(50, (int)(origHeight * ratio));

        const int edgePadding = 50;
        var minX = edgePadding;
        var minY = edgePadding;
        var maxX = Math.Max(minX + 1, monitor.Width - targetWidth - edgePadding);
        var maxY = Math.Max(minY + 1, monitor.Height - targetHeight - edgePadding);

        return new ImageGeometry
        {
            X = monitor.X + _random.Next(minX, maxX),
            Y = monitor.Y + _random.Next(minY, maxY),
            Width = targetWidth,
            Height = targetHeight
        };
    }

    private bool IsOverlapping(int x, int y, int w, int h)
    {
        lock (_sync)
        {
            foreach (var window in _activeWindows)
            {
                try
                {
                    var wx = window.Position.X;
                    var wy = window.Position.Y;
                    var ww = (int)window.Width;
                    var wh = (int)window.Height;
                    var dx = Math.Min(x + w, wx + ww) - Math.Max(x, wx);
                    var dy = Math.Min(y + h, wy + wh) - Math.Max(y, wy);
                    if (dx >= 0 && dy >= 0)
                    {
                        var overlapArea = dx * dy;
                        var windowArea = w * h;
                        if (overlapArea > windowArea * 0.3) return true;
                    }
                }
                catch { }
            }
        }
        return false;
    }

    private static void ApplyWindowStyles(Window window, bool clickable)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero) return;

            var exStyle = (uint)GetWindowLong(hwnd, GWL_EXSTYLE).ToInt64();
            exStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            if (!clickable) exStyle |= WS_EX_TRANSPARENT;
            SetWindowLong(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
        }
        catch
        {
            // best-effort styling
        }
    }

    private static void ForceTopmost(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero) return;
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
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

    private sealed class LoadedImageData
    {
        public string FilePath { get; set; } = "";
        public Bitmap Bitmap { get; set; } = null!;
        public int Width { get; set; }
        public int Height { get; set; }
        public ImageGeometry Geometry { get; set; } = new();
        public MonitorInfo Monitor { get; set; } = new();
    }

    private sealed class ImageGeometry
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    private sealed class MonitorInfo
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsPrimary { get; set; }
    }

    private sealed class FlashOverlayWindow : Window
    {
        private readonly Action<FlashOverlayWindow> _onClick;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
        private readonly DateTime _expiresAt;
        private readonly double _maxOpacity;
        private bool _isFadingOut;
        private bool _closed;

        public bool IsClickable { get; }
        public int OriginalLifetimeMs { get; }
        public int HydraGeneration { get; }
        public MonitorInfo Monitor { get; }
        public DateTime ExpiresAt => _expiresAt;

        public FlashOverlayWindow(
            int x, int y, int width, int height,
            Bitmap bitmap, bool clickable, int lifetimeMs, int hydraGeneration, MonitorInfo monitor,
            double maxOpacity, Action<FlashOverlayWindow> onClick)
        {
            _onClick = onClick;
            IsClickable = clickable;
            OriginalLifetimeMs = lifetimeMs;
            HydraGeneration = hydraGeneration;
            Monitor = monitor;
            _expiresAt = DateTime.Now.AddMilliseconds(lifetimeMs);
            _maxOpacity = maxOpacity;

            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            CanResize = false;
            ShowActivated = false;

            Position = new PixelPoint(x, y);
            Width = width;
            Height = height;
            Opacity = 0;

            var image = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                Width = width,
                Height = height
            };

            if (clickable)
            {
                image.Cursor = new Cursor(StandardCursorType.Hand);
                image.PointerPressed += OnPointerPressed;
            }

            Content = image;

            _timer.Tick += OnTick;
            _timer.Start();
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!IsClickable) return;
            var props = e.GetCurrentPoint((Visual?)sender!).Properties;
            if (props.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
            e.Handled = true;
            _onClick(this);
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (_closed) return;
            const double dt = 0.033;
            var show = DateTime.Now < _expiresAt && !_isFadingOut;
            var target = show ? _maxOpacity : 0.0;
            var step = FADE_PER_SEC * dt;

            if (Opacity < target)
            {
                Opacity = Math.Min(target, Opacity + step);
            }
            else if (Opacity > target)
            {
                Opacity = Math.Max(0.0, Opacity - step);
                if (Opacity <= 0)
                {
                    CloseInternal();
                }
            }
        }

        public void BeginFadeOut() => _isFadingOut = true;

        private void CloseInternal()
        {
            if (_closed) return;
            _closed = true;
            _timer.Stop();
            _timer.Tick -= OnTick;
            try { Close(); } catch { }
        }
    }
}
