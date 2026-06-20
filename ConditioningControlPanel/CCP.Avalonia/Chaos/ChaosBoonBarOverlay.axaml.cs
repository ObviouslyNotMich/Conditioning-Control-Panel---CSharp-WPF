using System;
using System.Collections.Generic;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Threading;

using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of ChaosBoonBarOverlay: horizontal ribbon of run-pick tiles across the top.
/// Created once at run start and rebuilt in place.
/// </summary>
public partial class ChaosBoonBarOverlay : Window
{
    private const double TILE = 42;
    private const double CLOCK_RESERVE = 220;

    private static ChaosBoonBarOverlay? _active;

    private readonly Border _pill;
    private readonly StackPanel _row;

    public ChaosBoonBarOverlay()
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
        SizeToContent = SizeToContent.WidthAndHeight;

        _row = new StackPanel { Orientation = Orientation.Horizontal };
        _pill = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(150, 0x12, 0x0E, 0x1E)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(140, 0xE8, 0x43, 0x93)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 6, 10, 6),
            IsVisible = false,
            Child = _row,
        };
        Content = _pill;

        var wa = GetWorkArea();
        Position = new PixelPoint((int)(wa.Right - CLOCK_RESERVE - 200), (int)(wa.Y + 10));
        SizeChanged += (_, _) =>
        {
            try
            {
                var area = GetWorkArea();
                Position = new PixelPoint((int)(area.Right - CLOCK_RESERVE - Width), Position.Y);
            }
            catch { }
        };

        Opened += (_, _) => ApplyExStyles();
    }

    public static void EnsureCreated()
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try { if (_active == null) { _active = new ChaosBoonBarOverlay(); ((global::Avalonia.Controls.Window)_active).Show(); } }
                catch (Exception ex) { App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Information("ChaosBoonBar.EnsureCreated: {E}", ex.Message); }
            });
        }
        catch { }
    }

    public static void SetPicks(IReadOnlyList<ChaosSidebarBoon> picks)
    {
        try
        {
            var snapshot = new List<ChaosSidebarBoon>(picks);
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (_active == null) { _active = new ChaosBoonBarOverlay(); ((global::Avalonia.Controls.Window)_active).Show(); }
                    _active.Rebuild(snapshot);
                }
                catch (Exception ex) { App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Information("ChaosBoonBar.SetPicks: {E}", ex.Message); }
            });
        }
        catch { }
    }

    public static void Clear()
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try { if (_active != null) _active._pill.IsVisible = false; }
                catch { }
            });
        }
        catch { }
    }

    public static void RaiseActive() => AvaloniaChaosWindowZ.RaiseTopmost(_active);

    public static void CloseActive()
    {
        try
        {
            Dispatcher.UIThread.Post(() => { try { _active?.CloseNow(); } catch { } });
        }
        catch { }
    }

    private void Rebuild(List<ChaosSidebarBoon> picks)
    {
        _row.Children.Clear();
        if (picks.Count == 0) { _pill.IsVisible = false; return; }
        _pill.IsVisible = true;

        foreach (var p in picks)
            _row.Children.Add(BuildTile(p));
    }

    private static Control BuildTile(ChaosSidebarBoon b)
    {
        var inner = new Grid
        {
            Clip = new RectangleGeometry(new Rect(0, 0, TILE, TILE), 10, 10),
        };
        if (b.Icon == null)
            inner.Children.Add(new TextBlock
            {
                Text = b.Glyph,
                FontSize = 19,
                Foreground = b.AccentBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        else
            inner.Children.Add(new Image { Source = b.Icon, Stretch = Stretch.UniformToFill });
        inner.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderBrush = b.AccentBrush,
            BorderThickness = new Thickness(3),
            IsHitTestVisible = false,
        });

        var tile = new Border
        {
            Width = TILE,
            Height = TILE,
            CornerRadius = new CornerRadius(10),
            Background = b.TileBackBrush,
            Margin = new Thickness(0, 0, 6, 0),
            Child = inner,
        };
        ToolTip.SetTip(tile, BuildTip(b));
        return tile;
    }

    private static Control BuildTip(ChaosSidebarBoon b)
    {
        var panel = new StackPanel { MaxWidth = 240 };
        panel.Children.Add(new TextBlock
        {
            Text = b.Name, FontWeight = FontWeight.Bold, FontSize = 13, Foreground = b.AccentBrush,
        });
        if (!string.IsNullOrEmpty(b.Desc))
            panel.Children.Add(new TextBlock
            {
                Text = b.Desc, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xE0, 0xF0)),
            });
        if (!string.IsNullOrEmpty(b.Flavor))
            panel.Children.Add(new TextBlock
            {
                Text = b.Flavor, FontStyle = FontStyle.Italic, FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xC8)),
            });
        return new ToolTip
        {
            Content = panel,
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x15, 0x12, 0x26)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0x69, 0xB4)),
            Foreground = Brushes.White,
            Padding = new Thickness(10, 8, 10, 8),
        };
    }

    private void CloseNow()
    {
        if (ReferenceEquals(_active, this)) _active = null;
        try { Close(); } catch { }
    }

    private void ApplyExStyles()
    {
        // TODO: apply WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE on Windows (no WS_EX_TRANSPARENT — want hover).
    }

    private static Rect GetWorkArea()
    {
        var screens = AvaloniaChaosWindowZ.GetScreens();
        var primary = screens?.Primary;
        if (primary == null) return new Rect(0, 0, 1920, 1080);
        var wa =
primary.WorkingArea;
        return new Rect(wa.X, wa.Y, wa.Width, wa.Height);
    }
}
