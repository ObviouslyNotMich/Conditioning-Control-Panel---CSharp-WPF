using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;

using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Chaos;

public partial class ChaosHudWindow : Window
{
    private readonly global::ConditioningControlPanel.IAppLogger _logger;
    private readonly global::ConditioningControlPanel.Core.Services.Settings.ISettingsService? _settings;


    private readonly IChaosService? _chaos;
    private readonly ChaosRunState _state;
    private readonly AvaloniaChaosHudViewModel _vm = new();
    private bool _expanded;
    private int _lastShields;
    private bool _pinnedOpen;
    private bool _onRight;
    private double _hiddenX = -300;
    private bool _clockVisible;
    private bool _endRushOn;
    private bool _focusLowShown;
    private bool _cursorOnLive;
    private bool _preRunMode;
    private int _lastCombo;
    private int _streakTier;
    private DispatcherTimer? _streakJitterTimer;
    private readonly Random _streakRng = new();

    private const double OPEN_DWELL_MS = 1000;
    private const double EXPAND_GRACE_MS = 1000;
    private const double LEAVE_RECHECK_MS = 220;
    private const double HIDDEN_OFFSET = 300;
    private DateTime _expandedAt;
    private DispatcherTimer? _collapseRecheck;
    private DispatcherTimer? _openDwell;

    private readonly TranslateTransform _panelSlide = new(-300, 0);
    private readonly ScaleTransform _streakScale = new(1, 1);
    private readonly RotateTransform _streakRot = new(0);
    private readonly TranslateTransform _streakJitter = new(0, 0);

    public ChaosHudWindow() : this(new ChaosRunState(), null) { }

    public ChaosHudWindow(ChaosRunState state, IChaosService? chaos)
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
        _settings = App.Services.GetRequiredService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>();
Panel.RenderTransform = _panelSlide;
        StreakBlock.RenderTransform = new TransformGroup { Children = { _streakScale, _streakRot, _streakJitter } };
        _chaos = chaos;
        _state = state;
        _vm.Mirror(state);
        DataContext = _vm;

        Topmost = AvaloniaChaosWindowZ.BornTopmost;
        _lastShields = state.Shields;
        state.PropertyChanged += (_, args) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                _vm.Mirror(state);
                switch (args.PropertyName)
                {
                    case nameof(ChaosRunState.FocusLow): SetFocusLowVisual(state.FocusLow); break;
                    case nameof(ChaosRunState.Combo): OnComboChanged(state.Combo); break;
                    case nameof(ChaosRunState.RippleReady): SetRippleReadyVisual(state.RippleReady); break;
                    case nameof(ChaosRunState.ClockText): UpdateClockEndRush(); break;
                    case nameof(ChaosRunState.Shields):
                        int now = state.Shields;
                        bool grew = now > _lastShields;
                        _lastShields = now;
                        if (grew) { PulseShields(); FlashShields(gain: true); }
                        break;
                }
            });
        };
        SetFocusLowVisual(state.FocusLow);
        SetRippleReadyVisual(state.RippleReady);
        _lastCombo = state.Combo;
        OnComboChanged(state.Combo);

        var screens = AvaloniaChaosWindowZ.GetScreens();
        var primary = screens?.Primary;
        var wa = primary?.WorkingArea;
        Position = new PixelPoint((int)(wa?.X ?? 0), (int)(wa?.Y ?? 0));
        Height = (wa?.Height ?? 1080) * 0.6;
        ApplySide(_settings?.Current?.ChaosHudOnRight ?? false);
        LoadPortrait();
        AttachHudTips();
        Opened += (_, _) => ApplyExStyles();
        Closed += (_, _) => _streakJitterTimer?.Stop();
    }

    private void AttachHudTips()
    {
        // TODO: wire Avalonia tooltips for HUD elements once a shared tip service is available.
    }

    private void LoadPortrait()
    {
        var src = ChaosArt.Resolve("portraits", "neutral");
        Portrait.Source = src;
        PortraitHost.IsVisible = src != null;
    }

    private void Hud_PointerEntered(object? sender, PointerEventArgs e)
    {
        _collapseRecheck?.Stop();
        if (_expanded || _pinnedOpen) return;
        if (_openDwell == null)
        {
            _openDwell = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(OPEN_DWELL_MS) };
            _openDwell.Tick += (_, _) =>
            {
                _openDwell!.Stop();
                if (_expanded || _pinnedOpen) return;
                if (!Strip.IsPointerOver) return;
                Expand();
            };
        }
        _openDwell.Stop();
        _openDwell.Start();
    }

    private void Strip_PointerExited(object? sender, PointerEventArgs e) => _openDwell?.Stop();

    private void Strip_Click(object? sender, PointerPressedEventArgs e)
    {
        _openDwell?.Stop();
        if (!_expanded && !_pinnedOpen) Expand();
    }

    private void Expand()
    {
        _collapseRecheck?.Stop();
        if (_expanded) return;
        _expanded = true;
        _expandedAt = DateTime.UtcNow;
        Panel.IsVisible = true;
        Strip.IsVisible = false;
        AnimatePanel(0);
    }

    private void Hud_PointerExited(object? sender, PointerEventArgs e)
    {
        if (_pinnedOpen || !_expanded) return;
        double sinceOpen = (DateTime.UtcNow - _expandedAt).TotalMilliseconds;
        double wait = Math.Max(LEAVE_RECHECK_MS, EXPAND_GRACE_MS - sinceOpen);
        if (_collapseRecheck == null)
        {
            _collapseRecheck = new DispatcherTimer();
            _collapseRecheck.Tick += (_, _) =>
            {
                _collapseRecheck!.Stop();
                if (_pinnedOpen || !_expanded) return;
                if (Panel.IsPointerOver || Strip.IsPointerOver) return;
                Collapse();
            };
        }
        _collapseRecheck.Stop();
        _collapseRecheck.Interval = TimeSpan.FromMilliseconds(wait);
        _collapseRecheck.Start();
    }

    private void Collapse()
    {
        if (!_expanded) return;
        _expanded = false;
        AnimatePanel(_hiddenX, () =>
        {
            if (_expanded) return;
            Panel.IsVisible = false;
            Strip.IsVisible = true;
        });
    }

    private void ApplySide(bool onRight)
    {
        _onRight = onRight;
        _hiddenX = onRight ? HIDDEN_OFFSET : -HIDDEN_OFFSET;

        var screens = AvaloniaChaosWindowZ.GetScreens();
        var primary = screens?.Primary;
        var wa = primary?.WorkingArea;
        Position = new PixelPoint((int)((onRight ? (wa?.X ?? 0) + (wa?.Width ?? 1920) - Width : (wa?.X ?? 0))),
                                  (int)(wa?.Y ?? 0));

        var align = onRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        Strip.HorizontalAlignment = align;
        Panel.HorizontalAlignment = align;

        Strip.CornerRadius = Panel.CornerRadius = onRight
            ? new CornerRadius(0, 0, 0, 18) : new CornerRadius(0, 0, 18, 0);
        Strip.BorderThickness = Panel.BorderThickness = onRight
            ? new Thickness(8, 0, 0, 8) : new Thickness(0, 0, 8, 8);

        if (!_expanded) _panelSlide.X = _hiddenX;

        StyleSwitch(SideLeftBtn, SideRightBtn, SideLeftGlyph, SideRightGlyph, onRight);
        StyleSwitch(SideLeftBtn2, SideRightBtn2, SideLeftGlyph2, SideRightGlyph2, onRight);
    }

    private static void StyleSwitch(Border leftBtn, Border rightBtn, TextBlock leftGlyph, TextBlock rightGlyph, bool onRight)
    {
        var pink = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
        var dim = new SolidColorBrush(Color.FromArgb(0xAA, 0xB8, 0xB8, 0xD0));
        leftBtn.Background = onRight ? Brushes.Transparent : pink;
        rightBtn.Background = onRight ? pink : Brushes.Transparent;
        leftGlyph.Foreground = onRight ? dim : Brushes.Black;
        rightGlyph.Foreground = onRight ? Brushes.Black : dim;
    }

    private void SideLeft_Click(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (!_onRight) return;
        ApplySide(false);
        PersistSide(false);
    }

    private void SideRight_Click(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (_onRight) return;
        ApplySide(true);
        PersistSide(true);
    }

    private static void PersistSide(bool onRight)
    {
        try
        {
            var appSettings = App.Services?.GetService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>();
            var settings = appSettings?.Current;
            if (settings != null && settings.ChaosHudOnRight != onRight)
            {
                settings.ChaosHudOnRight = onRight;
                appSettings?.Save();
            }
        }
        catch (Exception ex) { App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Information("ChaosHud side persist: {E}", ex.Message); }
    }

    public void SetPreRunExpanded(bool pinned)
    {
        _pinnedOpen = pinned;
        if (pinned)
        {
            _expanded = true;
            Panel.IsVisible = true;
            Strip.IsVisible = false;
            AnimatePanel(0);
        }
        else if (!Panel.IsPointerOver)
        {
            Collapse();
        }
    }

    public void SetClockVisible(bool on)
    {
        _clockVisible = on;
        TxtRunTime.IsVisible = BarRunProgress.IsVisible = on;
    }

    private void UpdateClockEndRush()
    {
        try
        {
            double remaining = _state.RunDurationSec - _state.ElapsedSec;
            bool rush = _clockVisible && _state.ElapsedSec > 0 && remaining <= 10;
            if (rush == _endRushOn) return;
            _endRushOn = rush;
            var red = Color.FromRgb(0xFF, 0x5A, 0x5A);
            if (rush)
            {
                TxtStripClock.Foreground = new SolidColorBrush(red);
                TxtRunTime.Foreground = new SolidColorBrush(red);
                BarRunProgress.Foreground = new SolidColorBrush(red);
                StartBlink(new[] { TxtStripClock, TxtRunTime });
            }
            else
            {
                _blinkTimer?.Stop();
                TxtStripClock.Opacity = TxtRunTime.Opacity = 1.0;
                TxtStripClock.Foreground = Brushes.White;
                TxtRunTime.Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xD0));
                BarRunProgress.Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x7D, 0xBD));
            }
        }
        catch { }
    }

    private DispatcherTimer? _blinkTimer;
    private void StartBlink(TextBlock[] targets)
    {
        _blinkTimer?.Stop();
        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        bool high = true;
        _blinkTimer.Tick += (_, _) =>
        {
            high = !high;
            foreach (var t in targets) t.Opacity = high ? 1.0 : 0.25;
        };
        _blinkTimer.Start();
    }

    private void SetRippleReadyVisual(bool ready)
    {
        try
        {
            _vm.RippleReady = ready;
            // TODO: replace DropShadowEffect with BoxShadow or Avalonia v12 effect API.
        }
        catch { }
    }

    public void SetCursorOnLive(bool on)
    {
        if (on == _cursorOnLive) return;
        _cursorOnLive = on;
        try
        {
            // TODO: replace DropShadowEffect with BoxShadow or Avalonia v12 effect API.
        }
        catch { }
    }

    public void SetPausedUi(bool paused)
    {
        try
        {
            BtnHero.IsVisible = !paused;
            PauseChoiceRow.IsVisible = paused;
            var settings = _settings?.Current;
            TxtPauseHint.Text = settings?.PanicKeyEnabled == true
                ? $"⏸ HELD · {settings.PanicKey} again wakes you up"
                : "⏸ HELD · the hole waits";
            TxtPauseHint.IsVisible = paused;
            _pinnedOpen = paused;
            if (paused)
            {
                _expanded = true;
                Panel.IsVisible = true;
                Strip.IsVisible = false;
                AnimatePanel(0);
            }
            else if (!Panel.IsPointerOver)
            {
                Collapse();
            }
        }
        catch { }
    }

    public void SetHeroMode(bool preRun)
    {
        _preRunMode = preRun;
        BtnHero.Content = preRun ? "▶ FALL IN" : "⏸ PAUSE";
        BtnHero.IsVisible = true;
        BtnCloseMode.IsVisible = preRun;
        PauseChoiceRow.IsVisible = false;
    }

    private void PocketTile_Click(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Grid { Tag: string id } && !string.IsNullOrEmpty(id))
            _chaos?.UnequipFromSidebar(id);
        else
            _chaos?.OpenWarrenAt("enhance");
    }

    private void AnimatePanel(double toX, Action? done = null)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        double from = _panelSlide.X;
        double startMs = Environment.TickCount64;
        timer.Tick += (_, _) =>
        {
            double t = Math.Min(1, (Environment.TickCount64 - startMs) / 180.0);
            t = 1 - (1 - t) * (1 - t); // quad out
            _panelSlide.X = from + (toX - from) * t;
            if (t >= 1) { timer.Stop(); done?.Invoke(); }
        };
        timer.Start();
    }

    private void SetFocusLowVisual(bool low)
    {
        if (low == _focusLowShown) return;
        _focusLowShown = low;
        try
        {
            foreach (var el in new Control[] { FocusStripBlock, FocusPanelBlock })
                ApplyFocusSteadyVisual(el);
            var target = low ? Color.FromRgb(0xE0, 0x45, 0x45) : Color.FromRgb(0x5A, 0xC8, 0xFA);
            FocusStripBar.Foreground = new SolidColorBrush(target);
            FocusPanelBar.Foreground = new SolidColorBrush(target);
        }
        catch { }
    }

    private void ApplyFocusSteadyVisual(Control el)
    {
        if (_focusLowShown)
        {
            _focusPulseTimer?.Stop();
            _focusPulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            double startMs = Environment.TickCount64;
            _focusPulseTimer.Tick += (_, _) =>
            {
                double t = (Environment.TickCount64 - startMs) / 650.0;
                el.Opacity = 0.35 + 0.4 * ((Math.Sin(t * Math.PI * 2) + 1) / 2);
            };
            _focusPulseTimer.Start();
        }
        else
        {
            _focusPulseTimer?.Stop();
            el.Opacity = 1.0;
        }
    }

    private DispatcherTimer? _focusPulseTimer;

    public void FlashFocusBar()
    {
        try
        {
            foreach (var el in new Control[] { FocusStripBlock, FocusPanelBlock })
            {
                _focusPulseTimer?.Stop();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
                int cycles = 0;
                timer.Tick += (_, _) =>
                {
                    cycles++;
                    el.Opacity = cycles % 2 == 1 ? 0.12 : 1.0;
                    if (cycles >= 6) { timer.Stop(); ApplyFocusSteadyVisual(el); }
                };
                timer.Start();
            }
        }
        catch { }
    }

    private static readonly Color[] StreakTierColors =
    {
        Color.FromRgb(0xFF, 0xFF, 0xFF),
        Color.FromRgb(0xFF, 0xE0, 0x66),
        Color.FromRgb(0xFF, 0xA9, 0x4D),
        Color.FromRgb(0xFF, 0x5E, 0x5E),
        Color.FromRgb(0xFF, 0x2E, 0x88),
    };

    private static int StreakTierFor(int combo)
        => combo >= 35 ? 4 : combo >= 20 ? 3 : combo >= 10 ? 2 : combo >= 5 ? 1 : 0;

    private void OnComboChanged(int combo)
    {
        try
        {
            bool gained = combo > _lastCombo;
            bool dropped = combo < _lastCombo;
            _lastCombo = combo;
            _streakTier = StreakTierFor(combo);
            var tierColor = StreakTierColors[_streakTier];

            TxtStreakNum.Text = "x" + combo;
            TxtStreakNum.FontSize = 24 + _streakTier * 2.5;
            var brush = new SolidColorBrush(tierColor);
            TxtStreakNum.Foreground = brush;
            TxtStreakLbl.Foreground = _streakTier >= 2
                ? new SolidColorBrush(Color.FromArgb(0xCC, tierColor.R, tierColor.G, tierColor.B))
                : new SolidColorBrush(Color.FromArgb(0xAA, 0xB8, 0xB8, 0xD0));
            // TODO: replace DropShadowEffect with BoxShadow or Avalonia v12 effect API.

            if (gained)
            {
                _ = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16), Tag = Environment.TickCount64 };
                var colorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                double cStart = Environment.TickCount64;
                colorTimer.Tick += (_, _) =>
                {
                    double t = Math.Min(1, (Environment.TickCount64 - cStart) / 260.0);
                    ((SolidColorBrush)TxtStreakNum.Foreground!).Color = Blend(Colors.White, tierColor, t);
                    if (t >= 1) colorTimer.Stop();
                };
                colorTimer.Start();
                AnimateTransform(_streakScale, 1.30 + _streakTier * 0.06, 1.0, 340, EaseOutBack);
            }
            else if (dropped)
            {
                var colorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                double cStart = Environment.TickCount64;
                colorTimer.Tick += (_, _) =>
                {
                    double t = Math.Min(1, (Environment.TickCount64 - cStart) / 650.0);
                    var from = Color.FromRgb(0xFF, 0x38, 0x38);
                    ((SolidColorBrush)TxtStreakNum.Foreground!).Color = Blend(from, tierColor, Math.Max(0, t - 0.12));
                    if (t >= 1) colorTimer.Stop();
                };
                colorTimer.Start();
                AnimateShake(_streakJitter, 450);
                AnimateTransform(_streakScale, 0.80, 1.0, 380, EaseOutBack);
            }

            UpdateStreakJitter();
        }
        catch { }
    }

    private void UpdateStreakJitter()
    {
        bool hot = _streakTier >= 2;
        if (hot)
        {
            if (_streakJitterTimer == null)
            {
                _streakJitterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
                _streakJitterTimer.Tick += (_, _) =>
                {
                    try
                    {
                        double amp = (_streakTier - 1) * 0.9;
                        _streakJitter.X = (_streakRng.NextDouble() * 2 - 1) * amp;
                        _streakJitter.Y = (_streakRng.NextDouble() * 2 - 1) * amp;
                        _streakRot.Angle = (_streakRng.NextDouble() * 2 - 1) * (_streakTier - 1) * 1.6;
                    }
                    catch { }
                };
            }
            if (!_streakJitterTimer.IsEnabled) _streakJitterTimer.Start();
        }
        else
        {
            _streakJitterTimer?.Stop();
            _streakJitter.X = 0; _streakJitter.Y = 0; _streakRot.Angle = 0;
        }
    }

    public void FlashShields(bool gain)
    {
        try
        {
            var hot = gain ? Color.FromRgb(0x5A, 0xC8, 0xFA) : Color.FromRgb(0xFF, 0x38, 0x38);
            var brush = new SolidColorBrush(hot);
            TxtShields.Foreground = brush;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            double startMs = Environment.TickCount64;
            timer.Tick += (_, _) =>
            {
                double t = Math.Min(1, (Environment.TickCount64 - startMs) / 650.0);
                ((SolidColorBrush)TxtShields.Foreground!).Color = Blend(hot, Color.FromRgb(0xFF, 0x6E, 0xC7), Math.Max(0, t - 0.16));
                if (t >= 1) timer.Stop();
            };
            timer.Start();
        }
        catch { }
    }

    private void PulseShields()
    {
        try
        {
            if (TxtShields.RenderTransform is not ScaleTransform st) return;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            double startMs = Environment.TickCount64;
            timer.Tick += (_, _) =>
            {
                double t = Math.Min(1, (Environment.TickCount64 - startMs) / 420.0);
                double v = 1 + 0.35 * Math.Sin(t * Math.PI * 3) * (1 - t);
                st.ScaleX = st.ScaleY = v;
                if (t >= 1) { st.ScaleX = st.ScaleY = 1; timer.Stop(); }
            };
            timer.Start();
        }
        catch { }
    }

    private void BtnHero_Click(object? sender, RoutedEventArgs e)
    {
        if (_preRunMode) { _chaos?.StartRunFromSidebar(); return; }
        if (_chaos is { IsManuallyPaused: false }) _chaos.ToggleManualPause();
    }

    private void BtnResume_Click(object? sender, RoutedEventArgs e)
    {
        if (_chaos is { IsManuallyPaused: true }) _chaos.ToggleManualPause();
    }

    private void BtnExit_Click(object? sender, RoutedEventArgs e) => _chaos?.RequestStop();
    private void BtnCloseMode_Click(object? sender, RoutedEventArgs e) => _chaos?.CloseWarrenPhase();

    public void RaiseToTopmost() => AvaloniaChaosWindowZ.RaiseTopmost(this);

    private void ApplyExStyles()
    {
        // TODO: apply WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE on Windows.
    }

    private static void AnimateTransform(Transform target, double from, double to, int ms, Func<double, double> ease)
    {
        if (target is ScaleTransform st) st.ScaleX = st.ScaleY = from;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        double startMs = Environment.TickCount64;
        timer.Tick += (_, _) =>
        {
            double t = Math.Min(1, (Environment.TickCount64 - startMs) / ms);
            double v = from + (to - from) * ease(t);
            if (target is ScaleTransform s) s.ScaleX = s.ScaleY = v;
            if (t >= 1) timer.Stop();
        };
        timer.Start();
    }

    private static void AnimateShake(TranslateTransform target, int ms)
    {
        double[] xs = { 0, -9, 8, -6, 5, -3, 2, 0 };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        double startMs = Environment.TickCount64;
        timer.Tick += (_, _) =>
        {
            double t = Math.Min(1, (Environment.TickCount64 - startMs) / ms);
            int i = Math.Min((int)(t * (xs.Length - 1)), xs.Length - 2);
            double local =
t * (xs.Length - 1) - i;
            target.X = xs[i] + (xs[i + 1] - xs[i]) * local;
            if (t >= 1) { target.X = 0; timer.Stop(); }
        };
        timer.Start();
    }

    private static double EaseOutBack(double t)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        return t >= 1 ? 1 : 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }

    private static Color Blend(Color a, Color b, double t)
    {
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }
}
