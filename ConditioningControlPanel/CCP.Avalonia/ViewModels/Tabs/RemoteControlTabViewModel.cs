using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;

using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the Remote Control tab. Wires the UI to <see cref="IRemoteControlService"/>
/// so start/stop, directory opt-in, and PIN copying work end-to-end.
/// </summary>
public partial class RemoteControlTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly IRemoteControlService? _remoteControlService;
    private readonly IAppLogger? _logger;
    private readonly IUiDispatcher? _uiDispatcher;

    public RemoteControlTabViewModel()
        : base("remotecontrol", "Remote Control", "🎮")
    {
        InitializeDefaults();
    }

    public RemoteControlTabViewModel(IServiceProvider services)
        : base("remotecontrol", "Remote Control", "🎮")
    {
        _settingsService = services.GetService<ISettingsService>();
        _dialogService = services.GetService<IDialogService>();
        _remoteControlService = services.GetService<IRemoteControlService>();
        _logger = services.GetService<IAppLogger>();
        _uiDispatcher = services.GetService<IUiDispatcher>();

        InitializeDefaults();

        var settings = _settingsService?.Current;
        if (settings != null)
        {
            settings.PropertyChanged += OnSettingsPropertyChanged;
            LoadSettings(settings);
            IsPremiumLocked = !(settings.HasLinkedPatreon || settings.HasLinkedDiscord);
        }
        else
        {
            IsPremiumLocked = true;
        }

        if (_remoteControlService != null)
        {
            _remoteControlService.SessionStarted += OnServiceSessionStarted;
            _remoteControlService.SessionEnded += OnServiceSessionEnded;
            _remoteControlService.ControllerConnectedChanged += OnControllerConnectedChanged;
        }
    }

    private void InitializeDefaults()
    {
        SelectedTier = "light";
        SelectedTierIndex = 0;
        UpdateTierSelectionFlags();

        OptInTags = new ObservableCollection<OptInTagItem>
        {
            new(Loc.Get("tag_bimbo")),
            new(Loc.Get("tag_drone")),
            new(Loc.Get("tag_trance")),
            new(Loc.Get("tag_feminization")),
            new(Loc.Get("tag_submission")),
            new(Loc.Get("tag_degradation")),
            new(Loc.Get("tag_audio_ok")),
            new(Loc.Get("tag_soft_only")),
            new(Loc.Get("tag_lockdown_ok")),
            new(Loc.Get("tag_chastity"))
        };
    }

    [ObservableProperty]
    private bool _isRemoteEnabled;

    [ObservableProperty]
    private bool _controllerConnected;

    [ObservableProperty]
    private string _sessionCode = "";

    [ObservableProperty]
    private string _connectPin = "";

    [ObservableProperty]
    private string _pairingUrl = "https://cclabs.app/remote/";

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private string _selectedTier = "light";

    [ObservableProperty]
    private int _selectedTierIndex;

    public IReadOnlyList<string> Tiers { get; } = new[] { "light", "standard", "full" };

    [ObservableProperty]
    private bool _isLightSelected;

    [ObservableProperty]
    private bool _isStandardSelected;

    [ObservableProperty]
    private bool _isFullSelected;

    [ObservableProperty]
    private bool _isDirectoryOptedIn;

    [ObservableProperty]
    private string _directoryStatusText = "";

    [ObservableProperty]
    private ObservableCollection<string> _commandLog = new();

    [ObservableProperty]
    private bool _stopEffectsOnDisconnect;

    [ObservableProperty]
    private bool _shareAvatar;

    [ObservableProperty]
    private bool _isPremiumLocked;

    [ObservableProperty]
    private ObservableCollection<OptInTagItem> _optInTags = new();

    [ObservableProperty]
    private string _optInStatusText = "";

    [ObservableProperty]
    private bool _rememberOptInDetails;

    [ObservableProperty]
    private string _customEmoteText = "";

    public string OptInStatusCharacterCount => $"{OptInStatusText?.Length ?? 0}/80";

    partial void OnSelectedTierChanged(string value)
    {
        SelectedTierIndex = Tiers.IndexOf(value);
        UpdateTierSelectionFlags();

        if (IsRemoteEnabled)
        {
            _ = RestartRemoteSessionAsync();
        }
    }

    partial void OnSelectedTierIndexChanged(int value)
    {
        if (value >= 0 && value < Tiers.Count && SelectedTier != Tiers[value])
        {
            SelectedTier = Tiers[value];
        }
    }

    partial void OnOptInStatusTextChanged(string value)
    {
        if (value.Length > 80)
        {
            OptInStatusText = value[..80];
            return;
        }

        OnPropertyChanged(nameof(OptInStatusCharacterCount));
    }

    partial void OnIsRemoteEnabledChanged(bool value)
    {
        if (value)
        {
            _ = StartRemoteSessionAsync();
        }
        else
        {
            _ = StopRemoteSessionAsync();
        }

        var settings = _settingsService?.Current;
        if (settings != null)
        {
            settings.StopEffectsOnRemoteDisconnect = StopEffectsOnDisconnect;
            settings.RemoteShareAvatar = ShareAvatar;
            _settingsService?.Save();
        }
    }

    public void RefreshStatus()
    {
        _logger?.Information("Remote Control tab activated.");
        SyncFromService();
    }

    [RelayCommand]
    private async Task StartSessionAsync()
    {
        if (_remoteControlService == null)
        {
            _logger?.Warning("Start session requested but IRemoteControlService is not available.");
            return;
        }

        await StartRemoteSessionAsync();
    }

    [RelayCommand]
    private async Task StopSessionAsync()
    {
        if (_remoteControlService == null)
        {
            _logger?.Warning("Stop session requested but IRemoteControlService is not available.");
            return;
        }

        await StopRemoteSessionAsync();
    }

    [RelayCommand]
    private async Task OptInAsync()
    {
        if (_remoteControlService == null)
        {
            _logger?.Warning("Directory opt-in requested but IRemoteControlService is not available.");
            return;
        }

        IsDirectoryOptedIn = !IsDirectoryOptedIn;
        if (IsDirectoryOptedIn)
        {
            DirectoryStatusText = Loc.Get("label_listed");
            _logger?.Information("Directory opt-in requested.");
            var selectedLabels = OptInTags
                .Where(t => t.IsSelected)
                .Select(t => t.Label)
                .ToList();
            await _remoteControlService.OptInToDirectoryAsync(selectedLabels, OptInStatusText);
        }
        else
        {
            DirectoryStatusText = Loc.Get("label_private_only");
        }
    }

    [RelayCommand]
    private async Task CopyPinAsync()
    {
        if (string.IsNullOrEmpty(ConnectPin)) return;

        var topLevel = GetCurrentTopLevel();
        if (topLevel?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(ConnectPin);
        }

        _logger?.Information("Copy remote PIN requested: {Pin}", ConnectPin);
    }

    [RelayCommand]
    private async Task CopySessionCodeAsync()
    {
        if (string.IsNullOrEmpty(SessionCode)) return;

        var topLevel = GetCurrentTopLevel();
        if (topLevel?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(SessionCode);
        }

        _logger?.Information("Copy remote session code requested: {Code}", SessionCode);
    }

    [RelayCommand]
    private async Task CopyPairingUrlAsync()
    {
        if (string.IsNullOrEmpty(PairingUrl)) return;

        var topLevel = GetCurrentTopLevel();
        if (topLevel?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(PairingUrl);
        }

        _logger?.Information("Copy remote pairing URL requested: {Url}", PairingUrl);
    }

    [RelayCommand]
    private async Task SendEmoteAsync(string? emote)
    {
        if (string.IsNullOrWhiteSpace(emote)) return;
        _logger?.Information("Send emote requested: {Emote}", emote);
        CommandLog.Add($"[{DateTime.Now:HH:mm:ss}] Emote: {emote}");
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task SendCustomEmoteAsync()
    {
        await SendEmoteAsync(CustomEmoteText);
        CustomEmoteText = "";
    }

    [RelayCommand]
    private void ToggleTag(OptInTagItem? tag)
    {
        if (tag == null) return;

        if (!tag.IsSelected && OptInTags.Count(t => t.IsSelected) >= 5)
        {
            _logger?.Warning("Directory opt-in tag limit reached.");
            return;
        }

        tag.IsSelected = !tag.IsSelected;
    }

    [RelayCommand]
    private async Task UnlockAsync()
    {
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("gate_premium_locked"),
            Loc.Get("desc_become_a_subject_locked"),
            DialogSeverity.Info) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task ShowHelpAsync()
    {
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("tab_remote_control"),
            Loc.Get("desc_remote_control") + "\n\n" + Loc.Get("desc_remote_control_bullets"),
            DialogSeverity.Info) ?? Task.CompletedTask);
    }

    private void UpdateTierSelectionFlags()
    {
        IsLightSelected = SelectedTier == "light";
        IsStandardSelected = SelectedTier == "standard";
        IsFullSelected = SelectedTier == "full";
    }

    private async Task StartRemoteSessionAsync()
    {
        _logger?.Information("Remote session start requested (tier: {Tier}).", SelectedTier);

        var settings = _settingsService?.Current;
        if (string.IsNullOrEmpty(settings?.UnifiedId))
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_login_required"),
                Loc.Get("msg_login_required_remote")) ?? Task.CompletedTask);
            IsRemoteEnabled = false;
            return;
        }

        if (!await ShowWaiverAsync())
        {
            IsRemoteEnabled = false;
            return;
        }

        if (_remoteControlService != null)
        {
            await _remoteControlService.StartSessionAsync(SelectedTier);
        }

        SyncFromService();
    }

    private async Task StopRemoteSessionAsync()
    {
        _logger?.Information("Remote session stop requested.");

        if (_remoteControlService != null)
        {
            await _remoteControlService.StopSessionAsync();
        }

        SyncFromService();
        await Task.CompletedTask;
    }

    private async Task RestartRemoteSessionAsync()
    {
        await StopRemoteSessionAsync();
        await StartRemoteSessionAsync();
    }

    private void OnServiceSessionStarted(object? sender, EventArgs e)
    {
        _uiDispatcher?.Post(SyncFromService);
    }

    private void OnServiceSessionEnded(object? sender, EventArgs e)
    {
        _uiDispatcher?.Post(SyncFromService);
    }

    private void OnControllerConnectedChanged(object? sender, EventArgs e)
    {
        _uiDispatcher?.Post(SyncFromService);
    }

    private void SyncFromService()
    {
        if (_remoteControlService == null) return;

        var active = _remoteControlService.IsActive;
        var connected = _remoteControlService.ControllerConnected;
        var code = _remoteControlService.SessionCode ?? "";
        var pin = _remoteControlService.ConnectPin ?? "";

        IsRemoteEnabled = active;
        ControllerConnected = connected;
        SessionCode = code;
        ConnectPin = pin;
        PairingUrl = string.IsNullOrEmpty(code)
            ? "https://cclabs.app/remote/"
            : $"https://cclabs.app/remote/#code={code.Replace("-", "")}";

        if (!active)
        {
            StatusMessage = Loc.Get("tab_remote_control_status_off");
            DirectoryStatusText = "";
            IsDirectoryOptedIn = false;
        }
        else if (connected)
        {
            StatusMessage = Loc.Get("tab_remote_control_status_connected");
        }
        else
        {
            StatusMessage = Loc.Get("label_waiting_for_controller");
        }

        if (active && !IsDirectoryOptedIn)
        {
            DirectoryStatusText = Loc.Get("label_private_only");
        }
    }

    private async Task<bool> ShowWaiverAsync()
    {
        var message =
            "You are about to allow another person to remotely control parts of your app.\n\n" +
            "The Controller will be able to:\n" +
            "  - Trigger flash images (from YOUR image folder)\n" +
            "  - Trigger subliminal messages (from YOUR subliminal pool)\n" +
            "  - Toggle overlays (pink filter, spiral)\n" +
            "  - Start/stop bubbles\n";

        if (SelectedTier is "standard" or "full")
        {
            message +=
                "  - Trigger mandatory videos (from YOUR video folder)\n" +
                "  - Trigger haptic device patterns\n" +
                "  - Duck/unduck audio\n";
        }

        if (SelectedTier == "full")
        {
            message +=
                "  - Start/stop autonomy mode\n" +
                "  - Start/pause/stop sessions\n" +
                "  - Enable strict lock (videos cannot be skipped)\n" +
                "  - Disable panic button (ESC key won't work)\n";
        }

        message +=
            "\nAll media content shown comes from YOUR local files and settings.\n" +
            "You assume full responsibility for this interaction.\n" +
            "You can stop the session at ANY time by clicking \"Stop Session\" or closing the app.";

        return await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("title_remote_control_waiver"),
            message) ?? Task.FromResult(false));
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not Core.Models.AppSettings settings) return;

        if (e.PropertyName == nameof(Core.Models.AppSettings.StopEffectsOnRemoteDisconnect))
        {
            StopEffectsOnDisconnect = settings.StopEffectsOnRemoteDisconnect;
        }
        else if (e.PropertyName == nameof(Core.Models.AppSettings.RemoteShareAvatar))
        {
            ShareAvatar = settings.RemoteShareAvatar;
        }
        else if (e.PropertyName is nameof(Core.Models.AppSettings.HasLinkedPatreon) or nameof(Core.Models.AppSettings.HasLinkedDiscord))
        {
            IsPremiumLocked = !(settings.HasLinkedPatreon || settings.HasLinkedDiscord);
        }
    }

    private void LoadSettings(Core.Models.AppSettings settings)
    {
        StopEffectsOnDisconnect = settings.StopEffectsOnRemoteDisconnect;
        ShareAvatar = settings.RemoteShareAvatar;
    }

    private static TopLevel? GetCurrentTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } window)
        {
            return window;
        }

        if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime single
            && single.MainView is { } view)
        {
            return TopLevel.GetTopLevel(view);
        }

        return null;
    }

    /// <summary>
    /// Selectable tag pill shown in the directory opt-in panel.
    /// </summary>
    public sealed partial class OptInTagItem : ObservableObject
    {
        [ObservableProperty]
        private string _label;

        [ObservableProperty]
        private bool _isSelected;

        public OptInTagItem(string label)
        {
            _label = label;
        }
    }
}
