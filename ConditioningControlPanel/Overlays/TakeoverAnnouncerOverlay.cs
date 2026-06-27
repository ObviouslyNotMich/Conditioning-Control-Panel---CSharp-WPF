using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ConditioningControlPanel;

/// <summary>
/// Full-screen, click-through overlay that softly flashes a two-line cue near the top of the
/// screen whenever the Takeover (Autonomy) feature fires an effect: a small faded eyebrow
/// "TAKEOVER" above the effect name (FLASH, SPIRAL, MANTRA…). The point is *clarity* — it lets
/// the user tell a takeover-driven effect apart from an ordinary engine/scheduler effect.
/// Deliberately distinct from <see cref="ChaosAnnouncerOverlay"/>: text-only, two stacked
/// lines, a single calm purple/white palette and a softer peak opacity, so it never reads like
/// a Chaos power-up pop.
///
/// Like the Chaos announcer, ONE window is created on first use and KEPT ALIVE between cues
/// (each cue just swaps the label) — creating/closing a layered window churns the shared WPF
/// render thread and can wedge it (Application Hang 1002). It idles hidden between cues.
/// </summary>
public sealed class TakeoverAnnouncerOverlay : Window
{
    // ---- timing / layout tunables (≈ the user's "1 second") ----
    private const int IN_MS  = 150;   // fade-in
    private const int HOLD_MS = 700;  // dwell
    private const int OUT_MS = 250;   // fade-out
    private const double PEAK_OPACITY = 0.85;   // softer than a hard Chaos pop
    private const double EYEBROW_FONT = 22;
    private const double EFFECT_FONT  = 52;
    private const double TOP_OFFSET_DIP = 92;   // mirror the Chaos announcer's top anchor

    // Palette — one calm takeover identity (violet eyebrow, white effect), unlike Chaos's per-kind colors.
    private static readonly Brush EyebrowFill = Frozen(Color.FromRgb(0xC9, 0xA0, 0xFF)); // soft violet
    private static readonly Brush EffectFill  = Frozen(Color.FromRgb(0xFF, 0xFF, 0xFF)); // white
    private static readonly Brush StrokeBrush = Frozen(Color.FromRgb(0x0B, 0x08, 0x12)); // near-black outline

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); if (b.CanFreeze) b.Freeze(); return b; }

    private static TakeoverAnnouncerOverlay? _active;
    private static readonly Queue<string> _queue = new();
    private static bool _showing;

    /// <summary>
    /// Queue a takeover cue showing "TAKEOVER" + <paramref name="effectLabel"/>. No-op on a
    /// null/empty label or once the app is shutting down. Safe to call from any thread.
    /// </summary>
    public static void Announce(string? effectLabel)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(effectLabel)) return;
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.Invoke(() =>
            {
                _queue.Enqueue(effectLabel!);
                if (!_showing) ShowNext();
            });
        }
        catch (Exception ex) { App.Logger?.Debug("TakeoverAnnouncer.Announce: {E}", ex.Message); }
    }

    /// <summary>Drop any queued/visible cue and tear the window down (e.g. app teardown).</summary>
    public static void CloseActive()
    {
        try { _queue.Clear(); _showing = false; _active?.CloseNow(); } catch { }
    }

    private static void ShowNext()
    {
        if (_queue.Count == 0) { _showing = false; return; }
        _showing = true;
        var label = _queue.Dequeue();
        try
        {
            if (_active == null) { _active = new TakeoverAnnouncerOverlay(); ((Window)_active).Show(); }
            else if (!_active.IsVisible) { try { ((Window)_active).Show(); } catch { } }
            _active.Display(label);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("TakeoverAnnouncer.ShowNext: {E}", ex.Message);
            _showing = false;
        }
    }

    private readonly Grid _host;
    private readonly DispatcherTimer _life;
    private string? _pending;   // first Display can land before Loaded

    private TakeoverAnnouncerOverlay()
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

        _host = new Grid();
        Content = _host;

        SourceInitialized += (_, _) => ApplyExStyles();
        Loaded += (_, _) =>
        {
            if (_pending is { } p) { _pending = null; DisplayCore(p); }
        };

        _life = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(IN_MS + HOLD_MS) };
        _life.Tick += (_, _) =>
        {
            _life.Stop();
            var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(OUT_MS));
            fade.Completed += (_, _) =>
            {
                try { _host.Children.Clear(); } catch { }   // window stays — only content goes
                if (_queue.Count == 0) { try { Hide(); } catch { } }
                ShowNext();
            };
            BeginAnimation(OpacityProperty, fade);
        };
    }

    private void Display(string label)
    {
        if (!IsLoaded) { _pending = label; return; }
        DisplayCore(label);
    }

    private void DisplayCore(string label)
    {
        _life.Stop();
        _life.Interval = TimeSpan.FromMilliseconds(IN_MS + HOLD_MS);

        var eyebrow = new OutlinedText
        {
            Text = "TAKEOVER",
            FontSize = EYEBROW_FONT,
            Fill = EyebrowFill,
            Stroke = StrokeBrush,
            StrokeThickness = 2.0,
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.75,   // extra-faded eyebrow
        };
        eyebrow.Build();

        var effect = new OutlinedText
        {
            Text = label.ToUpperInvariant(),
            FontSize = EFFECT_FONT,
            Fill = EffectFill,
            Stroke = StrokeBrush,
            StrokeThickness = 3.0,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
        };
        effect.Build();

        var panel = new StackPanel { Orientation = Orientation.Vertical };
        panel.Children.Add(eyebrow);
        panel.Children.Add(effect);
        panel.HorizontalAlignment = HorizontalAlignment.Center;
        panel.VerticalAlignment = VerticalAlignment.Top;

        // Centered over the PRIMARY work area (this window spans the whole virtual screen, so
        // centering against the window itself drifts off-monitor with a second display).
        var wa = SystemParameters.WorkArea;
        var anchor = new Grid
        {
            Width = wa.Width,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(
                Math.Max(0, wa.Left - SystemParameters.VirtualScreenLeft),
                Math.Max(0, wa.Top - SystemParameters.VirtualScreenTop) + TOP_OFFSET_DIP, 0, 0),
        };
        anchor.Children.Add(panel);
        _host.Children.Clear();
        _host.Children.Add(anchor);

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, PEAK_OPACITY, TimeSpan.FromMilliseconds(IN_MS)));
        _life.Start();
    }

    private void CloseNow()
    {
        try { _life.Stop(); } catch { }
        try { _host.Children.Clear(); } catch { }
        if (ReferenceEquals(_active, this)) _active = null;
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
