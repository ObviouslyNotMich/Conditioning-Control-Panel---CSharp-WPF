using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Models.Deeper;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.DeeperTab partial.
/// Full Deeper hub: welcome card, library filtering/sorting, webcam setup gateway,
/// and all header / row / webcam commands. Services that have not been extracted
/// to CCP.Core yet are surfaced through <see cref="IDialogService"/>.
/// </summary>
public partial class DeeperTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly IAppLogger? _logger;

    private readonly ObservableCollection<DeeperLibraryRowViewModel> _allEntries = new();

    public enum DeeperMediaTypeFilter { All, Video, Audio }
    public enum DeeperSortMode { Recent, Name, Creator }

    public DeeperTabViewModel() : base("deeper", "Deeper", "🌊")
    {
        SortOptions = new ObservableCollection<string>
        {
            Loc.Get("deeper_hub_sort_recent"),
            Loc.Get("deeper_hub_sort_name"),
            Loc.Get("deeper_hub_sort_creator")
        };
        WebcamDevices = new ObservableCollection<string>();
        Monitors = new ObservableCollection<string>();
        FilteredEntries = new ObservableCollection<DeeperLibraryRowViewModel>();

        MediaTypeFilter = DeeperMediaTypeFilter.All;
        WebcamCalibrationStatusText = Loc.Get("blink_trainer_calibration_none");
        WebcamTrackerButtonText = Loc.Get("deeper_webcam_start_tracker");
        WebcamManageConsentButtonText = Loc.Get("deeper_webcam_grant_consent");
        WebcamConsentStatusText = Loc.Get("deeper_webcam_consent_missing");

        if (Design.IsDesignMode)
        {
            LoadDesignTimeData();
        }
        else
        {
            WelcomeCardVisible = true;
            RefreshWebcamUi();
        }
    }

    public DeeperTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        IAppLogger logger) : base("deeper", "Deeper", "🌊")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;

        SortOptions = new ObservableCollection<string>
        {
            Loc.Get("deeper_hub_sort_recent"),
            Loc.Get("deeper_hub_sort_name"),
            Loc.Get("deeper_hub_sort_creator")
        };
        WebcamDevices = new ObservableCollection<string>();
        Monitors = new ObservableCollection<string>();
        FilteredEntries = new ObservableCollection<DeeperLibraryRowViewModel>();

        MediaTypeFilter = DeeperMediaTypeFilter.All;
        WebcamCalibrationStatusText = Loc.Get("blink_trainer_calibration_none");
        WebcamTrackerButtonText = Loc.Get("deeper_webcam_start_tracker");
        WebcamManageConsentButtonText = Loc.Get("deeper_webcam_grant_consent");
        WebcamConsentStatusText = Loc.Get("deeper_webcam_consent_missing");

        WelcomeCardVisible = !(_settingsService.Current?.HasSeenDeeperWelcome ?? true);
        RefreshWebcamUi();
    }

    #region Header / Welcome

    [ObservableProperty]
    private bool _welcomeCardVisible;

    [ObservableProperty]
    private string _searchText = "";

    #endregion

    #region Filters / Sort

    [ObservableProperty]
    private DeeperMediaTypeFilter _mediaTypeFilter;

    [ObservableProperty]
    private bool _isFilterAll;

    [ObservableProperty]
    private bool _isFilterVideo;

    [ObservableProperty]
    private bool _isFilterAudio;

    [ObservableProperty]
    private bool _filterHaptics;

    [ObservableProperty]
    private bool _filterWebcam;

    [ObservableProperty]
    private int _selectedSortIndex;

    [ObservableProperty]
    private ObservableCollection<string> _sortOptions;

    [ObservableProperty]
    private ObservableCollection<DeeperLibraryRowViewModel> _filteredEntries;

    public int AllCount => _allEntries.Count;
    public int VideoCount => _allEntries.Count(e => e.MediaType == MediaTypes.Video);
    public int AudioCount => _allEntries.Count(e => e.MediaType == MediaTypes.Audio);
    public int HapticsCount => _allEntries.Count(e => e.HasHapticsTag);
    public int WebcamCount => _allEntries.Count(e => e.HasWebcamTag);

    public string LibraryCountText => Loc.GetF("deeper_library_count_fmt", FilteredEntries.Count);
    public bool IsLibraryEmpty => FilteredEntries.Count == 0;
    public string LibraryEmptyText =>
        string.IsNullOrWhiteSpace(SearchText) &&
        !FilterHaptics &&
        !FilterWebcam &&
        MediaTypeFilter == DeeperMediaTypeFilter.All &&
        _allEntries.Count == 0
            ? Loc.Get("deeper_library_empty")
            : Loc.Get("deeper_hub_empty_filtered");

    #endregion

    #region Webcam Setup

    [ObservableProperty]
    private ObservableCollection<string> _webcamDevices;

    [ObservableProperty]
    private string? _selectedWebcamDevice;

    [ObservableProperty]
    private ObservableCollection<string> _monitors;

    [ObservableProperty]
    private string? _selectedMonitor;

    [ObservableProperty]
    private bool _isWebcamConsentGranted;

    [ObservableProperty]
    private string _webcamConsentStatusText;

    [ObservableProperty]
    private string _webcamManageConsentButtonText;

    [ObservableProperty]
    private string _webcamCalibrationStatusText;

    [ObservableProperty]
    private string _webcamTrackerButtonText;

    [ObservableProperty]
    private bool _isTrackerRunning;

    [ObservableProperty]
    private bool _restrictGazeToCalScreen = true;

    [ObservableProperty]
    private bool _blinkToRecalEnabled;

    #endregion

    #region Property Change Hooks

    partial void OnWelcomeCardVisibleChanged(bool value)
    {
        if (_settingsService?.Current is { } s)
        {
            s.HasSeenDeeperWelcome = !value;
            _settingsService.Save();
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilterAndSort();

    partial void OnMediaTypeFilterChanged(DeeperMediaTypeFilter value)
    {
        IsFilterAll = value == DeeperMediaTypeFilter.All;
        IsFilterVideo = value == DeeperMediaTypeFilter.Video;
        IsFilterAudio = value == DeeperMediaTypeFilter.Audio;
        ApplyFilterAndSort();
    }

    partial void OnFilterHapticsChanged(bool value) => ApplyFilterAndSort();

    partial void OnFilterWebcamChanged(bool value) => ApplyFilterAndSort();

    partial void OnSelectedSortIndexChanged(int value) => ApplyFilterAndSort();

    partial void OnIsWebcamConsentGrantedChanged(bool value) => RefreshWebcamUi();

    partial void OnIsTrackerRunningChanged(bool value)
    {
        WebcamTrackerButtonText = value
            ? Loc.Get("deeper_webcam_stop_tracker")
            : Loc.Get("deeper_webcam_start_tracker");
    }

    #endregion

    #region Header Commands

    [RelayCommand]
    private async Task ShowTutorialAsync()
    {
        _logger?.Information("Deeper tutorial requested");
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_tutorial"),
            Loc.Get("msg_feature_not_implemented")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task OpenPlayerAsync()
    {
        _logger?.Information("Open Deeper player requested");
        await ShowNotImplementedAsync(Loc.Get("deeper_tab_open_player"));
    }

    [RelayCommand]
    private async Task ImportEnhancementsAsync()
    {
        _logger?.Information("Import enhancements requested");
        var files = await (_dialogService?.ShowOpenFileDialogAsync(
            Loc.Get("title_import_enhancement"),
            new[]
            {
                new FileFilter("Deeper enhancements", new[] { "ccpenh.json" }),
                new FileFilter("JSON files", new[] { "json" }),
                new FileFilter("All files", new[] { "*" })
            },
            allowMultiple: true) ?? Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));

        if (files.Count == 0) return;

        await ShowNotImplementedAsync(Loc.Get("deeper_hub_import_tooltip"));
    }

    [RelayCommand]
    private async Task NewEnhancementAsync()
    {
        _logger?.Information("New enhancement requested");

        var mainWindow = GetMainWindow();
        if (mainWindow is null) return;

        var dialog = new NewEnhancementDialog();
        var confirmed = await dialog.ShowDialog<bool>(mainWindow);
        if (!confirmed)
        {
            _logger?.Information("New enhancement dialog cancelled");
            return;
        }

        var enhancement = new Enhancement
        {
            MediaType = dialog.SelectedMediaType,
            MediaSource = dialog.SelectedSource
        };

        var editor = new DeeperEditorWindow(enhancement, null);
        editor.Show();
    }

    [RelayCommand]
    private async Task OpenCatalogueAsync()
    {
        const string url = "https://app.cclabs.app/catalogue";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            _logger?.Information("Opened Deeper catalogue URL");
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Failed to open Deeper catalogue URL");
        }
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task OpenLibraryFolderAsync()
    {
        try
        {
            _logger?.Information("Open Deeper library folder requested");
            var folder = _settingsService?.Current?.DeeperLastDirectory;
            if (!string.IsNullOrEmpty(folder))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            else
            {
                await ShowNotImplementedAsync(Loc.Get("deeper_library_open_folder_tooltip"));
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Open Deeper library folder failed");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                ex.Message,
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
    }

    #endregion

    #region Welcome Card Commands

    [RelayCommand]
    private async Task DismissWelcomeAsync()
    {
        WelcomeCardVisible = false;
        _logger?.Information("Deeper welcome card dismissed");
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task WelcomeTourAsync()
    {
        WelcomeCardVisible = false;
        _logger?.Information("Deeper welcome tour requested");
        await ShowNotImplementedAsync(Loc.Get("deeper_welcome_card_take_tour"));
    }

    [RelayCommand]
    private async Task WelcomeDemoAsync()
    {
        WelcomeCardVisible = false;
        _logger?.Information("Deeper welcome demo requested");
        await ShowNotImplementedAsync(Loc.Get("deeper_welcome_card_open_demo"));
    }

    #endregion

    #region Filter / Sort Commands

    [RelayCommand]
    private void FilterAll()
    {
        MediaTypeFilter = DeeperMediaTypeFilter.All;
    }

    [RelayCommand]
    private void FilterVideo()
    {
        MediaTypeFilter = DeeperMediaTypeFilter.Video;
    }

    [RelayCommand]
    private void FilterAudio()
    {
        MediaTypeFilter = DeeperMediaTypeFilter.Audio;
    }

    [RelayCommand]
    private void ToggleHapticsFilter()
    {
        FilterHaptics = !FilterHaptics;
    }

    [RelayCommand]
    private void ToggleWebcamFilter()
    {
        FilterWebcam = !FilterWebcam;
    }

    #endregion

    #region Webcam Commands

    [RelayCommand]
    private async Task RefreshWebcamDevicesAsync()
    {
        _logger?.Information("Refresh Deeper webcam devices requested");
        await ShowNotImplementedAsync(Loc.Get("blink_trainer_camera_refresh"));
    }

    [RelayCommand]
    private void ManageWebcamConsent()
    {
        _logger?.Information("Manage Deeper webcam consent requested");
        IsWebcamConsentGranted = true;
    }

    [RelayCommand]
    private void RevokeWebcamConsent()
    {
        _logger?.Information("Revoke Deeper webcam consent requested");
        IsWebcamConsentGranted = false;
        IsTrackerRunning = false;
    }

    [RelayCommand]
    private async Task CalibrateWebcamAsync()
    {
        _logger?.Information("Calibrate Deeper webcam requested");
        if (!IsWebcamConsentGranted)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_not_implemented"),
                Loc.Get("deeper_webcam_consent_missing")) ?? Task.CompletedTask);
            return;
        }
        WebcamCalibrationStatusText = Loc.GetF("blink_trainer_calibration_calibrated_format", Loc.Get("blink_trainer_section_webcam"));
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task QuickRecalWebcamAsync()
    {
        _logger?.Information("Quick recal Deeper webcam requested");
        await CalibrateWebcamAsync();
    }

    [RelayCommand]
    private async Task StartStopTrackerAsync()
    {
        _logger?.Information("Deeper webcam tracker toggle requested");
        if (!IsWebcamConsentGranted)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_not_implemented"),
                Loc.Get("deeper_webcam_consent_missing")) ?? Task.CompletedTask);
            return;
        }
        IsTrackerRunning = !IsTrackerRunning;
        await Task.CompletedTask;
    }

    #endregion

    #region Filtering / Sorting

    private void ApplyFilterAndSort()
    {
        try
        {
            var needle = (SearchText ?? "").Trim();
            var filtered = _allEntries
                .Where(e => EntryMatchesSearch(e, needle))
                .Where(e => MediaTypeFilter == DeeperMediaTypeFilter.All || e.MediaType == MediaTypeFilter.ToString().ToLowerInvariant())
                .Where(e => !FilterHaptics || e.HasHapticsTag)
                .Where(e => !FilterWebcam || e.HasWebcamTag);

            filtered = SelectedSortIndex switch
            {
                1 => filtered.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
                2 => filtered.OrderBy(e => e.Creator, StringComparer.OrdinalIgnoreCase),
                _ => filtered.OrderByDescending(e => e.LastModified)
            };

            var list = filtered.ToList();
            FilteredEntries.Clear();
            foreach (var vm in list) FilteredEntries.Add(vm);

            OnPropertyChanged(nameof(LibraryCountText));
            OnPropertyChanged(nameof(IsLibraryEmpty));
            OnPropertyChanged(nameof(LibraryEmptyText));
            OnPropertyChanged(nameof(AllCount));
            OnPropertyChanged(nameof(VideoCount));
            OnPropertyChanged(nameof(AudioCount));
            OnPropertyChanged(nameof(HapticsCount));
            OnPropertyChanged(nameof(WebcamCount));
        }
        catch (Exception ex)
        {
            _logger?.Warning("ApplyDeeperFilterAndSort error: {Error}", ex.Message);
        }
    }

    private static bool EntryMatchesSearch(DeeperLibraryRowViewModel e, string needle)
    {
        if (string.IsNullOrEmpty(needle)) return true;
        if (e.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        if (e.Creator.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        return e.Tags.Any(t => t.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Helpers

    private async Task ShowNotImplementedAsync(string featureName)
    {
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            string.Format(Loc.Get("msg_not_implemented_body_fmt"), featureName)) ?? Task.CompletedTask);
    }

    private void RefreshWebcamUi()
    {
        if (IsWebcamConsentGranted)
        {
            WebcamConsentStatusText = Loc.Get("deeper_webcam_consent_granted");
            WebcamManageConsentButtonText = Loc.Get("deeper_webcam_manage_consent");
        }
        else
        {
            WebcamConsentStatusText = Loc.Get("deeper_webcam_consent_missing");
            WebcamManageConsentButtonText = Loc.Get("deeper_webcam_grant_consent");
            IsTrackerRunning = false;
        }
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }

    private void LoadDesignTimeData()
    {
        WelcomeCardVisible = true;
        MediaTypeFilter = DeeperMediaTypeFilter.All;

        WebcamDevices.Add("Integrated Camera");
        WebcamDevices.Add("USB Webcam");
        SelectedWebcamDevice = WebcamDevices[0];

        Monitors.Add("Primary");
        Monitors.Add("Secondary");
        SelectedMonitor = Monitors[0];

        IsWebcamConsentGranted = true;
        WebcamCalibrationStatusText = Loc.Get("blink_trainer_calibration_none");

        var videoEntry = new Enhancement
        {
            MediaType = MediaTypes.Video,
            MediaSource = "https://app.cclabs.app/catalogue/demo",
            Metadata = new EnhancementMetadata
            {
                Name = "Welcome to Deeper demo",
                Creator = "cclabs",
                Tags = new() { "beginner", "demo" }
            }
        };

        var audioEntry = new Enhancement
        {
            MediaType = MediaTypes.Audio,
            MediaSource = "C:\\Deeper\\purr-loop.mp3",
            Metadata = new EnhancementMetadata
            {
                Name = "Purr loop",
                Creator = "local-user",
                Tags = new() { "audio", "loop" }
            }
        };

        var now = DateTime.Now;
        _allEntries.Add(CreateRow(videoEntry, "Published", true, now.AddHours(-2)));
        _allEntries.Add(CreateRow(audioEntry, "", false, now.AddDays(-1)));

        ApplyFilterAndSort();
    }

    private DeeperLibraryRowViewModel CreateRow(Enhancement entry, string submissionStatus, bool isCatalogueEligible, DateTime lastModified)
    {
        var row = new DeeperLibraryRowViewModel(_dialogService, _logger)
        {
            Entry = entry,
            FilePath = entry.MediaSource,
            Name = entry.Metadata.Name,
            Creator = entry.Metadata.Creator,
            MediaType = entry.MediaType,
            MediaSource = entry.MediaSource,
            LastModified = lastModified,
            SubmissionStatus = submissionStatus,
            IsCatalogueEligible = isCatalogueEligible,
            CanSubmit = isCatalogueEligible,
            TimestampDisplay = FormatRelativeTime(lastModified),
            ShowCreator = !string.IsNullOrWhiteSpace(entry.Metadata.Creator),
            ShowMediaSource = true,
            ShowTimestamp = true,
            ShowSubmitButton = isCatalogueEligible,
            SubmitEnabled = isCatalogueEligible,
            SubmitTooltip = Loc.Get("deeper_library_submit_tooltip")
        };

        row.CreatorDisplay = row.ShowCreator
            ? string.Format(Loc.Get("deeper_library_creator_fmt"), entry.Metadata.Creator)
            : "";

        if (entry.MediaType == MediaTypes.Audio)
        {
            row.MediaTypeBadgeBg = "#33FF69B4";
            row.MediaTypeIcon = "🎵";
        }
        else
        {
            row.MediaTypeBadgeBg = "#337B5CFF";
            row.MediaTypeIcon = "🎬";
        }

        if (entry.MediaSource.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            row.MediaSourceGlyph = "🌐";
            row.MediaSourceLabel = Loc.Get("deeper_hub_catalogue_btn");
            row.MediaSourceBrush = "#FF7B5CFF";
        }
        else
        {
            row.MediaSourceGlyph = "💾";
            row.MediaSourceLabel = Path.GetFileName(entry.MediaSource);
            row.MediaSourceBrush = "#FFAAAAAA";
        }

        foreach (var tag in entry.Metadata.Tags)
        {
            row.Tags.Add(tag);
        }

        // Hardware auto-tags inferred from project contents.
        row.HasHapticsTag = entry.HapticTracks.Any();
        row.HasWebcamTag = false; // Would be inferred from triggers in a real implementation.

        if (row.HasHapticsTag)
        {
            row.TagBadges.Add(new DeeperLibraryTagViewModel
            {
                Glyph = "📳",
                Label = Loc.Get("deeper_library_autotag_haptics"),
                Background = "#33FF69B4",
                Foreground = "#FFFFFFFF"
            });
        }

        if (row.HasWebcamTag)
        {
            row.TagBadges.Add(new DeeperLibraryTagViewModel
            {
                Glyph = "📷",
                Label = Loc.Get("deeper_library_autotag_webcam"),
                Background = "#337B5CFF",
                Foreground = "#FFFFFFFF"
            });
        }

        foreach (var tag in entry.Metadata.Tags)
        {
            row.TagBadges.Add(new DeeperLibraryTagViewModel
            {
                Glyph = "🏷",
                Label = tag,
                Background = "#33252540",
                Foreground = "#FFAAAAAA"
            });
        }

        row.ShowTags = row.TagBadges.Any();

        if (!string.IsNullOrWhiteSpace(submissionStatus))
        {
            row.ShowSubmissionBadge = true;
            row.SubmissionBadgeLabel = submissionStatus;
            row.SubmissionBadgeGlyph = submissionStatus switch
            {
                "Published" => "✓",
                "Pending" => "⏳",
                "Rejected" => "✕",
                _ => "•"
            };
            row.SubmissionBadgeTooltip = submissionStatus switch
            {
                "Published" => Loc.Get("deeper_submission_badge_published_tip"),
                "Pending" => Loc.Get("deeper_submission_badge_pending_tip"),
                "Rejected" => Loc.Get("deeper_submission_badge_rejected_tip"),
                _ => ""
            };
            row.SubmissionBadgeBg = submissionStatus switch
            {
                "Published" => "#1A4ADE80",
                "Pending" => "#33FFD166",
                "Rejected" => "#33FF6060",
                _ => "#33252540"
            };
            row.SubmissionBadgeFg = submissionStatus switch
            {
                "Published" => "#FF4ADE80",
                "Pending" => "#FFFFFFFF",
                "Rejected" => "#FFFF6060",
                _ => "#FFFFFFFF"
            };
        }

        return row;
    }

    private static string FormatRelativeTime(DateTime dt)
    {
        var diff = DateTime.Now - dt;
        if (diff.TotalMinutes < 1) return Loc.Get("deeper_hub_time_just_now");
        if (diff.TotalHours < 1) return Loc.GetF("deeper_hub_time_minutes_ago", (int)diff.TotalMinutes);
        if (diff.TotalDays < 1) return Loc.GetF("deeper_hub_time_hours_ago", (int)diff.TotalHours);
        if (diff.TotalDays < 7) return Loc.GetF("deeper_hub_time_days_ago", (int)diff.TotalDays);
        if (diff.TotalDays < 30) return Loc.GetF("deeper_hub_time_weeks_ago", (int)(diff.TotalDays / 7));
        if (diff.TotalDays < 365) return Loc.GetF("deeper_hub_time_months_ago", (int)(diff.TotalDays / 30));
        return Loc.GetF("deeper_hub_time_years_ago", (int)(diff.TotalDays / 365));
    }

    #endregion
}
