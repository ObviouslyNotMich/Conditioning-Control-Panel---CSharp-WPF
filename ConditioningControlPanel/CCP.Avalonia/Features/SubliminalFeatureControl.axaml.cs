using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Features;

public partial class SubliminalFeatureControl : UserControl
{
    private readonly ISettingsService _settings;
    private bool _isLoading = true;

    public SubliminalFeatureControl()
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
            ChkEnable.IsChecked = s.SubliminalEnabled;
            SliderPerMin.Value = s.SubliminalFrequency;
            TxtPerMin.Text = s.SubliminalFrequency.ToString();
            SliderFrames.Value = s.SubliminalDuration;
            TxtFrames.Text = s.SubliminalDuration.ToString();
            SliderOpacity.Value = s.SubliminalOpacity;
            TxtOpacity.Text = $"{s.SubliminalOpacity}%";
            ChkWhispers.IsChecked = s.SubAudioEnabled;
            SliderWhisperVol.Value = s.SubAudioVolume;
            TxtWhisperVol.Text = $"{s.SubAudioVolume}%";
        }
        finally { _isLoading = false; }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.SubliminalEnabled)
            or nameof(AppSettings.SubliminalFrequency)
            or nameof(AppSettings.SubliminalDuration)
            or nameof(AppSettings.SubliminalOpacity)
            or nameof(AppSettings.SubAudioEnabled)
            or nameof(AppSettings.SubAudioVolume))
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(LoadFromSettings);
        }
    }

    private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.SubliminalEnabled = ChkEnable.IsChecked ?? false;
        _settings.Save();

        // TODO: live start/stop subliminal service once the engine is ported to Avalonia.
        // if (App.IsEngineRunning)
        // {
        //     if (_settings.Current.SubliminalEnabled) App.Subliminal?.Start();
        //     else App.Subliminal?.Stop();
        // }
    }

    private void SliderPerMin_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtPerMin.Text = v.ToString();
        _settings.Current.SubliminalFrequency = v;
        _settings.Save();
    }

    private void SliderFrames_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtFrames.Text = v.ToString();
        _settings.Current.SubliminalDuration = v;
        _settings.Save();
    }

    private void SliderOpacity_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtOpacity.Text = $"{v}%";
        _settings.Current.SubliminalOpacity = v;
        _settings.Save();
    }

    private void ChkWhispers_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.SubAudioEnabled = ChkWhispers.IsChecked ?? false;
        _settings.Save();
    }

    private void SliderWhisperVol_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtWhisperVol.Text = $"{v}%";
        _settings.Current.SubAudioVolume = v;
        _settings.Save();
    }

    private void BtnManageMessages_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: port TextEditorDialog to Avalonia so users can edit the subliminal pool.
        // var s = _settings.Current;
        // var oldKeys = new HashSet<string>(s.SubliminalPool.Keys);
        // var dialog = new TextEditorDialog("Subliminal Messages", s.SubliminalPool);
        // if (dialog.ShowDialog() == true && dialog.ResultData != null)
        // {
        //     var newKeys = new HashSet<string>(dialog.ResultData.Keys);
        //     foreach (var key in newKeys)
        //         if (!oldKeys.Contains(key)) s.UserAddedSubliminals.Add(key);
        //     foreach (var key in oldKeys)
        //         if (!newKeys.Contains(key)) s.UserAddedSubliminals.Remove(key);
        //     s.SubliminalPool = dialog.ResultData;
        //     _settings.Save();
        // }
    }

    private void BtnAdvanced_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: port ColorEditorDialog to Avalonia for advanced subliminal visual settings.
    }
}
