using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;

namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Avalonia port of the asset catalogue submission dialog.
/// </summary>
public partial class AssetSubmitDialog : Window
{
    public bool Confirmed { get; private set; }
    public string Creator { get; private set; } = "";
    public IReadOnlyList<string> Tags { get; private set; } = Array.Empty<string>();

    public AssetSubmitDialog()
    {
        InitializeComponent();
        UpdateSubmitEnabled();
    }

    public AssetSubmitDialog(string assetName, string? defaultCreator = null)
    {
        InitializeComponent();

        TxtSubtitle.Text = string.IsNullOrWhiteSpace(assetName)
            ? string.Empty
            : Loc.GetF("dialog_catalogue_submit_subtitle_fmt", assetName);

        if (!string.IsNullOrWhiteSpace(defaultCreator))
            TxtCreator.Text = defaultCreator.Trim();

        UpdateSubmitEnabled();
    }

    private void ChkAffirm_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        UpdateSubmitEnabled();
    }

    private void TxtCreator_TextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateSubmitEnabled();
    }

    private void UpdateSubmitEnabled()
    {
        BtnSubmit.IsEnabled = ChkAffirm.IsChecked == true
            && !string.IsNullOrWhiteSpace(TxtCreator.Text);
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close(false);
    }

    private void BtnSubmit_Click(object? sender, RoutedEventArgs e)
    {
        if (ChkAffirm.IsChecked != true || string.IsNullOrWhiteSpace(TxtCreator.Text))
            return;

        Creator = TxtCreator.Text.Trim();
        Tags = (TxtTags.Text ?? string.Empty)
            .Split(new[] { ',', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

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
