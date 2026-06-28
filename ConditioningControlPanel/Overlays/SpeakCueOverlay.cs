using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ConditioningControlPanel;

/// <summary>
/// Full-screen, click-through overlay that holds a single voice-prompt cue (e.g. "Say YES")
/// near the top of the screen for as long as a <c>speak</c> Deeper effect is waiting on the
/// user. Unlike <see cref="TakeoverAnnouncerOverlay"/> this has NO auto-dismiss timer — it
/// stays up until <see cref="Hide"/> is called when the prompt completes or its band ends.
/// Used only for <see cref="Models.Deeper.SpeakCueMode.Persistent"/>; the intermittent mode
/// re-uses the subliminal flash path instead.
///
/// Like the other announcer overlays, ONE window is created on first use and KEPT ALIVE
/// between shows (each show just swaps the label) — creating/closing a layered window churns
/// the shared WPF render thread and can wedge it. It idles hidden (opacity 0) between shows.
/// </summary>
public sealed class SpeakCueOverlay : Window
{
    private const int FADE_MS = 180;
    private const double PEAK_OPACITY = 0.92;
    private const double CUE_FONT = 60;
    private const double TOP_OFFSET_DIP = 120;

    private static readonly Brush CueFill    = Frozen(Color.FromRgb(0xFF, 0xFF, 0xFF)); // white
    private static readonly Brush StrokeBrush = Frozen(Color.FromRgb(0x12, 0x06, 0x1A)); // near-black outline
    private static readonly Brush AccentFill  = Frozen(Color.FromRgb(0xFF, 0x8A, 0xD8)); // soft pink eyebrow

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); if (b.CanFreeze) b.Freeze(); return b; }

    private static SpeakCueOverlay? _active;

    /// <summary>Show (or update) the persistent cue text. Safe to call from any thread.</summary>
    public static void ShowCue(string? text)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.Invoke(() =>
            {
                if (_active == null) { _active = new SpeakCueOverlay(); ((Window)_active).Show(); }
                else if (!_active.IsVisible) { try { ((Window)_active).Show(); } catch { } }
                _active.Display(text!);
            });
        }
        catch (Exception ex) { App.Logger?.Debug("SpeakCueOverlay.Show: {E}", ex.Message); }
    }

    /// <summary>Fade the cue out and idle the window (kept alive). Safe to call from any thread.</summary>
    public static void HideCue()
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.Invoke(() => _active?.FadeOut());
        }
        catch (Exception ex) { App.Logger?.Debug("SpeakCueOverlay.Hide: {E}", ex.Message); }
    }

    /// <summary>Tear the window down entirely (e.g. app teardown).</summary>
    public static void CloseActive()
    {
        try { _active?.CloseNow(); } catch { }
    }

    private readonly Grid _host;
    private string? _pending;   // first Display can land before Loaded

    private SpeakCueOverlay()
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
    }

    private void Display(string text)
    {
        if (!IsLoaded) { _pending = text; return; }
        DisplayCore(text);
    }

    private void DisplayCore(string text)
    {
        var eyebrow = new OutlinedText
        {
            Text = "SAY IT",
            FontSize = 22,
            Fill = AccentFill,
            Stroke = StrokeBrush,
            StrokeThickness = 2.0,
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.8,
        };
        eyebrow.Build();

        var cue = new OutlinedText
        {
            Text = text,
            FontSize = CUE_FONT,
            Fill = CueFill,
            Stroke = StrokeBrush,
            StrokeThickness = 3.0,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
        };
        cue.Build();

        var panel = new StackPanel { Orientation = Orientation.Vertical };
        panel.Children.Add(eyebrow);
        panel.Children.Add(cue);
        panel.HorizontalAlignment = HorizontalAlignment.Center;
        panel.VerticalAlignment = VerticalAlignment.Top;

        // Centered over the PRIMARY work area (the window spans the whole virtual screen).
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

        BeginAnimation(OpacityProperty, new DoubleAnimation(Opacity, PEAK_OPACITY, TimeSpan.FromMilliseconds(FADE_MS)));
    }

    private void FadeOut()
    {
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(FADE_MS));
        fade.Completed += (_, _) =>
        {
            try { _host.Children.Clear(); } catch { }   // window stays — only content goes
            HideWindow();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    // Hide the kept-alive window (calls Window.Hide via the base type). Wrapped so the
    // fade-completed lambda doesn't reference `base` directly.
    private void HideWindow() { try { ((Window)this).Hide(); } catch { } }

    private void CloseNow()
    {
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
