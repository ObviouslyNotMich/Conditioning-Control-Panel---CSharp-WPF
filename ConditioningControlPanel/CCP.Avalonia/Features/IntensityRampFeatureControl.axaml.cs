using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Features;

public partial class IntensityRampFeatureControl : UserControl
{
    private readonly ISettingsService _settings;
    private bool _isLoading = true;

    public IntensityRampFeatureControl()
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
            ChkEnabled.IsChecked = s.IntensityRampEnabled;
            SliderDuration.Value = s.RampDurationMinutes;
            TxtDuration.Text = $"{s.RampDurationMinutes} min";
            SliderMultiplier.Value = s.SchedulerMultiplier;
            TxtMultiplier.Text = $"{s.SchedulerMultiplier:F1}x";
            ChkEndAt.IsChecked = s.EndSessionOnRampComplete;
            ChkLinkFlash.IsChecked = s.RampLinkFlashOpacity;
            ChkLinkSpiral.IsChecked = s.RampLinkSpiralOpacity;
            ChkLinkPink.IsChecked = s.RampLinkPinkFilterOpacity;
            ChkLinkMaster.IsChecked = s.RampLinkMasterAudio;
            ChkLinkSub.IsChecked = s.RampLinkSubliminalAudio;
        }
        finally { _isLoading = false; }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.IntensityRampEnabled)
            or nameof(AppSettings.RampDurationMinutes)
            or nameof(AppSettings.SchedulerMultiplier)
            or nameof(AppSettings.EndSessionOnRampComplete)
            or nameof(AppSettings.RampLinkFlashOpacity)
            or nameof(AppSettings.RampLinkSpiralOpacity)
            or nameof(AppSettings.RampLinkPinkFilterOpacity)
            or nameof(AppSettings.RampLinkMasterAudio)
            or nameof(AppSettings.RampLinkSubliminalAudio))
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(LoadFromSettings);
        }
    }

    private void ChkEnabled_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.IntensityRampEnabled = ChkEnabled.IsChecked ?? false;
        _settings.Save();
    }

    private void SliderDuration_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtDuration.Text = $"{v} min";
        _settings.Current.RampDurationMinutes = v;
        _settings.Save();
    }

    private void SliderMultiplier_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = e.NewValue;
        TxtMultiplier.Text = $"{v:F1}x";
        _settings.Current.SchedulerMultiplier = v;
        _settings.Save();
    }

    private void ChkEndAt_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.EndSessionOnRampComplete = ChkEndAt.IsChecked ?? false;
        _settings.Save();
    }

    private void Link_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.RampLinkFlashOpacity = ChkLinkFlash.IsChecked ?? false;
        _settings.Current.RampLinkSpiralOpacity = ChkLinkSpiral.IsChecked ?? false;
        _settings.Current.RampLinkPinkFilterOpacity = ChkLinkPink.IsChecked ?? false;
        _settings.Current.RampLinkMasterAudio = ChkLinkMaster.IsChecked ?? false;
        _settings.Current.RampLinkSubliminalAudio = ChkLinkSub.IsChecked ?? false;
        _settings.Save();
    }
}
