using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;
using LibVLCSharp.Shared;
using BubbleCountService = ConditioningControlPanel.Avalonia.Services.BubbleCountService;

using IBubbleCountService = ConditioningControlPanel.IBubbleCountService;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the bubble-count challenge window.
///
/// Watch a video, count the bubbles that appear, then enter the total.
/// Multi-monitor support mirrors the WPF implementation: a LibVLC-backed
/// VideoView on each screen, separate topmost bubble windows, a shared
/// animation timer, and mod-aware bubble images.
/// </summary>
public partial class BubbleCountWindow : Window
{
    private readonly ILogger<BubbleCountWindow>? _logger;

    private readonly string _videoPath = string.Empty;
    private readonly BubbleCountService.Difficulty _difficulty;
    private readonly bool _strictMode;
    private readonly Action<bool> _onComplete = _ => { };
    private readonly ScreenInfo? _screen;
    private readonly bool _isPrimary;

    private readonly LibVLC? _libVLC;
    private readonly MediaPlayer? _mediaPlayer;
    private DispatcherTimer? _safetyTimer;
    private DispatcherTimer? _bubbleSpawnTimer;
    private DispatcherTimer? _bubbleAnimTimer;

    private readonly Random _random = new();
    private readonly List<CountBubble> _activeBubbles = new();
    private Bitmap? _bubbleImage;

    private int _targetBubbleCount;
    private int _bubbleCount;
    private double _videoDurationSeconds = 30;
    private bool _videoEnded;
    private bool _gameCompleted;

    private const double BubbleAnimTickMs = 30;

    private static readonly object _cleanupLock = new();
    private static bool _isCleaningUp;
    private static readonly List<BubbleCountWindow> _allWindows = new();
    private static int _sharedBubbleCount;
    private static int _sharedTargetCount;

    /// <summary>Duration of the last played video in seconds (shared for XP scaling).</summary>
    internal static double LastVideoDurationSeconds { get; private set; } = 30;

    private readonly IBubbleCountService _bubbleCountService;

    public BubbleCountWindow()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<BubbleCountWindow>>();
        _bubbleCountService = App.Services.GetRequiredService<IBubbleCountService>();
    }

    public BubbleCountWindow(string videoPath, BubbleCountService.Difficulty difficulty,
        bool strictMode, Action<bool> onComplete,
        ScreenInfo? screen = null, bool isPrimary = true)
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<BubbleCountWindow>>();
        _bubbleCountService = App.Services.GetRequiredService<IBubbleCountService>();
        _videoPath = videoPath;
        _difficulty = difficulty;
        _strictMode = strictMode;
        _onComplete = onComplete;
        _screen = screen;
        _isPrimary = isPrimary;

        LoadBubbleImage();

        TxtDifficulty.Text = $" ({difficulty})";
        if (_strictMode)
        {
            TxtStrict.IsVisible = true;
            TxtEscHint.IsVisible = false;
        }

        try
        {
            _libVLC = App.Services?.GetService<LibVLC>();
            if (_libVLC != null)
            {
                _mediaPlayer = new MediaPlayer(_libVLC);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BubbleCountWindow: failed to create LibVLC media player");
        }

        if (_mediaPlayer != null)
        {
            VideoView.MediaPlayer = _mediaPlayer;
            _mediaPlayer.EndReached += OnMediaEndReached;
            _mediaPlayer.EncounteredError += OnMediaError;
        }

        PositionWindow();
        KeyDown += Window_KeyDown;
        _allWindows.Add(this);

        Loaded += OnLoaded;
    }

    /// <summary>
    /// Show the bubble-count game on all monitors.
    /// </summary>
    public static void ShowOnAllMonitors(string videoPath, BubbleCountService.Difficulty difficulty,
        bool strictMode, Action<bool> onComplete)
    {
        var logger = App.Services.GetRequiredService<ILogger<BubbleCountWindow>>();
        lock (_cleanupLock)
        {
            _isCleaningUp = false;
        }

        _allWindows.Clear();
        _sharedBubbleCount = 0;
        _sharedTargetCount = 0;

        var provider = App.Services?.GetService<IScreenProvider>();
        var screens = provider?.GetAllScreens();

        if (screens == null || screens.Count == 0)
        {
            App.Services?.GetRequiredService<ILogger<BubbleCountWindow>>().LogError("BubbleCountWindow: no screens available");
            onComplete?.Invoke(false);
            return;
        }

        var settings = App.Services?.GetService<ISettingsService>()?.Current;
        var allScreens = screens.ToArray();
        var primary = allScreens[0];

        try
        {
            var primaryWindow = new BubbleCountWindow(videoPath, difficulty, strictMode, onComplete, primary, true);
            primaryWindow.Show();

            if (settings?.DualMonitorEnabled == true)
            {
                foreach (var screen in allScreens.Where(s => s != primary))
                {
                    var secondary = new BubbleCountWindow(videoPath, difficulty, strictMode, onComplete, screen, false);
                    secondary.Show();
                }
            }

            primaryWindow.Activate();
        }
        catch (Exception ex)
        {
            App.Services?.GetRequiredService<ILogger<BubbleCountWindow>>().LogError(ex, "BubbleCountWindow: failed to create/show windows");
            onComplete?.Invoke(false);
        }
    }

    /// <summary>
    /// Force close all bubble-count windows.
    /// </summary>
    public static void ForceCloseAll()
    {
        var logger = App.Services.GetRequiredService<ILogger<BubbleCountWindow>>();
        lock (_cleanupLock)
        {
            if (_isCleaningUp) return;
            _isCleaningUp = true;
        }

        var windowsCopy = _allWindows.ToList();
        _allWindows.Clear();

        foreach (var window in windowsCopy)
        {
            try
            {
                window._safetyTimer?.Stop();
                window._bubbleSpawnTimer?.Stop();
                window._bubbleAnimTimer?.Stop();
                window._mediaPlayer?.Stop();
                window.Close();
            }
            catch (Exception ex)
            {
                App.Services?.GetRequiredService<ILogger<BubbleCountWindow>>().LogInformation("BubbleCountWindow: error closing window - {Error}", ex.Message);
            }
        }

        try
        {
            App.Services!.GetRequiredService<IBubbleCountService>().ResetBusyState();
        }
        catch { /* legacy service may not be present */ }

        lock (_cleanupLock)
        {
            _isCleaningUp = false;
        }
    }

    /// <summary>
    /// Check if any bubble-count window is currently open.
    /// </summary>
    public static bool IsAnyOpen() => _allWindows.Count > 0;

    private void PositionWindow()
    {
        try
        {
            var screen = _screen ?? App.Services?.GetService<IScreenProvider>()?.GetPrimaryScreen();
            if (screen == null) return;

            Position = new PixelPoint((int)screen.Bounds.X, (int)screen.Bounds.Y);
            Width = screen.Bounds.Width / screen.Scaling;
            Height = screen.Bounds.Height / screen.Scaling;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BubbleCountWindow: failed to position window");
            WindowState = WindowState.Maximized;
        }
    }

    private void LoadBubbleImage()
    {
        try
        {
            _bubbleImage = AvaloniaBitmapHelper.LoadResource("bubble.png");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "BubbleCountWindow: failed to load bubble image");
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _logger?.LogInformation("BubbleCountWindow.OnLoaded: primary={IsPrimary}, video={Video}", _isPrimary, _videoPath);

        try
        {
            if (_mediaPlayer == null || _libVLC == null)
            {
                _logger?.LogError("BubbleCountWindow.OnLoaded: LibVLC not available");
                if (_isPrimary) CloseAllWindows(false);
                return;
            }

            if (!File.Exists(_videoPath))
            {
                _logger?.LogError("BubbleCountWindow.OnLoaded: video file not found: {Path}", _videoPath);
                if (_isPrimary) CloseAllWindows(false);
                return;
            }

            if (_isPrimary)
            {
                _videoDurationSeconds = GetVideoDuration(_videoPath);
                LastVideoDurationSeconds = _videoDurationSeconds;
                _targetBubbleCount = CalculateTargetBubbles();
                _sharedTargetCount = _targetBubbleCount;

                StartSafetyTimer(_videoDurationSeconds);
                StartBubbleSpawning();

                _logger?.LogInformation("BubbleCount game started - Target: {Target} bubbles, Duration: {Duration}s, Difficulty: {Diff}",
                    _targetBubbleCount, _videoDurationSeconds, _difficulty);

                var settings = App.Services?.GetService<ISettingsService>()?.Current;
                var volume = (int)((settings?.MasterVolume ?? 100) / 100.0 * 100);
                _mediaPlayer.Volume = Math.Clamp(volume, 0, 100);
            }
            else
            {
                _targetBubbleCount = _sharedTargetCount;
            }

            var media = new Media(_libVLC, _videoPath, FromType.FromPath);
            if (!_isPrimary)
            {
                media.AddOption(":no-audio");
            }

            _mediaPlayer.Play(media);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BubbleCountWindow.OnLoaded: failed to initialize game");
            if (_isPrimary) CloseAllWindows(false);
        }
    }

    private double GetVideoDuration(string path)
    {
        try
        {
            if (_libVLC == null) return 30;
            using var media = new Media(_libVLC, path, FromType.FromPath);
            media.Parse(MediaParseOptions.ParseLocal, 2000);
            if (media.Duration > 0)
            {
                return media.Duration / 1000.0;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "BubbleCountWindow: failed to parse video duration for {Path}", path);
        }
        return 30;
    }

    private void OnMediaEndReached(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_isCleaningUp && _isPrimary)
            {
                OnVideoEnded();
            }
        });
    }

    private void OnMediaError(object? sender, EventArgs e)
    {
        _logger?.LogError("BubbleCountWindow: media playback error");
        Dispatcher.UIThread.Post(() =>
        {
            if (_isPrimary)
            {
                OnVideoEnded();
            }
        });
    }

    private int CalculateTargetBubbles()
    {
        double baseRate = _difficulty switch
        {
            BubbleCountService.Difficulty.Easy => 3,
            BubbleCountService.Difficulty.Hard => 8,
            _ => 5,
        };

        var scaledCount = (baseRate / 30.0) * _videoDurationSeconds;
        var variance = scaledCount * 0.2;
        var count = (int)Math.Round(scaledCount + (_random.NextDouble() * variance * 2 - variance));
        return Math.Max(3, count);
    }

    private void StartSafetyTimer(double videoDurationSeconds)
    {
        _safetyTimer?.Stop();
        var timeoutSeconds = videoDurationSeconds + 5;

        _safetyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(timeoutSeconds) };
        _safetyTimer.Tick += (_, _) =>
        {
            _safetyTimer?.Stop();
            if (!_videoEnded && !_isCleaningUp)
            {
                _logger?.LogWarning("BubbleCountWindow: safety timeout - forcing video end");
                OnVideoEnded();
            }
        };
        _safetyTimer.Start();
    }

    private void StartBubbleSpawning()
    {
        if (!_isPrimary) return;

        var intervalMs = (_videoDurationSeconds * 1000) / Math.Max(1, _targetBubbleCount);

        _bubbleSpawnTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(intervalMs * 0.7)
        };

        _bubbleSpawnTimer.Tick += (_, _) =>
        {
            if (_sharedBubbleCount < _targetBubbleCount && !_videoEnded && !_isCleaningUp)
            {
                if (_random.NextDouble() < 0.7 || _sharedBubbleCount < _targetBubbleCount / 2)
                {
                    SpawnBubbleOnAllWindows();
                }
            }
        };

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1500);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_videoEnded || _isCleaningUp) return;
                    _bubbleSpawnTimer?.Start();
                    SpawnBubbleOnAllWindows();
                });
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("BubbleCount: failed to start spawning - {Error}", ex.Message);
            }
        });
    }

    private void SpawnBubbleOnAllWindows()
    {
        if (_sharedBubbleCount >= _targetBubbleCount) return;
        _sharedBubbleCount++;
        _bubbleCount = _sharedBubbleCount;

        var relX = _random.NextDouble() * 0.7 + 0.15;
        var relY = _random.NextDouble() * 0.5 + 0.25;
        var size = _random.Next(120, 225);

        var windows = _allWindows.ToList();
        if (windows.Count > 0)
        {
            var randomWindow = windows[_random.Next(windows.Count)];
            randomWindow.SpawnBubbleAt(relX, relY, size);
        }
    }

    private void SpawnBubbleAt(double relX, double relY, int size)
    {
        try
        {
            var screen = _screen ?? App.Services?.GetService<IScreenProvider>()?.GetPrimaryScreen();
            if (screen == null) return;

            var area = screen.WorkingArea;
            var screenX = area.X + (relX * area.Width) - (size / 2.0 * screen.Scaling);
            var screenY = area.Y + (relY * area.Height) - (size / 2.0 * screen.Scaling);

            var bubble = new CountBubble(_bubbleImage, size, screenX, screenY, _random, PlayPopSound);
            _activeBubbles.Add(bubble);
            EnsureBubbleAnimTimer();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("BubbleCountWindow: failed to spawn bubble - {Error}", ex.Message);
        }
    }

    private void EnsureBubbleAnimTimer()
    {
        if (_bubbleAnimTimer != null) return;
        _bubbleAnimTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(BubbleAnimTickMs)
        };
        _bubbleAnimTimer.Tick += AnimateAllCountBubbles;
        _bubbleAnimTimer.Start();
    }

    private void AnimateAllCountBubbles(object? sender, EventArgs e)
    {
        for (int i = _activeBubbles.Count - 1; i >= 0; i--)
        {
            if (i >= _activeBubbles.Count) continue;
            var bubble = _activeBubbles[i];
            bubble.Tick(BubbleAnimTickMs);
            if (bubble.IsFinished)
            {
                _activeBubbles.RemoveAt(i);
                bubble.Dispose();
            }
        }

        if (_activeBubbles.Count == 0)
        {
            _bubbleAnimTimer?.Stop();
            _bubbleAnimTimer = null;
        }
    }

    private void PlayPopSound()
    {
        try
        {
            var settings = App.Services?.GetService<ISettingsService>()?.Current;
            var masterVolume = (settings?.MasterVolume ?? 100) / 100.0;
            var bubblesVolume = (settings?.BubblesVolume ?? 50) / 100.0;
            var volume = (float)Math.Pow(masterVolume * bubblesVolume, 1.5);
            App.Services?.GetService<ISfxPlayer>()?.Play("pop", volume);
        }
        catch
        {
            // Best-effort pop sound.
        }
    }

    private void OnVideoEnded()
    {
        if (_videoEnded || _isCleaningUp) return;
        _videoEnded = true;

        _safetyTimer?.Stop();
        _bubbleSpawnTimer?.Stop();

        foreach (var window in _allWindows.ToList())
        {
            window._videoEnded = true;
            window._bubbleSpawnTimer?.Stop();
        }

        foreach (var window in _allWindows.ToList())
        {
            foreach (var bubble in window._activeBubbles.ToArray())
            {
                bubble.Dispose();
            }
            window._activeBubbles.Clear();
            window._bubbleAnimTimer?.Stop();
            window._bubbleAnimTimer = null;
        }

        if (_isPrimary && _videoDurationSeconds > 0)
        {
            _logger?.LogInformation("BubbleCount video watched: {Duration}s", _videoDurationSeconds);
            try
            {
                App.Services?.GetService<IAchievementService>()?.TrackVideoWatched(_videoDurationSeconds);
            }
            catch { /* achievement service may not be present */ }
        }

        if (_isPrimary)
        {
            ShowResultWindow();
        }
    }

    private void ShowResultWindow()
    {
        foreach (var window in _allWindows.ToList())
        {
            try { window.Hide(); } catch { }
        }

        BubbleCountResultWindow.ShowOnAllMonitors(
            _sharedBubbleCount,
            _strictMode,
            success =>
            {
                _gameCompleted = true;
                CloseAllWindows(success);
            });
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_strictMode && !_gameCompleted && !_isCleaningUp)
        {
            _gameCompleted = true;
            CloseAllWindows(false);
        }
    }

    private void CloseAllWindows(bool success)
    {
        lock (_cleanupLock)
        {
            if (_isCleaningUp) return;
            _isCleaningUp = true;
        }

        try
        {
            foreach (var window in _allWindows.ToList())
            {
                try
                {
                    window._safetyTimer?.Stop();
                    window._bubbleSpawnTimer?.Stop();
                    window._bubbleAnimTimer?.Stop();
                    window._mediaPlayer?.Stop();
                    window.Close();
                }
                catch { }
            }

            _allWindows.Clear();
            _onComplete?.Invoke(success);
        }
        finally
        {
            lock (_cleanupLock)
            {
                _isCleaningUp = false;
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _safetyTimer?.Stop();
        _bubbleSpawnTimer?.Stop();
        _bubbleAnimTimer?.Stop();
        _bubbleAnimTimer = null;

        foreach (var bubble in _activeBubbles.ToArray())
        {
            bubble.Dispose();
        }
        _activeBubbles.Clear();

        _mediaPlayer?.Stop();
        _mediaPlayer?.Dispose();
        _allWindows.Remove(this);
        base.OnClosed(e);
    }

    /// <summary>
    /// Individual topmost bubble window for the counting game.
    /// </summary>
    private sealed class CountBubble : IDisposable
    {
        private readonly Window _window;
        private readonly Control _visual;
        private readonly Action? _playSound;

        private double _scale = 0.1;
        private double _targetScale = 1.0;
        private double _opacity = 1.0;
        private double _rotation = 0;
        private bool _isPopping;
        private bool _isDisposed;
        private double _lifeRemainingMs;
        private readonly int _size;

        public bool IsFinished { get; private set; }

        public CountBubble(Bitmap? image, int size, double screenX, double screenY,
            Random random, Action? playSound)
        {
            _playSound = playSound;
            _rotation = random.Next(360);
            _size = size;
            _lifeRemainingMs = 1000 + random.Next(500);

            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new ScaleTransform(_scale, _scale));
            transformGroup.Children.Add(new RotateTransform(_rotation));

            if (image != null)
            {
                _visual = new Image
                {
                    Width = size,
                    Height = size,
                    Stretch = Stretch.Uniform,
                    Source = image,
                    RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                    RenderTransform = transformGroup
                };
            }
            else
            {
                _visual = new global::Avalonia.Controls.Shapes.Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = new RadialGradientBrush
                    {
                        GradientStops =
                        {
                            new GradientStop(Color.FromArgb(200, 255, 182, 193), 0),
                            new GradientStop(Color.FromArgb(100, 255, 105, 180), 1)
                        }
                    },
                    Stroke = Brushes.White,
                    StrokeThickness = 2,
                    RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                    RenderTransform = transformGroup
                };
            }

            _window = new Window
            {
                WindowDecorations = WindowDecorations.None,
                Background = null,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                IsHitTestVisible = false,
                CanResize = false,
                Width = size,
                Height = size,
                Position = new PixelPoint((int)screenX, (int)screenY),
                Content = _visual
            };

            _window.Show();
        }

        public void Tick(double dtMs)
        {
            if (_isDisposed) return;

            try
            {
                if (!_isPopping)
                {
                    _lifeRemainingMs -= dtMs;
                    if (_lifeRemainingMs <= 0) StartPopping();
                }

                if (_isPopping)
                {
                    _scale += 0.08;
                    _opacity -= 0.12;
                    _rotation += 5;

                    if (_opacity <= 0)
                    {
                        IsFinished = true;
                        return;
                    }
                }
                else
                {
                    if (_scale < _targetScale)
                    {
                        _scale = Math.Min(_targetScale, _scale + 0.1);
                    }
                    _rotation += 0.5;
                }

                _window.Opacity = Math.Max(0, _opacity);

                if (_visual.RenderTransform is TransformGroup tg && tg.Children.Count >= 2)
                {
                    if (tg.Children[0] is ScaleTransform st)
                    {
                        st.ScaleX = _scale;
                        st.ScaleY = _scale;
                    }
                    if (tg.Children[1] is RotateTransform rt)
                    {
                        rt.Angle = _rotation;
                    }
                }
            }
            catch
            {
                // Bubble animation is best-effort.
            }
        }

        private void StartPopping()
        {
            if (_isPopping || _isDisposed) return;
            _isPopping = true;
            _playSound?.Invoke();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            try { _window.Close(); } catch { }
        }
    }
}
