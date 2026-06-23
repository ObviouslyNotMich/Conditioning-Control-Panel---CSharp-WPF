using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Catalogue;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.DeeperSubmissions partial.
/// Tracks catalogue submission status badges and polls for accepted/published
/// transitions.
/// </summary>
public partial class DeeperSubmissionsTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly ILogger<DeeperSubmissionsTabViewModel>? _logger;
    private readonly ICatalogueService? _catalogueService;

    private DateTime _lastCheckUtc = DateTime.MinValue;
    private static readonly TimeSpan CheckThrottle = TimeSpan.FromSeconds(90);
    private bool _checkInFlight;

    public DeeperSubmissionsTabViewModel() : base("deepersubmissions", "Deeper Submissions", "📤")
    {
        _submissions = new ObservableCollection<DeeperSubmissionRowViewModel>();
    }

    public DeeperSubmissionsTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        ILogger<DeeperSubmissionsTabViewModel> logger,
        ICatalogueService catalogueService) : base("deepersubmissions", "Deeper Submissions", "📤")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;
        _catalogueService = catalogueService;
        _submissions = new ObservableCollection<DeeperSubmissionRowViewModel>();
    }

    public override void OnSelected()
    {
        base.OnSelected();
        if (_submissions.Count == 0)
        {
            LoadSubmissions();
        }
    }

    [ObservableProperty]
    private ObservableCollection<DeeperSubmissionRowViewModel> _submissions;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _lastCheckedText = Loc.Get("label_never");

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _logger?.LogInformation("Refreshing Deeper submissions");
        LoadSubmissions();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task CheckStatusesAsync(bool force = false)
    {
        if (_checkInFlight) return;
        if (_settingsService?.Current == null) return;
        if (string.IsNullOrEmpty(_settingsService.Current.AuthToken))
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("catalogue_toast_auth_failed"),
                Loc.Get("msg_catalogue_auth_required"),
                DialogSeverity.Warning) ?? Task.CompletedTask);
            return;
        }

        var subs = _settingsService.Current.DeeperSubmissions;
        if (subs.Count == 0) return;

        bool anyOpen = subs.Values.Any(r => !IsAcceptedStatus(r.Status) || !r.AcceptedNotified);
        if (!anyOpen) return;

        if (!force && DateTime.UtcNow - _lastCheckUtc < CheckThrottle) return;

        IsBusy = true;
        _checkInFlight = true;
        _lastCheckUtc = DateTime.UtcNow;

        try
        {
            _logger?.LogInformation("Polling Deeper submission statuses");
            var statuses = await (_catalogueService?.FetchMySubmissionsAsync(default)
                ?? Task.FromResult<Dictionary<string, string>?>(null));
            if (statuses == null) return;

            bool changed = false;
            foreach (var kvp in subs)
            {
                var rec = kvp.Value;
                if (rec == null || string.IsNullOrEmpty(rec.CatalogueId)) continue;
                if (!statuses.TryGetValue(rec.CatalogueId, out var serverStatus) || string.IsNullOrEmpty(serverStatus))
                    continue;

                rec.LastCheckedUtc = DateTime.UtcNow;

                if (!string.Equals(rec.Status, serverStatus, StringComparison.OrdinalIgnoreCase))
                {
                    rec.Status = serverStatus;
                    changed = true;
                }

                if (IsAcceptedStatus(serverStatus) && !rec.AcceptedNotified)
                {
                    rec.AcceptedNotified = true;
                    changed = true;
                    NotifyDeeperSubmissionAccepted(rec.CatalogueId, kvp.Key);
                }
            }

            if (changed)
            {
                _settingsService.Save();
                LoadSubmissions();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("CheckDeeperSubmissionStatuses failed: {Error}", ex.Message);
        }
        finally
        {
            _checkInFlight = false;
            IsBusy = false;
            LastCheckedText = DateTime.UtcNow.ToString("g", CultureInfo.CurrentCulture);
            LoadSubmissions();
        }
    }

    [RelayCommand]
    private async Task RecordSubmissionAsync(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        if (_settingsService?.Current == null) return;
        if (string.IsNullOrEmpty(_settingsService.Current.AuthToken))
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("catalogue_toast_auth_failed"),
                Loc.Get("msg_catalogue_auth_required"),
                DialogSeverity.Warning) ?? Task.CompletedTask);
            return;
        }

        _logger?.LogInformation("Recording Deeper submission: {Path}", filePath);
        IsBusy = true;
        try
        {
            var result = await (_catalogueService?.SubmitEnhancementAsync(filePath, default)
                ?? Task.FromResult<SubmissionResult>(new SubmissionResult.UnknownError(0, "service_unavailable")));
            RecordDeeperSubmission(filePath, result);
            await ShowCatalogueSubmissionResultAsync(result);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Record Deeper submission failed");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                ex.Message,
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
        finally
        {
            IsBusy = false;
            LoadSubmissions();
        }
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

    private void NotifyDeeperSubmissionAccepted(string catalogueId, string canonicalPath)
    {
        try
        {
            string name = Path.GetFileNameWithoutExtension(canonicalPath) ?? canonicalPath;
            var msg = Loc.GetF("deeper_submission_accepted_toast_fmt", name);
            _ = _dialogService?.ShowMessageAsync(
                Loc.Get("title_success"),
                msg,
                DialogSeverity.Info);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("[Catalogue] NotifyDeeperSubmissionAccepted failed: {Error}", ex.Message);
        }
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

    private void LoadSubmissions()
    {
        Submissions.Clear();
        var settings = _settingsService?.Current;
        if (settings?.DeeperSubmissions == null) return;

        foreach (var kvp in settings.DeeperSubmissions)
        {
            var rec = kvp.Value;
            if (rec == null) continue;
            var status = (rec.Status ?? "").ToLowerInvariant();
            var (glyph, label) = status switch
            {
                "approved" or "published" => ("✅", Loc.Get("deeper_submission_badge_published")),
                "rejected" => ("⚠", Loc.Get("deeper_submission_badge_rejected")),
                _ => ("⏳", Loc.Get("deeper_submission_badge_pending"))
            };

            Submissions.Add(new DeeperSubmissionRowViewModel
            {
                FilePath = kvp.Key,
                CatalogueId = rec.CatalogueId,
                StatusGlyph = glyph,
                StatusLabel = label,
                Status = rec.Status ?? "pending",
                SubmittedUtc = rec.SubmittedUtc,
                LastCheckedUtc = rec.LastCheckedUtc,
                AcceptedNotified = rec.AcceptedNotified
            });
        }
    }
}

/// <summary>
/// Per-row view model for a tracked Deeper catalogue submission.
/// </summary>
public partial class DeeperSubmissionRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private string _catalogueId = "";

    [ObservableProperty]
    private string _statusGlyph = "";

    [ObservableProperty]
    private string _statusLabel = "";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private DateTime _submittedUtc;

    [ObservableProperty]
    private DateTime _lastCheckedUtc;

    [ObservableProperty]
    private bool _acceptedNotified;
}
