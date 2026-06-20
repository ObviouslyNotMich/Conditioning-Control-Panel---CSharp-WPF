using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Features;

public partial class VisualsFeatureControl : UserControl
{
    private readonly ISettingsService _settings;
    private bool _isLoading = true;

    public IPlatformCapabilities Capabilities { get; }

    public VisualsFeatureControl()
    {
        InitializeComponent();
        _settings = App.Services.GetRequiredService<ISettingsService>();
        Capabilities = App.Services.GetRequiredService<IPlatformCapabilities>();
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
            SliderSize.Value = s.ImageScale;
            TxtSize.Text = $"{s.ImageScale}%";
            SliderOpacity.Value = s.FlashOpacity;
            TxtOpacity.Text = $"{s.FlashOpacity}%";
            SliderFade.Value = s.FadeDuration;
            TxtFade.Text = $"{s.FadeDuration}%";
            SliderDuration.Value = s.FlashDuration;
            TxtDuration.Text = $"{s.FlashDuration}s";
            ChkAudio.IsChecked = s.FlashAudioEnabled;
        }
        finally { _isLoading = false; }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.ImageScale)
            or nameof(AppSettings.FlashOpacity)
            or nameof(AppSettings.FadeDuration)
            or nameof(AppSettings.FlashDuration)
            or nameof(AppSettings.FlashAudioEnabled))
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(LoadFromSettings);
        }
    }

    private void SliderSize_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtSize.Text = $"{v}%";
        _settings.Current.ImageScale = v;
        _settings.Save();
    }

    private void SliderOpacity_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtOpacity.Text = $"{v}%";
        _settings.Current.FlashOpacity = v;
        _settings.Save();
    }

    private void SliderFade_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtFade.Text = $"{v}%";
        _settings.Current.FadeDuration = v;
        _settings.Save();
    }

    private void SliderDuration_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtDuration.Text = $"{v}s";
        _settings.Current.FlashDuration = v;
        _settings.Save();
    }

    private void ChkAudio_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.FlashAudioEnabled = ChkAudio.IsChecked ?? false;
        _settings.Save();
    }
}
