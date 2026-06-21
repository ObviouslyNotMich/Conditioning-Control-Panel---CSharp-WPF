using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.DeeperHub partial.
/// Filter / sort / search plumbing and per-row view-model projection for the
/// Deeper library hub. WPF-only services are stubbed with TODOs.
/// </summary>
public partial class DeeperHubTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly IAppLogger? _logger;

    public DeeperHubTabViewModel() : base("deeperhub", "Deeper Hub", "🌊")
    {
        _allEntries = new ObservableCollection<DeeperLibraryRowViewModel>();
        _filteredEntries = new ObservableCollection<DeeperLibraryRowViewModel>();
    }

    public DeeperHubTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        IAppLogger logger) : base("deeperhub", "Deeper Hub", "🌊")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;
        _allEntries = new ObservableCollection<DeeperLibraryRowViewModel>();
        _filteredEntries = new ObservableCollection<DeeperLibraryRowViewModel>();
    }

    public enum DeeperMediaTypeFilter { All, Video, Audio }
    public enum DeeperSortMode { Recent, Name, Creator }

    [ObservableProperty]
    private ObservableCollection<DeeperLibraryRowViewModel> _allEntries;

    [ObservableProperty]
    private ObservableCollection<DeeperLibraryRowViewModel> _filteredEntries;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private DeeperMediaTypeFilter _mediaTypeFilter = DeeperMediaTypeFilter.All;

    [ObservableProperty]
    private bool _filterHaptics;

    [ObservableProperty]
    private bool _filterWebcam;

    [ObservableProperty]
    private DeeperSortMode _sortMode = DeeperSortMode.Recent;

    [ObservableProperty]
    private int _allCount;

    [ObservableProperty]
    private int _videoCount;

    [ObservableProperty]
    private int _audioCount;

    [ObservableProperty]
    private int _hapticsCount;

    [ObservableProperty]
    private int _webcamCount;

    [ObservableProperty]
    private string _emptyStateText = Loc.Get("deeper_library_empty");

    [ObservableProperty]
    private string _libraryCountText = string.Format(CultureInfo.InvariantCulture, Loc.Get("deeper_library_count_fmt"), 0);

    [ObservableProperty]
    private bool _isBusy;

    partial void OnSearchTextChanged(string value) => ApplyFilterAndSort();
    partial void OnMediaTypeFilterChanged(DeeperMediaTypeFilter value) => ApplyFilterAndSort();
    partial void OnFilterHapticsChanged(bool value) => ApplyFilterAndSort();
    partial void OnFilterWebcamChanged(bool value) => ApplyFilterAndSort();
    partial void OnSortModeChanged(DeeperSortMode value) => ApplyFilterAndSort();

    [RelayCommand]
    private async Task ReloadLibraryAsync()
    {
        IsBusy = true;
        try
        {
            _logger?.Information("Reloading Deeper library");
            // TODO: wire to IDeeperEnhancementLibrary.ScanLibrary() once it is extracted to CCP.Core.
            AllEntries.Clear();
            await Task.Delay(50); // simulated async load
            ApplyFilterAndSort();
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Reload Deeper library failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SetMediaTypeFilter(string? filter)
    {
        MediaTypeFilter = filter?.ToLowerInvariant() switch
        {
            "video" => DeeperMediaTypeFilter.Video,
            "audio" => DeeperMediaTypeFilter.Audio,
            _ => DeeperMediaTypeFilter.All
        };
    }

    [RelayCommand]
    private void ToggleHapticsFilter() => FilterHaptics = !FilterHaptics;

    [RelayCommand]
    private void ToggleWebcamFilter() => FilterWebcam = !FilterWebcam;

    [RelayCommand]
    private void SetSortMode(string? mode)
    {
        SortMode = mode?.ToLowerInvariant() switch
        {
            "name" => DeeperSortMode.Name,
            "creator" => DeeperSortMode.Creator,
            _ => DeeperSortMode.Recent
        };
    }

    [RelayCommand]
    private async Task OpenEntryAsync(DeeperLibraryRowViewModel? row)
    {
        if (row == null) return;
        _logger?.Information("Open Deeper entry: {Path}", row.FilePath);
        // TODO: wire to IDeeperEnhancementLibrary.Open() and Deeper editor window.
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            Loc.Get("msg_deeper_editor_not_yet_ported")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task PlayEntryAsync(DeeperLibraryRowViewModel? row)
    {
        if (row == null) return;
        _logger?.Information("Play Deeper entry: {Path}", row.FilePath);
        // TODO: wire to IDeeperPlayerHost.LoadEnhancementFile() once ported.
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            Loc.Get("msg_deeper_player_not_yet_ported")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task DeleteEntryAsync(DeeperLibraryRowViewModel? row)
    {
        if (row == null) return;

        var confirm = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("deeper_library_delete_title"),
            string.Format(Loc.Get("deeper_library_delete_confirm_fmt"), row.Name)) ?? Task.FromResult(false));
        if (!confirm) return;

        try
        {
            _logger?.Information("Delete Deeper entry: {Path}", row.FilePath);
            // TODO: wire to file system via IDeeperEnhancementLibrary once ported.
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_not_implemented"),
                Loc.Get("msg_deeper_delete_not_yet_ported")) ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Delete Deeper entry failed");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                ex.Message,
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task SubmitEntryAsync(DeeperLibraryRowViewModel? row)
    {
        if (row == null) return;
        if (string.IsNullOrEmpty(_settingsService?.Current?.AuthToken))
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("catalogue_toast_auth_failed"),
                Loc.Get("msg_catalogue_auth_required"),
                DialogSeverity.Warning) ?? Task.CompletedTask);
            return;
        }

        var confirm = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("dialog_submit_catalogue"),
            string.Format(Loc.Get("msg_submit_catalogue_confirm_fmt"), row.Name)) ?? Task.FromResult(false));
        if (!confirm) return;

        _logger?.Information("Submit Deeper entry to catalogue: {Path}", row.FilePath);
        // TODO: wire to ICatalogueService.SubmitEnhancementAsync() once it is extracted to CCP.Core.
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            Loc.Get("msg_catalogue_submit_not_yet_ported")) ?? Task.CompletedTask);
    }

    private void ApplyFilterAndSort()
    {
        try
        {
            var needle = (SearchText ?? "").Trim();
            var filtered = AllEntries
                .Where(e => EntryMatchesSearch(e, needle))
                .Where(e => MediaTypeFilter == DeeperMediaTypeFilter.All || e.MediaType == MediaTypeFilter.ToString().ToLowerInvariant())
                .Where(e => !FilterHaptics || e.HasHapticsTag)
                .Where(e => !FilterWebcam || e.HasWebcamTag);

            filtered = SortMode switch
            {
                DeeperSortMode.Name => filtered.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase),
                DeeperSortMode.Creator => filtered.OrderBy(e => e.Creator, StringComparer.OrdinalIgnoreCase),
                _ => filtered.OrderByDescending(e => e.LastModified)
            };

            var list = filtered.ToList();
            FilteredEntries.Clear();
            foreach (var vm in list) FilteredEntries.Add(vm);

            UpdatePillCounts(needle);
            UpdateEmptyState(list.Count, AllEntries.Count);
            LibraryCountText = string.Format(CultureInfo.InvariantCulture, Loc.Get("deeper_library_count_fmt"), AllEntries.Count);
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

    private void UpdatePillCounts(string needle)
    {
        var searched = AllEntries.Where(e => EntryMatchesSearch(e, needle)).ToList();

        AllCount = searched.Count();
        VideoCount = searched.Count(e => e.MediaType == "video");
        AudioCount = searched.Count(e => e.MediaType == "audio");
        HapticsCount = searched.Count(e => e.HasHapticsTag);
        WebcamCount = searched.Count(e => e.HasWebcamTag);
    }

    private void UpdateEmptyState(int filteredCount, int totalCount)
    {
        if (totalCount == 0)
        {
            EmptyStateText = Loc.Get("deeper_library_empty");
        }
        else if (filteredCount == 0)
        {
            EmptyStateText = Loc.Get("deeper_hub_empty_filtered");
        }
    }
}

/// <summary>
/// Per-row view model for the Deeper library hub.
/// </summary>
public partial class DeeperLibraryRowViewModel : ObservableObject
{
    private readonly IDialogService? _dialogService;
    private readonly IAppLogger? _logger;

    public DeeperLibraryRowViewModel()
    {
    }

    public DeeperLibraryRowViewModel(IDialogService? dialogService, IAppLogger? logger)
    {
        _dialogService = dialogService;
        _logger = logger;
    }

    /// <summary>
    /// Backing enhancement model, if any. Commands use this to open the editor/player.
    /// </summary>
    public Enhancement? Entry { get; set; }

    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _creator = "";

    [ObservableProperty]
    private string _mediaType = "";

    [ObservableProperty]
    private string _mediaSource = "";

    [ObservableProperty]
    private DateTime _lastModified;

    [ObservableProperty]
    private ObservableCollection<string> _tags = new();

    [ObservableProperty]
    private bool _hasHapticsTag;

    [ObservableProperty]
    private bool _hasWebcamTag;

    [ObservableProperty]
    private string _submissionStatus = "";

    [ObservableProperty]
    private bool _isCatalogueEligible;

    [ObservableProperty]
    private bool _canSubmit;

    [ObservableProperty]
    private string _mediaSourceLabel = "";

    [ObservableProperty]
    private string _mediaSourceGlyph = "";

    [ObservableProperty]
    private string _timestampDisplay = "";

    // Extended properties used by the richer DeeperTabView row template.

    [ObservableProperty]
    private string _mediaTypeBadgeBg = "#00000000";

    [ObservableProperty]
    private string _mediaTypeIcon = "";

    [ObservableProperty]
    private bool _showSubmissionBadge;

    [ObservableProperty]
    private string _submissionBadgeBg = "#00000000";

    [ObservableProperty]
    private string _submissionBadgeFg = "#FFFFFFFF";

    [ObservableProperty]
    private string _submissionBadgeGlyph = "";

    [ObservableProperty]
    private string _submissionBadgeLabel = "";

    [ObservableProperty]
    private string _submissionBadgeTooltip = "";

    [ObservableProperty]
    private string _creatorDisplay = "";

    [ObservableProperty]
    private bool _showCreator;

    [ObservableProperty]
    private bool _showMediaSource = true;

    [ObservableProperty]
    private string _mediaSourceBrush = "#FFFFFFFF";

    [ObservableProperty]
    private bool _showTags;

    [ObservableProperty]
    private ObservableCollection<DeeperLibraryTagViewModel> _tagBadges = new();

    [ObservableProperty]
    private bool _showTimestamp = true;

    [ObservableProperty]
    private bool _showSubmitButton = true;

    [ObservableProperty]
    private bool _submitEnabled = true;

    [ObservableProperty]
    private string _submitTooltip = "";

    [RelayCommand]
    private async Task OpenAsync()
    {
        _logger?.Information("Open Deeper row: {Name}", Name);
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            string.Format(Loc.Get("msg_not_implemented_body_fmt"), Loc.Get("deeper_hub_tip_row_open"))) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task PlayAsync()
    {
        _logger?.Information("Play Deeper row: {Name}", Name);
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            string.Format(Loc.Get("msg_not_implemented_body_fmt"), Loc.Get("deeper_hub_tip_row_play"))) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        _logger?.Information("Delete Deeper row: {Name}", Name);
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            string.Format(Loc.Get("msg_not_implemented_body_fmt"), Loc.Get("deeper_hub_tip_row_delete"))) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        _logger?.Information("Submit Deeper row to catalogue: {Name}", Name);
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            string.Format(Loc.Get("msg_not_implemented_body_fmt"), Loc.Get("deeper_library_submit_tooltip"))) ?? Task.CompletedTask);
    }
}
