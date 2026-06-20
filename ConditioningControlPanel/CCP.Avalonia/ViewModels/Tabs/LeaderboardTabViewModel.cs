using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.Leaderboard partial.
/// Drives leaderboard mode selection, sorting, and refresh.
/// Leaderboard/ProfileSync services are not abstracted in Core yet, so refresh
/// and server operations are stubbed with TODOs.
/// </summary>
public partial class LeaderboardTabViewModel : TabItemViewModel
{
    private readonly IDialogService? _dialogService;
    private readonly IAppLogger? _logger;

    public LeaderboardTabViewModel() : base("leaderboard", "Leaderboard", "📊")
    {
        _entries = new ObservableCollection<LeaderboardEntryViewModel>();
        _modes = new ObservableCollection<LeaderboardModeViewModel>
        {
            new("monthly", Loc.Get("label_monthly"), "#FF69B4"),
            new("all-time", Loc.Get("label_all_time"), "#FFD700")
        };
    }

    public LeaderboardTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        IAppLogger logger) : base("leaderboard", "Leaderboard", "📊")
    {
        _dialogService = dialogService;
        _logger = logger;
        _entries = new ObservableCollection<LeaderboardEntryViewModel>();
        _modes = new ObservableCollection<LeaderboardModeViewModel>
        {
            new("monthly", Loc.Get("label_monthly"), "#FF69B4"),
            new("all-time", Loc.Get("label_all_time"), "#FFD700")
        };
    }

    [ObservableProperty]
    private ObservableCollection<LeaderboardModeViewModel> _modes;

    [ObservableProperty]
    private string _currentMode = "monthly";

    [ObservableProperty]
    private ObservableCollection<LeaderboardEntryViewModel> _entries;

    [ObservableProperty]
    private LeaderboardEntryViewModel? _selectedEntry;

    [ObservableProperty]
    private string _statusText = Loc.Get("label_syncing");

    [ObservableProperty]
    private string _seasonText = "";

    [ObservableProperty]
    private string _subtitleText = "";

    [ObservableProperty]
    private bool _isBusy;

    partial void OnCurrentModeChanged(string value)
    {
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync(string? sortBy = null)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = Loc.Get("label_syncing");

        try
        {
            _logger?.Information("Refreshing leaderboard (mode={Mode}, sort={Sort})", CurrentMode, sortBy ?? "default");

            // TODO: wire to IProfileSyncService.SyncProfileAsync() and ILeaderboardService.RefreshAsync() once extracted.
            await Task.Delay(250);

            StatusText = Loc.Get("label_failed_to_load");
            SeasonText = CurrentMode == "all-time"
                ? Loc.Get("label_all_time_legends_never_die")
                : Loc.GetF("label_0_prove_your_devotion", "Current Season");
            SubtitleText = CurrentMode == "all-time"
                ? Loc.Get("label_cumulative_xp_across_all_seasons")
                : Loc.Get("label_resets_monthly_your_rank_is_everything");

            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_not_implemented"),
                "Leaderboard refresh is not yet ported to Avalonia.") ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Error refreshing leaderboard");
            StatusText = Loc.Get("label_error_loading_leaderboard");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SetModeAsync(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode) || mode == CurrentMode) return;
        CurrentMode = mode;
        await RefreshAsync(mode == "all-time" ? "xp" : "level");
    }

    [RelayCommand]
    private async Task SortAsync(string? sortBy)
    {
        if (string.IsNullOrWhiteSpace(sortBy)) return;
        _logger?.Information("Leaderboard sort requested: {Sort}", sortBy);

        var sorted = sortBy.ToLowerInvariant() switch
        {
            "name" => Entries.OrderBy(x => x.DisplayName).ToList(),
            "online" => Entries.OrderByDescending(x => x.IsOnline).ThenByDescending(x => x.Level).ToList(),
            "achievements" => Entries.OrderByDescending(x => x.AchievementsCount).ToList(),
            _ => Entries.ToList()
        };

        Entries.Clear();
        for (int i = 0; i < sorted.Count; i++)
        {
            sorted[i].Rank = i + 1;
            Entries.Add(sorted[i]);
        }

        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task OpenDiscordProfileAsync(string? discordId)
    {
        if (string.IsNullOrWhiteSpace(discordId)) return;
        try
        {
            var url = $"https://discord.com/users/{discordId}";
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            _logger?.Information("Opened Discord profile for {DiscordId}", discordId);
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Failed to open Discord profile for {DiscordId}", discordId);
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                ex.Message,
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task ViewSelectedProfileAsync()
    {
        if (SelectedEntry == null) return;
        _logger?.Information("Leaderboard: view profile for {Name}", SelectedEntry.DisplayName);
        // TODO: switch to Profile tab and search once the Avalonia shell exposes tab routing.
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            "Profile search from leaderboard is not yet ported to Avalonia.") ?? Task.CompletedTask);
    }
}

/// <summary>
/// Single leaderboard row view model.
/// </summary>
public partial class LeaderboardEntryViewModel : ObservableObject
{
    [ObservableProperty]
    private int _rank;

    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private int _level;

    [ObservableProperty]
    private double _xp;

    [ObservableProperty]
    private double _totalXpEarned;

    [ObservableProperty]
    private int _highestLevelEver;

    [ObservableProperty]
    private int _patreonTier;

    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    private int _achievementsCount;

    [ObservableProperty]
    private string? _discordId;

    [ObservableProperty]
    private bool _isSeason0Og;
}

/// <summary>
/// Leaderboard mode toggle view model.
/// </summary>
public partial class LeaderboardModeViewModel : ObservableObject
{
    [ObservableProperty]
    private string _key = "";

    [ObservableProperty]
    private string _label = "";

    [ObservableProperty]
    private string _accentColor = "#FF69B4";

    public LeaderboardModeViewModel(string key, string label, string accentColor)
    {
        _key = key;
        _label = label;
        _accentColor = accentColor;
    }
}
