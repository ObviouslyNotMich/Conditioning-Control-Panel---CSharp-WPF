using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Features;

public partial class BouncingTextFeatureControl : UserControl
{
    private readonly ISettingsService _settings;
    private bool _isLoading = true;

    public BouncingTextFeatureControl()
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
            ChkEnable.IsChecked = s.BouncingTextEnabled;
            SliderSpeed.Value = s.BouncingTextSpeed;
            TxtSpeed.Text = s.BouncingTextSpeed.ToString();
            SliderSize.Value = s.BouncingTextSize;
            TxtSize.Text = $"{s.BouncingTextSize}%";
            ChkAlwaysOnTop.IsChecked = s.BouncingTextAlwaysOnTop;
        }
        finally { _isLoading = false; }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.BouncingTextEnabled)
            or nameof(AppSettings.BouncingTextSpeed)
            or nameof(AppSettings.BouncingTextSize)
            or nameof(AppSettings.BouncingTextAlwaysOnTop))
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(LoadFromSettings);
        }
    }

    private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var s = _settings.Current;
        var on = ChkEnable.IsChecked ?? false;
        s.BouncingTextEnabled = on;
        _settings.Save();

        // TODO: Live-apply start/stop bouncing text when the Avalonia engine service is available.
        // if (ConditioningControlPanel.App.IsEngineRunning)
        // {
        //     if (on)
        //         ConditioningControlPanel.App.BouncingText?.Start();
        //     else
        //         ConditioningControlPanel.App.BouncingText?.Stop();
        // }
    }

    private void SliderSpeed_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var s = _settings.Current;
        var v = (int)e.NewValue;
        TxtSpeed.Text = v.ToString();
        s.BouncingTextSpeed = v;

        // TODO: Refresh the Avalonia bouncing text renderer when the service is wired up.
        // try { ConditioningControlPanel.App.BouncingText?.Refresh(); }
        // catch (Exception ex) { ConditioningControlPanel.App.Logger?.Warning(ex, "BouncingText Refresh failed"); }

        _settings.Save();
    }

    private void SliderSize_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var s = _settings.Current;
        var v = (int)e.NewValue;
        TxtSize.Text = $"{v}%";
        s.BouncingTextSize = v;

        // TODO: Refresh the Avalonia bouncing text renderer when the service is wired up.
        // try { ConditioningControlPanel.App.BouncingText?.Refresh(); }
        // catch (Exception ex) { ConditioningControlPanel.App.Logger?.Warning(ex, "BouncingText Refresh failed"); }

        _settings.Save();
    }

    private void ChkAlwaysOnTop_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.BouncingTextAlwaysOnTop = ChkAlwaysOnTop.IsChecked ?? false;
        _settings.Save();
    }

    private void BtnEditPhrases_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: Port TextEditorDialog to Avalonia and wire phrase editing.
        // var s = _settings.Current;
        // var editor = new TextEditorDialog("Bouncing Text Phrases", s.BouncingTextPool)
        // {
        //     Owner = TopLevel.GetTopLevel(this) as Window
        // };
        // if (editor.ShowDialog() == true && editor.ResultData != null)
        // {
        //     s.BouncingTextPool = editor.ResultData;
        //     _settings.Save();
        //     ConditioningControlPanel.App.Logger?.Information("Bouncing text phrases updated: {Count} items", editor.ResultData.Count);
        // }
    }
}
