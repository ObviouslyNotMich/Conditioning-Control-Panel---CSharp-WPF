using System;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Services;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Avalonia implementation of the skill-tree service.
/// Manages unlocked skills, XP multipliers, skill purchases, streak shields,
/// oopsie insurance, daily streak bonuses, and free rerolls from local settings.
/// Does not port the legacy WPF Pink Rush / Lucky Proc runtime effects.
/// </summary>
public sealed class AvaloniaSkillTreeService : ISkillTreeService, IDisposable
{
    private readonly ISettingsService _settingsService;

    public AvaloniaSkillTreeService(ISettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    /// <inheritdoc />
    public event EventHandler<string>? SkillUnlocked;

    /// <inheritdoc />
    public bool HasSkill(string skillId)
    {
        return _settingsService.Current?.UnlockedSkills.Contains(skillId) == true;
    }

    /// <inheritdoc />
    public double GetTotalXpMultiplier()
    {
        var unlocked = _settingsService.Current?.UnlockedSkills;
        if (unlocked == null) return 1.0;

        double multiplier = 1.0;
        foreach (var skill in SkillDefinition.All)
        {
            if (unlocked.Contains(skill.Id) && skill.EffectType == SkillEffectType.XpMultiplier)
            {
                multiplier += skill.EffectValue;
            }
        }

        return multiplier;
    }

    /// <inheritdoc />
    public int TotalPointsSpent
    {
        get
        {
            var unlocked = _settingsService.Current?.UnlockedSkills;
            if (unlocked == null) return 0;

            return SkillDefinition.All
                .Where(s => unlocked.Contains(s.Id))
                .Sum(s => s.Cost);
        }
    }

    /// <inheritdoc />
    public Task<(bool Success, string? Error)> PurchaseSkillAsync(string skillId)
    {
        var settings = _settingsService.Current;
        if (settings == null)
            return Task.FromResult<(bool, string?)>((false, "Settings are not available."));

        var skill = SkillDefinition.All.FirstOrDefault(s => s.Id == skillId);
        if (skill == null)
            return Task.FromResult<(bool, string?)>((false, "Unknown skill."));

        if (settings.UnlockedSkills.Contains(skillId))
            return Task.FromResult<(bool, string?)>((false, "Skill already unlocked."));

        if (!string.IsNullOrEmpty(skill.PrerequisiteId) && !settings.UnlockedSkills.Contains(skill.PrerequisiteId))
            return Task.FromResult<(bool, string?)>((false, "Prerequisite skill is not unlocked."));

        if (settings.SkillPoints < skill.Cost)
            return Task.FromResult<(bool, string?)>((false, "Not enough skill points."));

        if (skill.IsSecret && !IsSecretSkillAvailable(skillId, settings))
            return Task.FromResult<(bool, string?)>((false, "Secret skill requirement is not met."));

        settings.SkillPoints -= skill.Cost;
        settings.UnlockedSkills.Add(skillId);

        ApplySkillEffects(skillId, settings);
        _settingsService.Save();

        SkillUnlocked?.Invoke(this, skillId);
        return Task.FromResult<(bool, string?)>((true, null));
    }

    private static void ApplySkillEffects(string skillId, AppSettings settings)
    {
        switch (skillId)
        {
            case "good_girl_streak":
                settings.StreakShieldsRemaining = 1;
                settings.LastStreakShieldResetDate = DateTime.UtcNow.Date;
                break;
        }
    }

    private static bool IsSecretSkillAvailable(string skillId, AppSettings settings)
    {
        return skillId switch
        {
            "night_shift" => settings.NightTimeUsageCount >= 10,
            "early_bird_bimbo" => settings.EarlyMorningUsageCount >= 10,
            "eternal_doll" => settings.HighestLevelEver >= 50,
            _ => false,
        };
    }

    /// <inheritdoc />
    public void Start()
    {
        var settings = _settingsService.Current;
        if (settings == null) return;

        // Reset weekly streak shields if 7+ days since last reset.
        if (HasSkill("good_girl_streak"))
        {
            var daysSinceReset = (DateTime.UtcNow.Date - (settings.LastStreakShieldResetDate ?? DateTime.MinValue)).TotalDays;
            if (daysSinceReset >= 7)
            {
                ResetWeeklyShields();
            }
        }

        // Track time-of-day usage for secret skill unlocks.
        TrackTimeOfDayUsage();
    }

    /// <inheritdoc />
    public void Stop()
    {
        // No background timers to stop in this port.
    }

    public void Dispose() => Stop();

    #region Legacy Core stubs

    /// <inheritdoc />
    public bool UseStreakShield()
    {
        var settings = _settingsService.Current;
        if (settings == null) return false;
        if (!HasSkill("good_girl_streak")) return false;
        if (settings.StreakShieldsRemaining <= 0) return false;

        settings.StreakShieldsRemaining--;
        _settingsService.Save();
        return true;
    }

    private void ResetWeeklyShields()
    {
        var settings = _settingsService.Current;
        if (settings == null) return;
        if (!HasSkill("good_girl_streak")) return;

        settings.StreakShieldsRemaining = 1;
        settings.LastStreakShieldResetDate = DateTime.UtcNow.Date;
        _settingsService.Save();
    }

    /// <inheritdoc />
    public bool UseOopsieInsurance()
    {
        var settings = _settingsService.Current;
        if (settings == null) return false;
        if (!HasSkill("oopsie_insurance")) return false;
        if (settings.SeasonalStreakRecoveryUsed) return false;
        if (settings.PlayerXP < 500) return false;

        settings.PlayerXP -= 500;
        settings.SeasonalStreakRecoveryUsed = true;
        _settingsService.Save();
        return true;
    }

    /// <inheritdoc />
    public int GetDailyStreakBonus(int consecutiveDays)
    {
        if (!HasSkill("milestone_rewards")) return 0;
        if (consecutiveDays <= 0) return 0;

        var baseXp = consecutiveDays switch
        {
            <= 3 => 50,
            <= 6 => 100,
            <= 13 => 150,
            <= 29 => 200,
            _ => 300
        };

        var level = _settingsService.Current?.PlayerLevel ?? 1;
        var levelMultiplier = 1.0 + (level - 1) * 0.03;
        return (int)Math.Round(baseXp * levelMultiplier);
    }

    /// <inheritdoc />
    public int GetDailyFreeRerolls()
    {
        int total = 0;
        if (HasSkill("quest_refresh")) total += 1;
        if (HasSkill("reroll_addict")) total += 2;
        return total;
    }

    /// <inheritdoc />
    public void AddConditioningTime(double minutes)
    {
        var settings = _settingsService.Current;
        if (settings == null || minutes <= 0) return;

        settings.TotalConditioningMinutes += minutes;
        settings.SeasonConditioningMinutes += minutes;
        _settingsService.Save();
    }

    private void TrackTimeOfDayUsage()
    {
        var settings = _settingsService.Current;
        if (settings == null) return;

        var hour = DateTime.Now.Hour;
        if (hour >= 23 || hour < 5)
            settings.NightTimeUsageCount++;
        if (hour >= 5 && hour < 8)
            settings.EarlyMorningUsageCount++;

        _settingsService.Save();
    }

    #endregion
}
