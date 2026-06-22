using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Dialogs;

public partial class DisplayNameDialog : Window
{
    public string DisplayName { get; private set; } = "";

    private readonly bool _isDeleteMode;
    private readonly IDialogService? _dialogService;
    private int _maxLength = 20;

    public DisplayNameDialog()
    {
        InitializeComponent();
        _dialogService = global::ConditioningControlPanel.Avalonia.App.Services?.GetService<IDialogService>();
        SetupTextChanged();

        Loaded += (_, _) => TxtDisplayName.Focus();
    }

    public DisplayNameDialog(bool isChangeName, string? currentName) : this()
    {
        if (isChangeName)
        {
            TxtTitle.Text = Loc.Get("label_change_your_display_name");
            WarningPanel.IsVisible = false;

            if (!string.IsNullOrEmpty(currentName))
            {
                TxtDisplayName.Text = currentName;
                TxtDisplayName.SelectAll();
            }
        }
    }

    public DisplayNameDialog(string confirmationMode) : this()
    {
        if (confirmationMode == "delete")
        {
            _isDeleteMode = true;
            _maxLength = 6;
            TxtTitle.Text = Loc.Get("label_delete_your_profile");
            TxtTitle.Foreground = new SolidColorBrush((Color)global::Avalonia.Application.Current!.Resources["Danger"]!);

            // Red-tinted warning
            WarningPanel.IsVisible = true;
            TxtWarningLabel.Text = Loc.Get("label_warning_2");
            TxtWarningLabel.Foreground = new SolidColorBrush((Color)global::Avalonia.Application.Current!.Resources["Danger"]!);
            TxtWarningText.Text = Loc.Get("label_this_will_permanently_delete_all_your_data_an");
            TxtWarningText.Foreground = new SolidColorBrush((Color)global::Avalonia.Application.Current!.Resources["Danger"]!);

            TxtPrompt.Text = Loc.Get("label_type_delete_to_confirm");
            BtnConfirm.Content = Loc.Get("btn_delete");
            BtnConfirm.Background = new SolidColorBrush((Color)global::Avalonia.Application.Current!.Resources["Danger"]!);

            TxtDisplayName.MaxLength = _maxLength;
            TxtDisplayName.Text = "";
            TxtCharCount.IsVisible = false;
        }
    }

    private void SetupTextChanged()
    {
        TxtDisplayName.TextChanged += (s, e) =>
        {
            if (_isDeleteMode)
            {
                BtnConfirm.IsEnabled = (TxtDisplayName.Text ?? "").Trim() == "DELETE";
            }
            else
            {
                var length = (TxtDisplayName.Text ?? "").Trim().Length;
                TxtCharCount.Text = Loc.GetF("label_char_count_of_max", length, _maxLength);
                BtnConfirm.IsEnabled = length >= 2 && length <= _maxLength;
            }
        };
    }

    private void TxtDisplayName_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && BtnConfirm.IsEnabled)
        {
            Accept();
        }
        else if (e.Key == Key.Escape)
        {
            Close(false);
        }
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void BtnConfirm_Click(object? sender, RoutedEventArgs e)
    {
        Accept();
    }

    private async void Accept()
    {
        var name = (TxtDisplayName.Text ?? "").Trim();

        if (_isDeleteMode)
        {
            if (name != "DELETE") return;
            DisplayName = name;
            Close(true);
            return;
        }

        if (name.Length < 2 || name.Length > _maxLength)
        {
            if (_dialogService != null)
            {
                await _dialogService.ShowMessageAsync(
                    Loc.Get("title_invalid_name"),
                    Loc.Get("msg_enter_name_2_20_chars"),
                    DialogSeverity.Warning);
            }
            return;
        }

        DisplayName = name;
        Close(true);
    }
}
