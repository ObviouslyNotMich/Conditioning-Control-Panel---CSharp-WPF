using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Features;

public partial class AttentionCheckFeatureControl : UserControl
{
    private readonly ISettingsService _settings;
    private bool _isLoading = true;

    public AttentionCheckFeatureControl()
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
            ChkEnable.IsChecked = s.AttentionCheckEnabled;
            SliderMin.Value = s.AttentionCheckMinPerSession;
            TxtMin.Text = s.AttentionCheckMinPerSession.ToString();
            SliderMax.Value = s.AttentionCheckMaxPerSession;
            TxtMax.Text = s.AttentionCheckMaxPerSession.ToString();
            SliderGrace.Value = s.AttentionCheckGraceMs;
            TxtGrace.Text = $"{s.AttentionCheckGraceMs} ms";
            CmbFailMode.SelectedIndex = s.AttentionCheckFailMode switch
            {
                AppSettings.AttentionCheckFailModeKind.XpPenalty => 0,
                AppSettings.AttentionCheckFailModeKind.LockCard => 1,
                _ => 2,
            };
            CmbScope.SelectedIndex = s.AttentionCheckScope == AppSettings.AttentionCheckScopeKind.Always ? 0 : 1;
        }
        finally { _isLoading = false; }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.AttentionCheckEnabled)
            or nameof(AppSettings.AttentionCheckMinPerSession)
            or nameof(AppSettings.AttentionCheckMaxPerSession)
            or nameof(AppSettings.AttentionCheckGraceMs)
            or nameof(AppSettings.AttentionCheckFailMode)
            or nameof(AppSettings.AttentionCheckScope))
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(LoadFromSettings);
        }
    }

    private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.AttentionCheckEnabled = ChkEnable.IsChecked ?? false;
        _settings.Save();
    }

    private void SliderMin_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var s = _settings.Current;
        var v = (int)e.NewValue;
        TxtMin.Text = v.ToString();
        s.AttentionCheckMinPerSession = v;
        if (s.AttentionCheckMaxPerSession < v)
        {
            s.AttentionCheckMaxPerSession = v;
            SliderMax.Value = v;
            TxtMax.Text = v.ToString();
        }
        _settings.Save();
    }

    private void SliderMax_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var s = _settings.Current;
        var v = (int)e.NewValue;
        TxtMax.Text = v.ToString();
        s.AttentionCheckMaxPerSession = v;
        if (s.AttentionCheckMinPerSession > v)
        {
            s.AttentionCheckMinPerSession = v;
            SliderMin.Value = v;
            TxtMin.Text = v.ToString();
        }
        _settings.Save();
    }

    private void SliderGrace_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtGrace.Text = $"{v} ms";
        _settings.Current.AttentionCheckGraceMs = v;
        _settings.Save();
    }

    private void CmbFailMode_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.AttentionCheckFailMode = CmbFailMode.SelectedIndex switch
        {
            1 => AppSettings.AttentionCheckFailModeKind.LockCard,
            2 => AppSettings.AttentionCheckFailModeKind.None,
            _ => AppSettings.AttentionCheckFailModeKind.XpPenalty,
        };
        _settings.Save();
    }

    private void CmbScope_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.AttentionCheckScope = CmbScope.SelectedIndex == 1
            ? AppSettings.AttentionCheckScopeKind.DuringSessionsOnly
            : AppSettings.AttentionCheckScopeKind.Always;
        _settings.Save();
    }
}
