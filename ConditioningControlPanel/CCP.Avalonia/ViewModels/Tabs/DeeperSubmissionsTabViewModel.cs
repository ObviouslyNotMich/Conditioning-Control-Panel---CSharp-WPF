using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.DeeperSubmissions partial.
/// Tracks catalogue submission status badges and polls for accepted/published
/// transitions. WPF-only services are stubbed with TODOs.
/// </summary>
public partial class DeeperSubmissionsTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly IAppLogger? _logger;

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
        IAppLogger logger) : base("deepersubmissions", "Deeper Submissions", "📤")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;
        _submissions = new ObservableCollection<DeeperSubmissionRowViewModel>();
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
        _logger?.Information("Refreshing Deeper submissions");
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

        if (!force && DateTime.UtcNow - _lastCheckUtc < CheckThrottle) return;

        IsBusy = true;
        _checkInFlight = true;
        _lastCheckUtc = DateTime.UtcNow;

        try
        {
            _logger?.Information("Polling Deeper submission statuses");
            // TODO: wire to ICatalogueService.FetchMySubmissionsAsync() once extracted to CCP.Core.
            await Task.Delay(250); // simulated network call
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_not_implemented"),
                Loc.Get("msg_deeper_submission_poll_not_yet_ported")) ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            _logger?.Warning("CheckDeeperSubmissionStatuses failed: {Error}", ex.Message);
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
        _logger?.Information("Recording Deeper submission: {Path}", filePath);
        // TODO: wire to ICatalogueService submission result once extracted.
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            Loc.Get("msg_deeper_submission_record_not_yet_ported")) ?? Task.CompletedTask);
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
