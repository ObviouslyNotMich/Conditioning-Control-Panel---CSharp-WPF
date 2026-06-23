using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Core.Services.Companion;

/// <summary>
/// Cross-platform abstraction for companion switching and progress.
/// </summary>
public interface ICompanionService
{
    CompanionId ActiveCompanion { get; }
    CompanionDefinition ActiveCompanionDef { get; }
    CompanionProgress ActiveProgress { get; }

    event EventHandler<CompanionId>? CompanionSwitched;
    event EventHandler<(CompanionId Companion, double Amount, double Modifier)>? XPAwarded;
    event EventHandler<(CompanionId Companion, int NewLevel)>? LevelUp;
    event EventHandler<double>? XPDrained;

    CompanionProgress GetProgress(CompanionId id);
    bool SwitchCompanion(CompanionId newCompanion);
    void AddCompanionXP(double baseAmount, XPSource source);
    void OnAttentionCheckFailed();
    string GetCompanionStatusText();
}

/// <summary>
/// Cross-platform abstraction for community AI personality prompts.
/// </summary>
public interface ICommunityPromptService
{
    IReadOnlyList<string> InstalledPromptIds { get; }
    string? ActivePromptId { get; }

    event EventHandler<CommunityPrompt>? PromptInstalled;
    event EventHandler<string>? PromptRemoved;

    Task<List<CommunityPromptManifestEntry>> GetAvailablePromptsAsync(bool forceRefresh = false);
    Task<CommunityPrompt?> InstallPromptAsync(string promptId);
    Task<CommunityPrompt?> ImportFromFileAsync(string filePath);
    void RemovePrompt(string promptId);
    CommunityPrompt? GetInstalledPrompt(string promptId);
    List<CommunityPrompt> GetInstalledPrompts();
    bool ActivatePrompt(string promptId);
    void DeactivatePrompt();
}
