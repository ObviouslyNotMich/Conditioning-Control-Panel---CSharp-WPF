using System;
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

    public void ToggleManualPause()
    {
        if (!_spawning || _paused) return;
        _manualPaused = !_manualPaused;
        if (_manualPaused) _spawnTimer?.Stop();
        else _spawnTimer?.Start();
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

        App.Bubbles?.BeginChaosMode(OnBenignPopped, OnDefused, OnDetonated, OnDarterCaught);
        App.Bark?.NotifyChaosRunStarted(_state.Config.Difficulty.ToString());
        _state.PushEvent("⚡ run started");
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

        if (elapsed >= _state.RunDurationSec) { EndRun(); return; }

        double waveLen = (double)_state.RunDurationSec / _state.WaveCount;
        int newWave = Math.Min(_state.WaveCount, 1 + (int)(elapsed / waveLen));
        _state.WaveProgress = (elapsed % waveLen) / waveLen;

        if (newWave > _state.WaveIndex) BeginWaveTransition(newWave);
    }

    private void SpawnTick(object? sender, EventArgs e)
    {
        if (!_spawning || _state == null || _paused || _manualPaused) return;

        var cfg = _state.Config;
        double intensity = _state.RunIntensity;
        double effIntensity = Math.Clamp(intensity + (cfg.DifficultyMult - 1.0) * 0.15, 0, 1);
        double diffFactor = cfg.DifficultyMult;

        int maxConcurrent = (int)Math.Round((4 + intensity * 7) * Math.Sqrt(diffFactor)) + cfg.MaxBubblesBonus;
        if ((App.Bubbles?.ActiveBubbles ?? 0) < maxConcurrent)
        {
            var spec = ChaosBubbleVariants.Pick(effIntensity, _state.FuseTimeMult,
                cfg.MotionOverride, cfg.EnabledVariants, cfg.EffectIntensity);
            App.Bubbles?.SpawnChaosBubble(spec);
        }

        // Darters spawn on their own intensity-scaled roll, independent of the bubble cap.
        if (cfg.DartersEnabled)
        {
            var darter = ChaosBubbleVariants.RollDarter(effIntensity);
            if (darter != null) App.Bubbles?.SpawnChaosBubble(darter);
        }

        double interval = (1300 - intensity * 850) / diffFactor;
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
        // Clear the field so live fuses don't tick (and detonate) behind the draft overlay.
        App.Bubbles?.PopAllBubbles();

        // Clear the finished wave's transient (next-wave) flags before drafting.
        _state.AllLiveNextWave = false;
        _state.DoubleOrNothingActive = false;
        _state.NextWavePayoutMult = 1.0;

        _pendingWave = newWave;
        App.Bark?.NotifyChaosWaveEscalated(newWave);

        var options = ChaosBoonPool.Draft(_state.Config.AllowCurses, _state.Config.DraftChoices);
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
        // The darter IS the micro-conditioning: fire its brief low-strength flash on catch.
        spec.Payload.Fire();
        _state.EffectsFired++;
        Pulse(Color.FromRgb(255, 90, 200), quick ? 0.22 : 0.14);   // flash-pink confirm
        _state.PushEvent(quick ? "⚡ quick catch!" : "✦ darter caught");
        CheckComboMilestone();
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
