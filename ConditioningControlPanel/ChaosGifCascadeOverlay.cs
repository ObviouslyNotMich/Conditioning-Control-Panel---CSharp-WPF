using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using XamlAnimatedGif;

namespace ConditioningControlPanel;

/// <summary>
/// Full-screen, click-through overlay for the Chaos "GifCascade" payload: images/gifs spawn at the top
/// of the screen on a timer and fall/cascade downward, then despawn off the bottom. Sources images from
/// the SAME pool the flash/braindrain payloads draw on (<c>EffectiveAssetsPath/images</c>). Silent no-op
/// if the pool is empty. One instance at a time; a new Show() replaces any active cascade.
/// </summary>
public sealed class ChaosGifCascadeOverlay : Window
{
    private static readonly string[] Extensions =
        { ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".webp", ".bmp" };

    /// <summary>
    /// Hard ceiling on clips alive at once. Animated GIFs are decoded frame-by-frame at full
    /// native resolution by XamlAnimatedGif, so a pile-up of large gifs can exhaust memory and
    /// hard-crash the process (no managed exception). This cap makes that impossible regardless
    /// of spawn rate / fall speed.
    /// </summary>
    private const int MAX_CONCURRENT = 14;

    private static ChaosGifCascadeOverlay? _active;
    private static readonly Random _rng = new();

    /// <summary>Spawn a falling cascade of flash-pool images. All knobs come from the payload's named consts.</summary>
    public static void Show(double spawnRatePerSec, double durationSec, double gifSize, double fallSpeed, double opacity, double startScale = 1.0)
    {
        try
        {
            var files = PickFiles();
            if (files.Count == 0) return;
            _active?.CloseNow();
            _active = new ChaosGifCascadeOverlay(files, spawnRatePerSec, durationSec, gifSize, fallSpeed, opacity, startScale);
            ((Window)_active).Show();
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosGifCascadeOverlay.Show: {E}", ex.Message); }
    }

    /// <summary>Close any active cascade immediately (run teardown).</summary>
    public static void CloseActive() { try { _active?.CloseNow(); } catch { } }

    private readonly Canvas _canvas;
    private readonly List<string> _files;
    private readonly double _gifSize;
    private readonly double _fallSpeed;
    private readonly double _opacity;
    private readonly double _startScale;   // <1: clips spawn small at the top and grow toward _gifSize as they slide down
    private readonly List<Faller> _fallers = new();
    private readonly DispatcherTimer _spawn;
    private readonly DispatcherTimer _anim;
    private readonly DispatcherTimer _life;
    private bool _spawning = true;

    private sealed class Faller { public Image Img = null!; public double Y; public double CenterX; public double Speed; }

    private ChaosGifCascadeOverlay(List<string> files, double spawnRatePerSec, double durationSec,
                                   double gifSize, double fallSpeed, double opacity, double startScale)
    {
        _files = files;
        _gifSize = Math.Clamp(gifSize, 40, 600);
        _fallSpeed = Math.Clamp(fallSpeed, 0.5, 30);
        _opacity = Math.Clamp(opacity, 0.05, 1.0);
        _startScale = Math.Clamp(startScale, 0.1, 1.0);

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        _canvas = new Canvas { IsHitTestVisible = false };
        Content = _canvas;
        SourceInitialized += (_, _) => ApplyExStyles();

        double interval = 1000.0 / Math.Max(0.05, spawnRatePerSec);
        _spawn = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(interval) };
        _spawn.Tick += (_, _) => SpawnOne();

        _anim = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _anim.Tick += (_, _) => Step();

        _life = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1.0, durationSec)) };
        _life.Tick += (_, _) => { _life.Stop(); _spawning = false; _spawn.Stop(); };  // stop spawning; let in-flight fall out

        Loaded += (_, _) => { SpawnOne(); _spawn.Start(); _anim.Start(); _life.Start(); };
    }

    private void SpawnOne()
    {
        if (!_spawning) return;
        if (_fallers.Count >= MAX_CONCURRENT) return;   // never let clips pile up into an OOM
        try
        {
            string path = _files[_rng.Next(_files.Count)];
            var img = new Image { Stretch = Stretch.Uniform, Opacity = _opacity };
            if (path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            {
                AnimationBehavior.SetRepeatBehavior(img, System.Windows.Media.Animation.RepeatBehavior.Forever);
                AnimationBehavior.SetAutoStart(img, true);
                AnimationBehavior.SetSourceUri(img, new Uri(path, UriKind.Absolute));
            }
            else
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = (int)_gifSize;   // decode at display size — cheap
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                if (bmp.CanFreeze) bmp.Freeze();
                img.Source = bmp;
            }

            // Center-anchored so a clip grows about its own centre as it descends (reads as "approaching").
            double centerX = _gifSize / 2 + _rng.NextDouble() * Math.Max(1, Width - _gifSize);
            double y = -_gifSize;
            double w0 = _gifSize * ScaleAt(y);
            img.Width = w0;
            Canvas.SetLeft(img, centerX - w0 / 2);
            Canvas.SetTop(img, y);
            _canvas.Children.Add(img);
            _fallers.Add(new Faller { Img = img, CenterX = centerX, Y = y, Speed = _fallSpeed * (0.7 + _rng.NextDouble() * 0.6) });
        }
        catch (Exception ex) { App.Logger?.Debug("GifCascade spawn: {E}", ex.Message); }
    }

    private void Step()
    {
        try
        {
            for (int i = _fallers.Count - 1; i >= 0; i--)
            {
                var f = _fallers[i];
                f.Y += f.Speed;
                double w = _gifSize * ScaleAt(f.Y);
                f.Img.Width = w;
                Canvas.SetLeft(f.Img, f.CenterX - w / 2);
                Canvas.SetTop(f.Img, f.Y);
                if (f.Y > Height + _gifSize)
                {
                    try { AnimationBehavior.SetSourceUri(f.Img, null); f.Img.Source = null; } catch { }
                    _canvas.Children.Remove(f.Img);
                    _fallers.RemoveAt(i);
                }
            }
            // Cascade fully drained after the spawn window closed → close the overlay.
            if (!_spawning && _fallers.Count == 0) CloseNow();
        }
        catch (Exception ex) { App.Logger?.Debug("GifCascade step: {E}", ex.Message); }
    }

    /// <summary>Grow factor for a clip at vertical position <paramref name="y"/>: starts at
    /// <see cref="_startScale"/> up top and eases to full by ~75% of the way down.</summary>
    private double ScaleAt(double y)
    {
        if (_startScale >= 1.0) return 1.0;
        double p = Math.Clamp(y / Math.Max(1.0, Height * 0.75), 0, 1);
        return _startScale + (1.0 - _startScale) * p;
    }

    private void CloseNow()
    {
        try { _spawn.Stop(); } catch { }
        try { _anim.Stop(); } catch { }
        try { _life.Stop(); } catch { }
        foreach (var f in _fallers)
        {
            try { AnimationBehavior.SetSourceUri(f.Img, null); f.Img.Source = null; } catch { }
        }
        _fallers.Clear();
        if (ReferenceEquals(_active, this)) _active = null;
        try { Close(); } catch { }
    }

    private static List<string> PickFiles()
    {
        try
        {
            var dir = Path.Combine(App.EffectiveAssetsPath ?? "", "images");
            if (!Directory.Exists(dir)) return new List<string>();
            return Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();
        }
        catch { return new List<string>(); }
    }

    private void ApplyExStyles()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}
