using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Core.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// Renders a spiral image overlay. Supports static images and animated GIFs.
/// The service controls the source path and opacity.
/// </summary>
public sealed class SpiralLayer : BaseLayer, IDisposable
{
    private Bitmap? _bitmap;
    private SKImage? _skImage;
    private double _opacity = 0;
    private string? _lastPath;
    private double _rotationAngle; // degrees
    private const double RotationSpeed = 30.0; // degrees per second

    public override int ZIndex => CompositorLayers.Spiral;

    public override bool IsActive => _opacity > 0 && _skImage != null;

    public void SetSource(string? path, double opacity)
    {
        _opacity = Math.Clamp(opacity, 0, 1);
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            ClearSource();
            return;
        }
        if (string.Equals(path, _lastPath, StringComparison.OrdinalIgnoreCase)) return;

        ClearSource();
        _lastPath = path;

        try
        {
            using var stream = System.IO.File.OpenRead(path);
            _skImage = SKImage.FromEncodedData(stream);
            if (_skImage == null)
            {
                ClearSource();
            }
        }
        catch (Exception)
        {
            ClearSource();
        }
    }

    public void ClearSource()
    {
        _skImage?.Dispose();
        _skImage = null;
        _bitmap?.Dispose();
        _bitmap = null;
        _lastPath = null;
        _rotationAngle = 0;
    }

    public override void Update(TimeSpan deltaTime)
    {
        if (_opacity <= 0 || _skImage == null) return;
        _rotationAngle += RotationSpeed * deltaTime.TotalSeconds;
        if (_rotationAngle >= 360) _rotationAngle -= 360;
    }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
    {
        if (_skImage == null || _opacity <= 0) return;

        // Get the image's native dimensions.
        var imgW = (float)_skImage.Width;
        var imgH = (float)_skImage.Height;
        if (imgW <= 0 || imgH <= 0) return;

        // Cover the whole monitor. Because we rotate the image about its centre,
        // a plain scale-to-cover would expose the corners as the rectangle spins,
        // so we size the image to the screen *diagonal*: that guarantees full
        // coverage at every rotation angle. The image is centred on the monitor.
        var boundsW = (float)bounds.Width;
        var boundsH = (float)bounds.Height;
        var diagonal = MathF.Sqrt(boundsW * boundsW + boundsH * boundsH);
        var scale = diagonal / Math.Min(imgW, imgH);

        var destW = imgW * scale;
        var destH = imgH * scale;
        var centerX = (float)bounds.X + boundsW / 2f;
        var centerY = (float)bounds.Y + boundsH / 2f;
        var destX = centerX - destW / 2f;
        var destY = centerY - destH / 2f;

        canvas.Save();
        canvas.Translate(centerX, centerY);
        canvas.RotateDegrees((float)_rotationAngle);
        canvas.Translate(-centerX, -centerY);

        using var paint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, (byte)(_opacity * 255))
        };
        var dest = new SKRect(destX, destY, destX + destW, destY + destH);
        canvas.DrawImage(_skImage, dest, paint);
        canvas.Restore();
    }

    public void Dispose()
    {
        ClearSource();
    }
}
