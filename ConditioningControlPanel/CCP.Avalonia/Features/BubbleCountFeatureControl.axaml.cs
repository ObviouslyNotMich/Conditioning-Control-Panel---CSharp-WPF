using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Sessions;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Features;

public partial class BubbleCountFeatureControl : UserControl
{
    private readonly ISettingsService _settings;
    private readonly IBubbleCountService _bubbleCount;
    private readonly ISessionService _sessionService;
    private readonly IDialogService _dialogService;
    private bool _isLoading = true;

    public BubbleCountFeatureControl()
    {
        InitializeComponent();
        _settings = App.Services.GetRequiredService<ISettingsService>();
        _bubbleCount = App.Services.GetRequiredService<IBubbleCountService>();
        _sessionService = App.Services.GetRequiredService<ISessionService>();
        _dialogService = App.Services.GetRequiredService<IDialogService>();
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
            ChkEnable.IsChecked = s.BubbleCountEnabled;
            SliderFreq.Value = s.BubbleCountFrequency;
            TxtFreq.Text = s.BubbleCountFrequency.ToString();

            foreach (var item in CmbDifficulty.Items)
            {
                if (item is ComboBoxItem cbi &&
                    cbi.Tag is string tag &&
                    int.TryParse(tag, out var val) &&
                    val == s.BubbleCountDifficulty)
                {
                    CmbDifficulty.SelectedItem = item;
                    break;
                }
            }

            ChkStrict.IsChecked = s.BubbleCountStrictLock;
        }
        finally { _isLoading = false; }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.BubbleCountEnabled)
            or nameof(AppSettings.BubbleCountFrequency)
            or nameof(AppSettings.BubbleCountDifficulty)
            or nameof(AppSettings.BubbleCountStrictLock))
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(LoadFromSettings);
        }
    }

    private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var s = _settings.Current;
        var on = ChkEnable.IsChecked ?? false;
        s.BubbleCountEnabled = on;
        _settings.Save();

        if (_sessionService.State == SessionState.Running)
        {
            if (on)
                _bubbleCount.Start();
            else
                _bubbleCount.Stop();
        }
    }

    private void SliderFreq_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var s = _settings.Current;
        var v = (int)e.NewValue;
        TxtFreq.Text = v.ToString();
        s.BubbleCountFrequency = v;

        _settings.Save();
        _bubbleCount.RefreshSchedule();
    }

    private void CmbDifficulty_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        if (CmbDifficulty.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            int.TryParse(tag, out var difficulty))
        {
            _settings.Current.BubbleCountDifficulty = difficulty;
            _settings.Save();
        }
    }

    private async void ChkStrict_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var s = _settings.Current;
        var on = ChkStrict.IsChecked ?? false;

        if (on)
        {
            bool confirmed = await ShowStrictWarningAsync();
            if (!confirmed)
            {
                _isLoading = true;
                ChkStrict.IsChecked = false;
                _isLoading = false;
                return;
            }
        }

        s.BubbleCountStrictLock = on;
        _settings.Save();
    }

    private async Task<bool> ShowStrictWarningAsync()
    {
        return await _dialogService.ShowConfirmationAsync(
            "Strict Bubble Count",
            "Enabling strict mode can be very restrictive.\n\n" +
            "• You must complete the required count before continuing.\n\n" +
            "Are you sure?");
    }

    private void BtnTest_Click(object? sender, RoutedEventArgs e)
    {
        _bubbleCount.TriggerGame(forceTest: true);
    }
}
