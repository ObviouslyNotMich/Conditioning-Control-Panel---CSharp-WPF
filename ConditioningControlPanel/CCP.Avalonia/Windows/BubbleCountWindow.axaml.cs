using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
/// The WPF version uses per-window VideoViews, NAudio pop sounds, WPF Screen APIs,
/// and the legacy BubbleCountService. This port plays the video through LibVLC and
/// stubs the bubble-spawn game loop with TODOs until the engine is extracted.
/// </summary>
public partial class BubbleCountWindow : Window
{
    private readonly global::ConditioningControlPanel.IAppLogger? _logger;


    private readonly string _videoPath = string.Empty;
    private readonly BubbleCountService.Difficulty _difficulty;
    private readonly bool _strictMode;
    private readonly Action<bool> _onComplete = _ => { };
    private readonly ScreenInfo? _screen;
    private readonly bool _isPrimary;

    private readonly LibVLC? _libVLC;
    private readonly MediaPlayer? _mediaPlayer;
    private DispatcherTimer? _safetyTimer;

    private int _targetBubbleCount;
    private double _videoDurationSeconds = 30;
    private bool _videoEnded;
    private bool _gameCompleted;

    private static readonly object _cleanupLock = new();
    private static bool _isCleaningUp;
    private static readonly List<BubbleCountWindow> _allWindows = new();
    private static int _sharedBubbleCount;
    private static int _sharedTargetCount;

    /// <summary>Duration of the last played video in seconds (shared for XP scaling).</summary>
    internal static double LastVideoDurationSeconds { get; private set; } = 30;

    private readonly IBubbleCountService _bubbleCount;

    public BubbleCountWindow()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
_bubbleCount = App.Services.GetRequiredService<IBubbleCountService>();
    }

    public BubbleCountWindow(string videoPath, BubbleCountService.Difficulty difficulty,
        bool strictMode, Action<bool> onComplete,
        ScreenInfo? screen = null, bool isPrimary = true)
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
_bubbleCount = App.Services.GetRequiredService<IBubbleCountService>();
        _videoPath = videoPath;
        _difficulty = difficulty;
        _strictMode = strictMode;
        _onComplete = onComplete;
        _screen = screen;
        _isPrimary = isPrimary;

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
            _logger?.Error(ex, "BubbleCountWindow: failed to create LibVLC media player");
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
        var logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
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
            App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Error("BubbleCountWindow: no screens available");
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
            App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Error(ex, "BubbleCountWindow: failed to create/show windows");
            onComplete?.Invoke(false);
        }
    }

    /// <summary>
    /// Force close all bubble-count windows.
    /// </summary>
    public static void ForceCloseAll()
    {
        var logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
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
                window._mediaPlayer?.Stop();
                window.Close();
            }
            catch (Exception ex)
            {
                App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Information("BubbleCountWindow: error closing window - {Error}", ex.Message);
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
            _logger?.Error(ex, "BubbleCountWindow: failed to position window");
            WindowState = WindowState.Maximized;
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _logger?.Information("BubbleCountWindow.OnLoaded: primary={IsPrimary}, video={Video}", _isPrimary, _videoPath);

        try
        {
            if (_mediaPlayer == null || _libVLC == null)
            {
                _logger?.Error("BubbleCountWindow.OnLoaded: LibVLC not available");
                if (_isPrimary) CloseAllWindows(false);
                return;
            }

            if (!File.Exists(_videoPath))
            {
                _logger?.Error("BubbleCountWindow.OnLoaded: video file not found: {Path}", _videoPath);
                if (_isPrimary) CloseAllWindows(false);
                return;
            }

            if (_isPrimary)
            {
                _videoDurationSeconds = 30;
                LastVideoDurationSeconds = _videoDurationSeconds;
                _targetBubbleCount = CalculateTargetBubbles();
                _sharedTargetCount = _targetBubbleCount;

                StartSafetyTimer(_videoDurationSeconds);
                // TODO: start actual bubble spawning once the game engine is ported.
                _ = Dispatcher.UIThread.InvokeAsync(() => { /* placeholder spawn tick */ });
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
            else
            {
                var settings = App.Services?.GetService<ISettingsService>()?.Current;
                var volume = (int)((settings?.MasterVolume ?? 100) / 100.0 * 100);
                _mediaPlayer.Volume = Math.Clamp(volume, 0, 100);
            }

            _mediaPlayer.Play(media);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "BubbleCountWindow.OnLoaded: failed to initialize game");
            if (_isPrimary) CloseAllWindows(false);
        }
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
        _logger?.Error("BubbleCountWindow: media playback error");
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
        var random = new Random();
        var count = (int)Math.Round(scaledCount + (random.NextDouble() * variance * 2 - variance));
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
                _logger?.Warning("BubbleCountWindow: safety timeout - forcing video end");
                OnVideoEnded();
            }
        };
        _safetyTimer.Start();
    }

    private void OnVideoEnded()
    {
        if (_videoEnded || _isCleaningUp) return;
        _videoEnded = true;

        _safetyTimer?.Stop();

        foreach (var window in _allWindows.ToList())
        {
            window._videoEnded = true;
        }

        if (_isPrimary && _videoDurationSeconds > 0)
        {
            _logger?.Information("BubbleCount video watched: {Duration}s", _videoDurationSeconds);
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
        _mediaPlayer?.Stop();
        _mediaPlayer?.Dispose();
        _allWindows.Remove(this);
        base.OnClosed(e);
    }
}
