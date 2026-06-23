using System;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;

using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// CCBill compliance — moderation escalation warning modal.
/// </summary>
public partial class ContentPolicyWarningDialog : Window
{
    private readonly ILogger<ContentPolicyWarningDialog> _logger;


private const string PolicyUrl = "https://app.cclabs.app/policies/prohibited-content";

    public ContentPolicyWarningDialog()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<ContentPolicyWarningDialog>>();
}

    public ContentPolicyWarningDialog(int hitCount)
        : this()
    {
        TxtBodyCount.Text = string.Format(
            CultureInfo.CurrentCulture,
            Loc.Get("policy_warning_body_count"),
            hitCount);
    }

    private void BtnOk_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void LnkPolicy_Click(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(PolicyUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "ContentPolicyWarningDialog: failed to open policy URL {Url}", PolicyUrl);
        }
    }
}
