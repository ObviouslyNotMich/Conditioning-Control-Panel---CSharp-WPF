using System;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel;

/// <summary>
/// Bottom-left build bar for a Chaos run: the drafted boons (as chips, with empty
/// slots for remaining capacity) plus HEAT and COMBO meters. Collapsed it shows a
/// thin strip; on hover it slides up. Only the painted strip/panel eats clicks —
/// the rest is alpha-0 and click-through. Bound to <see cref="ChaosRunState"/>;
/// reads as one connected corner with the left-edge HUD.
/// </summary>
public partial class ChaosBottomBarWindow : Window
{
    private readonly ChaosRunState _state;
    private bool _expanded;

    public ChaosBottomBarWindow(ChaosRunState state)
    {
        InitializeComponent();
        _state = state;
        DataContext = state;

        Width = Math.Min(660, SystemParameters.PrimaryScreenWidth);
        Left = 0;
        Top = SystemParameters.PrimaryScreenHeight - Height;

        _state.ActiveBoons.CollectionChanged += OnBoonsChanged;
        _state.ActiveCurses.CollectionChanged += OnBoonsChanged;
        RebuildBoons();

        SourceInitialized += (_, _) => ApplyExStyles();
        Closed += (_, _) =>
        {
            _state.ActiveBoons.CollectionChanged -= OnBoonsChanged;
            _state.ActiveCurses.CollectionChanged -= OnBoonsChanged;
        };
    }

    private void OnBoonsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess()) RebuildBoons();
        else Dispatcher.BeginInvoke(new Action(RebuildBoons));
    }

    private void RebuildBoons()
    {
        BoonStrip.Children.Clear();
        int filled = _state.ActiveBoons.Count + _state.ActiveCurses.Count;

        foreach (var b in _state.ActiveBoons) BoonStrip.Children.Add(BuildChip(b.Id, b.Name, isCurse: false));
        foreach (var c in _state.ActiveCurses) BoonStrip.Children.Add(BuildChip(c.Id, c.Name, isCurse: true));

        // Empty slots up to a nominal capacity (one boon per wave, capped for layout).
        int capacity = Math.Min(8, Math.Max(filled, _state.Config.WaveCount));
        for (int i = filled; i < capacity; i++) BoonStrip.Children.Add(BuildEmptySlot());

        StripBoons.Text = filled == 1 ? "1 boon" : $"{filled} boons";
    }

    private Border BuildChip(string id, string name, bool isCurse)
    {
        var accent = isCurse ? Color.FromRgb(0xFF, 0x8A, 0x8A) : Color.FromRgb(0x9C, 0xE8, 0xA0);

        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var iconSrc = ChaosArt.Resolve("boons", id);
        if (iconSrc != null)
            sp.Children.Add(new Image { Source = iconSrc, Width = 20, Height = 20, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        else
            sp.Children.Add(new TextBlock { Text = isCurse ? "☠" : "◈", Foreground = new SolidColorBrush(accent), FontSize = 13, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });

        sp.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });

        return new Border
        {
            Child = sp,
            Background = new SolidColorBrush(Color.FromArgb(40, accent.R, accent.G, accent.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(140, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(9, 5, 11, 5),
            Margin = new Thickness(0, 0, 8, 8)
        };
    }

    private static Border BuildEmptySlot() => new()
    {
        Width = 92,
        Height = 30,
        CornerRadius = new CornerRadius(8),
        Margin = new Thickness(0, 0, 8, 8),
        Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
        BorderThickness = new Thickness(1)
    };

    private void Bar_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_expanded) return;
        _expanded = true;
        Panel.Visibility = Visibility.Visible;
        var slide = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        PanelSlide.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void Bar_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_expanded) return;
        _expanded = false;
        var slide = new DoubleAnimation(172, TimeSpan.FromMilliseconds(160));
        slide.Completed += (_, _) => { if (!_expanded) Panel.Visibility = Visibility.Collapsed; };
        PanelSlide.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    // Don't steal focus / show in Alt+Tab. No WS_EX_TRANSPARENT — the bar must be
    // interactive; the unpainted alpha-0 region is click-through automatically.
    private void ApplyExStyles()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}
