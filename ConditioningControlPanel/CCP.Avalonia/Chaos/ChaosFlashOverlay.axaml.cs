using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;
using global::Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;

using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of ChaosFlashOverlay: full-screen, click-through overlay for the
/// Chaos "braindrain" payload. One window is created on first use and kept alive.
/// Animated GIFs are rendered via the cross-platform SkiaSharp helper <see cref="AvaloniaAnimatedGif"/>.
/// </summary>
public partial class ChaosFlashOverlay : Window
{
    private const int DEFAULT_DURATION_MS = 10000;
    private const double DEFAULT_OPACITY = 0.10;

    private static readonly string[] Extensions =
        { ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".webp", ".bmp" };

    private static ChaosFlashOverlay? _active;
    private static readonly Random _rng = new();

    private readonly Image _img;
    private readonly DispatcherTimer _life = new();
    private OpacityFade? _fade;
    private AvaloniaAnimatedGif? _anim;
    private (string path, int durationMs, double opacity)? _pending;

    public ChaosFlashOverlay()
    {
        InitializeComponent();

_img = new Image { Stretch = Stretch.UniformToFill };
        Content = _img;

        var (sl, st, sw, sh) = AvaloniaChaosWindowZ.StageBounds();
        Position = new PixelPoint((int)sl, (int)st);
        Width = sw;
        Height = sh;
        Opacity = 0;

        Opened += (_, _) => ApplyExStyles();
        Loaded += (_, _) =>
        {
            if (_pending is { } p) { _pending = null; DisplayCore(p.path, p.durationMs, p.opacity); }
        };

        _life.Interval = TimeSpan.FromMilliseconds(DEFAULT_DURATION_MS);
        _life.Tick += (_, _) =>
        {
            _life.Stop();
            _fade?.Dispose();
            _fade = new OpacityFade(this, Opacity, 0, 700, () =>
            {
                ClearImage();
                try { Hide(); } catch { }
            });
        };
    }

    public static void Show(int durationMs = DEFAULT_DURATION_MS, double opacity = DEFAULT_OPACITY)
    {
        var logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
        try
        {
            var pick = PickImage();
            if (pick == null) return;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_active == null) { _active = new ChaosFlashOverlay(); ((global::Avalonia.Controls.Window)_active).Show(); }
                    else if (!_active.IsVisible) { try { ((global::Avalonia.Controls.Window)_active).Show(); } catch { } }
                    AvaloniaChaosWindowZ.RaiseAboveVideo(_active);
                    _active.Display(pick, durationMs, opacity);
                }
                catch (Exception ex) { App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Information("ChaosFlashOverlay.Show: {E}", ex.Message); }
            });
        }
        catch (Exception ex) { App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Information("ChaosFlashOverlay.Show: {E}", ex.Message); }
    }

    public static void RaiseActive() => AvaloniaChaosWindowZ.RaiseTopmost(_active);
    public static void CloseActive() { try { _active?.CloseNow(); } catch { } }

    private void Display(string path, int durationMs, double opacity)
    {
        if (!IsLoaded) { _pending = (path, durationMs, opacity); return; }
        DisplayCore(path, durationMs, opacity);
    }

    private void DisplayCore(string path, int durationMs, double opacity)
    {
        _life.Stop();
        ClearImage();

        if (path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
        {
            _anim = AvaloniaAnimatedGif.TryCreate(path);
            if (_anim != null)
            {
                _img.Source = _anim.Source;
                _anim.Start();
            }
            else
            {
                _img.Source = AvaloniaChaosArt.TryLoad(path);
            }
        }
        else
        {
            try
            {
                using var stream = File.OpenRead(path);
_img.Source = new Bitmap(stream);
            }
            catch { _img.Source = null; }
        }

        double peak = Math.Clamp(opacity, 0.02, 1.0);
        _fade?.Dispose();
        _fade = new OpacityFade(this, 0, peak, 500);
        _life.Interval = TimeSpan.FromMilliseconds(Math.Max(600, durationMs));
        _life.Start();
    }

    private void ClearImage()
    {
        try { _img.Source = null; } catch { }
        _anim?.Dispose();
        _anim = null;
    }

    private void CloseNow()
    {
        try { _life.Stop(); } catch { }
        _fade?.Dispose();
        ClearImage();
        if (ReferenceEquals(_active, this)) _active = null;
        try { Close(); } catch { }
    }

    private static string? PickImage()
    {
        try
        {
            var dir = Path.Combine(AvaloniaChaosEnv.EffectiveAssetsPath ?? "", "images");
            if (!Directory.Exists(dir)) return null;
            var files =
Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();
            if (files.Count == 0) return null;
            return files[_rng.Next(files.Count)];
        }
        catch { return null; }
    }

    private void ApplyExStyles()
    {
        // TODO: apply WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT on Windows.
    }
}
