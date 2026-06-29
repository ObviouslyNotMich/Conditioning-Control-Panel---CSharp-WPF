using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Core.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// Renders ambient and chaos bubbles on the compositor as a single Skia surface (UCE approach).
/// All bubbles — ambient (Bubble Pop dashboard game) and chaos (Down the Rabbit Hole) — render
/// through this layer. No per-window bubble windows.
///
/// Visual contract:
/// - Ambient bubbles (isChaos == false): uniform bubble image, no tint, no fuse ring.
///   Every ambient bubble looks identical.
/// - Chaos bubbles (isChaos == true): bubble image + variant tint overlay + optional label.
///   Live chaos bubbles additionally draw a shrinking fuse ring (the defuse countdown).
/// The BubbleService drives position, scale, and lifecycle via the IBubbleRenderer callbacks.
/// </summary>
public sealed class BubbleLayer : BaseLayer, IDisposable
{
    private readonly List<BubbleItem> _items = new();
    private readonly object _sync = new();

    // The bubble image is decoded ONCE, lazily, on first render and cached for the app lifetime.
    // It is NEVER replaced or disposed while the app runs. This is critical: an SKImage is a
    // native Skia handle, and sharing a mutable/disposable SKImage across the UI thread and the
    // render thread causes a use-after-free (the render thread draws a handle the UI thread just
    // disposed), which corrupts the native heap and faults with a non-deterministic access
    // violation (0xC0000005). See AvaloniaUI/Avalonia#13521. An immutable, never-freed native
    // handle is safe for concurrent reads from any thread, so we treat _bubbleImage as a
    // write-once cached field with no teardown.
    private SKImage? _bubbleImage;
    private bool _imageLoadAttempted;
    private bool _disposed;

    public override int ZIndex => CompositorLayers.Bubbles;

    public override bool IsActive
    {
        get { lock (_sync) { return _items.Count > 0; } }
    }

    /// <summary>
    /// Lazily decodes the bubble.png asset into an immutable SKImage cached for the app lifetime.
    /// Called from the render path. Safe to call from any thread; the decode is gated by
    /// <see cref="_imageLoadAttempted"/> so it runs at most once. The resulting image is never
    /// disposed (intentional — see the field comment above) which keeps the native handle valid
    /// for every subsequent render.
    /// </summary>
    private SKImage? EnsureBubbleImage()
    {
        if (_bubbleImage != null || _imageLoadAttempted) return _bubbleImage;
        _imageLoadAttempted = true;
        try
        {
            using var stream = global::Avalonia.Platform.AssetLoader.Open(
                new Uri("avares://CCP.Avalonia/Assets/bubble.png"));
            _bubbleImage = stream != null ? SKImage.FromEncodedData(stream) : null;
        }
        catch
        {
            _bubbleImage = null;
        }
        return _bubbleImage;
    }

    public void AddBubble(Guid id, double x, double y, double size, double opacity, double scale,
        string? label, (byte r, byte g, byte b)? tint, bool isChaos, double fuseFraction, bool clickable)
    {
        lock (_sync)
        {
            _items.RemoveAll(i => i.Id == id);
            _items.Add(new BubbleItem(id, x, y, size, opacity, scale, label, tint, isChaos,
                Math.Clamp(fuseFraction, 0, 1), clickable));
        }
    }

    public void UpdateBubble(Guid id, double x, double y, double opacity, double scale)
    {
        lock (_sync)
        {
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item != null)
            {
                item.X = x;
                item.Y = y;
                item.Opacity = opacity;
                item.Scale = scale;
            }
        }
    }

    public void SetLabel(Guid id, string label)
    {
        lock (_sync)
        {
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item != null) item.Label = label;
        }
    }

    public void SetFuse(Guid id, double fraction)
    {
        lock (_sync)
        {
            var item = _items.FirstOrDefault(i => i.Id == id);
            if (item != null) item.FuseFraction = Math.Clamp(fraction, 0, 1);
        }
    }

    public void RemoveBubble(Guid id)
    {
        lock (_sync)
        {
            _items.RemoveAll(i => i.Id == id);
        }
    }

    public void Clear()
    {
        lock (_sync) { _items.Clear(); }
    }

    /// <summary>Hit-test a single point in reverse z-order (topmost first). Returns the hit id or empty.</summary>
    public Guid HitTest(double x, double y)
    {
        lock (_sync)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var item = _items[i];
                if (!item.Clickable) continue;
                var half = item.Size * item.Scale / 2.0;
                var dx = x - (item.X + half);
                var dy = y - (item.Y + half);
                if (dx * dx + dy * dy <= half * half)
                    return item.Id;
            }
        }
        return Guid.Empty;
    }

    /// <summary>Return the ids of all clickable bubbles whose bounds intersect the given rect (DIPs).</summary>
    public List<Guid> HitTestInRect(ConditioningControlPanel.Core.Platform.PixelRect rect)
    {
        lock (_sync)
        {
            return _items
                .Where(i => i.Clickable)
                .Where(i =>
                {
                    var half = i.Size * i.Scale / 2.0;
                    var cx = i.X + half;
                    var cy = i.Y + half;
                    // Circle-vs-rect intersection: closest point on rect to circle centre.
                    var closestX = Math.Max(rect.X, Math.Min(cx, rect.Right));
                    var closestY = Math.Max(rect.Y, Math.Min(cy, rect.Bottom));
                    var dx = cx - closestX;
                    var dy = cy - closestY;
                    return dx * dx + dy * dy <= half * half;
                })
                .Select(i => i.Id)
                .ToList();
        }
    }

    public override void Update(TimeSpan deltaTime)
    {
        // Service drives updates; layer is a thin render adapter (UCE rule: layer state is service-owned).
    }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
    {
        List<BubbleItem> snapshot;
        lock (_sync) { snapshot = _items.ToList(); }
        if (snapshot.Count == 0) return;

        // Decode-once, immutable, never-freed: safe to read from the render thread.
        var image = EnsureBubbleImage();
        foreach (var item in snapshot)
        {
            if (item.Opacity <= 0) continue;
            RenderBubble(canvas, item, image);
        }
    }

    private static void RenderBubble(SKCanvas canvas, BubbleItem item, SKImage? image)
    {
        byte alpha = (byte)(item.Opacity * 255);
        var half = (float)(item.Size * item.Scale / 2.0);
        var cx = (float)(item.X + half);
        var cy = (float)(item.Y + half);
        var radius = half;
        var dest = new SKRect(cx - radius, cy - radius, cx + radius, cy + radius);

        // 1. Bubble image (uniform look for all bubbles). When the asset is unavailable,
        //    fall back to a soft circle so the game is still playable.
        if (image != null)
        {
            using var imgPaint = new SKPaint
            {
                Color = new SKColor(255, 255, 255, alpha),
                IsAntialias = true,
                FilterQuality = SKFilterQuality.Medium
            };
            canvas.DrawImage(image, dest, imgPaint);
        }
        else
        {
            // Fallback: white circle with a subtle border.
            (byte r, byte g, byte b) fb = item.IsChaos
                ? (item.Tint ?? ((byte)0xFF, (byte)0x69, (byte)0xB4))
                : ((byte)0xE8, (byte)0xE8, (byte)0xF0);
            using var fillPaint = new SKPaint
            {
                Color = new SKColor(fb.r, fb.g, fb.b, alpha),
                IsAntialias = true
            };
            canvas.DrawCircle(cx, cy, radius, fillPaint);
        }

        // 2. Chaos variant tint overlay — only chaos bubbles get coloured. Ambient bubbles
        //    stay uniform (the plain bubble image), satisfying the "all bubbles look the same
        //    outside the Down the Rabbit Hole game" contract.
        if (item.IsChaos && item.Tint is { } tint)
        {
            using var tintPaint = new SKPaint
            {
                Color = new SKColor(tint.r, tint.g, tint.b, (byte)(alpha * 0.45)),
                IsAntialias = true
            };
            canvas.DrawCircle(cx, cy, radius, tintPaint);
        }

        // 3. White border ring for definition.
        using var strokePaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, (byte)(alpha * 0.8)),
            IsStroke = true,
            StrokeWidth = 2,
            IsAntialias = true
        };
        canvas.DrawCircle(cx, cy, radius, strokePaint);

        // 4. Fuse ring (shrinking countdown) — only live chaos bubbles. Ambient bubbles never show this.
        if (item.IsChaos && item.FuseFraction > 0 && item.FuseFraction < 1)
        {
            using var fusePaint = new SKPaint
            {
                Color = new SKColor(0xFF, 0xFF, 0x00, alpha),
                IsStroke = true,
                StrokeWidth = 4,
                IsAntialias = true
            };
            var fuseRadius = radius * (1 - (float)item.FuseFraction);
            if (fuseRadius > 0)
                canvas.DrawCircle(cx, cy, fuseRadius, fusePaint);
        }

        // 5. Label (chaos treat bubbles carry a short word).
        if (!string.IsNullOrEmpty(item.Label))
        {
            using var textPaint = new SKPaint
            {
                Color = new SKColor(255, 255, 255, alpha),
                TextSize = Math.Min((float)item.Size * 0.4f, 18),
                IsAntialias = true,
                TextAlign = SKTextAlign.Center
            };
            canvas.DrawText(item.Label, cx, cy + textPaint.TextSize / 3, textPaint);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_sync)
        {
            // NOTE: _bubbleImage is intentionally NOT disposed here. It is an immutable,
            // write-once cached native handle (see EnsureBubbleImage). Disposing it could free a
            // native handle the render thread is still drawing (use-after-free → heap corruption,
            // AvaloniaUI/Avalonia#13521). It leaks exactly one SKImage for the app lifetime, which
            // is acceptable and far safer than racing on its lifetime.
            _items.Clear();
        }
    }

    private sealed class BubbleItem
    {
        public Guid Id { get; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Size { get; }
        public double Opacity { get; set; }
        public double Scale { get; set; }
        public string? Label { get; set; }
        public (byte r, byte g, byte b)? Tint { get; }
        public bool IsChaos { get; }
        public double FuseFraction { get; set; }
        public bool Clickable { get; }

        public BubbleItem(Guid id, double x, double y, double size, double opacity, double scale,
            string? label, (byte r, byte g, byte b)? tint, bool isChaos, double fuseFraction, bool clickable)
        {
            Id = id;
            X = x;
            Y = y;
            Size = size;
            Opacity = opacity;
            Scale = scale;
            Label = label;
            Tint = tint;
            IsChaos = isChaos;
            FuseFraction = fuseFraction;
            Clickable = clickable;
        }
    }
}
