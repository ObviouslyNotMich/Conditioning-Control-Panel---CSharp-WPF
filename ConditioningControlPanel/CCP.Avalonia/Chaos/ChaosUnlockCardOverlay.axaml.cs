using System;
using System.Collections.Generic;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Threading;

using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of ChaosUnlockCardOverlay: dim scrim with a centered unlock card.
/// </summary>
public partial class ChaosUnlockCardOverlay : Window
{
    private const int IN_MS = 160;
    private const int OUT_MS = 150;
    private const int AUTO_DISMISS_MS = 12000;

    private static ChaosUnlockCardOverlay? _active;
    private static readonly Queue<(ChaosUnlockCardData data, Action? onDismissed, bool autoDismiss)> _queue = new();
    private static bool _showing;

    public static bool IsShowing => _showing || _queue.Count > 0;

    private readonly Grid _host;
    private readonly DispatcherTimer _auto = new();
    private Action? _onDismissed;
    private bool _dismissing;
    private (ChaosUnlockCardData data, Action? onDismissed, bool autoDismiss)? _pending;
    private OpacityFade? _fade;

    public ChaosUnlockCardOverlay()
    {
        InitializeComponent();

WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = AvaloniaChaosWindowZ.BornTopmost;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var screens = AvaloniaChaosWindowZ.GetScreens();
        var all = screens?.All;
        if (all == null || all.Count == 0)
        {
            Position = new PixelPoint(0, 0);
            Width = 1920;
            Height = 1080;
        }
        else
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var s in all)
            {
                var b = s.Bounds;
                minX = Math.Min(minX, b.X);
                minY = Math.Min(minY, b.Y);
                maxX = Math.Max(maxX, b.Right);
                maxY = Math.Max(maxY, b.Bottom);
            }
            Position = new PixelPoint((int)minX, (int)minY);
            Width = maxX - minX;
            Height = maxY - minY;
        }
        Opacity = 0;

        _host = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x66, 0x00, 0x00, 0x00)) };
        Content = _host;
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) Dismiss();
        };

        Opened += (_, _) => ApplyExStyles();
        Loaded += (_, _) =>
        {
            if (_pending is { } p) { _pending = null; DisplayCore(p.data, p.onDismissed, p.autoDismiss); }
        };

        _auto.Interval = TimeSpan.FromMilliseconds(AUTO_DISMISS_MS);
        _auto.Tick += (_, _) => Dismiss();
    }

    public static void Show(ChaosUnlockCardData data, Action? onDismissed = null, bool autoDismiss = true)
    {
        try
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                _queue.Enqueue((data, onDismissed, autoDismiss));
                if (!_showing) ShowNext();
            });
        }
        catch (Exception ex) { App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Information("ChaosUnlockCard.Show: {E}", ex.Message); }
    }

    public static void CloseActive()
    {
        try { _queue.Clear(); _showing = false; _active?.CloseNow(); } catch { }
    }

    private static void ShowNext()
    {
        if (_queue.Count == 0) { _showing = false; return; }
        _showing = true;
        var (data, onDismissed, autoDismiss) = _queue.Dequeue();
        try
        {
            if (_active == null) { _active = new ChaosUnlockCardOverlay(); ((global::Avalonia.Controls.Window)_active).Show(); }
            else if (!_active.IsVisible) { try { ((global::Avalonia.Controls.Window)_active).Show(); } catch { } }
            _active.Display(data, onDismissed, autoDismiss);
        }
        catch (Exception ex)
        {
            App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Information("ChaosUnlockCard.ShowNext: {E}", ex.Message);
            _showing = false;
            try { onDismissed?.Invoke(); } catch { }
        }
    }

    private void Display(ChaosUnlockCardData data, Action? onDismissed, bool autoDismiss)
    {
        if (!IsLoaded) { _pending = (data, onDismissed, autoDismiss); return; }
        DisplayCore(data, onDismissed, autoDismiss);
    }

    private void DisplayCore(ChaosUnlockCardData data, Action? onDismissed, bool autoDismiss)
    {
        _auto.Stop();
        _onDismissed = onDismissed;
        _dismissing = false;

        var card = ChaosUnlockCards.BuildCardVisual(data, 420);
        var hint = new TextBlock
        {
            Text = "click to continue",
            Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
            FontSize = 12,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var panel = new StackPanel
        {
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
        };
        panel.Children.Add(card);
        panel.Children.Add(hint);

        var wa = GetPrimaryWorkArea();
        var area = new Grid
        {
            Width = wa.Width,
            Height = wa.Height,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(wa.X - VirtualLeft(), wa.Y - VirtualTop(), 0, 0),
        };
        area.Children.Add(panel);

        var scale = new ScaleTransform(0.9, 0.9);
        panel.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        panel.RenderTransform = scale;

        _host.Children.Clear();
        _host.Children.Add(area);

        _fade?.Dispose();
        _fade = new OpacityFade(this, 0, 1, IN_MS);
        var pop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        double startMs = Environment.TickCount64;
        double durMs = IN_MS + 80;
        pop.Tick += (_, _) =>
        {
            double t = Math.Min(1, (Environment.TickCount64 - startMs) / durMs);
            double eased = 0.9 + 0.1 * EaseOutBack(t);
            scale.ScaleX = eased;
            scale.ScaleY = eased;
            if (t >= 1) pop.Stop();
        };
        pop.Start();

        if (autoDismiss) _auto.Start();
    }

    private static double EaseOutBack(double t)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }

    private void Dismiss()
    {
        if (_dismissing || !_showing) return;
        _dismissing = true;
        _auto.Stop();
        var done = _onDismissed; _onDismissed = null;

        if (_queue.Count > 0)
        {
            try { done?.Invoke(); } catch { }
            ShowNext();
            return;
        }

        _fade?.Dispose();
        _fade = new OpacityFade(this, Opacity, 0, OUT_MS, () =>
        {
            try { _host.Children.Clear(); } catch { }
            _showing = false;
            try { Hide(); } catch { }
            try { done?.Invoke(); } catch { }
            if (_queue.Count > 0) ShowNext();
        });
    }

    private void CloseNow()
    {
        try { _auto.Stop(); } catch { }
        try { _host.Children.Clear(); } catch { }
        _onDismissed = null;
        if (ReferenceEquals(_active, this)) _active = null;
        try { Close(); } catch { }
    }

    private void ApplyExStyles()
    {
        // TODO: apply WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE on Windows (NOT TRANSPARENT — needs click).
    }

    private static Rect GetPrimaryWorkArea()
    {
        var screens = AvaloniaChaosWindowZ.GetScreens();
        var primary = screens?.Primary;
        if (primary == null) return new Rect(0, 0, 1920, 1080);
        var wa = primary.WorkingArea;
        return new Rect(wa.X, wa.Y, wa.Width, wa.Height);
    }

    private static double VirtualLeft()
    {
        var screens = AvaloniaChaosWindowZ.GetScreens();
        if (screens?.All.Count == 0) return 0;
        double minX = double.MaxValue;
        foreach (var s in screens!.All) minX = Math.Min(minX, s.Bounds.X);
        return minX;
    }

    private static double VirtualTop()
    {
        var screens = AvaloniaChaosWindowZ.GetScreens();
        if (screens?.All.Count == 0) return 0;
        double minY = double.MaxValue;
        foreach (var s in screens!.All) minY = Math.Min(minY, s.Bounds.Y);
        return minY;
}
}
