using System;

namespace ConditioningControlPanel.Core.Services.Moderation;

/// <summary>
/// Sliding-window moderation hit counter with escalation, persisted across launches.
/// </summary>
public interface IModerationCounter
{
    /// <summary>Records a prohibited-content hit from the given source.</summary>
    void RecordHit(ProhibitedCategory category, string source);

    /// <summary>Returns the current counter state (prunes expired hits).</summary>
    ModerationCounterState GetState();

    /// <summary>Hydrates in-memory state from disk. Safe to call once at startup.</summary>
    void LoadFromDisk();

    /// <summary>Raised when the warning threshold is crossed.</summary>
    event Action<ModerationCounterState>? WarningTriggered;

    /// <summary>Raised when a cooldown begins.</summary>
    event Action<DateTime>? CooldownStarted;

    /// <summary>Raised when a cooldown ends naturally.</summary>
    event Action? CooldownEnded;
}

/// <summary>Snapshot of the moderation counter state.</summary>
public sealed record ModerationCounterState(
    int HitsInLastTenMinutes,
    bool WarningTriggered,
    bool CooldownActive,
    DateTime? CooldownEndsAt);
