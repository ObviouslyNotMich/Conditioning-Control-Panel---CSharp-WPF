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
/// Renders subliminal text flashes on the compositor. Supports multiple concurrent flashes
/// (queued), configurable colors, background opacity, and duration.
/// </summary>
public sealed class SubliminalLayer : BaseLayer
{
    private readonly List<SubliminalItem> _items = new();
    private readonly object _sync = new();

    public override int ZIndex => CompositorLayers.Subliminal;

    public override bool IsActive
    {
        get
        {
            lock (_sync) { return _items.Count > 0; }
        }
    }

    /// <summary>Queue a new subliminal flash. Multiple flashes can be active concurrently.</summary>
    public void Flash(string text, Color bgColor, Color textColor, int durationMs, bool bgTransparent)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (_sync)
        {
            _items.Add(new SubliminalItem(text, bgColor, textColor, durationMs, bgTransparent));
        }
    }

    public void Clear()
    {
        lock (_sync) { _items.Clear(); }
    }

    public override void Update(TimeSpan deltaTime)
    {
        lock (_sync)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                _items[i].Remaining -= deltaTime;
                if (_items[i].Remaining <= TimeSpan.Zero)
                    _items.RemoveAt(i);
            }
        }
    }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
    {
        List<SubliminalItem> snapshot;
        lock (_sync) { snapshot = _items.ToList(); }
        if (snapshot.Count == 0) return;

        // Draw most recent on top (last in list)
        var item = snapshot[^1];

        if (!item.BgTransparent)
        {
            using var bgPaint = new SKPaint
            {
                Color = new SKColor(item.BgColor.R, item.BgColor.G, item.BgColor.B, 180)
            };
            canvas.DrawRect(ToSkRect(bounds), bgPaint);
        }

        using var textPaint = new SKPaint
        {
            Color = new SKColor(item.TextColor.R, item.TextColor.G, item.TextColor.B),
            TextSize = 120,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center
        };

        var centerX = bounds.X + bounds.Width / 2.0f;
        var centerY = bounds.Y + bounds.Height / 2.0f;
        canvas.DrawText(item.Text, (float)centerX, (float)centerY + textPaint.TextSize / 3, textPaint);
    }

    private sealed class SubliminalItem
    {
        public string Text { get; }
        public Color BgColor { get; }
        public Color TextColor { get; }
        public bool BgTransparent { get; }
        public TimeSpan Remaining { get; set; }

        public SubliminalItem(string text, Color bgColor, Color textColor, int durationMs, bool bgTransparent)
        {
            Text = text;
            BgColor = bgColor;
            TextColor = textColor;
            BgTransparent = bgTransparent;
            Remaining = TimeSpan.FromMilliseconds(durationMs);
        }
    }
}
