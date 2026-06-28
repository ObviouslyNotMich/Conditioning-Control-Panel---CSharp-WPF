using System;
using Avalonia;
using ConditioningControlPanel.Core.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// A test layer that fills the entire screen with a configurable color.
/// Used to prove the compositor pipeline works end-to-end before real layers are migrated.
/// </summary>
public sealed class PlaceholderLayer : BaseLayer
{
    private readonly int _zIndex;
    private readonly SKColor _color;
    private double _phase;

    public PlaceholderLayer(int zIndex, SKColor color)
    {
        _zIndex = zIndex;
        _color = color;
    }

    public override int ZIndex => _zIndex;

    public override void Update(TimeSpan deltaTime)
    {
        // Slowly cycle opacity to prove animation works
        _phase += deltaTime.TotalSeconds * 0.5;
        if (_phase > Math.PI * 2) _phase -= Math.PI * 2;
    }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
    {
        var alpha = (byte)(128 + 127 * Math.Sin(_phase)); // 1..255 cycling
        using var paint = new SKPaint { Color = _color.WithAlpha(alpha) };
        canvas.DrawRect(ToSkRect(bounds), paint);
    }
}
