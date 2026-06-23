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
/// Avalonia port of the WPF MainWindow.CatalogueSubmissions partial.
/// Tracks catalogue preset/session submission status badges and polls for
/// accepted/published transitions.
/// </summary>
public partial class CatalogueSubmissionsTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly ILogger<CatalogueSubmissionsTabViewModel>? _logger;
    private readonly ICatalogueService? _catalogueService;

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
        ILogger<CatalogueSubmissionsTabViewModel> logger,
        ICatalogueService catalogueService) : base("cataloguesubmissions", "Catalogue Submissions", "📤")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;
        _catalogueService = catalogueService;
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
        _logger?.LogInformation("Refreshing catalogue submissions");
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

        bool anyOpen = dict.Values.Any(r => !IsCatalogueAcceptedStatus(r.Status) || !r.AcceptedNotified);
        if (!anyOpen) return;

        var lastCheck = isPresets ? _lastPresetCheckUtc : _lastSessionCheckUtc;
        if (!force && DateTime.UtcNow - lastCheck < CheckThrottle) return;

        if (isPresets) { _presetCheckInFlight = true; _lastPresetCheckUtc = DateTime.UtcNow; }
        else { _sessionCheckInFlight = true; _lastSessionCheckUtc = DateTime.UtcNow; }

        IsBusy = true;
        try
        {
            _logger?.LogInformation("Polling catalogue {Kind} submission statuses", kind);
            var statuses = await (_catalogueService?.FetchMyCatalogueAssetsAsync(kind, default)
                ?? Task.FromResult<Dictionary<string, string>?>(null));
            if (statuses == null) return;

            bool changed = false;
            foreach (var kvp in dict)
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

                if (IsCatalogueAcceptedStatus(serverStatus) && !rec.AcceptedNotified)
                {
                    rec.AcceptedNotified = true;
                    changed = true;
                    NotifyCatalogueSubmissionAccepted(kind, rec.CatalogueId, kvp.Key);
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
            _logger?.LogWarning("CheckCatalogueSubmissionStatuses failed: {Error}", ex.Message);
        }
        finally
        {
            if (isPresets) _presetCheckInFlight = false; else _sessionCheckInFlight = false;
            IsBusy = false;
            LastCheckedText = DateTime.UtcNow.ToString("g", CultureInfo.CurrentCulture);
            LoadSubmissions();
        }
    }

    private static bool IsCatalogueAcceptedStatus(string? status) =>
        string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "published", StringComparison.OrdinalIgnoreCase);

    private void NotifyCatalogueSubmissionAccepted(string kind, string catalogueId, string key)
    {
        try
        {
            string name = ResolveCatalogueDisplayName(kind, key);
            var msg = Loc.GetF("catalogue_submission_accepted_toast_fmt", name);
            _ = _dialogService?.ShowMessageAsync(
                Loc.Get("title_success"),
                msg,
                DialogSeverity.Info);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("[Catalogue] NotifyCatalogueSubmissionAccepted failed: {Error}", ex.Message);
        }
    }

    private string ResolveCatalogueDisplayName(string kind, string key)
    {
        try
        {
            if (kind == CatalogueKindSessions)
            {
                var fileName = Path.GetFileNameWithoutExtension(key);
                if (fileName.EndsWith(".session", StringComparison.OrdinalIgnoreCase))
                    fileName = fileName[..^8];
                return fileName;
            }
            if (kind == CatalogueKindPresets)
            {
                var preset = _settingsService?.Current?.UserPresets?.FirstOrDefault(p => p.Id == key);
                if (preset != null && !string.IsNullOrEmpty(preset.Name)) return preset.Name;
            }
        }
        catch { }
        return key;
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
