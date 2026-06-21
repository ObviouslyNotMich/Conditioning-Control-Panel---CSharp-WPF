using System;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ConditioningControlPanel.Models;

using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// CCBill AI Content Merchant Addendum — 18+ and content-policy acknowledgement gate.
/// </summary>
public partial class ExplicitContentAcknowledgementDialog : Window
{
    private readonly global::ConditioningControlPanel.IAppLogger _logger;
    private readonly global::ConditioningControlPanel.Core.Services.Settings.ISettingsService _settings;


    private const string PolicyUrl = "https://app.cclabs.app/policies/prohibited-content";

    public ExplicitContentAcknowledgementDialog()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
        _settings = App.Services.GetRequiredService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>();
}

    private void ChkAgeConfirm_CheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (BtnAccept != null)
        {
            BtnAccept.IsEnabled = ChkAgeConfirm?.IsChecked == true;
        }
    }

    private void BtnAccept_Click(object? sender, RoutedEventArgs e)
    {
        if (ChkAgeConfirm?.IsChecked != true)
            return;

        // P2 C3: stamp the audit-trail fields before the caller flips acknowledgement.
        try
        {
            CompanionPromptSettings? promptSettings =
_settings?.Current?.CompanionPrompt;
            if (promptSettings != null)
            {
                promptSettings.ExplicitAcknowledgedAt =
                    DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                promptSettings.ExplicitAcknowledgedLocale = CultureInfo.CurrentCulture.Name;
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "ExplicitContentAcknowledgementDialog: failed to capture ack timestamp/locale");
        }

        Close(true);
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void LnkPolicy_Click(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(PolicyUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "ExplicitContentAcknowledgementDialog: failed to open policy URL {Url}", PolicyUrl);
        }
    }
}
