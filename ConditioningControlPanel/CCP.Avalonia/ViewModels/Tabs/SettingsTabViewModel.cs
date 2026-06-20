using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.Settings partial top-level actions:
/// save, exit, help, bug report, tutorial launch, feature enable dashboard,
/// and embedded browser launcher.
/// Feature-specific settings live in their respective FeatureControl view-models.
/// </summary>
public partial class SettingsTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly IAppLogger? _logger;
    private readonly IBrowserHost? _browserHost;

    /// <summary>
    /// Exposed so the view can request an embedded browser control from the platform host.
    /// </summary>
    public IBrowserHost? BrowserHost => _browserHost;

    public SettingsTabViewModel() : base("settings", "Dashboard", "📊")
    {
    }

    public SettingsTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        IAppLogger logger,
        IBrowserHost browserHost) : base("settings", "Dashboard", "📊")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;
        _browserHost = browserHost;
        RefreshFromSettings();
    }

    [ObservableProperty]
    private bool _isHelpOverlayVisible;

    #region Feature toggles

    [ObservableProperty]
    private bool _flashEnabled;

    [ObservableProperty]
    private bool _subliminalEnabled;

    [ObservableProperty]
    private bool _spiralEnabled;

    [ObservableProperty]
    private bool _pinkFilterEnabled;

    [ObservableProperty]
    private bool _bubblesEnabled;

    [ObservableProperty]
    private bool _lockCardEnabled;

    partial void OnFlashEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.FlashEnabled = value;
        Save();
    }

    partial void OnSubliminalEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.SubliminalEnabled = value;
        Save();
    }

    partial void OnSpiralEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.SpiralEnabled = value;
        Save();
    }

    partial void OnPinkFilterEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.PinkFilterEnabled = value;
        Save();
    }

    partial void OnBubblesEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.BubblesEnabled = value;
        Save();
    }

    partial void OnLockCardEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.LockCardEnabled = value;
        Save();
    }

    private void RefreshFromSettings()
    {
        if (_settingsService?.Current == null) return;
        FlashEnabled = _settingsService.Current.FlashEnabled;
        SubliminalEnabled = _settingsService.Current.SubliminalEnabled;
        SpiralEnabled = _settingsService.Current.SpiralEnabled;
        PinkFilterEnabled = _settingsService.Current.PinkFilterEnabled;
        BubblesEnabled = _settingsService.Current.BubblesEnabled;
        LockCardEnabled = _settingsService.Current.LockCardEnabled;
        AudioDuckEnabled = _settingsService.Current.AudioDuckingEnabled;
        MasterVolume = _settingsService.Current.MasterVolume;
        VideoVolume = _settingsService.Current.VideoVolume;
        DuckVolume = _settingsService.Current.DuckingLevel;
        ExcludeBrowserDucking = _settingsService.Current.ExcludeBambiCloudFromDucking;
        DiscordRichPresenceEnabled = _settingsService.Current.DiscordRichPresenceEnabled;
    }

    #endregion

    #region Audio

    [ObservableProperty]
    private bool _audioDuckEnabled;

    partial void OnAudioDuckEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.AudioDuckingEnabled = value;
        Save();
    }

    [ObservableProperty]
    private int _masterVolume;

    partial void OnMasterVolumeChanged(int value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.MasterVolume = value;
        MasterVolumeText = $"{value}%";
        Save();
    }

    [ObservableProperty]
    private string _masterVolumeText = "32%";

    [ObservableProperty]
    private int _videoVolume;

    partial void OnVideoVolumeChanged(int value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.VideoVolume = value;
        VideoVolumeText = $"{value}%";
        Save();
    }

    [ObservableProperty]
    private string _videoVolumeText = "50%";

    [ObservableProperty]
    private int _duckVolume;

    partial void OnDuckVolumeChanged(int value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.DuckingLevel = value;
        DuckVolumeText = $"{value}%";
        Save();
    }

    [ObservableProperty]
    private string _duckVolumeText = "80%";

    [ObservableProperty]
    private bool _excludeBrowserDucking;

    partial void OnExcludeBrowserDuckingChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.ExcludeBambiCloudFromDucking = value;
        Save();
    }

    #endregion

    #region Quick Links

    [ObservableProperty]
    private bool _discordRichPresenceEnabled;

    partial void OnDiscordRichPresenceEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.DiscordRichPresenceEnabled = value;
        Save();
        _logger?.Information("Discord Rich Presence {Status}", value ? "enabled" : "disabled");
    }

    [RelayCommand]
    private async Task UnifiedLoginAsync()
    {
        _logger?.Information("Unified login requested");
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            Loc.Get("msg_login_dialog_not_yet_ported")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task OpenDiscordAsync()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://discord.gg/YxVAMt4qaZ",
                UseShellExecute = true
            });
            _logger?.Information("Opened Discord invite link");
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to open Discord link");
        }
        await Task.CompletedTask;
    }

    #endregion

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            _settingsService?.SaveImmediate(suppressCloudBackup: false);
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_success"),
                Loc.Get("msg_settings_saved")) ?? Task.CompletedTask);
            _logger?.Information("Settings saved from settings tab");
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to save settings");
        }
    }

    [RelayCommand]
    private async Task OpenBugReportAsync()
    {
        _logger?.Information("Bug report requested");
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("bug_report_title"),
            Loc.Get("bug_report_error_toast")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private void ShowHelpOverlay()
    {
        IsHelpOverlayVisible = true;
        _logger?.Information("Help overlay opened");
    }

    [RelayCommand]
    private void HideHelpOverlay()
    {
        IsHelpOverlayVisible = false;
    }

    [RelayCommand]
    private async Task StartTutorialAsync(string tutorialType)
    {
        _logger?.Information("Tutorial requested: {Type}", tutorialType);
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_tutorial"),
            Loc.GetF("msg_tutorial_starting_0", tutorialType)) ?? Task.CompletedTask);
    }

    #region Browser

    [ObservableProperty]
    private bool _browserInitialized;

    [ObservableProperty]
    private string _browserStatusText = Loc.Get("label_disconnected");

    [ObservableProperty]
    private string _browserLoadingText = Loc.Get("label_initializing_webview2");

    [ObservableProperty]
    private bool _isBambiCloudSelected = true;

    [ObservableProperty]
    private bool _audioSyncLatencyPanelVisible;

    [ObservableProperty]
    private int _legacyAudioSyncLatency;

    [ObservableProperty]
    private string _legacyAudioSyncLatencyText = "+0ms";

    [ObservableProperty]
    private int _legacyAudioSyncIntensity = 50;

    [ObservableProperty]
    private string _legacyAudioSyncIntensityText = "50%";

    partial void OnLegacyAudioSyncLatencyChanged(int value)
    {
        if (_settingsService?.Current?.Haptics?.AudioSync == null) return;
        _settingsService.Current.Haptics.AudioSync.ManualLatencyOffsetMs = value;
        LegacyAudioSyncLatencyText = $"{(value >= 0 ? "+" : "")}{value}ms";
        Save();
    }

    partial void OnLegacyAudioSyncIntensityChanged(int value)
    {
        if (_settingsService?.Current?.Haptics?.AudioSync == null) return;
        _settingsService.Current.Haptics.AudioSync.LiveIntensity = value / 100.0;
        LegacyAudioSyncIntensityText = $"{value}%";
        Save();
    }

    private string ResolveBrowserUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "https://bambicloud.com/";

        return url.ToLowerInvariant() switch
        {
            "hypnotube" => "https://hypnotube.com/",
            "bambicloud" => "https://bambicloud.com/",
            _ => url
        };
    }

    [RelayCommand]
    private async Task OpenBrowserAsync(string? url)
    {
        if (_settingsService?.Current?.OfflineMode == true)
        {
            _logger?.Information("Browser launch blocked in offline mode");
            return;
        }

        var targetUrl = ResolveBrowserUrl(url);
        IsBambiCloudSelected = targetUrl.Contains("bambicloud.com", StringComparison.OrdinalIgnoreCase);

        try
        {
            _logger?.Information("Navigating browser to {Url}", targetUrl);
            await (_browserHost?.NavigateAsync(new Uri(targetUrl)) ?? Task.CompletedTask);
            BrowserStatusText = Loc.Get("label_connected");
            BrowserInitialized = true;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to navigate browser to {Url}", targetUrl);
            BrowserStatusText = Loc.Get("label_failed");
        }
    }

    [RelayCommand]
    private async Task InitializeBrowserAsync(string? startUrl)
    {
        if (_settingsService?.Current?.OfflineMode == true)
        {
            _logger?.Information("Browser initialization blocked in offline mode");
            return;
        }

        BrowserStatusText = Loc.Get("label_loading");
        BrowserLoadingText = Loc.Get("label_creating_browser");

        await OpenBrowserAsync(startUrl);

        BrowserLoadingText = "";
    }

    [RelayCommand]
    private async Task NavigateBrowserAsync(string? url)
    {
        if (_settingsService?.Current?.OfflineMode == true) return;
        if (string.IsNullOrEmpty(url)) return;

        await OpenBrowserAsync(url);
    }

    [RelayCommand]
    private async Task BrowserSiteToggleChangedAsync(string? site)
    {
        if (_settingsService?.Current?.OfflineMode == true) return;
        var url = site?.ToLowerInvariant() == "hypnotube"
            ? "https://hypnotube.com/"
            : "https://bambicloud.com/";
        await OpenBrowserAsync(url);
    }

    [RelayCommand]
    private async Task PopOutBrowserAsync()
    {
        if (_settingsService?.Current?.OfflineMode == true) return;
        await OpenBrowserAsync(null);
        _logger?.Information("Browser pop-out requested");
    }

    #endregion

    private void Save()
    {
        try { _settingsService?.Save(); }
        catch (Exception ex) { _logger?.Warning(ex, "Failed to save settings"); }
    }
}
