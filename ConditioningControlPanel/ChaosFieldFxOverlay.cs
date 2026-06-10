using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ConditioningControlPanel;

/// <summary>
/// Field FX for the run-boon pool: Size Queen's expanding pop-ring, Aftermath's crackling
/// residue zone, and Tail-Plug's rabbit sparkle trail all draw on this ONE full-virtual-screen
/// click-through canvas. The popping itself lives in BubbleService.TickFieldHazards — this
/// window is purely the visible half. Keep-alive contract like every chaos overlay: created
/// once at run start (<see cref="EnsureCreated"/>), shown only while something is drawn
/// (idle visible layered surfaces tax DWM), closed only at teardown (<see cref="CloseActive"/>)
/// — layered-window churn is what deadlocks the render thread.
/// </summary>
public sealed class ChaosFieldFxOverlay : Window
{
    private const int TRAIL_DOT_POOL = 90;     // recycled spark ring buffer (rabbits emit ~1 dot / 40 DIPs)
    private const double TRAIL_DOT_SIZE = 16;

    private static readonly Color RingColor = Color.FromRgb(0x7A, 0xE0, 0xFF);      // Size Queen — snap-cyan
    private static readonly Color ResidueColor = Color.FromRgb(0x9C, 0x5C, 0xFF);   // Aftermath — E-Stim violet
    private static readonly Color TrailColor = Color.FromRgb(0xFF, 0x4D, 0xC4);     // Tail-Plug — rabbit pink

    private static ChaosFieldFxOverlay? _active;

    /// <summary>Create the (hidden) field-FX window ahead of time at run start.</summary>
    public static void EnsureCreated()
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() =>
            {
                try
                {
                    if (_active == null)
                    {
                        _active = new ChaosFieldFxOverlay();
                        ((Window)_active).Show();   // realize the hwnd once...
                        _active.Hide();             // ...then idle hidden until something draws
                    }
                }
                catch (Exception ex) { App.Logger?.Debug("ChaosFieldFx.EnsureCreated: {E}", ex.Message); }
            });
        }
        catch { }
    }

    /// <summary>Size Queen: draw one expanding ring at <paramref name="centerPx"/> (physical px).</summary>
    public static void Ripple(Point centerPx, double radiusPx, double lifeMs) =>
        OnUi(w => w.DrawRipple(centerPx, radiusPx, lifeMs));

    /// <summary>Aftermath: draw one crackling residue zone for <paramref name="lifeMs"/>.</summary>
    public static void Residue(Point centerPx, double radiusPx, double lifeMs) =>
        OnUi(w => w.DrawResidue(centerPx, radiusPx, lifeMs));

    /// <summary>Tail-Plug: drop one fading sparkle at a rabbit's trail point.</summary>
    public static void TrailDot(Point centerPx, double lifeSec) =>
        OnUi(w => w.DrawTrailDot(centerPx, lifeSec));

    /// <summary>Instant teardown (run end / shutdown) — the only place this hwnd dies.</summary>
    public static void CloseActive()
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) { _active = null; return; }
            disp.BeginInvoke(() =>
            {
                try
                {
                    var w = _active;
                    _active = null;
                    w?.Close();
                }
                catch { }
            });
        }
        catch { }
    }

    private static void OnUi(Action<ChaosFieldFxOverlay> act)
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() =>
            {
                try
                {
                    if (_active == null)
                    {
                        _active = new ChaosFieldFxOverlay();
                        ((Window)_active).Show();
                    }
                    else if (!_active.IsVisible) _active.Show();
                    act(_active);
                }
                catch (Exception ex) { App.Logger?.Debug("ChaosFieldFx: {E}", ex.Message); }
            });
        }
        catch { }
    }

    private readonly Canvas _canvas;
    private readonly Ellipse[] _trailDots = new Ellipse[TRAIL_DOT_POOL];
    private readonly ScaleTransform[] _trailScales = new ScaleTransform[TRAIL_DOT_POOL];
    private int _trailIndex;
    private int _transientCount;   // live ripples/residues — the window hides when everything faded

    private ChaosFieldFxOverlay()
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

        _canvas = new Canvas { IsHitTestVisible = false };
        Content = _canvas;

        // Pre-build the recycled trail-spark pool (rabbit-pink, gold core).
        var dotBrush = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5) };
        dotBrush.GradientStops.Add(new GradientStop(Color.FromArgb(200, 0xFF, 0xE9, 0xA0), 0.0));
        dotBrush.GradientStops.Add(new GradientStop(Color.FromArgb(120, TrailColor.R, TrailColor.G, TrailColor.B), 0.55));
        dotBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, TrailColor.R, TrailColor.G, TrailColor.B), 1.0));
        if (dotBrush.CanFreeze) dotBrush.Freeze();
        for (int i = 0; i < TRAIL_DOT_POOL; i++)
        {
            var sc = new ScaleTransform(1, 1);
            var dot = new Ellipse
            {
                Width = TRAIL_DOT_SIZE, Height = TRAIL_DOT_SIZE,
                Fill = dotBrush,
                IsHitTestVisible = false,
                Opacity = 0,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = sc,
            };
            _trailDots[i] = dot;
            _trailScales[i] = sc;
            _canvas.Children.Add(dot);
        }

        SourceInitialized += (_, _) => ApplyExStyles();
    }

    /// <summary>Physical px → this window's local DIPs (it spans the virtual screen).</summary>
    private Point? Local(Point px)
    {
        var t = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
        if (t == null) return null;
        var p = t.Value.Transform(px);
        return new Point(p.X - Left, p.Y - Top);
    }

    private void DrawRipple(Point centerPx, double radiusPx, double lifeMs)
    {
        if (Local(centerPx) is not Point c) return;
        // px radius → DIPs via the same device transform (uniform scale assumed per ring).
        double scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        double r = radiusPx * scale;

        var st = new ScaleTransform(0.05, 0.05);
        var ring = new Ellipse
        {
            Width = r * 2, Height = r * 2,
            Stroke = new SolidColorBrush(Color.FromArgb(190, RingColor.R, RingColor.G, RingColor.B)),
            StrokeThickness = 6,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = st,
        };
        Canvas.SetLeft(ring, c.X - r);
        Canvas.SetTop(ring, c.Y - r);
        _canvas.Children.Add(ring);
        _transientCount++;

        var dur = TimeSpan.FromMilliseconds(lifeMs);
        var grow = new DoubleAnimation(0.05, 1.0, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var fade = new DoubleAnimation(0.95, 0, dur);
        fade.Completed += (_, _) => RemoveTransient(ring);
        st.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        ring.BeginAnimation(OpacityProperty, fade);
    }

    private void DrawResidue(Point centerPx, double radiusPx, double lifeMs)
    {
        if (Local(centerPx) is not Point c) return;
        double scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        double r = radiusPx * scale;

        var brush = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(70, 0xBF, 0xEC, 0xFF), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(90, ResidueColor.R, ResidueColor.G, ResidueColor.B), 0.55));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, ResidueColor.R, ResidueColor.G, ResidueColor.B), 1.0));
        if (brush.CanFreeze) brush.Freeze();
        var zone = new Ellipse
        {
            Width = r * 2, Height = r * 2,
            Fill = brush,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(zone, c.X - r);
        Canvas.SetTop(zone, c.Y - r);
        _canvas.Children.Add(zone);
        _transientCount++;

        // A crackling flicker for its whole life, then a quick fade-away at the end.
        var flicker = new DoubleAnimationUsingKeyFrames();
        int steps = Math.Max(2, (int)(lifeMs / 90));
        var rng = new Random();
        for (int i = 0; i < steps; i++)
            flicker.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.55 + rng.NextDouble() * 0.45,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(i * 90))));
        flicker.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(lifeMs))));
        flicker.Completed += (_, _) => RemoveTransient(zone);
        zone.BeginAnimation(OpacityProperty, flicker);
    }

    private void DrawTrailDot(Point centerPx, double lifeSec)
    {
        if (Local(centerPx) is not Point c) return;
        var dot = _trailDots[_trailIndex];
        var sc = _trailScales[_trailIndex];
        _trailIndex = (_trailIndex + 1) % TRAIL_DOT_POOL;

        Canvas.SetLeft(dot, c.X - TRAIL_DOT_SIZE / 2);
        Canvas.SetTop(dot, c.Y - TRAIL_DOT_SIZE / 2);
        var dur = TimeSpan.FromSeconds(Math.Max(0.3, lifeSec));
        dot.BeginAnimation(OpacityProperty, null);
        dot.Opacity = 0.65;
        var fade = new DoubleAnimation(0.65, 0, dur);
        fade.Completed += (_, _) =>
        {
            if (_transientCount == 0 && Services.BubbleService.ChaosRabbitTrailSecNow <= 0)
            {
                try { Hide(); } catch { }   // boon gone / run over — don't idle visible
            }
        };
        dot.BeginAnimation(OpacityProperty, fade);
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.1, 0.3, dur));
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.1, 0.3, dur));
    }

    /// <summary>A ripple/residue finished: drop its element; hide the window once idle.
    /// (Trail dots are pooled + opacity-0 when faded, so they don't hold the window open —
    /// but while rabbits are dragging trails the steady dot stream keeps it visible anyway.)</summary>
    private void RemoveTransient(UIElement el)
    {
        try { _canvas.Children.Remove(el); } catch { }
        _transientCount = Math.Max(0, _transientCount - 1);
        if (_transientCount == 0 && Services.BubbleService.ChaosRabbitTrailSecNow <= 0)
        {
            try { Hide(); } catch { }
        }
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
