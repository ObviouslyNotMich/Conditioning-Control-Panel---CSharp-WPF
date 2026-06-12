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
    private readonly ChaosRunState _state;
    private bool _expanded;

    private int _lastShields;

    public ChaosHudWindow(ChaosRunState state, ChaosModeService chaos)
    {
        InitializeComponent();
        _chaos = chaos;
        _state = state;
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
            if (args.PropertyName == nameof(ChaosRunState.Combo))
            {
                OnComboChanged(state.Combo);
                return;
            }
            if (args.PropertyName == nameof(ChaosRunState.RippleReady))
            {
                SetRippleReadyVisual(state.RippleReady);
                return;
            }
            if (args.PropertyName == nameof(ChaosRunState.ClockText))
            {
                UpdateClockEndRush();
                return;
            }
            if (args.PropertyName != nameof(ChaosRunState.Shields)) return;
            int now = state.Shields;
            bool grew = now > _lastShields;
            _lastShields = now;
            if (grew) { PulseShields(); FlashShields(gain: true); }
        };
        SetFocusLowVisual(state.FocusLow);
        SetRippleReadyVisual(state.RippleReady);
        _lastCombo = state.Combo;
        OnComboChanged(state.Combo);                       // seed the tier visuals
        Closed += (_, _) => _streakJitterTimer?.Stop();    // never outlive the window

        // Top-anchored and ~60% of the work-area height, so it doesn't span the whole
        // screen (shrinks from the bottom up).
        var wa = SystemParameters.WorkArea;
        Left = wa.Left;
        Top = wa.Top;
        Height = wa.Height * 0.6;
        LoadPortrait();
        AttachHudTips();
        SourceInitialized += (_, _) => ApplyExStyles();
    }

    /// <summary>Themed hover cards for every sidebar element — exact numbers, lexicon voice.
    /// One text per concept, attached to both its strip and panel surfaces.</summary>
    private void AttachHudTips()
    {
        try
        {
            const string TIP_CLOCK =
                "how long you've been down this descent (minutes:seconds).";
            const string TIP_SCORE =
                "every pop and snap pays base points x the multiplier stack. at the recap the score banks into drops ✦.";
            const string TIP_MULT =
                "the whole stack multiplied out: streak x difficulty x lust x mantras (sins can stretch it further). every payout is scaled by this.";
            const string TIP_STREAK =
                "+1 per pop or snap. each point adds +0.08x to the stack, capped at x6.0. a treat left to rot HALVES it; an unblocked trigger ZEROES it. it heats up at 5 / 10 / 20 / 35.";
            const string TIP_FOCUS =
                "the defuse fuel. a hold costs 30 (15 per bound half). treats refill +10, rabbits and heavy drops +15, a denied tease +10. max 100, you fall in with 50. pressing a live bubble with less than 30 detonates it in your grip. snaps during a freeze are free.";
            const string TIP_RESIST =
                "each ♥ absorbs one trigger: the effect still washes past, but your streak and lust survive (some sins demand 2). with none left, a trigger zeroes both. you fall in with 0 — charms, hearts and mantras grant it.";
            const string TIP_LUST =
                "climbs while you perform (each snap +0.07) and pays up to x2.0 at full burn — the orange bar. an unblocked trigger cools it to zero.";
            const string TIP_RIPPLE =
                "the right-click wave. cast it near the bubbles: treats pop paid, trances snap clean, rabbits get flung. one charge, gathered back over time — READY means it's in your hand.";

            ChaosTips.Attach(TxtStripClock, "the fall", TIP_CLOCK);
            ChaosTips.Attach(TxtStripScore, "score", TIP_SCORE);
            ChaosTips.Attach(TxtStripMult, "the multiplier", TIP_MULT);
            ChaosTips.Attach(StreakBlock, "streak", TIP_STREAK);
            ChaosTips.Attach(FocusStripBlock, "focus", TIP_FOCUS);
            ChaosTips.Attach(RippleStripBlock, "the ripple", TIP_RIPPLE);

            ChaosTips.Attach(TxtPanelScore, "score", TIP_SCORE);
            ChaosTips.Attach(TxtPanelMult, "the multiplier", TIP_MULT);
            ChaosTips.Attach(TxtActWave, "where you are", "the current act and loop of this descent. loops end with a draft; the last one ends the fall.");
            ChaosTips.Attach(HdrStack, "the multiplier stack", TIP_MULT);
            ChaosTips.Attach(RowStreak, "streak", TIP_STREAK);
            ChaosTips.Attach(RowDifficulty, "difficulty", "set by the pill you picked: Gentle x1.0, Teasing x1.3, Relentless x1.7, Inescapable x2.2.");
            ChaosTips.Attach(RowLust, "lust", TIP_LUST);
            ChaosTips.Attach(BarLust, "lust", TIP_LUST);
            ChaosTips.Attach(RowMantras, "mantras", "every x-multiplier mantra you took this run, multiplied together. the picks themselves are listed under CONDITIONING.");
            ChaosTips.Attach(HdrResistance, "resistance", TIP_RESIST);
            ChaosTips.Attach(TxtShields, "resistance", TIP_RESIST);
            ChaosTips.Attach(HdrFocus, "focus", TIP_FOCUS);
            ChaosTips.Attach(FocusPanelBlock, "focus", TIP_FOCUS);
            ChaosTips.Attach(HdrPockets, "toys", "the active toys you took down — two pockets at most. hover a tile for its card; before the fall starts, clicking a tile takes it off.");
            ChaosTips.Attach(HdrAccessories, "accessories", "the accessories you wore down — two at most. hover a tile for its card; before the fall starts, clicking a tile takes it off.");
            ChaosTips.Attach(HdrConditioning, "conditioning", "the mantras and sins you accepted this run, in draft order. hover each for what it does.");
            ChaosTips.Attach(HdrModifiers, "modifiers", "your trained habits — always on, every descent. switch them at the Dollhouse, not here.");
            ChaosTips.Attach(HdrFeed, "the feed", "the last few things that happened down here, newest first.");
        }
        catch (Exception ex) { App.Logger?.Debug("AttachHudTips: {E}", ex.Message); }
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

    // Hover-expand swaps which surface is under a stationary cursor (strip hides, the
    // panel slides in over 180ms, tooltips pop their own hwnd), so WPF can fire a
    // spurious MouseLeave mid-transition — collapsing instantly flaps the sidebar
    // open/shut until the timing settles. So: never collapse within a grace window of
    // opening, and treat every leave as a debounced "re-check, then fold if truly gone".
    private const double EXPAND_GRACE_MS = 1000;
    private const double LEAVE_RECHECK_MS = 220;
    private DateTime _expandedAt;
    private System.Windows.Threading.DispatcherTimer? _collapseRecheck;

    private void Hud_MouseEnter(object sender, MouseEventArgs e)
    {
        _collapseRecheck?.Stop();
        if (_expanded) return;
        _expanded = true;
        _expandedAt = DateTime.UtcNow;
        Panel.Visibility = Visibility.Visible;
        Strip.Visibility = Visibility.Hidden;   // the panel is translucent — don't let the strip bleed through
        Animate(0);
    }

    private void Hud_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_pinnedOpen || !_expanded) return;
        double sinceOpen = (DateTime.UtcNow - _expandedAt).TotalMilliseconds;
        double wait = Math.Max(LEAVE_RECHECK_MS, EXPAND_GRACE_MS - sinceOpen);
        if (_collapseRecheck == null)
        {
            _collapseRecheck = new System.Windows.Threading.DispatcherTimer();
            _collapseRecheck.Tick += (_, _) =>
            {
                _collapseRecheck!.Stop();
                if (_pinnedOpen || !_expanded) return;
                if (Panel.IsMouseOver || Strip.IsMouseOver) return;   // never left — stay open
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
    /// the charm — without it, how long you've been under stays a mystery. The final-10s red
    /// flash rides the same gate: no watch, no countdown knowledge to flash.</summary>
    public void SetClockVisible(bool on)
    {
        _clockVisible = on;
        TxtRunTime.Visibility = BarRunProgress.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool _clockVisible;
    private bool _endRushOn;

    /// <summary>Pocket Watch only: the last ten seconds of the descent blink the clocks red —
    /// the run gets a visible finale instead of stopping mid-streak. A Relapse extension that
    /// pushes the clock back out restores the calm look.</summary>
    private void UpdateClockEndRush()
    {
        try
        {
            double remaining = _state.RunDurationSec - _state.ElapsedSec;
            bool rush = _clockVisible && _state.ElapsedSec > 0 && remaining <= 10;
            if (rush == _endRushOn) return;
            _endRushOn = rush;
            var red = System.Windows.Media.Color.FromRgb(0xFF, 0x5A, 0x5A);
            if (rush)
            {
                TxtStripClock.Foreground = new System.Windows.Media.SolidColorBrush(red);
                TxtRunTime.Foreground = new System.Windows.Media.SolidColorBrush(red);
                BarRunProgress.Foreground = new System.Windows.Media.SolidColorBrush(red);
                var blink = new DoubleAnimation(1.0, 0.25, TimeSpan.FromMilliseconds(420))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                TxtStripClock.BeginAnimation(OpacityProperty, blink);
                TxtRunTime.BeginAnimation(OpacityProperty, blink);
            }
            else
            {
                TxtStripClock.BeginAnimation(OpacityProperty, null);
                TxtRunTime.BeginAnimation(OpacityProperty, null);
                TxtStripClock.Opacity = TxtRunTime.Opacity = 1.0;
                TxtStripClock.Foreground = System.Windows.Media.Brushes.White;
                TxtRunTime.Foreground = (System.Windows.Media.Brush)FindResource("TextDim");
                BarRunProgress.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x8A, 0x7D, 0xBD));
            }
        }
        catch { }
    }

    /// <summary>READY breathes a soft cyan glow on the strip's ripple readout (same pulse
    /// the toy hero button uses); charging carries no effect — the dim style does the rest.</summary>
    private void SetRippleReadyVisual(bool ready)
    {
        try
        {
            if (ready)
            {
                var glow = new System.Windows.Media.Effects.DropShadowEffect
                { Color = System.Windows.Media.Color.FromRgb(0x7A, 0xE0, 0xFF), BlurRadius = 12, ShadowDepth = 0, Opacity = 0.3 };
                RippleStripText.Effect = glow;
                glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty,
                    new DoubleAnimation(0.25, 0.95, TimeSpan.FromMilliseconds(950))
                    { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            }
            else RippleStripText.Effect = null;
        }
        catch { }
    }

    private bool _cursorOnLive;

    /// <summary>The cursor is resting on a live bubble (service-polled, 4x/s): both focus
    /// bars glow — "check your fuel first" lands exactly when the decision is being made.</summary>
    public void SetCursorOnLive(bool on)
    {
        if (on == _cursorOnLive) return;
        _cursorOnLive = on;
        try
        {
            foreach (var el in new FrameworkElement[] { FocusStripBlock, FocusPanelBlock })
                el.Effect = on
                    ? new System.Windows.Media.Effects.DropShadowEffect
                      { Color = System.Windows.Media.Color.FromRgb(0x7A, 0xE0, 0xFF), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.85 }
                    : null;
        }
        catch { }
    }

    /// <summary>Mirror the manual pause from EVERY entry point (HUD buttons or the panic key):
    /// paused pins the panel open on the continue-or-wake-up choice with the panic hint under
    /// it; resuming hands the panel back to hover. Never runs pre-run (pause needs a live field).</summary>
    public void SetPausedUi(bool paused)
    {
        try
        {
            BtnHero.Visibility = paused ? Visibility.Collapsed : Visibility.Visible;
            PauseChoiceRow.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;
            var settings = App.Settings?.Current;
            TxtPauseHint.Text = settings?.PanicKeyEnabled == true
                ? $"⏸ HELD · {settings.PanicKey} again wakes you up"
                : "⏸ HELD · the hole waits";
            TxtPauseHint.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;
            _pinnedOpen = paused;
            if (paused)
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
        catch { }
    }

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
                ApplyFocusSteadyVisual(el);
            // Danger tint: below a defuse's price the fill itself runs red, not just dim —
            // the 30-mark tick on the bar shows exactly where healthy starts again.
            var target = low ? System.Windows.Media.Color.FromRgb(0xE0, 0x45, 0x45)
                             : System.Windows.Media.Color.FromRgb(0x5A, 0xC8, 0xFA);
            foreach (var bar in new[] { FocusStripBar, FocusPanelBar })
            {
                var brush = new System.Windows.Media.SolidColorBrush(target);
                bar.Foreground = brush;
                brush.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty,
                    new ColorAnimation(target, TimeSpan.FromMilliseconds(280)));
            }
        }
        catch { }
    }

    /// <summary>The steady focus-bar look for the current state: the soft low-focus pulse,
    /// or full opacity. Also what a one-shot flash hands the bars back to when it ends.</summary>
    private void ApplyFocusSteadyVisual(FrameworkElement el)
    {
        if (_focusLowShown)
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

    /// <summary>A NO FOCUS press just detonated a live bubble: three hard blinks on both
    /// focus bars so the eye lands on WHY, then the steady visual resumes.</summary>
    public void FlashFocusBar()
    {
        try
        {
            foreach (var el in new FrameworkElement[] { FocusStripBlock, FocusPanelBlock })
            {
                var blink = new DoubleAnimation(1.0, 0.12, TimeSpan.FromMilliseconds(110))
                {
                    AutoReverse = true,
                    RepeatBehavior = new RepeatBehavior(3)
                };
                blink.Completed += (_, _) => ApplyFocusSteadyVisual(el);
                el.BeginAnimation(OpacityProperty, blink);
            }
        }
        catch { }
    }

    // ======================= streak juice (Balatro-style) =======================
    // The strip's STREAK readout heats through color tiers as the combo climbs, jitters
    // and glows when hot, punches on every gain and shakes hard on a drop. Driven from
    // the state PropertyChanged hook in the ctor — no service code involved.

    private int _lastCombo;
    private int _streakTier;
    private System.Windows.Threading.DispatcherTimer? _streakJitterTimer;
    private readonly Random _streakRng = new();

    private static readonly System.Windows.Media.Color[] StreakTierColors =
    {
        System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF),   // 0: calm white
        System.Windows.Media.Color.FromRgb(0xFF, 0xE0, 0x66),   // 1: warm gold   (5+)
        System.Windows.Media.Color.FromRgb(0xFF, 0xA9, 0x4D),   // 2: orange      (10+)
        System.Windows.Media.Color.FromRgb(0xFF, 0x5E, 0x5E),   // 3: red         (20+)
        System.Windows.Media.Color.FromRgb(0xFF, 0x2E, 0x88),   // 4: fever pink  (35+)
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

            // Settle visuals for the tier: number, color, size, glow. The brush is fresh
            // per change so the flash ColorAnimations below never fight a frozen brush.
            TxtStreakNum.Text = "x" + combo;
            TxtStreakNum.FontSize = 24 + _streakTier * 2.5;   // 24 → 34 at fever
            var brush = new System.Windows.Media.SolidColorBrush(tierColor);
            TxtStreakNum.Foreground = brush;
            TxtStreakLbl.Foreground = _streakTier >= 2
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(
                      0xCC, tierColor.R, tierColor.G, tierColor.B))
                : (System.Windows.Media.Brush)FindResource("TextDim");
            TxtStreakNum.Effect = _streakTier >= 2
                ? new System.Windows.Media.Effects.DropShadowEffect
                  { Color = tierColor, BlurRadius = 8 + _streakTier * 4, ShadowDepth = 0, Opacity = 0.9 }
                : null;

            if (gained)
            {
                // White-hot flash settling into the tier color + a spring punch.
                brush.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty,
                    new ColorAnimation(System.Windows.Media.Colors.White, tierColor, TimeSpan.FromMilliseconds(260)));
                double from = 1.30 + _streakTier * 0.06;
                var punch = new DoubleAnimation(from, 1.0, TimeSpan.FromMilliseconds(340))
                { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 } };
                StreakScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, punch);
                StreakScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, punch);
            }
            else if (dropped)
            {
                // Red flash settling into wherever the streak landed + a hard side shake.
                brush.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty,
                    new ColorAnimation(System.Windows.Media.Color.FromRgb(0xFF, 0x38, 0x38), tierColor,
                                       TimeSpan.FromMilliseconds(650)) { BeginTime = TimeSpan.FromMilliseconds(120) });
                var shake = new DoubleAnimationUsingKeyFrames
                { Duration = TimeSpan.FromMilliseconds(450), FillBehavior = FillBehavior.Stop };
                double[] xs = { 0, -9, 8, -6, 5, -3, 2, 0 };
                for (int i = 0; i < xs.Length; i++)
                    shake.KeyFrames.Add(new LinearDoubleKeyFrame(xs[i], KeyTime.FromPercent(i / (double)(xs.Length - 1))));
                StreakJitter.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, shake);
                var dip = new DoubleAnimation(0.80, 1.0, TimeSpan.FromMilliseconds(380))
                { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut } };
                StreakScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, dip);
                StreakScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, dip);
            }

            UpdateStreakJitter();
        }
        catch { }
    }

    /// <summary>Hot streaks (tier 2+) vibrate: tiny random offsets + a wobble of rotation,
    /// amplitude scaling with the tier. The timer only runs while hot.</summary>
    private void UpdateStreakJitter()
    {
        bool hot = _streakTier >= 2;
        if (hot)
        {
            if (_streakJitterTimer == null)
            {
                _streakJitterTimer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromMilliseconds(45) };
                _streakJitterTimer.Tick += (_, _) =>
                {
                    try
                    {
                        double amp = (_streakTier - 1) * 0.9;             // 0.9 / 1.8 / 2.7 px
                        StreakJitter.X = (_streakRng.NextDouble() * 2 - 1) * amp;
                        StreakJitter.Y = (_streakRng.NextDouble() * 2 - 1) * amp;
                        StreakRot.Angle = (_streakRng.NextDouble() * 2 - 1) * (_streakTier - 1) * 1.6;
                    }
                    catch { }
                };
            }
            if (!_streakJitterTimer.IsEnabled) _streakJitterTimer.Start();
        }
        else
        {
            _streakJitterTimer?.Stop();
            StreakJitter.X = 0; StreakJitter.Y = 0; StreakRot.Angle = 0;
        }
    }

    /// <summary>Color flash on the resistance hearts: bright blue when a point lands,
    /// red when a hit arrives that resistance couldn't pay (empty or not enough).
    /// Fades back to the hearts' XAML pink afterwards.</summary>
    public void FlashShields(bool gain)
    {
        try
        {
            var hot = gain ? System.Windows.Media.Color.FromRgb(0x5A, 0xC8, 0xFA)
                           : System.Windows.Media.Color.FromRgb(0xFF, 0x38, 0x38);
            var brush = new System.Windows.Media.SolidColorBrush(hot);
            TxtShields.Foreground = brush;
            brush.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty,
                new ColorAnimation(hot, System.Windows.Media.Color.FromRgb(0xFF, 0x6E, 0xC7), TimeSpan.FromMilliseconds(650))
                { BeginTime = TimeSpan.FromMilliseconds(160) });
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
        // Pause the descent and ask what they actually want — SetPausedUi (called by the
        // service) flips the rows, so the panic key and this button stay in lockstep.
        if (!_chaos.IsManuallyPaused) _chaos.ToggleManualPause();
    }

    private void BtnResume_Click(object sender, RoutedEventArgs e)
    {
        if (_chaos.IsManuallyPaused) _chaos.ToggleManualPause();
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
