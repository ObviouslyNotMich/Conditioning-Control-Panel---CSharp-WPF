using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Avalonia.Services.Video;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.AccountShell and MainWindow.CloudBackup partials.
/// Hosts account actions, update checks, cloud backup/export, and support links.
/// </summary>
public partial class AppInfoTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly ISettingsBackupProvider? _backupProvider;
    private readonly IDialogService? _dialogService;
    private readonly IAppLogger? _logger;
    private readonly IAudioPlayer? _audioPlayer;
    private readonly AvaloniaDualMonitorVideoService? _videoService;

    public AppInfoTabViewModel() : base("appinfo", "App Info", "ℹ️")
    {
        LanguageOptions = new ObservableCollection<LanguageOption>();
        InitializeLanguages();
    }

    public AppInfoTabViewModel(
        ISettingsService settingsService,
        ISettingsBackupProvider backupProvider,
        IDialogService dialogService,
        IAppLogger logger,
        IAudioPlayer? audioPlayer = null,
        AvaloniaDualMonitorVideoService? videoService = null) : base("appinfo", "App Info", "ℹ️")
    {
        _settingsService = settingsService;
        _backupProvider = backupProvider;
        _dialogService = dialogService;
        _logger = logger;
        _audioPlayer = audioPlayer;
        _videoService = videoService;
        LanguageOptions = new ObservableCollection<LanguageOption>();
        InitializeLanguages();
        _ = RefreshBackupStatusAsync();
    }

    [ObservableProperty]
    private string _patreonButtonText = Loc.Get("btn_login_patreon");

    [ObservableProperty]
    private string _discordButtonText = Loc.Get("btn_login_discord");

    [ObservableProperty]
    private string _backupStatusText = Loc.Get("label_no_cloud_backup_found_back_up_your_settings_t");

    [ObservableProperty]
    private bool _isRichPresenceEnabled;

    [ObservableProperty]
    private ObservableCollection<LanguageOption> _languageOptions;

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    [ObservableProperty]
    private bool _isBusy;

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value == null || _settingsService?.Current == null) return;
        var current = _settingsService.Current;
        if (current.Language == value.Code) return;

        current.Language = value.Code;
        LocalizationManager.Instance.SetLanguage(value.Code);
        _settingsService.Save();
        _logger?.Information("Language changed to {Language}", value.Code);
    }

    private void InitializeLanguages()
    {
        var current = _settingsService?.Current?.Language ?? LocalizationManager.Instance.CurrentLanguage ?? "en";
        foreach (var (code, displayName, shortName) in LocalizationManager.AvailableLanguages)
        {
            var option = new LanguageOption(code, $"🌐 {shortName}", displayName);
            LanguageOptions.Add(option);
            if (code == current) SelectedLanguage = option;
        }
    }

    [RelayCommand]
    private async Task QuickPatreonLoginAsync()
    {
        // Legacy OAuth flow is WPF-only. Avalonia uses a unified login dialog (future work).
        _logger?.Information("Patreon login requested");
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            Loc.Get("msg_patreon_login_dialog_not_yet_ported")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task QuickDiscordLoginAsync()
    {
        _logger?.Information("Discord login requested");
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            Loc.Get("msg_discord_login_dialog_not_yet_ported")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task OpenDiscordInviteAsync()
    {
        try
        {
            OpenUrl("https://discord.gg/YxVAMt4qaZ");
            _logger?.Information("Opened Discord invite link");
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to open Discord link");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                ex.Message,
                DialogSeverity.Error) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task ToggleRichPresenceAsync()
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.DiscordRichPresenceEnabled = IsRichPresenceEnabled;
        _settingsService.Save();
        _logger?.Information("Discord Rich Presence {Status}", IsRichPresenceEnabled ? "enabled" : "disabled");
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task BackupSettingsAsync()
    {
        if (_backupProvider == null) return;
        IsBusy = true;
        try
        {
            await _backupProvider.BackupSettingsAsync();
            await RefreshBackupStatusAsync();
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_backup_complete"),
                Loc.Get("msg_settings_backed_up_to_cloud_successfully")) ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Manual settings backup failed");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_backup_failed"),
                Loc.Get("msg_failed_to_backup_settings"),
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestoreSettingsAsync()
    {
        if (_backupProvider == null) return;

        var confirm = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("title_restore_settings_from_cloud"),
            Loc.Get("msg_restore_settings_confirm")) ?? Task.FromResult(false));
        if (!confirm) return;

        IsBusy = true;
        try
        {
            // Restore is provider-specific; the current no-op provider has no data to restore.
            await _backupProvider.BackupSettingsAsync();
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_restore_failed"),
                Loc.Get("msg_no_cloud_backup_found_or_restore_failed"),
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Manual settings restore failed");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_restore_error"),
                Loc.GetF("msg_restore_failed_0", ex.Message),
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportDataAsync()
    {
        if (_settingsService?.Current == null) return;

        IsBusy = true;
        try
        {
            var path = await (_dialogService?.ShowSaveFileDialogAsync(
                Loc.Get("title_save_data_export"),
                new[] { new FileFilter("JSON files", new[] { "json" }) },
                $"my-data-export-{DateTime.Now:yyyy-MM-dd}.json") ?? Task.FromResult<string?>(null));

            if (string.IsNullOrEmpty(path)) return;

            // Minimal export: current settings serialized. Full export is provider-specific.
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(_settingsService.Current, Newtonsoft.Json.Formatting.Indented);
            await File.WriteAllTextAsync(path, json);

            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_export_complete"),
                Loc.GetF("msg_data_exported_to_0", path)) ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Data export failed");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_export_error"),
                Loc.GetF("msg_export_failed_0", ex.Message),
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenPrivacyPolicyAsync()
    {
        try
        {
            OpenUrl("https://cclabs.app/privacy-policy.html");
            _logger?.Information("Opened privacy policy");
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Failed to open privacy policy");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                ex.Message,
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
    }

    private async Task RefreshBackupStatusAsync()
    {
        try
        {
            if (_backupProvider?.HasCloudIdentity != true)
            {
                BackupStatusText = Loc.Get("label_no_cloud_backup_found_back_up_your_settings_t");
                return;
            }

            // The no-op provider has no timestamp; real providers would expose this.
            BackupStatusText = Loc.Get("label_cloud_backup_available");
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Failed to update backup status");
            BackupStatusText = Loc.Get("label_could_not_check_backup_status");
        }

        await Task.CompletedTask;
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private async Task SmokeTestAudioAsync()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "AwarenessPresets", "audio", "chime.wav");
            if (!File.Exists(path))
            {
                _logger?.Warning("Smoke-test audio sample not found: {Path}", path);
                await (_dialogService?.ShowMessageAsync("Audio smoke test", "Sample not found.", DialogSeverity.Warning) ?? Task.CompletedTask);
                return;
            }

            await (_audioPlayer?.PlayAsync(path) ?? Task.CompletedTask);
            await Task.Delay(1200);
            _audioPlayer?.Stop();
            _logger?.Information("Audio smoke-test completed");
            await (_dialogService?.ShowMessageAsync("Audio smoke test", "Played sample successfully.") ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Audio smoke-test failed");
            await (_dialogService?.ShowMessageAsync("Audio smoke test", $"Failed: {ex.Message}", DialogSeverity.Error) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task SmokeTestVideoAsync()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "tutorial_videos", "_test_loop.mp4");
            if (!File.Exists(path))
            {
                _logger?.Warning("Smoke-test video sample not found: {Path}", path);
                await (_dialogService?.ShowMessageAsync("Video smoke test", "Sample not found.", DialogSeverity.Warning) ?? Task.CompletedTask);
                return;
            }

            _videoService?.PlayFile(path, 640, 360);
            await Task.Delay(3000);
            _videoService?.Stop();
            _logger?.Information("Video smoke-test completed");
            await (_dialogService?.ShowMessageAsync("Video smoke test", "Played sample successfully.") ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Video smoke-test failed");
            await (_dialogService?.ShowMessageAsync("Video smoke test", $"Failed: {ex.Message}", DialogSeverity.Error) ?? Task.CompletedTask);
        }
    }
}

public sealed record LanguageOption(string Code, string DisplayName, string ToolTip);
