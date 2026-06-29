using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Core.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// Renders flash image popups. The service controls spawning and lifetime;
/// this layer handles position, fade, and rendering.
/// Supports animated GIFs via frame-by-frame decoding with SkiaSharp.
/// </summary>
public sealed class FlashLayer : BaseLayer
{
    private readonly List<FlashItem> _items = new();
    private readonly object _sync = new();
    private const double FADE_PER_SEC = 3.0;

    public override int ZIndex => CompositorLayers.Flash;

    public override bool IsActive
    {
        get
        {
            lock (_sync) { return _items.Count > 0; }
        }
    }

    /// <summary>Spawn a new flash image. Returns the item ID for tracking.</summary>
    public Guid Spawn(string? filePath, Bitmap bitmap, double x, double y, double width, double height, double maxOpacity, int lifetimeMs, bool clickable)
    {
        // Fast path: create item with single frame immediately so it appears without blocking.
        var firstFrame = ConvertFirstFrame(bitmap);
        if (firstFrame == null) return Guid.Empty;

        var id = Guid.NewGuid();
        var item = new FlashItem(id, new[] { firstFrame }, x, y, width, height, maxOpacity, lifetimeMs, clickable);
        lock (_sync)
        {
            _items.Add(item);
        }

        // If this is an animated GIF, decode remaining frames on a background thread.
        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".gif")
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        var frames = ExtractGifFrames(filePath);
                        if (frames != null && frames.Length > 1)
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                lock (_sync)
                                {
                                    if (!_items.Contains(item)) return;
                                    item.SetFrames(frames);
                                }
                            });
                        }
                        else
                        {
                            // No animation; dispose any allocated frames.
                            if (frames != null)
                                foreach (var f in frames) f?.Dispose();
                        }
                    }
                    catch { /* best effort */ }
                });
            }
        }

        return id;
    }

    private static SKImage? ConvertFirstFrame(Bitmap bitmap)
    {
        try
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            stream.Position = 0;
            return SKImage.FromEncodedData(stream);
        }
        catch { return null; }
    }

    private static SKImage[]? ExtractGifFrames(string filePath)
    {
        try
        {
            using var codec = SKCodec.Create(filePath);
            if (codec == null || codec.FrameCount <= 1) return null;

            var frames = new SKImage[codec.FrameCount];
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height);
            for (int i = 0; i < codec.FrameCount; i++)
            {
                using var frameBitmap = new SKBitmap(info);
                var opts = new SKCodecOptions(i);
                codec.GetPixels(frameBitmap.Info, frameBitmap.GetPixels(), opts);
                frames[i] = SKImage.FromBitmap(frameBitmap);
            }
            return frames;
        }
        catch { return null; }
    }

    public void RemoveItem(Guid id)
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

    public void RemoveItem(FlashItem item) => RemoveItem(item.Id);

    public override void Update(TimeSpan deltaTime)
    {
        double dt = deltaTime.TotalSeconds;
        lock (_sync)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var item = _items[i];
                item.Update(dt, FADE_PER_SEC);
                if (item.IsDone) _items.RemoveAt(i);
            }
        }
    }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
    {
        // Snapshot each item's current draw state (frame + opacity + transform) under the lock so the
        // GPU render thread never dereferences an SKImage that SetFrames()/Update() is disposing or
        // replacing on the UI thread. Drawing is done outside the lock to minimize contention.
        List<(FlashItem item, SKImage? frame, double opacity, double x, double y, double w, double h)> snapshot;
        lock (_sync)
        {
            snapshot = _items
                .Where(i => i.Opacity > 0)
                .Select(i => (i, i.CurrentFrame, i.Opacity, i.X, i.Y, i.Width, i.Height))
                .ToList();
        }

        foreach (var entry in snapshot)
        {
            var image = entry.frame;
            if (image == null) continue;
            using var paint = new SKPaint
            {
                Color = new SKColor(255, 255, 255, (byte)(entry.opacity * 255))
            };
            var dest = new SKRect((float)entry.x, (float)entry.y, (float)(entry.x + entry.w), (float)(entry.y + entry.h));
            canvas.DrawImage(image, dest, paint);
        }
    }

    /// <summary>Hit-test in reverse order (topmost first). Returns the clicked item or null.</summary>
    public FlashItem? HitTest(double x, double y)
    {
        lock (_sync)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var item = _items[i];
                if (item.Clickable && x >= item.X && x <= item.X + item.Width && y >= item.Y && y <= item.Y + item.Height)
                    return item;
            }
        }
        return null;
    }

    public sealed class FlashItem
    {
        public Guid Id { get; }
        private SKImage[] _frames;
        public SKImage[] Frames => _frames;
        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }
        public double MaxOpacity { get; }
        public bool Clickable { get; }
        public double Opacity { get; private set; }
        public bool IsDone { get; private set; }
        public DateTime ExpiresAt { get; }
        public int OriginalLifetimeMs { get; }

        private bool _isFadingOut;
        private int _currentFrame;
        private double _frameTimer;
        private const double FrameDuration = 0.08; // ~12.5 fps for GIFs

        public SKImage CurrentFrame => Frames.Length > 0 ? Frames[_currentFrame % Frames.Length] : Frames[0];

        public FlashItem(Guid id, SKImage[] frames, double x, double y, double width, double height, double maxOpacity, int lifetimeMs, bool clickable)
        {
            Id = id;
            _frames = frames;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            MaxOpacity = maxOpacity;
            Clickable = clickable;
            Opacity = 0;
            ExpiresAt = DateTime.Now.AddMilliseconds(lifetimeMs);
            OriginalLifetimeMs = lifetimeMs;
        }

        public void SetFrames(SKImage[] newFrames)
        {
            if (newFrames == null || newFrames.Length == 0) return;
            foreach (var f in Frames) f?.Dispose();
            _frames = newFrames;
            _currentFrame = 0;
            _frameTimer = 0;
        }

        public void Update(double dt, double fadePerSec)
        {
            var show = DateTime.Now < ExpiresAt && !_isFadingOut;
            var target = show ? MaxOpacity : 0.0;
            var step = fadePerSec * dt;

            if (Opacity < target)
                Opacity = Math.Min(target, Opacity + step);
            else if (Opacity > target)
            {
                Opacity = Math.Max(0.0, Opacity - step);
                if (Opacity <= 0) IsDone = true;
            }

            // Advance GIF frames
            if (Frames.Length > 1)
            {
                _frameTimer += dt;
                if (_frameTimer >= FrameDuration)
                {
                    _frameTimer = 0;
                    _currentFrame = (_currentFrame + 1) % Frames.Length;
                }
            }
        }

        public void BeginFadeOut() => _isFadingOut = true;
    }
}
