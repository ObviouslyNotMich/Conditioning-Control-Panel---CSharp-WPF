using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.Animations partial.
/// Exposes commands for expandable icon buttons and tab-level animation effects
/// (season-title shimmer, lockdown pulse, skill-tree particles). These are head-specific
/// render operations, so the view is responsible for the actual storyboards; the view-model
/// provides state and triggers.
/// </summary>
public partial class AnimationsTabViewModel : TabItemViewModel
{
    private readonly IDialogService? _dialogService;
    private readonly IAppLogger? _logger;

    public AnimationsTabViewModel() : base("animations", "Animations", "✨")
    {
    }

    public AnimationsTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        IAppLogger logger) : base("animations", "Animations", "✨")
    {
        _dialogService = dialogService;
        _logger = logger;
    }

    [ObservableProperty]
    private bool _isSeasonTitleShimmerActive;

    [ObservableProperty]
    private bool _isLockdownPulseActive;

    [ObservableProperty]
    private bool _areSkillTreeAnimationsActive;

    [RelayCommand]
    private void StartSeasonTitleShimmer()
    {
        IsSeasonTitleShimmerActive = true;
        _logger?.Information("Season title shimmer started");
    }

    [RelayCommand]
    private void StopSeasonTitleShimmer()
    {
        IsSeasonTitleShimmerActive = false;
        _logger?.Information("Season title shimmer stopped");
    }

    [RelayCommand]
    private void StartLockdownPulse()
    {
        IsLockdownPulseActive = true;
        _logger?.Information("Lockdown pulse started");
    }

    [RelayCommand]
    private void StopLockdownPulse()
    {
        IsLockdownPulseActive = false;
        _logger?.Information("Lockdown pulse stopped");
    }

    [RelayCommand]
    private void StartSkillTreeAnimations()
    {
        AreSkillTreeAnimationsActive = true;
        _logger?.Information("Skill tree animations started");
    }

    [RelayCommand]
    private void StopSkillTreeAnimations()
    {
        AreSkillTreeAnimationsActive = false;
        _logger?.Information("Skill tree animations stopped");
    }

    [RelayCommand]
    private async Task ShowExpandableIconLabelAsync(bool show)
    {
        _logger?.Information("Expandable icon label state: {Show}", show);
        // The view should react to a shared visual-state property if needed.
        // TODO: port ExpandableIcon_MouseEnter/Leave storyboards to Avalonia once animations are needed.
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ShowOgWelcomePopupAsync()
    {
        _logger?.Information("OG welcome popup requested");
        // TODO: port the Season 0 OG welcome dialog to Avalonia.
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_welcome_back"),
            "Season 0 OG welcome popup is not yet ported to Avalonia.") ?? Task.CompletedTask);
    }
}
