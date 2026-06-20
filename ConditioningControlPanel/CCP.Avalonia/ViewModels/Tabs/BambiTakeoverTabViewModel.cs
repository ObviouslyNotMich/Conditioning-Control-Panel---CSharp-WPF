using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.Autonomy partial.
/// Exposes autonomy mode toggles, intensity/cooldown sliders, behaviour checkboxes,
/// and test/force commands. Full service integration is stubbed behind the dialog
/// service until an IAutonomyService seam is extracted to Core.
/// </summary>
public partial class BambiTakeoverTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly IAppLogger? _logger;

    public BambiTakeoverTabViewModel() : base("bambitakeover", "Takeover", "🌀")
    {
    }

    public BambiTakeoverTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        IAppLogger logger) : base("bambitakeover", "Takeover", "🌀")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;
        LoadFromSettings();
    }

    [ObservableProperty]
    private bool _autonomyEnabled;

    [ObservableProperty]
    private bool _autonomyConsentGiven;

    [ObservableProperty]
    private int _autonomyIntensity = 50;

    [ObservableProperty]
    private int _autonomyCooldown = 60;

    [ObservableProperty]
    private int _autonomyInterval = 300;

    [ObservableProperty]
    private bool _autonomyIdleTrigger;

    [ObservableProperty]
    private bool _autonomyRandomTrigger;

    [ObservableProperty]
    private bool _autonomyTimeAware;

    [ObservableProperty]
    private bool _canTriggerFlash;

    [ObservableProperty]
    private bool _canTriggerVideo;

    [ObservableProperty]
    private bool _canTriggerWebVideo;

    [ObservableProperty]
    private bool _canTriggerSubliminal;

    [ObservableProperty]
    private bool _canTriggerBubbles;

    [ObservableProperty]
    private bool _canComment;

    [ObservableProperty]
    private bool _canTriggerMindWipe;

    [ObservableProperty]
    private bool _canTriggerLockCard;

    [ObservableProperty]
    private bool _canTriggerSpiral;

    [ObservableProperty]
    private bool _canTriggerPinkFilter;

    [ObservableProperty]
    private bool _canTriggerBouncingText;

    [ObservableProperty]
    private bool _canTriggerBubbleCount;

    [ObservableProperty]
    private int _announcementChance = 50;

    partial void OnAutonomyEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;

        if (value && !AutonomyConsentGiven)
        {
            _ = PromptForConsentAsync();
            return;
        }

        _settingsService.Current.AutonomyModeEnabled = value;
        Save();
        _logger?.Information("Autonomy Mode toggled: {Enabled}", value);
    }

    partial void OnAutonomyIntensityChanged(int value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.AutonomyIntensity = value;
        Save();
    }

    partial void OnAutonomyCooldownChanged(int value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.AutonomyCooldownSeconds = value;
        Save();
    }

    partial void OnAutonomyIntervalChanged(int value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.AutonomyRandomIntervalSeconds = value;
        Save();
    }

    partial void OnAutonomyIdleTriggerChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.AutonomyIdleTriggerEnabled = value;
        Save();
    }

    partial void OnAutonomyRandomTriggerChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.AutonomyRandomTriggerEnabled = value;
        Save();
    }

    partial void OnAutonomyTimeAwareChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.AutonomyTimeAwareEnabled = value;
        Save();
    }

    partial void OnAnnouncementChanceChanged(int value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.AutonomyAnnouncementChance = value;
        Save();
    }

    partial void OnCanTriggerFlashChanged(bool value) => SaveBehavior(nameof(CanTriggerFlash), value, s => s.AutonomyCanTriggerFlash = value);
    partial void OnCanTriggerVideoChanged(bool value) => SaveBehavior(nameof(CanTriggerVideo), value, s => s.AutonomyCanTriggerVideo = value);
    partial void OnCanTriggerWebVideoChanged(bool value) => SaveBehavior(nameof(CanTriggerWebVideo), value, s => s.AutonomyCanTriggerWebVideo = value);
    partial void OnCanTriggerSubliminalChanged(bool value) => SaveBehavior(nameof(CanTriggerSubliminal), value, s => s.AutonomyCanTriggerSubliminal = value);
    partial void OnCanTriggerBubblesChanged(bool value) => SaveBehavior(nameof(CanTriggerBubbles), value, s => s.AutonomyCanTriggerBubbles = value);
    partial void OnCanCommentChanged(bool value) => SaveBehavior(nameof(CanComment), value, s => s.AutonomyCanComment = value);
    partial void OnCanTriggerMindWipeChanged(bool value) => SaveBehavior(nameof(CanTriggerMindWipe), value, s => s.AutonomyCanTriggerMindWipe = value);
    partial void OnCanTriggerLockCardChanged(bool value) => SaveBehavior(nameof(CanTriggerLockCard), value, s => s.AutonomyCanTriggerLockCard = value);
    partial void OnCanTriggerSpiralChanged(bool value) => SaveBehavior(nameof(CanTriggerSpiral), value, s => s.AutonomyCanTriggerSpiral = value);
    partial void OnCanTriggerPinkFilterChanged(bool value) => SaveBehavior(nameof(CanTriggerPinkFilter), value, s => s.AutonomyCanTriggerPinkFilter = value);
    partial void OnCanTriggerBouncingTextChanged(bool value) => SaveBehavior(nameof(CanTriggerBouncingText), value, s => s.AutonomyCanTriggerBouncingText = value);
    partial void OnCanTriggerBubbleCountChanged(bool value) => SaveBehavior(nameof(CanTriggerBubbleCount), value, s => s.AutonomyCanTriggerBubbleCount = value);

    private void SaveBehavior(string name, bool value, Action<AppSettings> apply)
    {
        if (_settingsService?.Current == null) return;
        apply(_settingsService.Current);
        Save();
        _logger?.Information("Autonomy behavior {Name} = {Value}", name, value);
    }

    private async Task PromptForConsentAsync()
    {
        var confirm = await (_dialogService?.ShowConfirmationAsync(
            "Enable Autonomy Mode",
            "AUTONOMY MODE\n\nThis feature allows the companion to autonomously trigger effects:\n" +
            "• Flash images\n• Videos\n• Subliminal messages\n• Comments\n\n" +
            "You can disable this at any time. Do you consent?") ?? Task.FromResult(false));

        if (confirm && _settingsService?.Current != null)
        {
            AutonomyConsentGiven = true;
            _settingsService.Current.AutonomyConsentGiven = true;
            _settingsService.Current.AutonomyModeEnabled = true;
            Save();
            _logger?.Information("Autonomy consent granted and enabled");
        }
        else
        {
            AutonomyEnabled = false;
        }
    }

    [RelayCommand]
    private async Task ToggleAutonomyAsync()
    {
        if (_settingsService?.Current == null) return;

        if (!AutonomyEnabled && !AutonomyConsentGiven)
        {
            var confirm = await (_dialogService?.ShowConfirmationAsync(
                "Autonomy Mode Consent",
                "Do you consent to enabling Autonomy Mode?") ?? Task.FromResult(false));
            if (!confirm) return;
            AutonomyConsentGiven = true;
            _settingsService.Current.AutonomyConsentGiven = true;
        }

        AutonomyEnabled = !AutonomyEnabled;
        _settingsService.Current.AutonomyModeEnabled = AutonomyEnabled;
        Save();
        _logger?.Information("Autonomy Mode button toggled: {Enabled}", AutonomyEnabled);
    }

    [RelayCommand]
    private void TestAutonomy()
    {
        _logger?.Information("Autonomy test trigger requested");
    }

    [RelayCommand]
    private void ForceStartAutonomy()
    {
        _logger?.Information("Autonomy force start requested");
    }

    private void LoadFromSettings()
    {
        var s = _settingsService?.Current;
        if (s == null) return;

        AutonomyEnabled = s.AutonomyModeEnabled;
        AutonomyConsentGiven = s.AutonomyConsentGiven;
        AutonomyIntensity = s.AutonomyIntensity;
        AutonomyCooldown = s.AutonomyCooldownSeconds;
        AutonomyInterval = s.AutonomyRandomIntervalSeconds;
        AutonomyIdleTrigger = s.AutonomyIdleTriggerEnabled;
        AutonomyRandomTrigger = s.AutonomyRandomTriggerEnabled;
        AutonomyTimeAware = s.AutonomyTimeAwareEnabled;
        AnnouncementChance = s.AutonomyAnnouncementChance;
        CanTriggerFlash = s.AutonomyCanTriggerFlash;
        CanTriggerVideo = s.AutonomyCanTriggerVideo;
        CanTriggerWebVideo = s.AutonomyCanTriggerWebVideo;
        CanTriggerSubliminal = s.AutonomyCanTriggerSubliminal;
        CanTriggerBubbles = s.AutonomyCanTriggerBubbles;
        CanTriggerMindWipe = s.AutonomyCanTriggerMindWipe;
        CanTriggerLockCard = s.AutonomyCanTriggerLockCard;
        CanTriggerSpiral = s.AutonomyCanTriggerSpiral;
        CanComment = s.AutonomyCanComment;
        CanTriggerPinkFilter = s.AutonomyCanTriggerPinkFilter;
        CanTriggerBouncingText = s.AutonomyCanTriggerBouncingText;
        CanTriggerBubbleCount = s.AutonomyCanTriggerBubbleCount;
    }

    private void Save()
    {
        try { _settingsService?.Save(); }
        catch (Exception ex) { _logger?.Warning(ex, "Failed to save autonomy settings"); }
    }
}
