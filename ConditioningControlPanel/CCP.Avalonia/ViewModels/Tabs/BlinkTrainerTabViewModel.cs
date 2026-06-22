using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.LabTab partial.
/// Exposes webcam debug start/stop, calibration, tracker test, quick recal,
/// gaze focus toggle, device/monitor selection, and the blink-to-recalibrate
/// shortcut. Live Webcam/GazeFocus services are not abstracted in Core yet,
/// so most commands are stubbed with logging and dialogs.
///
/// This VM also supports the richer secondary-tab shell: hero banner, stage,
/// asset-packs list, session/webcam cards, and premium gating. No actual
/// eye-tracking or session engine logic is implemented here.
/// </summary>
public partial class BlinkTrainerTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly IAppLogger? _logger;

    public BlinkTrainerTabViewModel() : base("blinktrainer", "Blink Trainer", "💫")
    {
        WebcamDevices = new ObservableCollection<WebcamDeviceOption>();
        Monitors = new ObservableCollection<MonitorOption>();
        DebugLog = new ObservableCollection<string>();
        AssetFolders = new ObservableCollection<AssetFolderItem>();
        InitializeDefaults();
    }

    public BlinkTrainerTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        IAppLogger logger) : base("blinktrainer", "Blink Trainer", "💫")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;
        WebcamDevices = new ObservableCollection<WebcamDeviceOption>();
        Monitors = new ObservableCollection<MonitorOption>();
        DebugLog = new ObservableCollection<string>();
        AssetFolders = new ObservableCollection<AssetFolderItem>();
        InitializeDefaults();
        LoadFromSettings();
    }

    private void InitializeDefaults()
    {
        IsPremiumLocked = false;
        IsSessionRunning = false;
        SessionButtonText = Loc.Get("blink_trainer_start_session");
        StatusText = Loc.Get("blink_trainer_status_ready");
        StatusColor = new SolidColorBrush(Color.Parse("#FFFF69B4"));
        PreviewLabel = Loc.Get("blink_trainer_preview_demo_label");
        ConsentGranted = false;
        ConsentStatusText = Loc.Get("blink_trainer_consent_required");
        CalibrationStatusText = Loc.Get("blink_trainer_calibration_none");

        // Placeholder folder so the asset-packs card is not empty at design time.
        AssetFolders.Add(new AssetFolderItem("Default", Loc.Get("blink_trainer_folder_empty_or_invalid")));
    }

    [ObservableProperty]
    private bool _isTracking;

    [ObservableProperty]
    private string _trackerButtonText = Loc.Get("blink_trainer_tracker_start");

    [ObservableProperty]
    private string _statusText = Loc.Get("blink_trainer_status_stopped");

    [ObservableProperty]
    private IBrush _statusColor = new SolidColorBrush(Color.Parse("#FFFF69B4"));

    [ObservableProperty]
    private string _counterText = Loc.Get("blink_trainer_counter_format");

    [ObservableProperty]
    private bool _focusGazeActive;

    [ObservableProperty]
    private string _focusGazeStatus = "";

    [ObservableProperty]
    private bool _blinkRecalibrateShortcutEnabled;

    [ObservableProperty]
    private bool _restrictGazeToCalibratedScreen;

    [ObservableProperty]
    private bool _debugCursorEnabled;

    [ObservableProperty]
    private ObservableCollection<WebcamDeviceOption> _webcamDevices;

    [ObservableProperty]
    private WebcamDeviceOption? _selectedWebcamDevice;

    [ObservableProperty]
    private ObservableCollection<MonitorOption> _monitors;

    [ObservableProperty]
    private MonitorOption? _selectedMonitor;

    [ObservableProperty]
    private ObservableCollection<string> _debugLog;

    // Rich-shell properties

    [ObservableProperty]
    private bool _isPremiumLocked;

    [ObservableProperty]
    private bool _isSessionRunning;

    [ObservableProperty]
    private string _sessionButtonText = Loc.Get("blink_trainer_start_session");

    [ObservableProperty]
    private double _sessionDuration = 10;

    [ObservableProperty]
    private double _overlayOpacity = 0.7;

    [ObservableProperty]
    private bool _isMixMode;

    [ObservableProperty]
    private bool _includeVideos;

    [ObservableProperty]
    private string _previewLabel = "";

    [ObservableProperty]
    private bool _consentGranted;

    [ObservableProperty]
    private string _consentStatusText = "";

    [ObservableProperty]
    private string _calibrationStatusText = "";

    [ObservableProperty]
    private ObservableCollection<AssetFolderItem> _assetFolders;

    partial void OnFocusGazeActiveChanged(bool value)
    {
        FocusGazeStatus = value ? Loc.Get("label_focus_gaze_active") : "";
        _logger?.Information("Focus Gaze toggled: {Active}", value);
    }

    partial void OnBlinkRecalibrateShortcutEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.BlinkRecalibrateShortcutEnabled = value;
        Save();
    }

    partial void OnRestrictGazeToCalibratedScreenChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.RestrictGazeContentToCalibratedScreen = value;
        Save();
    }

    partial void OnDebugCursorEnabledChanged(bool value)
    {
        AppendLog(value ? Loc.Get("blink_trainer_log_debug_cursor_enabled") : Loc.Get("blink_trainer_log_debug_cursor_hidden"));
    }

    partial void OnSelectedWebcamDeviceChanged(WebcamDeviceOption? value)
    {
        if (value == null || _settingsService?.Current == null) return;
        _settingsService.Current.WebcamDeviceIndex = value.Index;
        _settingsService.Current.WebcamDeviceName = value.Name;
        Save();
        AppendLog(Loc.GetF("blink_trainer_log_camera_set_fmt", value.Name));
    }

    partial void OnSelectedMonitorChanged(MonitorOption? value)
    {
        if (value == null || _settingsService?.Current == null) return;
        _settingsService.Current.WebcamCalibrationScreen = value.DeviceName;
        Save();
        AppendLog(Loc.GetF("blink_trainer_log_monitor_set_fmt", value.Label));
    }

    partial void OnIsSessionRunningChanged(bool value)
    {
        SessionButtonText = value ? Loc.Get("blink_trainer_stop_session") : Loc.Get("blink_trainer_start_session");
        StatusText = value
            ? Loc.GetF("blink_trainer_status_running", $"{SessionDuration:0} min")
            : Loc.Get("blink_trainer_status_ready");
        UpdateStatusColor();
    }

    partial void OnSessionDurationChanged(double value)
    {
        if (IsSessionRunning)
            StatusText = Loc.GetF("blink_trainer_status_running", $"{value:0} min");
    }

    partial void OnConsentGrantedChanged(bool value)
    {
        ConsentStatusText = value ? Loc.Get("blink_trainer_consent_granted") : Loc.Get("blink_trainer_consent_required");
    }

    partial void OnIncludeVideosChanged(bool value)
    {
        _logger?.Information("Include videos toggled: {Value}", value);
    }

    [RelayCommand]
    private async Task ToggleTrackingAsync()
    {
        if (IsTracking)
        {
            IsTracking = false;
            TrackerButtonText = Loc.Get("blink_trainer_tracker_start");
            StatusText = Loc.Get("blink_trainer_status_ready");
            UpdateStatusColor();
            AppendLog(Loc.Get("blink_trainer_log_stop_requested"));
        }
        else
        {
            var consent = await (_dialogService?.ShowConfirmationAsync(
                Loc.Get("blink_trainer_webcam_consent_prompt_title"),
                Loc.Get("blink_trainer_webcam_consent_prompt_body")) ?? Task.FromResult(false));
            if (!consent) return;

            IsTracking = true;
            TrackerButtonText = Loc.Get("blink_trainer_tracker_stop");
            StatusText = Loc.Get("blink_trainer_status_starting");
            UpdateStatusColor();
            AppendLog(Loc.Get("blink_trainer_log_start_result"));
        }
    }

    [RelayCommand]
    private void ToggleSession()
    {
        IsSessionRunning = !IsSessionRunning;
        AppendLog(IsSessionRunning ? Loc.Get("blink_trainer_log_session_started") : Loc.Get("blink_trainer_log_session_stopped"));
    }

    [RelayCommand]
    private void AddFolder()
    {
        AppendLog(Loc.Get("blink_trainer_log_add_folder_requested"));
    }

    [RelayCommand]
    private void GrantConsent()
    {
        ConsentGranted = true;
        AppendLog(Loc.Get("blink_trainer_log_consent_granted"));
    }

    [RelayCommand]
    private async Task UnlockAsync()
    {
        _logger?.Information("Premium unlock requested from blink trainer gate");
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("gate_premium_locked"),
            Loc.Get("blink_trainer_gate_body")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task ShowHelpAsync()
    {
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("blink_trainer_help_title"),
            Loc.Get("blink_trainer_help_body")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task CalibrateAsync()
    {
        AppendLog(Loc.Get("blink_trainer_log_calibration_opening"));
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("blink_trainer_calibration_dialog_title"),
            Loc.Get("blink_trainer_calibration_unavailable_body")) ?? Task.CompletedTask);
        AppendLog(Loc.Get("blink_trainer_log_calibration_cancelled"));
    }

    [RelayCommand]
    private async Task TrackerTestAsync()
    {
        AppendLog(Loc.Get("blink_trainer_log_tracker_test_opening"));
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("blink_trainer_tracker_test_dialog_title"),
            Loc.Get("blink_trainer_tracker_test_unavailable_body")) ?? Task.CompletedTask);
        AppendLog(Loc.Get("blink_trainer_log_tracker_test_closed"));
    }

    [RelayCommand]
    private async Task QuickRecalAsync()
    {
        AppendLog(Loc.Get("blink_trainer_log_quick_recal_opening"));
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("blink_trainer_quick_recal_dialog_title"),
            Loc.Get("blink_trainer_quick_recal_unavailable_body")) ?? Task.CompletedTask);
        AppendLog(Loc.Get("blink_trainer_log_quick_recal_cancelled"));
    }

    [RelayCommand]
    private async Task ReviewPrivacyAsync()
    {
        AppendLog(Loc.Get("blink_trainer_log_privacy_reviewed"));
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("blink_trainer_privacy_dialog_title"),
            Loc.Get("blink_trainer_privacy_dialog_body")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task RevokeConsentAsync()
    {
        var confirmed = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("blink_trainer_consent_revoke_confirm_title"),
            Loc.Get("blink_trainer_revoke_confirm_body_short")) ?? Task.FromResult(false));
        if (!confirmed) return;

        IsTracking = false;
        TrackerButtonText = Loc.Get("blink_trainer_tracker_start");
        StatusText = Loc.Get("blink_trainer_status_ready");
        ConsentGranted = false;
        DebugCursorEnabled = false;
        UpdateStatusColor();
        AppendLog(Loc.Get("blink_trainer_log_consent_revoked"));
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        WebcamDevices.Clear();
        WebcamDevices.Add(new WebcamDeviceOption(0, Loc.Get("blink_trainer_default_camera")));
        SelectedWebcamDevice = WebcamDevices[0];
        AppendLog(Loc.GetF("blink_trainer_log_camera_scan_result_fmt", 1));
    }

    [RelayCommand]
    private void RefreshMonitors()
    {
        Monitors.Clear();
        Monitors.Add(new MonitorOption("Primary", Loc.Get("webcam_monitor_primary")));
        SelectedMonitor = Monitors[0];
        AppendLog(Loc.Get("blink_trainer_log_monitors_refreshed"));
    }

    [RelayCommand]
    private void OpenGazeMinigame()
    {
        _logger?.Information("Gaze minigame requested");
    }

    private void LoadFromSettings()
    {
        var s = _settingsService?.Current;
        if (s == null) return;

        BlinkRecalibrateShortcutEnabled = s.BlinkRecalibrateShortcutEnabled;
        RestrictGazeToCalibratedScreen = s.RestrictGazeContentToCalibratedScreen;

        WebcamDevices.Clear();
        WebcamDevices.Add(new WebcamDeviceOption(Math.Max(0, s.WebcamDeviceIndex), s.WebcamDeviceName));
        SelectedWebcamDevice = WebcamDevices[0];

        Monitors.Clear();
        Monitors.Add(new MonitorOption(s.WebcamCalibrationScreen, s.WebcamCalibrationScreen));
        SelectedMonitor = Monitors[0];

        IsPremiumLocked = !(s.HasLinkedPatreon || s.HasLinkedDiscord);
    }

    private void Save()
    {
        try { _settingsService?.Save(); }
        catch (Exception ex) { _logger?.Warning(ex, "Failed to save blink trainer settings"); }
    }

    private void AppendLog(string line)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        DebugLog.Add($"[{stamp}] {line}");
        while (DebugLog.Count > 12) DebugLog.RemoveAt(0);
    }

    private void UpdateStatusColor()
    {
        var color = IsTracking || IsSessionRunning ? "#FF00C853" : "#FFFF69B4";
        StatusColor = new SolidColorBrush(Color.Parse(color));
    }
}

public sealed record WebcamDeviceOption(int Index, string Name);
public sealed record MonitorOption(string DeviceName, string Label);
public sealed record AssetFolderItem(string Name, string Status);
