using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.CatalogueSubmissions partial.
/// Tracks catalogue preset/session submission status badges and polls for
/// accepted/published transitions. WPF-only catalogue services are stubbed with TODOs.
/// </summary>
public partial class CatalogueSubmissionsTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly IAppLogger? _logger;

    private static readonly TimeSpan CheckThrottle = TimeSpan.FromSeconds(90);
    private DateTime _lastPresetCheckUtc = DateTime.MinValue;
    private DateTime _lastSessionCheckUtc = DateTime.MinValue;
    private bool _presetCheckInFlight;
    private bool _sessionCheckInFlight;

    public const string CatalogueKindPresets = "presets";
    public const string CatalogueKindSessions = "sessions";

    public CatalogueSubmissionsTabViewModel() : base("cataloguesubmissions", "Catalogue Submissions", "📤")
    {
        _presetSubmissions = new ObservableCollection<CatalogueSubmissionRowViewModel>();
        _sessionSubmissions = new ObservableCollection<CatalogueSubmissionRowViewModel>();
    }

    public CatalogueSubmissionsTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        IAppLogger logger) : base("cataloguesubmissions", "Catalogue Submissions", "📤")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;
        _presetSubmissions = new ObservableCollection<CatalogueSubmissionRowViewModel>();
        _sessionSubmissions = new ObservableCollection<CatalogueSubmissionRowViewModel>();
        LoadSubmissions();
    }

    [ObservableProperty]
    private ObservableCollection<CatalogueSubmissionRowViewModel> _presetSubmissions;

    [ObservableProperty]
    private ObservableCollection<CatalogueSubmissionRowViewModel> _sessionSubmissions;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _lastCheckedText = Loc.Get("label_never");

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _logger?.Information("Refreshing catalogue submissions");
        LoadSubmissions();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task CheckPresetStatusesAsync(bool force = false)
    {
        await CheckStatusesAsync(CatalogueKindPresets, force);
    }

    [RelayCommand]
    private async Task CheckSessionStatusesAsync(bool force = false)
    {
        await CheckStatusesAsync(CatalogueKindSessions, force);
    }

    private async Task CheckStatusesAsync(string kind, bool force)
    {
        var isPresets = kind == CatalogueKindPresets;
        var inFlight = isPresets ? _presetCheckInFlight : _sessionCheckInFlight;
        if (inFlight) return;

        if (_settingsService?.Current == null) return;
        if (string.IsNullOrEmpty(_settingsService.Current.AuthToken))
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("catalogue_toast_auth_failed"),
                Loc.Get("msg_catalogue_auth_required"),
                DialogSeverity.Warning) ?? Task.CompletedTask);
            return;
        }

        var dict = isPresets
            ? _settingsService.Current.CataloguePresetSubmissions
            : _settingsService.Current.CatalogueSessionSubmissions;

        if (dict == null || dict.Count == 0) return;

        var lastCheck = isPresets ? _lastPresetCheckUtc : _lastSessionCheckUtc;
        if (!force && DateTime.UtcNow - lastCheck < CheckThrottle) return;

        if (isPresets) { _presetCheckInFlight = true; _lastPresetCheckUtc = DateTime.UtcNow; }
        else { _sessionCheckInFlight = true; _lastSessionCheckUtc = DateTime.UtcNow; }

        IsBusy = true;
        try
        {
            _logger?.Information("Polling catalogue {Kind} submission statuses", kind);
            // TODO: wire to ICatalogueService.FetchMyCatalogueAssetsAsync() once extracted to CCP.Core.
            await Task.Delay(250);
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_not_implemented"),
                $"Catalogue {kind} status polling is not yet ported to Avalonia.") ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            _logger?.Warning("CheckCatalogueSubmissionStatuses failed: {Error}", ex.Message);
        }
        finally
        {
            if (isPresets) _presetCheckInFlight = false; else _sessionCheckInFlight = false;
            IsBusy = false;
            LastCheckedText = DateTime.UtcNow.ToString("g", CultureInfo.CurrentCulture);
            LoadSubmissions();
        }
    }

    [RelayCommand]
    private async Task RecordSubmissionAsync((string Kind, string Key)? args)
    {
        if (args == null || string.IsNullOrWhiteSpace(args.Value.Key)) return;
        _logger?.Information("Recording catalogue {Kind} submission for {Key}", args.Value.Kind, args.Value.Key);
        // TODO: wire to ICatalogueService submission result once extracted.
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            "Recording catalogue submissions is not yet ported to Avalonia.") ?? Task.CompletedTask);
    }

    private void LoadSubmissions()
    {
        PresetSubmissions.Clear();
        SessionSubmissions.Clear();
        var settings = _settingsService?.Current;
        if (settings == null) return;

        LoadKind(settings.CataloguePresetSubmissions, PresetSubmissions);
        LoadKind(settings.CatalogueSessionSubmissions, SessionSubmissions);
    }

    private static void LoadKind(
        Dictionary<string, DeeperSubmissionRecord>? dict,
        ObservableCollection<CatalogueSubmissionRowViewModel> target)
    {
        if (dict == null) return;
        foreach (var kvp in dict)
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

            target.Add(new CatalogueSubmissionRowViewModel
            {
                Key = kvp.Key,
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
/// Per-row view model for a tracked catalogue submission.
/// </summary>
public partial class CatalogueSubmissionRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _key = "";

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
