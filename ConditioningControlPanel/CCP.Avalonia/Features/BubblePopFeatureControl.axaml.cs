using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Features;

public partial class BubblePopFeatureControl : UserControl
{
    private readonly ISettingsService _settings;
    private readonly ISessionService _sessionService;
    private readonly IBubbleService? _bubbles;
    private bool _isLoading = true;

    public BubblePopFeatureControl()
    {
        InitializeComponent();
        _settings = App.Services.GetRequiredService<ISettingsService>();
        _sessionService = App.Services.GetRequiredService<ISessionService>();
        _bubbles = App.Services.GetService<IBubbleService>();
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
            ChkEnable.IsChecked = s.BubblesEnabled;

            var freq = Math.Clamp(s.BubblesFrequency, (int)SliderFreq.Minimum, (int)SliderFreq.Maximum);
            SliderFreq.Value = freq;
            TxtFreq.Text = freq.ToString();

            var vol = Math.Clamp(s.BubblesVolume, (int)SliderVolume.Minimum, (int)SliderVolume.Maximum);
            SliderVolume.Value = vol;
            TxtVolume.Text = $"{vol}%";
        }
        finally { _isLoading = false; }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.BubblesEnabled)
            or nameof(AppSettings.BubblesFrequency)
            or nameof(AppSettings.BubblesVolume))
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(LoadFromSettings);
        }
    }

    private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var on = ChkEnable.IsChecked ?? false;
        _settings.Current.BubblesEnabled = on;
        _settings.Save();

        try
        {
            if (_sessionService.State == SessionState.Running && _bubbles != null)
            {
                if (on && !_bubbles.IsRunning) _bubbles.Start();
                else if (!on && _bubbles.IsRunning) _bubbles.Stop();
            }
        }
        catch (Exception ex)
        {
            global::ConditioningControlPanel.Avalonia.App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Warning(ex, "BubblePopFeatureControl start/stop failed");
        }
    }

    private void SliderFreq_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = Math.Clamp((int)e.NewValue, (int)SliderFreq.Minimum, (int)SliderFreq.Maximum);
        TxtFreq.Text = v.ToString();
        _settings.Current.BubblesFrequency = v;

        try { _bubbles?.RefreshFrequency(); }
        catch (Exception ex)
        {
            global::ConditioningControlPanel.Avalonia.App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Warning(ex, "Bubbles RefreshFrequency failed");
        }

        _settings.Save();
    }

    private void SliderVolume_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = Math.Clamp((int)e.NewValue, (int)SliderVolume.Minimum, (int)SliderVolume.Maximum);
        TxtVolume.Text = $"{v}%";
        _settings.Current.BubblesVolume = v;
        _settings.Save();
    }
}
