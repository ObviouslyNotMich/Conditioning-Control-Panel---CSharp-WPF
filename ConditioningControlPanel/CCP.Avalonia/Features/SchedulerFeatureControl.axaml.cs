using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Features;

public partial class SchedulerFeatureControl : UserControl
{
    private readonly ISettingsService _settings;
    private bool _isLoading = true;

    public SchedulerFeatureControl()
    {
        InitializeComponent();
        _settings = App.Services.GetRequiredService<ISettingsService>();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        LoadFromSettings();
        if (_settings.Current is INotifyPropertyChanged inpc)
            inpc.PropertyChanged += OnSettingsPropertyChanged;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_settings.Current is INotifyPropertyChanged inpc)
            inpc.PropertyChanged -= OnSettingsPropertyChanged;
    }

    private void LoadFromSettings()
    {
        if (_settings.Current is not { } s) return;
        _isLoading = true;
        try
        {
            ChkEnabled.IsChecked = s.SchedulerEnabled;
            TxtStart.Text = s.SchedulerStartTime ?? "00:00";
            TxtEnd.Text = s.SchedulerEndTime ?? "22:00";
            DayMon.IsChecked = s.SchedulerMonday;
            DayTue.IsChecked = s.SchedulerTuesday;
            DayWed.IsChecked = s.SchedulerWednesday;
            DayThu.IsChecked = s.SchedulerThursday;
            DayFri.IsChecked = s.SchedulerFriday;
            DaySat.IsChecked = s.SchedulerSaturday;
            DaySun.IsChecked = s.SchedulerSunday;
        }
        finally { _isLoading = false; }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName?.StartsWith("Scheduler") == true)
            global::Avalonia.Threading.Dispatcher.UIThread.Post(LoadFromSettings);
    }

    private void ChkEnabled_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.SchedulerEnabled = ChkEnabled.IsChecked ?? false;
        _settings.Save();
    }

    private void TxtTime_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.SchedulerStartTime = TxtStart.Text ?? string.Empty;
        _settings.Current.SchedulerEndTime = TxtEnd.Text ?? string.Empty;
        _settings.Save();
    }

    private void Day_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var s = _settings.Current;
        s.SchedulerMonday = DayMon.IsChecked ?? true;
        s.SchedulerTuesday = DayTue.IsChecked ?? true;
        s.SchedulerWednesday = DayWed.IsChecked ?? true;
        s.SchedulerThursday = DayThu.IsChecked ?? true;
        s.SchedulerFriday = DayFri.IsChecked ?? true;
        s.SchedulerSaturday = DaySat.IsChecked ?? true;
        s.SchedulerSunday = DaySun.IsChecked ?? true;
        _settings.Save();
    }
}
