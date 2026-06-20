using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Features;

public partial class PinkFilterFeatureControl : UserControl
{
    private readonly ISettingsService _settings;
    private bool _isLoading = true;

    public PinkFilterFeatureControl()
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
            ChkEnable.IsChecked = s.PinkFilterEnabled;
            SliderOpacity.Value = s.PinkFilterOpacity;
            TxtOpacity.Text = $"{s.PinkFilterOpacity}%";
        }
        finally { _isLoading = false; }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.PinkFilterEnabled)
            or nameof(AppSettings.PinkFilterOpacity))
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(LoadFromSettings);
        }
    }

    private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.PinkFilterEnabled = ChkEnable.IsChecked ?? false;
        _settings.Save();

        // TODO: Wire up overlay refresh once Avalonia overlay service is available.
        // try { App.Overlay?.RefreshOverlays(); }
        // catch (Exception ex) { App.Logger?.Warning(ex, "PinkFilter toggle: RefreshOverlays failed"); }
    }

    private void SliderOpacity_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtOpacity.Text = $"{v}%";
        _settings.Current.PinkFilterOpacity = v;
        _settings.Save();

        // TODO: Wire up overlay refresh once Avalonia overlay service is available.
        // try { App.Overlay?.RefreshOverlays(); }
        // catch (Exception ex) { App.Logger?.Warning(ex, "PinkFilter opacity: RefreshOverlays failed"); }
    }
}
