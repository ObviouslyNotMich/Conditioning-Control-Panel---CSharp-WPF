using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.CompanionTab partial.
/// Companion selection cards, prompts, and avatar UI settings.
/// WPF-only services are stubbed with TODOs.
/// </summary>
public partial class CompanionTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly IAppLogger? _logger;
    private readonly IModService? _modService;

    public CompanionTabViewModel() : base("companion", "Companion", "🤖")
    {
        _companions = new ObservableCollection<CompanionCardViewModel>();
        _installedPrompts = new ObservableCollection<CommunityPromptRowViewModel>();
        InitializeDesignData();
    }

    public CompanionTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        IAppLogger logger,
        IModService modService) : base("companion", "Companion", "🤖")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;
        _modService = modService;
        _companions = new ObservableCollection<CompanionCardViewModel>();
        _installedPrompts = new ObservableCollection<CommunityPromptRowViewModel>();
        SyncUi();
    }

    [ObservableProperty]
    private ObservableCollection<CompanionCardViewModel> _companions;

    [ObservableProperty]
    private ObservableCollection<CommunityPromptRowViewModel> _installedPrompts;

    [ObservableProperty]
    private CompanionCardViewModel? _activeCompanion;

    [ObservableProperty]
    private string _activeCompanionName = "";

    [ObservableProperty]
    private string _activeCompanionLevelText = "";

    [ObservableProperty]
    private string _activeCompanionDescription = "";

    [ObservableProperty]
    private string _activeCompanionXpText = "";

    [ObservableProperty]
    private double _activeCompanionProgress;

    [ObservableProperty]
    private bool _avatarEnabled;

    [ObservableProperty]
    private bool _triggerModeEnabled;

    [ObservableProperty]
    private int _triggerIntervalSeconds = 60;

    [ObservableProperty]
    private int _idleIntervalSeconds = 120;

    [ObservableProperty]
    private int _bubbleDurationSeconds = 2;

    [ObservableProperty]
    private bool _isDetached;

    [ObservableProperty]
    private string _activePromptName = Loc.Get("label_default_built_in");

    [ObservableProperty]
    private string _customizePromptName = "";

    partial void OnAvatarEnabledChanged(bool value) => Save();
    partial void OnTriggerModeEnabledChanged(bool value) => Save();
    partial void OnTriggerIntervalSecondsChanged(int value) => Save();
    partial void OnIdleIntervalSecondsChanged(int value) => Save();
    partial void OnBubbleDurationSecondsChanged(int value) => Save();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _logger?.Information("Refreshing Companion tab");
        SyncUi();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task SwitchCompanionAsync(int companionIndex)
    {
        _logger?.Information("Switch companion requested: {Index}", companionIndex);
        // TODO: wire to ICompanionService.SwitchCompanion() once extracted to CCP.Core.
        var card = Companions.FirstOrDefault(c => c.Index == companionIndex);
        if (card != null)
        {
            foreach (var c in Companions) c.IsActive = false;
            card.IsActive = true;
            ActiveCompanion = card;
            UpdateActiveCompanionDetails(card);
        }

        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            Loc.Get("msg_companion_switch_not_yet_ported")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task AssignPersonalityAsync(int companionIndex)
    {
        _logger?.Information("Assign personality requested for companion {Index}", companionIndex);

        var filters = new[] { new FileFilter("JSON files", new[] { "json" }) };
        var files = await (_dialogService?.ShowOpenFileDialogAsync(
            Loc.Get("title_select_ai_personality"),
            filters) ?? Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));

        if (files.Count == 0) return;

        // TODO: wire to ICommunityPromptService.ImportFromFile() and companion prompt assignment.
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            Loc.Get("msg_personality_assign_not_yet_ported")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task ActivatePromptAsync(string? promptId)
    {
        if (string.IsNullOrWhiteSpace(promptId)) return;
        _logger?.Information("Activate community prompt: {PromptId}", promptId);
        // TODO: wire to ICommunityPromptService.ActivatePrompt() and explicit-content gate.
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            Loc.Get("msg_prompt_activate_not_yet_ported")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task RemovePromptAsync(string? promptId)
    {
        if (string.IsNullOrWhiteSpace(promptId)) return;
        _logger?.Information("Remove community prompt: {PromptId}", promptId);
        // TODO: wire to ICommunityPromptService.RemovePrompt().
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            Loc.Get("msg_prompt_remove_not_yet_ported")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task DeactivatePromptAsync()
    {
        _logger?.Information("Deactivate community prompt requested");
        // TODO: wire to ICommunityPromptService.DeactivatePrompt().
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            Loc.Get("msg_prompt_deactivate_not_yet_ported")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task CustomizePromptAsync()
    {
        _logger?.Information("Customize companion prompt requested");
        // TODO: wire to companion prompt editor dialog once ported.
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            Loc.Get("msg_prompt_customize_not_yet_ported")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task ToggleDetachAsync()
    {
        IsDetached = !IsDetached;
        _logger?.Information("Companion tab detach toggled: {Detached}", IsDetached);
        // TODO: wire to IAvatarTubeWindowService.Attach()/Detach().
        await Task.CompletedTask;
    }

    private void SyncUi()
    {
        try
        {
            var settings = _settingsService?.Current;
            if (settings == null)
            {
                InitializeDesignData();
                return;
            }

            AvatarEnabled = settings.AvatarEnabled;
            TriggerModeEnabled = settings.TriggerModeEnabled;
            TriggerIntervalSeconds = settings.TriggerIntervalSeconds;
            IdleIntervalSeconds = settings.IdleGiggleIntervalSeconds;
            BubbleDurationSeconds = (int)settings.BubbleDurationSeconds;

            RefreshCompanionCards();
            RefreshPrompts();
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "SyncCompanionTabUI failed");
        }
    }

    private void RefreshCompanionCards()
    {
        Companions.Clear();
        var colors = new[] { "#FF69B4", "#9370DB", "#50C878", "#FF6B6B", "#F5DEB3" };
        var activeId = 0; // TODO: wire to ICompanionService.ActiveCompanion.

        for (int i = 0; i < 5; i++)
        {
            // TODO: replace with ICompanionDefinition.GetById() and ICompanionService.GetProgress().
            var name = $"Companion {i + 1}";
            var level = 1;
            var isMax = false;
            var isSupported = true;

            Companions.Add(new CompanionCardViewModel
            {
                Index = i,
                Name = _modService?.MakeModAware(name) ?? name,
                LevelText = isMax ? "MAX" : $"Lv.{level}",
                ColorHex = colors[i],
                IsActive = i == activeId,
                IsSupported = isSupported,
                AssignedPromptName = ""
            });
        }

        ActiveCompanion = Companions.FirstOrDefault(c => c.IsActive);
        if (ActiveCompanion != null) UpdateActiveCompanionDetails(ActiveCompanion);
    }

    private void UpdateActiveCompanionDetails(CompanionCardViewModel card)
    {
        ActiveCompanionName = card.Name;
        ActiveCompanionLevelText = card.LevelText == "MAX" ? " · MAX LEVEL" : $" · Level {card.LevelText.TrimStart('L', 'v', '.')}";
        // TODO: wire to ICompanionDefinition.Description.
        ActiveCompanionDescription = Loc.Get("label_companion_description_placeholder");
        ActiveCompanionXpText = card.LevelText == "MAX" ? "Complete!" : "0 / 100 XP";
        ActiveCompanionProgress = card.LevelText == "MAX" ? 100 : 0;
    }

    private void RefreshPrompts()
    {
        InstalledPrompts.Clear();
        var settings = _settingsService?.Current;
        var activePromptId = settings?.ActiveCommunityPromptId;
        var installedIds = settings?.InstalledCommunityPromptIds ?? new List<string>();

        CustomizePromptName = GetActivePromptDisplayName();
        ActivePromptName = GetActivePromptDisplayName();

        if (installedIds.Count == 0)
        {
            InstalledPrompts.Add(new CommunityPromptRowViewModel
            {
                Name = Loc.Get("label_no_prompts_installed"),
                IsPlaceholder = true
            });
            return;
        }

        foreach (var id in installedIds)
        {
            // TODO: wire to ICommunityPromptService.GetInstalledPrompt(id).
            InstalledPrompts.Add(new CommunityPromptRowViewModel
            {
                Id = id,
                Name = $"Prompt {id}",
                Author = "Unknown",
                IsActive = id == activePromptId
            });
        }
    }

    private string GetActivePromptDisplayName()
    {
        var activePromptId = _settingsService?.Current?.ActiveCommunityPromptId;
        if (!string.IsNullOrEmpty(activePromptId))
        {
            // TODO: wire to ICommunityPromptService.GetInstalledPrompt(activePromptId).Name.
            return $"Prompt {activePromptId}";
        }

        if (_settingsService?.Current?.CompanionPrompt?.UseCustomPrompt == true)
        {
            return Loc.Get("label_custom_edited");
        }

        return Loc.Get("label_default_built_in");
    }

    private void Save()
    {
        try
        {
            var settings = _settingsService?.Current;
            if (settings == null) return;
            settings.AvatarEnabled = AvatarEnabled;
            settings.TriggerModeEnabled = TriggerModeEnabled;
            settings.TriggerIntervalSeconds = TriggerIntervalSeconds;
            settings.IdleGiggleIntervalSeconds = IdleIntervalSeconds;
            settings.BubbleDurationSeconds = BubbleDurationSeconds;
            _settingsService?.Save();
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Failed to save companion settings");
        }
    }

    private void InitializeDesignData()
    {
        AvatarEnabled = true;
        TriggerModeEnabled = false;
        TriggerIntervalSeconds = 60;
        IdleIntervalSeconds = 120;
        BubbleDurationSeconds = 2;
        Companions.Clear();
        InstalledPrompts.Clear();
        ActivePromptName = Loc.Get("label_default_built_in");
    }
}

public partial class CompanionCardViewModel : ObservableObject
{
    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _levelText = "";

    [ObservableProperty]
    private string _colorHex = "#FF69B4";

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isSupported = true;

    [ObservableProperty]
    private string _assignedPromptName = "";
}

public partial class CommunityPromptRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = "";

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _author = "";

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isPlaceholder;
}
