using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Features;

public partial class LockCardFeatureControl : UserControl
{
    private readonly ISettingsService _settings;
    private bool _isLoading = true;

    public IPlatformCapabilities Capabilities { get; }

    public LockCardFeatureControl()
    {
        InitializeComponent();
        _settings = App.Services.GetRequiredService<ISettingsService>();
        Capabilities = App.Services.GetRequiredService<IPlatformCapabilities>();
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
            ChkEnable.IsChecked = s.LockCardEnabled;
            SliderFreq.Value = s.LockCardFrequency;
            TxtFreq.Text = s.LockCardFrequency.ToString();
            SliderRepeats.Value = s.LockCardRepeats;
            TxtRepeats.Text = $"{s.LockCardRepeats}x";
            ChkStrict.IsChecked = s.LockCardStrict;
        }
        finally { _isLoading = false; }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.LockCardEnabled) ||
            e.PropertyName == nameof(AppSettings.LockCardFrequency) ||
            e.PropertyName == nameof(AppSettings.LockCardRepeats) ||
            e.PropertyName == nameof(AppSettings.LockCardStrict))
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(LoadFromSettings);
        }
    }

    private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var s = _settings.Current;
        var on = ChkEnable.IsChecked ?? false;
        s.LockCardEnabled = on;
        _settings.Save();

        // TODO: live-apply lock card service when engine is running
        // (App.IsEngineRunning / App.LockCard are not available in Avalonia yet).
        // if (App.IsEngineRunning)
        // {
        //     if (on) App.LockCard?.Start();
        //     else App.LockCard?.Stop();
        // }
    }

    private void SliderFreq_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var s = _settings.Current;
        var v = (int)e.NewValue;
        TxtFreq.Text = v.ToString();
        s.LockCardFrequency = v;
        _settings.Save();
    }

    private void SliderRepeats_Changed(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var s = _settings.Current;
        var v = (int)e.NewValue;
        TxtRepeats.Text = $"{v}x";
        s.LockCardRepeats = v;
        _settings.Save();
    }

    private void ChkStrict_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var s = _settings.Current;
        var on = ChkStrict.IsChecked ?? false;

        if (on)
        {
            // TODO: WarningDialog.ShowDoubleWarning is WPF-only and has not been ported to Avalonia yet.
            // The strict-lock toggle is accepted without a confirmation prompt for now.
        }

        s.LockCardStrict = on;
        _settings.Save();
    }

    private void BtnManagePhrases_Click(object? sender, RoutedEventArgs e)
    {
        if (_settings.Current is not { } s) return;
        // TODO: TextEditorDialog / lock-card phrase editor is WPF-only and not ported to Avalonia yet.
        // When ported, open it, assign s.LockCardPhrases = editor.ResultData, then call _settings.Save().
        _settings.Save();
    }

    private void BtnTest_Click(object? sender, RoutedEventArgs e)
    {
        if (_settings.Current is not { } s) return;
        var enabledPhrases = (s.LockCardPhrases ?? new System.Collections.Generic.Dictionary<string, bool>())
            .Where(p => p.Value).Select(p => p.Key).ToList();
        if (enabledPhrases.Count == 0)
        {
            // TODO: MessageBox warning ("No phrases enabled...") is WPF-only and not ported to Avalonia yet.
            return;
        }

        // TODO: App.LockCard?.TestLockCard() is WPF-only and not ported to Avalonia yet.
    }

    private void BtnColorSettings_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: LockCardColorDialog is WPF-only and not ported to Avalonia yet.
    }
}
