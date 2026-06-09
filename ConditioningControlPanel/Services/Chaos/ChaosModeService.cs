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
    // ---- near-miss danger telegraph (last window before a live bubble detonates) ----
    /// <summary>How long before a live detonation the escalating danger telegraph runs.</summary>
    public const int NEAR_MISS_TELEGRAPH_MS = 800;
    private static readonly Color NEAR_MISS_COLOR = Color.FromRgb(255, 60, 40);
    private const double NEAR_MISS_PULSE = 0.18;     // per-tick escalating edge-flash strength
    private const int NEAR_MISS_TICK_MS = 220;       // cadence of the escalating tick
    // ---- heat temperature tint (additive overlay only; rises with HeatMult) ----
    private static readonly Color HEAT_TINT_COLOR = Color.FromRgb(255, 90, 40);
    private const double HEAT_TINT_MIN = 0.30;       // heat must exceed this before the tint shows
    private const double HEAT_TINT_MAX_OPACITY = 0.30;  // peak held-edge opacity at full heat
    private double _lastHeatTint = -1;               // last applied heat-tint level (avoid churn)
    private double _nearMissTickAccumMs;             // throttles the escalating telegraph tick

    public bool IsRunning => _active;

    public bool IsManuallyPaused => _manualPaused;

    public void ToggleManualPause()
    {
        if (!_spawning || _paused) return;
        _manualPaused = !_manualPaused;
        if (_manualPaused)
        {
            // Freeze the whole field: stop spawning, hold the clock (RunTick early-returns),
            // and freeze every bubble's motion + fuse so nothing detonates while paused.
            _spawnTimer?.Stop();
            App.Bubbles?.SetChaosFrozen(true);
            _state?.PushEvent("⏸ paused — frozen");
        }
        else
        {
            App.Bubbles?.SetChaosFrozen(false);
            _spawnTimer?.Start();
            _state?.PushEvent("▶ resumed");
        }
    }

    // ============================ start / countdown ============================

    public void StartRun(ChaosRunConfig? config = null, bool isRestart = false)
    {
        if (_active) return;
        var cfg = config ?? ChaosRunConfig.FromSettings();

        try
        {
            App.Bubbles?.PauseAndClear();
            _state = new ChaosRunState(cfg);
            _active = true;

            _hud = new ChaosHudWindow(_state, this);
            _hud.Show();

            _overlay = new ChaosOverlayWindow();
            _overlay.OnRunAgain = RunAgain;
            _overlay.OnDismissed = OnOverlayClosed;
            _overlay.Show();
            _overlay.ShowCountdown(BeginRun, shortFlash: isRestart);   // 1s flash on RunAgain
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "ChaosModeService.StartRun failed");
            CleanupAfterRun();
            App.Bubbles?.Resume();
            MessageBox.Show("Couldn't start Chaos Mode:\n\n" + ex, "Chaos Mode",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BeginRun()
    {
        if (!_active || _state == null) return;

        App.Bubbles?.BeginChaosMode(OnBenignPopped, OnDefused, OnDetonated, OnDarterCaught, OnFreezeCaught);
        App.Bark?.NotifyChaosRunStarted(_state.Config.Difficulty.ToString());
        _state.PushEvent("⚡ run started");
        _runDetonations = 0;
        _lastComboBigFired = 0;
        _lastActFired = 1;
        _lastHeatTint = -1;
        EndSlowMo(); EndFreeze();   // clean power-up state for the new run (no leak across runs)
        App.Overlay?.WarmSpiralCache();   // pre-decode the spiral off-thread so its first show doesn't hitch

        // Loadout: a pre-equipped start boon enters the run already active (before wave 1).
        var equipped = ChaosMeta.State.EquippedStartBoon;
        if (!string.IsNullOrEmpty(equipped))
        {
            var boon = ChaosBoonPool.All.FirstOrDefault(b => b.Id == equipped);
            if (boon != null) { _state.ApplyBoon(boon); ChaosMeta.MarkDiscovered("boon:" + boon.Id); }
        }
        _spawning = true;

        try { _fx = new ChaosFxWindow(); _fx.Show(); } catch (Exception ex) { App.Logger?.Debug("Chaos FX init: {E}", ex.Message); }

        _runTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _runTimer.Tick += RunTick;
        _runTimer.Start();

        _spawnTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _spawnTimer.Tick += SpawnTick;
        _spawnTimer.Start();
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
        UpdateNearMissTelegraph(dt);

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

        if (elapsed >= _state.RunDurationSec) { EndRun(); return; }

        double waveLen = (double)_state.RunDurationSec / _state.WaveCount;
        int newWave = Math.Min(_state.WaveCount, 1 + (int)(elapsed / waveLen));
        _state.WaveProgress = (elapsed % waveLen) / waveLen;

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

        int maxConcurrent = (int)Math.Round((4 + intensity * 7) * Math.Sqrt(diffFactor)) + cfg.MaxBubblesBonus;
        if ((App.Bubbles?.ActiveBubbles ?? 0) < maxConcurrent)
        {
            var spec = ChaosBubbleVariants.Pick(effIntensity, _state.FuseTimeMult,
                cfg.MotionOverride, cfg.EnabledVariants, cfg.EffectIntensity);
            ChaosMeta.MarkDiscovered("bubble:" + spec.VariantId);
            App.Bubbles?.SpawnChaosBubble(spec);
        }

        // Darters spawn on their own intensity-scaled roll, independent of the bubble cap.
        if (cfg.DartersEnabled)
        {
            var darter = ChaosBubbleVariants.RollDarter(effIntensity);
            if (darter != null) { ChaosMeta.MarkDiscovered("bubble:darter"); App.Bubbles?.SpawnChaosBubble(darter); }
        }

        double interval = (1300 - intensity * 850) / diffFactor;
        if (_slowMoRemainingSec > 0) interval /= SLOWMO_FACTOR;   // slow-mo stretches the spawn cadence
        _spawnTimer!.Interval = TimeSpan.FromMilliseconds(Math.Max(280, interval));
    }

    private void BeginWaveTransition(int newWave)
    {
        if (_state == null) return;

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

        var options = ChaosBoonPool.Draft(_state.Config.AllowCurses, _state.Config.DraftChoices);
        foreach (var o in options) ChaosMeta.MarkDiscovered("boon:" + o.Id);
        _overlay?.ShowBoonDraft(_state.WaveIndex, options, OnBoonChosen, _state.Config.DraftAutoResumeSec);
    }

    private void OnBoonChosen(ChaosBoon? boon)
    {
        if (_state == null) return;

        if (boon != null)
        {
            _state.ApplyBoon(boon);
            if (boon.Id == "extra_shield") Pulse(SHIELD_GAIN_COLOR, SHIELD_GAIN_PULSE);   // +2 shields cue
            if (boon.IsCurse)
                App.Bark?.NotifyChaosCursePicked(boon.Name, boon.Rarity.ToString(), boon.RunMultBonus);
            else
                App.Bark?.NotifyChaosBoonPicked(boon.Name);
        }
        else
        {
            _state.Shields += 1;
            _state.PushEvent("⛊ skipped → +1 shield");
            // Shield-gain cue (+1 from skip) — pulse + bark.
            Pulse(SHIELD_GAIN_COLOR, SHIELD_GAIN_PULSE);
            App.Bark?.NotifyChaosBoonSkipped(_state.Shields);
        }

        if (_state.DoubleOrNothingArmed)
        {
            _state.DoubleOrNothingActive = true;
            _state.DoubleOrNothingArmed = false;
        }

        _state.WaveIndex = _pendingWave;
        _state.ActIndex = 1 + (_state.WaveIndex - 1) / 5;
        FireActChangedIfCrossed();

        _paused = false;
        if (_spawning) _spawnTimer?.Start();
    }

    // ============================ bubble callbacks ============================

    private double BasePoints(int strength) => 40 + strength * 1.6; // 40..200

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

    private void OnBenignPopped(EffectBubbleSpec spec)
    {
        if (_state == null || _paused) return;
        spec.Payload.Fire();                 // benign pop = a treat
        _state.EffectsFired++;
        _state.Combo++;
        _state.Heat = Math.Min(1.0, _state.Heat + 0.04);
        // golden_touch upgrade bumps benign-pop scoring (0.4 → 0.6); neutral when unowned.
        double benignMult = _state.Config.GoldenTouchBaseline ? 0.6 : 0.4;
        _state.Score += BasePoints(spec.Strength) * benignMult * _state.TotalMult;
        App.Achievements?.TrackBubblePopped();
        Pulse(BENIGN_POP_COLOR, BENIGN_POP_PULSE);   // the most-frequent action now has a tiny pop pulse
        App.Bark?.NotifyChaosBenignPopped(spec.VariantId, spec.Payload.DisplayName, _state.Combo);
        _state.PushEvent($"○ popped {spec.Payload.DisplayName}");
        CheckComboMilestone();
    }

    private void OnDefused(EffectBubbleSpec spec)
    {
        if (_state == null || _paused) return;
        _state.Defused++;
        _state.Combo++;
        _state.Heat = Math.Min(1.0, _state.Heat + 0.07);
        _state.Score += BasePoints(spec.Strength) * 1.0 * _state.TotalMult;
        App.Achievements?.TrackBubblePopped();
        App.Bark?.NotifyChaosBubbleDefused(_state.Combo, spec.VariantId, _state.Config.Difficulty.ToString());
        Pulse(Color.FromRgb(90, 255, 150), 0.16);   // soft green confirm
        _state.PushEvent($"✔ defused {spec.Payload.DisplayName}");
        CheckComboMilestone();
    }

    private void OnDetonated(EffectBubbleSpec spec)
    {
        if (_state == null || _paused) return;
        FirePayloadForDetonation(spec);      // the threat goes off (ambient mode may soften it)
        _state.EffectsFired++;
        _state.Detonated++;
        _runDetonations++;

        string variant = spec.VariantId;     // payload ctx = variant id (e.g. "braindrain","video")
        string diff = _state.Config.Difficulty.ToString();
        double s = spec.Strength / 100.0;
        int shieldCost = _state.DoubleOrNothingActive ? 2 : 1;
        if (_state.Shields >= shieldCost)
        {
            _state.Shields -= shieldCost;
            _state.Heat = Math.Max(0, _state.Heat - 0.2);
            _state.PushEvent($"🛡 absorbed {spec.Payload.DisplayName}!");
            Pulse(Color.FromRgb(80, 160, 255), 0.28);    // blue shield-save
            Shake(0.25 + s * 0.3, 280);
            // Clutch shield-save branch — distinct trigger so the matcher can praise it.
            App.Bark?.NotifyChaosBubbleDetonatedAbsorbed(variant, spec.Strength, _runDetonations, _state.Combo, diff, _state.Shields);
        }
        else
        {
            int comboBeforeBreak = _state.Combo;   // capture BEFORE zeroing (frozen contract)
            _state.Combo = 0;
            _lastComboBigFired = 0;                // combo broke → reset ComboBig crossing tracking
            _state.Heat = 0;
            _state.PushEvent($"💥 {spec.Payload.DisplayName} detonated!");
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
    private void FirePayloadForDetonation(EffectBubbleSpec spec)
    {
        try
        {
            if (_state?.Config?.AmbientMode == true && IsIntrusivePayload(spec.Payload.Kind))
            {
                EffectPayload soft = _ambientRng.NextDouble() < 0.5
                    ? new BouncingTextPayload()
                    : new GifCascadePayload();
                soft.Strength = spec.Payload.Strength;
                soft.Fire();
                return;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("Chaos ambient remap: {E}", ex.Message); }
        spec.Payload.Fire();
    }

    private static bool IsIntrusivePayload(EffectBubblePayloadKind k) =>
        k == EffectBubblePayloadKind.Video || k == EffectBubblePayloadKind.HtLink;

    private static readonly Random _ambientRng = new();

    private void OnDarterCaught(EffectBubbleSpec spec, bool quick)
    {
        if (_state == null || _paused) return;
        int pts = ChaosBubbleVariants.DARTER_BASE_POINTS + (quick ? ChaosBubbleVariants.DARTER_QUICK_BONUS : 0);
        _state.Score += pts * _state.TotalMult;
        _state.Combo++;
        _state.Heat = Math.Min(1.0, _state.Heat + 0.05);
        App.Achievements?.TrackBubblePopped();
        // The darter is a utility pickup: catching it slows time (no conditioning jolt).
        ActivateSlowMo();
        Pulse(Color.FromRgb(120, 200, 255), quick ? 0.32 : 0.24);   // icy slow-mo flash
        App.Bark?.NotifyChaosDarterCaught(pts, _state.Combo, quick);
        _state.PushEvent(quick ? "⚡ quick catch — time slows!" : "⏳ darter caught — time slows!");
        CheckComboMilestone();
    }

    /// <summary>Catching a Freeze bubble is a GOOD pickup: it freezes the whole field — every bubble
    /// shudders then holds in place, live fuses pause, spawns halt — and plays an icy "freeze" burst
    /// with a held white-blue edge glow and per-bubble blue auras. Refreshes on each catch.</summary>
    private void OnFreezeCaught(EffectBubbleSpec spec)
    {
        if (_state == null || _paused) return;
        _state.Score += FREEZE_BASE_POINTS * _state.TotalMult;
        _state.Combo++;
        _state.Heat = Math.Min(1.0, _state.Heat + 0.05);
        App.Achievements?.TrackBubblePopped();
        ActivateFreeze();
        App.Bark?.NotifyChaosFreezeCaught(FREEZE_BASE_POINTS * _state.TotalMult, _state.Combo);
        _state.PushEvent("❄ frozen — the field holds!");
        CheckComboMilestone();
    }

    // ---- darter slow-mo power-up ----
    private const double SLOWMO_FACTOR = 0.20;        // chaos motion/fuse speed while active (lower = stronger slow)
    private const double SLOWMO_DURATION_SEC = 5.0;   // real-time length of the slow-mo
    private double _slowMoRemainingSec;

    /// <summary>Catching a darter slows the whole field: bubbles drift slower, live fuses last
    /// longer, spawns stretch out, and flash/overlay payloads linger. Refreshes on each catch.</summary>
    private void ActivateSlowMo()
    {
        _slowMoRemainingSec = SLOWMO_DURATION_SEC;
        App.Bubbles?.SetChaosTimeScale(SLOWMO_FACTOR);
        EffectPayload.GlobalDurationMult = 1.0 / SLOWMO_FACTOR;
    }

    private void EndSlowMo()
    {
        _slowMoRemainingSec = 0;
        App.Bubbles?.SetChaosTimeScale(1.0);
        if (_freezeRemainingSec <= 0) EffectPayload.GlobalDurationMult = 1.0;   // don't clobber an active freeze
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
        App.Bubbles?.SetChaosFrozen(true);
        App.Bubbles?.VibrateAllForFreeze(FREEZE_VIBRATE_MS);
        EffectPayload.GlobalDurationMult = FREEZE_DURATION_MULT;
        if (_state?.Config?.ColorFlashesEnabled == true)
        {
            _fx?.FreezeBurst(FREEZE_EDGE);        // icy "ice hit" flash
            _fx?.BeginEdgeHold(FREEZE_EDGE, 0.5); // sustained white-blue edges for the frozen window
        }
        Shake(0.3, FREEZE_VIBRATE_MS);
    }

    private void EndFreeze()
    {
        _freezeRemainingSec = 0;
        App.Bubbles?.SetChaosFrozen(false);
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
                _state.PushEvent($"🔥🔥 COMBO x{combo}!");
                App.Bark?.NotifyChaosComboBig(combo, t);
                Pulse(COMBO_BIG_COLOR, COMBO_BIG_PULSE);   // distinct bigger beat
            }
        }

        if (combo % 10 == 0)
        {
            _state.PushEvent($"🔥 combo x{combo}!");
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
        }
    }

    /// <summary>Near-miss danger telegraph: in the last <see cref="NEAR_MISS_TELEGRAPH_MS"/> before the
    /// soonest live bubble detonates, fire an escalating red edge-flash + audio tick. Additive feel only —
    /// it polls a read-only fuse accessor and never changes the fuse/score. No-op when frozen/paused.</summary>
    private void UpdateNearMissTelegraph(double dt)
    {
        if (_state?.Config?.ColorFlashesEnabled != true) { _nearMissTickAccumMs = 0; return; }
        if (_freezeRemainingSec > 0) { _nearMissTickAccumMs = 0; return; }   // time frozen: no impending detonation

        double? minFuse = App.Bubbles?.MinLiveFuseRemainingMs();
        if (minFuse == null || minFuse.Value > NEAR_MISS_TELEGRAPH_MS) { _nearMissTickAccumMs = 0; return; }

        _nearMissTickAccumMs += dt * 1000.0;
        if (_nearMissTickAccumMs < NEAR_MISS_TICK_MS) return;
        _nearMissTickAccumMs = 0;

        // The closer to zero, the stronger the flash (0..1 urgency above the window).
        double urgency = 1.0 - Math.Clamp(minFuse.Value / NEAR_MISS_TELEGRAPH_MS, 0, 1);
        Pulse(NEAR_MISS_COLOR, NEAR_MISS_PULSE + urgency * 0.22);
        ChaosSfx.PlayNearMissTick();
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
        EndSlowMo(); EndFreeze();
        try { ChaosFlashOverlay.CloseActive(); } catch { }
        try { ChaosGifCascadeOverlay.CloseActive(); } catch { }
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
        _spawning = false;
        _runTimer?.Stop();
        _spawnTimer?.Stop();
        App.Bubbles?.EndChaosMode();
        EndSlowMo(); EndFreeze();
        try { ChaosFlashOverlay.CloseActive(); } catch { }
        try { ChaosGifCascadeOverlay.CloseActive(); } catch { }
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
        try { ChaosMeta.AwardRunRewards(_state); }
        catch (Exception ex) { App.Logger?.Debug("Chaos meta award: {E}", ex.Message); }

        string diff = _state.Config.Difficulty.ToString();
        App.Bark?.NotifyChaosRunCompleted((int)finalXp, diff);

        _hud?.Close();
        _hud = null;
        _overlay?.ShowResults(_state, baseXp, skillMult, finalXp, previousBest);

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
