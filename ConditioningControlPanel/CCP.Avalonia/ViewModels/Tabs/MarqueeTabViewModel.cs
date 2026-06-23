using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Avalonia.Windows;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.Marquee partial.
/// Hosts the banner/marquee message, server-controlled update banner checks,
/// and server-triggered announcement polling. Animation rendering is head-specific,
/// so the actual banner rotation UI is left to the view with these view-models
/// providing the message state and refresh commands.
/// </summary>
public partial class MarqueeTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly ILogger<MarqueeTabViewModel>? _logger;

    public MarqueeTabViewModel() : base("marquee", "Marquee", "📢")
    {
        InitializeDefaultMessage();
    }

    public MarqueeTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        ILogger<MarqueeTabViewModel> logger) : base("marquee", "Marquee", "📢")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;
        InitializeDefaultMessage();
        LoadMessageFromSettings();
    }

    [ObservableProperty]
    private string _marqueeMessage = "";

    [ObservableProperty]
    private string _updateBannerText = "";

    [ObservableProperty]
    private string? _updateBannerUrl;

    [ObservableProperty]
    private string _welcomeText = "";

    [ObservableProperty]
    private bool _isBusy;

    partial void OnMarqueeMessageChanged(string value)
    {
        if (_settingsService?.Current != null)
        {
            _settingsService.Current.MarqueeMessage = value;
            Save();
        }
    }

    [RelayCommand]
    private async Task RefreshFromServerAsync()
    {
        IsBusy = true;
        try
        {
            _logger?.LogInformation("Refreshing marquee message from server");
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await client.GetAsync("https://codebambi-proxy.vercel.app/config/marquee");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<MarqueeResponse>(json);
                if (!string.IsNullOrWhiteSpace(result?.Message) && result.Message != MarqueeMessage)
                {
                    MarqueeMessage = result.Message.ToUpperInvariant();
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Failed to refresh marquee from server: {Error}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckUpdateBannerAsync()
    {
        IsBusy = true;
        try
        {
            _logger?.LogInformation("Checking server update banner");
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await client.GetAsync("https://codebambi-proxy.vercel.app/config/update-banner");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<UpdateBannerResponse>(json);
                if (result?.Enabled == true && !string.IsNullOrWhiteSpace(result.Version))
                {
                    UpdateBannerText = $"UPDATE AVAILABLE v{result.Version}";
                    UpdateBannerUrl = result.Url;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Failed to check server update banner: {Error}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CheckAnnouncementAsync()
    {
        IsBusy = true;
        try
        {
            _logger?.LogInformation("Checking server announcement");
            var url = "https://codebambi-proxy.vercel.app/config/announcement";
            var unifiedId = _settingsService?.Current?.UnifiedId;
            if (!string.IsNullOrWhiteSpace(unifiedId))
            {
                url += $"?unified_id={Uri.EscapeDataString(unifiedId)}";
            }

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<AnnouncementResponse>(json);
                if (result?.Enabled == true
                    && !string.IsNullOrWhiteSpace(result.Id)
                    && !string.IsNullOrWhiteSpace(result.Title)
                    && result.Id != _settingsService?.Current?.DismissedAnnouncementId)
                {
                    _logger?.LogInformation("Server announcement received: id={Id}, title={Title}", result.Id, result.Title);
                    var popup = new AnnouncementPopup(
                        result.Id!,
                        result.Title!,
                        result.Message ?? "",
                        result.ImageUrl,
                        result.LinkUrl,
                        result.Theme)
                    {
                        WindowStartupLocation = WindowStartupLocation.CenterScreen
                    };
                    popup.Show();
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Failed to check server announcement: {Error}", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenUpdateUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(UpdateBannerUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = UpdateBannerUrl,
                UseShellExecute = true
            });
            _logger?.LogInformation("Opened update banner URL");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to open update URL");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                ex.Message,
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task UpdateWelcomeMessageAsync()
    {
        var settings = _settingsService?.Current;
        if (settings?.OfflineMode == true && !string.IsNullOrWhiteSpace(settings.OfflineUsername))
        {
            WelcomeText = Loc.GetF("label_welcome_back_0_offline_mode", settings.OfflineUsername);
        }
        else if (!string.IsNullOrEmpty(settings?.UserDisplayName))
        {
            WelcomeText = Loc.GetF("label_welcome_back_0", settings.UserDisplayName);
        }
        else
        {
            WelcomeText = Loc.Get("label_welcome_consider_logging_in_with_patreon_for");
        }

        await Task.CompletedTask;
    }

    private void InitializeDefaultMessage()
    {
        MarqueeMessage = "GOOD GIRLS CONDITION DAILY     ❤️🔒";
    }

    private void LoadMessageFromSettings()
    {
        var saved = _settingsService?.Current?.MarqueeMessage;
        if (!string.IsNullOrWhiteSpace(saved))
        {
            MarqueeMessage = saved;
        }

        if (string.IsNullOrWhiteSpace(MarqueeMessage) ||
            MarqueeMessage.Contains("WELCOME TO YOUR CONDITIONING") ||
            MarqueeMessage.Contains("RELAX AND SUBMIT"))
        {
            MarqueeMessage = "GOOD GIRLS CONDITION DAILY     ❤️🔒";
        }

        _ = UpdateWelcomeMessageAsync();
    }

    private void Save()
    {
        try { _settingsService?.Save(); }
        catch (Exception ex) { _logger?.LogWarning(ex, "Failed to save marquee settings"); }
    }

    private sealed class MarqueeResponse
    {
        public string? Message { get; set; }
    }

    private sealed class UpdateBannerResponse
    {
        public bool Enabled { get; set; }
        public string? Version { get; set; }
        public string? Message { get; set; }
        public string? Url { get; set; }
    }

    private sealed class AnnouncementResponse
    {
        public bool Enabled { get; set; }
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Message { get; set; }
        public string? ImageUrl { get; set; }
        public string? LinkUrl { get; set; }
        public string? Theme { get; set; }
    }
}
