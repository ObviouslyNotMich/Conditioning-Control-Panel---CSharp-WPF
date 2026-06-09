using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ConditioningControlPanel;

/// <summary>
/// A small, faint, color-coded word that pops at a bubble's location the instant its effect
/// fires (classic "floating combat text" for clarity in a fast game). Modeled on
/// <see cref="ChaosAnnouncerOverlay"/> but tiny and positional: one little click-through
/// window anchored over the bubble, a quick rise + fade, gone in ~half a second. Uses the
/// geometry-stroked <see cref="OutlinedText"/> (no pixel-shader effects) so several at once
/// stay cheap. Master-gated on the same <c>ChaosAnnouncerEnabled</c> switch as the big
/// announcer, so on-screen Chaos text is one toggle.
/// </summary>
public sealed class ChaosPopText : Window
{
    // ---- timing / layout tunables (fast: ~0.5s total, the action-game standard) ----
    private const int    IN_MS      = 60;    // fade + scale in
    private const int    HOLD_MS    = 230;   // dwell
    private const int    OUT_MS     = 200;   // fade out
    private const double FONT_SIZE  = 22;    // small
    private const double PEAK_OPAC  = 0.58;  // faint, not solid
    private const double RISE_DIP   = 22;    // how far the word drifts upward over its life
    private const double WIN_W      = 280;
    private const double WIN_H      = 88;

    /// <summary>
    /// Flash <paramref name="text"/> at a screen point (DIPs, matching bubble-window coords),
    /// tinted by <paramref name="color"/>. No-op when the text is empty or Chaos on-screen text
    /// is disabled. Safe to call from any thread.
    /// </summary>
    public static void Show(double anchorXDip, double anchorYDip, string text, Color color)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (App.Settings?.Current?.ChaosAnnouncerEnabled != true) return;
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(new Action(() =>
            {
                try { new ChaosPopText(anchorXDip, anchorYDip, text, color).Show(); }
                catch (Exception ex) { App.Logger?.Debug("ChaosPopText.Show inner: {E}", ex.Message); }
            }));
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosPopText.Show: {E}", ex.Message); }
    }

    private ChaosPopText(double anchorXDip, double anchorYDip, string text, Color color)
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
        Width = WIN_W;
        Height = WIN_H;
        Left = anchorXDip - WIN_W / 2;
        Top = anchorYDip - WIN_H / 2;
        Opacity = 0;

        var (fill, stroke) = Palette(color);
        var rise = new TranslateTransform(0, 6);
        var label = new OutlinedText
        {
            Text = text.ToUpperInvariant(),
            FontSize = FONT_SIZE,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = 2.0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = rise,
        };
        Content = new Grid { Children = { label } };

        SourceInitialized += (_, _) => ApplyExStyles();
        Loaded += (_, _) =>
        {
            label.Build();

            // Fade in fast; a one-shot timer holds, then fades out and closes.
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, PEAK_OPAC, TimeSpan.FromMilliseconds(IN_MS)));
            var timer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(IN_MS + HOLD_MS) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var outAnim = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(OUT_MS));
                outAnim.Completed += (_, _) => CloseNow();
                BeginAnimation(OpacityProperty, outAnim);
            };
            timer.Start();

            // Gentle upward drift across the whole life so the word reads as a rising pop.
            rise.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(6, -RISE_DIP, TimeSpan.FromMilliseconds(IN_MS + HOLD_MS + OUT_MS)));
        };
    }

    private void CloseNow()
    {
        try { Close(); } catch { }
    }

    /// <summary>Lift the bubble's tint toward white so the faint word still reads over any desktop.</summary>
    private static (Brush fill, Brush stroke) Palette(Color tint)
    {
        byte Lift(byte c) => (byte)Math.Clamp(c + (255 - c) * 0.28, 0, 255);
        var c = Color.FromRgb(Lift(tint.R), Lift(tint.G), Lift(tint.B));
        var fill = new SolidColorBrush(c); if (fill.CanFreeze) fill.Freeze();
        var stroke = new SolidColorBrush(Color.FromRgb(0x0B, 0x08, 0x12)); if (stroke.CanFreeze) stroke.Freeze();
        return (fill, stroke);
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
