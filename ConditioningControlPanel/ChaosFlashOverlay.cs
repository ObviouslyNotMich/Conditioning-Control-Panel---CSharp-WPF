using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using XamlAnimatedGif;

namespace ConditioningControlPanel;

/// <summary>
/// Full-screen, click-through overlay for the Chaos "braindrain" payload: picks one random
/// image from the same pool the flashes draw on (<c>EffectiveAssetsPath/images</c>) and holds
/// it over the whole desktop at a low opacity for a few seconds, fading in then back out.
/// Animated GIFs loop; static images are shown frozen. Silent no-op if the pool is empty.
/// </summary>
public sealed class ChaosFlashOverlay : Window
{
    private const int DEFAULT_DURATION_MS = 10000;   // ~10s on screen
    private const double DEFAULT_OPACITY = 0.10;     // faint 10% wash

    private static readonly string[] Extensions =
        { ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".webp", ".bmp" };

    private static ChaosFlashOverlay? _active;
    private static readonly Random _rng = new();

    /// <summary>Show a random flash-pool image full-screen for <paramref name="durationMs"/>
    /// at <paramref name="opacity"/> (0..1). No-op if no images are available.</summary>
    public static void Show(int durationMs = DEFAULT_DURATION_MS, double opacity = DEFAULT_OPACITY)
    {
        try
        {
            var pick = PickImage();
            if (pick == null) return;
            _active?.CloseNow();
            _active = new ChaosFlashOverlay(pick, durationMs, opacity);
            ((Window)_active).Show();
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosFlashOverlay.Show: {E}", ex.Message); }
    }

    /// <summary>Close any active overlay immediately (run teardown).</summary>
    public static void CloseActive() { try { _active?.CloseNow(); } catch { } }

    private readonly Image _img;
    private readonly DispatcherTimer _life;

    private ChaosFlashOverlay(string path, int durationMs, double opacity)
    {
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
        Opacity = 0;

        _img = new Image { Stretch = Stretch.UniformToFill };
        if (path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
        {
            AnimationBehavior.SetRepeatBehavior(_img, RepeatBehavior.Forever);
            AnimationBehavior.SetAutoStart(_img, true);
            AnimationBehavior.SetSourceUri(_img, new Uri(path, UriKind.Absolute));
        }
        else
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            if (bmp.CanFreeze) bmp.Freeze();
            _img.Source = bmp;
        }
        Content = _img;

        double peak = Math.Clamp(opacity, 0.02, 1.0);
        SourceInitialized += (_, _) => ApplyExStyles();
        Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(0, peak, TimeSpan.FromMilliseconds(500)));

        _life = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(600, durationMs)) };
        _life.Tick += (_, _) =>
        {
            _life.Stop();
            var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(700));
            fade.Completed += (_, _) => CloseNow();
            BeginAnimation(OpacityProperty, fade);
        };
        _life.Start();
    }

    private void CloseNow()
    {
        try { _life.Stop(); } catch { }
        try { AnimationBehavior.SetSourceUri(_img, null); } catch { }
        try { _img.Source = null; } catch { }
        if (ReferenceEquals(_active, this)) _active = null;
        try { Close(); } catch { }
    }

    /// <summary>Pick a random image file from the flash images folder, or null if none.</summary>
    private static string? PickImage()
    {
        try
        {
            var dir = Path.Combine(App.EffectiveAssetsPath ?? "", "images");
            if (!Directory.Exists(dir)) return null;
            var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();
            if (files.Count == 0) return null;
            return files[_rng.Next(files.Count)];
        }
        catch { return null; }
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
