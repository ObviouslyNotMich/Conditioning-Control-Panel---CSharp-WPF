using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.Lab partial.
/// Exposes lockdown, quiz, chaos mode, wallpaper override, and lab session commands.
/// Live services (Lockdown, Quiz, Chaos, Wallpaper) are not abstracted in Core yet,
/// so most commands are stubbed with logging and dialogs.
/// </summary>
public partial class LabTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly IAppLogger? _logger;

    public LabTabViewModel() : base("lab", "Lab", "🧪")
    {
        LockdownDurations = new ObservableCollection<int> { 5, 10, 15, 30, 60 };
        PastQuizzes = new ObservableCollection<PastQuizViewModel>();
    }

    public LabTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        IAppLogger logger) : base("lab", "Lab", "🧪")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;
        LockdownDurations = new ObservableCollection<int> { 5, 10, 15, 30, 60 };
        PastQuizzes = new ObservableCollection<PastQuizViewModel>();
        LoadFromSettings();
    }

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

    [RelayCommand]
    private async Task ActivateLockdownAsync()
    {
        var confirmed = await (_dialogService?.ShowConfirmationAsync(
            "Lockdown Mode",
            $"You will be LOCKED IN for {SelectedLockdownDuration} minutes.\n" +
            "Strict Lock will be FORCED ON. Panic Key will be DISABLED.\n" +
            "Continue?") ?? Task.FromResult(false));
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
            "Down the Rabbit Hole",
            "Chaos mode is not yet available in the Avalonia head.") ?? Task.CompletedTask);
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

    private void LoadFromSettings()
    {
        var s = _settingsService?.Current;
        if (s == null) return;

        PopQuizEnabled = s.PopQuizEnabled;
        PopQuizFrequency = s.PopQuizFrequency;
        WallpaperEnabled = s.WallpaperEnabled;
    }

    private void Save()
    {
        try { _settingsService?.Save(); }
        catch (Exception ex) { _logger?.Warning(ex, "Failed to save lab settings"); }
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
