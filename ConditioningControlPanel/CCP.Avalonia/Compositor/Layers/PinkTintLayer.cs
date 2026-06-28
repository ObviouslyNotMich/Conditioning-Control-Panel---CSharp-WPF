using System;
using Avalonia;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Core.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// Renders a full-screen pink tint overlay. The service controls the color and opacity;
/// this layer only renders what the service tells it to.
/// </summary>
public sealed class PinkTintLayer : BaseLayer
{
    private Color _color = Colors.Transparent;
    private double _opacity = 0;

    public override int ZIndex => CompositorLayers.PinkTint;

    public override bool IsActive => _opacity > 0;

    public void SetColor(Color color, double opacity)
    {
        _color = color;
        _opacity = Math.Clamp(opacity, 0, 1);
    }

    public override void Update(TimeSpan deltaTime) { }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
    {
        if (_opacity <= 0) return;
        var skColor = new SKColor(_color.R, _color.G, _color.B, (byte)(_opacity * 255));
        using var paint = new SKPaint { Color = skColor };
        canvas.DrawRect(ToSkRect(bounds), paint);
    }
}
