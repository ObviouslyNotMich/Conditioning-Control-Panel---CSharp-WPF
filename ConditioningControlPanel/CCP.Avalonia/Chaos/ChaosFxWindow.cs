using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of ChaosFxWindow: full-screen click-through colour vignette pulses.
/// </summary>
public sealed class ChaosFxWindow : Window
{
    private readonly Border _vignette;
    private readonly Border _edge;
    private readonly Border _heat;

    public ChaosFxWindow()
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = AvaloniaChaosWindowZ.BornTopmost;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(0, 0);

        var screens = AvaloniaChaosWindowZ.GetScreens();
        var primary = screens?.Primary;
        Width = primary?.Bounds.Width ?? 1920;
        Height = primary?.Bounds.Height ?? 1080;

        _heat = new Border { Opacity = 0, IsHitTestVisible = false };
        _edge = new Border { Opacity = 0, IsHitTestVisible = false };
        _vignette = new Border { Opacity = 0, IsHitTestVisible = false };
        var root = new Grid { IsHitTestVisible = false };
        root.Children.Add(_heat);
        root.Children.Add(_edge);
        root.Children.Add(_vignette);
        Content = root;
        Opened += (_, _) => ApplyExStyles();
    }

    public void BeginEdgeHold(Color color, double strength)
    {
        try
        {
            double peak = Math.Clamp(0.25 + strength * 0.45, 0.2, 0.7);
            _edge.Background = RadialEdge(color);
            FadeBorder(_edge, peak, 180);
        }
        catch { }
    }

    public void EndEdgeHold() => FadeBorder(_edge, 0, 450);

    public void SetHeatTint(Color color, double opacity)
    {
        try
        {
            double peak = Math.Clamp(opacity, 0.0, 0.5);
            _heat.Background ??= RadialHeat(color);
            FadeBorder(_heat, peak, 400);
        }
        catch { }
    }

    public void EndHeatTint() => FadeBorder(_heat, 0, 600);

    public void FreezeBurst(Color color)
    {
        try
        {
            _vignette.Background = FreezeBrush(color);
            KeyFade(_vignette, 0.6, 50, 650);
        }
        catch { }
    }

    public void Pulse(Color color, double strength)
    {
        try
        {
            double peak = Math.Clamp(0.22 + strength * 0.5, 0.15, 0.72);
            _vignette.Background = RadialEdge(color);
            KeyFade(_vignette, peak, 40, 300);
        }
        catch { }
    }

    public void RaiseToTopmost() => AvaloniaChaosWindowZ.RaiseTopmost(this);

    private static void FadeBorder(Border b, double to, int ms)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        double from = b.Opacity;
        double startMs = Environment.TickCount64;
        timer.Tick += (_, _) =>
        {
            double t = Math.Min(1, (Environment.TickCount64 - startMs) / ms);
            b.Opacity = from + (to - from) * t;
            if (t >= 1) timer.Stop();
        };
        timer.Start();
    }

    private static void KeyFade(Border b, double peak, int inMs, int totalMs)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        double startMs = Environment.TickCount64;
        timer.Tick += (_, _) =>
        {
            double elapsed = Environment.TickCount64 - startMs;
            double t = elapsed / totalMs;
            if (t >= 1) { b.Opacity = 0; timer.Stop(); return; }
            b.Opacity = elapsed <= inMs ? peak * (elapsed / inMs) : peak * (1 - (elapsed - inMs) / (double)(totalMs - inMs));
        };
        timer.Start();
    }

    private static RadialGradientBrush RadialEdge(Color color)
    {
        var brush = new RadialGradientBrush
        {
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.95, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(1.05, RelativeUnit.Relative),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0.55));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, color.R, color.G, color.B), 1.0));
        return brush;
    }

    private static RadialGradientBrush RadialHeat(Color color)
    {
        var brush = new RadialGradientBrush
        {
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(1.0, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(1.1, RelativeUnit.Relative),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0.40));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, color.R, color.G, color.B), 1.0));
        return brush;
    }

    private static RadialGradientBrush FreezeBrush(Color color)
    {
        var brush = new RadialGradientBrush
        {
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(1.1, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(1.2, RelativeUnit.Relative),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(235, 255, 255, 255), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(170, color.R, color.G, color.B), 0.45));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(90, color.R, color.G, color.B), 1.0));
        return brush;
    }

    private void ApplyExStyles() => ChaosWin32Helper.ApplyOverlayExStyles(this, true);
}
