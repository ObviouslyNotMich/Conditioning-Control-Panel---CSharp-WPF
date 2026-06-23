using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using LibVLCSharp.Shared;

using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Controls;

/// <summary>
/// Avalonia port of <c>InlineLoopVideo</c>: a muted, looping clip surface.
///
/// Uses <see cref="AvaloniaAnimatedGif"/> for animated GIF files and the existing
/// <see cref="AvaloniaInlineLoopVideo"/> (LibVLC memory-callback renderer) for
/// video files, so it composites cleanly inside transparent popups.
///
/// Lifecycle: <see cref="Resume"/> on show, <see cref="Pause"/> on hide,
/// <see cref="Dispose"/> on teardown. Fail-soft: if LibVLC is unavailable or
/// the clip is missing, the surface simply stays blank.
/// </summary>
public sealed class InlineLoopVideo : IDisposable
{
    private readonly ILogger<InlineLoopVideo>? _logger;


    private readonly AvaloniaInlineLoopVideo? _video;
    private readonly AvaloniaAnimatedGif? _gif;
    private readonly Image _image;
    private bool _disposed;

    public InlineLoopVideo(string clipPath, uint width = 480, uint height = 270)
    {
        _logger = App.Services.GetRequiredService<ILogger<InlineLoopVideo>>();

_image = new Image
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        if (string.IsNullOrEmpty(clipPath) || !File.Exists(clipPath))
            return;

        // Prefer the lightweight GIF renderer for animated GIFs.
        if (clipPath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
        {
            _gif = AvaloniaAnimatedGif.TryCreate(clipPath);
            if (_gif != null)
                _image.Source = _gif.Source;
            return;
        }

        // Fall back to LibVLC for video clips.
        try
        {
            var libVlc =
App.Services.GetService<LibVLC>();
            if (libVlc != null)
                _video = new AvaloniaInlineLoopVideo(libVlc, clipPath, width, height);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "InlineLoopVideo: could not create LibVLC surface for {Path}", clipPath);
        }
    }

    /// <summary>The Avalonia element to place in the layout.</summary>
    public Control Surface => _video?.Surface ?? _image;

    /// <summary>Start (first call) or resume decoding into the surface.</summary>
    public void Resume()
    {
        if (_disposed) return;
        _video?.Resume();
        _gif?.Start();
    }

    /// <summary>Stop decoding but keep the player alive for a later <see cref="Resume"/>.</summary>
    public void Pause()
    {
        if (_disposed) return;
        _video?.Pause();
        _gif?.Stop();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _video?.Dispose();
        _gif?.Dispose();
    }
}
