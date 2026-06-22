using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Webcam;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.Lab partial.
/// Exposes lockdown, quiz, chaos mode, wallpaper override, and lab session commands.
/// Live services (Lockdown, Quiz, Chaos, Wallpaper, Webcam, AI) are not abstracted in Core yet,
/// so most commands are stubbed with logging and dialogs.
/// </summary>
public partial class LabTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly IWebcamService? _webcam;
    private readonly IAppLogger? _logger;
    private bool _syncingPlayMode;

    public LabTabViewModel() : base("lab", "Lab", "🧪")
    {
        LockdownDurations = new ObservableCollection<int> { 5, 10, 15, 30, 60 };
        PastQuizzes = new ObservableCollection<PastQuizViewModel>();
        WebcamDevices = new ObservableCollection<WebcamDeviceOption>();
        Monitors = new ObservableCollection<MonitorOption>();
        EffectPermissions = new ObservableCollection<LabEffectPermissionItem>();
        DebugLog = new ObservableCollection<string>();
        InitializeDefaults();
    }

    public LabTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        IWebcamService webcam,
        IAppLogger logger) : base("lab", "Lab", "🧪")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _webcam = webcam;
        _logger = logger;
        LockdownDurations = new ObservableCollection<int> { 5, 10, 15, 30, 60 };
        PastQuizzes = new ObservableCollection<PastQuizViewModel>();
        WebcamDevices = new ObservableCollection<WebcamDeviceOption>();
        Monitors = new ObservableCollection<MonitorOption>();
        EffectPermissions = new ObservableCollection<LabEffectPermissionItem>();
        DebugLog = new ObservableCollection<string>();
        InitializeDefaults();
        LoadFromSettings();
    }

    #region Lockdown / Quiz / Chaos / Wallpaper (existing bindings)

    [ObservableProperty]
    private bool _isLockdownActive;

    [ObservableProperty]
    private int _selectedLockdownDuration = 10;

    [ObservableProperty]
    private string _lockdownTimerText = "00:00";

    [ObservableProperty]
    private bool _popQuizEnabled;

    [ObservableProperty]
    private int _popQuizFrequency = 3;

    [ObservableProperty]
    private string _popQuizFrequencyText = "3/session hr";

    [ObservableProperty]
    private bool _wallpaperEnabled;

    [ObservableProperty]
    private string _currentWallpaper = "";

    [ObservableProperty]
    private bool _quizFullscreen;

    [ObservableProperty]
    private bool _quizDrone;

    [ObservableProperty]
    private ObservableCollection<int> _lockdownDurations;

    [ObservableProperty]
    private ObservableCollection<PastQuizViewModel> _pastQuizzes;

    #endregion

    #region Hero / how-to-play

    [ObservableProperty]
    private bool _playModeFreeDesktop = true;

    [ObservableProperty]
    private bool _playModeStory;

    [ObservableProperty]
    private bool _announcementsEnabled = true;

    #endregion

    #region AI Effects & Memory

    [ObservableProperty]
    private bool _aiEffectsEnabled;

    [ObservableProperty]
    private bool _aiMemoryEnabled;

    [ObservableProperty]
    private ObservableCollection<LabEffectPermissionItem> _effectPermissions;

    #endregion

    #region Webcam engine

    [ObservableProperty]
    private ObservableCollection<WebcamDeviceOption> _webcamDevices;

    [ObservableProperty]
    private WebcamDeviceOption? _selectedWebcamDevice;

    [ObservableProperty]
    private ObservableCollection<MonitorOption> _monitors;

    [ObservableProperty]
    private MonitorOption? _selectedMonitor;

    [ObservableProperty]
    private bool _isTracking;

    [ObservableProperty]
    private string _trackerButtonText = "";

    [ObservableProperty]
    private string _trackerStatusText = "";

    [ObservableProperty]
    private IBrush _trackerStatusColor = GetThemeBrush("PinkBrush", "#FFFF69B4");

    [ObservableProperty]
    private bool _debugCursorEnabled;

    [ObservableProperty]
    private string _countersText = "";

    [ObservableProperty]
    private ObservableCollection<string> _debugLog;

    #endregion

    #region EYES cards

    [ObservableProperty]
    private bool _focusGazeEnabled;

    [ObservableProperty]
    private string _focusGazeStatusText = "";

    [ObservableProperty]
    private IBrush _focusGazeStatusColor = GetThemeBrush("TextMutedBrush", "#FF888888");

    #endregion

    #region Gating

    [ObservableProperty]
    private bool _isBetaLocked;

    #endregion

    private void InitializeDefaults()
    {
        IsBetaLocked = false;
        PlayModeFreeDesktop = true;
        PlayModeStory = false;
        AnnouncementsEnabled = true;
        TrackerButtonText = Loc.Get("lab_start_tracking");
        TrackerStatusText = Loc.Get("lab_tracker_status_stopped");
        CountersText = "0 blinks · 0 gaze hits · 0 rabbits";
        FocusGazeStatusText = Loc.Get("lab_focus_gaze_hint");

        EffectPermissions.Add(new LabEffectPermissionItem(Loc.Get("recap_feat_flash")));
        EffectPermissions.Add(new LabEffectPermissionItem(Loc.Get("recap_feat_video")));
        EffectPermissions.Add(new LabEffectPermissionItem(Loc.Get("recap_feat_overlay")));
        EffectPermissions.Add(new LabEffectPermissionItem(Loc.Get("recap_feat_bubbles")));
        EffectPermissions.Add(new LabEffectPermissionItem(Loc.Get("label_haptics_enabled")));
        EffectPermissions.Add(new LabEffectPermissionItem(Loc.Get("label_audio_whispers")));

        WebcamDevices.Add(new WebcamDeviceOption(0, Loc.Get("blink_trainer_default_camera")));
        SelectedWebcamDevice = WebcamDevices[0];

        Monitors.Add(new MonitorOption("Primary", Loc.Get("webcam_monitor_primary")));
        SelectedMonitor = Monitors[0];
    }

    partial void OnPlayModeFreeDesktopChanged(bool value)
    {
        if (_syncingPlayMode) return;
        _syncingPlayMode = true;
        PlayModeStory = !value;
        _syncingPlayMode = false;
    }

    partial void OnPlayModeStoryChanged(bool value)
    {
        if (_syncingPlayMode) return;
        _syncingPlayMode = true;
        PlayModeFreeDesktop = !value;
        _syncingPlayMode = false;
    }

    partial void OnPopQuizEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.PopQuizEnabled = value;
        Save();
    }

    partial void OnPopQuizFrequencyChanged(int value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.PopQuizFrequency = value;
        PopQuizFrequencyText = $"{value}/session hr";
        Save();
    }

    partial void OnWallpaperEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        if (value)
        {
            _logger?.Information("Wallpaper override activated (stub)");
            CurrentWallpaper = "wallpaper-sample.jpg";
        }
        else
        {
            _logger?.Information("Wallpaper override deactivated (stub)");
            CurrentWallpaper = "";
        }
        _settingsService.Current.WallpaperEnabled = value;
        Save();
    }

    partial void OnAiEffectsEnabledChanged(bool value)
    {
        _logger?.Information("AI effects master toggle: {Active}", value);
    }

    partial void OnAiMemoryEnabledChanged(bool value)
    {
        _logger?.Information("AI memory toggle: {Active}", value);
    }

    partial void OnIsTrackingChanged(bool value)
    {
        TrackerButtonText = value ? Loc.Get("btn_stop") : Loc.Get("lab_start_tracking");
        TrackerStatusText = value ? Loc.Get("lab_tracker_status_running") : Loc.Get("lab_tracker_status_stopped");
        TrackerStatusColor = value
            ? GetThemeBrush("SuccessBrush", "#FF00C853")
            : GetThemeBrush("PinkBrush", "#FFFF69B4");
        AppendLog(value ? Loc.Get("lab_log_tracking_started") : Loc.Get("lab_log_tracking_stopped"));
    }

    partial void OnDebugCursorEnabledChanged(bool value)
    {
        AppendLog(value ? Loc.Get("lab_log_debug_cursor_enabled") : Loc.Get("lab_log_debug_cursor_hidden"));
    }

    partial void OnFocusGazeEnabledChanged(bool value)
    {
        FocusGazeStatusText = value ? Loc.Get("lab_tracker_status_running") : Loc.Get("lab_focus_gaze_hint");
        FocusGazeStatusColor = value
            ? GetThemeBrush("SuccessBrush", "#FF00C853")
            : GetThemeBrush("TextMutedBrush", "#FF888888");
        _logger?.Information("Focus Gaze toggled: {Active}", value);
    }

    [RelayCommand]
    private async Task ActivateLockdownAsync()
    {
        var confirmed = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("lab_lockdown_title"),
            string.Format(Loc.Get("lab_lockdown_message_fmt"), SelectedLockdownDuration)) ?? Task.FromResult(false));
        if (!confirmed) return;

        IsLockdownActive = true;
        _logger?.Information("Lockdown activated for {Minutes} minutes (stub)", SelectedLockdownDuration);
    }

    [RelayCommand]
    private async Task StartQuizAsync()
    {
        _logger?.Information("Quiz requested");
        await (_dialogService?.ShowMessageAsync(
            "Login Required",
            Loc.Get("msg_you_need_to_be_logged_in_to_use_the_ai_quiz")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task StartChaosAsync()
    {
        _logger?.Information("Chaos mode requested");
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("lab_chaos_title"),
            Loc.Get("lab_chaos_not_available")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task QuickStartChaosAsync()
    {
        _logger?.Information("Chaos quick start requested");
        await StartChaosAsync();
    }

    [RelayCommand]
    private void TestPopQuiz()
    {
        _logger?.Information("Pop quiz test requested");
    }

    [RelayCommand]
    private void ShuffleWallpaper()
    {
        CurrentWallpaper = "wallpaper-shuffled.jpg";
        _logger?.Information("Wallpaper shuffled (stub)");
    }

    [RelayCommand]
    private void LockdownTimerClick()
    {
        _logger?.Information("Lockdown timer clicked (stub)");
    }

    [RelayCommand]
    private void AttemptLockdownExit(string? phrase)
    {
        if (string.IsNullOrEmpty(phrase)) return;
        _logger?.Information("Lockdown exit attempt (stub)");
        IsLockdownActive = false;
    }

    [RelayCommand]
    private async Task ForgetEverythingAsync()
    {
        var confirmed = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("lab_forget_title"),
            Loc.Get("lab_forget_message")) ?? Task.FromResult(false));
        if (!confirmed) return;

        AiMemoryEnabled = false;
        _logger?.Information("AI memory cleared (stub)");
        AppendLog(Loc.Get("lab_log_ai_memory_cleared"));
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        _webcam?.RefreshDevices();
        WebcamDevices.Clear();
        WebcamDevices.Add(new WebcamDeviceOption(0, Loc.Get("blink_trainer_default_camera")));
        SelectedWebcamDevice = WebcamDevices[0];
        AppendLog(Loc.Get("lab_log_camera_list_refreshed"));
    }

    [RelayCommand]
    private void RefreshMonitors()
    {
        Monitors.Clear();
        Monitors.Add(new MonitorOption("Primary", Loc.Get("webcam_monitor_primary")));
        SelectedMonitor = Monitors[0];
        AppendLog(Loc.Get("lab_log_monitor_list_refreshed"));
    }

    [RelayCommand]
    private void StartTracking()
    {
        if (IsTracking)
        {
            _webcam?.StopTracking();
            IsTracking = false;
        }
        else
        {
            _webcam?.StartTracking();
            IsTracking = true;
        }
    }

    [RelayCommand]
    private async Task CalibrateAsync()
    {
        _webcam?.Calibrate();
        AppendLog(Loc.Get("lab_log_calibration_requested"));
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("lab_calibration_title"),
            Loc.Get("lab_calibration_not_available")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task QuickRecalAsync()
    {
        _webcam?.Calibrate();
        AppendLog(Loc.Get("lab_log_quick_recal_requested"));
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("lab_quick_recal_title"),
            Loc.Get("lab_quick_recal_not_available")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task TrackerTestAsync()
    {
        _webcam?.TestTracker();
        AppendLog(Loc.Get("lab_log_tracker_test_requested"));
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("lab_tracker_test_title"),
            Loc.Get("lab_tracker_test_not_available")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task RevokeConsentAsync()
    {
        var confirmed = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("lab_revoke_consent_title"),
            Loc.Get("lab_revoke_consent_message")) ?? Task.FromResult(false));
        if (!confirmed) return;

        _webcam?.RevokeConsent();
        IsTracking = false;
        DebugCursorEnabled = false;
        AppendLog(Loc.Get("lab_log_consent_revoked"));
    }

    [RelayCommand]
    private void OpenGazeMinigame()
    {
        _logger?.Information("Gaze minigame requested");
        AppendLog(Loc.Get("lab_log_gaze_minigame_opened"));
    }

    [RelayCommand]
    private async Task GoToBlinkTrainerAsync()
    {
        _logger?.Information("Navigate to Blink Trainer requested");
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("lab_blink_trainer_title"),
            Loc.Get("lab_blink_trainer_message")) ?? Task.CompletedTask);
    }

    private void LoadFromSettings()
    {
        var s = _settingsService?.Current;
        if (s == null)
        {
            IsBetaLocked = true;
            return;
        }

        PopQuizEnabled = s.PopQuizEnabled;
        PopQuizFrequency = s.PopQuizFrequency;
        WallpaperEnabled = s.WallpaperEnabled;

        WebcamDevices.Clear();
        WebcamDevices.Add(new WebcamDeviceOption(Math.Max(0, s.WebcamDeviceIndex), s.WebcamDeviceName));
        SelectedWebcamDevice = WebcamDevices[0];

        Monitors.Clear();
        Monitors.Add(new MonitorOption(s.WebcamCalibrationScreen, s.WebcamCalibrationScreen));
        SelectedMonitor = Monitors[0];

        IsBetaLocked = !(s.HasLinkedPatreon || s.HasLinkedDiscord);
    }

    private void Save()
    {
        try { _settingsService?.Save(); }
        catch (Exception ex) { _logger?.Warning(ex, "Failed to save lab settings"); }
    }

    private void AppendLog(string line)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        DebugLog.Add($"[{stamp}] {line}");
        while (DebugLog.Count > 12) DebugLog.RemoveAt(0);
    }

    private static IBrush GetThemeBrush(string key, string fallbackHex)
    {
        var app = Application.Current;
        if (app != null &&
            app.Resources.TryGetResource(key, app.ActualThemeVariant, out var value) &&
            value is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.Parse(fallbackHex));
    }
}

public sealed partial class PastQuizViewModel : ObservableObject
{
    [ObservableProperty]
    private string _label = "";

    [ObservableProperty]
    private string _category = "";

    [ObservableProperty]
    private int _percent;
}

/// <summary>
/// Visual-shell item for an AI effect permission toggle in the Lab AI card.
/// </summary>
public sealed partial class LabEffectPermissionItem : ObservableObject
{
    public LabEffectPermissionItem(string label)
    {
        Label = label;
    }

    [ObservableProperty]
    private string _label = "";

    [ObservableProperty]
    private bool _isEnabled;
}
