using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// Public surface for the ambient bubble popping service.
/// Stage 1 covers ambient clickable bubbles; Chaos mode hooks are stubbed.
/// </summary>
public interface IBubbleService
{
    /// <summary>Raised when a bubble is popped by the player.</summary>
    event Action? OnBubblePopped;

    /// <summary>Raised when a bubble floats off screen without being popped.</summary>
    event Action? OnBubbleMissed;

    /// <summary>True when the ambient bubble loop is active.</summary>
    bool IsRunning { get; }

    /// <summary>True when the service is paused and cleared.</summary>
    bool IsPaused { get; }

    /// <summary>Number of bubbles currently alive.</summary>
    int ActiveBubbles { get; }

    /// <summary>Starts the ambient spawn loop.</summary>
    void Start();

    /// <summary>Stops the spawn loop and removes all bubbles.</summary>
    void Stop();

    /// <summary>Clears the field and pauses spawning without tearing down timers.</summary>
    void PauseAndClear();

    /// <summary>Resumes from a paused state.</summary>
    void Resume();

    /// <summary>Re-reads settings and restarts the spawn timer.</summary>
    void RefreshFrequency();

    /// <summary>Spawns a single ambient bubble immediately if the service is running.</summary>
    void SpawnOnce();

    /// <summary>Pops every currently alive ambient bubble.</summary>
    void PopAllBubbles();

    // ---- Chaos-mode stubs required by IAvaloniaBubbleService ----

    /// <summary>Tail-Plug trail seconds; Stage 1 always returns 0.</summary>
    double ChaosRabbitTrailSecNow { get; }

    /// <summary>Sets the Tail-Plug trail duration for the current chaos run.</summary>
    void SetRabbitTrailSec(double seconds);

    /// <summary>Pops chaos bubbles intersecting the given DIP rectangle; Stage 1 is a no-op.</summary>
    void PopBubblesInRect(PixelRect rectDips);

    /// <summary>True if any darter intersects the rectangle; Stage 1 always returns false.</summary>
    bool AnyDarterIntersects(PixelRect rectDips);

    // ---- Stage 2a chaos mode hooks ----

    /// <summary>
    /// Enters chaos mode and wires callbacks for benign pops, defuses, detonations,
    /// and (Avalonia port) hold-to-defuse channel start/broken events.
    /// </summary>
    void BeginChaosMode(
        Action<ChaosBubbleSpec> onBenignPop,
        Action<ChaosBubbleSpec, double, bool> onDefuse,
        Action<ChaosBubbleSpec> onDetonate,
        Func<ChaosBubbleSpec, bool>? canChannel = null,
        Action<ChaosBubbleSpec>? onChannelStarted = null,
        Action<ChaosBubbleSpec, string>? onChannelBroken = null);

    /// <summary>Leaves chaos mode and destroys all chaos bubbles.</summary>
    void EndChaosMode();

    /// <summary>Queues a chaos bubble for materialization.</summary>
    void SpawnChaosBubble(ChaosBubbleSpec spec);

    /// <summary>Pauses or resumes chaos bubble physics.</summary>
    void SetChaosFrozen(bool frozen);

    /// <summary>Adjusts the chaos simulation speed multiplier.</summary>
    void SetChaosTimeScale(double scale);

    /// <summary>Locks or unlocks chaos input handling.</summary>
    void SetChaosInputLocked(bool locked);

    // ---- Active-toy APIs (Avalonia parity) ----

    /// <summary>Enables or disables the VibePopping sweep mode. While active, left-clicks within
    /// the sweep radius pop nearby chaos bubbles instantly (live bubbles snap for full pay).</summary>
    void SetVibePop(bool active, bool hoverPops = false);

    /// <summary>Briefly vibrates all chaos bubble windows to telegraph a freeze.</summary>
    void VibrateAllForFreeze(int durationMs);

    /// <summary>Instantly defuses every live chaos bubble currently on screen.</summary>
    void DefuseAllLive();

    /// <summary>Pops every paid chaos bubble (treats + lives) currently on screen.</summary>
    void PopAllChaosPaid();

    /// <summary>Arms the E-Stim effect for the next N bubble clicks/detonations.</summary>
    void ArmEStim(int charges, bool chainReaction = false);

    /// <summary>Remaining E-Stim charges; 0 when unarmed.</summary>
    int EStimChargesLeft { get; }

    /// <summary>Casts a player ripple wave from the given physical-pixel centre.</summary>
    void TriggerPlayerRipple(Point centerPx, double radiusPx, double lifeMs);
}
