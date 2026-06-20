using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

using IAttentionCheckService = ConditioningControlPanel.IAttentionCheckService;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Modal wrapper around <see cref="Features.AttentionCheckFeatureControl"/>.
/// Opened from the Lab webcam-debug card's "Configure..." button so the
/// full setting surface is reachable without bloating the card itself.
/// </summary>
public partial class AttentionCheckSettingsDialog : Window
{
    private readonly global::ConditioningControlPanel.IAppLogger _logger;


    private readonly IAttentionCheckService _attentionCheck;
public AttentionCheckSettingsDialog()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
_attentionCheck = App.Services.GetRequiredService<IAttentionCheckService>();
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnTestNow_Click(object? sender, RoutedEventArgs e)
    {
        // Close the dialog first so the popup isn't behind it. Fire on
        // the next dispatcher pass so the close completes before the test
        // popup appears.
        Close();
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _attentionCheck.FireNow();
            }
            catch (Exception ex)
            {
                _logger?.Warning("Test now: FireNow failed: {Error}", ex.Message);
            }
        }, global::Avalonia.Threading.DispatcherPriority.Background);
    }
}
