using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Avalonia progression service that persists XP, levels, and skill points through
/// <see cref="ISettingsService"/> and applies the skill-tree XP multiplier.
/// Mirrors the legacy WPF level curve and session multiplier.
/// </summary>
public sealed class AvaloniaProgressionService : IProgressionService
{
    private readonly ISettingsService _settingsService;
    private readonly ISkillTreeService _skillTreeService;
    private readonly Dictionary<int, double> _cumulativeXPCache = new();

    public AvaloniaProgressionService(ISettingsService settingsService, ISkillTreeService skillTreeService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _skillTreeService = skillTreeService ?? throw new ArgumentNullException(nameof(skillTreeService));
    }

    /// <inheritdoc />
    public event EventHandler<int>? LevelUp;

    /// <inheritdoc />
    public void AddXP(int amount, XPSource source)
    {
        if (amount <= 0) return;

        var settings = _settingsService.Current;
        if (settings == null) return;

        double multiplier = _skillTreeService.GetTotalXpMultiplier();
        double adjusted = amount * multiplier;
        settings.PlayerXP += adjusted;

        double xpNeeded = GetXPForLevel(settings.PlayerLevel);
        while (settings.PlayerXP >= xpNeeded)
        {
            settings.PlayerXP -= xpNeeded;
            settings.PlayerLevel++;
            settings.SkillPoints += 5;

            if (settings.PlayerLevel > settings.HighestLevelEver)
                settings.HighestLevelEver = settings.PlayerLevel;

            LevelUp?.Invoke(this, settings.PlayerLevel);
            xpNeeded = GetXPForLevel(settings.PlayerLevel);
        }

        _settingsService.Save();
    }

    /// <inheritdoc />
    public double GetSessionXPMultiplier(int playerLevel)
    {
        if (playerLevel < 30) return 1.0;
        if (playerLevel < 80) return 1.0 + ((playerLevel - 30) * 0.01);   // 1.0x → 1.5x
        if (playerLevel < 125) return 1.5 + ((playerLevel - 80) * 0.02);  // 1.5x → 2.4x
        if (playerLevel < 150) return 2.4 + ((playerLevel - 125) * 0.03); // 2.4x → 3.15x
        return Math.Min(5.0, 3.15 + ((playerLevel - 150) * 0.03));         // 3.15x → 5.0x cap
    }

    /// <inheritdoc />
    public double GetXPForLevel(int level)
    {
        if (level <= 0) return 100.0;

        if (level <= 80)
        {
            // Linear growth from 800 to 2500.
            return Math.Round(800 + (level - 1) * (1700.0 / 79));
        }
        else if (level <= 100)
        {
            // Linear growth from 2500 to 4000.
            return Math.Round(2500 + (level - 80) * (1500.0 / 20));
        }
        else if (level <= 125)
        {
            // Linear growth from 4000 to 6000.
            return Math.Round(4000 + (level - 100) * (2000.0 / 25));
        }
        else if (level <= 150)
        {
            // Linear growth from 6000 to 10000.
            return Math.Round(6000 + (level - 125) * (4000.0 / 25));
        }
        else
        {
            // 3% compound growth per level beyond 150.
            return Math.Round(10000 * Math.Pow(1.03, level - 150));
        }
    }

    /// <inheritdoc />
    public double GetTotalXP(int level, double currentXP)
    {
        return GetCumulativeXPForLevel(level - 1) + currentXP;
    }

    /// <inheritdoc />
    public double GetCurrentLevelXP(int level, double totalXP)
    {
        var cumulativeForPreviousLevels = GetCumulativeXPForLevel(level - 1);
        return Math.Max(0, totalXP - cumulativeForPreviousLevels);
    }

    /// <summary>
    /// Gets the cumulative XP required to reach a given level (sum of all previous levels).
    /// Results are memoized for performance.
    /// </summary>
    private double GetCumulativeXPForLevel(int level)
    {
        if (level <= 0) return 0;

        if (_cumulativeXPCache.TryGetValue(level, out double cached))
            return cached;

        double cumulative = GetCumulativeXPForLevel(level - 1) + GetXPForLevel(level);
        _cumulativeXPCache[level] = cumulative;
        return cumulative;
    }
}
