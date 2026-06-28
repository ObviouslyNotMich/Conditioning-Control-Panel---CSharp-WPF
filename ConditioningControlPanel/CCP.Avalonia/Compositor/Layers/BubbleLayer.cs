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
/// Renders ambient and chaos bubbles on the compositor. Basic circles with labels,
/// tints, and opacity. The BubbleService drives position, scale, and lifecycle updates.
/// </summary>
public sealed class BubbleLayer : BaseLayer
{
    private readonly List<BubbleItem> _items = new();
    private readonly object _sync = new();

    public override int ZIndex => CompositorLayers.Bubbles;

    public override bool IsActive
    {
        get
        {
            lock (_sync) { return _items.Count > 0; }
        }
    }

    public void AddBubble(Guid id, double x, double y, double size, double opacity, double scale,
        string? label = null, (byte r, byte g, byte b)? tint = null, bool clickable = true)
    {
        lock (_sync)
        {
            _items.RemoveAll(i => i.Id == id);
            _items.Add(new BubbleItem(id, x, y, size, opacity, scale, label, tint, clickable));
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

    /// <summary>Hit-test in reverse order (topmost first). Returns the hit bubble id or empty.</summary>
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

    public override void Update(TimeSpan deltaTime)
    {
        // Service drives updates; layer just handles cleanup of stale items
    }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
    {
        List<BubbleItem> snapshot;
        lock (_sync) { snapshot = _items.ToList(); }
        if (snapshot.Count == 0) return;

        foreach (var item in snapshot)
        {
            if (item.Opacity <= 0) continue;

            var half = (float)(item.Size * item.Scale / 2.0);
            var cx = (float)(item.X + half);
            var cy = (float)(item.Y + half);
            var radius = half;

            // Bubble fill
            byte alpha = (byte)(item.Opacity * 255);
            var (r, g, b) = item.Tint ?? (0xFF, 0x69, 0xB4); // default hot pink
            using var fillPaint = new SKPaint
            {
                Color = new SKColor(r, g, b, alpha),
                IsAntialias = true
            };
            canvas.DrawCircle(cx, cy, radius, fillPaint);

            // Border
            using var strokePaint = new SKPaint
            {
                Color = new SKColor(255, 255, 255, (byte)(alpha * 0.8)),
                IsStroke = true,
                StrokeWidth = 2,
                IsAntialias = true
            };
            canvas.DrawCircle(cx, cy, radius, strokePaint);

            // Fuse ring (if active)
            if (item.FuseFraction > 0 && item.FuseFraction < 1)
            {
                using var fusePaint = new SKPaint
                {
                    Color = new SKColor(255, 255, 0, alpha),
                    IsStroke = true,
                    StrokeWidth = 4,
                    IsAntialias = true
                };
                var fuseRadius = radius * (1 - (float)item.FuseFraction);
                canvas.DrawCircle(cx, cy, fuseRadius, fusePaint);
            }

            // Label
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
        public (byte r, byte g, byte b)? Tint { get; set; }
        public bool Clickable { get; }
        public double FuseFraction { get; set; }

        public BubbleItem(Guid id, double x, double y, double size, double opacity, double scale,
            string? label, (byte r, byte g, byte b)? tint, bool clickable)
        {
            Id = id;
            X = x;
            Y = y;
            Size = size;
            Opacity = opacity;
            Scale = scale;
            Label = label;
            Tint = tint;
            Clickable = clickable;
        }
    }
}
