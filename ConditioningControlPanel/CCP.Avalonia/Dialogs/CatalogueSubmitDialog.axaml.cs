using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;

namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Avalonia port of the catalogue submission affirmation dialog.
/// </summary>
public partial class CatalogueSubmitDialog : Window
{
    public bool Confirmed { get; private set; }

    public CatalogueSubmitDialog()
    {
        InitializeComponent();
    }

    public CatalogueSubmitDialog(string enhancementName)
    {
        InitializeComponent();

        TxtSubtitle.Text = string.IsNullOrWhiteSpace(enhancementName)
            ? string.Empty
            : Loc.GetF("dialog_catalogue_submit_subtitle_fmt", enhancementName);
    }

    private void ChkAffirm_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        BtnSubmit.IsEnabled = ChkAffirm.IsChecked == true;
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close(false);
    }

    private void BtnSubmit_Click(object? sender, RoutedEventArgs e)
    {
        if (ChkAffirm.IsChecked != true)
            return;

        Confirmed = true;
        Close(true);
    }

    private void LinkGuidelines_Click(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://app.cclabs.app/catalogue/guidelines",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open guidelines: {ex.Message}");
        }
    }
}
