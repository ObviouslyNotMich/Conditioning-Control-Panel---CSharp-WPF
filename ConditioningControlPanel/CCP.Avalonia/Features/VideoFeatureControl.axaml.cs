using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Video;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Features;

public partial class VideoFeatureControl : UserControl
{
    private readonly ISettingsService _settings;
    private readonly IVideoService? _video;
    private readonly ISessionService? _session;
    private readonly ILogger<VideoFeatureControl>? _logger;
    private readonly IDialogService _dialogService;
    private readonly IInteractionQueueService? _interactionQueue;
    private bool _isLoading = true;

    public VideoFeatureControl()
    {
        InitializeComponent();
        _settings = App.Services.GetRequiredService<ISettingsService>();
        _video = App.Services.GetService<IVideoService>();
        _session = App.Services.GetService<ISessionService>();
        _logger = App.Services.GetRequiredService<ILogger<VideoFeatureControl>>();
        _dialogService = App.Services.GetRequiredService<IDialogService>();
        _interactionQueue = App.Services.GetService<IInteractionQueueService>();
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
            Dispatcher.UIThread.Post(LoadFromSettings);
        }
    }

    private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var on = ChkEnable.IsChecked ?? false;
        _settings.Current.MandatoryVideosEnabled = on;
        _settings.Save();
        LiveApply(on);
    }

    private void LiveApply(bool on)
    {
        if (_session?.State != SessionState.Running || _video == null) return;

        try
        {
            if (on && !_video.IsRunning) _video.Start();
            else if (!on && _video.IsRunning) _video.Stop();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Video enable toggle: live apply failed");
        }
    }

    private void SliderPerHour_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtPerHour.Text = v.ToString();
        _settings.Current.VideosPerHour = v;
        _settings.Save();
    }

    private async void ChkStrict_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var on = ChkStrict.IsChecked ?? false;

        if (on)
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                Loc.Get("setting_strict_lock"),
                "Enabling Strict Lock is a dangerous setting.\n\n" +
                "• You will NOT be able to escape videos with ESC\n" +
                "• You MUST watch the video to completion\n" +
                "• This can be very restrictive!\n\n" +
                "Are you absolutely sure?");
            if (!confirmed)
            {
                _isLoading = true;
                ChkStrict.IsChecked = false;
                _isLoading = false;
                return;
            }
        }

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

    private async void BtnManageAttention_Click(object? sender, RoutedEventArgs e)
    {
        if (_settings.Current is not { } s) return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var dialog = new TextEditorDialog(Loc.Get("label_attention_targets"), s.AttentionPool);
        var result = await dialog.ShowDialog<bool?>(owner);
        if (result == true && dialog.ResultData != null)
        {
            s.AttentionPool = dialog.ResultData;
            _settings.Save();
            _logger?.LogInformation("Attention pool updated: {Count} items", dialog.ResultData.Count);
        }
    }

    private async void BtnAttentionStyle_Click(object? sender, RoutedEventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var dialog = new AttentionTargetEditorDialog();
        await dialog.ShowDialog<bool?>(owner);
    }

    private async void BtnTestVideo_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            // Mirror the WPF safety checks: if a video is actually playing or the
            // interaction queue is blocked, offer to force-cleanup before testing.
            if (_video?.IsPlaying == true)
            {
                var proceed = await _dialogService.ShowConfirmationAsync(
                    Loc.Get("title_confirm"),
                    Loc.Get("msg_video_test_already_playing"));
                if (!proceed) return;

                _logger?.LogWarning("User requested force reset of stuck video state");
                _video?.ForceCleanup();
                _interactionQueue?.ForceReset();
            }

            if (_interactionQueue is { IsBusy: true })
            {
                var proceed = await _dialogService.ShowConfirmationAsync(
                    Loc.Get("title_confirm"),
                    Loc.Get("msg_video_test_queue_busy"));
                if (!proceed) return;

                _video?.ForceCleanup();
                _interactionQueue.ForceReset();
            }

            _video?.TriggerVideo();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Test video failed");
            await _dialogService.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.GetF("msg_video_test_error", ex.Message),
                DialogSeverity.Error);
        }
    }
}
