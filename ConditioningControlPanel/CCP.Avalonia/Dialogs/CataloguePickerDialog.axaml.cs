using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Models;

namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Avalonia port of the catalogue picker dialog.
/// </summary>
public partial class CataloguePickerDialog : Window
{
    public CatalogueEntry? SelectedEntry { get; private set; }

    private readonly string? _htVideoId;

    public CataloguePickerDialog()
    {
        InitializeComponent();
    }

    public CataloguePickerDialog(List<CatalogueEntry> entries, string? htVideoId)
    {
        InitializeComponent();

        _htVideoId = htVideoId;
        TxtSubtitle.Text = Loc.GetF("dialog_catalogue_picker_subtitle_fmt", entries.Count);
        EntriesList.ItemsSource = entries;
    }

    private void EntryRow_Click(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;

        if (sender is Control { DataContext: CatalogueEntry entry })
        {
            Select(entry);
        }
    }

    private void Select(CatalogueEntry entry)
    {
        SelectedEntry = entry;
        Close(true);
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        SelectedEntry = null;
        Close(false);
    }

    private void LinkBrowseWeb_Click(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;

        var url = string.IsNullOrEmpty(_htVideoId)
            ? "https://app.cclabs.app/catalogue"
            : $"https://app.cclabs.app/catalogue?video={Uri.EscapeDataString(_htVideoId)}";

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open browser: {ex.Message}");
        }
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SelectedEntry = null;
            Close(false);
        }
    }
}
