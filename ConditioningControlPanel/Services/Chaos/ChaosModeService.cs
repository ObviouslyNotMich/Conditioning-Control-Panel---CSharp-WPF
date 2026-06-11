using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Owns the Chaos Mode run lifecycle over the LIVE desktop: a 3·2·1·GO countdown,
/// then waves of effect bubbles (rendered by <see cref="BubbleService"/>'s chaos
/// API) that the player pops/defuses/lets-detonate, the combo/heat/multiplier
/// stack + boon draft + shields, and the capped XP payout. The HUD is a thin
/// collapsible strip (<see cref="ChaosHudWindow"/>); the countdown/boon-draft/
/// results are a centered overlay (<see cref="ChaosOverlayWindow"/>).
///
/// Parallel to <c>BubbleService</c>'s ambient pop game — it pauses/resumes that
/// game around the run but never modifies it.
/// </summary>
public sealed class ChaosModeService
{
    private ChaosRunState? _state;
    private ChaosHudWindow? _hud;
    private ChaosOverlayWindow? _overlay;
    private ChaosFxWindow? _fx;
    private DispatcherTimer? _runTimer;
    private DispatcherTimer? _spawnTimer;
    private bool _active;        // a run session exists (countdown → results dismissed)
    private bool _spawning;      // GO fired, bubbles spawning, not yet ended
    private bool _paused;        // boon draft on screen (clock + spawns held)
    private bool _manualPaused;  // user hit pause
    private int _pendingWave;
    private int _runDetonations;  // per-run count of detonations (absorbed + unshielded)
    private int _lastComboBigFired;   // highest ComboBig threshold already fired this combo streak
    private int _lastActFired = 1;    // last ActIndex an ActChanged bark fired for

    // ---- "start small": named tunables (conservative defaults; no magic numbers in logic) ----
    /// <summary>High-combo thresholds that fire ChaosComboBig once per crossing (edge-detected).</summary>
    private static readonly int[] COMBO_BIG_THRESHOLDS = { 25, 50, 100 };
    /// <summary>Countdown length (ms) used on RunAgain (full-start countdown is unchanged at 3s).</summary>
    public const int ChaosRestartCountdownMs = 1000;
    /// <summary>Seconds an untouched boon draft waits before auto-skipping (+1 shield). 0 disables.</summary>
    public const int DraftAutoResumeSecDefault = 15;

    // ---- benign-pop juice ----
    private static readonly Color BENIGN_POP_COLOR = Color.FromRgb(255, 200, 235);  // soft pink/white
    private const double BENIGN_POP_PULSE = 0.10;                                    // low-strength micro-pulse
    // ---- combo micro-build juice ----
    private const double COMBO_MICRO_PULSE_MIN = 0.04;   // tiny pulse just after a milestone
    private const double COMBO_MICRO_PULSE_MAX = 0.12;   // grows toward the next milestone
    private const double COMBO_BIG_PULSE = 0.55;         // distinct big-combo beat
    private static readonly Color COMBO_MICRO_COLOR = Color.FromRgb(255, 170, 80);
    private static readonly Color COMBO_BIG_COLOR   = Color.FromRgb(255, 230, 120);
    // ---- shield gain/loss cue ----
    private static readonly Color SHIELD_GAIN_COLOR = Color.FromRgb(120, 220, 160);  // green +shield
    private const double SHIELD_GAIN_PULSE = 0.22;
    // ---- wave-clear cue ----
    private static readonly Color WAVE_CLEAR_COLOR = Color.FromRgb(150, 200, 255);
    private const double WAVE_CLEAR_PULSE = 0.30;
    // Near-miss danger telegraph is now visual-only and per-bubble (the about-to-blow bubble
    // flashes + breathes faster in BubbleService) — no screen flash, no audio tick.
    // ---- heat temperature tint (additive overlay only; rises with HeatMult) ----
    private static readonly Color HEAT_TINT_COLOR = Color.FromRgb(255, 90, 40);
    private const double HEAT_TINT_MIN = 0.30;       // heat must exceed this before the tint shows
    private const double HEAT_TINT_MAX_OPACITY = 0.30;  // peak held-edge opacity at full heat
    private double _lastHeatTint = -1;               // last applied heat-tint level (avoid churn)

    public ChaosModeService()
    {
        // Unlock toast: when something reveals mid-run, one quiet line in the event feed.
        RevealService.Pending += OnRevealPending;
    }

    private DateTime _lastRevealToastUtc = DateTime.MinValue;

    /// <summary>One event-feed line per reveal batch (Sync raises Pending per id; the
    /// debounce window collapses a batch to a single line). Run-active only.</summary>
    private void OnRevealPending(string id)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return;
        disp.BeginInvoke(new Action(() =>
        {
            try
            {
                if (!_spawning || _state == null) return;
                if ((DateTime.UtcNow - _lastRevealToastUtc).TotalSeconds < 8) return;
                _lastRevealToastUtc = DateTime.UtcNow;
                _state.PushEvent("something new in the dollhouse.");
            }
            catch { }
        }));
    }

    public bool IsRunning => _active;

    public bool IsManuallyPaused => _manualPaused;

    public void ToggleManualPause()
    {
        if (!_spawning || _paused) return;
        _manualPaused = !_manualPaused;
        if (_manualPaused)
        {
            // Freeze the whole field: stop spawning, hold the clock (RunTick early-returns),
            // freeze every bubble's motion + fuse, and swallow clicks — a paused field
            // can't be farmed for pops.
            _spawnTimer?.Stop();
            App.Bubbles?.SetChaosFrozen(true);
            App.Bubbles?.SetChaosInputLocked(true);
            _state?.PushEvent("⏸ held. the hole waits.");
        }
        else
        {
            App.Bubbles?.SetChaosInputLocked(false);
            App.Bubbles?.SetChaosFrozen(false);
            _spawnTimer?.Start();
            _state?.PushEvent("▶ sinking again");
        }
    }

    // ============================ start / countdown ============================

    public void StartRun(ChaosRunConfig? config = null, bool isRestart = false)
    {
        if (_active) return;

        // A chaos run takes over the screen with its own overlays/HUD; stop any running
        // conditioning engine or AI session first so the two don't fight over the display.
        // (Self-guards — a no-op when nothing is running.)
        if (App.IsEngineRunning || App.IsSessionRunning)
            App.MainWindowRef?.StopEngineAndSession("Chaos");

        CloseLoadoutSidebar();   // the Warren-phase sidebar hands off to the real run HUD
        var cfg = config ?? ChaosRunConfig.FromSettings();

        try
        {
            App.Bubbles?.PauseAndClear();
            _state = new ChaosRunState(cfg);
            _active = true;

            _hud = new ChaosHudWindow(_state, this);
            _hud.Show();

            // In-run sidebar stays collapsed unless hovered — the pre-run glance already happened
            // beside the Warren, so the descent starts with a clean screen.
            RefreshSidebarLoadout();

            _overlay = new ChaosOverlayWindow();
            _overlay.OnRunAgain = RunAgain;
            _overlay.OnDismissed = OnOverlayClosed;
            _overlay.Show();
            if (!isRestart) ChaosSfx.Play("fall_in", 0.55f);   // the falling whoosh under the countdown
            _overlay.ShowCountdown(BeginRun, shortFlash: isRestart);   // 1s flash on RunAgain

            // The run's topmost windows (overlay/FX/payload washes/bubbles) would otherwise bury an
            // ATTACHED avatar's speech bubble in the non-topmost band — keep it topmost for the run.
            App.AvatarWindow?.SetChaosRunActive(true);
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "ChaosModeService.StartRun failed");
            CleanupAfterRun();
            App.Bubbles?.Resume();
            MessageBox.Show("Couldn't open the Rabbit Hole:\n\n" + ex, "The Rabbit Hole",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BeginRun()
    {
        if (!_active || _state == null) return;

        App.Bubbles?.BeginChaosMode(OnBenignPopped, OnDefused, OnDetonated, OnDarterCaught, OnFreezeCaught,
            chainReach: () => _state?.ChainReactionReach ?? 0,
            hitboxScale: () => _state?.Config?.HitboxScale ?? 1.0,
            bubbleOpacity: () => _state?.BlindfoldActive == true ? _state.BlindfoldOpacity : 1.0,
            wandShimmer: () => false,   // magic_wand retired 2026-06-10 (BubbleService hook kept for reuse)
            cursorPull: () => (_state?.CursorPullStrength ?? 0) - (_state?.CamGirlFlee ?? 0),   // Cam Girl flees; The Pull fights back
            rabbitHoming: () => (_state?.CursorPullStrength ?? 0) > 0,
            spankerOn: () => _state?.SpankerActive == true,
            spankGrow: () => _state?.SpankGrowFactor ?? 1.0,
            onTreatExpired: OnTreatExpired,
            onEStimArc: OnEStimArc,
            rabbitTrailSec: () => _state?.RabbitTrailSec ?? 0,
            electrifiedRabbits: () => _state?.ElectrifiedRabbits == true,
            canChannelDefuse: CanChannelDefuse,
            onChannelBroken: OnChannelBroken,
            onTeaseTouched: OnTeaseTouched,
            onTeaseDenied: OnTeaseDenied,
            onBoundEnraged: OnBoundEnraged,
            onBrittleShattered: OnBrittleShattered);
        App.Bark?.NotifyChaosRunStarted(_state.Config.Difficulty.ToString());
        _state.PushEvent("🐇 the descent begins");
        _runDetonations = 0;
        _lastComboBigFired = 0;
        _lastActFired = 1;
        _lastHeatTint = -1;
        ChaosLessonHooks.OnRunStarted();   // lessons: fresh per-run trackers
        EndSlowMo(); EndFreeze();   // clean power-up state for the new run (no leak across runs)
        _snapFlashRemainingSec = 0; _rabbitStormRemainingSec = 0; _rabbitStormAccumSec = 0; _thoughtAccumSec = 0;
        _pendulumRolledWave = 0;   // pendulum event re-rolls its beat for wave 1
        _heartRolledWave = 0; _heartArmedThisWave = false;   // pop-up heart re-rolls too
        _spawnSerial = 0; _pendulumSlowActive = false; _afterglowApplied = false;   // run-boon transient state
        _heavyUntilUtc = DateTime.MinValue; _chaosVideoCapUtc = DateTime.MinValue;   // heavy gate never leaks across runs
        // The Spanker capstone gate — each DVD/thought logo samples this when it spawns.
        ChaosDvdOverlay.SpankerRedirect = () => _state?.SpankerActive == true
                                             && _state?.MaxedBoons.Contains("the_spanker") == true;
        App.Overlay?.WarmSpiralCache();   // pre-decode the spiral off-thread so its first show doesn't hitch
        ChaosEffectBannerOverlay.EnsureCreated();   // birth the banner window NOW, not mid-chaos
        ChaosFieldFxOverlay.EnsureCreated();        // ripples/residue/trails are drafted mid-run — pre-create always

        // Loadout: a pre-equipped start boon enters the run already active (before wave 1).
        var equipped = ChaosMeta.State.EquippedStartBoon;
        if (!string.IsNullOrEmpty(equipped))
        {
            var boon = ChaosBoonPool.All.FirstOrDefault(b => b.Id == equipped);
            if (boon != null) { _state.ApplyBoon(boon); ChaosMeta.MarkDiscovered("boon:" + boon.Id); }
        }
        // Welcome Shower equipped as the start boon: the very first GO! gets its treat dump too.
        if (_state.WelcomeShowerEnabled) SpawnWelcomeShower();

        // Lifetime boons (Skills/Accessories/Utility): apply active ones at their level, then
        // mirror them into the HUD strip as icons. The pre-run glance is over: the loadout
        // locks in and the pinned-open panel folds away.
        ChaosMeta.ApplyLifetimeBoons(_state);
        RefreshSidebarLoadout();
        _hud?.SetPreRunExpanded(false);
        // Pocket Watch gates ALL timekeeping: the sidebar run clock + fill bar only show with the charm.
        _hud?.SetClockVisible(_state.ShowWaveTimer);

        // Pocket Watch: birth the wave-countdown window NOW (keep-alive contract — never mid-run).
        if (_state.ShowWaveTimer) ChaosWaveTimerOverlay.EnsureCreated();
        // Rabbit Caller equipped: pre-create the cursor-glow halo for the summon-at-click.
        if (ChaosMeta.IsBoonActive("rabbit_caller")) ChaosCursorGlowOverlay.EnsureCreated();
        if (ChaosMeta.IsBoonActive("e_stim")) ChaosEStimOverlay.EnsureCreated();
        // VibePopping equipped: pre-create the pointer glow + trail for the buzz window.
        if (ChaosMeta.IsBoonActive("vibe_popping")) ChaosVibeTrailOverlay.EnsureCreated();

        // Muscle Memory regen caps at the resistance you descended with (capstone: +1 above it).
        _state.StartShields = _state.Shields;
        _regenPopCount = 0;
        _heartbeatCooldownSec = 0;

        // Hold-to-defuse: fresh focus warning + Snap Chain invuln state for the new descent.
        _focusLowAccumSec = 0;
        _focusLowBarkFired = false;
        _invulnUntilUtc = DateTime.MinValue;
        _teaseDeniedThisRun = 0;
        _teaseDeniedStreakBarked = false;
        // First descent since the verb changed: one quiet line so the hold isn't a mystery.
        if (!ChaosMeta.State.SeenDefuseTutorial)
        {
            ChaosMeta.State.SeenDefuseTutorial = true;
            ChaosMeta.Save();
            ChaosAnnouncerOverlay.Announce("press and HOLD a live one to snap it", ChaosAnnounceKind.Willpower);
            _state.PushEvent("✋ hold to snap. let go and it triggers.");
        }

        // Active skills (toys): build their state, listen for keybinds, and park one big
        // hero button per toy at the bottom-left of the screen (clickable at a glance).
        _vibeRemainingSec = 0;
        BuildActiveToys();
        StartKeyHook();
        for (int i = 0; i < _state.ActiveToys.Count; i++)
        {
            try
            {
                var btn = new ChaosToyButtonWindow(_state.ActiveToys[i], this, i);
                btn.Show();
                _toyButtons.Add(btn);
            }
            catch (Exception ex) { App.Logger?.Debug("Toy button: {E}", ex.Message); }
        }

        _spawning = true;

        try { _fx = new ChaosFxWindow(); _fx.Show(); } catch (Exception ex) { App.Logger?.Debug("Chaos FX init: {E}", ex.Message); }

        // A mandatory-video payload can fire mid-run; keep the gameplay layer above it (see handler).
        if (App.Video != null) App.Video.VideoStarted += OnVideoStartedDuringRun;
        if (App.Video != null) App.Video.VideoEnded += OnVideoEndedDuringRun;

        _runTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _runTimer.Tick += RunTick;
        _runTimer.Start();

        _spawnTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _spawnTimer.Tick += SpawnTick;
        _spawnTimer.Start();
    }

    // ---- pre-run loadout sidebar (shown beside the Warren hub, before any run exists) ----
    private ChaosHudWindow? _preHud;
    private ChaosRunState? _preState;

    /// <summary>The loadout is editable while the Warren sidebar is up or from FALL IN until
    /// SINK fires (boons apply at BeginRun).</summary>
    public bool CanEditLoadout => !_spawning && (_active || _preHud != null);

    /// <summary>Open the loadout sidebar next to the Warren: the run HUD, pinned open, pockets
    /// editable. Lives until the hub closes (a started run swaps in the real HUD).</summary>
    public void ShowLoadoutSidebar()
    {
        if (_active || _preHud != null) return;
        try
        {
            _preState = new ChaosRunState(ChaosRunConfig.FromSettings());
            _preHud = new ChaosHudWindow(_preState, this);
            _preHud.Show();
            RefreshSidebarLoadout();
            _preHud.SetHeroMode(preRun: true);   // hero button reads FALL IN until the run takes over
            _preHud.SetPreRunExpanded(true);
        }
        catch (Exception ex) { App.Logger?.Debug("ShowLoadoutSidebar: {E}", ex.Message); }
    }

    /// <summary>Close the Warren-phase sidebar (hub closed or a run is taking over).</summary>
    public void CloseLoadoutSidebar()
    {
        try { _preHud?.Close(); } catch { }
        _preHud = null;
        _preState = null;
    }

    /// <summary>A pocket tile was clicked in the HUD during the pre-run glance: take the boon off.</summary>
    public void UnequipFromSidebar(string id)
    {
        if (!CanEditLoadout || string.IsNullOrEmpty(id)) return;
        if (!ChaosMeta.IsBoonActive(id)) return;
        ChaosMeta.SetBoonActive(id, false);
        RefreshSidebarLoadout();
        (_state ?? _preState)?.PushEvent("👝 left behind: " + (ChaosLifetimeBoons.ById(id)?.Name ?? id));
        ChaosHubWindow.Current?.RefreshAfterExternalLoadoutChange();   // keep the open Warren in sync
    }

    /// <summary>The Warren changed the loadout while the pre-run glance is up — mirror it.</summary>
    public void NotifyLoadoutChanged()
    {
        if (CanEditLoadout) RefreshSidebarLoadout();
    }

    /// <summary>An empty "+" pocket tile was clicked in the sidebar: take the player shopping —
    /// bring the open Warren forward on the right tab (no-op mid-run, when no Warren exists).</summary>
    public void OpenWarrenAt(string tab) => ChaosHubWindow.Current?.NavigateTo(tab);

    /// <summary>The sidebar's pre-run ✖: leave the rabbit hole — close the Warren, which folds
    /// the sidebar away with it (no-op once a run is live; the pause flow owns exits then).</summary>
    public void CloseWarrenPhase()
    {
        if (_active) return;
        if (ChaosHubWindow.Current != null) ChaosHubWindow.Current.Close();
        else CloseLoadoutSidebar();
    }

    /// <summary>The sidebar's FALL IN hero button: start the descent. Goes through the open
    /// Warren when there is one (so the run setup saves), else starts directly.</summary>
    public void StartRunFromSidebar()
    {
        if (_active) return;
        if (ChaosHubWindow.Current != null) ChaosHubWindow.Current.FallIn();
        else StartRun();
    }

    /// <summary>Populate the HUD's pocket loadout — equipped lifetime boons in slot order, with dim
    /// empty-slot tiles for unfilled pockets (the pre-run glance) — plus the passive modifier list
    /// (owned always-on upgrades shaping this run).</summary>
    private void RefreshSidebarLoadout()
    {
        var st = _state ?? _preState;   // live run, or the Warren-phase preview state
        if (st == null) return;
        st.ActiveSidebarBoons.Clear();
        foreach (var cat in new[] { ChaosBoonCategory.Skill, ChaosBoonCategory.Accessory })
        {
            int filled = 0;
            foreach (var b in ChaosLifetimeBoons.InCategory(cat))
            {
                if (!ChaosMeta.IsBoonActive(b.Id)) continue;
                int lvl = ChaosMeta.BoonLevel(b.Id);
                st.ActiveSidebarBoons.Add(new ChaosSidebarBoon
                {
                    Id = b.Id,
                    Icon = ChaosArt.Resolve("boons", b.Id),
                    Glyph = b.Glyph,
                    Name = b.Name,
                    Level = lvl,
                    Desc = b.Desc,
                    Extra = lvl >= b.MaxLevel && !string.IsNullOrEmpty(b.CapstoneDesc) ? "max: " + b.CapstoneDesc : "",
                });
                filled++;
            }
            // Empty "+" slots only exist during the Warren-phase glance (clicking one jumps the
            // Warren to Enhancements). Mid-run the loadout is locked — don't render dead tiles.
            if (_preHud != null)
                for (int i = filled; i < ChaosMeta.SlotsFor(cat); i++)
                    st.ActiveSidebarBoons.Add(new ChaosSidebarBoon
                    {
                        IsEmptySlot = true,
                        Glyph = "+",
                        Name = cat == ChaosBoonCategory.Skill ? "empty toy pocket" : "empty accessory pocket",
                        Desc = "click to go shopping in the dollhouse.",
                    });
        }
        st.RunModifiers.Clear();
        foreach (var id in ChaosMeta.State.PurchasedUpgrades)
        {
            if (!ChaosMeta.IsUpgradeActive(id)) continue;   // switched-off habits sit out the run
            var u = ChaosUpgrades.ById(id);
            if (u == null) continue;
            st.RunModifiers.Add(new ChaosSidebarBoon
            {
                Icon = u.IconPath != null ? ChaosArt.TryLoad(u.IconPath) : ChaosArt.Resolve("upgrades", u.Id),
                Glyph = u.Glyph,
                Name = u.Name,
                Desc = u.Desc,
                IsModifier = true,
            });
        }
        // Worn leveled habits (Utility lifetime boons — Rabbit's Foot etc.) are modifiers
        // too, not pocket gear: they list here beside the other always-on passives.
        foreach (var b in ChaosLifetimeBoons.InCategory(ChaosBoonCategory.Utility))
        {
            if (!ChaosMeta.IsBoonActive(b.Id)) continue;
            int lvl = ChaosMeta.BoonLevel(b.Id);
            st.RunModifiers.Add(new ChaosSidebarBoon
            {
                Id = b.Id,
                Icon = ChaosArt.Resolve("boons", b.Id),
                Glyph = b.Glyph,
                Name = $"{b.Name} · L{lvl}",
                Level = lvl,
                Desc = b.Desc,
                Extra = lvl >= b.MaxLevel && !string.IsNullOrEmpty(b.CapstoneDesc) ? "max: " + b.CapstoneDesc : "",
                IsModifier = true,
            });
        }
    }

    /// <summary>
    /// A chaos payload can fire a mandatory video mid-run. The video is a freshly-raised
    /// fullscreen topmost window that lands on top of the run's bubbles + HUD + FX. Lift the
    /// gameplay layer back ABOVE it (focus-free, so the video keeps playing) — the video ends up
    /// UNDER the assets but over everything else, and the player keeps popping. Re-kick once after
    /// the video's maximize/activate settles (mirrors VideoService's 50ms attention-target kick).
    /// New bubbles spawned during the video already land on top naturally (fresh topmost windows).
    /// </summary>
    private void OnVideoStartedDuringRun(object? sender, EventArgs e)
    {
        void Raise()
        {
            if (!_spawning) return;
            App.Bubbles?.BringAllToFront();
            try { _fx?.RaiseToTopmost(); } catch { }
            try { _hud?.RaiseToTopmost(); } catch { }
            foreach (var b in _toyButtons) { try { b.RaiseToTopmost(); } catch { } }
        }
        var disp = Application.Current?.Dispatcher;
        if (disp == null) return;
        try { disp.BeginInvoke((Action)Raise); } catch { }
        System.Threading.Tasks.Task.Delay(60).ContinueWith(_ =>
        {
            if (Application.Current?.Dispatcher == null) return;
            try { Application.Current.Dispatcher.BeginInvoke((Action)Raise); }
            catch (Exception ex) { App.Logger?.Debug("Chaos raise-above-video kick: {E}", ex.Message); }
        });
    }

    // ============================ run loop ============================

    private void RunTick(object? sender, EventArgs e)
    {
        if (!_spawning || _state == null || _paused || _manualPaused) return;

        double dt = 0.25;
        double elapsed = _state.ElapsedSec + dt;
        _state.ElapsedSec = elapsed;
        _state.Heat = Math.Max(0, _state.Heat - 0.0015);
        UpdateHeatTint();

        // Power-ups run on the real clock (so they don't extend the run length).
        if (_slowMoRemainingSec > 0)
        {
            _slowMoRemainingSec -= dt;
            if (_slowMoRemainingSec <= 0) EndSlowMo();
        }
        if (_freezeRemainingSec > 0)
        {
            _freezeRemainingSec -= dt;
            if (_freezeRemainingSec <= 0) EndFreeze();
        }

        TickBlindfoldHeartbeat(dt);   // Blindfold capstone: the closest fuse gets a pulse
        TickActiveToys(dt);           // toy cooldowns + the VibePopping buzz window
        ChaosLessonHooks.SampleCursor();   // the_pull lesson: cheap, self-disabling once learned

        // rh_focus_low: focus has sat below a defuse's price while live threats hang on screen.
        // Once per run — a nudge toward farming treats, not a nag.
        if (!_focusLowBarkFired)
        {
            if (_state.FocusLow && App.Bubbles?.MinChaosFuseSec() != null)
            {
                _focusLowAccumSec += dt;
                if (_focusLowAccumSec >= ChaosTuning.FOCUS_LOW_BARK_SEC)
                {
                    _focusLowBarkFired = true;
                    App.Bark?.NotifyChaosFocusLow();
                }
            }
            else _focusLowAccumSec = 0;
        }

        // Mandatory-video hard caps: a chaos-fired video never runs past its 15s slice, and
        // never into the run's final 3s (the recap should never land on top of a playing tape).
        if (_chaosVideoCapUtc != DateTime.MinValue)
        {
            bool capHit = DateTime.UtcNow >= _chaosVideoCapUtc;
            bool runClosing = _state.RunDurationSec - elapsed <= 3;
            if (App.Video?.IsPlaying == true && (capHit || runClosing))
            {
                _chaosVideoCapUtc = DateTime.MinValue;
                try { App.Video?.ForceCleanup(); } catch (Exception ex) { App.Logger?.Debug("Chaos video cap: {E}", ex.Message); }
                ExtendHeavyQuarantine(VIDEO_TEARDOWN_QUARANTINE_SEC);   // ForceCleanup may not raise VideoEnded
                _state.PushEvent("▶ the tape snaps off");
                // porn_dvd lesson: the full slice ran (the 15s cap IS the slice length);
                // a run-closing cut before the cap is an abort and doesn't count.
                if (capHit) ChaosLessonHooks.OnVideoEndured();
            }
            else if (App.Video?.IsPlaying != true && capHit)
            {
                _chaosVideoCapUtc = DateTime.MinValue;   // ended on its own — clear the cap
                ExtendHeavyQuarantine(VIDEO_TEARDOWN_QUARANTINE_SEC);
                ChaosLessonHooks.OnVideoEndured();   // porn_dvd lesson: endured to its natural end
            }
        }

        if (elapsed >= _state.RunDurationSec)
        {
            // Relapse sin: the hole isn't done with you — one more loop, paying double drops + gold.
            if (_state.RelapseLoopArmed && !_state.RelapseLoopActive)
            {
                _state.ExtendOneLoop();
                ChaosAnnouncerOverlay.Announce("☠ RELAPSE — one more loop", ChaosAnnounceKind.Temptation);
                ChaosSfx.Play("sin_accept", 0.6f);
                _state.PushEvent("☠ relapse. one more loop — everything drips double");
                App.Bark?.NotifyChaosWaveEscalated(_state.WaveIndex + 1);
            }
            else { EndRun(); return; }
        }

        double waveLen = (double)_state.RunDurationSec / _state.WaveCount;
        int newWave = Math.Min(_state.WaveCount, 1 + (int)(elapsed / waveLen));
        _state.WaveProgress = (elapsed % waveLen) / waveLen;

        // Pocket Watch: the wave countdown at the top of the screen.
        if (_state.ShowWaveTimer)
            ChaosWaveTimerOverlay.Update(newWave, _state.WaveCount, waveLen - (elapsed % waveLen));

        if (newWave > _state.WaveIndex) BeginWaveTransition(newWave);
    }

    private void SpawnTick(object? sender, EventArgs e)
    {
        if (!_spawning || _state == null || _paused || _manualPaused) return;
        if (_freezeRemainingSec > 0) return;   // time is frozen: hold the field, spawn nothing new

        var cfg = _state.Config;
        double intensity = _state.RunIntensity;
        double effIntensity = Math.Clamp(intensity + (cfg.DifficultyMult - 1.0) * 0.15, 0, 1);
        double diffFactor = cfg.DifficultyMult;

        int maxConcurrent = (int)Math.Round((4 + intensity * 7) * Math.Sqrt(diffFactor));
        // Behavioral bubbles (Echo/Chaperone/Tease/Bound): each rolls to REPLACE this ordinary
        // spawn slot, so the field density stays the same. Darters still roll below either way.
        bool behavioralSpawned = (App.Bubbles?.ActiveBubbles ?? 0) < maxConcurrent
                                 && TrySpawnBehavioralBubble(cfg, effIntensity);
        if (!behavioralSpawned && (App.Bubbles?.ActiveBubbles ?? 0) < maxConcurrent)
        {
            // Be gentle with the tape: no video bubble while a heavy effect (video/cascade) is
            // running, and none when the loop or run is too close to its end for the bubble's
            // fuse plus the 15s video slice to fit.
            IReadOnlyCollection<string>? enabled = cfg.EnabledVariants;
            double waveLen = (double)_state.RunDurationSec / _state.WaveCount;
            double waveLeft = waveLen - (_state.ElapsedSec % waveLen);
            double runLeft = _state.RunDurationSec - _state.ElapsedSec;
            if (enabled != null && enabled.Contains("video")
                && (HeavyEffectActive || waveLeft < 20 || runLeft < 25))
            {
                enabled = enabled.Where(id => id != "video").ToList();
            }

            // Heavy Drop: every Nth ordinary spawn swaps for a giant, slow, triple-pay treat.
            EffectBubbleSpec spec;
            if (_state.HeavyDropEvery > 0 && ++_spawnSerial % _state.HeavyDropEvery == 0)
            {
                spec = ChaosBubbleVariants.BuildHeavy(effIntensity, cfg.EffectIntensity, _state.BubbleScale);
            }
            else
            {
                spec = ChaosBubbleVariants.Pick(effIntensity, _state.FuseTimeMult,
                    cfg.MotionOverride, enabled, cfg.EffectIntensity, _state.BubbleScale);
            }
            ChaosMeta.MarkDiscovered("bubble:" + spec.VariantId);
            App.Bubbles?.SpawnChaosBubble(spec);

            // Lucky golden bubble: a rare bonus roll riding every ordinary spawn (base 0.5%;
            // Rabbit's Foot raises it). Pays real gold — Sparks — the moment it's popped.
            if (Random.Shared.NextDouble() < _state.GoldenChance)
            {
                ChaosMeta.MarkDiscovered("bubble:golden");
                App.Bubbles?.SpawnChaosBubble(ChaosBubbleVariants.BuildGolden());
                App.Bubbles?.PlayChime(0.30f);   // a soft chime so a sharp ear catches the chance
            }

            // "Look at the bright colors..." sin: sometimes a mimic prism drifts in.
            if (_state.PrismChance > 0 && Random.Shared.NextDouble() < _state.PrismChance)
            {
                ChaosMeta.MarkDiscovered("bubble:prism");
                App.Bubbles?.SpawnChaosBubble(ChaosBubbleVariants.BuildPrism(effIntensity, cfg.EffectIntensity));
            }

            // The Brittle (not on Gentle): a glass mine rides in alongside the field — the
            // cursor merely brushing it shatters it and a random live effect fires.
            if (cfg.Difficulty != ChaosDifficulty.Easy
                && Random.Shared.NextDouble() < ChaosTuning.BRITTLE_SPAWN_CHANCE)
            {
                if (!ChaosMeta.State.SeenBrittle)
                {
                    ChaosMeta.State.SeenBrittle = true; ChaosMeta.Save();
                    ChaosAnnouncerOverlay.Announce("◇ THE BRITTLE — don't even hover", ChaosAnnounceKind.Temptation);
                    _state.PushEvent("◇ thin glass drifts in. steer around it.");
                }
                ChaosMeta.MarkDiscovered("bubble:brittle");
                App.Bubbles?.SpawnChaosBubble(ChaosBubbleVariants.BuildBrittle(effIntensity,
                    cfg.EffectIntensity, _state.BubbleScale));
            }
        }

        // Darters spawn on their own intensity-scaled roll, independent of the bubble cap.
        // (Tunnel Vision habit cut 2026-06-10 — the spotlight viz lives on in the variant builder.)
        if (cfg.DartersEnabled)
        {
            var darter = ChaosBubbleVariants.RollDarter(effIntensity, _state.RabbitRateMult, spotlight: false);
            if (darter != null)
            {
                ChaosMeta.MarkDiscovered("bubble:darter");
                App.Bubbles?.SpawnChaosBubble(darter);
            }
        }

        double interval = (1300 - intensity * 850) / diffFactor;
        if (_slowMoRemainingSec > 0) interval /= SLOWMO_FACTOR;   // slow-mo stretches the spawn cadence
        _spawnTimer!.Interval = TimeSpan.FromMilliseconds(Math.Max(280, interval));
    }

    // ============================ behavioral bubbles (Echo / Chaperone / Tease / Bound) ============================

    /// <summary>
    /// Roll the behavioral bubbles for this spawn slot. A hit REPLACES the ordinary spawn
    /// (density stays sane; a debut also consumes the tick → it spawns alone). Gating:
    /// none on Gentle; Echo + Chaperone from Teasing; Tease from the Slipping rank;
    /// Bound from Relentless. Debuts get a gentler trance and announce themselves.
    /// </summary>
    private bool TrySpawnBehavioralBubble(ChaosRunConfig cfg, double effIntensity)
    {
        if (_state == null) return false;
        if (cfg.Difficulty == ChaosDifficulty.Easy) return false;   // Gentle: none of them spawn

        // The Echo (Teasing+): trigger it and it multiplies; only the held defuse is clean.
        if (Random.Shared.NextDouble() < ChaosTuning.ECHO_SPAWN_CHANCE)
        {
            bool debut = !ChaosMeta.State.SeenEcho;
            if (debut)
            {
                ChaosMeta.State.SeenEcho = true; ChaosMeta.Save();
                ChaosAnnouncerOverlay.Announce("◌ THE ECHO — hold it down, or it multiplies", ChaosAnnounceKind.Item);
                _state.PushEvent("◌ something doubled stirs below");
            }
            ChaosMeta.MarkDiscovered("bubble:echo");
            App.Bubbles?.SpawnChaosBubble(ChaosBubbleVariants.BuildEcho(effIntensity, _state.FuseTimeMult,
                _state.BubbleScale, debut ? ChaosTuning.DEBUT_FUSE_MULT : 1.0));
            return true;
        }

        // The Chaperone (Teasing+): shielded while its escort circles — pop the escort first.
        if (Random.Shared.NextDouble() < ChaosTuning.CHAPERONE_SPAWN_CHANCE)
        {
            bool debut = !ChaosMeta.State.SeenChaperone;
            if (debut)
            {
                ChaosMeta.State.SeenChaperone = true; ChaosMeta.Save();
                ChaosAnnouncerOverlay.Announce("💞 THE CHAPERONE — its little escort first", ChaosAnnounceKind.Item);
                _state.PushEvent("💞 it brought company");
            }
            var (live, escort) = ChaosBubbleVariants.BuildChaperonePair(effIntensity, _state.FuseTimeMult,
                cfg.EffectIntensity, _state.BubbleScale, debut ? ChaosTuning.DEBUT_FUSE_MULT : 1.0);
            ChaosMeta.MarkDiscovered("bubble:chaperone");
            App.Bubbles?.SpawnChaosChaperone(live, escort);
            return true;
        }

        // The Bound (Relentless+): two lives, one thread — both must come down quickly.
        if (cfg.Difficulty >= ChaosDifficulty.Hard
            && Random.Shared.NextDouble() < ChaosTuning.BOUND_SPAWN_CHANCE)
        {
            bool debut = !ChaosMeta.State.SeenBound;
            if (debut)
            {
                ChaosMeta.State.SeenBound = true; ChaosMeta.Save();
                ChaosAnnouncerOverlay.Announce("⛓ THE BOUND — both, and quickly", ChaosAnnounceKind.Item);
                _state.PushEvent("⛓ two of them, one thread");
            }
            var (a, b) = ChaosBubbleVariants.BuildBoundPair(effIntensity, _state.FuseTimeMult,
                cfg.EffectIntensity, _state.BubbleScale, debut ? ChaosTuning.DEBUT_FUSE_MULT : 1.0);
            ChaosMeta.MarkDiscovered("bubble:bound");
            App.Bubbles?.SpawnChaosBoundPair(a, b);
            return true;
        }

        // The Tease (Slipping rank): the one you beat by NOT touching it.
        if (ChaosMeta.AtLeast(ChaosRank.Slipping)
            && Random.Shared.NextDouble() < ChaosTuning.TEASE_SPAWN_CHANCE)
        {
            bool debut = !ChaosMeta.State.SeenTease;
            if (debut)
            {
                ChaosMeta.State.SeenTease = true; ChaosMeta.Save();
                ChaosAnnouncerOverlay.Announce("✖ THE TEASE — whatever you do, don't", ChaosAnnounceKind.Temptation);
                _state.PushEvent("✖ it wants your hand. don't.");
                App.Bark?.NotifyChaosTeaseDebut();
            }
            ChaosMeta.MarkDiscovered("bubble:tease");
            App.Bubbles?.SpawnChaosBubble(ChaosBubbleVariants.BuildTease(effIntensity,
                cfg.EffectIntensity, _state.BubbleScale));
            return true;
        }

        return false;
    }

    // ---- The Tease: touch + denial outcomes ----
    private int _teaseDeniedThisRun;
    private bool _teaseDeniedStreakBarked;

    /// <summary>A mouse-down landed on a Tease: its payload fires (resistance can absorb THAT,
    /// nothing else) and the streak HALVES no matter what — that's the price of touching.</summary>
    private void OnTeaseTouched(EffectBubbleSpec spec)
    {
        if (_state == null || _paused || _manualPaused) return;
        _state.Detonated++;
        _runDetonations++;
        ChaosLessonHooks.OnDetonation();   // silk_touch: a touched Tease dirties the loop too
        double s = spec.Strength / 100.0;

        int shieldCost = _state.DoubleOrNothingActive ? 2 : 1;
        if (_state.Shields >= shieldCost)
        {
            // Resistance prevents only the payload — the streak still pays below.
            _state.Shields -= shieldCost;
            _state.Heat = Math.Max(0, _state.Heat - 0.2);
            ChaosSfx.Play(_state.Shields == 0 ? "resist_crumble" : "resist_absorb", 0.6f);
            _state.PushEvent($"♥ resistance takes the sting ({spec.Payload.DisplayName})");
        }
        else
        {
            FirePayloadForDetonation(spec);
            _state.EffectsFired++;
            ChaosSfx.Play("trigger", 0.55f);
            Shake(0.3 + s * 0.4, 320);
        }

        _state.Combo = _state.Combo > 1 ? _state.Combo / 2 : 0;
        _lastComboBigFired = 0;
        _state.PushEvent($"✖ you touched it. it laughs — streak halves to x{_state.Combo}");
        Pulse(Color.FromRgb(0xFF, 0x3D, 0x5A), 0.38);
        App.Bark?.NotifyChaosTeaseClicked();
    }

    /// <summary>The Brittle shattered — the cursor brushed (or pressed) the glass. The mimic's
    /// live effect fires; resistance can absorb the payload (a strayed hand is an accident,
    /// like touching the Tease) but unlike the Tease the streak is spared — the effect itself
    /// is the whole price. Never counts as a missed trance.</summary>
    private void OnBrittleShattered(EffectBubbleSpec spec)
    {
        if (_state == null || _paused || _manualPaused) return;
        ChaosSfx.Play(ChaosSfx.ResolvePath("glass_shatter").Length > 0 ? "glass_shatter" : "trigger", 0.55f);

        int shieldCost = _state.DoubleOrNothingActive ? 2 : 1;
        if (_state.Shields >= shieldCost)
        {
            _state.Shields -= shieldCost;
            _state.Heat = Math.Max(0, _state.Heat - 0.2);
            ChaosSfx.Play(_state.Shields == 0 ? "resist_crumble" : "resist_absorb", 0.6f);
            _state.PushEvent($"♥ resistance takes the shards ({spec.Payload.DisplayName})");
        }
        else
        {
            FirePayloadForDetonation(spec);
            _state.EffectsFired++;
            double s = spec.Strength / 100.0;
            Shake(0.25 + s * 0.35, 300);
            _state.PushEvent($"◇ it shatters — {spec.Payload.DisplayName} was inside");
        }
        Pulse(Color.FromRgb(0xBF, 0xE6, 0xFF), 0.32);
    }

    /// <summary>A Bound survivor's tether snapped (window lapsed or its partner triggered) —
    /// it enraged: half the trance left, half again the speed. The juice lives here.</summary>
    private void OnBoundEnraged(EffectBubbleSpec spec)
    {
        if (_state == null) return;
        ChaosSfx.Play("toy_denied", 0.5f);   // a sharp denial sting until a dedicated cue ships
        Pulse(Color.FromRgb(0xFF, 0x4A, 0x4A), 0.30);
        _state.PushEvent("⛓ the tether snaps — it enrages");
    }

    /// <summary>The Tease expired untouched: restraint pays — gold, score AND focus.</summary>
    private void OnTeaseDenied(EffectBubbleSpec spec)
    {
        if (_state == null || _paused || _manualPaused) return;
        int gold = GoldScaled(Random.Shared.Next(ChaosTuning.TEASE_GOLD_MIN, ChaosTuning.TEASE_GOLD_MAX + 1));
        BankGold(gold);
        double pts = ChaosTuning.TEASE_DENIED_SCORE * _state.TotalMult * BoonPayMult;
        _state.Score += pts;
        _state.Focus += ChaosTuning.FOCUS_PER_DENIED;   // restraint feeds focus like a treat would
        ShowPopScore(pts);
        _state.PushEvent($"{ChaosGlyphs.Gold} denied. it pays +{gold} gold");
        ChaosAnnouncerOverlay.Announce($"DENIED. +{gold} {ChaosGlyphs.Gold} gold", ChaosAnnounceKind.PowerUp);
        Pulse(Color.FromRgb(0xFF, 0xD7, 0x00), 0.25);
        App.Bark?.NotifyChaosTeaseDenied(++_teaseDeniedThisRun);
        if (_teaseDeniedThisRun >= ChaosTuning.TEASE_DENIED_STREAK_COUNT && !_teaseDeniedStreakBarked)
        {
            _teaseDeniedStreakBarked = true;
            App.Bark?.NotifyChaosTeaseDeniedStreak(_teaseDeniedThisRun);
        }
    }

    /// <summary>The Echo triggered: NO payload — two smaller, faster lives burst out at its
    /// spot instead. Children are ordinary bubbles (light live trio) and never re-split.</summary>
    private void SpawnEchoChildren(EffectBubbleSpec parent)
    {
        if (_state == null) return;
        for (int i = 0; i < 2; i++)
        {
            var child = ChaosBubbleVariants.BuildEchoChild(parent.SizePx,
                BubbleService.ChaosLastPopXPx + Random.Shared.Next(-70, 71),
                BubbleService.ChaosLastPopYPx + Random.Shared.Next(-50, 51),
                _state.Config.EffectIntensity);
            App.Bubbles?.SpawnChaosBubble(child);
        }
        _state.PushEvent("◌ it splits — two more");
        Pulse(Color.FromRgb(0xC9, 0xC4, 0xE8), 0.30);
    }

    private void BeginWaveTransition(int newWave)
    {
        if (_state == null) return;
        ChaosLessonHooks.OnLoopCompleted();   // lessons: judge silk_touch, reset per-loop tallies

        // Drafts disabled → roll straight into the next wave with no pause.
        if (!_state.Config.BoonDraftEnabled)
        {
            _state.AllLiveNextWave = false;
            _state.DoubleOrNothingActive = false;
            _state.NextWavePayoutMult = 1.0;
            _state.WaveIndex = newWave;
            _state.ActIndex = 1 + (newWave - 1) / 5;
            App.Bark?.NotifyChaosWaveEscalated(newWave);
            FireActChangedIfCrossed();
            return;
        }

        _paused = true;
        _spawnTimer?.Stop();
        ChaosWaveTimerOverlay.Clear();   // the watch blanks while the draft table is out
        // Clear the field so live fuses don't tick (and detonate) behind the draft overlay,
        // with a single burst cue as everything pops.
        App.Bubbles?.PopAllBubbles();
        ChaosSfx.PlayWaveClear();
        // Wave-clear juice (additive only): a soft screen pulse + a clear bark on the boundary.
        Pulse(WAVE_CLEAR_COLOR, WAVE_CLEAR_PULSE);
        App.Bark?.NotifyChaosWaveCleared(_state.WaveIndex);

        // Clear the finished wave's transient (next-wave) flags before drafting.
        _state.AllLiveNextWave = false;
        _state.DoubleOrNothingActive = false;
        _state.NextWavePayoutMult = 1.0;

        _pendingWave = newWave;
        App.Bark?.NotifyChaosWaveEscalated(newWave);

        // Surrender capstone: every draft carries a sin (only while the user allows sins at all).
        var options = ChaosBoonPool.Draft(_state.Config.AllowCurses, _state.Config.DraftChoices,
            guaranteeCurse: _state.MaxedBoons.Contains("surrender"), takenIds: TakenBoonIds(),
            sinChance: _state.Config.SinChance);
        foreach (var o in options) ChaosMeta.MarkDiscovered("boon:" + o.Id);
        _overlay?.ShowBoonDraft(_state.WaveIndex, options, OnBoonChosen, _state.Config.DraftAutoResumeSec,
            rerollsLeft: _state.RerollsLeft, onReroll: RerollDraft);
    }

    /// <summary>Run-boon ids already drafted this descent (unique cards sit the rest out).</summary>
    private System.Collections.Generic.HashSet<string> TakenBoonIds()
    {
        var taken = new System.Collections.Generic.HashSet<string>();
        if (_state == null) return taken;
        foreach (var b in _state.ActiveBoons) taken.Add(b.Id);
        foreach (var b in _state.ActiveCurses) taken.Add(b.Id);
        return taken;
    }

    /// <summary>Taking Chances: spend one reroll for a fresh deal at the draft table. Null = none left.</summary>
    private (System.Collections.Generic.List<ChaosBoon> options, int rerollsLeft)? RerollDraft()
    {
        if (_state == null || _state.RerollsLeft <= 0) return null;
        _state.RerollsLeft--;
        _state.PushEvent("🎲 tempted fate again");
        var options = ChaosBoonPool.Draft(_state.Config.AllowCurses, _state.Config.DraftChoices,
            guaranteeCurse: _state.MaxedBoons.Contains("surrender"), takenIds: TakenBoonIds(),
            sinChance: _state.Config.SinChance);
        foreach (var o in options) ChaosMeta.MarkDiscovered("boon:" + o.Id);
        return (options, _state.RerollsLeft);
    }

    private void OnBoonChosen(ChaosBoon? boon)
    {
        if (_state == null) return;

        if (boon != null)
        {
            // Surrender capstone: the FIRST sin of the descent keeps its sweetness, loses its sting.
            bool sinShielded = boon.IsCurse && _state.MaxedBoons.Contains("surrender") && !_state.SurrenderShieldUsed;
            if (sinShielded) _state.SurrenderShieldUsed = true;

            ChaosLessonHooks.OnDraftCardTaken(boon.IsCurse);   // draft4 (any card) + surrender (sins)
            _state.ApplyBoon(boon, shieldDrawback: sinShielded);
            if (boon.Id == "extra_shield") Pulse(SHIELD_GAIN_COLOR, SHIELD_GAIN_PULSE);   // +2 shields cue
            if (boon.IsCurse)
            {
                // Surrender: each accepted sin sweetens the multiplier; at max, saying yes gives back.
                if (_state.SinExtraMult > 0)
                {
                    _state.BoonMult += _state.SinExtraMult;
                    _state.PushEvent($"🕯 sin embraced (+{_state.SinExtraMult:0.00}x)");
                }
                if (sinShielded) _state.PushEvent("🕯 the candle took the sting (no drawback)");
                if (_state.MaxedBoons.Contains("surrender"))
                {
                    _state.Shields += 1;
                    _state.PushEvent("🕯 you said yes. it gave back (+1 resistance)");
                    Pulse(SHIELD_GAIN_COLOR, SHIELD_GAIN_PULSE);
                }
                App.Bark?.NotifyChaosCursePicked(boon.Name, boon.Rarity.ToString(), boon.RunMultBonus);
                ChaosSfx.Play("sin_accept", 0.6f);
                ChaosAnnouncerOverlay.Announce($"☠ {boon.Name}", ChaosAnnounceKind.Temptation);
            }
            else
            {
                App.Bark?.NotifyChaosBoonPicked(boon.Name);
                ChaosAnnouncerOverlay.Announce($"◈ {boon.Name}", ChaosAnnounceKind.Mantra);   // ◈ mantra mark (✦ is drops only)
            }
        }
        else
        {
            _state.Shields += 1;
            _state.PushEvent("♥ resisted → +1 resistance");
            Pulse(SHIELD_GAIN_COLOR, SHIELD_GAIN_PULSE);
            App.Bark?.NotifyChaosBoonSkipped(_state.Shields);
            ChaosAnnouncerOverlay.Announce("+1 RESISTANCE", ChaosAnnounceKind.Willpower);
        }

        if (_state.DoubleOrNothingArmed)
        {
            _state.DoubleOrNothingActive = true;
            _state.DoubleOrNothingArmed = false;
        }

        _state.WaveIndex = _pendingWave;
        _state.ActIndex = 1 + (_state.WaveIndex - 1) / 5;
        FireActChangedIfCrossed();

        // A brief "Ready? :3" → "GO!" beat (same flashing display as run start) before the next
        // loop resumes, so the pick lands with a moment to settle. Stays paused until GO.
        if (_overlay != null) _overlay.ShowReadyGo(ResumeAfterDraft);
        else ResumeAfterDraft();
    }

    /// <summary>Un-pause the field and restart spawns after the post-pick "Ready? → GO!" beat.</summary>
    private void ResumeAfterDraft()
    {
        _paused = false;
        if (_spawning) _spawnTimer?.Start();
        // Welcome Shower: every loop's GO! dumps a quick rain of treats from the top.
        if (_state?.WelcomeShowerEnabled == true) SpawnWelcomeShower();
    }

    /// <summary>Welcome Shower: dump a handful of treats (flash/subliminal) raining from the top.</summary>
    private void SpawnWelcomeShower()
    {
        if (_state == null) return;
        try
        {
            int count = 6;
            for (int i = 0; i < count; i++)
            {
                var variant = ChaosBubbleVariants.All[Random.Shared.Next(2)];   // rows 0/1 = the treats
                var spec = ChaosBubbleVariants.Build(variant, _state.RunIntensity, _state.FuseTimeMult,
                    ChaosMotion.RainDown, _state.Config.EffectIntensity, _state.BubbleScale);
                App.Bubbles?.SpawnChaosBubble(spec);
            }
            App.Bubbles?.PlayChime(0.25f);
            _state.PushEvent("🚿 welcome shower — treats from above");
        }
        catch (Exception ex) { App.Logger?.Debug("SpawnWelcomeShower: {E}", ex.Message); }
    }

    // ============================ bubble callbacks ============================

    private double BasePoints(int strength) => 40 + strength * 1.6; // 40..200

    /// <summary>Lifetime-boon payout layer on bubble pops: Blindfold's pay multiplier (1.0 unworn).</summary>
    private double BoonPayMult => _state?.BlindfoldPayMult ?? 1.0;

    // Config-gated juice helpers (no-op when the user disabled that feedback).
    private void Pulse(Color color, double strength)
    {
        if (_state?.Config?.ColorFlashesEnabled == true) _fx?.Pulse(color, strength);
    }
    private void Shake(double baseIntensity, int durMs)
    {
        var cfg = _state?.Config;
        if (cfg?.ScreenShakeEnabled == true)
            App.ScreenShake?.Shake(Math.Clamp(baseIntensity * cfg.ShakeIntensity, 0, 1), durMs);
    }

    /// <summary>Blank Eyes: float the pop's real payout (mults, flips and all) just under the
    /// pop word, anchored at the popped bubble's centre (written by Bubble.Pop a moment ago).</summary>
    private void ShowPopScore(double pts)
    {
        if (_state?.ShowPopScores != true || pts <= 0) return;
        ChaosPopText.Show(BubbleService.ChaosLastPopXDip, BubbleService.ChaosLastPopYDip + 30,
            "+" + ((long)pts).ToString("N0"), Color.FromRgb(0xFF, 0xE9, 0xA0));
    }

    /// <summary>Taking Chances: every pop is a coin flip — x2 with the level's odds, else x0.5. 1.0 when unworn.</summary>
    private double ChanceFlip() =>
        _state?.ChanceDoubleOdds > 0 ? (Random.Shared.NextDouble() < _state.ChanceDoubleOdds ? 2.0 : 0.5) : 1.0;

    /// <summary>"Focus here...": x3 while the pendulum's slow swing holds (1.0 otherwise).</summary>
    private double PendulumFactor() =>
        _pendulumSlowActive && _state?.PendulumPayMult > 1 ? _state.PendulumPayMult : 1.0;

    /// <summary>Relapse's bonus loop pays double gold — every gold bank routes through here.</summary>
    private int GoldScaled(int gold) => _state?.RelapseLoopActive == true ? gold * 2 : gold;

    /// <summary>
    /// Bank an instant in-run payout as GOLD (ChaosGlyphs.Gold, her bench's coin; never drops/✦).
    /// Persists immediately via <see cref="ChaosMeta.AddGold"/>; the very first gold ever
    /// also gets its quiet debut beat (flag + bark + one feed line). When
    /// <paramref name="floatAtPop"/> is set, a small gold figure floats at the last pop point.
    /// </summary>
    private void BankGold(int gold, bool floatAtPop = false)
    {
        if (gold <= 0) return;
        bool first = !ChaosMeta.State.SeenGoldFirst;
        if (first) ChaosMeta.State.SeenGoldFirst = true;
        ChaosMeta.AddGold(gold);   // persists the balance (and the first-gold flag with it)
        if (floatAtPop)
            ChaosPopText.Show(BubbleService.ChaosLastPopXDip, BubbleService.ChaosLastPopYDip + 30,
                $"+{gold} {ChaosGlyphs.Gold}", Color.FromRgb(0xFF, 0xD7, 0x00));
        if (first)
        {
            _state?.PushEvent($"{ChaosGlyphs.Gold} gold. she takes it at her bench.");
            try { App.Bark?.NotifyChaosGoldFirst(); } catch { }
        }
    }

    /// <summary>Drip Feed drops per pop, doubled during the Relapse bonus loop.</summary>
    private int DropsPerPopNow() => (_state?.DropPerPop ?? 0) * (_state?.RelapseLoopActive == true ? 2 : 1);

    /// <summary>Cam Girl: any pop can tip gold (banked instantly at her bench's balance).</summary>
    private void RollCamGirlTip()
    {
        if (_state == null || _state.CamGirlTipChance <= 0) return;
        if (Random.Shared.NextDouble() >= _state.CamGirlTipChance) return;
        int tip = GoldScaled(Random.Shared.Next(2, 5));
        BankGold(tip, floatAtPop: true);
        _state.PushEvent($"{ChaosGlyphs.Gold} tipped +{tip} gold");
    }

    private void OnBenignPopped(EffectBubbleSpec spec)
    {
        if (_state == null || _paused || _manualPaused) return;
        CountRegenPop();   // Slow Recovery: every pop feeds the regen counter

        // Lucky golden bubble: pure treasure — real gold (Sparks) banked instantly, outside
        // the score/combo economy entirely (so coin flips, mults and streaks never touch it).
        // Pop-up Notification heart: pure kindness — +1 resistance, outside the score/combo
        // economy entirely (no points, no streak, no payload).
        if (spec.IsHeart)
        {
            _state.Shields += 1;
            _state.Focus += ChaosTuning.FOCUS_PER_HEART;
            _state.PushEvent("💖 pop-up notification! +1 resistance");
            ChaosAnnouncerOverlay.Announce("💖 +1 resistance", ChaosAnnounceKind.PowerUp);
            Pulse(SHIELD_GAIN_COLOR, 0.25);
            ChaosSfx.Play("resist_absorb", 0.55f);
            return;
        }

        // Gold Digger droplet: a little gold per bead, outside the score economy like its parent.
        if (spec.IsDroplet)
        {
            _state.Focus += ChaosTuning.FOCUS_PER_DROPLET;
            int dGold = GoldScaled(Random.Shared.Next(3, 8));
            BankGold(dGold, floatAtPop: true);
            _state.PushEvent($"{ChaosGlyphs.Gold} droplet +{dGold} gold");
            Pulse(Color.FromRgb(255, 215, 0), 0.12);
            ChaosSfx.Play("golden_pop", 0.35f);
            return;
        }

        if (spec.IsGolden)
        {
            _state.Focus += ChaosTuning.FOCUS_PER_GOLDEN;
            // Rabbit's Foot scales the gold per level (10–20 unworn … 20–40 at the capstone).
            int lvl = ChaosMeta.IsBoonActive("rabbits_foot") ? ChaosMeta.BoonLevel("rabbits_foot") : 0;
            var (gMin, gMax) = ChaosLifetimeBoons.GoldenPayRange(lvl);
            int gold = GoldScaled(Random.Shared.Next(gMin, gMax + 1));
            BankGold(gold, floatAtPop: true);
            _state.PushEvent($"{ChaosGlyphs.Gold} lucky bubble! +{gold} gold");
            ChaosAnnouncerOverlay.Announce($"{ChaosGlyphs.Gold} +{gold} gold", ChaosAnnounceKind.PowerUp);
            Pulse(Color.FromRgb(255, 215, 0), 0.35);
            ChaosSfx.Play("golden_pop", 0.6f);   // coins spill — real gold just landed
            // Gold Digger: the lucky bubble bursts into 3 falling droplets at the pop point.
            if (_state.GoldDiggerEnabled)
            {
                for (int i = 0; i < 3; i++)
                    App.Bubbles?.SpawnChaosBubble(ChaosBubbleVariants.BuildGoldDroplet(
                        BubbleService.ChaosLastPopXPx + Random.Shared.Next(-50, 51),
                        BubbleService.ChaosLastPopYPx + Random.Shared.Next(-20, 21)));
                _state.PushEvent("⛏ gold digger — it spills");
            }
            return;
        }

        // Mimic prism ("Look at the bright colors..."): 10x pay — and the copied effect fires.
        if (spec.IsPrism)
        {
            ChaosLessonHooks.OnPrismPopped();   // taking_chances lesson
            _state.Focus += ChaosTuning.FOCUS_PER_PRISM;
            FireScaledPayload(spec.Payload);
            _state.EffectsFired++;
            _state.Combo++;
            _state.Heat = Math.Min(1.0, _state.Heat + 0.05);
            double prismPts = BasePoints(spec.Strength) * 10.0 * _state.TotalMult * BoonPayMult;
            _state.Score += prismPts;
            ShowPopScore(prismPts);
            ChaosAnnouncerOverlay.Announce($"🔮 the colors! 10x — it was {spec.Payload.DisplayName}", ChaosAnnounceKind.Temptation);
            _state.PushEvent($"🔮 prism! 10x · {spec.Payload.DisplayName} fires");
            Pulse(Color.FromRgb(0xC8, 0xA8, 0xFF), 0.40);
            App.Achievements?.TrackBubblePopped();
            RollCamGirlTip();
            CheckComboMilestone();
            return;
        }

        spec.Payload.Fire();                 // benign pop = a treat
        ChaosLessonHooks.OnTreatPopped(spec);   // vibe_popping / chain_reaction / the_pull / intrusive_thoughts
        _state.EffectsFired++;
        _state.Combo++;
        // Focus economy: every treat-class pop refuels the hand, REGARDLESS of source (a
        // rabbit mowing treats still feeds the player). Heavies refuel a little extra.
        _state.Focus += spec.PayMult > 1 ? ChaosTuning.FOCUS_PER_HEAVY : ChaosTuning.FOCUS_PER_POP;
        _state.Heat = Math.Min(1.0, _state.Heat + 0.04);
        // Golden Touch charm raises the calm-pop baseline (0.4 unworn → 0.45–0.60 by level).
        double benignMult = _state.BenignBaseline;
        double pts = BasePoints(spec.Strength) * benignMult * spec.PayMult * PendulumFactor()
                     * ChanceFlip() * _state.TotalMult * BoonPayMult;
        _state.Score += pts;
        if (_state.DropPerPop > 0) _state.TrickleDrops += DropsPerPopNow();   // Drip Feed (x2 in the relapse loop)
        ShowPopScore(pts);                                                     // Blank Eyes
        App.Achievements?.TrackBubblePopped();
        Pulse(BENIGN_POP_COLOR, BENIGN_POP_PULSE);   // the most-frequent action now has a tiny pop pulse
        if (spec.PayMult > 1) _state.PushEvent("🪨 heavy drop! x3");
        RollCamGirlTip();
        // Body Buzz: one pop in eight detonates an electric shockwave — the ring strikes every
        // bubble it covers. Shockwave pops re-enter here and re-roll, but 1/8 damps the cascade.
        if (_state.EStimShockwaveChance > 0 && Random.Shared.NextDouble() < _state.EStimShockwaveChance)
        {
            App.Bubbles?.TriggerEStimShockwave(new System.Windows.Point(
                BubbleService.ChaosLastPopXPx, BubbleService.ChaosLastPopYPx));
            Pulse(Color.FromRgb(0x7A, 0xE0, 0xFF), 0.25);
            _state.PushEvent("⚡ body buzz — the current spreads");
        }
        // GG make more GG: sometimes a popped treat bursts into 3 wild sweeper rabbits.
        if (_state.GgRabbitChance > 0 && Random.Shared.NextDouble() < _state.GgRabbitChance)
        {
            for (int i = 0; i < 3; i++)
                App.Bubbles?.SpawnChaosBubble(ChaosBubbleVariants.BuildDarter(_state.RunIntensity, spotlight: false,
                    BubbleService.ChaosLastPopXPx + Random.Shared.Next(-40, 41),
                    BubbleService.ChaosLastPopYPx + Random.Shared.Next(-40, 41),
                    sweeper: true));
            ChaosSfx.Play("rabbit_spawn", 0.5f);
            _state.PushEvent("🐇 GG! they multiply");
        }
        App.Bark?.NotifyChaosBenignPopped(spec.VariantId, spec.Payload.DisplayName, _state.Combo);
        _state.PushEvent($"○ popped {spec.Payload.DisplayName}");
        CheckComboMilestone();
    }

    /// <summary>A treat (flash/subliminal/golden) sat unpopped past its 5s screen life: it
    /// dissolved — no pop, no payload — and the streak HALVES. Letting rewards rot really
    /// hurts now, which keeps the player chasing the good bubbles too.</summary>
    private void OnTreatExpired(EffectBubbleSpec spec)
    {
        if (_state == null || _paused || _manualPaused) return;
        string name = spec.IsGolden ? "lucky bubble" : spec.Payload.DisplayName;
        if (_state.Combo > 1)
        {
            _state.Combo /= 2;
            _state.PushEvent($"💨 {name} faded… streak halved to x{_state.Combo}");
        }
        else
        {
            _state.Combo = 0;
            _state.PushEvent($"💨 {name} faded away");
        }
        Pulse(Color.FromRgb(150, 150, 175), 0.10);   // faint grey sigh — a reward slipped by
    }

    // ---- hold-to-defuse: the focus gate + channel feedback (BubbleService calls these) ----
    private DateTime _invulnUntilUtc = DateTime.MinValue;   // Snap Chain: triggers bounce off inside this window
    private double _focusLowAccumSec;                        // rh_focus_low: dry-spell stopwatch
    private bool _focusLowBarkFired;                         // once per run max

    /// <summary>Defuse cost for one channel on this bubble (Bound halves pay half each,
    /// so the pair totals one normal defuse).</summary>
    private double DefuseCostFor(EffectBubbleSpec spec) =>
        spec.IsBoundHalf ? ChaosTuning.DEFUSE_COST_BOUND : ChaosTuning.DEFUSE_COST;

    /// <summary>May the player's press start a defuse channel? Frozen fields channel for free —
    /// otherwise the focus must cover the bubble's cost (deducted on COMPLETION, not here).</summary>
    private bool CanChannelDefuse(EffectBubbleSpec spec)
    {
        if (_state == null) return false;
        bool ok = _freezeRemainingSec > 0 || _state.Focus >= DefuseCostFor(spec);
        if (ok) ChaosLessonHooks.OnChannelStarted();   // slow_fuses: the hold's clock starts now
        return ok;
    }

    /// <summary>A channel never completed — the bubble is already triggering (onDetonate carries
    /// the consequences); this is purely the "WHY it blew" feedback + first-time barks.</summary>
    private void OnChannelBroken(EffectBubbleSpec spec, string reason)
    {
        if (_state == null) return;
        ChaosLessonHooks.OnChannelBroken();   // slow_fuses: bank the partial hold ("nofocus" never started one)
        switch (reason)
        {
            case "nofocus":
                // Distinct cue + red flash so the lesson lands: you grabbed what you couldn't pay for.
                ChaosSfx.Play("focus_empty", 0.55f);
                Pulse(Color.FromRgb(255, 80, 80), 0.22);
                _state.PushEvent("✋ no focus — it triggers in your grip");
                if (!ChaosMeta.State.SeenBarkDefuseNoFocus)
                {
                    ChaosMeta.State.SeenBarkDefuseNoFocus = true; ChaosMeta.Save();
                    App.Bark?.NotifyChaosDefuseNoFocus();
                }
                break;
            case "click":
                _state.PushEvent("💥 a tap isn't a hold");
                if (!ChaosMeta.State.SeenBarkClickDetonate)
                {
                    ChaosMeta.State.SeenBarkClickDetonate = true; ChaosMeta.Save();
                    App.Bark?.NotifyChaosClickDetonate();
                }
                break;
            default:   // "release" (early let-go, or the cursor strayed off the bubble)
                _state.PushEvent("💥 you let go");
                if (!ChaosMeta.State.SeenBarkDefuseRelease)
                {
                    ChaosMeta.State.SeenBarkDefuseRelease = true; ChaosMeta.Save();
                    App.Bark?.NotifyChaosDefuseRelease();
                }
                break;
        }
    }

    private void OnDefused(EffectBubbleSpec spec, double fuseSecLeft, bool viaChannel)
    {
        if (_state == null || _paused || _manualPaused) return;
        // The player's hand pays for its defuses; toys, chains and zones never do. A channel
        // completed during a freeze is free (that's the freeze's reward).
        if (viaChannel)
        {
            if (_freezeRemainingSec <= 0) _state.Focus -= DefuseCostFor(spec);
            if (!ChaosMeta.State.SeenBarkDefuseFirst)
            {
                ChaosMeta.State.SeenBarkDefuseFirst = true; ChaosMeta.Save();
                App.Bark?.NotifyChaosDefuseFirst();
            }
        }
        // Snap Chain mantra: every completed defuse opens a brief invulnerability window.
        if (_state.DefuseInvulnMs > 0)
            _invulnUntilUtc = DateTime.UtcNow.AddMilliseconds(_state.DefuseInvulnMs);
        CountRegenPop();   // Slow Recovery: snaps count toward the regen threshold too
        ChaosLessonHooks.OnDefuseCompleted(fuseSecLeft, viaChannel);   // snap_field / blindfold / last_breath / slow_fuses
        _state.Defused++;
        _state.Combo++;
        _state.Heat = Math.Min(1.0, _state.Heat + 0.07);
        // Last Breath: snapping at the very brink pays fortunes (window + payout scale by level).
        double lastBreath = _state.LastBreathWindowSec > 0 && fuseSecLeft <= _state.LastBreathWindowSec
            ? _state.LastBreathPayMult : 1.0;
        // Slowburner capstone: a snap inside the trance's final 1.5 seconds pays triple.
        double slowburn = fuseSecLeft <= 1.5 && _state.MaxedBoons.Contains("slowburner") ? 3.0 : 1.0;
        double pts = BasePoints(spec.Strength) * 1.0 * lastBreath * slowburn * PendulumFactor()
                     * ChanceFlip() * _state.TotalMult * BoonPayMult;
        _state.Score += pts;
        if (_state.DropPerPop > 0) _state.TrickleDrops += DropsPerPopNow();   // Drip Feed (x2 in the relapse loop)
        ShowPopScore(pts);                                                     // Blank Eyes
        App.Achievements?.TrackBubblePopped();
        RollCamGirlTip();
        // Playing with fire: a snap inside the final second pays gold on the spot.
        if (_state.LastSecondGoldEnabled && fuseSecLeft <= 1.0)
        {
            int fGold = GoldScaled(Random.Shared.Next(5, 10));
            BankGold(fGold, floatAtPop: true);
            _state.PushEvent($"{ChaosGlyphs.Gold} playing with fire +{fGold} gold");
            Pulse(Color.FromRgb(255, 140, 60), 0.30);
        }
        // Aftermath: a brink-snap leaves 2s of crackling residue at the pop point.
        if (_state.AftermathEnabled && fuseSecLeft <= 1.5)
        {
            App.Bubbles?.AddChaosResidue(new Point(BubbleService.ChaosLastPopXPx, BubbleService.ChaosLastPopYPx));
            _state.PushEvent("⚡ aftermath — the air still crackles");
        }
        // Size Queen: every snap rings outward and pops the treats it touches.
        if (_state.RippleEnabled)
            App.Bubbles?.TriggerChaosRipple(new Point(BubbleService.ChaosLastPopXPx, BubbleService.ChaosLastPopYPx));
        App.Bark?.NotifyChaosBubbleDefused(_state.Combo, spec.VariantId, _state.Config.Difficulty.ToString());
        if (lastBreath > 1)
        {
            _state.PushEvent($"⏱ last breath! x{lastBreath:0}");
            ChaosAnnouncerOverlay.Announce($"⏱ last breath x{lastBreath:0}", ChaosAnnounceKind.PowerUp);
            Pulse(Color.FromRgb(255, 215, 0), 0.40);   // gold flash — you earned it
        }
        else
        {
            Pulse(Color.FromRgb(90, 255, 150), 0.16);  // soft green confirm
        }
        if (slowburn > 1) _state.PushEvent("🐌 slow burn! x3");
        _state.PushEvent($"✔ snapped {spec.Payload.DisplayName}");
        CheckComboMilestone();
    }

    private void OnDetonated(EffectBubbleSpec spec)
    {
        if (_state == null || _paused || _manualPaused) return;
        if (spec.IsEcho)
        {
            SpawnEchoChildren(spec);         // The Echo fires NO conditioning payload — it SPLITS
        }
        else
        {
            FirePayloadForDetonation(spec);  // the threat goes off (ambient mode may soften it)
            _state.EffectsFired++;
        }
        _state.Detonated++;
        _runDetonations++;
        ChaosLessonHooks.OnDetonation();   // silk_touch: the loop is no longer clean

        string variant = spec.VariantId;     // payload ctx = variant id (e.g. "braindrain","video")
        string diff = _state.Config.Difficulty.ToString();
        double s = spec.Strength / 100.0;

        // Snap Chain mantra: inside the post-snap invulnerability window a trigger can't take
        // anything from you — the payload already fired above, but streak, lust and resistance
        // all hold (and no shield is spent).
        if (DateTime.UtcNow < _invulnUntilUtc)
        {
            _state.PushEvent($"⛓ snap chain holds ({spec.Payload.DisplayName})");
            Pulse(Color.FromRgb(0x7A, 0xE0, 0xFF), 0.22);
            App.Bark?.NotifyChaosBubbleDetonatedAbsorbed(variant, spec.Strength, _runDetonations, _state.Combo, diff, _state.Shields);
            return;
        }

        int shieldCost = _state.DoubleOrNothingActive ? 2 : 1;
        if (_state.Shields >= shieldCost)
        {
            _state.Shields -= shieldCost;
            _state.Heat = Math.Max(0, _state.Heat - 0.2);
            _state.PushEvent($"♥ resistance crumbles ({spec.Payload.DisplayName})");
            // The last point of resistance going has its own, sadder cue.
            ChaosSfx.Play(_state.Shields == 0 ? "resist_crumble" : "resist_absorb", 0.6f);
            Pulse(Color.FromRgb(80, 160, 255), 0.28);    // blue shield-save
            Shake(0.25 + s * 0.3, 280);
            // Clutch shield-save branch — distinct trigger so the matcher can praise it.
            App.Bark?.NotifyChaosBubbleDetonatedAbsorbed(variant, spec.Strength, _runDetonations, _state.Combo, diff, _state.Shields);
        }
        else if (_state.CollarSaves > 0)
        {
            // Collar: out of resistance, but the streak is held — combo and lust survive the hit.
            // The payload still fired above; the collar protects the chain, not the screen.
            _state.CollarSaves--;
            _state.PushEvent($"📿 the collar holds ({_state.CollarSaves} left)");
            ChaosSfx.Play("collar_save", 0.6f);
            Pulse(Color.FromRgb(255, 215, 0), 0.32);             // gold save flash
            Shake(0.25 + s * 0.3, 280);
            // Unleashed: the save ITSELF strikes back — a golden shockwave snaps every live bubble.
            if (_state.UnleashedEnabled)
            {
                App.Bubbles?.DefuseAllLive();
                ChaosAnnouncerOverlay.Announce("📿 UNLEASHED", ChaosAnnounceKind.PowerUp);
                _state.PushEvent("📿 unleashed — the field lets go");
                Pulse(Color.FromRgb(255, 215, 0), 0.50);
            }
            App.Bark?.NotifyChaosBubbleDetonatedAbsorbed(variant, spec.Strength, _runDetonations, _state.Combo, diff, _state.Shields);
        }
        else
        {
            int comboBeforeBreak = _state.Combo;   // capture BEFORE zeroing (frozen contract)
            _state.Combo = 0;
            _lastComboBigFired = 0;                // combo broke → reset ComboBig crossing tracking
            _state.Heat = 0;
            _state.PushEvent($"💥 {spec.Payload.DisplayName} triggered!");
            ChaosSfx.Play("trigger", 0.55f);                     // the muffled boom under the payload stinger
            Pulse(Color.FromRgb(255, 50, 50), 0.4 + s * 0.35);   // red malus
            Shake(0.4 + s * 0.5, 380);                           // the malus jolt
            // Real-hit branch (unshielded only now).
            App.Bark?.NotifyChaosBubbleDetonated(variant, spec.Strength, _runDetonations, comboBeforeBreak, diff);
        }
    }

    /// <summary>
    /// Fire a detonating bubble's payload. In opt-in Ambient mode, an intrusive payload
    /// (video / HT link) is remapped to one of the two lighter ambient payloads so a
    /// background run never yanks a fullscreen video/browser over the user's work.
    /// Outside ambient mode the payload fires exactly as built (neutral invariant).
    /// </summary>
    // ---- heavy-payload gate (video + gif cascade): ONE at a time, never queued ----
    // Stacked LibVLC players + cascade windows is what crashed the app (2026-06-10): while a
    // heavy effect runs, any further heavy detonation is simply dropped — no queue, no stack.
    private DateTime _heavyUntilUtc = DateTime.MinValue;     // expected end of the running heavy effect
    private DateTime _chaosVideoCapUtc = DateTime.MinValue;  // hard stop for a chaos-fired video
    private const double VIDEO_HARD_CAP_SEC = 15;            // = VideoPayload.SEGMENT_SEC (random 15s slice)

    /// <summary>True while a mandatory video or gif cascade is (still) running. The cascade is
    /// checked for REAL (slow clips can outlive the time estimate); the timer remains as a floor
    /// covering window-open latency and the post-video teardown quarantine.</summary>
    private bool HeavyEffectActive =>
        App.Video?.IsPlaying == true || ChaosGifCascadeOverlay.IsRaining || DateTime.UtcNow < _heavyUntilUtc;

    /// <summary>Seconds the heavy gate stays shut AFTER a video ends. Closing the fullscreen
    /// video windows + disposing LibVLC players runs async for several seconds; raising the
    /// cascade's layered window into that churn wedged the render thread twice (22:41, 22:50
    /// freezes — both were "cascade ~4s after video teardown").</summary>
    private const double VIDEO_TEARDOWN_QUARANTINE_SEC = 6;

    private void ExtendHeavyQuarantine(double sec)
    {
        var until = DateTime.UtcNow.AddSeconds(sec);
        if (until > _heavyUntilUtc) _heavyUntilUtc = until;
    }

    /// <summary>Any video ending during a run (natural end, attention-check retry, cap) starts
    /// the teardown quarantine so no cascade rises into the LibVLC disposal churn.</summary>
    private void OnVideoEndedDuringRun(object? sender, EventArgs e) =>
        ExtendHeavyQuarantine(VIDEO_TEARDOWN_QUARANTINE_SEC);

    /// <summary>Fire a payload under "Playing with fire": detonation durations scale by the sin's
    /// knob for exactly this call (GlobalDurationMult is shared with slow-mo/freeze — scope it).</summary>
    private void FireScaledPayload(EffectPayload payload)
    {
        ChaosLessonHooks.OnPayloadFired(payload.Kind);   // blindfold: the screen turns busy
        double m = _state?.DetonationDurationMult ?? 1.0;
        if (m == 1.0) { payload.Fire(); return; }
        EffectPayload.GlobalDurationMult *= m;
        try { payload.Fire(); }
        finally { EffectPayload.GlobalDurationMult /= m; }
    }

    private void FirePayloadForDetonation(EffectBubbleSpec spec)
    {
        try
        {
            if (_state?.Config?.AmbientMode == true && IsIntrusivePayload(spec.Payload.Kind))
            {
                // Soft remap. While a heavy effect runs the coin always lands on text — never a
                // second cascade on top of a running one.
                bool cascade = !HeavyEffectActive && _ambientRng.NextDouble() < 0.5;
                EffectPayload soft = cascade ? new GifCascadePayload() : new BouncingTextPayload();
                soft.Strength = spec.Payload.Strength;
                if (cascade)
                    _heavyUntilUtc = DateTime.UtcNow.AddSeconds(GifCascadePayload.DURATION_SEC + 5);
                PlayPayloadStinger(cascade ? "fx_rain_start" : "fx_text");
                FireScaledPayload(soft);
                return;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("Chaos ambient remap: {E}", ex.Message); }

        // ONE heavy effect at a time: a video/cascade detonating while another heavy effect is
        // up gets dropped on the floor (the shield/streak consequences upstream still applied).
        var kind = spec.Payload.Kind;
        bool heavy = kind == EffectBubblePayloadKind.Video || kind == EffectBubblePayloadKind.GifCascade;
        if (heavy && HeavyEffectActive)
        {
            App.Logger?.Information("Chaos: dropped {Kind} detonation — a heavy effect is already running", kind);
            _state?.PushEvent($"▶ the deep is busy — {spec.Payload.DisplayName} fizzles");
            return;
        }
        if (kind == EffectBubblePayloadKind.Video)
        {
            _chaosVideoCapUtc = DateTime.UtcNow.AddSeconds(VIDEO_HARD_CAP_SEC);
            _heavyUntilUtc = DateTime.UtcNow.AddSeconds(VIDEO_HARD_CAP_SEC + 3);   // cap + open/close slack
        }
        else if (kind == EffectBubblePayloadKind.GifCascade)
        {
            // Spawn window + the last clips' ride to the bottom of the screen.
            _heavyUntilUtc = DateTime.UtcNow.AddSeconds(GifCascadePayload.DURATION_SEC + 5);
        }
        PlayPayloadStinger(StingerForVariant(spec.VariantId));
        FireScaledPayload(spec.Payload);   // Playing with fire: detonations linger 50% longer
    }

    /// <summary>Flavor stinger over the detonation boom — one per payload family. Video, flash,
    /// and subliminal payloads carry their own audio, so they get no stinger.</summary>
    private static string StingerForVariant(string variantId) => variantId switch
    {
        "braindrain" => "fx_drain",
        "bambifreeze" => "fx_freeze",
        "htlink" => "fx_rain_start",
        _ => "",
    };

    private static void PlayPayloadStinger(string name)
    {
        if (name.Length > 0) ChaosSfx.Play(name, 0.45f);
    }

    private static bool IsIntrusivePayload(EffectBubblePayloadKind k) =>
        k == EffectBubblePayloadKind.Video || k == EffectBubblePayloadKind.HtLink;

    private static readonly Random _ambientRng = new();

    private void OnDarterCaught(EffectBubbleSpec spec, bool quick)
    {
        if (_state == null || _paused || _manualPaused) return;
        int pts = ChaosBubbleVariants.DARTER_BASE_POINTS + (quick ? ChaosBubbleVariants.DARTER_QUICK_BONUS : 0);
        _state.Score += pts * _state.TotalMult;
        _state.Focus += ChaosTuning.FOCUS_PER_RABBIT;
        _state.Combo++;
        _state.Heat = Math.Min(1.0, _state.Heat + 0.05);
        App.Achievements?.TrackBubblePopped();
        ChaosLessonHooks.OnRabbitCaught();   // rabbit_caller lesson
        // The darter is a utility pickup: catching it slows time (no conditioning jolt).
        ActivateSlowMo();
        Pulse(Color.FromRgb(120, 200, 255), quick ? 0.32 : 0.24);   // icy slow-mo flash
        App.Bark?.NotifyChaosDarterCaught(pts, _state.Combo, quick);
        _state.PushEvent(quick ? "⚡ quick catch! time slows" : "🐇 white rabbit caught! time slows");
        CheckComboMilestone();
    }

    /// <summary>Catching a Freeze bubble is a GOOD pickup: it freezes the whole field — every bubble
    /// shudders then holds in place, live fuses pause, spawns halt — and plays an icy "freeze" burst
    /// with a held white-blue edge glow and per-bubble blue auras. Refreshes on each catch.</summary>
    private void OnFreezeCaught(EffectBubbleSpec spec)
    {
        if (_state == null || _paused || _manualPaused) return;
        _state.Score += FREEZE_BASE_POINTS * _state.TotalMult;
        _state.Combo++;
        _state.Heat = Math.Min(1.0, _state.Heat + 0.05);
        App.Achievements?.TrackBubblePopped();
        ChaosLessonHooks.OnFreezeCaught();   // freeze_trigger lesson (pickups only, not the toy)
        ActivateFreeze();
        App.Bark?.NotifyChaosFreezeCaught(FREEZE_BASE_POINTS * _state.TotalMult, _state.Combo);
        _state.PushEvent("❄ frozen. the field holds");
        CheckComboMilestone();
    }

    // ---- darter slow-mo power-up ----
    private const double SLOWMO_FACTOR = 0.12;        // chaos motion/fuse speed while active (lower = stronger slow)
    private const double SLOWMO_DURATION_SEC = 6.0;   // real-time length of the slow-mo
    private double _slowMoRemainingSec;

    /// <summary>Catching a darter slows the whole field: bubbles drift slower, live fuses last
    /// longer, spawns stretch out, and flash/overlay payloads linger. Refreshes on each catch.
    /// Default duration is base + the Pendulum boon's bonus; pass an explicit duration to
    /// override (the Collar capstone's short recovery beat).</summary>
    private bool _slowMoCueOn;   // an in-cue played; the matching out-cue is owed on end

    private void ActivateSlowMo(double? durationSec = null, string bannerLabel = "Time Slow")
    {
        _slowMoRemainingSec = durationSec ?? (SLOWMO_DURATION_SEC + (_state?.SlowMoBonusSec ?? 0));
        // "Focus here...": triple pay rides ONLY the pendulum's own swing — a darter slow-mo
        // refreshing over it hands the window back to normal scoring.
        _pendulumSlowActive = bannerLabel == "Pendulum";
        App.Bubbles?.SetChaosTimeScale(SLOWMO_FACTOR);
        ChaosEffectBannerOverlay.Show("slowmo", bannerLabel, Color.FromRgb(0x7A, 0xE0, 0xFF));
        EffectPayload.GlobalDurationMult = 1.0 / SLOWMO_FACTOR;
        if (!_slowMoCueOn) ChaosSfx.Play("time_slow_in", 0.5f);   // refreshes shouldn't re-warp
        _slowMoCueOn = true;
    }

    // ---- Slow Recovery charm: resistance regen EARNED by pops (time-based habit retired 2026-06-10) ----
    private int _regenPopCount;

    /// <summary>Every pop (treat, golden, or snap) feeds Slow Recovery: once
    /// <see cref="ChaosRunState.ShieldRegenPops"/> pops accumulate while you're below the
    /// resistance you descended with, one point knits back and the count restarts.</summary>
    private void CountRegenPop()
    {
        if (_state == null || _state.ShieldRegenPops <= 0) return;
        if (_state.Shields >= _state.StartShields) { _regenPopCount = 0; return; }
        if (++_regenPopCount < _state.ShieldRegenPops) return;
        _regenPopCount = 0;
        _state.Shields += 1;
        _state.PushEvent("♥ resistance regrows");
        Pulse(SHIELD_GAIN_COLOR, 0.18);
    }

    // ---- Blindfold capstone: one global heartbeat tracking the field's closest fuse ----
    private double _heartbeatCooldownSec;

    private void TickBlindfoldHeartbeat(double dt)
    {
        if (_state == null || !_state.MaxedBoons.Contains("blindfold")) return;
        _heartbeatCooldownSec -= dt;
        if (_heartbeatCooldownSec > 0) return;
        var fuse = App.Bubbles?.MinChaosFuseSec();
        if (fuse == null || fuse > 4.0) { _heartbeatCooldownSec = 0; return; }
        // Quickens as the nearest detonation closes in: ~1/s at 4s out, capped at ~3/s.
        _heartbeatCooldownSec = Math.Clamp(fuse.Value / 4.0, 0.33, 1.0);
        try
        {
            var path = ModResourceResolver.ResolveAudioPath("chaos/heartbeat.mp3");
            App.Bubbles?.PlayCue(path, 0.5f);   // File.Exists-guarded: silent until the asset ships
        }
        catch (Exception ex) { App.Logger?.Debug("Blindfold heartbeat: {E}", ex.Message); }
    }

    private void EndSlowMo()
    {
        _slowMoRemainingSec = 0;
        _pendulumSlowActive = false;
        App.Bubbles?.SetChaosTimeScale(1.0);
        ChaosEffectBannerOverlay.End("slowmo");
        if (_slowMoCueOn) ChaosSfx.Play("time_slow_out", 0.45f);
        _slowMoCueOn = false;
        if (_freezeRemainingSec <= 0) EffectPayload.GlobalDurationMult = 1.0;   // don't clobber an active freeze
    }

    // ============================ active skills (toys you press) ============================
    // Equipped active-use Skills fire on their keybind (pass-through global hook, run-scoped)
    // or their HUD button. Cooldowns/charges live per run on ChaosToyState (HUD binds them).

    private Services.GlobalKeyboardHook? _keyHook;
    private double _vibeRemainingSec;   // VibePopping buzz window (real clock, like the power-ups)
    private bool _dvdBannerOn;          // the "PORN DVD" banner is up; drop it when the last logo lands
    private readonly System.Collections.Generic.List<ChaosToyButtonWindow> _toyButtons = new();

    private void CloseToyButtons()
    {
        foreach (var b in _toyButtons.ToArray())
            try { b.Close(); } catch { }
        _toyButtons.Clear();
    }

    /// <summary>Build the HUD button/keybind state for equipped active-use skills. Slot order =
    /// catalogue order; slot 1 fires on ChaosAccessoryKey1 (slot 2 arrives with the second pocket).</summary>
    private void BuildActiveToys()
    {
        if (_state == null) return;
        _state.ActiveToys.Clear();
        // Pockets are sewn at her bench: zero pockets = no toy buttons, no keybinds —
        // even if a stale save still has an active toy flagged.
        int pockets = ChaosMeta.SlotsFor(ChaosBoonCategory.Skill);
        if (pockets <= 0) return;
        var s = App.Settings?.Current;
        string[] keys = { s?.ChaosAccessoryKey1 ?? "Q", s?.ChaosAccessoryKey2 ?? "E" };
        int slot = 0;
        foreach (var b in ChaosLifetimeBoons.All)
        {
            if (slot >= pockets) break;
            if (!b.IsActiveUse || !ChaosMeta.IsBoonActive(b.Id)) continue;
            if (!_state.ToyPower.TryGetValue(b.Id, out var power)) continue;
            var toy = new ChaosToyState
            {
                Id = b.Id, Name = b.Name, Glyph = b.Glyph, Desc = b.Desc, CapstoneDesc = b.CapstoneDesc,
                KeyLabel = slot < keys.Length ? keys[slot] : "",
                CooldownSec = b.UseCooldownSec,
            };
            if (b.UseCooldownSec <= 0) toy.ChargesLeft = (int)power;   // charge-based (Freeze Trigger)
            _state.ActiveToys.Add(toy);
            slot++;
        }
    }

    private void StartKeyHook()
    {
        if (_state == null || _state.ActiveToys.Count == 0) return;
        try
        {
            _keyHook = new Services.GlobalKeyboardHook();
            _keyHook.KeyPressed += OnToyKey;
            _keyHook.Start();
        }
        catch (Exception ex) { App.Logger?.Debug("Chaos toy key hook: {E}", ex.Message); }
    }

    private void StopKeyHook()
    {
        try
        {
            if (_keyHook != null)
            {
                _keyHook.KeyPressed -= OnToyKey;
                _keyHook.Dispose();
            }
        }
        catch { }
        _keyHook = null;
    }

    /// <summary>Hook callback → marshal to the dispatcher. Keys pass through to whatever has
    /// focus (the hook never swallows); cooldowns absorb accidental fires while typing.</summary>
    private void OnToyKey(System.Windows.Input.Key key)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return;
        disp.BeginInvoke(new Action(() =>
        {
            if (!_spawning || _state == null || _paused || _manualPaused) return;
            string name = key >= System.Windows.Input.Key.D0 && key <= System.Windows.Input.Key.D9
                ? ((int)(key - System.Windows.Input.Key.D0)).ToString()
                : key.ToString();
            foreach (var toy in _state.ActiveToys)
                if (!string.IsNullOrEmpty(toy.KeyLabel) &&
                    toy.KeyLabel.Equals(name, StringComparison.OrdinalIgnoreCase))
                { UseToy(toy); break; }
        }));
    }

    /// <summary>HUD button entry point.</summary>
    public void UseToyById(string id)
    {
        if (_state == null) return;
        foreach (var t in _state.ActiveToys)
            if (t.Id == id) { UseToy(t); return; }
    }

    private void UseToy(ChaosToyState toy)
    {
        if (_state == null || !_spawning || _paused || _manualPaused) return;
        // The urge sin: your toys are off-limits for the rest of the run.
        if (_state.ActivesDisabled)
        {
            ChaosSfx.Play("toy_denied", 0.4f);
            _state.PushEvent("🫦 the urge holds your hands — no toys");
            return;
        }
        if (!toy.IsReady) { ChaosSfx.Play("toy_denied", 0.4f); return; }
        double power = _state.ToyPower.TryGetValue(toy.Id, out var p) ? p : 0;
        bool maxed = _state.MaxedBoons.Contains(toy.Id);

        switch (toy.Id)
        {
            case "vibe_popping":
                // Hold the button and sweep: everything the cursor passes over pops, live ones snap.
                // Capstone: the hold isn't needed — hovering alone pops.
                App.Bubbles?.SetVibePop(true, hoverPops: maxed);
                _vibeRemainingSec = Math.Max(1, power);
                _afterglowApplied = false;   // each buzz earns one fresh afterglow window
                toy.CooldownRemainingSec = toy.CooldownSec;
                toy.IsEffectActive = true;
                ChaosVibeTrailOverlay.Start();   // pointer glow + trail while the buzz runs
                ChaosEffectBannerOverlay.Show("vibe", "VibePopping", Color.FromRgb(0xFF, 0xB0, 0x3A));
                _state.PushEvent("🔸 it buzzes. hold and sweep");
                break;

            case "freeze_trigger":
                if (toy.ChargesLeft <= 0) { ChaosSfx.Play("toy_denied", 0.4f); return; }
                toy.ChargesLeft--;
                ChaosSfx.Play("freeze_trigger", 0.6f);
                ActivateFreeze();   // identical to catching a freeze bubble (shows the FREEZE banner)
                if (maxed) App.Bubbles?.DefuseAllLive();
                toy.CooldownRemainingSec = 3;   // anti-doubletap between charges
                toy.IsEffectActive = true;
                _state.PushEvent("❄ everything holds still");
                break;

            case "porn_dvd":
                int lvl = ChaosMeta.BoonLevel(toy.Id);
                double speed = lvl switch { 1 => 0.7, 2 => 0.85, _ => 1.0 };
                double scale = lvl switch { 1 => 0.8, 2 => 0.9, _ => 1.0 };
                ChaosDvdOverlay.Launch(Math.Max(5, power), speed, scale, count: maxed ? 2 : 1,
                    splitBounces: _state.DvdSplitBounces);   // Casting Couch: 2, then 4
                ChaosSfx.Play("dvd_launch", 0.55f);
                toy.CooldownRemainingSec = toy.CooldownSec;
                toy.IsEffectActive = true;
                ChaosEffectBannerOverlay.Show("dvd", "Porn DVD", Color.FromRgb(0xFF, 0x69, 0xB4));
                _dvdBannerOn = true;
                _state.PushEvent("📀 now loading");
                break;

            case "snap_field":
                // One clean snap: every live bubble on screen lets go. Capstone clears EVERYTHING.
                if (maxed) App.Bubbles?.PopAllBubbles();
                else App.Bubbles?.DefuseAllLive();
                ChaosSfx.Play("freeze_trigger", 0.5f);
                toy.CooldownRemainingSec = Math.Max(5, power);   // level value IS the cooldown (60/45/30s)
                _snapFlashRemainingSec = 1.0;                    // brief glow on the hero button
                toy.IsEffectActive = true;
                Pulse(Color.FromRgb(150, 220, 255), 0.35);
                _state.PushEvent(maxed ? "✋ snapped. all of it." : "✋ snapped — every live one let go");
                break;

            case "rabbit_caller":
                // Whistle, then point: the cursor glows and the rabbits (plus the capstone
                // storm) arrive wherever the player clicks next — see RabbitAimTick.
                ArmRabbitCall(Math.Max(1, (int)power), maxed);
                ChaosSfx.Play("toy_ready", 0.5f);
                toy.CooldownRemainingSec = toy.CooldownSec;
                toy.IsEffectActive = true;
                _state.PushEvent("🐇 the whistle hangs — your next click calls them");
                break;

            case "e_stim":
                // Charge up: the next N clicks on good bubbles discharge lightning into the
                // bubbles around them. Capstone turns each charged pop into a chain reaction.
                // Overload mantra: the charge runs double.
                int charges = Math.Max(1, (int)power) * Math.Max(1, _state.EStimChargeMult);
                App.Bubbles?.ArmEStim(charges, maxed);
                ChaosEStimOverlay.Arm();   // violet halo rides the cursor while charges wait
                ChaosSfx.Play("toy_ready", 0.5f);
                toy.CooldownRemainingSec = toy.CooldownSec;
                toy.IsEffectActive = true;
                ChaosEffectBannerOverlay.Show("estim", "E-Stim", Color.FromRgb(0x9C, 0x5C, 0xFF));
                Pulse(Color.FromRgb(0x9C, 0x5C, 0xFF), 0.25);
                _state.PushEvent(maxed
                    ? $"⚡ charged — your next {charges} pops chain-react"
                    : $"⚡ charged — your next {charges} pops conduct");
                break;
        }
    }

    /// <summary>A charged E-Stim pop just discharged (per-arc juice lives here; the bolts and
    /// the staggered pops are BubbleService's). Drops the banner once the last charge is spent.</summary>
    private void OnEStimArc(int chargesLeft)
    {
        if (_state == null) return;
        ChaosSfx.Play("estim_zap", 0.6f);   // the lightning arc cracks
        Pulse(Color.FromRgb(0x9C, 0x5C, 0xFF), 0.25);
        if (chargesLeft > 0)
        {
            _state.PushEvent($"⚡ the current arcs ({chargesLeft} left)");
        }
        else
        {
            ChaosEffectBannerOverlay.End("estim");
            ChaosEStimOverlay.Disarm();
            _state.PushEvent("⚡ the charge is spent");
        }
    }

    // ---- Snap Field / Rabbit Caller / Intrusive Thoughts transient state ----
    private double _snapFlashRemainingSec;     // brief hero-button glow after a snap
    private int _spawnSerial;                  // Heavy Drop: ordinary-spawn counter (every Nth goes giant)
    private bool _pendulumSlowActive;          // the running slow-mo IS the pendulum ("Focus here..." pays now)
    private bool _afterglowApplied;            // Afterglow: one lingering window per buzz
    private int _pendulumRolledWave;           // Pendulum event: wave the swing was last rolled for
    private double _pendulumFireAtProgress;    // 0..1 wave-progress beat the swing fires at
    private bool _pendulumFiredThisWave;
    private int _heartRolledWave;              // Pop-up Notification: wave the heart was last rolled for
    private double _heartFireAtProgress;       // 0..1 wave-progress beat the heart drifts in at
    private bool _heartArmedThisWave;          // this wave's 60% roll landed and hasn't fired yet
    private double _rabbitStormRemainingSec;   // Rabbit Caller capstone: storm window
    private double _rabbitStormAccumSec;       // spawns one rabbit every 1.25s while the storm runs
    private double _thoughtAccumSec;           // Intrusive Thoughts: one bouncing text every 5s

    /// <summary>Intrusive Thoughts: a phrase from the user's bouncing-text pool (fallback included).</summary>
    private static string PickThoughtText()
    {
        try
        {
            var pool = App.Settings?.Current?.BouncingTextPool;
            if (pool != null)
            {
                var enabled = pool.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
                if (enabled.Count > 0) return enabled[Random.Shared.Next(enabled.Count)];
            }
        }
        catch { }
        return "GIVE IN";
    }

    /// <summary>Force-spawn one white rabbit right now (Rabbit Caller; storm ticks reuse it).
    /// Optional physical-px point pins the spawn there (the summon-at-click).</summary>
    private void SpawnDarter(double? atPxX = null, double? atPxY = null)
    {
        if (_state == null) return;
        var spec = ChaosBubbleVariants.BuildDarter(_state.RunIntensity, spotlight: false, atPxX, atPxY);
        ChaosMeta.MarkDiscovered("bubble:darter");
        App.Bubbles?.SpawnChaosBubble(spec);
    }

    // ---- Rabbit Caller: whistle, then point — the rabbits arrive at your next click ----
    private System.Windows.Threading.DispatcherTimer? _rabbitAimTimer;
    private int _rabbitCallPending;          // rabbits waiting on the click (0 = not armed)
    private bool _rabbitCallMaxed;           // capstone storm rides the summon
    private bool _rabbitAimPrevDown;         // press-edge detection (starts true: the arming click)

    /// <summary>Arm the whistle: the cursor glows, and the next click summons the rabbits there.</summary>
    private void ArmRabbitCall(int rabbits, bool maxed)
    {
        _rabbitCallPending = rabbits;
        _rabbitCallMaxed = maxed;
        _rabbitAimPrevDown = true;   // swallow the press that armed us (HUD/toy-button click)
        ChaosCursorGlowOverlay.Arm();
        if (_rabbitAimTimer == null)
        {
            _rabbitAimTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
            _rabbitAimTimer.Tick += RabbitAimTick;
        }
        _rabbitAimTimer.Start();
    }

    /// <summary>Stand down without summoning (run end / teardown).</summary>
    private void DisarmRabbitCall()
    {
        _rabbitCallPending = 0;
        _rabbitAimTimer?.Stop();
        ChaosCursorGlowOverlay.Disarm();
    }

    private void RabbitAimTick(object? sender, EventArgs e)
    {
        try
        {
            if (_rabbitCallPending <= 0 || _state == null || !_active)
            {
                DisarmRabbitCall();
                return;
            }
            if (!GetCursorPos(out var cur)) return;
            ChaosCursorGlowOverlay.MoveToPx(cur.X, cur.Y);

            bool down = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
            bool pressed = down && !_rabbitAimPrevDown;
            _rabbitAimPrevDown = down;
            // The field must be live for the summon (drafts/pauses hold the whistle).
            if (!pressed || _paused || _manualPaused || !_spawning) return;

            int rabbits = _rabbitCallPending;
            bool maxed = _rabbitCallMaxed;
            DisarmRabbitCall();
            for (int i = 0; i < rabbits; i++)
            {
                // A small scatter so a triple summon doesn't stack into one spot.
                double jx = cur.X + Random.Shared.Next(-60, 61);
                double jy = cur.Y + Random.Shared.Next(-60, 61);
                SpawnDarter(jx, jy);
            }
            if (maxed)
            {
                _rabbitStormRemainingSec = 10.0;             // ~8 more over the next 10s
                _rabbitStormAccumSec = 0;
                ChaosEffectBannerOverlay.Show("rabbits", "Rabbit Storm", Color.FromRgb(0xFF, 0x4D, 0xC4));
            }
            ChaosSfx.Play("rabbit_spawn", 0.5f);
            _state.PushEvent(maxed ? $"🐇 {rabbits} at your fingertip… and the warren is emptying"
                                   : $"🐇 {rabbits} answered at your fingertip");
        }
        catch (Exception ex) { App.Logger?.Debug("RabbitAimTick: {E}", ex.Message); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out System.Drawing.Point lpPoint);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    private const int VK_LBUTTON = 0x01;

    private void TickActiveToys(double dt)
    {
        if (_state == null) return;
        if (_vibeRemainingSec > 0)
        {
            _vibeRemainingSec -= dt;
            if (_vibeRemainingSec <= 0)
            {
                // Afterglow mantra: the buzz doesn't quite stop — hovering keeps popping and the
                // trail keeps drawing for the lingering window, then it truly ends.
                if (_state.AfterglowSec > 0 && !_afterglowApplied)
                {
                    _afterglowApplied = true;
                    _vibeRemainingSec = _state.AfterglowSec;
                    App.Bubbles?.SetVibePop(true, hoverPops: true);
                    _state.PushEvent("🔸 afterglow — it lingers");
                }
                else
                {
                    App.Bubbles?.SetVibePop(false);
                    ChaosVibeTrailOverlay.Stop();
                    ChaosEffectBannerOverlay.End("vibe");
                }
            }
        }
        if (_snapFlashRemainingSec > 0) _snapFlashRemainingSec -= dt;
        // Pendulum: a free once-per-loop random event (the habit retired 2026-06-10) — at a
        // random beat of every loop the world dips into slow-mo on its own, announced on top.
        if (_spawning && _state.WaveIndex != _pendulumRolledWave)
        {
            _pendulumRolledWave = _state.WaveIndex;
            _pendulumFireAtProgress = 0.15 + Random.Shared.NextDouble() * 0.65;   // somewhere mid-loop
            _pendulumFiredThisWave = false;
        }
        if (_spawning && !_pendulumFiredThisWave && _state.WaveProgress >= _pendulumFireAtProgress
            && _freezeRemainingSec <= 0 && _slowMoRemainingSec <= 0)
        {
            _pendulumFiredThisWave = true;
            ActivateSlowMo(2.5, bannerLabel: "Pendulum");   // so the swing reads as ITS OWN event, not a darter
            ChaosSfx.PlayTickTock();   // tick-tock underlay while time hangs (silent until the asset ships)
            ChaosAnnouncerOverlay.Announce(_state.PendulumPayMult > 1
                ? "🕰 FOCUS HERE — everything pays x3"      // the mantra turns the swing into a scoring window
                : "🕰 the pendulum swings", ChaosAnnounceKind.PowerUp);
            _state.PushEvent("🕰 the pendulum swings");
        }
        // Pop-up Notification habit: once per loop, sometimes (60%), a little heart drifts
        // down at a random beat. Catch = +1 resistance; a miss just exits the bottom.
        if (_state.Config.PopupHeartEnabled && _spawning && _state.WaveIndex != _heartRolledWave)
        {
            _heartRolledWave = _state.WaveIndex;
            _heartArmedThisWave = Random.Shared.NextDouble() < 0.60;
            _heartFireAtProgress = 0.20 + Random.Shared.NextDouble() * 0.60;
        }
        if (_heartArmedThisWave && _spawning && _state.WaveProgress >= _heartFireAtProgress)
        {
            _heartArmedThisWave = false;
            App.Bubbles?.SpawnChaosBubble(ChaosBubbleVariants.BuildHeart());
            App.Bubbles?.PlayChime(0.22f);   // the soft ping — a notification, after all
        }
        // Rabbit Caller capstone storm: one more rabbit every 1.25s for the window's length.
        if (_rabbitStormRemainingSec > 0)
        {
            _rabbitStormRemainingSec -= dt;
            _rabbitStormAccumSec += dt;
            if (_rabbitStormAccumSec >= 1.25) { _rabbitStormAccumSec = 0; SpawnDarter(); }
            if (_rabbitStormRemainingSec <= 0) ChaosEffectBannerOverlay.End("rabbits");
        }
        // Intrusive Thoughts: every 5s a bouncing text races across on its own (30% faster than
        // the DVD's; level sets its 3/4/5s life). Holds its tongue while time is frozen.
        if (_state.IntrusiveThoughtsSec > 0 && _freezeRemainingSec <= 0)
        {
            _thoughtAccumSec += dt;
            if (_thoughtAccumSec >= 5.0)
            {
                _thoughtAccumSec = 0;
                ChaosDvdOverlay.Launch(_state.IntrusiveThoughtsSec, 1.3, 0.8, count: 1,
                    text: PickThoughtText(),
                    splitOnRabbit: _state.MaxedBoons.Contains("intrusive_thoughts"));
            }
        }
        foreach (var t in _state.ActiveToys)
        {
            if (t.CooldownRemainingSec > 0)
            {
                t.CooldownRemainingSec -= dt;
                // Ready-again ding the instant the cooldown lapses (spent charge-toys stay quiet).
                if (t.CooldownRemainingSec <= 0 && t.ChargesLeft != 0) ChaosSfx.Play("toy_ready", 0.45f);
            }
            // Glow state: each toy knows whether its own temporary effect is still running.
            t.IsEffectActive = t.Id switch
            {
                "vibe_popping" => _vibeRemainingSec > 0,
                "freeze_trigger" => _freezeRemainingSec > 0,
                "porn_dvd" => ChaosDvdOverlay.AnyToyActive,
                "snap_field" => _snapFlashRemainingSec > 0,
                "rabbit_caller" => _rabbitCallPending > 0 || _rabbitStormRemainingSec > 0,
                "e_stim" => (App.Bubbles?.EStimChargesLeft ?? 0) > 0,
                _ => false,
            };
        }
        if (_dvdBannerOn && !ChaosDvdOverlay.AnyToyActive) { ChaosEffectBannerOverlay.End("dvd"); _dvdBannerOn = false; }
    }

    // ---- freeze-bubble power-up ----
    private const double FREEZE_DURATION_SEC = 3.5;   // real-time length of the freeze
    private const double FREEZE_DURATION_MULT = 2.5;  // flash/overlay payloads linger this much longer while frozen
    private const int    FREEZE_VIBRATE_MS    = 200;  // whole-field shudder as the freeze lands
    private const int    FREEZE_BASE_POINTS   = 140;
    private static readonly Color FREEZE_EDGE = Color.FromRgb(150, 210, 255);   // icy white-blue
    private double _freezeRemainingSec;

    private void ActivateFreeze()
    {
        _freezeRemainingSec = FREEZE_DURATION_SEC;
        _freezeCueOn = true;   // the catch/toy cue covers the way in; the shatter covers the way out
        App.Bubbles?.SetChaosFrozen(true);
        App.Bubbles?.VibrateAllForFreeze(FREEZE_VIBRATE_MS);
        ChaosEffectBannerOverlay.Show("freeze", "Freeze", Color.FromRgb(0x96, 0xD2, 0xFF));
        EffectPayload.GlobalDurationMult = FREEZE_DURATION_MULT;
        if (_state?.Config?.ColorFlashesEnabled == true)
        {
            _fx?.FreezeBurst(FREEZE_EDGE);        // icy "ice hit" flash
            _fx?.BeginEdgeHold(FREEZE_EDGE, 0.5); // sustained white-blue edges for the frozen window
        }
        Shake(0.3, FREEZE_VIBRATE_MS);
    }

    private bool _freezeCueOn;   // the field is audibly frozen; the shatter is owed on release

    private void EndFreeze()
    {
        _freezeRemainingSec = 0;
        App.Bubbles?.SetChaosFrozen(false);
        ChaosEffectBannerOverlay.End("freeze");
        if (_freezeCueOn) ChaosSfx.Play("freeze_shatter", 0.5f);
        _freezeCueOn = false;
        if (_slowMoRemainingSec <= 0) EffectPayload.GlobalDurationMult = 1.0;   // don't clobber an active slow-mo
        _fx?.EndEdgeHold();
    }

    private void CheckComboMilestone()
    {
        if (_state == null) return;
        int combo = _state.Combo;
        if (combo <= 0) return;

        // Big-combo crossing: edge-detected, fires ONCE per threshold per streak.
        foreach (int t in COMBO_BIG_THRESHOLDS)
        {
            if (combo >= t && _lastComboBigFired < t)
            {
                _lastComboBigFired = t;
                _state.PushEvent($"🔥🔥 STREAK x{combo}!");
                App.Bark?.NotifyChaosComboBig(combo, t);
                ChaosSfx.Play("streak_milestone", 0.5f);
                ChaosAnnouncerOverlay.Announce($"STREAK ×{t}", ChaosAnnounceKind.Streak);
                Pulse(COMBO_BIG_COLOR, COMBO_BIG_PULSE);   // distinct bigger beat
            }
        }

        if (combo % 10 == 0)
        {
            _state.PushEvent($"🔥 streak x{combo}!");
            App.Bark?.NotifyChaosComboMilestone(combo, _state.Config.Difficulty.ToString());
            Pulse(Color.FromRgb(255, 200, 60), 0.4);   // gold combo flash
        }
        else
        {
            // Micro-build between milestones: a tiny pulse that grows toward the next ×10.
            double frac = (combo % 10) / 10.0;
            double strength = COMBO_MICRO_PULSE_MIN + (COMBO_MICRO_PULSE_MAX - COMBO_MICRO_PULSE_MIN) * frac;
            Pulse(COMBO_MICRO_COLOR, strength);
        }
    }

    /// <summary>Fire ChaosActChanged once when ActIndex advances (edge-detected, not per tick).</summary>
    private void FireActChangedIfCrossed()
    {
        if (_state == null) return;
        if (_state.ActIndex > _lastActFired)
        {
            _lastActFired = _state.ActIndex;
            App.Bark?.NotifyChaosActChanged(_state.ActIndex, _state.WaveIndex);
            ChaosSfx.Play("depth_change", 0.55f);
            ChaosAnnouncerOverlay.Announce($"DEPTH {_state.ActIndex}", ChaosAnnounceKind.Depth);
        }
    }

    /// <summary>Make heat visible: a subtle rising temperature tint as Heat climbs (additive
    /// held-edge overlay only — never touches scoring/multiplier math). Honors the color-flashes toggle.</summary>
    private void UpdateHeatTint()
    {
        if (_state?.Config?.ColorFlashesEnabled != true || _fx == null) return;
        double heat = _state.Heat;
        double level = heat <= HEAT_TINT_MIN
            ? 0
            : (heat - HEAT_TINT_MIN) / (1.0 - HEAT_TINT_MIN);   // 0..1 above the floor
        // Quantize so we don't re-issue an animation every 250ms tick.
        double q = Math.Round(level, 1);
        if (Math.Abs(q - _lastHeatTint) < 0.05) return;
        _lastHeatTint = q;
        if (q <= 0) _fx.EndHeatTint();
        else _fx.SetHeatTint(HEAT_TINT_COLOR, q * HEAT_TINT_MAX_OPACITY);
    }

    // ============================ end / teardown ============================

    public void RequestStop()
    {
        if (_spawning) EndRun();
    }

    /// <summary>
    /// Hard teardown for app exit / main-window close: stop everything, clear all
    /// chaos bubbles, close the HUD + overlay, resume the ambient game. No results,
    /// no payout. Safe to call when no run is active.
    /// </summary>
    public void ForceShutdown()
    {
        if (!_active && _hud == null && _overlay == null) return;
        _spawning = false;
        _active = false;
        _paused = false;
        _runTimer?.Stop();
        _spawnTimer?.Stop();
        try { App.Bubbles?.EndChaosMode(); } catch { }
        try { App.Bubbles?.Resume(); } catch { }
        StopKeyHook();
        CloseToyButtons();
        EndSlowMo(); EndFreeze();
        try { ChaosFlashOverlay.CloseActive(); } catch { }
        try { ChaosGifCascadeOverlay.CloseActive(); } catch { }
        try { ChaosAnnouncerOverlay.CloseActive(); } catch { }
        try { ChaosDvdOverlay.CloseActive(); } catch { }
        try { ChaosEffectBannerOverlay.CloseActive(); } catch { }
        try { ChaosWaveTimerOverlay.CloseActive(); } catch { }
        try { DisarmRabbitCall(); ChaosCursorGlowOverlay.CloseActive(); } catch { }
        try { ChaosVibeTrailOverlay.CloseActive(); } catch { }
        try { ChaosEStimOverlay.CloseActive(); } catch { }
        try { ChaosFieldFxOverlay.CloseActive(); } catch { }
        try { ChaosPopText.ShutdownPool(); } catch { }
        try { _fx?.Close(); } catch { }
        if (_overlay != null)
        {
            _overlay.OnDismissed = null;   // avoid re-entrant cleanup
            _overlay.OnRunAgain = null;
            try { _overlay.Close(); } catch { }
        }
        try { _hud?.Close(); } catch { }
        CleanupAfterRun();
    }

    private void EndRun()
    {
        if (!_spawning || _state == null) return;
        // Lessons: final-loop + end-of-descent judgments (popup_notification / extreme_tier /
        // silk_touch). A quit mid-fall (RequestStop) didn't run the full course.
        ChaosLessonHooks.OnRunCompleted(_state.Shields, _state.Config.Difficulty,
            ranFullCourse: _state.ElapsedSec >= _state.RunDurationSec);
        _spawning = false;
        if (App.Video != null) App.Video.VideoStarted -= OnVideoStartedDuringRun;
        if (App.Video != null) App.Video.VideoEnded -= OnVideoEndedDuringRun;
        _runTimer?.Stop();
        _spawnTimer?.Stop();
        StopKeyHook();
        CloseToyButtons();
        App.Bubbles?.EndChaosMode();
        EndSlowMo(); EndFreeze();
        try { ChaosFlashOverlay.CloseActive(); } catch { }
        try { ChaosGifCascadeOverlay.CloseActive(); } catch { }
        try { ChaosAnnouncerOverlay.CloseActive(); } catch { }
        try { ChaosDvdOverlay.CloseActive(); } catch { }
        try { ChaosEffectBannerOverlay.CloseActive(); } catch { }
        try { ChaosWaveTimerOverlay.CloseActive(); } catch { }
        try { DisarmRabbitCall(); ChaosCursorGlowOverlay.CloseActive(); } catch { }
        try { ChaosVibeTrailOverlay.CloseActive(); } catch { }
        try { ChaosEStimOverlay.CloseActive(); } catch { }
        try { ChaosFieldFxOverlay.CloseActive(); } catch { }
        try { ChaosPopText.ShutdownPool(); } catch { }
        try { _fx?.Close(); } catch { }
        _fx = null;

        double durMin = Math.Max(1, _state.RunDurationSec) / 60.0;
        double capBase = 250.0 * durMin * _state.Config.DifficultyMult;
        double baseXp = Math.Min(_state.Score, capBase);
        double skillMult = _state.SkillMult;
        double finalXp = baseXp * skillMult;

        try { App.Progression?.AddXP(baseXp, XPSource.Chaos); }
        catch (Exception ex) { App.Logger?.Debug("Chaos payout AddXP: {E}", ex.Message); }

        // Capture the previous best BEFORE the meta award updates it — for the PB delta line/bark.
        long previousBest = ChaosMeta.State.BestScore;

        // Meta-progression: bank Sparks + update lifetime stats (separate from XP payout).
        int sparksEarned = 0;
        try { sparksEarned = ChaosMeta.AwardRunRewards(_state); }
        catch (Exception ex) { App.Logger?.Debug("Chaos meta award: {E}", ex.Message); }

        // RunsCompleted just moved — queue any freshly crossed reveals for the next dollhouse open.
        try { RevealService.Sync("run_end"); } catch { }

        // Rank spine: did this descent push the rank past the last card shown? Only the
        // HIGHEST new rank gets a card (a debug fast-forward skips the ones in between).
        ChaosRank? rankUp = null;
        try
        {
            var nowRank = ChaosRanks.For(ChaosMeta.State.RunsCompleted);
            if ((int)nowRank > ChaosMeta.State.LastRankSeen) rankUp = nowRank;
        }
        catch { }

        string diff = _state.Config.Difficulty.ToString();
        App.Bark?.NotifyChaosRunCompleted((int)finalXp, diff);

        _hud?.Close();
        _hud = null;
        _overlay?.ShowResults(_state, baseXp, skillMult, finalXp, previousBest, sparksEarned, rankUp);

        App.Bubbles?.Resume();
        App.Logger?.Information("Chaos run complete: base {Base:0} x skill {Mult:0.0} = {Final:0} XP (defused {D}, detonated {Det})",
            baseXp, skillMult, finalXp, _state.Defused, _state.Detonated);
    }

    private void RunAgain()
    {
        // Tear down the current run windows, then start a fresh one (short 1s GO flash on restart).
        _overlay?.Close();   // triggers OnOverlayClosed → CleanupAfterRun
        Application.Current?.Dispatcher.BeginInvoke(new Action(() => StartRun(isRestart: true)));
    }

    private void OnOverlayClosed()
    {
        // Results dismissed (or window closed mid-run). Ensure everything is torn down.
        if (_spawning)
        {
            _spawning = false;
            _runTimer?.Stop();
            _spawnTimer?.Stop();
            App.Bubbles?.EndChaosMode();
            App.Bubbles?.Resume();
        }
        try { _hud?.Close(); } catch { }
        CleanupAfterRun();
    }

    private void CleanupAfterRun()
    {
        if (App.Video != null) App.Video.VideoStarted -= OnVideoStartedDuringRun;   // belt-and-suspenders (mid-run close)
        if (App.Video != null) App.Video.VideoEnded -= OnVideoEndedDuringRun;
        StopKeyHook();   // idempotent; covers the overlay-closed-mid-run path
        CloseToyButtons();
        try { ChaosDvdOverlay.CloseActive(); } catch { }
        try { ChaosEffectBannerOverlay.CloseActive(); } catch { }
        try { ChaosWaveTimerOverlay.CloseActive(); } catch { }
        try { DisarmRabbitCall(); ChaosCursorGlowOverlay.CloseActive(); } catch { }
        try { ChaosVibeTrailOverlay.CloseActive(); } catch { }
        try { ChaosEStimOverlay.CloseActive(); } catch { }
        try { ChaosFieldFxOverlay.CloseActive(); } catch { }
        try { ChaosPopText.ShutdownPool(); } catch { }
        App.AvatarWindow?.SetChaosRunActive(false);   // restore the avatar's normal attached z-order
        _runTimer = null;
        _spawnTimer = null;
        _hud = null;
        _overlay = null;
        _fx = null;
        _state = null;
        _active = false;
        _spawning = false;
        _paused = false;
        _manualPaused = false;
    }
}
