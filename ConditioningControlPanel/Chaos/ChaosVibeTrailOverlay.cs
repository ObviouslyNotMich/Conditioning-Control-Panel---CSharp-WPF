using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ConditioningControlPanel;

/// <summary>
/// VibePopping's buzz telegraph: while the vibe window runs, the pointer wears a small warm
/// glow and leaves a short fading trail behind it — so the sweep reads as a sweep. One
/// full-virtual-screen click-through window with a fixed ring buffer of trail dots
/// (recycled, zero per-frame allocations beyond the fade animations). Keep-alive contract
/// like every chaos overlay: created once at run start (<see cref="EnsureCreated"/>),
/// shown per vibe and HIDDEN between them (an idle visible full-screen layered surface
/// taxes DWM composition), closed only at teardown (<see cref="CloseActive"/>).
/// </summary>
public sealed class ChaosVibeTrailOverlay : Window
{
    private const double GLOW_SIZE = 58;     // pointer halo (smaller + faster than Rabbit Caller's)
    private const double DOT_SIZE = 20;      // trail dot at birth (shrinks as it fades)
    private const int TRAIL_DOTS = 14;       // ring buffer size
    private const double EMIT_DIST = 9;      // min cursor travel (DIPs) between dots
    private const int FADE_MS = 340;         // trail dot lifetime
    private const int TICK_MS = 16;          // cursor follow cadence

    private static ChaosVibeTrailOverlay? _active;

    /// <summary>Create the (hidden) trail window ahead of time at run start.</summary>
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
                        _active = new ChaosVibeTrailOverlay();
                        ((Window)_active).Show();   // realize the hwnd once...
                        _active.Hide();             // ...then idle hidden until a vibe starts
                    }
                }
                catch (Exception ex) { App.Logger?.Debug("ChaosVibeTrail.EnsureCreated: {E}", ex.Message); }
            });
        }
        catch { }
    }

    /// <summary>The vibe started: show the glow + trail and follow the cursor. Safe from any thread.</summary>
    public static void Start()
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
                        _active = new ChaosVibeTrailOverlay();
                        ((Window)_active).Show();
                    }
                    else if (!_active.IsVisible) _active.Show();
                    ChaosWindowZ.RaiseAboveVideo(_active);   // un-hiding doesn't re-stack — kick over a playing video
                    _active.BeginFollow();
                }
                catch (Exception ex) { App.Logger?.Debug("ChaosVibeTrail.Start: {E}", ex.Message); }
            });
        }
        catch { }
    }

    /// <summary>The vibe ended: stop following and hide (window stays alive for the next buzz).</summary>
    public static void Stop()
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() => { try { _active?.EndFollow(); } catch { } });
        }
        catch { }
    }

    /// <summary>Re-stack the live window above a mandatory video (see ChaosWindowZ). UI thread only.</summary>
    public static void RaiseActive() => ChaosWindowZ.RaiseTopmost(_active);

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
                    if (w != null) { w._follow.Stop(); w.Close(); }
                }
                catch { }
            });
        }
        catch { }
    }

    private readonly Canvas _canvas;
    private readonly Ellipse _glow;
    private readonly Ellipse[] _dots = new Ellipse[TRAIL_DOTS];
    private readonly ScaleTransform[] _dotScales = new ScaleTransform[TRAIL_DOTS];
    private readonly DispatcherTimer _follow;
    private int _dotIndex;
    private Point _lastEmit = new(double.MinValue, double.MinValue);

    private ChaosVibeTrailOverlay()
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

        // Trail dots first (under the glow): warm amber sparks, recycled round-robin.
        var dotBrush = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5) };
        dotBrush.GradientStops.Add(new GradientStop(Color.FromArgb(190, 0xFF, 0xD7, 0x6A), 0.0));
        dotBrush.GradientStops.Add(new GradientStop(Color.FromArgb(110, 0xFF, 0xB0, 0x3A), 0.55));
        dotBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0x8A, 0x3A), 1.0));
        if (dotBrush.CanFreeze) dotBrush.Freeze();
        for (int i = 0; i < TRAIL_DOTS; i++)
        {
            var sc = new ScaleTransform(1, 1);
            var dot = new Ellipse
            {
                Width = DOT_SIZE, Height = DOT_SIZE,
                Fill = dotBrush,
                IsHitTestVisible = false,
                Opacity = 0,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = sc,
            };
            _dots[i] = dot;
            _dotScales[i] = sc;
            _canvas.Children.Add(dot);
        }

        // The pointer glow: amber like the VibePopping banner, with a fast little buzz-pulse.
        var glowBrush = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5) };
        glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xE9, 0xA0), 0.12));
        glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(160, 0xFF, 0xB0, 0x3A), 0.5));
        glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0x69, 0xB4), 1.0));
        if (glowBrush.CanFreeze) glowBrush.Freeze();

        var glowScale = new ScaleTransform(1, 1);
        _glow = new Ellipse
        {
            Width = GLOW_SIZE, Height = GLOW_SIZE,
            Fill = glowBrush,
            IsHitTestVisible = false,
            Opacity = 0,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = glowScale,
        };
        _canvas.Children.Add(_glow);

        // A quick tremble, not the Rabbit Caller's slow breath — this one is buzzing.
        var pulse = new DoubleAnimation(0.9, 1.12, TimeSpan.FromMilliseconds(180))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        glowScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        glowScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);

        _follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TICK_MS) };
        _follow.Tick += FollowTick;

        SourceInitialized += (_, _) => ApplyExStyles();
    }

    private void BeginFollow()
    {
        _lastEmit = new Point(double.MinValue, double.MinValue);
        _glow.Opacity = 1;
        FollowTick(null, EventArgs.Empty);   // snap to the cursor before the first frame renders
        _follow.Start();
    }

    private void EndFollow()
    {
        _follow.Stop();
        _glow.Opacity = 0;
        foreach (var dot in _dots)
        {
            dot.BeginAnimation(OpacityProperty, null);
            dot.Opacity = 0;
        }
        try { Hide(); } catch { }   // idle hidden — no DWM tax between vibes
    }

    private void FollowTick(object? sender, EventArgs e)
    {
        try
        {
            if (!GetCursorPos(out var px)) return;
            var t = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
            if (t == null) return;
            var p = t.Value.Transform(new Point(px.X, px.Y));
            // Window-local coords (the window spans the virtual screen, which may not start at 0,0).
            double cx = p.X - Left, cy = p.Y - Top;

            Canvas.SetLeft(_glow, cx - GLOW_SIZE / 2);
            Canvas.SetTop(_glow, cy - GLOW_SIZE / 2);

            // Drop a trail spark once the pointer has moved far enough since the last one.
            double dx = cx - _lastEmit.X, dy = cy - _lastEmit.Y;
            if (dx * dx + dy * dy >= EMIT_DIST * EMIT_DIST)
            {
                _lastEmit = new Point(cx, cy);
                var dot = _dots[_dotIndex];
                var sc = _dotScales[_dotIndex];
                _dotIndex = (_dotIndex + 1) % TRAIL_DOTS;

                Canvas.SetLeft(dot, cx - DOT_SIZE / 2);
                Canvas.SetTop(dot, cy - DOT_SIZE / 2);
                dot.BeginAnimation(OpacityProperty, null);
                dot.Opacity = 0.5;
                dot.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0.5, 0, TimeSpan.FromMilliseconds(FADE_MS)));
                sc.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(FADE_MS)));
                sc.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(FADE_MS)));
            }
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosVibeTrail tick: {E}", ex.Message); }
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

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}
