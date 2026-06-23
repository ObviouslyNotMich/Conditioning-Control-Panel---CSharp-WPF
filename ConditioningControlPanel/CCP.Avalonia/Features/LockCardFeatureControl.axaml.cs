using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.LockCard;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Features;

public partial class LockCardFeatureControl : UserControl
{
    private readonly ISettingsService _settings;
    private readonly ILockCardService? _lockCard;
    private readonly ISessionService? _session;
    private readonly ILogger<LockCardFeatureControl>? _logger;
    private bool _isLoading = true;

    public IPlatformCapabilities Capabilities { get; }

    public LockCardFeatureControl()
    {
        _settings = App.Services.GetRequiredService<ISettingsService>();
        _lockCard = App.Services.GetService<ILockCardService>();
        _session = App.Services.GetService<ISessionService>();
        _logger = App.Services.GetRequiredService<ILogger<LockCardFeatureControl>>();
        Capabilities = App.Services.GetRequiredService<IPlatformCapabilities>();
        InitializeComponent();
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

        LiveApply(on);
    }

    private void LiveApply(bool on)
    {
        if (_session?.State != SessionState.Running || _lockCard == null) return;

        try
        {
            if (on && !_lockCard.IsRunning) _lockCard.Start();
            else if (!on && _lockCard.IsRunning) _lockCard.Stop();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "LockCard enable toggle: live apply failed");
        }
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

    private async void ChkStrict_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings.Current == null) return;
        var s = _settings.Current;
        var on = ChkStrict.IsChecked ?? false;

        if (on)
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner != null)
            {
                var confirmed = await WarningDialog.ShowDoubleWarning(
                    owner,
                    "Strict Lock Card",
                    "• You will NOT be able to escape lock cards with ESC\n" +
                    "• You MUST type the phrase the required number of times\n" +
                    "• This can be very restrictive!");

                if (!confirmed)
                {
                    _isLoading = true;
                    ChkStrict.IsChecked = false;
                    _isLoading = false;
                    return;
                }
            }
        }

        s.LockCardStrict = on;
        _settings.Save();
    }

    private async void BtnManagePhrases_Click(object? sender, RoutedEventArgs e)
    {
        if (_settings.Current is not { } s) return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        var editor = new TextEditorDialog(Loc.Get("label_lock_card"), s.LockCardPhrases ?? new System.Collections.Generic.Dictionary<string, bool>());
        var result = await editor.ShowDialog<bool?>(owner);
        if (result == true && editor.ResultData != null)
        {
            s.LockCardPhrases = editor.ResultData;
            _settings.Save();
            _logger?.LogInformation("LockCard phrases updated: {Count} items", editor.ResultData.Count);
        }
    }

    private void BtnTest_Click(object? sender, RoutedEventArgs e)
    {
        if (_settings.Current is not { } s) return;
        var enabledPhrases = (s.LockCardPhrases ?? new System.Collections.Generic.Dictionary<string, bool>())
            .Where(p => p.Value).Select(p => p.Key).ToList();
        if (enabledPhrases.Count == 0)
        {
            _logger?.LogWarning("LockCard test: no phrases enabled");
            return;
        }

        try { _lockCard?.TestLockCard(); }
        catch (Exception ex) { _logger?.LogWarning(ex, "LockCard test failed"); }
    }

    private async void BtnColorSettings_Click(object? sender, RoutedEventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        var dialog = new LockCardColorDialog();
        await dialog.ShowDialog<bool?>(owner);
    }
}
