using System;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Core.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// Renders a brain-drain blur/distortion overlay. The service controls the intensity;
/// this layer renders a dark violet pulsing tint based on the current intensity.
/// </summary>
public sealed class BrainDrainLayer : BaseLayer
{
    private int _intensity = 0;
    private double _pulsePhase = 0;

    public override int ZIndex => CompositorLayers.BrainDrain;

    public override bool IsActive => _intensity > 0;

    public void SetIntensity(int intensity)
    {
        _intensity = intensity;
    }

    public override void Update(TimeSpan deltaTime)
    {
        if (_intensity > 0)
        {
            _pulsePhase += deltaTime.TotalSeconds * 2.0;
            if (_pulsePhase > Math.PI * 2) _pulsePhase -= Math.PI * 2;
        }
    }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
    {
        if (_intensity <= 0) return;
        var baseAlpha = Math.Clamp(_intensity / 100.0, 0, 1);
        var pulse = 0.85 + 0.15 * Math.Sin(_pulsePhase); // subtle 85%-100% pulse
        var alpha = (byte)Math.Clamp(baseAlpha * pulse * 255, 0, 255);
        using var paint = new SKPaint { Color = new SKColor(20, 0, 40, alpha) };
        canvas.DrawRect(ToSkRect(bounds), paint);
    }
}
