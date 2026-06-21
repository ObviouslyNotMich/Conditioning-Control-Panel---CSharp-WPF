using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.MindWipe;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Features;

public partial class MindWipeFeatureControl : UserControl
{
    private readonly ISettingsService _settings;
    private readonly IMindWipeService? _mindWipe;
    private readonly ISessionService? _session;
    private readonly IAppLogger? _logger;
    private bool _isLoading = true;

    public MindWipeFeatureControl()
    {
        InitializeComponent();
        _settings = App.Services.GetRequiredService<ISettingsService>();
        _mindWipe = App.Services.GetService<IMindWipeService>();
        _session = App.Services.GetService<ISessionService>();
        _logger = App.Services.GetService<IAppLogger>();
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
            ChkEnable.IsChecked = s.MindWipeEnabled;

            var freq = Math.Clamp(s.MindWipeFrequency, (int)SliderFreq.Minimum, (int)SliderFreq.Maximum);
            SliderFreq.Value = freq;
            TxtFreq.Text = $"{freq}/h";

            var vol = Math.Clamp(s.MindWipeVolume, (int)SliderVolume.Minimum, (int)SliderVolume.Maximum);
            SliderVolume.Value = vol;
            TxtVolume.Text = $"{vol}%";

            ChkLoop.IsChecked = s.MindWipeLoop;
            UpdateAudioFileLabel(s);
        }
        finally { _isLoading = false; }
    }

    private void UpdateAudioFileLabel(AppSettings s)
    {
        var path = s.MindWipeAudioPath;
        TxtAudioFile.Text = !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path)
            ? System.IO.Path.GetFileName(path)
            : LocalizationManager.Instance["label_mind_wipe_default_audio"];
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.MindWipeEnabled)
            or nameof(AppSettings.MindWipeFrequency)
            or nameof(AppSettings.MindWipeVolume)
            or nameof(AppSettings.MindWipeLoop)
            or nameof(AppSettings.MindWipeAudioPath))
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(LoadFromSettings);
        }
    }

    private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var on = ChkEnable.IsChecked ?? false;
        _settings.Current.MindWipeEnabled = on;
        _settings.Save();
        LiveApply(on);
    }

    private void SliderFreq_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = Math.Clamp((int)e.NewValue, (int)SliderFreq.Minimum, (int)SliderFreq.Maximum);
        TxtFreq.Text = $"{v}/h";
        _settings.Current.MindWipeFrequency = v;
        _settings.Save();

        if (_mindWipe?.IsRunning == true && _session?.State == SessionState.Running)
        {
            try
            {
                _mindWipe.Stop();
                _mindWipe.Start(_settings.Current.MindWipeFrequency, _settings.Current.MindWipeVolume / 100.0);
            }
            catch (Exception ex) { _logger?.Warning(ex, "MindWipe frequency change failed"); }
        }
    }

    private void SliderVolume_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = Math.Clamp((int)e.NewValue, (int)SliderVolume.Minimum, (int)SliderVolume.Maximum);
        TxtVolume.Text = $"{v}%";
        _settings.Current.MindWipeVolume = v;
        _settings.Save();

        if (_settings.Current.MindWipeLoop && _session?.State == SessionState.Running)
        {
            try { _mindWipe?.StartLoop(_settings.Current.MindWipeVolume / 100.0); }
            catch (Exception ex) { _logger?.Warning(ex, "MindWipe volume/loop change failed"); }
        }
    }

    private void ChkLoop_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var looping = ChkLoop.IsChecked ?? false;
        _settings.Current.MindWipeLoop = looping;
        _settings.Save();

        if (_session?.State != SessionState.Running || _mindWipe == null) return;
        try
        {
            if (looping)
                _mindWipe.StartLoop(_settings.Current.MindWipeVolume / 100.0);
            else
                _mindWipe.StopLoop();
        }
        catch (Exception ex) { _logger?.Warning(ex, "MindWipe loop toggle failed"); }
    }

    private void BtnTest_Click(object? sender, RoutedEventArgs e)
    {
        try { _mindWipe?.TriggerOnce(); }
        catch (Exception ex) { _logger?.Warning(ex, "MindWipe TriggerOnce failed"); }
    }

    private void LiveApply(bool on)
    {
        if (_session?.State != SessionState.Running || _mindWipe == null) return;

        try
        {
            if (on && !_mindWipe.IsRunning)
            {
                if (_settings.Current?.MindWipeLoop == true)
                    _mindWipe.StartLoop((_settings.Current?.MindWipeVolume ?? 50) / 100.0);
                else
                    _mindWipe.Start(_settings.Current?.MindWipeFrequency ?? 10, (_settings.Current?.MindWipeVolume ?? 50) / 100.0);
            }
            else if (!on && _mindWipe.IsRunning)
            {
                _mindWipe.Stop();
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "MindWipe enable toggle: live apply failed");
        }
    }

    private async void BtnSelectAudio_Click(object? sender, RoutedEventArgs e)
    {
        if (_settings.Current is not { } s) return;

        try
        {
            var dialog = App.Services.GetRequiredService<IDialogService>();
            var result = await dialog.ShowOpenFileDialogAsync(
                LocalizationManager.Instance["title_select_mind_wipe_audio"],
                new FileFilter[]
                {
                    new(LocalizationManager.Instance["label_audio_files"], new[] { "mp3", "wav", "ogg", "flac", "m4a" }),
                    new(LocalizationManager.Instance["label_all_files"], new[] { "*" })
                });

            if (result.Count == 0) return;

            s.MindWipeAudioPath = result[0];
            _settings.Save();
            ApplyAudioChange(s);
        }
        catch (Exception ex)
        {
            // TODO: App.Logger?.Warning(ex, "MindWipe audio select failed");
            Console.WriteLine($"[MindWipeFeatureControl] Select failed: {ex.Message}");
        }
    }

    private void BtnClearAudio_Click(object? sender, RoutedEventArgs e)
    {
        if (_settings.Current is not { } s) return;
        if (string.IsNullOrEmpty(s.MindWipeAudioPath)) return;

        s.MindWipeAudioPath = "";
        _settings.Save();
        ApplyAudioChange(s);
    }

    private void ApplyAudioChange(AppSettings s)
    {
        UpdateAudioFileLabel(s);

        // TODO: reload mind-wipe audio once App.MindWipe is available in Avalonia.
        // try
        // {
        //     App.MindWipe?.ReloadAudioFiles();
        //     if (s.MindWipeLoop && (App.MindWipe?.IsLooping ?? false))
        //     {
        //         App.MindWipe?.StopLoop();
        //         App.MindWipe?.StartLoop(s.MindWipeVolume / 100.0);
        //     }
        // }
        // catch (Exception ex) { App.Logger?.Warning(ex, "MindWipe audio change failed"); }
    }
}
