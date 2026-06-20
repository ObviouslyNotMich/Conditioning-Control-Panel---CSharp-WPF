using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Features;

public partial class VideoFeatureControl : UserControl
{
    private readonly ISettingsService _settings;
    private bool _isLoading = true;

    public VideoFeatureControl()
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
            ChkEnable.IsChecked = s.MandatoryVideosEnabled;
            SliderPerHour.Value = s.VideosPerHour;
            TxtPerHour.Text = s.VideosPerHour.ToString();
            ChkStrict.IsChecked = s.StrictLockEnabled;
            SliderVideoMinDur.Value = s.VideoMinDurationSeconds;
            TxtVideoMinDur.Text = FormatDuration(s.VideoMinDurationSeconds);
            SliderVideoMaxDur.Value = s.VideoMaxDurationSeconds;
            TxtVideoMaxDur.Text = FormatDuration(s.VideoMaxDurationSeconds);
            ChkMiniGame.IsChecked = s.AttentionChecksEnabled;
            SliderTargets.Value = s.AttentionDensity;
            TxtTargets.Text = s.AttentionDensity.ToString();
            ChkRandomize.IsChecked = s.RandomizeAttentionTargets;
            SliderDuration.Value = s.AttentionLifespan;
            TxtDuration.Text = s.AttentionLifespan.ToString();
            SliderTargetSize.Value = s.AttentionSize;
            TxtTargetSize.Text = s.AttentionSize.ToString();
            ChkVideoGazeClick.IsChecked = s.VideoGazeClickEnabled;
        }
        finally { _isLoading = false; }
    }

    private static string FormatDuration(int seconds)
    {
        if (seconds <= 0) return "off";
        if (seconds < 60) return $"{seconds}s";
        var m = seconds / 60;
        var rem = seconds % 60;
        return rem == 0 ? $"{m}m" : $"{m}m {rem}s";
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.MandatoryVideosEnabled) ||
            e.PropertyName == nameof(AppSettings.VideosPerHour) ||
            e.PropertyName == nameof(AppSettings.StrictLockEnabled) ||
            e.PropertyName == nameof(AppSettings.VideoMinDurationSeconds) ||
            e.PropertyName == nameof(AppSettings.VideoMaxDurationSeconds) ||
            e.PropertyName == nameof(AppSettings.AttentionChecksEnabled) ||
            e.PropertyName == nameof(AppSettings.AttentionDensity) ||
            e.PropertyName == nameof(AppSettings.RandomizeAttentionTargets) ||
            e.PropertyName == nameof(AppSettings.AttentionLifespan) ||
            e.PropertyName == nameof(AppSettings.AttentionSize) ||
            e.PropertyName == nameof(AppSettings.VideoGazeClickEnabled))
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(LoadFromSettings);
        }
    }

    private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var on = ChkEnable.IsChecked ?? false;
        _settings.Current.MandatoryVideosEnabled = on;
        _settings.Save();

        // TODO: Live-apply: start/stop video service if engine is running.
        // WPF used App.IsEngineRunning / App.Video which are not available in Avalonia yet.
        // if (App.IsEngineRunning)
        // {
        //     if (on)
        //         App.Video?.Start();
        //     else
        //         App.Video?.Stop();
        // }
    }

    private void SliderPerHour_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtPerHour.Text = v.ToString();
        _settings.Current.VideosPerHour = v;
        _settings.Save();
    }

    private void ChkStrict_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var on = ChkStrict.IsChecked ?? false;

        // TODO: Strict-lock confirmation dialog is Windows-specific and not ported yet.
        // The setting is applied directly without UI blocking for the cross-platform build.

        _settings.Current.StrictLockEnabled = on;
        _settings.Save();
    }

    private void SliderVideoMinDur_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtVideoMinDur.Text = FormatDuration(v);
        _settings.Current.VideoMinDurationSeconds = v;

        // Keep max >= min when both are non-zero, so the user can't trap the queue empty.
        if (_settings.Current.VideoMaxDurationSeconds > 0 && v > 0 && _settings.Current.VideoMaxDurationSeconds < v)
        {
            _settings.Current.VideoMaxDurationSeconds = v;
            SliderVideoMaxDur.Value = v;
            TxtVideoMaxDur.Text = FormatDuration(v);
        }

        _settings.Save();
    }

    private void SliderVideoMaxDur_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtVideoMaxDur.Text = FormatDuration(v);
        _settings.Current.VideoMaxDurationSeconds = v;

        // Keep min <= max when both are non-zero.
        if (_settings.Current.VideoMinDurationSeconds > 0 && v > 0 && _settings.Current.VideoMinDurationSeconds > v)
        {
            _settings.Current.VideoMinDurationSeconds = v;
            SliderVideoMinDur.Value = v;
            TxtVideoMinDur.Text = FormatDuration(v);
        }

        _settings.Save();
    }

    private void ChkMiniGame_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.AttentionChecksEnabled = ChkMiniGame.IsChecked ?? false;
        _settings.Save();
    }

    private void SliderTargets_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtTargets.Text = v.ToString();
        _settings.Current.AttentionDensity = v;
        _settings.Save();
    }

    private void ChkRandomize_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.RandomizeAttentionTargets = ChkRandomize.IsChecked ?? false;
        _settings.Save();
    }

    private void SliderDuration_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtDuration.Text = v.ToString();
        _settings.Current.AttentionLifespan = v;
        _settings.Save();
    }

    private void SliderTargetSize_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtTargetSize.Text = v.ToString();
        _settings.Current.AttentionSize = v;
        _settings.Save();
    }

    private void ChkVideoGazeClick_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.VideoGazeClickEnabled = ChkVideoGazeClick.IsChecked ?? false;
        _settings.Save();
    }

    private void BtnManageAttention_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Port TextEditorDialog for attention phrases from WPF.
        // WPF used:
        // var dialog = new TextEditorDialog("Attention Targets", _settings.Current.AttentionPool)
        // {
        //     Owner = Window.GetWindow(this) ?? Application.Current.MainWindow
        // };
        // if (dialog.ShowDialog() == true && dialog.ResultData != null)
        // {
        //     _settings.Current.AttentionPool = dialog.ResultData;
        //     _settings.Save();
        //     App.Logger?.Information("Attention pool updated: {Count} items", dialog.ResultData.Count);
        // }
    }

    private void BtnAttentionStyle_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Port AttentionTargetEditorDialog from WPF.
        // WPF used:
        // var dialog = new AttentionTargetEditorDialog
        // {
        //     Owner = Window.GetWindow(this) ?? Application.Current.MainWindow
        // };
        // dialog.ShowDialog();
    }

    private void BtnTestVideo_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Port test-video logic from WPF.
        // WPF used MessageBox.Show confirmations, App.Video start/stop/trigger,
        // and App.InteractionQueue which are not available in Avalonia yet.
        // try
        // {
        //     if (App.Video?.IsPlaying == true)
        //     {
        //         var result = MessageBox.Show("A video appears to be playing...", ...);
        //         ...
        //     }
        //     if (App.InteractionQueue != null && !App.InteractionQueue.CanStart)
        //     {
        //         var result = MessageBox.Show($"Another interaction is in progress...", ...);
        //         ...
        //     }
        //     App.Video?.TriggerVideo();
        // }
        // catch (Exception ex)
        // {
        //     App.Logger?.Error(ex, "Error in BtnTestVideo_Click");
        //     MessageBox.Show($"Error triggering video: {ex.Message}", "Error", ...);
        // }
    }
}
