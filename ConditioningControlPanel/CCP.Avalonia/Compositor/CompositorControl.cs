using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using ConditioningControlPanel.Core.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor;

/// <summary>
/// Custom Avalonia control that renders the compositor layers directly via Skia
/// using <see cref="ICustomDrawOperation"/>. This is the proper Avalonia pattern
/// for high-performance Skia rendering — no WriteableBitmap, no Image control,
/// no manual invalidation hacks.
/// </summary>
public class CompositorControl : Control
{
    private readonly CompositorEngine _engine;

    public CompositorControl(CompositorEngine engine)
    {
        _engine = engine;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        context.Custom(new CompositorDrawOp(_engine, bounds));
    }
}

/// <summary>
/// Skia draw operation that renders all active compositor layers.
/// Runs on the render thread via <see cref="ISkiaSharpApiLeaseFeature"/>.
/// </summary>
public class CompositorDrawOp : ICustomDrawOperation
{
    private readonly CompositorEngine _engine;
    private readonly Rect _bounds;

    public CompositorDrawOp(CompositorEngine engine, Rect bounds)
    {
        _engine = engine;
        _bounds = bounds;
    }

    public Rect Bounds => _bounds;
    public bool HitTest(global::Avalonia.Point p) => false;

    public void Render(ImmediateDrawingContext context)
    {
        var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (leaseFeature == null) return;

        using var lease = leaseFeature.Lease();
        var canvas = lease.SkCanvas;
        var bounds = new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, (int)_bounds.Width, (int)_bounds.Height);
        _engine.RenderToCanvas(canvas, bounds);
    }

    public void Dispose() { }
    public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);
    public override bool Equals(object? obj) => obj is ICustomDrawOperation other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_engine, _bounds);
}
