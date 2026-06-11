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

    private int _lastShields;

    public ChaosHudWindow(ChaosRunState state, ChaosModeService chaos)
    {
        InitializeComponent();
        _chaos = chaos;
        DataContext = state;

        // Muscle Memory capstone feedback: pulse the resistance hearts whenever they GROW
        // (regen or a boon) so the player always knows a point came back. Window outlives no
        // run (closed in CleanupAfterRun), so no unsubscribe bookkeeping is needed.
        _lastShields = state.Shields;
        state.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ChaosRunState.FocusLow))
            {
                SetFocusLowVisual(state.FocusLow);
                return;
            }
            if (args.PropertyName != nameof(ChaosRunState.Shields)) return;
            int now = state.Shields;
            bool grew = now > _lastShields;
            _lastShields = now;
            if (grew) PulseShields();
        };
        SetFocusLowVisual(state.FocusLow);

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

    private bool _pinnedOpen;   // pre-run loadout glance: panel stays open until SINK fires

    private void Hud_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_expanded) return;
        _expanded = true;
        Panel.Visibility = Visibility.Visible;
        Strip.Visibility = Visibility.Hidden;   // the panel is translucent — don't let the strip bleed through
        Animate(0);
    }

    private void Hud_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_pinnedOpen) return;
        Collapse();
    }

    private void Collapse()
    {
        if (!_expanded) return;
        _expanded = false;
        var slide = new DoubleAnimation(-300, TimeSpan.FromMilliseconds(180));
        slide.Completed += (_, _) =>
        {
            if (_expanded) return;
            Panel.Visibility = Visibility.Collapsed;
            Strip.Visibility = Visibility.Visible;
        };
        PanelSlide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slide);
    }

    /// <summary>Pin the panel open for the pre-run loadout glance (FALL IN → countdown), then
    /// release it when the run begins — it folds away unless the mouse is parked on it.</summary>
    public void SetPreRunExpanded(bool pinned)
    {
        _pinnedOpen = pinned;
        if (pinned)
        {
            _expanded = true;
            Panel.Visibility = Visibility.Visible;
            Strip.Visibility = Visibility.Hidden;
            Animate(0);
        }
        else if (!Panel.IsMouseOver)
        {
            Collapse();
        }
    }

    /// <summary>Pocket Watch gate: the run clock + its fill bar only exist for players wearing
    /// the charm — without it, how long you've been under stays a mystery.</summary>
    public void SetClockVisible(bool on) =>
        TxtRunTime.Visibility = BarRunProgress.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

    private bool _preRunMode;

    /// <summary>Warren-phase sidebar: the hero button reads FALL IN and starts the run from here;
    /// on the in-run HUD it reads PAUSE (and pausing asks continue-or-wake-up).</summary>
    public void SetHeroMode(bool preRun)
    {
        _preRunMode = preRun;
        BtnHero.Content = preRun ? "▶ FALL IN" : "⏸ PAUSE";
        BtnHero.Visibility = Visibility.Visible;
        BtnCloseMode.Visibility = preRun ? Visibility.Visible : Visibility.Collapsed;
        PauseChoiceRow.Visibility = Visibility.Collapsed;
    }

    /// <summary>A pocket tile was clicked: a filled tile unequips its boon (the service ignores
    /// it once SINK has fired); an empty "+" tile brings the Warren forward on Enhancements.</summary>
    private void PocketTile_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id } && !string.IsNullOrEmpty(id))
            _chaos.UnequipFromSidebar(id);
        else
            _chaos.OpenWarrenAt("enhance");
    }

    private void Animate(double toX)
    {
        var slide = new DoubleAnimation(toX, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        PanelSlide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slide);
    }

    private bool _focusLowShown;

    /// <summary>Focus below a defuse's cost: both bars dim and pulse softly — a readable
    /// "don't touch the live ones" warning. Restores full opacity the moment focus recovers.</summary>
    private void SetFocusLowVisual(bool low)
    {
        if (low == _focusLowShown) return;
        _focusLowShown = low;
        try
        {
            foreach (var el in new FrameworkElement[] { FocusStripBlock, FocusPanelBlock })
            {
                if (low)
                {
                    var pulse = new DoubleAnimation(0.75, 0.35, TimeSpan.FromMilliseconds(650))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                    };
                    el.BeginAnimation(OpacityProperty, pulse);
                }
                else
                {
                    el.BeginAnimation(OpacityProperty, null);
                    el.Opacity = 1.0;
                }
            }
        }
        catch { }
    }

    /// <summary>Brief scale pop on the resistance hearts (a regen/gain just landed).</summary>
    private void PulseShields()
    {
        try
        {
            if (TxtShields.RenderTransform is not System.Windows.Media.ScaleTransform st) return;
            var pulse = new DoubleAnimation(1.35, 1.0, TimeSpan.FromMilliseconds(420))
            {
                EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 5 }
            };
            st.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, pulse);
            st.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, pulse);
        }
        catch { }
    }

    private void BtnHero_Click(object sender, RoutedEventArgs e)
    {
        if (_preRunMode) { _chaos.StartRunFromSidebar(); return; }
        // Pause the descent and ask what they actually want.
        if (!_chaos.IsManuallyPaused) _chaos.ToggleManualPause();
        BtnHero.Visibility = Visibility.Collapsed;
        PauseChoiceRow.Visibility = Visibility.Visible;
    }

    private void BtnResume_Click(object sender, RoutedEventArgs e)
    {
        if (_chaos.IsManuallyPaused) _chaos.ToggleManualPause();
        BtnHero.Visibility = Visibility.Visible;
        PauseChoiceRow.Visibility = Visibility.Collapsed;
    }

    private void BtnExit_Click(object sender, RoutedEventArgs e) => _chaos.RequestStop();

    /// <summary>Pre-run ✖ beside FALL IN: leave the rabbit hole entirely (Warren + sidebar).</summary>
    private void BtnCloseMode_Click(object sender, RoutedEventArgs e) => _chaos.CloseWarrenPhase();

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

    /// <summary>Re-assert the HUD to the top of the topmost band without stealing focus, so it
    /// stays visible over a mandatory video that a chaos payload raised mid-run.</summary>
    public void RaiseToTopmost()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
