using System;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Avalonia implementation of the skill-tree service.
/// Manages unlocked skills, XP multipliers, and skill purchases from local settings.
/// Does not port the legacy WPF Pink Rush / Lucky Proc runtime effects.
/// </summary>
public sealed class AvaloniaSkillTreeService : ISkillTreeService
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
        _settingsService.Save();

        SkillUnlocked?.Invoke(this, skillId);
        return Task.FromResult<(bool, string?)>((true, null));
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

    #region Legacy Core stubs

    /// <inheritdoc />
    public bool UseStreakShield() => false;

    /// <inheritdoc />
    public bool UseOopsieInsurance() => false;

    /// <inheritdoc />
    public int GetDailyStreakBonus(int consecutiveDays) => 0;

    /// <inheritdoc />
    public int GetDailyFreeRerolls() => 0;

    /// <inheritdoc />
    public void AddConditioningTime(double minutes)
    {
        var settings = _settingsService.Current;
        if (settings == null || minutes <= 0) return;

        settings.TotalConditioningMinutes += minutes;
        _settingsService.Save();
    }

    #endregion
}
