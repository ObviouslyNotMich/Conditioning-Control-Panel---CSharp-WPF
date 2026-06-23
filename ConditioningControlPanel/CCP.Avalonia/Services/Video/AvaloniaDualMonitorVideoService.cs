using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Core.Platform;
using LibVLCSharp.Shared;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace ConditioningControlPanel.Avalonia.Services.Video;

/// <summary>
/// Avalonia port of the legacy WPF <c>DualMonitorVideoService</c>.
/// Plays a single decoded video stream across all connected monitors using LibVLC
/// memory rendering (RV32) into per-window Avalonia <see cref="WriteableBitmap"/> instances.
/// </summary>
public sealed class AvaloniaDualMonitorVideoService : IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly IScreenProvider _screenProvider;
    private readonly ILogger<AvaloniaDualMonitorVideoService> _logger;

    private VlcMediaPlayer? _mediaPlayer;
    private IntPtr _frameBuffer = IntPtr.Zero;
    private uint _videoWidth;
    private uint _videoHeight;
    private readonly List<(Window Window, WriteableBitmap Bitmap, Image ImageControl)> _windowData = new();
    private readonly object _bufferLock = new();
    private volatile bool _frameReady;
    private volatile bool _bufferValid;
    private bool _isPlaying;
    private bool _disposed;
    private Task? _playerDisposeTask;
    private readonly DispatcherTimer _renderTimer;

    public event EventHandler? PlaybackStarted;
    public event EventHandler? PlaybackEnded;
    public event EventHandler<string>? PlaybackError;

    public bool IsPlaying => _isPlaying;

    public AvaloniaDualMonitorVideoService(
        LibVLC libVlc,
        IScreenProvider screenProvider,
        ILogger<AvaloniaDualMonitorVideoService> logger)
    {
        _libVlc = libVlc;
        _screenProvider = screenProvider;
        _logger = logger;

        _renderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _renderTimer.Tick += OnRenderTick;
    }

    /// <summary>
    /// Play a video URL on all monitors simultaneously.
    /// </summary>
    /// <param name="videoUrl">Direct video URL (mp4, m3u8, etc.)</param>
    /// <param name="width">Video width for buffer allocation (default 1920)</param>
    /// <param name="height">Video height for buffer allocation (default 1080)</param>
    public void Play(string videoUrl, uint width = 1920, uint height = 1080)
    {
        if (_isPlaying)
        {
            Stop();
        }

        try
        {
            _videoWidth = width;
            _videoHeight = height;

            var bufferSize = _videoWidth * _videoHeight * 4; // BGRA = 4 bytes per pixel
            lock (_bufferLock)
            {
                _frameBuffer = Marshal.AllocHGlobal((int)bufferSize);
                _bufferValid = true;
            }

            _mediaPlayer = new VlcMediaPlayer(_libVlc);
            _mediaPlayer.SetVideoCallbacks(LockCallback, null, DisplayCallback);
            _mediaPlayer.SetVideoFormat("RV32", _videoWidth, _videoHeight, _videoWidth * 4);

            _mediaPlayer.Playing += OnPlaying;
            _mediaPlayer.EndReached += OnEndReached;
            _mediaPlayer.EncounteredError += OnError;

            Dispatcher.UIThread.Invoke(CreateWindows);
            Dispatcher.UIThread.Invoke(() => _renderTimer.Start());

            using var media = new Media(_libVlc, videoUrl, FromType.FromLocation);
            _mediaPlayer.Play(media);
            _isPlaying = true;

            _logger.LogInformation("AvaloniaDualMonitorVideo: Started playback of {Url} on {Count} monitors",
                videoUrl, _windowData.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AvaloniaDualMonitorVideo: Failed to start playback");
            PlaybackError?.Invoke(this, ex.Message);
            Stop();
        }
    }

    /// <summary>
    /// Play a local video file on all monitors.
    /// </summary>
    public void PlayFile(string filePath, uint width = 1920, uint height = 1080)
    {
        if (!File.Exists(filePath))
        {
            PlaybackError?.Invoke(this, $"File not found: {filePath}");
            return;
        }

        Play(new Uri(filePath).AbsoluteUri, width, height);
    }

    /// <summary>
    /// Stop playback and clean up resources.
    /// </summary>
    public void Stop()
    {
        // CRITICAL: Invalidate buffer FIRST to stop render loop from using it
        _bufferValid = false;
        _isPlaying = false;
        _frameReady = false;

        Dispatcher.UIThread.Invoke(() => _renderTimer.Stop());

        try
        {
            _mediaPlayer?.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogInformation("AvaloniaDualMonitorVideo: Error stopping media player: {Error}", ex.Message);
        }

        // Wait a bit for LibVLC to fully stop rendering while pumping the UI thread,
        // preventing deadlocks when LibVLC threads need to marshal to the UI thread.
        WaitWithMessagePump(150);

        Dispatcher.UIThread.Invoke(() =>
        {
            foreach (var (window, _, _) in _windowData.ToArray())
            {
                try
                {
                    window.Close();
                }
                catch (Exception ex)
                {
                    _logger.LogInformation("AvaloniaDualMonitorVideo: Error closing window: {Error}", ex.Message);
                }
            }
            _windowData.Clear();
        });

        var playerToDispose = _mediaPlayer;
        _mediaPlayer = null;

        if (playerToDispose != null)
        {
            playerToDispose.Playing -= OnPlaying;
            playerToDispose.EndReached -= OnEndReached;
            playerToDispose.EncounteredError -= OnError;

            _playerDisposeTask = Task.Run(async () =>
            {
                await Task.Delay(500);
                try
                {
                    playerToDispose.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogInformation("AvaloniaDualMonitorVideo: Error disposing media player: {Error}", ex.Message);
                }
            });
        }

        IntPtr bufferToFree;
        lock (_bufferLock)
        {
            bufferToFree = _frameBuffer;
            _frameBuffer = IntPtr.Zero;
        }

        if (bufferToFree != IntPtr.Zero)
        {
            Task.Run(async () =>
            {
                await Task.Delay(500);
                try
                {
                    Marshal.FreeHGlobal(bufferToFree);
                }
                catch (Exception ex)
                {
                    _logger.LogInformation("AvaloniaDualMonitorVideo: Error freeing frame buffer: {Error}", ex.Message);
                }
            });
        }

        _logger.LogInformation("AvaloniaDualMonitorVideo: Playback stopped");
    }

    /// <summary>
    /// Waits for a specified number of milliseconds while continuing to pump the Avalonia dispatcher.
    /// </summary>
    private static void WaitWithMessagePump(int milliseconds)
    {
        var endTime = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < endTime)
        {
            try
            {
                Dispatcher.UIThread.Invoke(
                    () => { },
                    DispatcherPriority.Background);
            }
            catch
            {
                return;
            }
            Thread.Sleep(10);
        }
    }

    /// <summary>
    /// Set the volume (0-100).
    /// </summary>
    public void SetVolume(int volume)
    {
        var player = _mediaPlayer;
        if (player != null)
        {
            try { player.Volume = Math.Clamp(volume, 0, 100); }
            catch (ObjectDisposedException) { }
        }
    }

    /// <summary>
    /// Get or set mute state.
    /// </summary>
    public bool Mute
    {
        get => _mediaPlayer?.Mute ?? false;
        set
        {
            var player = _mediaPlayer;
            if (player != null)
            {
                try { player.Mute = value; }
                catch (ObjectDisposedException) { }
            }
        }
    }

    private void CreateWindows()
    {
        var screens = _screenProvider.GetAllScreens();

        if (screens.Count == 0)
        {
            _logger.LogWarning("AvaloniaDualMonitorVideo: No screens found");
            return;
        }

        _logger.LogInformation("AvaloniaDualMonitorVideo: Creating windows for {Count} screens: {Names}",
            screens.Count, string.Join(", ", screens.Select(s => s.Name)));

        foreach (var screen in screens)
        {
            try
            {
                var bitmap = new WriteableBitmap(
                    new PixelSize((int)_videoWidth, (int)_videoHeight),
                    new Vector(96, 96),
                    global::Avalonia.Platform.PixelFormat.Bgra8888,
                    global::Avalonia.Platform.AlphaFormat.Unpremul);

                var (window, imageControl) = CreateFullscreenWindow(screen, bitmap);
                window.Show();
                _windowData.Add((window, bitmap, imageControl));

                _logger.LogInformation("AvaloniaDualMonitorVideo: Created window on {Screen} at {Bounds}",
                    screen.Name, screen.Bounds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AvaloniaDualMonitorVideo: Failed to create window on {Screen}", screen.Name);
            }
        }

        _logger.LogInformation("AvaloniaDualMonitorVideo: Successfully created {Count} windows", _windowData.Count);
    }

    private (Window Window, Image ImageControl) CreateFullscreenWindow(ScreenInfo screen, WriteableBitmap bitmap)
    {
        var image = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var grid = new Grid
        {
            Background = Brushes.Black,
            Children = { image }
        };

        var window = new Window
        {
            Title = "DualMonitorVideo",
            WindowDecorations = WindowDecorations.None,
            CanResize = false,
            Topmost = true,
            ShowInTaskbar = false,
            Background = Brushes.Black,
            Content = grid
        };
        window.ConstrainToScreen(screen);

        window.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Stop();
            }
        };

        return (window, image);
    }

    #region LibVLC Callbacks

    /// <summary>
    /// LibVLC lock callback - called when LibVLC wants to write a frame.
    /// Returns pointer to our frame buffer.
    /// </summary>
    private IntPtr LockCallback(IntPtr opaque, IntPtr planes)
    {
        lock (_bufferLock)
        {
            if (!_bufferValid || _frameBuffer == IntPtr.Zero)
            {
                Marshal.WriteIntPtr(planes, IntPtr.Zero);
                return IntPtr.Zero;
            }

            Marshal.WriteIntPtr(planes, _frameBuffer);
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// LibVLC display callback - called when a frame is ready to display.
    /// Sets flag for the render loop to pick up.
    /// </summary>
    private void DisplayCallback(IntPtr opaque, IntPtr picture)
    {
        _frameReady = true;
    }

    #endregion

    #region Render Loop

    /// <summary>
    /// Avalonia dispatcher timer callback.
    /// Copies the frame from LibVLC buffer to each window's WriteableBitmap.
    /// </summary>
    private unsafe void OnRenderTick(object? sender, EventArgs e)
    {
        if (!_bufferValid || !_frameReady)
            return;

        var windows = _windowData.ToArray();
        if (windows.Length == 0)
            return;

        _frameReady = false;

        bool lockAcquired = false;
        try
        {
            lockAcquired = Monitor.TryEnter(_bufferLock, 16); // ~1 frame at 60fps
            if (!lockAcquired)
            {
                return;
            }

            if (!_bufferValid || _frameBuffer == IntPtr.Zero)
                return;

            var bufferSize = _videoWidth * _videoHeight * 4;

            foreach (var (_, bitmap, _) in windows)
            {
                try
                {
                    using var framebuffer = bitmap.Lock();
                    var src = _frameBuffer.ToPointer();
                    var dst = framebuffer.Address.ToPointer();
                    Buffer.MemoryCopy(src, dst, bufferSize, bufferSize);
                }
                catch (Exception ex)
                {
                    _logger.LogInformation("AvaloniaDualMonitorVideo: Frame copy error for one window: {Error}", ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogInformation("AvaloniaDualMonitorVideo: Frame copy error: {Error}", ex.Message);
        }
        finally
        {
            if (lockAcquired)
            {
                Monitor.Exit(_bufferLock);
            }
        }
    }

    #endregion

    #region Media Player Events

    private void OnPlaying(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            PlaybackStarted?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
            Stop();
        });
    }

    private void OnError(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            PlaybackError?.Invoke(this, "LibVLC encountered an error during playback");
            Stop();
        });
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();

        if (_playerDisposeTask != null)
        {
            try
            {
                if (!_playerDisposeTask.Wait(2000))
                {
                    _logger.LogWarning("AvaloniaDualMonitorVideo: Player dispose task did not complete within timeout");
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation("AvaloniaDualMonitorVideo: Error waiting for player dispose: {Error}", ex.Message);
            }
        }

        _renderTimer.Tick -= OnRenderTick;

        _logger.LogInformation("AvaloniaDualMonitorVideoService disposed");
    }
}
