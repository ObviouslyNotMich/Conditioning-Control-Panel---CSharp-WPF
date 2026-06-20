using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.Companion partial.
/// Avatar tube window management and companion event reactions.
/// WPF-only services are stubbed with TODOs.
/// </summary>
public partial class CompanionHubTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly IAppLogger? _logger;

    public CompanionHubTabViewModel() : base("companionhub", "Companion Hub", "🤖")
    {
    }

    public CompanionHubTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        IAppLogger logger) : base("companionhub", "Companion Hub", "🤖")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;
    }

    [ObservableProperty]
    private bool _avatarVisible;

    [ObservableProperty]
    private bool _avatarDetached;

    [ObservableProperty]
    private string _avatarStatusText = Loc.Get("label_avatar_anchored");

    [ObservableProperty]
    private int _currentPose = 1;

    [RelayCommand]
    private async Task InitializeAvatarAsync()
    {
        _logger?.Information("Initialize Avatar Tube requested");
        // TODO: port AvatarTubeWindow to Avalonia and wire to IAvatarTubeWindowService.
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            Loc.Get("msg_avatar_tube_not_yet_ported")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task ShowAvatarAsync()
    {
        if (_settingsService?.Current?.AvatarEnabled != true)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_avatar_disabled"),
                Loc.Get("msg_enable_avatar_first")) ?? Task.CompletedTask);
            return;
        }

        AvatarVisible = true;
        _logger?.Information("Show Avatar Tube requested");
        // TODO: wire to IAvatarTubeWindowService.ShowTube().
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            Loc.Get("msg_avatar_tube_not_yet_ported")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task HideAvatarAsync()
    {
        if (AvatarDetached && _settingsService?.Current?.AvatarEnabled == true)
        {
            // Don't hide detached tube when main window minimizes.
            return;
        }

        AvatarVisible = false;
        _logger?.Information("Hide Avatar Tube requested");
        // TODO: wire to IAvatarTubeWindowService.HideTube().
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task WakeBambiAsync()
    {
        _logger?.Information("Wake Bambi requested");
        AvatarVisible = true;
        AvatarDetached = true;
        AvatarStatusText = Loc.Get("label_avatar_floating");
        // TODO: wire to IAvatarTubeWindowService.Detach() and Giggle().
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            Loc.Get("msg_avatar_wake_not_yet_ported")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task ToggleDetachAsync()
    {
        AvatarDetached = !AvatarDetached;
        AvatarStatusText = AvatarDetached ? Loc.Get("label_avatar_floating") : Loc.Get("label_avatar_anchored");
        _logger?.Information("Avatar detach toggled: {Detached}", AvatarDetached);
        // TODO: wire to IAvatarTubeWindowService.Attach()/Detach().
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task SetPoseAsync(int poseNumber)
    {
        CurrentPose = poseNumber;
        _logger?.Information("Set avatar pose: {Pose}", poseNumber);
        // TODO: wire to IAvatarTubeWindowService.SetPose().
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task MuteAvatarAsync(bool muted)
    {
        if (_settingsService?.Current is { } s)
        {
            s.AvatarMuted = muted;
            _settingsService.Save();
        }
        _logger?.Information("Avatar mute set to {Muted}", muted);
        // TODO: wire to IAvatarTubeWindowService.SetMuteAvatar().
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task OpenAvatarSettingsAsync()
    {
        _logger?.Information("Open avatar settings requested");
        // TODO: wire to IAvatarSettingsDialog once ported.
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            Loc.Get("msg_avatar_settings_not_yet_ported")) ?? Task.CompletedTask);
    }

    /// <summary>
    /// Handles XP drain events from the companion system.
    /// </summary>
    [RelayCommand]
    private async Task OnXpDrainAsync(double amount)
    {
        _logger?.Information("Companion XP drain: {Amount}", amount);
        // TODO: wire to overlay/flash animation service once ported.
        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles companion level-up events.
    /// </summary>
    [RelayCommand]
    private async Task OnLevelUpAsync((int Companion, int NewLevel) args)
    {
        _logger?.Information("Companion level up: {Companion} -> {Level}", args.Companion, args.NewLevel);
        // TODO: wire to notification sound + tray toast once ported.
        await Task.CompletedTask;
    }
}
