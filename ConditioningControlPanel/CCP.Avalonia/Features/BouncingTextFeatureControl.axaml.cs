using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.BouncingText;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Features;

public partial class BouncingTextFeatureControl : UserControl
{
    private readonly ISettingsService _settings;
    private readonly IBouncingTextService? _bouncingText;
    private readonly ISessionService? _session;
    private readonly ILogger<BouncingTextFeatureControl>? _logger;
    private bool _isLoading = true;

    public BouncingTextFeatureControl()
    {
        InitializeComponent();
        _settings = App.Services.GetRequiredService<ISettingsService>();
        _bouncingText = App.Services.GetService<IBouncingTextService>();
        _session = App.Services.GetService<ISessionService>();
        _logger = App.Services.GetRequiredService<ILogger<BouncingTextFeatureControl>>();
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
        var on = ChkEnable.IsChecked ?? false;
        _settings.Current.BouncingTextEnabled = on;
        _settings.Save();
        LiveApply(on);
    }

    private void SliderSpeed_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtSpeed.Text = v.ToString();
        _settings.Current.BouncingTextSpeed = v;
        _settings.Save();

        try { _bouncingText?.Refresh(); }
        catch (Exception ex) { _logger?.LogWarning(ex, "BouncingText speed change: Refresh failed"); }
    }

    private void SliderSize_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var v = (int)e.NewValue;
        TxtSize.Text = $"{v}%";
        _settings.Current.BouncingTextSize = v;
        _settings.Save();

        try { _bouncingText?.Refresh(); }
        catch (Exception ex) { _logger?.LogWarning(ex, "BouncingText size change: Refresh failed"); }
    }

    private void LiveApply(bool on)
    {
        if (_session?.State != SessionState.Running || _bouncingText == null) return;

        try
        {
            if (on && !_bouncingText.IsRunning) _bouncingText.Start();
            else if (!on && _bouncingText.IsRunning) _bouncingText.Stop();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "BouncingText enable toggle: live apply failed");
        }
    }

    private void ChkAlwaysOnTop_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        _settings.Current.BouncingTextAlwaysOnTop = ChkAlwaysOnTop.IsChecked ?? false;
        _settings.Save();
    }

    private async void BtnEditPhrases_Click(object? sender, RoutedEventArgs e)
    {
        if (_settings.Current is not { } s) return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        var editor = new TextEditorDialog(Loc.Get("label_bouncing_text"), s.BouncingTextPool);
        var result = await editor.ShowDialog<bool?>(owner);
        if (result == true && editor.ResultData != null)
        {
            s.BouncingTextPool = editor.ResultData;
            _settings.Save();
            _logger?.LogInformation("Bouncing text phrases updated: {Count} items", editor.ResultData.Count);
        }
    }
}
