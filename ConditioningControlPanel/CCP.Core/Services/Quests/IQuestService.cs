using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Core.Services.Quests;

/// <summary>
/// Generates, persists, and tracks progress for daily and weekly quests.
/// </summary>
public interface IQuestService
{
    /// <summary>
    /// Current quest progress (active quests, reroll state, and statistics).
    /// </summary>
    QuestProgress Progress { get; }

    /// <summary>
    /// Raised whenever active quests or reroll state change.
    /// </summary>
    event EventHandler? QuestsChanged;

    /// <summary>
    /// Ensures daily and weekly quests have been generated for the current period.
    /// </summary>
    void EnsureGenerated();

    /// <summary>
    /// Rerolls the current daily quest if rerolls are available.
    /// </summary>
    /// <returns>True if a reroll was performed.</returns>
    bool RerollDaily();

    /// <summary>
    /// Rerolls the current weekly quest if rerolls are available.
    /// </summary>
    /// <returns>True if a reroll was performed.</returns>
    bool RerollWeekly();

    /// <summary>
    /// Adds progress to any active quest matching the given category.
    /// </summary>
    void AddProgress(QuestCategory category, int amount);

    /// <summary>
    /// Marks the current daily quest as completed, awards XP, and clears the slot
    /// so <see cref="EnsureGenerated"/> can create the next daily quest.
    /// </summary>
    void CompleteDailyQuest();

    /// <summary>
    /// Gets the number of daily rerolls still available this period.
    /// </summary>
    int GetRemainingDailyRerolls();

    /// <summary>
    /// Gets the number of weekly rerolls still available this period.
    /// </summary>
    int GetRemainingWeeklyRerolls();

    /// <summary>
    /// Gets the scaled XP reward for the current daily quest.
    /// </summary>
    int GetScaledDailyXp();

    /// <summary>
    /// Gets the scaled XP reward for the current weekly quest.
    /// </summary>
    int GetScaledWeeklyXp();
}
