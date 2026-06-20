using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Features;

public partial class FlashFeatureControl : UserControl
{
    private readonly ISettingsService _settings;
    private bool _isLoading = true;

    public FlashFeatureControl()
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
            ChkEnable.IsChecked = s.FlashEnabled;
            SliderFrequency.Value = s.FlashFrequency;
            TxtFrequency.Text = s.FlashFrequency.ToString();
            SliderImages.Value = s.SimultaneousImages;
            TxtImages.Text = s.SimultaneousImages.ToString();
            SliderMaxOnScreen.Value = s.HydraLimit;
            TxtMaxOnScreen.Text = s.HydraLimit.ToString();
            ChkClickable.IsChecked = s.FlashClickable;
            ChkCorruption.IsChecked = s.CorruptionMode;
            ChkHydraLinked.IsChecked = s.HydraLinkedTiming;
            ChkGlow.IsChecked = s.FlashGlowEnabled;
            ChkFlashGazePop.IsChecked = s.FlashGazePopEnabled;
            ChkFlashGazeLinger.IsChecked = s.FlashGazeLingerEnabled;
            SliderFlashLingerMs.Value = s.FlashGazeLingerExtensionMs;
            TxtFlashLingerMs.Text = LocalizationManager.Instance.GetF("label_0_1_ms", s.FlashGazeLingerExtensionMs, " ");
        }
        finally { _isLoading = false; }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Reload on any flash-related property; the set is small.
        if (e.PropertyName == nameof(AppSettings.FlashEnabled) ||
            e.PropertyName == nameof(AppSettings.FlashFrequency) ||
            e.PropertyName == nameof(AppSettings.SimultaneousImages) ||
            e.PropertyName == nameof(AppSettings.HydraLimit) ||
            e.PropertyName == nameof(AppSettings.FlashClickable) ||
            e.PropertyName == nameof(AppSettings.CorruptionMode) ||
            e.PropertyName == nameof(AppSettings.HydraLinkedTiming) ||
            e.PropertyName == nameof(AppSettings.FlashGlowEnabled) ||
            e.PropertyName == nameof(AppSettings.FlashGazePopEnabled) ||
            e.PropertyName == nameof(AppSettings.FlashGazeLingerEnabled) ||
            e.PropertyName == nameof(AppSettings.FlashGazeLingerExtensionMs))
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(LoadFromSettings);
        }
    }

    private void ChkFlashGazePop_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.FlashGazePopEnabled = ChkFlashGazePop.IsChecked ?? false;
        _settings.Save();
    }

    private void ChkFlashGazeLinger_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.FlashGazeLingerEnabled = ChkFlashGazeLinger.IsChecked ?? false;
        _settings.Save();
    }

    private void SliderFlashLingerMs_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtFlashLingerMs.Text = LocalizationManager.Instance.GetF("label_0_1_ms", v, " ");
        _settings.Current.FlashGazeLingerExtensionMs = v;
        _settings.Save();
    }

    private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var on = ChkEnable.IsChecked ?? false;
        _settings.Current.FlashEnabled = on;
        _settings.Save();

        // TODO: Live-apply: start/stop flash service if engine is running
        // WPF used App.IsEngineRunning / App.Flash which are not available in Avalonia yet.
        // if (App.IsEngineRunning)
        // {
        //     if (on)
        //         App.Flash?.Start();
        //     else
        //         App.Flash?.Stop();
        // }
    }

    private void SliderFrequency_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtFrequency.Text = v.ToString();
        _settings.Current.FlashFrequency = v;

        // TODO: WPF called App.Flash?.RefreshSchedule(); App.Logger?.Warning(...)
        // Those services are not available in Avalonia yet.
        _settings.Save();
    }

    private void SliderImages_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtImages.Text = v.ToString();
        _settings.Current.SimultaneousImages = v;
        _settings.Save();
    }

    private void SliderMaxOnScreen_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtMaxOnScreen.Text = v.ToString();
        _settings.Current.HydraLimit = v;
        _settings.Save();
    }

    private void ChkClickable_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.FlashClickable = ChkClickable.IsChecked ?? false;
        _settings.Save();
    }

    private void ChkCorruption_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.CorruptionMode = ChkCorruption.IsChecked ?? false;
        _settings.Save();
    }

    private void ChkHydraLinked_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.HydraLinkedTiming = ChkHydraLinked.IsChecked ?? false;
        _settings.Save();
    }

    private void ChkGlow_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.FlashGlowEnabled = ChkGlow.IsChecked ?? false;
        _settings.Save();
    }
}
