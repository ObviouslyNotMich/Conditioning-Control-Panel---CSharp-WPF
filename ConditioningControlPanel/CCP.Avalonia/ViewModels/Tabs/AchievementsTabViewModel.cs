using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Avalonia.Windows;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.AchievementsTab partial.
/// Renders the achievement gallery and season-recap re-view action.
/// </summary>
public partial class AchievementsTabViewModel : TabItemViewModel
{
    private readonly IAchievementService? _achievementService;
    private readonly IDialogService? _dialogService;
    private readonly IAppLogger? _logger;
    private readonly ISettingsService? _settingsService;

    public AchievementsTabViewModel() : base("achievements", "Achievements", "🏆")
    {
        _freeTiles = new ObservableCollection<AchievementTileViewModel>();
        _patronTiles = new ObservableCollection<AchievementTileViewModel>();
        PopulateTiles();
    }

    public AchievementsTabViewModel(
        IAchievementService achievementService,
        IDialogService dialogService,
        IAppLogger logger,
        ISettingsService settingsService) : base("achievements", "Achievements", "🏆")
    {
        _achievementService = achievementService;
        _dialogService = dialogService;
        _logger = logger;
        _settingsService = settingsService;
        _freeTiles = new ObservableCollection<AchievementTileViewModel>();
        _patronTiles = new ObservableCollection<AchievementTileViewModel>();
        PopulateTiles();
        UpdateCounts();
    }

    [ObservableProperty]
    private ObservableCollection<AchievementTileViewModel> _freeTiles;

    [ObservableProperty]
    private ObservableCollection<AchievementTileViewModel> _patronTiles;

    [ObservableProperty]
    private string _freeCountText = "0 / 0";

    [ObservableProperty]
    private string _patronCountText = "0 / 0";

    [ObservableProperty]
    private bool _showPatronOverlay = true;

    [ObservableProperty]
    private bool _hasSeasonRecap;

    [RelayCommand]
    private void Refresh()
    {
        PopulateTiles();
        UpdateCounts();
    }

    [RelayCommand]
    private async Task ViewSeasonRecapAsync()
    {
        _logger?.Information("Season recap re-view requested");
        try
        {
            var snapshot = SeasonRecapService.LoadLatest();
            if (snapshot == null)
            {
                var settings = _settingsService?.Current;
                var seasonKey = settings?.SeasonStatsSeason ?? DateTime.UtcNow.ToString("yyyy-MM");
                snapshot = new SeasonRecapSnapshot
                {
                    SeasonKey = seasonKey,
                    CapturedAtUtc = DateTime.UtcNow,
                    HighestLevelEver = settings?.HighestLevelEver ?? 1,
                    SessionCount = settings?.SeasonSessionsStarted ?? 0,
                    SeasonMinutes = settings?.SeasonConditioningMinutes ?? 0,
                    AllTimeMinutes = settings?.TotalConditioningMinutes ?? 0,
                    LongestStreak = settings?.HighestStreak ?? 0,
                    DaysActive = 0,
                    SeasonLengthDays = 30,
                    FeatureUse = new Dictionary<string, int>(settings?.SeasonFeatureUse ?? new Dictionary<string, int>()),
                    FeaturesTotal = 0
                };
            }

            var window = new SeasonRecapWindow(new SeasonRecapViewModel(snapshot));
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
                await window.ShowDialog(desktop.MainWindow);
            else
                window.Show();

            if (_settingsService?.Current is { } currentSettings)
            {
                currentSettings.LastSeasonResetSeen = snapshot.SeasonKey;
                _settingsService.Save();
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "SeasonRecap: failed to open re-view");
        }
    }

    private void PopulateTiles()
    {
        FreeTiles.Clear();
        PatronTiles.Clear();

        var progress = _achievementService?.Progress;
        foreach (var kvp in Achievement.All)
        {
            var achievement = kvp.Value;
            if (achievement.IsHidden) continue;

            var isUnlocked = progress?.IsUnlocked(achievement.Id) ?? false;
            var tile = new AchievementTileViewModel
            {
                Id = achievement.Id,
                Name = achievement.LocalizedName,
                Requirement = achievement.LocalizedRequirement,
                FlavorText = achievement.LocalizedFlavorText,
                ImageName = achievement.ImageName,
                IsUnlocked = isUnlocked,
                IsExclusive = achievement.IsExclusive
            };

            if (achievement.IsExclusive)
                PatronTiles.Add(tile);
            else
                FreeTiles.Add(tile);
        }
    }

    private void UpdateCounts()
    {
        var progress = _achievementService?.Progress;
        var freeUnlocked = FreeTiles.Count(t => t.IsUnlocked);
        var freeTotal = FreeTiles.Count;
        var patronUnlocked = PatronTiles.Count(t => t.IsUnlocked);
        var patronTotal = PatronTiles.Count;

        FreeCountText = Loc.GetF("label_0_1_achievements_unlocked", freeUnlocked, freeTotal);
        PatronCountText = Loc.GetF("label_0_1_achievements_unlocked", patronUnlocked, patronTotal);

        // The WPF overlay hides patron achievements when the user has no premium access.
        ShowPatronOverlay = _achievementService?.CanUnlockExclusive ?? true;

        try
        {
            HasSeasonRecap = SeasonRecapService.HasAnySnapshot();
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "SeasonRecap: failed to check snapshot availability");
            HasSeasonRecap = false;
        }
    }
}

/// <summary>
/// Single achievement tile view model for the gallery grid.
/// </summary>
public partial class AchievementTileViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = "";

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _requirement = "";

    [ObservableProperty]
    private string _flavorText = "";

    [ObservableProperty]
    private string _imageName = "";

    [ObservableProperty]
    private bool _isUnlocked;

    [ObservableProperty]
    private bool _isExclusive;

    public string ToolTip => IsUnlocked
        ? $"{Name}\n\n\"{FlavorText}\""
        : $"???\n\nRequirement: {Requirement}";
}
