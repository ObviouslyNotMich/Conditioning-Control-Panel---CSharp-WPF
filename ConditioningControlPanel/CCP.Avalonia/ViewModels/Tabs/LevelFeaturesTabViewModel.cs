using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.LevelFeatures partial.
/// Groups the level-gated mini-features (Bubble Count, Bouncing Text, Mind Wipe, Brain Drain)
/// into a single tab while dedicated feature controls are being ported.
/// </summary>
public partial class LevelFeaturesTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly IAppLogger? _logger;

    private int _playerLevel = 1;

    public const int BubbleCountLevel = 50;
    public const int BouncingTextLevel = 60;
    public const int BrainDrainLevel = 70;
    public const int MindWipeLevel = 75;

    public LevelFeaturesTabViewModel() : base("levelfeatures", "Level Features", "🎚️")
    {
    }

    public LevelFeaturesTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        IAppLogger logger) : base("levelfeatures", "Level Features", "🎚️")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;

        PlayerLevel = _settingsService?.Current?.PlayerLevel ?? 1;
    }

    #region Level / Lock State

    public int PlayerLevel
    {
        get => _playerLevel;
        set
        {
            if (SetProperty(ref _playerLevel, value))
            {
                OnPropertyChanged(nameof(IsBubbleCountLocked));
                OnPropertyChanged(nameof(IsBouncingTextLocked));
                OnPropertyChanged(nameof(IsBrainDrainLocked));
                OnPropertyChanged(nameof(IsMindWipeLocked));
                OnPropertyChanged(nameof(BubbleCountUnlockText));
                OnPropertyChanged(nameof(BouncingTextUnlockText));
                OnPropertyChanged(nameof(BrainDrainUnlockText));
                OnPropertyChanged(nameof(MindWipeUnlockText));
            }
        }
    }

    public int BubbleCountLockLevel => BubbleCountLevel;
    public int BouncingTextLockLevel => BouncingTextLevel;
    public int BrainDrainLockLevel => BrainDrainLevel;
    public int MindWipeLockLevel => MindWipeLevel;

    public bool IsBubbleCountLocked => !IsLevelUnlocked(BubbleCountLevel);
    public bool IsBouncingTextLocked => !IsLevelUnlocked(BouncingTextLevel);
    public bool IsBrainDrainLocked => !IsLevelUnlocked(BrainDrainLevel);
    public bool IsMindWipeLocked => !IsLevelUnlocked(MindWipeLevel);

    public string BubbleCountUnlockText => Loc.GetF("feature_unlocks_at_level_fmt", BubbleCountLevel);
    public string BouncingTextUnlockText => Loc.GetF("feature_unlocks_at_level_fmt", BouncingTextLevel);
    public string BrainDrainUnlockText => Loc.GetF("feature_unlocks_at_level_fmt", BrainDrainLevel);
    public string MindWipeUnlockText => Loc.GetF("feature_unlocks_at_level_fmt", MindWipeLevel);

    private bool IsLevelUnlocked(int requiredLevel)
    {
        if (_settingsService?.Current is not null)
            return _settingsService.Current.IsLevelUnlocked(requiredLevel);

        return PlayerLevel >= requiredLevel;
    }

    #endregion

    #region Bubble Count (Level 50)

    public bool BubbleCountEnabled
    {
        get => _settingsService?.Current?.BubbleCountEnabled ?? false;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.BubbleCountEnabled = value;
            OnPropertyChanged();
            Save();
            _logger?.Information("Bubble Count toggled: {Enabled}", value);
        }
    }

    public int BubbleCountFrequency
    {
        get => _settingsService?.Current?.BubbleCountFrequency ?? 3;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.BubbleCountFrequency = value;
            OnPropertyChanged();
            Save();
        }
    }

    public int BubbleCountDifficulty
    {
        get => _settingsService?.Current?.BubbleCountDifficulty ?? 50;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.BubbleCountDifficulty = value;
            OnPropertyChanged();
            Save();
        }
    }

    public bool BubbleCountStrictLock
    {
        get => _settingsService?.Current?.BubbleCountStrictLock ?? false;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.BubbleCountStrictLock = value;
            OnPropertyChanged();
            Save();
        }
    }

    [RelayCommand]
    private async Task ToggleBubbleCountStrictAsync()
    {
        if (BubbleCountStrictLock)
        {
            var confirmed = await (_dialogService?.ShowConfirmationAsync(
                Loc.Get("dialog_strict_bubble_count_title"),
                Loc.Get("dialog_strict_bubble_count_body")) ?? Task.FromResult(false));
            if (!confirmed)
            {
                BubbleCountStrictLock = false;
                return;
            }
        }

        Save();
    }

    [RelayCommand]
    private async Task TestBubbleCountAsync()
    {
        _logger?.Information("Test bubble count requested");
        await ShowNotImplementedAsync(Loc.Get("feature_bubble_count"));
    }

    [RelayCommand]
    private void ToggleBubbleCountEnabled()
    {
        BubbleCountEnabled = !BubbleCountEnabled;
    }

    [RelayCommand]
    private async Task OpenBubbleCountDetailsAsync()
    {
        await ShowNotImplementedAsync(Loc.Get("feature_bubble_count"));
    }

    #endregion

    #region Bouncing Text (Level 60)

    public bool BouncingTextEnabled
    {
        get => _settingsService?.Current?.BouncingTextEnabled ?? false;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.BouncingTextEnabled = value;
            OnPropertyChanged();
            Save();
            _logger?.Information("Bouncing Text toggled: {Enabled}", value);
        }
    }

    public int BouncingTextSpeed
    {
        get => _settingsService?.Current?.BouncingTextSpeed ?? 50;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.BouncingTextSpeed = value;
            OnPropertyChanged();
            Save();
        }
    }

    public int BouncingTextSize
    {
        get => _settingsService?.Current?.BouncingTextSize ?? 48;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.BouncingTextSize = value;
            OnPropertyChanged();
            Save();
        }
    }

    public bool BouncingTextAlwaysOnTop
    {
        get => _settingsService?.Current?.BouncingTextAlwaysOnTop ?? false;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.BouncingTextAlwaysOnTop = value;
            OnPropertyChanged();
            Save();
        }
    }

    [RelayCommand]
    private async Task EditBouncingTextPhrasesAsync()
    {
        _logger?.Information("Edit bouncing text phrases requested");
        await ShowNotImplementedAsync(Loc.Get("feature_bouncing_text"));
    }

    [RelayCommand]
    private async Task TestBouncingTextAsync()
    {
        _logger?.Information("Test bouncing text requested");
        await ShowNotImplementedAsync(Loc.Get("feature_bouncing_text"));
    }

    [RelayCommand]
    private void ToggleBouncingTextEnabled()
    {
        BouncingTextEnabled = !BouncingTextEnabled;
    }

    [RelayCommand]
    private async Task OpenBouncingTextDetailsAsync()
    {
        await ShowNotImplementedAsync(Loc.Get("feature_bouncing_text"));
    }

    #endregion

    #region Mind Wipe (Level 75)

    public bool MindWipeEnabled
    {
        get => _settingsService?.Current?.MindWipeEnabled ?? false;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.MindWipeEnabled = value;
            OnPropertyChanged();
            Save();
            _logger?.Information("Mind Wipe toggled: {Enabled}", value);
        }
    }

    public int MindWipeFrequency
    {
        get => _settingsService?.Current?.MindWipeFrequency ?? 2;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.MindWipeFrequency = value;
            OnPropertyChanged();
            Save();
        }
    }

    public int MindWipeVolume
    {
        get => _settingsService?.Current?.MindWipeVolume ?? 50;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.MindWipeVolume = value;
            OnPropertyChanged();
            Save();
        }
    }

    public bool MindWipeLoop
    {
        get => _settingsService?.Current?.MindWipeLoop ?? false;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.MindWipeLoop = value;
            OnPropertyChanged();
            Save();
            _logger?.Information("Mind Wipe loop toggled: {Looping}", value);
        }
    }

    [RelayCommand]
    private async Task TestMindWipeAsync()
    {
        _logger?.Information("Test mind wipe requested");
        await ShowNotImplementedAsync(Loc.Get("feature_mind_wipe"));
    }

    [RelayCommand]
    private void ToggleMindWipeEnabled()
    {
        MindWipeEnabled = !MindWipeEnabled;
    }

    [RelayCommand]
    private async Task OpenMindWipeDetailsAsync()
    {
        await ShowNotImplementedAsync(Loc.Get("feature_mind_wipe"));
    }

    #endregion

    #region Brain Drain (Level 70)

    public bool BrainDrainEnabled
    {
        get => _settingsService?.Current?.BrainDrainEnabled ?? false;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.BrainDrainEnabled = value;
            OnPropertyChanged();
            Save();
            _logger?.Information("Brain Drain toggled: {Enabled}", value);
        }
    }

    public int BrainDrainIntensity
    {
        get => _settingsService?.Current?.BrainDrainIntensity ?? 50;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.BrainDrainIntensity = value;
            OnPropertyChanged();
            Save();
        }
    }

    public bool BrainDrainHighRefresh
    {
        get => _settingsService?.Current?.BrainDrainHighRefresh ?? false;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.BrainDrainHighRefresh = value;
            OnPropertyChanged();
            Save();
            _logger?.Information("Brain Drain High Refresh toggled: {Enabled}", value);
        }
    }

    [RelayCommand]
    private void ToggleBrainDrainEnabled()
    {
        BrainDrainEnabled = !BrainDrainEnabled;
    }

    [RelayCommand]
    private async Task OpenBrainDrainDetailsAsync()
    {
        await ShowNotImplementedAsync(Loc.Get("feature_brain_drain"));
    }

    #endregion

    private async Task ShowNotImplementedAsync(string featureName)
    {
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            Loc.GetF("msg_not_implemented_body_fmt", featureName)) ?? Task.CompletedTask);
    }

    private void Save()
    {
        try { _settingsService?.Save(); }
        catch (Exception ex) { _logger?.Warning(ex, "Failed to save settings"); }
    }
}
