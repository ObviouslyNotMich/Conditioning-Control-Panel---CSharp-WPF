using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Video;
using ConditioningControlPanel.Models;
using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;

namespace ConditioningControlPanel.Avalonia.Services.Video;

/// <summary>
/// Avalonia implementation of the mandatory-video effect engine.
/// Schedules random full-screen video overlays during a session and supports
/// one-off video playback via <see cref="PlaySpecificVideo"/> and <see cref="PlayUrl"/>.
/// </summary>
public sealed class AvaloniaVideoService : IVideoService, IDisposable
{
    private static readonly string[] VideoExtensions = { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm", ".m4v", ".flv" };

    private readonly ISettingsService _settings;
    private readonly IAppEnvironment _environment;
    private readonly IScreenProvider _screens;
    private readonly IScheduler _scheduler;
    private readonly IUiDispatcher _dispatcher;
    private readonly IInteractionQueueService _interactionQueue;
    private readonly LibVLC _libVlc;
    private readonly IAudioDeviceService? _audioDeviceService;
    private readonly IModService? _mods;
    private readonly IAchievementService? _achievements;
    private readonly IProgressionService? _progression;
    private readonly IAppLogger? _logger;
    private readonly Random _random = new();
    private readonly object _sync = new();
    private readonly List<string> _videoFiles = new();
    private readonly List<FloatingText> _attentionTargets = new();
    private readonly List<double> _attentionSpawnTimes = new();
    private readonly List<Window> _messageWindows = new();
    private readonly List<VideoOverlayWindow> _secondaryWindows = new();

    private CancellationTokenSource? _cts;
    private IDisposable? _scheduledTimer;
    private IDisposable? _safetyTimer;
    private VideoOverlayWindow? _currentWindow;
    private string? _currentRetryPath;
    private bool _currentStrictMode;
    private double _currentDurationSeconds;
    private DateTime _videoStartTime;
    private int _attentionHits;
    private int _attentionSpawned;
    private int _attentionTotal;
    private int _attentionPenalties;
    private DispatcherTimer? _attentionTimer;
    private bool _isDisposed;

    public AvaloniaVideoService(
        ISettingsService settings,
        IAppEnvironment environment,
        IScreenProvider screens,
        IScheduler scheduler,
        IUiDispatcher dispatcher,
        IInteractionQueueService interactionQueue,
        LibVLC libVlc,
        IAudioDeviceService? audioDeviceService = null,
        IModService? mods = null,
        IAchievementService? achievements = null,
        IProgressionService? progression = null,
        IAppLogger? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _screens = screens ?? throw new ArgumentNullException(nameof(screens));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _interactionQueue = interactionQueue ?? throw new ArgumentNullException(nameof(interactionQueue));
        _libVlc = libVlc ?? throw new ArgumentNullException(nameof(libVlc));
        _audioDeviceService = audioDeviceService;
        _mods = mods;
        _achievements = achievements;
        _progression = progression;
        _logger = logger;

        RefreshVideosPath();
    }

    public bool IsRunning { get; private set; }
    public bool IsPlaying => _currentWindow?.IsPlaying ?? false;
    public string? LastVideoTitle => string.IsNullOrEmpty(_currentRetryPath) ? null : Path.GetFileNameWithoutExtension(_currentRetryPath);
    public string? LastVideoPath => _currentRetryPath;
    public int PlaythroughFailCount => _attentionPenalties;

    public event EventHandler? VideoAboutToStart;
    public event EventHandler? VideoStarted;
    public event EventHandler? VideoEnded;

    public void Start()
    {
        if (IsRunning || _isDisposed) return;
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
        {
            _logger?.Debug("AvaloniaVideoService: full-screen video overlays are not supported on mobile; Start is a no-op");
            return;
        }

        IsRunning = true;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        RefreshVideosPath();
        ScheduleNext();
        _logger?.Information("AvaloniaVideoService started (videos: {Count})", _videoFiles.Count);
    }

    public void Stop()
    {
        if (!IsRunning && _currentWindow == null) return;
        IsRunning = false;

        _scheduledTimer?.Dispose();
        _scheduledTimer = null;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _dispatcher.Invoke(() => CleanupInternal(notifyEnded: false));
        _logger?.Information("AvaloniaVideoService stopped");
    }

    public void RefreshVideosPath()
    {
        LoadVideoFiles();
    }

    public void PlaySpecificVideo(string videoPath, bool strictMode)
    {
        if (_isDisposed) return;
        if (string.IsNullOrWhiteSpace(videoPath)) return;
        var path = videoPath;
        if (!Path.IsPathRooted(path))
            path = Path.Combine(_environment.EffectiveAssetsPath, "videos", path);
        if (!File.Exists(path))
        {
            _logger?.Warning("AvaloniaVideoService: PlaySpecificVideo path not found {Path}", path);
            return;
        }
        _dispatcher.Invoke(() => PlayFile(path, strictMode));
    }

    public void PlayUrl(string url)
    {
        if (_isDisposed) return;
        if (string.IsNullOrWhiteSpace(url)) return;
        _dispatcher.Invoke(() => PlayUrlCore(url));
    }

    private void LoadVideoFiles()
    {
        lock (_sync)
        {
            _videoFiles.Clear();
            var folders = new[]
            {
                Path.Combine(_environment.EffectiveAssetsPath, "videos"),
                Path.Combine(_environment.BaseDirectory, "Resources", "videos")
            };

            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var ext in VideoExtensions)
                {
                    try
                    {
                        _videoFiles.AddRange(Directory.GetFiles(folder, $"*{ext}", SearchOption.AllDirectories)
                            .Where(f => IsPathSafe(f, _environment.EffectiveAssetsPath) || IsPathSafe(f, _environment.BaseDirectory)));
                    }
                    catch { }
                }
            }
        }
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

    private void ScheduleNext()
    {
        if (!IsRunning) return;

        var settings = _settings.Current;
        if (settings == null || !settings.MandatoryVideosEnabled) return;
        lock (_sync)
        {
            if (_videoFiles.Count == 0) return;
        }

        _scheduledTimer?.Dispose();

        var freq = Math.Max(1, settings.VideosPerHour);
        var baseInterval = 3600.0 / freq;
        var variance = baseInterval * 0.3;
        var interval = baseInterval + (Random.Shared.NextDouble() * variance * 2 - variance);
        interval = Math.Max(10, interval);

        _scheduledTimer = _scheduler.StartOneShotTimer(TimeSpan.FromSeconds(interval), () =>
        {
            if (!IsRunning) return;
            try
            {
                var s = _settings.Current;
                if (s != null && s.MandatoryVideosEnabled)
                    PlayRandomVideo();
            }
            catch (Exception ex) { _logger?.Warning(ex, "AvaloniaVideoService: scheduled playback failed"); }
            ScheduleNext();
        });
    }

    private void PlayRandomVideo()
    {
        string? file;
        lock (_sync)
        {
            if (_videoFiles.Count == 0) return;
            file = _videoFiles[Random.Shared.Next(_videoFiles.Count)];
        }
        var strict = _settings.Current?.StrictLockEnabled ?? false;
        PlayFile(file, strict);
    }

    private string? PickRandomVideo()
    {
        lock (_sync)
        {
            if (_videoFiles.Count == 0) return null;
            return _videoFiles[Random.Shared.Next(_videoFiles.Count)];
        }
    }

    private void PlayFile(string filePath, bool strictMode)
    {
        if (_isDisposed) return;
        CleanupInternal(notifyEnded: false);

        _interactionQueue.TryStart("Video", () =>
        {
            var screen = GetPrimaryScreen();
            var volume = (_settings.Current?.MasterVolume ?? 50) / 100.0;
            _currentWindow = CreateWindow(screen, filePath, fromUrl: false, volume, strictMode);
            _currentWindow.Show();
            SpawnSecondaryWindows(filePath, fromUrl: false, strictMode);
        });
    }

    private void PlayUrlCore(string url)
    {
        if (_isDisposed) return;
        CleanupInternal(notifyEnded: false);

        _interactionQueue.TryStart("Video", () =>
        {
            var screen = GetPrimaryScreen();
            var volume = (_settings.Current?.MasterVolume ?? 50) / 100.0;
            _currentWindow = CreateWindow(screen, url, fromUrl: true, volume, strictMode: false);
            _currentWindow.Show();
            SpawnSecondaryWindows(url, fromUrl: true, strictMode: false);
        });
    }

    private void SpawnSecondaryWindows(string source, bool fromUrl, bool strictMode)
    {
        try
        {
            var settings = _settings.Current;
            if (settings == null || !settings.DualMonitorEnabled) return;

            var allScreens = _screens.GetAllScreens();
            if (allScreens.Count <= 1) return;

            var primary = _screens.GetPrimaryScreen() ?? allScreens[0];
            foreach (var screen in allScreens)
            {
                if (screen == primary) continue;
                var win = new VideoOverlayWindow(_libVlc, screen, source, fromUrl, 0, strictMode, () => { }, _logger, withAudio: false);
                _secondaryWindows.Add(win);
                win.Show();
            }

            _logger?.Information("AvaloniaVideoService: spawned {Count} secondary video window(s)", _secondaryWindows.Count);
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "AvaloniaVideoService: failed to spawn secondary windows");
        }
    }

    private VideoOverlayWindow CreateWindow(ScreenInfo screen, string source, bool fromUrl, double volume, bool strictMode)
    {
        _currentRetryPath = fromUrl ? null : source;
        _currentStrictMode = strictMode;
        _currentDurationSeconds = 0;
        _attentionHits = 0;
        _attentionSpawned = 0;
        _attentionTotal = 0;
        _attentionSpawnTimes.Clear();
        lock (_attentionTargets) { _attentionTargets.Clear(); }

        var win = new VideoOverlayWindow(_libVlc, screen, source, fromUrl, volume, strictMode, () => { }, _logger);
        win.VideoStarted += OnVideoWindowStarted;
        win.VideoEnded += OnVideoWindowEnded;
        return win;
    }

    private void OnVideoWindowStarted()
    {
        _videoStartTime = DateTime.Now;
        VideoStarted?.Invoke(this, EventArgs.Empty);

        var settings = _settings.Current;
        if (settings == null) return;

        if (_currentWindow?.MediaPlayer is { } player && player.Length > 0)
        {
            _currentDurationSeconds = player.Length / 1000.0;
        }
        else
        {
            _currentDurationSeconds = 0;
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(60, _currentDurationSeconds + 30));
        _interactionQueue.ExtendTimeout(timeout);
        StartSafetyTimer(_currentDurationSeconds > 0 ? _currentDurationSeconds + 30 : 600);

        if (settings.AttentionChecksEnabled)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000);
                _dispatcher.Post(SetupAttention);
            });
        }
    }

    private void OnVideoWindowEnded()
    {
        OnVideoEnded();
    }

    private void StartSafetyTimer(double timeoutSeconds)
    {
        _safetyTimer?.Dispose();
        _safetyTimer = _scheduler.StartOneShotTimer(TimeSpan.FromSeconds(Math.Max(30, timeoutSeconds)), () =>
        {
            if (_currentWindow == null) return;
            _logger?.Warning("AvaloniaVideoService: safety timeout triggered, forcing cleanup");
            CleanupInternal(notifyEnded: true);
        });
    }

    private void SetupAttention()
    {
        try
        {
            var settings = _settings.Current;
            if (settings == null || !IsPlaying) return;

            lock (_attentionTargets)
            {
                foreach (var t in _attentionTargets.ToList()) t.Destroy();
                _attentionTargets.Clear();
            }

            _attentionSpawned = 0;
            _attentionHits = 0;
            _attentionSpawnTimes.Clear();

            var duration = _currentDurationSeconds > 0 ? _currentDurationSeconds : 60;
            var maxTargets = Math.Max(1, settings.AttentionDensity);
            _attentionTotal = settings.RandomizeAttentionTargets
                ? _random.Next(1, maxTargets + 1)
                : maxTargets;

            var availableWindow = Math.Max(1, duration - 8);
            const double minGap = 3.0;
            for (int i = 0; i < _attentionTotal; i++)
            {
                _attentionSpawnTimes.Add(3 + _random.NextDouble() * availableWindow);
            }
            _attentionSpawnTimes.Sort();
            for (int i = 1; i < _attentionSpawnTimes.Count; i++)
            {
                if (_attentionSpawnTimes[i] - _attentionSpawnTimes[i - 1] < minGap)
                    _attentionSpawnTimes[i] = _attentionSpawnTimes[i - 1] + minGap;
            }

            _attentionTimer?.Stop();
            _attentionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            _attentionTimer.Tick += (_, _) => CheckSpawnTargets();
            _attentionTimer.Start();

            _logger?.Information("Attention: {Count} targets over {Duration}s", _attentionTotal, (int)duration);
        }
        catch (Exception ex)
        {
            _logger?.Warning("AvaloniaVideoService.SetupAttention failed: {Error}", ex.Message);
        }
    }

    private void CheckSpawnTargets()
    {
        if (!IsPlaying) return;
        var elapsed = (DateTime.Now - _videoStartTime).TotalSeconds;
        while (_attentionSpawnTimes.Count > 0 && elapsed >= _attentionSpawnTimes[0])
        {
            _attentionSpawnTimes.RemoveAt(0);
            SpawnTarget();
        }
    }

    private void SpawnTarget()
    {
        try
        {
            var settings = _settings.Current;
            if (settings == null) return;

            var pool = settings.AttentionPool?.Where(p => p.Value).Select(p => p.Key).ToList()
                ?? new List<string>();
            var text = pool.Count > 0 ? pool[_random.Next(pool.Count)] : "CLICK ME";

            var screens = settings.DualMonitorEnabled ? _screens.GetAllScreens().ToArray() : new[] { GetPrimaryScreen() };
            if (screens.Length == 0 || screens[0] == null)
            {
                _logger?.Warning("AvaloniaVideoService.SpawnTarget: no screens available");
                return;
            }

            _attentionSpawned++;
            var spawnedTargets = new List<FloatingText>();
            bool hitRegistered = false;

            _logger?.Debug("Spawning attention target: '{Text}' on {ScreenCount} screen(s) ({Spawned}/{Total})",
                text, screens.Length, _attentionSpawned, _attentionTotal);

            foreach (var screen in screens)
            {
                if (screen == null) continue;
                FloatingText? target = null;
                target = new FloatingText(
                    _libVlc,
                    _audioDeviceService,
                    _environment,
                    settings,
                    screen,
                    text,
                    settings.AttentionSize,
                    () =>
                    {
                        if (hitRegistered) return;
                        hitRegistered = true;
                        _attentionHits++;
                        _progression?.AddXP(15, XPSource.Video);

                        lock (_attentionTargets)
                        {
                            foreach (var t in spawnedTargets)
                            {
                                if (_attentionTargets.Contains(t))
                                {
                                    _attentionTargets.Remove(t);
                                    if (t != target) t.Destroy();
                                }
                            }
                        }

                        List<FloatingText> remaining;
                        lock (_attentionTargets) { remaining = _attentionTargets.ToList(); }
                        _logger?.Information("ATTENTION: Hit {Hits}/{Spawned}, {Remaining} targets remaining", _attentionHits, _attentionSpawned, remaining.Count);

                        if (remaining.Count > 0)
                        {
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(300);
                                _dispatcher.Post(() =>
                                {
                                    foreach (var t in remaining) t.BringToFront();
                                });
                            });
                        }
                    });

                spawnedTargets.Add(target);
                lock (_attentionTargets) { _attentionTargets.Add(target); }
            }

            var lifespan = Math.Max(1, settings.AttentionLifespan) * 1000;
            _ = Task.Run(async () =>
            {
                await Task.Delay(lifespan);
                _dispatcher.Post(() =>
                {
                    lock (_attentionTargets)
                    {
                        foreach (var t in spawnedTargets)
                        {
                            if (_attentionTargets.Contains(t))
                            {
                                _attentionTargets.Remove(t);
                                t.Destroy();
                            }
                        }
                    }
                });
            });
        }
        catch (Exception ex)
        {
            _logger?.Error("AvaloniaVideoService.SpawnTarget failed: {Error}", ex.Message);
        }
    }

    private void OnVideoEnded()
    {
        _attentionTimer?.Stop();
        _attentionTimer = null;
        _safetyTimer?.Dispose();
        _safetyTimer = null;

        var settings = _settings.Current;
        bool loop = false, troll = false;

        if (settings != null && settings.AttentionChecksEnabled && _attentionSpawned > 0)
        {
            bool passed = _attentionHits >= _attentionSpawned;
            _logger?.Information("Attention result: {Hits}/{Spawned} (of {Total} scheduled) = {Result}",
                _attentionHits, _attentionSpawned, _attentionTotal, passed ? "PASS" : "FAIL");

            if (passed)
            {
                var xpForPlays = (_attentionPenalties + 1) * 50;
                _progression?.AddXP(xpForPlays + 200, XPSource.Video);
                _achievements?.TrackAttentionCheckPassed(isVideo: true);
                if (_random.NextDouble() < 0.1)
                {
                    loop = true;
                    troll = true;
                }
            }
            else
            {
                loop = true;
                _achievements?.TrackAttentionCheckFailed();
                _achievements?.TrackVideoAttentionCheckFailed();
            }
        }

        if (loop && !string.IsNullOrEmpty(_currentRetryPath))
        {
            _attentionPenalties++;
            if (_attentionPenalties >= 3 && settings?.MercySystemEnabled == true)
            {
                ShowMessage(_mods?.GetAttentionCheckMercyMessage() ?? "BAMBI GETS MERCY", 2500, () => CleanupInternal(notifyEnded: true));
            }
            else
            {
                var message = troll
                    ? "GOOD GIRL!\nWATCH AGAIN 😜"
                    : (_mods?.GetAttentionCheckFailMessage() ?? "DUMB BAMBI!\nTRY AGAIN");
                ShowMessage(message, 2000, () =>
                {
                    _attentionHits = 0;
                    _attentionSpawnTimes.Clear();
                    _interactionQueue.ExtendTimeout(TimeSpan.FromMinutes(5));
                    var retryVideo = PickRandomVideo();
                    PlayFile(string.IsNullOrEmpty(retryVideo) ? _currentRetryPath! : retryVideo, _currentStrictMode);
                });
            }
            return;
        }

        if (_currentDurationSeconds > 0)
        {
            _achievements?.TrackVideoWatched(_currentDurationSeconds);
        }

        CleanupInternal(notifyEnded: true);
    }

    private void ShowMessage(string text, int ms, Action then)
    {
        CleanupInternal(notifyEnded: false);

        var screens = _settings.Current?.DualMonitorEnabled == true
            ? _screens.GetAllScreens().ToArray()
            : new[] { GetPrimaryScreen() };
        if (screens.Length == 0 || screens[0] == null)
        {
            then();
            return;
        }

        foreach (var screen in screens)
        {
            if (screen == null) continue;
            var win = new Window
            {
                WindowDecorations = WindowDecorations.None,
                Background = new SolidColorBrush(Colors.Black),
                Topmost = true,
                ShowInTaskbar = false,
                CanResize = false,
                ShowActivated = false,
                WindowState = WindowState.FullScreen,
                Position = new PixelPoint((int)screen.Bounds.X, (int)screen.Bounds.Y),
                Width = screen.Bounds.Width,
                Height = screen.Bounds.Height,
                Content = new TextBlock
                {
                    Text = text,
                    Foreground = new SolidColorBrush(
                        global::Avalonia.Application.Current?.TryGetResource("PinkColor", global::Avalonia.Styling.ThemeVariant.Default, out var v) == true && v is Color c
                            ? c
                            : new Color(0xFF, 0xFF, 0x00, 0xFF)),
                    FontSize = 64,
                    FontWeight = FontWeight.Bold,
                    FontFamily = new FontFamily("Impact, Arial"),
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                }
            };
            win.Show();
            _messageWindows.Add(win);
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(ms);
            _dispatcher.Post(() =>
            {
                CloseMessageWindows();
                then();
            });
        });
    }

    private void CloseMessageWindows()
    {
        foreach (var w in _messageWindows.ToList())
        {
            try { w.Close(); } catch { }
        }
        _messageWindows.Clear();
    }

    private void CleanupInternal(bool notifyEnded)
    {
        _attentionTimer?.Stop();
        _attentionTimer = null;
        _safetyTimer?.Dispose();
        _safetyTimer = null;

        lock (_attentionTargets)
        {
            foreach (var t in _attentionTargets.ToList()) t.Destroy();
            _attentionTargets.Clear();
        }

        CloseMessageWindows();

        foreach (var win in _secondaryWindows.ToList())
        {
            try { win.Close(); } catch { }
        }
        _secondaryWindows.Clear();

        if (_currentWindow != null)
        {
            try
            {
                _currentWindow.VideoStarted -= OnVideoWindowStarted;
                _currentWindow.VideoEnded -= OnVideoWindowEnded;
                _currentWindow.Close();
            }
            catch { }
            _currentWindow = null;
        }

        _currentStrictMode = false;
        _currentDurationSeconds = 0;

        try { _interactionQueue.Complete("Video"); } catch { }

        if (notifyEnded)
        {
            VideoEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private ScreenInfo GetPrimaryScreen()
    {
        try
        {
            return _screens.GetPrimaryScreen()
                ?? _screens.GetAllScreens().FirstOrDefault()
                ?? new ScreenInfo("fallback", new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), 1.0);
        }
        catch (Exception ex)
        {
            _logger?.Debug("AvaloniaVideoService: could not get primary screen: {Error}", ex.Message);
            return new ScreenInfo("fallback", new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), 1.0);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
    }

    private sealed class VideoOverlayWindow : Window
    {
        private readonly LibVLC _libVlc;
        private readonly string _source;
        private readonly bool _fromUrl;
        private readonly double _volume;
        private readonly bool _strictMode;
        private readonly Action _onClosed;
        private readonly VideoView _videoView;
        private readonly IAppLogger? _logger;
        private readonly bool _withAudio;
        private MediaPlayer? _mediaPlayer;
        private Media? _media;
        private bool _isPlaying;

        public VideoOverlayWindow(LibVLC libVlc, ScreenInfo screen, string source, bool fromUrl, double volume, bool strictMode, Action onClosed, IAppLogger? logger, bool withAudio = true)
        {
            _libVlc = libVlc;
            _source = source;
            _fromUrl = fromUrl;
            _volume = volume;
            _strictMode = strictMode;
            _onClosed = onClosed;
            _logger = logger;
            _withAudio = withAudio;

            WindowDecorations = WindowDecorations.None;
            Topmost = true;
            ShowInTaskbar = false;
            CanResize = false;
            ShowActivated = false;
            WindowState = WindowState.FullScreen;

            Position = new PixelPoint((int)screen.Bounds.X, (int)screen.Bounds.Y);
            Width = screen.Bounds.Width;
            Height = screen.Bounds.Height;

            _videoView = new VideoView
            {
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch
            };
            Content = _videoView;

            Opened += OnOpened;
            Closing += OnClosing;
            Closed += OnClosed;
            KeyDown += OnKeyDown;
        }

        public bool IsPlaying => _isPlaying;
        public MediaPlayer? MediaPlayer => _mediaPlayer;

        public event Action? VideoStarted;
        public event Action? VideoEnded;

        private void OnOpened(object? sender, EventArgs e)
        {
            try
            {
                _mediaPlayer = new MediaPlayer(_libVlc)
                {
                    EnableHardwareDecoding = true
                };
                _mediaPlayer.Volume = _withAudio ? (int)(_volume * 100) : 0;
                _mediaPlayer.EndReached += OnEndReached;
                _mediaPlayer.LengthChanged += OnLengthChanged;
                _mediaPlayer.Playing += OnPlaying;

                _media = _fromUrl
                    ? new Media(_libVlc, new Uri(_source))
                    : new Media(_libVlc, _source, FromType.FromPath);

                _videoView.MediaPlayer = _mediaPlayer;
                _mediaPlayer.Play(_media);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "AvaloniaVideoService: failed to start video {Source}", _source);
                Close();
            }
        }

        private void OnPlaying(object? sender, EventArgs e)
        {
            _isPlaying = true;
            VideoStarted?.Invoke();
        }

        private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
        {
            // Length is in ms.
        }

        private void OnEndReached(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _isPlaying = false;
                try { Close(); } catch { }
            });
        }

        private void OnClosing(object? sender, WindowClosingEventArgs e)
        {
            if (_strictMode && _isPlaying)
            {
                e.Cancel = true;
                return;
            }
            _isPlaying = false;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _isPlaying = false;
            try
            {
                if (_mediaPlayer != null)
                {
                    _mediaPlayer.EndReached -= OnEndReached;
                    _mediaPlayer.LengthChanged -= OnLengthChanged;
                    _mediaPlayer.Playing -= OnPlaying;
                    _mediaPlayer.Stop();
                }
                _videoView.MediaPlayer = null;
                _mediaPlayer?.Dispose();
                _mediaPlayer = null;
                _media?.Dispose();
                _media = null;
            }
            catch { }
            VideoEnded?.Invoke();
            _onClosed();
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (!_strictMode) return;
            if (e.Key == Key.Escape ||
                (e.Key == Key.Tab && (e.KeyModifiers == KeyModifiers.Alt || e.KeyModifiers == KeyModifiers.Control)) ||
                e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LWin || e.Key == Key.RWin)
            {
                e.Handled = true;
            }
        }
    }

    private sealed class FloatingText
    {
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private readonly Window _win;
        private readonly DispatcherTimer _timer;
        private readonly Action _onHit;
        private readonly LibVLC _libVlc;
        private readonly IAudioDeviceService? _audioDeviceService;
        private readonly string _baseDirectory;
        private readonly double _masterVolume;
        private double _x, _y, _vx, _vy;
        private readonly double _minX, _minY, _maxX, _maxY;
        private readonly double _width, _height;
        private bool _dead;
        private bool _clicked;
        private IntPtr _hwnd;
        private int _tickCount;

        public FloatingText(LibVLC libVlc, IAudioDeviceService? audioDeviceService, IAppEnvironment environment, AppSettings settings, ScreenInfo screen, string text, int size, Action onHit)
        {
            _libVlc = libVlc;
            _audioDeviceService = audioDeviceService;
            _baseDirectory = environment.BaseDirectory;
            _onHit = onHit;
            _masterVolume = settings.MasterVolume / 100.0;

            size = Math.Max(40, size);
            text = FormatTriggerText(text);

            var area = screen.WorkingArea;
            double areaX = area.X / screen.Scaling;
            double areaY = area.Y / screen.Scaling;
            double areaWidth = area.Width / screen.Scaling;
            double areaHeight = area.Height / screen.Scaling;

            var marginX = Math.Min(150, areaWidth * 0.08);
            var marginY = Math.Min(100, areaHeight * 0.08);
            _minX = areaX + marginX;
            _minY = areaY + marginY;
            _maxX = areaX + areaWidth - marginX;
            _maxY = areaY + areaHeight - marginY;

            Color color1, color2, textColor, borderColor;
            try
            {
                color1 = Color.Parse(settings.AttentionColor1);
                color2 = Color.Parse(settings.AttentionColor2);
                textColor = Color.Parse(settings.AttentionTextColor);
                borderColor = Color.Parse(settings.AttentionBorderColor);
            }
            catch
            {
                color1 = AppColor("DarkPinkColor", Color.FromRgb(255, 20, 147));
                color2 = AppColor("PinkColor", Color.FromRgb(255, 105, 180));
                textColor = AppColor("TextLight", Color.FromRgb(255, 20, 147));
                borderColor = AppColor("DarkPinkColor", Color.FromRgb(255, 20, 147));
            }

            var isFloating = settings.AttentionFloatingText;

            var typeface = new Typeface(
                string.IsNullOrWhiteSpace(settings.AttentionFont) ? FontFamily.Default : new FontFamily(settings.AttentionFont),
                FontStyle.Normal,
                FontWeight.Bold);

            var formattedText = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                size,
                Brushes.White);
            formattedText.TextAlignment = TextAlignment.Center;

            const double outlineThickness = 7.5;
            _width = formattedText.WidthIncludingTrailingWhitespace + outlineThickness * 2 + 60;
            _height = formattedText.Height + outlineThickness * 2 + 40;
            _width = Math.Max(_width, 150);
            _height = Math.Max(_height, 60);

            var textBlock = new TextBlock
            {
                Text = text,
                FontSize = size,
                FontWeight = FontWeight.Bold,
                FontFamily = typeface.FontFamily,
                Foreground = new SolidColorBrush(textColor),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
            };

            var border = new Border
            {
                Background = isFloating ? null : new LinearGradientBrush
                {
                    GradientStops = new GradientStops { new GradientStop(color1, 0), new GradientStop(color2, 1) }
                },
                CornerRadius = isFloating ? new CornerRadius(0) : new CornerRadius(20),
                BorderBrush = (settings.AttentionShowBorder && !isFloating) ? new SolidColorBrush(borderColor) : null,
                BorderThickness = (settings.AttentionShowBorder && !isFloating) ? new Thickness(3) : new Thickness(0),
                Padding = isFloating ? new Thickness(0) : new Thickness(20, 10, 20, 10),
                Child = textBlock
            };

            var container = new Panel();
            var hitZone = new global::Avalonia.Controls.Shapes.Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0))
            };
            container.Children.Add(hitZone);
            container.Children.Add(border);

            _win = new Window
            {
                WindowDecorations = WindowDecorations.None,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                Background = null,
                Topmost = true,
                ShowInTaskbar = false,
                CanResize = false,
                ShowActivated = false,
                Width = _width,
                Height = _height,
                Content = container,
                WindowStartupLocation = WindowStartupLocation.Manual
            };

            var spawnRangeX = Math.Max(0, (_maxX - _width) - _minX);
            var spawnRangeY = Math.Max(0, (_maxY - _height) - _minY);
            _x = _minX + Random.Shared.NextDouble() * spawnRangeX;
            _y = _minY + Random.Shared.NextDouble() * spawnRangeY;
            _x = Math.Clamp(_x, _minX, Math.Max(_minX, _maxX - _width));
            _y = Math.Clamp(_y, _minY, Math.Max(_minY, _maxY - _height));

            var angle = Random.Shared.NextDouble() * Math.PI * 2;
            _vx = Math.Cos(angle) * 3.0;
            _vy = Math.Sin(angle) * 3.0;

            _win.Position = new PixelPoint((int)_x, (int)_y);

            _win.PointerPressed += (s, e) =>
            {
                if (e.GetCurrentPoint(_win).Properties.IsLeftButtonPressed)
                {
                    e.Handled = true;
                    Hit();
                }
            };

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += (_, _) =>
            {
                if (_dead) return;
                _x += _vx;
                _y += _vy;
                if (_x < _minX) { _x = _minX; _vx = Math.Abs(_vx); }
                if (_x + _width > _maxX) { _x = _maxX - _width; _vx = -Math.Abs(_vx); }
                if (_y < _minY) { _y = _minY; _vy = Math.Abs(_vy); }
                if (_y + _height > _maxY) { _y = _maxY - _height; _vy = -Math.Abs(_vy); }
                _win.Position = new PixelPoint((int)_x, (int)_y);

                _tickCount++;
                if (_tickCount >= 2 && _hwnd != IntPtr.Zero)
                {
                    _tickCount = 0;
                    SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            };

            _win.Opened += (s, e) =>
            {
                ApplyToolWindowStyle(_win);
                _hwnd = _win.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (_hwnd != IntPtr.Zero)
                {
                    SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                }
                _timer.Start();
            };

            _win.Show();
        }

        public void BringToFront()
        {
            if (_dead || _hwnd == IntPtr.Zero) return;
            try
            {
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            catch { }
        }

        public void Destroy()
        {
            if (_dead) return;
            _dead = true;
            _timer.Stop();
            try { _win.Close(); } catch { }
        }

        private void Hit()
        {
            if (_clicked || _dead) return;
            _clicked = true;
            PlayPopSound();
            try { _onHit?.Invoke(); } catch { }
            FadeOut();
        }

        private void FadeOut()
        {
            _timer.Stop();
            var fade = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            fade.Tick += (_, _) =>
            {
                _win.Opacity -= 0.15;
                if (_win.Opacity <= 0.1)
                {
                    fade.Stop();
                    Destroy();
                }
            };
            fade.Start();
        }

        private void PlayPopSound()
        {
            try
            {
                var soundsPath = Path.Combine(_baseDirectory, "Resources", "sounds", "bubbles");
                var popFiles = new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" };
                var chosen = popFiles[Random.Shared.Next(popFiles.Length)];
                var popPath = Path.Combine(soundsPath, chosen);
                if (!File.Exists(popPath)) return;

                _ = Task.Run(() =>
                {
                    try
                    {
                        using var player = new MediaPlayer(_libVlc);
                        var deviceId = _audioDeviceService?.GetDefaultOutputDeviceId();
                        if (!string.IsNullOrEmpty(deviceId))
                        {
                            try { player.SetOutputDevice(deviceId); } catch { }
                        }
                        player.Volume = (int)(60 * _masterVolume);
                        using var media = new Media(_libVlc, popPath, FromType.FromPath);
                        player.Play(media);
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        while (player.IsPlaying && sw.ElapsedMilliseconds < 3000)
                        {
                            Thread.Sleep(50);
                        }
                    }
                    catch { }
                });
            }
            catch { }
        }

        private static string FormatTriggerText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 2) return $"{words[0]}\n{words[1]}";
            if (words.Length >= 4)
            {
                int mid = words.Length / 2;
                return $"{string.Join(" ", words.Take(mid))}\n{string.Join(" ", words.Skip(mid))}";
            }
            return text;
        }

        private static Color AppColor(string key, Color fallback)
        {
            if (global::Avalonia.Application.Current?.TryGetResource(key, global::Avalonia.Styling.ThemeVariant.Default, out var v) == true && v is Color c)
                return c;
            return fallback;
        }

        private static void ApplyToolWindowStyle(Window window)
        {
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (hwnd == IntPtr.Zero) return;
                var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                exStyle |= WS_EX_TOOLWINDOW;
                exStyle &= ~WS_EX_APPWINDOW;
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
            }
            catch { }
        }
    }
}
