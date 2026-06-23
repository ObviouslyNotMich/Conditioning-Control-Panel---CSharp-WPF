using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Core.Services.Progression;

/// <summary>
/// Provides quest definitions, optionally fetched from a remote server and cached locally.
/// Falls back to the embedded quest pool when no remote definitions are available.
/// </summary>
public interface IQuestDefinitionService
{
    /// <summary>
    /// Current season title (e.g. "Obey-tober").
    /// </summary>
    string SeasonTitle { get; }

    /// <summary>
    /// Current cached definition version.
    /// </summary>
    int Version { get; }

    /// <summary>
    /// When the definitions were last fetched from the server.
    /// </summary>
    DateTime? LastUpdated { get; }

    /// <summary>
    /// True if there are any active seasonal quests.
    /// </summary>
    bool HasSeasonalQuests { get; }

    /// <summary>
    /// Raised when definitions are refreshed from the server or the cache is loaded.
    /// </summary>
    event Action? QuestDefinitionsUpdated;

    /// <summary>
    /// Loads the local cache and refreshes from the server if stale.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Forces a refresh from the server.
    /// </summary>
    Task RefreshFromServerAsync();

    /// <summary>
    /// Returns the active daily quest pool (embedded + remote + seasonal).
    /// </summary>
    List<QuestDefinition> GetDailyQuests();

    /// <summary>
    /// Returns the active weekly quest pool (embedded + remote + seasonal).
    /// </summary>
    List<QuestDefinition> GetWeeklyQuests();

    /// <summary>
    /// Returns only seasonal quests.
    /// </summary>
    List<QuestDefinition> GetSeasonalQuests();
}
