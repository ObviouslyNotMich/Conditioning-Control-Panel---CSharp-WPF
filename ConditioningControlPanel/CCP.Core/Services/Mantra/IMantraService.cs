namespace ConditioningControlPanel.Core.Services.Mantra;

/// <summary>
/// Cross-platform seam for the full-screen mantra typing lab.
/// Tracks a session of repeated mantras, enforces anti-cheat timing limits,
/// awards XP/quest progress, and surfaces per-mantra/session completion events.
/// </summary>
public interface IMantraService
{
    string? CurrentMantra { get; }
    int Streak { get; }
    int BestStreak { get; }
    int Completions { get; }
    int TargetCount { get; }
    bool IsActive { get; }

    event Action<int>? StreakChanged;
    event Action? StreakBroken;
    event Action? MantraCompleted;
    event Action<int, int>? SessionComplete; // totalReps, bestStreak

    void StartSession(int targetReps);
    bool TryCompleteMantra();
    void BreakStreak();
    void EndSession();
}
