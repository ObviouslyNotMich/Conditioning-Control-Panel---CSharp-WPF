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

    public void StartRun(ChaosRunConfig? config = null)
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
            _overlay.ShowCountdown(BeginRun);
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
            return;
        }

        _paused = true;
        _spawnTimer?.Stop();
        // Clear the field so live fuses don't tick (and detonate) behind the draft overlay,
        // with a single burst cue as everything pops.
        App.Bubbles?.PopAllBubbles();
        ChaosSfx.PlayWaveClear();

        // Clear the finished wave's transient (next-wave) flags before drafting.
        _state.AllLiveNextWave = false;
        _state.DoubleOrNothingActive = false;
        _state.NextWavePayoutMult = 1.0;

        _pendingWave = newWave;
        App.Bark?.NotifyChaosWaveEscalated(newWave);

        var options = ChaosBoonPool.Draft(_state.Config.AllowCurses, _state.Config.DraftChoices);
        foreach (var o in options) ChaosMeta.MarkDiscovered("boon:" + o.Id);
        _overlay?.ShowBoonDraft(_state.WaveIndex, options, OnBoonChosen);
    }

    private void OnBoonChosen(ChaosBoon? boon)
    {
        if (_state == null) return;

        if (boon != null)
        {
            _state.ApplyBoon(boon);
            App.Bark?.NotifyChaosBoonPicked(boon.Name);
        }
        else
        {
            _state.Shields += 1;
            _state.PushEvent("⛊ skipped → +1 shield");
        }

        if (_state.DoubleOrNothingArmed)
        {
            _state.DoubleOrNothingActive = true;
            _state.DoubleOrNothingArmed = false;
        }

        _state.WaveIndex = _pendingWave;
        _state.ActIndex = 1 + (_state.WaveIndex - 1) / 5;

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
        App.Bark?.NotifyChaosBubbleDefused();
        Pulse(Color.FromRgb(90, 255, 150), 0.16);   // soft green confirm
        _state.PushEvent($"✔ defused {spec.Payload.DisplayName}");
        CheckComboMilestone();
    }

    private void OnDetonated(EffectBubbleSpec spec)
    {
        if (_state == null || _paused) return;
        spec.Payload.Fire();                 // the threat goes off
        _state.EffectsFired++;
        _state.Detonated++;

        double s = spec.Strength / 100.0;
        int shieldCost = _state.DoubleOrNothingActive ? 2 : 1;
        if (_state.Shields >= shieldCost)
        {
            _state.Shields -= shieldCost;
            _state.Heat = Math.Max(0, _state.Heat - 0.2);
            _state.PushEvent($"🛡 absorbed {spec.Payload.DisplayName}!");
            Pulse(Color.FromRgb(80, 160, 255), 0.28);    // blue shield-save
            Shake(0.25 + s * 0.3, 280);
        }
        else
        {
            _state.Combo = 0;
            _state.Heat = 0;
            _state.PushEvent($"💥 {spec.Payload.DisplayName} detonated!");
            Pulse(Color.FromRgb(255, 50, 50), 0.4 + s * 0.35);   // red malus
            Shake(0.4 + s * 0.5, 380);                           // the malus jolt
        }
        App.Bark?.NotifyChaosBubbleDetonated(spec.Payload.DisplayName);
    }

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
        if (_state.Combo > 0 && _state.Combo % 10 == 0)
        {
            _state.PushEvent($"🔥 combo x{_state.Combo}!");
            App.Bark?.NotifyChaosComboMilestone(_state.Combo);
            Pulse(Color.FromRgb(255, 200, 60), 0.4);   // gold combo flash
        }
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
        try { _fx?.Close(); } catch { }
        _fx = null;

        double durMin = Math.Max(1, _state.RunDurationSec) / 60.0;
        double capBase = 250.0 * durMin * _state.Config.DifficultyMult;
        double baseXp = Math.Min(_state.Score, capBase);
        double skillMult = _state.SkillMult;
        double finalXp = baseXp * skillMult;

        try { App.Progression?.AddXP(baseXp, XPSource.Bubble); }
        catch (Exception ex) { App.Logger?.Debug("Chaos payout AddXP: {E}", ex.Message); }

        // Meta-progression: bank Sparks + update lifetime stats (separate from XP payout).
        try { ChaosMeta.AwardRunRewards(_state); }
        catch (Exception ex) { App.Logger?.Debug("Chaos meta award: {E}", ex.Message); }

        App.Bark?.NotifyChaosRunCompleted((int)finalXp);

        _hud?.Close();
        _hud = null;
        _overlay?.ShowResults(_state, baseXp, skillMult, finalXp);

        App.Bubbles?.Resume();
        App.Logger?.Information("Chaos run complete: base {Base:0} x skill {Mult:0.0} = {Final:0} XP (defused {D}, detonated {Det})",
            baseXp, skillMult, finalXp, _state.Defused, _state.Detonated);
    }

    private void RunAgain()
    {
        // Tear down the current run windows, then start a fresh one.
        _overlay?.Close();   // triggers OnOverlayClosed → CleanupAfterRun
        Application.Current?.Dispatcher.BeginInvoke(new Action(() => StartRun()));
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
