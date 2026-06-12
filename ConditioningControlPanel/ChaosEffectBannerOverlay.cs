using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ConditioningControlPanel;

/// <summary>
/// Top-of-screen, click-through strip that names every temporary Chaos bonus while it lasts
/// (VIBEPOPPING, FREEZE, TIME SLOW, PORN DVD…). Each label throbs gently for its whole
/// duration and fades away when its effect ends. One shared window hosts all concurrent
/// banners side by side, keyed by effect id so an effect can't stack duplicate labels.
/// The window is KEPT ALIVE while empty and only closed at run teardown (CloseActive):
/// creating/closing a layered window mid-run while flash/bubble windows are animating can
/// wedge the shared WPF render thread (Application Hang 1002, 2026-06-10 13:57 + 15:05 —
/// both ~6.4s after a darter catch, i.e. exactly at the slow-mo banner's fade-out close).
/// Pre-create it at run start via <see cref="EnsureCreated"/>.
/// </summary>
public sealed class ChaosEffectBannerOverlay : Window
{
    private const double FONT_SIZE = 34;
    private const int FADE_IN_MS = 200;
    private const int FADE_OUT_MS = 380;

    private static ChaosEffectBannerOverlay? _active;

    /// <summary>Create the (empty, invisible) banner window ahead of time — at run start,
    /// before the field gets hectic — so no Show() ever births a layered window mid-run.</summary>
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
                    if (_active == null) { _active = new ChaosEffectBannerOverlay(); ((Window)_active).Show(); }
                }
                catch (Exception ex) { App.Logger?.Debug("ChaosEffectBanner.EnsureCreated: {E}", ex.Message); }
            });
        }
        catch { }
    }

    /// <summary>Show (or keep) the banner for an effect. Safe from any thread.</summary>
    public static void Show(string id, string text, Color accent)
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() =>
            {
                try
                {
                    if (_active == null) { _active = new ChaosEffectBannerOverlay(); ((Window)_active).Show(); }
                    _active.AddEntry(id, text, accent);
                    ChaosWindowZ.RaiseAboveVideo(_active);   // keep-alive window — re-stack over a playing video
                }
                catch (Exception ex) { App.Logger?.Debug("ChaosEffectBanner.Show: {E}", ex.Message); }
            });
        }
        catch { }
    }

    /// <summary>Fade out + remove the banner for an effect (no-op if it isn't showing).</summary>
    public static void End(string id)
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() => _active?.FadeEntry(id));
        }
        catch { }
    }

    /// <summary>Re-stack the live window above a mandatory video (see ChaosWindowZ). UI thread only.</summary>
    public static void RaiseActive() => ChaosWindowZ.RaiseTopmost(_active);

    /// <summary>Instant teardown (run end / shutdown).</summary>
    public static void CloseActive()
    {
        try { _active?.CloseNow(); } catch { }
    }

    private readonly StackPanel _row;
    private readonly Dictionary<string, FrameworkElement> _entries = new();

    private ChaosEffectBannerOverlay()
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

        var wa = SystemParameters.WorkArea;
        Left = wa.Left;
        Top = wa.Top + 6;
        Width = wa.Width;
        Height = 80;

        _row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Content = _row;

        SourceInitialized += (_, _) => ApplyExStyles();
    }

    private void AddEntry(string id, string text, Color accent)
    {
        if (_entries.ContainsKey(id)) return;   // already on screen — let it ride

        var fill = new SolidColorBrush(accent); if (fill.CanFreeze) fill.Freeze();
        var stroke = new SolidColorBrush(Color.FromRgb(0x0B, 0x08, 0x12)); if (stroke.CanFreeze) stroke.Freeze();

        var throb = new ScaleTransform(1, 1);
        var label = new OutlinedText
        {
            Text = text.ToUpperInvariant(),
            FontSize = FONT_SIZE,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = 2.6,
            Margin = new Thickness(18, 0, 18, 0),
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = throb,
            Opacity = 0,
        };
        label.Build();
        _entries[id] = label;
        _row.Children.Add(label);

        label.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(FADE_IN_MS)));

        // The whole point: a barely-there heartbeat for as long as the effect runs.
        var pulse = new DoubleAnimation(1.0, 1.03, TimeSpan.FromMilliseconds(850))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        throb.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        throb.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }

    private void FadeEntry(string id)
    {
        if (!_entries.TryGetValue(id, out var el)) return;
        _entries.Remove(id);
        var fade = new DoubleAnimation(el.Opacity, 0, TimeSpan.FromMilliseconds(FADE_OUT_MS));
        // Empty window stays alive (transparent + click-through, ~free) — closing a layered
        // window mid-run is the render-thread deadlock trigger. CloseActive() ends it.
        fade.Completed += (_, _) => { try { _row.Children.Remove(el); } catch { } };
        el.BeginAnimation(OpacityProperty, fade);
    }

    private void CloseNow()
    {
        if (ReferenceEquals(_active, this)) _active = null;
        _entries.Clear();
        try { Close(); } catch { }
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
