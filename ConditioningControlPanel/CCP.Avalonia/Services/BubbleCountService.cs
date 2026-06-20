namespace ConditioningControlPanel.Avalonia.Services;

/// <summary>
/// Temporary Avalonia stub for the legacy WPF BubbleCountService.
/// The full bubble-count game engine (difficulty scaling, spawn rate math, etc.)
/// has not been extracted to CCP.Core yet.
/// </summary>
public static class BubbleCountService
{
    public enum Difficulty
    {
        Easy,
        Medium,
        Hard
    }

    /// <summary>
    /// Scales base XP by the last known video duration.
    /// TODO: wire real duration scaling once the game engine is ported.
    /// </summary>
    public static int ScaleXpByDuration(int baseXp)
    {
        // Legacy formula: baseXP scaled by (duration / 30s). Stubbed at 1x.
        return baseXp;
    }
}
