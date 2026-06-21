using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia view-model for the Lockdown tab. Manages the timed strict-lock session
/// UI and delegates runtime behavior to <see cref="ILockdownService"/>.
/// </summary>
public partial class LockdownTabViewModel : TabItemViewModel
{
    private readonly ILockdownService? _lockdownService;
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly IAppLogger? _logger;

    /// <summary>
    /// Design-time constructor.
    /// </summary>
    public LockdownTabViewModel() : base("lockdown", "Lockdown", "🔒", TabCapabilityRequirements.Desktop)
    {
        DurationOptions = new ObservableCollection<DurationOption>
        {
            new(5, "5 minutes"),
            new(10, "10 minutes"),
            new(15, "15 minutes"),
            new(30, "30 minutes"),
            new(60, "1 hour"),
            new(120, "2 hours"),
            new(240, "4 hours")
        };
        SelectedDurationIndex = 1;
        TimerText = "00:00";
        WarningText = "";
        ExitInputText = "";
        IsPremiumLocked = false;
    }

    public LockdownTabViewModel(
        ILockdownService lockdownService,
        ISettingsService settingsService,
        IDialogService dialogService,
        IAppLogger logger)
        : base("lockdown", "Lockdown", "🔒", TabCapabilityRequirements.Desktop)
    {
        _lockdownService = lockdownService;
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;

        DurationOptions = new ObservableCollection<DurationOption>
        {
            new(5, "5 minutes"),
            new(10, "10 minutes"),
            new(15, "15 minutes"),
            new(30, "30 minutes"),
            new(60, "1 hour"),
            new(120, "2 hours"),
            new(240, "4 hours")
        };
        SelectedDurationIndex = 1;
        TimerText = "00:00";
        WarningText = "";
        ExitInputText = "";

        var settings = _settingsService.Current;
        IsPremiumLocked = settings == null || !(settings.HasLinkedPatreon || settings.HasLinkedDiscord);
        if (settings != null)
        {
            settings.PropertyChanged += OnSettingsPropertyChanged;
        }

        _lockdownService.LockdownActivated += OnLockdownActivated;
        _lockdownService.LockdownDeactivated += OnLockdownDeactivated;
        _lockdownService.CountdownTick += OnCountdownTick;

        IsActive = _lockdownService.IsActive;
        if (_lockdownService.IsActive)
        {
            TimerText = FormatRemaining(_lockdownService.Remaining);
        }

        UpdateVisibility();
    }

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string _timerText = "00:00";

    [ObservableProperty]
    private bool _setupVisible = true;

    [ObservableProperty]
    private bool _activeVisible;

    [ObservableProperty]
    private bool _exitInputVisible;

    [ObservableProperty]
    private string _exitInputText = "";

    [ObservableProperty]
    private int _selectedDurationIndex;

    [ObservableProperty]
    private ObservableCollection<DurationOption> _durationOptions = new();

    [ObservableProperty]
    private bool _isPremiumLocked;

    [ObservableProperty]
    private string _warningText = "";

    [RelayCommand]
    private void Activate()
    {
        if (_lockdownService == null) return;
        if (SelectedDurationIndex < 0 || SelectedDurationIndex >= DurationOptions.Count)
            return;

        var minutes = DurationOptions[SelectedDurationIndex].Minutes;
        _lockdownService.Activate(TimeSpan.FromMinutes(minutes));
    }

    [RelayCommand]
    private async Task AbortAsync()
    {
        if (_lockdownService == null) return;

        var confirmed = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("dialog_title_abort_lockdown"),
            Loc.Get("dialog_message_abort_lockdown")) ?? Task.FromResult(false));

        if (confirmed)
        {
            _lockdownService.Deactivate();
        }
    }

    [RelayCommand]
    private void ToggleExitInput()
    {
        ExitInputVisible = !ExitInputVisible;
    }

    [RelayCommand]
    private void SubmitExitPhrase()
    {
        if (_lockdownService == null) return;

        var matched = _lockdownService.TryExitWithPhrase(ExitInputText);
        ExitInputText = "";

        if (!matched)
        {
            WarningText = Loc.Get("tab_lockdown_wrong_secret_phrase_text");
        }
        else
        {
            WarningText = "";
            ExitInputVisible = false;
        }
    }

    [RelayCommand]
    private async Task UnlockAsync()
    {
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("dialog_title_premium_locked"),
            Loc.Get("dialog_message_lockdown_premium_locked"),
            DialogSeverity.Info) ?? Task.CompletedTask);
    }

    private void OnLockdownActivated()
    {
        IsActive = true;
        UpdateVisibility();
    }

    private void OnLockdownDeactivated()
    {
        IsActive = false;
        TimerText = "00:00";
        ExitInputVisible = false;
        WarningText = "";
        UpdateVisibility();
    }

    private void OnCountdownTick(TimeSpan remaining)
    {
        TimerText = FormatRemaining(remaining);
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.HasLinkedPatreon) or nameof(AppSettings.HasLinkedDiscord))
        {
            var settings = _settingsService?.Current;
            if (settings != null)
            {
                IsPremiumLocked = !(settings.HasLinkedPatreon || settings.HasLinkedDiscord);
            }
        }
    }

    private void UpdateVisibility()
    {
        SetupVisible = !IsActive;
        ActiveVisible = IsActive;
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        var total = remaining.Duration();
        return $"{(int)total.TotalMinutes:D2}:{total.Seconds:D2}";
    }
}

/// <summary>
/// Simple duration choice for the Lockdown duration ComboBox.
/// </summary>
public sealed record DurationOption(int Minutes, string Label);
