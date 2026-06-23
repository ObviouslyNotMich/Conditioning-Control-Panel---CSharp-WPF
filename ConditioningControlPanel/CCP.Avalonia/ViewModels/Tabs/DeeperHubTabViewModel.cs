using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Catalogue;
using ConditioningControlPanel.Core.Services.Deeper;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.DeeperHub partial.
/// Filter / sort / search plumbing and per-row view-model projection for the
/// Deeper library hub.
/// </summary>
public partial class DeeperHubTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly ILogger<DeeperHubTabViewModel>? _logger;
    private readonly ILogger<DeeperLibraryRowViewModel>? _rowLogger;
    private readonly ICatalogueService? _catalogueService;

    public DeeperHubTabViewModel() : base("deeperhub", "Deeper Hub", "🌊")
    {
        _allEntries = new ObservableCollection<DeeperLibraryRowViewModel>();
        _filteredEntries = new ObservableCollection<DeeperLibraryRowViewModel>();
    }

    public DeeperHubTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        ILogger<DeeperHubTabViewModel> logger,
        ICatalogueService catalogueService,
        ILogger<DeeperLibraryRowViewModel> rowLogger) : base("deeperhub", "Deeper Hub", "🌊")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;
        _rowLogger = rowLogger;
        _catalogueService = catalogueService;
        _allEntries = new ObservableCollection<DeeperLibraryRowViewModel>();
        _filteredEntries = new ObservableCollection<DeeperLibraryRowViewModel>();
    }

    public override void OnSelected()
    {
        base.OnSelected();
        if (_allEntries.Count == 0)
        {
            _ = ReloadLibraryAsync();
        }
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
            _logger?.LogInformation("Reloading Deeper library");
            AllEntries.Clear();

            var folder = _settingsService?.Current?.DeeperLastDirectory;
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                var files = Directory.EnumerateFiles(folder, "*.ccpenh.json", SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    try
                    {
                        var enh = EnhancementSerializer.LoadFromFile(file);
                        var issues = EnhancementValidator.Validate(enh);
                        if (issues.Any(i => i.Severity == ValidationSeverity.Error))
                        {
                            _logger?.LogWarning("Skipping invalid enhancement {File}: {Issues}", file,
                                string.Join(", ", issues.Where(i => i.Severity == ValidationSeverity.Error).Select(i => i.Message)));
                            continue;
                        }

                        var lastModified = File.GetLastWriteTime(file);
                        AllEntries.Add(BuildRowViewModel(enh, file, lastModified));
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to scan enhancement {File}", file);
                    }
                }
            }

            ApplyFilterAndSort();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Reload Deeper library failed");
        }
        finally
        {
            IsBusy = false;
        }

        await Task.CompletedTask;
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
        var entry = row.Entry;
        if (entry == null)
        {
            _logger?.LogWarning("Open Deeper entry has no backing model: {Path}", row.FilePath);
            return;
        }

        _logger?.LogInformation("Open Deeper entry: {Path}", row.FilePath);
        try
        {
            var editor = new DeeperEditorWindow(entry, row.FilePath);
            editor.Show();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Open Deeper editor failed");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                ex.Message,
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task PlayEntryAsync(DeeperLibraryRowViewModel? row)
    {
        if (row == null) return;
        var entry = row.Entry;
        if (entry == null)
        {
            _logger?.LogWarning("Play Deeper entry has no backing model: {Path}", row.FilePath);
            return;
        }

        _logger?.LogInformation("Play Deeper entry: {Path}", row.FilePath);
        try
        {
            var player = new EnhancementPlayerWindow(entry, "hub");
            player.Show();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Open Deeper player failed");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                ex.Message,
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
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
            _logger?.LogInformation("Delete Deeper entry: {Path}", row.FilePath);
            if (File.Exists(row.FilePath))
            {
                File.Delete(row.FilePath);
            }

            AllEntries.Remove(row);
            ApplyFilterAndSort();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Delete Deeper entry failed");
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

        var label = string.IsNullOrEmpty(row.Name) ? Path.GetFileName(row.FilePath) : row.Name;
        bool confirmed;
        var mainWindow = GetMainWindow();
        if (mainWindow != null)
        {
            var dialog = new CatalogueSubmitDialog(label);
            confirmed = await dialog.ShowDialog<bool>(mainWindow);
        }
        else
        {
            confirmed = await (_dialogService?.ShowConfirmationAsync(
                Loc.Get("dialog_submit_catalogue"),
                string.Format(Loc.Get("msg_submit_catalogue_confirm_fmt"), row.Name)) ?? Task.FromResult(false));
        }
        if (!confirmed) return;

        _logger?.LogInformation("Submit Deeper entry to catalogue: {Path}", row.FilePath);
        IsBusy = true;
        try
        {
            var result = await (_catalogueService?.SubmitEnhancementAsync(row.FilePath, default)
                ?? Task.FromResult<SubmissionResult>(new SubmissionResult.UnknownError(0, "service_unavailable")));
            RecordDeeperSubmission(row.FilePath, result);
            ApplyPendingSubmissionBadge(row);
            await ShowCatalogueSubmissionResultAsync(result);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[Catalogue] Submit threw unexpectedly");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("catalogue_toast_unknown_error"),
                DialogSeverity.Error) ?? Task.CompletedTask);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OnRowDeleteRequestedAsync(DeeperLibraryRowViewModel row)
    {
        var label = string.IsNullOrEmpty(row.Name) ? Path.GetFileName(row.FilePath) : row.Name;
        var confirmed = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("deeper_library_delete_title"),
            string.Format(Loc.Get("deeper_library_delete_confirm_fmt"), label)) ?? Task.FromResult(false));
        if (!confirmed) return;

        try
        {
            _logger?.LogInformation("Deleting Deeper library entry {Path}", row.FilePath);
            if (File.Exists(row.FilePath))
                File.Delete(row.FilePath);

            _allEntries.Remove(row);
            ApplyFilterAndSort();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to delete Deeper library entry {Path}", row.FilePath);
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                ex.Message,
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
    }

    private async Task OnRowSubmitRequestedAsync(DeeperLibraryRowViewModel row)
    {
        await SubmitEntryAsync(row);
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    private static bool IsAcceptedStatus(string? status) =>
        string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "published", StringComparison.OrdinalIgnoreCase);

    private static string CanonicalSubmissionKey(string filePath)
    {
        try { return Path.GetFullPath(filePath); }
        catch { return filePath; }
    }

    private void RecordDeeperSubmission(string filePath, SubmissionResult result)
    {
        try
        {
            string id;
            string status;
            switch (result)
            {
                case SubmissionResult.Success s:
                    id = s.Id;
                    status = string.IsNullOrEmpty(s.Status) ? "pending" : s.Status;
                    break;
                case SubmissionResult.Duplicate d:
                    id = d.ExistingId;
                    status = string.IsNullOrEmpty(d.ExistingStatus) ? "pending" : d.ExistingStatus;
                    break;
                default:
                    return;
            }

            if (string.IsNullOrEmpty(id)) return;
            var settings = _settingsService?.Current;
            if (settings == null) return;

            var key = CanonicalSubmissionKey(filePath);
            settings.DeeperSubmissions.TryGetValue(key, out var existing);
            var rec = existing ?? new DeeperSubmissionRecord { SubmittedUtc = DateTime.UtcNow };
            rec.CatalogueId = id;
            rec.Status = status;
            rec.LastCheckedUtc = DateTime.UtcNow;
            if (IsAcceptedStatus(status)) rec.AcceptedNotified = true;

            settings.DeeperSubmissions[key] = rec;
            _settingsService?.Save();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("[Catalogue] RecordDeeperSubmission failed: {Error}", ex.Message);
        }
    }

    private static void ApplyPendingSubmissionBadge(DeeperLibraryRowViewModel row)
    {
        row.SubmissionStatus = "Pending";
        row.ShowSubmissionBadge = true;
        row.SubmissionBadgeGlyph = "⏳";
        row.SubmissionBadgeLabel = Loc.Get("deeper_submission_badge_pending");
        row.SubmissionBadgeTooltip = Loc.Get("deeper_submission_badge_pending_tip");
        row.SubmissionBadgeBg = "#33FFD166";
        row.SubmissionBadgeFg = "#FFFFFFFF";
    }

    private async Task ShowCatalogueSubmissionResultAsync(SubmissionResult result)
    {
        switch (result)
        {
            case SubmissionResult.Success:
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("title_success"),
                    Loc.Get("catalogue_toast_success"),
                    DialogSeverity.Info) ?? Task.CompletedTask);
                break;

            case SubmissionResult.Duplicate d:
            {
                var key = d.ExistingStatus switch
                {
                    "approved" => "catalogue_toast_duplicate_approved",
                    "rejected" => "catalogue_toast_duplicate_rejected",
                    _ => "catalogue_toast_duplicate_pending"
                };
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("title_info"),
                    Loc.Get(key),
                    DialogSeverity.Info) ?? Task.CompletedTask);
                break;
            }

            case SubmissionResult.ValidationError v:
            {
                var key = v.ErrorCode switch
                {
                    "missing_title" => "catalogue_toast_error_missing_title",
                    "missing_creator" => "catalogue_toast_error_missing_creator",
                    "invalid_media_source" => "catalogue_toast_error_invalid_media_source",
                    "invalid_schema" => "catalogue_toast_error_invalid_schema",
                    "file_too_large" => "catalogue_toast_error_file_too_large",
                    "stale_guidelines_version" => "catalogue_toast_error_stale_guidelines",
                    _ => ""
                };
                var msg = !string.IsNullOrEmpty(key)
                    ? Loc.Get(key)
                    : Loc.GetF("catalogue_toast_error_generic_fmt", v.ErrorCode);
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("title_warning"),
                    msg,
                    DialogSeverity.Warning) ?? Task.CompletedTask);
                break;
            }

            case SubmissionResult.AuthFailed:
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("catalogue_toast_auth_failed"),
                    Loc.Get("msg_catalogue_auth_required"),
                    DialogSeverity.Warning) ?? Task.CompletedTask);
                break;

            case SubmissionResult.TooLarge:
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("title_error"),
                    Loc.Get("catalogue_toast_too_large"),
                    DialogSeverity.Error) ?? Task.CompletedTask);
                break;

            case SubmissionResult.RateLimited r:
            {
                string msg;
                if (r.RetryAfterSeconds.HasValue && r.RetryAfterSeconds.Value > 0)
                {
                    var minutes = Math.Max(1, (int)Math.Ceiling(r.RetryAfterSeconds.Value / 60.0));
                    msg = Loc.GetF("catalogue_toast_rate_limited_minutes_fmt", minutes);
                }
                else
                {
                    msg = Loc.Get("catalogue_toast_rate_limited_unknown");
                }
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("title_warning"),
                    msg,
                    DialogSeverity.Warning) ?? Task.CompletedTask);
                break;
            }

            case SubmissionResult.UnknownError:
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("title_error"),
                    Loc.Get("catalogue_toast_unknown_error"),
                    DialogSeverity.Error) ?? Task.CompletedTask);
                break;
        }
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
            _logger?.LogWarning("ApplyDeeperFilterAndSort error: {Error}", ex.Message);
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

    private DeeperLibraryRowViewModel BuildRowViewModel(Enhancement entry, string filePath, DateTime lastModified)
    {
        var hasAuth = !string.IsNullOrEmpty(_settingsService?.Current?.AuthToken);
        var isEligible = IsCatalogueEligible(entry);
        var (mediaLabel, _, mediaBrush) = ResolveMediaSourceDisplay(entry.MediaSource);

        var row = new DeeperLibraryRowViewModel(_dialogService, _rowLogger)
        {
            Entry = entry,
            FilePath = filePath,
            Name = entry.Metadata.Name ?? Path.GetFileNameWithoutExtension(filePath),
            Creator = entry.Metadata.Creator ?? "",
            MediaType = (entry.MediaType ?? "").ToLowerInvariant(),
            MediaSource = entry.MediaSource ?? "",
            LastModified = lastModified,
            IsCatalogueEligible = isEligible,
            CanSubmit = isEligible && hasAuth,
            ShowSubmitButton = isEligible,
            SubmitEnabled = isEligible && hasAuth,
            SubmitTooltip = Loc.Get(hasAuth
                ? "deeper_library_submit_tooltip"
                : "deeper_library_submit_button_disabled_tooltip"),
            MediaSourceLabel = mediaLabel,
            MediaSourceBrush = mediaBrush,
            TimestampDisplay = FormatRelativeTime(lastModified),
            ShowCreator = !string.IsNullOrWhiteSpace(entry.Metadata.Creator),
            ShowTimestamp = lastModified != default,
            ShowMediaSource = true
        };

        if (entry.Metadata.Tags != null)
        {
            foreach (var tag in entry.Metadata.Tags)
            {
                if (!string.IsNullOrWhiteSpace(tag))
                    row.Tags.Add(tag);
            }
        }

        row.HasHapticsTag = entry.HapticTracks?.Any() == true;
        row.HasWebcamTag = false; // inferred from triggers in a real implementation
        row.ShowTags = row.Tags.Any();

        row.DeleteRequestedAsync = OnRowDeleteRequestedAsync;
        row.SubmitRequestedAsync = OnRowSubmitRequestedAsync;

        return row;
    }

    private static bool IsCatalogueEligible(Enhancement entry)
    {
        if (entry == null) return false;
        if (!string.Equals(entry.MediaType, "video", StringComparison.OrdinalIgnoreCase)) return false;
        return HtUrlHelper.IsEligibleHtUrl(entry.MediaSource);
    }

    private static (string label, string glyph, string brush) ResolveMediaSourceDisplay(string? mediaSource)
    {
        if (string.IsNullOrEmpty(mediaSource))
            return ("", "", "TextDimBrush");

        if (mediaSource.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            mediaSource.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            string host;
            try { host = new Uri(mediaSource).Host; }
            catch { host = mediaSource; }
            return (host, "🌐", "DeeperAccentBrush");
        }

        bool exists = false;
        try { exists = File.Exists(mediaSource); } catch { }
        var name = Path.GetFileName(mediaSource);
        if (string.IsNullOrEmpty(name)) name = mediaSource;
        return (name, exists ? "✓" : "⚠", exists ? "DeeperAccentBrush" : "TextMutedBrush");
    }

    private static string FormatRelativeTime(DateTime when)
    {
        if (when == default) return "";
        var diff = DateTime.Now - when;
        if (diff.TotalMinutes < 1) return Loc.Get("deeper_hub_time_just_now");
        if (diff.TotalMinutes < 60) return string.Format(CultureInfo.InvariantCulture, Loc.Get("deeper_hub_time_minutes_ago"), (int)diff.TotalMinutes);
        if (diff.TotalHours < 24) return string.Format(CultureInfo.InvariantCulture, Loc.Get("deeper_hub_time_hours_ago"), (int)diff.TotalHours);
        if (diff.TotalDays < 7) return string.Format(CultureInfo.InvariantCulture, Loc.Get("deeper_hub_time_days_ago"), (int)diff.TotalDays);
        if (diff.TotalDays < 31) return string.Format(CultureInfo.InvariantCulture, Loc.Get("deeper_hub_time_weeks_ago"), (int)(diff.TotalDays / 7));
        if (diff.TotalDays < 365) return string.Format(CultureInfo.InvariantCulture, Loc.Get("deeper_hub_time_months_ago"), (int)(diff.TotalDays / 30));
        return string.Format(CultureInfo.InvariantCulture, Loc.Get("deeper_hub_time_years_ago"), (int)(diff.TotalDays / 365));
    }
}

/// <summary>
/// Per-row view model for the Deeper library hub.
/// </summary>
public partial class DeeperLibraryRowViewModel : ObservableObject
{
    private readonly IDialogService? _dialogService;
    private readonly ILogger<DeeperLibraryRowViewModel>? _logger;

    public DeeperLibraryRowViewModel()
    {
    }

    public DeeperLibraryRowViewModel(IDialogService? dialogService, ILogger<DeeperLibraryRowViewModel>? logger)
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

    /// <summary>Called by the row to request deletion from the parent list and disk.</summary>
    public Func<DeeperLibraryRowViewModel, Task>? DeleteRequestedAsync { get; set; }

    /// <summary>Called by the row to request catalogue submission through the parent.</summary>
    public Func<DeeperLibraryRowViewModel, Task>? SubmitRequestedAsync { get; set; }

    [RelayCommand]
    private async Task OpenAsync()
    {
        _logger?.LogInformation("Open Deeper row: {Name}", Name);
        var entry = Entry;
        if (entry == null)
        {
            _logger?.LogWarning("Open Deeper row has no backing model: {Name}", Name);
            return;
        }

        try
        {
            var editor = new DeeperEditorWindow(entry, FilePath);
            editor.Show();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Open Deeper editor failed");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                ex.Message,
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task PlayAsync()
    {
        _logger?.LogInformation("Play Deeper row: {Name}", Name);
        var entry = Entry;
        if (entry == null)
        {
            _logger?.LogWarning("Play Deeper row has no backing model: {Name}", Name);
            return;
        }

        try
        {
            var player = new EnhancementPlayerWindow(entry, "hub");
            player.Show();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Open Deeper player failed");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                ex.Message,
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        _logger?.LogInformation("Delete Deeper row: {Name}", Name);
        if (DeleteRequestedAsync != null)
            await DeleteRequestedAsync(this);
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        _logger?.LogInformation("Submit Deeper row to catalogue: {Name}", Name);
        if (SubmitRequestedAsync != null)
            await SubmitRequestedAsync(this);
    }
}
