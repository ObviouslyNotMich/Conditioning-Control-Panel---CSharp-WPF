using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel;

/// <summary>
/// Thin left-edge HUD for a Chaos run. Collapsed it shows a compact strip (clock,
/// score, multiplier); on hover it slides out the full roguelite stack (boons,
/// curses, shields, multiplier breakdown, payload feed, controls). The window only
/// paints its left column — the rest is alpha-0 and click-through, so the desktop
/// stays fully usable during a run. Bound to <see cref="ChaosRunState"/>.
/// </summary>
public partial class ChaosHudWindow : Window
{
    private readonly ChaosModeService _chaos;
    private bool _expanded;

    public ChaosHudWindow(ChaosRunState state, ChaosModeService chaos)
    {
        InitializeComponent();
        _chaos = chaos;
        DataContext = state;

        // Top-anchored and ~60% of the work-area height, so it doesn't span the whole
        // screen (shrinks from the bottom up).
        var wa = SystemParameters.WorkArea;
        Left = wa.Left;
        Top = wa.Top;
        Height = wa.Height * 0.6;
        LoadPortrait();
        SourceInitialized += (_, _) => ApplyExStyles();
    }

    /// <summary>
    /// Sidebar portrait slot. Resolves art by convention (phase 5 wires mood swapping);
    /// with no art file present the host falls back to its tinted placeholder.
    /// </summary>
    private void LoadPortrait()
    {
        var src = ChaosArt.Resolve("portraits", "neutral");
        Portrait.Source = src;
        if (src == null) PortraitHost.Visibility = Visibility.Collapsed;
    }

    private void Hud_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_expanded) return;
        _expanded = true;
        Panel.Visibility = Visibility.Visible;
        Animate(0);
    }

    private void Hud_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_expanded) return;
        _expanded = false;
        var slide = new DoubleAnimation(-300, TimeSpan.FromMilliseconds(180));
        slide.Completed += (_, _) => { if (!_expanded) Panel.Visibility = Visibility.Collapsed; };
        PanelSlide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slide);
    }

    private void Animate(double toX)
    {
        var slide = new DoubleAnimation(toX, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        PanelSlide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slide);
    }

    private void BtnPause_Click(object sender, RoutedEventArgs e)
    {
        _chaos.ToggleManualPause();
        BtnPause.Content = _chaos.IsManuallyPaused ? "▶ resume" : "⏸ pause";
    }
    private void BtnStop_Click(object sender, RoutedEventArgs e) => _chaos.RequestStop();

    // Don't steal focus / show in Alt+Tab. (No WS_EX_TRANSPARENT — the HUD must be
    // interactive; the unpainted alpha-0 region is click-through automatically.)
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
