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

/// <summary>What a Chaos announcement is about — drives the accent palette.</summary>
public enum ChaosAnnounceKind { Mantra, Temptation, Willpower, Depth, Streak, Item, PowerUp }

/// <summary>
/// Full-screen, click-through overlay that flashes a fast bordered "subtitle" line in the
/// upper third of the screen to announce a Chaos pickup/beat (mantra drafted, temptation
/// taken, willpower gained, depth reached, streak milestone). Modeled on
/// <see cref="ChaosFlashOverlay"/>: transparent topmost window, hit-test invisible, lives a
/// fraction of a second then closes. Announcements queue so two back-to-back beats play in
/// sequence instead of stomping each other. Master-gated on
/// <c>AppSettings.ChaosAnnouncerEnabled</c> so the whole feature is one switch.
/// </summary>
public sealed class ChaosAnnouncerOverlay : Window
{
    // ---- timing / layout tunables ----
    private const int IN_MS         = 110;    // scale-pop + fade-in
    private const int HOLD_MS       = 650;    // dwell
    private const int OUT_MS        = 240;    // fade-out ("fades almost immediately")
    private const double FONT_SIZE  = 60;
    private const double TOP_FRACTION = 0.26; // vertical anchor (upper third, clear of bubbles/HUD)

    private static ChaosAnnouncerOverlay? _active;
    private static readonly Queue<(string text, ChaosAnnounceKind kind)> _queue = new();
    private static bool _showing;

    /// <summary>Queue a bordered fading announcement. No-op unless the announcer is enabled.</summary>
    public static void Announce(string text, ChaosAnnounceKind kind)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (App.Settings?.Current?.ChaosAnnouncerEnabled != true) return;
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.Invoke(() =>
            {
                _queue.Enqueue((text, kind));
                if (!_showing) ShowNext();
            });
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosAnnouncer.Announce: {E}", ex.Message); }
    }

    /// <summary>Drop any queued/visible announcement (run teardown).</summary>
    public static void CloseActive()
    {
        try { _queue.Clear(); _showing = false; _active?.CloseNow(); } catch { }
    }

    private static void ShowNext()
    {
        if (_queue.Count == 0) { _showing = false; return; }
        _showing = true;
        var (text, kind) = _queue.Dequeue();
        try
        {
            _active?.CloseNow();
            _active = new ChaosAnnouncerOverlay(text, kind);
            ((Window)_active).Show();
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ChaosAnnouncer.ShowNext: {E}", ex.Message);
            _showing = false;
        }
    }

    private readonly DispatcherTimer _life;

    private ChaosAnnouncerOverlay(string text, ChaosAnnounceKind kind)
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

        var (fill, stroke) = Palette(kind);
        var scale = new ScaleTransform(0.85, 0.85);
        var label = new OutlinedText
        {
            Text = text.ToUpperInvariant(),
            FontSize = FONT_SIZE,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = 3.2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale,
        };
        Content = new Grid { Children = { label } };

        SourceInitialized += (_, _) => ApplyExStyles();
        Loaded += (_, _) =>
        {
            label.Build();
            label.Margin = new Thickness(0, Math.Max(0, Height * TOP_FRACTION), 0, 0);

            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(IN_MS)));
            var pop = new DoubleAnimation(0.85, 1.0, TimeSpan.FromMilliseconds(IN_MS + 70))
            { EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            _life.Start();
        };

        _life = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(IN_MS + HOLD_MS) };
        _life.Tick += (_, _) =>
        {
            _life.Stop();
            var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(OUT_MS));
            fade.Completed += (_, _) => { CloseNow(); ShowNext(); };
            BeginAnimation(OpacityProperty, fade);
        };
    }

    private void CloseNow()
    {
        try { _life.Stop(); } catch { }
        if (ReferenceEquals(_active, this)) _active = null;
        try { Close(); } catch { }
    }

    /// <summary>Accent (fill, stroke) per announcement kind. Stroke stays a near-black for contrast.</summary>
    private static (Brush fill, Brush stroke) Palette(ChaosAnnounceKind kind)
    {
        Color c = kind switch
        {
            ChaosAnnounceKind.Mantra     => Color.FromRgb(0xFF, 0xD2, 0x7A), // warm gold
            ChaosAnnounceKind.Temptation => Color.FromRgb(0xFF, 0x6B, 0x6B), // risky red
            ChaosAnnounceKind.Willpower  => Color.FromRgb(0x7A, 0xE0, 0xFF), // cyan
            ChaosAnnounceKind.Depth      => Color.FromRgb(0xFF, 0xFF, 0xFF), // white
            ChaosAnnounceKind.Streak     => Color.FromRgb(0xFF, 0xC8, 0x3C), // bright gold
            ChaosAnnounceKind.Item       => Color.FromRgb(0x7A, 0xFF, 0xD2), // mint
            ChaosAnnounceKind.PowerUp    => Color.FromRgb(0x9C, 0xE8, 0xA0), // green
            _                            => Colors.White,
        };
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
