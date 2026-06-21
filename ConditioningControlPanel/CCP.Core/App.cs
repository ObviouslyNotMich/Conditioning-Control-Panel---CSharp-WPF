using System.Collections.Generic;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Moderation;
using ConditioningControlPanel.Core.Services.Roadmap;

namespace ConditioningControlPanel;

/// <summary>
/// Temporary static application stub for CCP.Core so copied model files can compile.
/// This mirrors the legacy WPF <c>App</c> service locator enough for Core build only;
/// heads should replace these static references with dependency injection.
/// </summary>
public static class App
{
    public static IAppSettingsService? Settings { get; set; }
    public static IAppLogger? Logger { get; set; }
    public static ISkillTreeService? SkillTree { get; set; }
    public static IModService? Mods { get; set; }
    public static IProgressionService? Progression { get; set; }
    public static IRoadmapService? Roadmap { get; set; }
    public static string? TutorialBaseUrl { get; set; }

    // Service references used by ported Avalonia mini-game windows; typed as object/dynamic
    // because the concrete implementations currently live in the legacy WPF head.
    public static object? Achievements { get; set; }
    public static object? BubbleCount { get; set; }
    public static object? LockCard { get; set; }
    public static object? InteractionQueue { get; set; }

    // Service references used by ported Avalonia dialogs; typed as object/dynamic
    // because the concrete implementations currently live in the legacy WPF head.
    public static object? AttentionCheck { get; set; }
    public static object? KeywordPresets { get; set; }
    public static object? KeywordTriggers { get; set; }
    public static object? CompanionPhrases { get; set; }
    public static IPromptValidator? PromptValidator { get; set; }
    public static object? ModerationLog { get; set; }

    // Auth, Chaos, avatar, bark, video and main-window references are now resolved
    // through the typed DI abstractions below and registered by each head.

    // Additional service stubs referenced by extracted Core services until they are fully ported.
    public static object? Overlay { get; set; }
    public static object? DeeperHost { get; set; }
    public static object? Quests { get; set; }
    public static object? Haptics { get; set; }

    /// <summary>Ported bubble service. Assigned by cross-platform heads after DI is built.</summary>
    public static IBubbleService? Bubbles { get; set; }
}

public interface IAppSettingsService
{
    AppSettings Current { get; }
    void Save();
}

public interface IAppLogger
{
    void Debug(string messageTemplate, params object?[] propertyValues);
    void Debug(Exception exception, string messageTemplate, params object?[] propertyValues);
    void Information(string messageTemplate, params object?[] propertyValues);
    void Information(Exception exception, string messageTemplate, params object?[] propertyValues);
    void Warning(string messageTemplate, params object?[] propertyValues);
    void Warning(Exception exception, string messageTemplate, params object?[] propertyValues);
    void Error(string messageTemplate, params object?[] propertyValues);
    void Error(Exception exception, string messageTemplate, params object?[] propertyValues);
}

public interface ISkillTreeService
{
    bool HasSkill(string skillId);
    double GetTotalXpMultiplier();
    int TotalPointsSpent { get; }
    event EventHandler<string>? SkillUnlocked;
    Task<(bool Success, string? Error)> PurchaseSkillAsync(string skillId);

    // Legacy stubs still referenced by Core services until those services are fully ported.
    bool UseStreakShield();
    bool UseOopsieInsurance();
    int GetDailyStreakBonus(int consecutiveDays);
    int GetDailyFreeRerolls();
    void AddConditioningTime(double minutes);
}

public interface IModService
{
    string GetModeDisplayName();
    string MakeModAware(string text);
    string GetAccentColorHex();
    string GetSecondaryColorHex();
    string[] GetPhrases(string category);
    string GetPinkRushName();
    string GetPinkRushDescription();

    /// <summary>
    /// All installed mods, including built-ins and discovered user mods.
    /// </summary>
    IReadOnlyList<ModPackage> InstalledMods { get; }

    /// <summary>
    /// The currently active mod package.
    /// </summary>
    ModPackage ActiveMod { get; }

    /// <summary>
    /// Raised after the active mod changes.
    /// </summary>
    event EventHandler<ModPackage>? ActiveModChanged;

    /// <summary>
    /// Loads built-in and user-installed mods and selects the persisted active mod.
    /// </summary>
    void Initialize(string? activeModId);

    /// <summary>
    /// Extracts and installs a .ccpmod package into the user mods folder.
    /// </summary>
    Task<ModInstallResult> InstallModAsync(string ccpmodPath);

    /// <summary>
    /// Removes a user-installed mod. Built-in mods cannot be uninstalled.
    /// </summary>
    bool UninstallMod(string modId);

    /// <summary>
    /// Activates the mod with the given ID, persists the choice, and raises <see cref="ActiveModChanged"/>.
    /// </summary>
    bool ActivateMod(string modId);

    /// <summary>
    /// Returns the attention-check failure message for the active mod.
    /// </summary>
    string GetAttentionCheckFailMessage();

    /// <summary>
    /// Returns the attention-check mercy message for the active mod.
    /// </summary>
    string GetAttentionCheckMercyMessage();
}

public interface IProgressionService
{
    void AddXP(int amount, XPSource source);
    double GetSessionXPMultiplier(int playerLevel);
    double GetXPForLevel(int level);
    event EventHandler<int>? LevelUp;
}

public interface IKeywordTriggerPresetService
{
    bool IsInstalled(string presetId);
    bool InstallPreset(string presetId);
    bool UninstallPreset(string presetId);
    KeywordTriggerPreset? CloneToCustom(string presetId);
    IReadOnlyList<KeywordTriggerPreset> VisiblePresets { get; }
    event EventHandler? PresetsChanged;
}

public interface IKeywordTriggerService
{
    void PreviewAudioClip(string filePath, int volume);
}

public interface ICompanionPhraseService
{
    IEnumerable<string> GetCategoryNames();
}

public interface IInteractionQueueService
{
    /// <summary>Whether any fullscreen interaction is currently active.</summary>
    bool IsBusy { get; }

    /// <summary>Try to start an interaction. Returns true if started immediately; false if queued or discarded.</summary>
    bool TryStart(string interactionType, Action triggerAction, bool queue = true);

    /// <summary>Mark the current interaction as complete and trigger the next queued one.</summary>
    void Complete(string interactionType);

    /// <summary>Force clear the current interaction and any queued items.</summary>
    void ForceReset();

    /// <summary>Extend the stuck-detection timeout for the current interaction.</summary>
    void ExtendTimeout(TimeSpan duration);
}

public interface IBubbleCountService
{
    bool IsRunning { get; }
    bool IsBusy { get; }

    void Start();
    void Stop();
    void TriggerGame(bool forceTest = false);
    void RefreshSchedule();
    void ResetBusyState();
}

public interface IAttentionCheckService
{
    void FireNow();
}

public interface IModerationLog
{
    void RecordEdit(string fieldName, int count, string source);
}

#region Typed service abstractions for cross-platform heads

public interface IAuthProvider
{
    string ProviderName { get; }
    bool IsLoggedIn { get; }
    bool HasPremiumAccess { get; }
    Task StartOAuthFlowAsync();
    string? GetAccessToken();
    void Logout();
    string? UnifiedUserId { get; set; }
    string? DisplayName { get; set; }
}

public interface IUnifiedUserService
{
    string? UnifiedUserId { get; set; }
}

public interface IChaosService
{
    bool IsRunning { get; }
    bool IsManuallyPaused { get; }
    void ShowLoadoutSidebar();
    void CloseLoadoutSidebar();
    void NotifyLoadoutChanged();
    void StartRun(object cfg);
    void StartRunFromSidebar();
    void ToggleManualPause();
    void RequestStop();
    void CloseWarrenPhase();
    void OpenWarrenAt(string tag);
    void UnequipFromSidebar(string id);
    void UseToyById(string id);
}

public interface IAvatarWindowService
{
    bool IsMuted { get; }
    void SetMuteAvatar(bool muted);
    void SetChaosRunActive(bool active);
    void OpenChatWindow();
}

public interface IBarkService
{
    void NotifyChaosDollhouseFirstOpen();
    void NotifyChaosRevealFlash(string id);
    void NotifyChaosResultsShown(double score, double best, double delta, bool pb,
                                 int defused, int detonated, int bestCombo, string difficulty);
    void NotifyChaosRankUp(string rankName);
    void NotifyChaosGiftGiven();
    void NotifyChaosDraftAutopick();
}

public interface IVideoInfo
{
    bool IsPlaying { get; }
}

public interface IMainWindowService
{
    object? MainWindow { get; }
}

public interface ISessionLogService
{
    IReadOnlyList<SessionLog> LoadRecentLogs();
}

#endregion
