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
    private readonly IAvatarWindowService? _avatarWindowService;
    private readonly ILogger<CompanionHubTabViewModel>? _logger;

    public CompanionHubTabViewModel() : base("companionhub", "Companion Hub", "🤖")
    {
    }

    public CompanionHubTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        IAvatarWindowService avatarWindowService,
        ILogger<CompanionHubTabViewModel> logger) : base("companionhub", "Companion Hub", "🤖")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _avatarWindowService = avatarWindowService;
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
        _logger?.LogInformation("Initialize Avatar Tube requested");
        _avatarWindowService?.ShowTube();
        AvatarVisible = _avatarWindowService?.IsVisible ?? false;
        await Task.CompletedTask;
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
        _logger?.LogInformation("Show Avatar Tube requested");
        _avatarWindowService?.ShowTube();
        await Task.CompletedTask;
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
        _logger?.LogInformation("Hide Avatar Tube requested");
        _avatarWindowService?.HideTube();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task WakeBambiAsync()
    {
        _logger?.LogInformation("Wake Bambi requested");
        AvatarVisible = true;
        AvatarDetached = true;
        AvatarStatusText = Loc.Get("label_avatar_floating");
        _avatarWindowService?.ShowTube();
        _avatarWindowService?.SetDetached(true);
        _avatarWindowService?.Giggle("Bambi is awake~");
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ToggleDetachAsync()
    {
        AvatarDetached = !AvatarDetached;
        AvatarStatusText = AvatarDetached ? Loc.Get("label_avatar_floating") : Loc.Get("label_avatar_anchored");
        _logger?.LogInformation("Avatar detach toggled: {Detached}", AvatarDetached);
        _avatarWindowService?.SetDetached(AvatarDetached);
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task SetPoseAsync(int poseNumber)
    {
        CurrentPose = poseNumber;
        _logger?.LogInformation("Set avatar pose: {Pose}", poseNumber);
        _avatarWindowService?.SetPose(poseNumber);
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
        _logger?.LogInformation("Avatar mute set to {Muted}", muted);
        _avatarWindowService?.SetMuteAvatar(muted);
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task OpenAvatarSettingsAsync()
    {
        _logger?.LogInformation("Open avatar settings requested");
        // The dedicated avatar settings dialog is not yet ported; explain to the user.
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            Loc.Get("msg_avatar_settings_not_yet_ported")) ?? Task.CompletedTask);
    }

}
