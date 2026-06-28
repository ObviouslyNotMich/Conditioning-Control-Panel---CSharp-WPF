using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace ConditioningControlPanel.Avalonia.Controls;

/// <summary>
/// Avalonia port of <c>InlineLoopVideo</c>: a muted, looping video surface that renders
/// LibVLC frames into an Avalonia <see cref="Image"/> via memory callbacks, so it
/// composites cleanly inside any Avalonia visual tree (including transparent popups).
/// </summary>
public sealed class AvaloniaInlineLoopVideo : IDisposable
{
    private readonly string _path;
    private readonly uint _w;
    private readonly uint _h;
    private readonly Image _image;
    private readonly WriteableBitmap _bitmap;
    private readonly object _bufferLock = new();

    private IntPtr _frameBuffer = IntPtr.Zero;
    private volatile bool _frameReady;
    private volatile bool _bufferValid;

    private readonly LibVLC _libVlc;
    private VlcMediaPlayer? _player;
    private Media? _media;
    private bool _started;
    private readonly DispatcherTimer _renderTimer;
    private bool _disposed;

    public AvaloniaInlineLoopVideo(LibVLC libVlc, string clipPath, uint width = 480, uint height = 270)
    {
        _libVlc = libVlc;
        _path = clipPath;
        _w = width;
        _h = height;
        _renderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _renderTimer.Tick += OnRenderTick;
        _bitmap = new WriteableBitmap(
            new PixelSize((int)_w, (int)_h),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);
        _image = new Image
        {
            Source = _bitmap,
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
    }

    /// <summary>The Avalonia element to place in the layout.</summary>
    public Control Surface => _image;

    /// <summary>Start (first call) or resume decoding into the surface.</summary>
    public void Resume()
    {
        if (_disposed) return;
        if (!_started)
        {
            _started = TryStart();
            return;
        }
        try { _player?.SetPause(false); } catch { /* disposed/native race */ }
        HookRendering();
    }

    /// <summary>Stop decoding but keep the player alive for a later <see cref="Resume"/>.</summary>
    public void Pause()
    {
        if (_disposed || !_started) return;
        UnhookRendering();
        try { _player?.SetPause(true); } catch { /* disposed/native race */ }
    }

    private bool TryStart()
    {
        if (string.IsNullOrEmpty(_path) || !File.Exists(_path)) return false;

        try
        {
            var bufferSize = _w * _h * 4;
            lock (_bufferLock)
            {
                _frameBuffer = Marshal.AllocHGlobal((int)bufferSize);
                _bufferValid = true;
            }

            _player = new VlcMediaPlayer(_libVlc) { Mute = true, EnableHardwareDecoding = true };
            _player.SetVideoFormat("RV32", _w, _h, _w * 4);
            _player.SetVideoCallbacks(LockCallback, null, DisplayCallback);
            _player.EndReached += OnEndReached;

            _media = new Media(_libVlc, _path, FromType.FromPath);
            _media.AddOption(":input-repeat=65535");
            _media.AddOption(":no-audio");

            HookRendering();
            _player.Play(_media);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AvaloniaInlineLoopVideo: failed to start {_path}: {ex.Message}");
            TeardownPlayer();
            return false;
        }
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || _media == null) return;
            try { _player?.Stop(); _player?.Play(_media); } catch { /* ignore */ }
        });
    }

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

    private void DisplayCallback(IntPtr opaque, IntPtr picture) => _frameReady = true;

    private void HookRendering()
    {
        if (_renderTimer.IsEnabled) return;
        _renderTimer.Start();
    }

    private void UnhookRendering()
    {
        if (!_renderTimer.IsEnabled) return;
        _renderTimer.Stop();
    }

    private unsafe void OnRenderTick(object? sender, EventArgs e)
    {
        if (!_bufferValid || !_frameReady) return;
        _frameReady = false;

        bool got = false;
        try
        {
            got = Monitor.TryEnter(_bufferLock, 8);
            if (!got) return;
            if (!_bufferValid || _frameBuffer == IntPtr.Zero) return;

            using var framebuffer = _bitmap.Lock();
            var dest = framebuffer.Address;
            var src = _frameBuffer.ToPointer();
            var dst = dest.ToPointer();
            int rowBytes = framebuffer.RowBytes;
            int copyBytes = (int)Math.Min(_w * 4, rowBytes) * (int)_h;
            Buffer.MemoryCopy(src, dst, copyBytes, copyBytes);
            _image.InvalidateVisual();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AvaloniaInlineLoopVideo: frame copy error: {ex.Message}");
        }
        finally
        {
            if (got) Monitor.Exit(_bufferLock);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TeardownPlayer();
    }

    private void TeardownPlayer()
    {
        _bufferValid = false;
        _frameReady = false;
        UnhookRendering();
        _renderTimer.Tick -= OnRenderTick;

        var player = _player;
        _player = null;
        var media = _media;
        _media = null;
        if (player != null) player.EndReached -= OnEndReached;

        try { player?.Stop(); } catch { /* ignore */ }

        IntPtr buf;
        lock (_bufferLock)
        {
            buf = _frameBuffer;
            _frameBuffer = IntPtr.Zero;
        }

        Task.Run(async () =>
        {
            await Task.Delay(400);
            try { player?.Dispose(); } catch { /* ignore */ }
            try { media?.Dispose(); } catch { /* ignore */ }
            if (buf != IntPtr.Zero)
            {
                try { Marshal.FreeHGlobal(buf); } catch { /* ignore */ }
            }
        });
    }
}
