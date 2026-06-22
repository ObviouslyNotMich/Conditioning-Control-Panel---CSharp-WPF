using System;
using ConditioningControlPanel.Avalonia.Windows;

namespace ConditioningControlPanel.Avalonia.Services;

/// <summary>
/// Avalonia helper for bubble-count XP scaling.
/// Mirrors the legacy WPF BubbleCountService duration scaling.
/// </summary>
public static class BubbleCountService
{
    public enum Difficulty
    {
        Easy,
        Medium,
        Hard
    }

    /// <summary>Minimum video duration (seconds) for full XP. Shorter videos scale proportionally.</summary>
    private const double FullXpVideoDurationSeconds = 60.0;

    /// <summary>
    /// Scales base XP by the last known video duration.
    /// </summary>
    public static int ScaleXpByDuration(int baseXp)
    {
        var duration = BubbleCountWindow.LastVideoDurationSeconds;
        if (duration >= FullXpVideoDurationSeconds) return baseXp;

        var scale = Math.Max(0.1, duration / FullXpVideoDurationSeconds);
        return Math.Max(1, (int)(baseXp * scale));
    }
}
