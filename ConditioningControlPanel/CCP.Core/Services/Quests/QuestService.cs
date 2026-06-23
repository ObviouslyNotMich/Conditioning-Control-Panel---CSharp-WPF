using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Core.Services.Quests;

/// <summary>
/// Cross-platform quest service: generates daily/weekly quests, tracks progress,
/// awards XP, and persists state to the user data folder.
/// </summary>
public sealed class QuestService : IQuestService
{
    private readonly ISettingsService _settingsService;
    private readonly ISkillTreeService _skillTreeService;
    private readonly IAppEnvironment _appEnvironment;
    private readonly ILogger<QuestService>? _logger;
    private readonly string _progressPath;
    private readonly Random _random = new();

    /// <inheritdoc />
    public QuestProgress Progress { get; private set; }

    /// <inheritdoc />
    public event EventHandler? QuestsChanged;

    public QuestService(
        ISettingsService settingsService,
        ISkillTreeService skillTreeService,
        IAppEnvironment appEnvironment,
        ILogger<QuestService>? logger = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _skillTreeService = skillTreeService ?? throw new ArgumentNullException(nameof(skillTreeService));
        _appEnvironment = appEnvironment ?? throw new ArgumentNullException(nameof(appEnvironment));
        _logger = logger;

        _progressPath = Path.Combine(appEnvironment.UserDataPath, "quests.json");
        Progress = LoadProgress();
    }

    #region Persistence

    private QuestProgress LoadProgress()
    {
        var tmpPath = _progressPath + ".tmp";

        if (File.Exists(_progressPath))
        {
            try
            {
                var json = File.ReadAllText(_progressPath);
                var progress = JsonConvert.DeserializeObject<QuestProgress>(json);
                if (progress != null)
                {
                    return progress;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load quest progress from {Path}", _progressPath);
            }
        }

        // Recover from an atomic-write temp file if the main file is missing or corrupt.
        if (File.Exists(tmpPath))
        {
            try
            {
                var json = File.ReadAllText(tmpPath);
                var progress = JsonConvert.DeserializeObject<QuestProgress>(json);
                if (progress != null)
                {
                    _logger?.LogWarning("Recovered quest progress from temp file {Path}", tmpPath);
                    try { File.Move(tmpPath, _progressPath, overwrite: true); } catch { }
                    return progress;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to recover quest progress from {Path}", tmpPath);
            }
        }

        return new QuestProgress();
    }

    private void SaveProgress()
    {
        try
        {
            var directory = Path.GetDirectoryName(_progressPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonConvert.SerializeObject(Progress, Formatting.Indented);
            var tmpPath = _progressPath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _progressPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save quest progress to {Path}", _progressPath);
        }
    }

    private void OnQuestsChanged()
    {
        SaveProgress();
        QuestsChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Generation

    /// <inheritdoc />
    public void EnsureGenerated()
    {
        bool changed = false;

        if ((Progress.DailyQuest == null || Progress.IsDailyExpired()) && !Progress.AreAllDailyQuestsCompleted())
        {
            GenerateDailyQuest();
            changed = true;
        }

        if (Progress.WeeklyQuest == null || Progress.IsWeeklyExpired())
        {
            GenerateWeeklyQuest();
            changed = true;
        }

        if (changed)
        {
            OnQuestsChanged();
        }
    }

    private void GenerateDailyQuest()
    {
        var pool = QuestDefinition.DailyQuests;
        if (pool.Count == 0) return;

        var excludeId = Progress.DailyQuest?.DefinitionId;
        var available = pool.Where(q => q.Id != excludeId).ToList();
        if (available.Count == 0)
        {
            available = pool.ToList();
        }

        var selected = available[_random.Next(available.Count)];
        Progress.DailyQuest = new ActiveQuest(selected.Id);
        Progress.DailyQuestGeneratedAt = DateTime.Today;
        _logger?.LogInformation("Generated new daily quest: {QuestId}", selected.Id);
    }

    private void GenerateWeeklyQuest()
    {
        var pool = QuestDefinition.WeeklyQuests;
        if (pool.Count == 0) return;

        var excludeId = Progress.WeeklyQuest?.DefinitionId;
        var available = pool.Where(q => q.Id != excludeId).ToList();
        if (available.Count == 0)
        {
            available = pool.ToList();
        }

        var selected = available[_random.Next(available.Count)];
        Progress.WeeklyQuest = new ActiveQuest(selected.Id);
        Progress.WeeklyQuestGeneratedAt = DateTime.Today;
        _logger?.LogInformation("Generated new weekly quest: {QuestId}", selected.Id);
    }

    #endregion

    #region Rerolls

    private bool HasPremiumAccess => _settingsService.Current?.HasCachedPremiumAccess == true;

    /// <inheritdoc />
    public int GetRemainingDailyRerolls()
    {
        if (Progress.DailyRerollResetDate?.Date != DateTime.Today)
        {
            Progress.DailyRerollsUsed = 0;
            Progress.DailyRerollResetDate = DateTime.Today;
        }

        int maxRerolls = HasPremiumAccess ? 3 : 1;
        maxRerolls += _skillTreeService.GetDailyFreeRerolls();
        maxRerolls += _settingsService.Current?.BonusDailyRerolls ?? 0;
        return Math.Max(0, maxRerolls - Progress.DailyRerollsUsed);
    }

    /// <inheritdoc />
    public int GetRemainingWeeklyRerolls()
    {
        var startOfWeek = GetStartOfWeek(DateTime.Today);

        if (!Progress.WeeklyRerollResetDate.HasValue || Progress.WeeklyRerollResetDate.Value.Date < startOfWeek)
        {
            Progress.WeeklyRerollsUsed = 0;
            Progress.WeeklyRerollResetDate = DateTime.Today;
        }

        int maxRerolls = HasPremiumAccess ? 3 : 1;
        maxRerolls += _skillTreeService.GetDailyFreeRerolls();
        maxRerolls += _settingsService.Current?.BonusWeeklyRerolls ?? 0;
        return Math.Max(0, maxRerolls - Progress.WeeklyRerollsUsed);
    }

    private static DateTime GetStartOfWeek(DateTime date)
    {
        int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-diff).Date;
    }

    /// <inheritdoc />
    public bool RerollDaily()
    {
        if (GetRemainingDailyRerolls() <= 0)
        {
            _logger?.LogDebug("No daily rerolls remaining");
            return false;
        }

        if (Progress.DailyQuest?.IsCompleted == true)
        {
            _logger?.LogDebug("Cannot reroll completed daily quest");
            return false;
        }

        var oldId = Progress.DailyQuest?.DefinitionId;
        GenerateDailyQuest();
        Progress.DailyRerollsUsed++;
        _logger?.LogInformation("Daily quest rerolled from {OldId} to {NewId}", oldId, Progress.DailyQuest?.DefinitionId);
        OnQuestsChanged();
        return true;
    }

    /// <inheritdoc />
    public bool RerollWeekly()
    {
        if (GetRemainingWeeklyRerolls() <= 0)
        {
            _logger?.LogDebug("No weekly rerolls remaining");
            return false;
        }

        if (Progress.WeeklyQuest?.IsCompleted == true)
        {
            _logger?.LogDebug("Cannot reroll completed weekly quest");
            return false;
        }

        var oldId = Progress.WeeklyQuest?.DefinitionId;
        GenerateWeeklyQuest();
        Progress.WeeklyRerollsUsed++;
        _logger?.LogInformation("Weekly quest rerolled from {OldId} to {NewId}", oldId, Progress.WeeklyQuest?.DefinitionId);
        OnQuestsChanged();
        return true;
    }

    #endregion

    #region Progress

    /// <inheritdoc />
    public void AddProgress(QuestCategory category, int amount)
    {
        if (amount <= 0) return;

        bool changed = false;

        var dailyDef = GetDailyDefinition();
        if (dailyDef != null && dailyDef.Category == category && Progress.DailyQuest?.IsCompleted == false)
        {
            Progress.DailyQuest.CurrentProgress = Math.Min(
                Progress.DailyQuest.CurrentProgress + amount,
                dailyDef.TargetValue);

            if (Progress.DailyQuest.CurrentProgress >= dailyDef.TargetValue)
            {
                Progress.DailyQuest.IsCompleted = true;
                Progress.DailyQuest.CompletedAt = DateTime.Now;
            }
            changed = true;
        }

        var weeklyDef = GetWeeklyDefinition();
        if (weeklyDef != null && weeklyDef.Category == category && Progress.WeeklyQuest?.IsCompleted == false)
        {
            Progress.WeeklyQuest.CurrentProgress = Math.Min(
                Progress.WeeklyQuest.CurrentProgress + amount,
                weeklyDef.TargetValue);

            if (Progress.WeeklyQuest.CurrentProgress >= weeklyDef.TargetValue)
            {
                Progress.WeeklyQuest.IsCompleted = true;
                Progress.WeeklyQuest.CompletedAt = DateTime.Now;
            }
            changed = true;
        }

        if (changed)
        {
            OnQuestsChanged();
        }
    }

    /// <inheritdoc />
    public void TrackMantraCompleted()
    {
        AddProgress(QuestCategory.Mantra, 1);
    }

    private QuestDefinition? GetDailyDefinition()
    {
        if (Progress.DailyQuest == null) return null;
        return QuestDefinition.DailyQuests.FirstOrDefault(q => q.Id == Progress.DailyQuest.DefinitionId);
    }

    private QuestDefinition? GetWeeklyDefinition()
    {
        if (Progress.WeeklyQuest == null) return null;
        return QuestDefinition.WeeklyQuests.FirstOrDefault(q => q.Id == Progress.WeeklyQuest.DefinitionId);
    }

    /// <inheritdoc />
    public void CompleteDailyQuest()
    {
        var settings = _settingsService.Current;
        if (settings == null) return;

        int xp = GetScaledDailyXp();

        Progress.GetDailyQuestsCompletedToday();
        Progress.DailyQuestsCompletedToday++;
        Progress.TotalDailyQuestsCompleted++;

        var today = DateTime.Today;
        if (!Progress.DailyQuestCompletionDates.Contains(today))
        {
            Progress.DailyQuestCompletionDates.Add(today);
        }

        Progress.TotalXPFromQuests += xp;
        settings.PlayerXP += xp;

        Progress.DailyQuest = null;
        _settingsService.Save();

        _logger?.LogInformation("Daily quest completed: awarded {Xp} XP", xp);
        OnQuestsChanged();

        if (!Progress.AreAllDailyQuestsCompleted())
        {
            EnsureGenerated();
        }
    }

    /// <inheritdoc />
    public int GetScaledDailyXp()
    {
        var settings = _settingsService.Current;
        int playerLevel = settings?.PlayerLevel ?? 1;
        int questStreak = settings?.DailyQuestStreak ?? 0;
        return (int)Math.Round(150 * (1 + playerLevel * 0.04) * (1 + questStreak * 0.03));
    }

    /// <inheritdoc />
    public int GetScaledWeeklyXp()
    {
        var settings = _settingsService.Current;
        int playerLevel = settings?.PlayerLevel ?? 1;
        int questStreak = settings?.DailyQuestStreak ?? 0;
        return (int)Math.Round(600 * (1 + playerLevel * 0.04) * (1 + questStreak * 0.03));
    }

    #endregion
}
