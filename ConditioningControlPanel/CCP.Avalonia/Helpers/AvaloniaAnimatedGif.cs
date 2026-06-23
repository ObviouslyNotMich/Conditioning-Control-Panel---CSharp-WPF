using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Helpers;

/// <summary>
/// Cross-platform animated GIF renderer for Avalonia.
/// Decodes frames with SkiaSharp and writes them into a <see cref="WriteableBitmap"/>
/// so the result can be assigned directly to <see cref="Avalonia.Controls.Image.Source"/>.
/// </summary>
public sealed class AvaloniaAnimatedGif : IDisposable
{
    private readonly WriteableBitmap _bitmap;
    private readonly SKCodec _codec;
    private readonly SKCodecFrameInfo[] _frames;
    private readonly SKBitmap _frameBuffer;
    private readonly DispatcherTimer _timer = new();

    private int _frameIndex;
    private int _remainingLoops = -1;
    private bool _disposed;
    private bool _playOnce;
    private bool _decodePending;

    /// <summary>The Avalonia image source to bind to an <see cref="Avalonia.Controls.Image"/>.</summary>
    public IImage Source => _bitmap;

    /// <summary>True when the clip has finished playing (play-once mode).</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Fired after each frame is rendered so the hosting <see cref="Image"/> can be invalidated.</summary>
    public event EventHandler? FrameRendered;

    /// <summary>Fired once when the clip finishes playing (play-once mode).</summary>
    public event EventHandler? Completed;

    /// <summary>Attempt to create an animated GIF renderer for <paramref name="path"/>.</summary>
    /// <returns>The renderer, or <c>null</c> if the file is missing, not a GIF, or has no animation.</returns>
    public static AvaloniaAnimatedGif? TryCreate(string path, bool playOnce = false)
    {
        try
        {
            if (!File.Exists(path) || !path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                return null;

            var codec = SKCodec.Create(path);
            if (codec == null || codec.FrameCount <= 1)
            {
                codec?.Dispose();
                return null;
            }

            return new AvaloniaAnimatedGif(codec, playOnce);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Attempt to create an animated GIF renderer from <paramref name="stream"/>.</summary>
    /// <returns>The renderer, or <c>null</c> if the stream is empty, not a GIF, or has no animation.</returns>
    public static AvaloniaAnimatedGif? TryCreate(Stream stream, bool playOnce = false)
    {
        try
        {
            if (stream == null || !stream.CanRead)
                return null;

            var codec = SKCodec.Create(stream);
            if (codec == null || codec.FrameCount <= 1)
            {
                codec?.Dispose();
                return null;
            }

            return new AvaloniaAnimatedGif(codec, playOnce);
        }
        catch
        {
            return null;
        }
    }

    private AvaloniaAnimatedGif(SKCodec codec, bool playOnce)
    {
        _codec = codec;
        _playOnce = playOnce;
        if (codec.FrameCount > 0)
        {
            _frames = new SKCodecFrameInfo[codec.FrameCount];
            for (int i = 0; i < _frames.Length; i++)
                codec.GetFrameInfo(i, out _frames[i]);
        }
        else
        {
            _frames = Array.Empty<SKCodecFrameInfo>();
        }

        var info = codec.Info
            .WithColorType(SKColorType.Bgra8888)
            .WithAlphaType(SKAlphaType.Premul);

        _frameBuffer = new SKBitmap(info);
        _bitmap = new WriteableBitmap(
            new PixelSize(info.Width, info.Height),
            new Vector(96, 96),
            global::Avalonia.Platform.PixelFormat.Bgra8888,
            global::Avalonia.Platform.AlphaFormat.Premul);

        _remainingLoops = playOnce ? 1 : Math.Max(-1, codec.RepetitionCount);

        _timer.Tick += OnTick;
        RenderFrame();
    }

    /// <summary>Start animating. Safe to call multiple times.</summary>
    public void Start()
    {
        if (_disposed || _frames.Length == 0) return;
        if (!_timer.IsEnabled) _timer.Start();
    }

    /// <summary>Stop animating without disposing.</summary>
    public void Stop()
    {
        _timer.Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_disposed || _decodePending) return;
        Advance();
    }

    private void Advance()
    {
        if (_frames.Length == 0) return;

        _frameIndex++;
        if (_frameIndex >= _frames.Length)
        {
            _frameIndex = 0;
            if (_remainingLoops > 0)
            {
                _remainingLoops--;
                if (_remainingLoops == 0)
                {
                    IsComplete = true;
                    _timer.Stop();
                    try { Completed?.Invoke(this, EventArgs.Empty); } catch { }
                    return;
                }
            }
            else if (_playOnce)
            {
                IsComplete = true;
                _timer.Stop();
                try { Completed?.Invoke(this, EventArgs.Empty); } catch { }
                return;
            }
        }

        RenderFrameAsync();
    }

    private void RenderFrame()
    {
        // Initial frame is rendered synchronously on the caller; subsequent frames are
        // decoded on the thread pool to keep the UI thread responsive.
        RenderFrameCore();
    }

    private void RenderFrameAsync()
    {
        _decodePending = true;
        var prior = _frameIndex == 0 ? -1 : _frameIndex - 1;
        var options = new SKCodecOptions(_frameIndex) { PriorFrame = prior };
        var duration = Math.Max(20, _frames[_frameIndex].Duration);

        _ = Task.Run(() =>
        {
            try
            {
                if (_disposed) return;
                var result = _codec.GetPixels(_frameBuffer.Info, _frameBuffer.GetPixels(), options);
                Dispatcher.UIThread.Post(() => ApplyFrame(result, duration));
            }
            catch
            {
                Dispatcher.UIThread.Post(() => _decodePending = false);
            }
        });
    }

    private void ApplyFrame(SKCodecResult result, int durationMs)
    {
        try
        {
            if (_disposed) return;
            if (result == SKCodecResult.Success || result == SKCodecResult.IncompleteInput)
            {
                CopyToWriteableBitmap(_frameBuffer, _bitmap);
                try { FrameRendered?.Invoke(this, EventArgs.Empty); } catch { /* ignore invalidation failures */ }
            }
            _timer.Interval = TimeSpan.FromMilliseconds(durationMs);
        }
        finally
        {
            _decodePending = false;
        }
    }

    private void RenderFrameCore()
    {
        if (_frames.Length == 0) return;

        var prior = _frameIndex == 0 ? -1 : _frameIndex - 1;
        var options = new SKCodecOptions(_frameIndex) { PriorFrame = prior };
        var result = _codec.GetPixels(_frameBuffer.Info, _frameBuffer.GetPixels(), options);
        ApplyFrame(result, Math.Max(20, _frames[_frameIndex].Duration));
    }

    private static unsafe void CopyToWriteableBitmap(SKBitmap src, WriteableBitmap dst)
    {
        using var fb = dst.Lock();
        var source = (byte*)src.GetPixels().ToPointer();
        var dest = (byte*)fb.Address.ToPointer();
        var rowBytes = Math.Min(src.RowBytes, fb.RowBytes);
        var height = Math.Min(src.Height, fb.Size.Height);

        for (int y = 0; y < height; y++)
        {
            Buffer.MemoryCopy(
                source + y * src.RowBytes,
                dest + y * fb.RowBytes,
                rowBytes,
                rowBytes);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _frameBuffer.Dispose();
        _codec.Dispose();
    }
}
