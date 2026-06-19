using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ConditioningControlPanel;

/// <summary>
/// Rabbit Caller's "whistle held" telegraph: a soft pink-gold halo that rides the cursor
/// from the moment the skill is pressed until the next click summons the rabbits there.
/// Click-through + NOACTIVATE (the click lands on whatever is underneath — the service
/// samples the button globally). Keep-alive contract like every chaos overlay: created
/// once at run start (<see cref="EnsureCreated"/>), shown/hidden in place, closed only
/// at teardown (<see cref="CloseActive"/>) — layered-window churn mid-run can wedge the
/// shared WPF render thread.
/// </summary>
public sealed class ChaosCursorGlowOverlay : Window
{
    private const double SIZE = 76;

    private static ChaosCursorGlowOverlay? _active;

    /// <summary>Create the (hidden) halo window ahead of time at run start.</summary>
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
                    if (_active == null) { _active = new ChaosCursorGlowOverlay(); ((Window)_active).Show(); }
                }
                catch (Exception ex) { App.Logger?.Debug("ChaosCursorGlow.EnsureCreated: {E}", ex.Message); }
            });
        }
        catch { }
    }

    /// <summary>Show the halo (armed). Safe from any thread.</summary>
    public static void Arm()
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() =>
            {
                try
                {
                    if (_active == null) { _active = new ChaosCursorGlowOverlay(); ((Window)_active).Show(); }
                    _active._halo.Visibility = Visibility.Visible;
                    ChaosWindowZ.RaiseAboveVideo(_active);   // keep-alive window — re-stack over a playing video
                }
                catch (Exception ex) { App.Logger?.Debug("ChaosCursorGlow.Arm: {E}", ex.Message); }
            });
        }
        catch { }
    }

    /// <summary>Hide the halo (the whistle was answered or cancelled).</summary>
    public static void Disarm()
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() => { try { if (_active != null) _active._halo.Visibility = Visibility.Collapsed; } catch { } });
        }
        catch { }
    }

    /// <summary>Follow the cursor (physical px in, DIPs applied). UI thread only.</summary>
    public static void MoveToPx(double pxX, double pxY)
    {
        try
        {
            var w = _active;
            if (w == null || w._halo.Visibility != Visibility.Visible) return;
            var t = PresentationSource.FromVisual(w)?.CompositionTarget?.TransformFromDevice;
            if (t == null) return;
            var p = t.Value.Transform(new Point(pxX, pxY));
            w.Left = p.X - SIZE / 2;
            w.Top = p.Y - SIZE / 2;
        }
        catch { }
    }

    /// <summary>Re-stack the live window above a mandatory video (see ChaosWindowZ). UI thread only.</summary>
    public static void RaiseActive() => ChaosWindowZ.RaiseTopmost(_active);

    /// <summary>Instant teardown (run end / shutdown).</summary>
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

    private readonly Ellipse _halo;

    private ChaosCursorGlowOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = ChaosWindowZ.BornTopmost;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = SIZE;
        Height = SIZE;
        Left = -SIZE * 2;   // parked off-screen until armed
        Top = -SIZE * 2;

        var brush = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xD7, 0x00), 0.18));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(150, 0xFF, 0x8F, 0xC8), 0.55));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0x4D, 0xC4), 1.0));
        if (brush.CanFreeze) brush.Freeze();

        var scale = new ScaleTransform(1, 1);
        _halo = new Ellipse
        {
            Width = SIZE, Height = SIZE,
            Fill = brush,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale,
        };
        Content = _halo;

        // A slow breath so the halo reads as "armed, waiting" rather than a stuck cursor trail.
        var pulse = new DoubleAnimation(0.85, 1.12, TimeSpan.FromMilliseconds(620))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);

        SourceInitialized += (_, _) => ApplyExStyles();
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
