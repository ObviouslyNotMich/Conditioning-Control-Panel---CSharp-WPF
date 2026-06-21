using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Dialog for choosing an offline username.
/// </summary>
public partial class OfflineUsernameDialog : Window
{
    public string Username { get; private set; } = "";

    private readonly IDialogService? _dialogService;

    public OfflineUsernameDialog()
    {
        InitializeComponent();

        _dialogService = global::ConditioningControlPanel.Avalonia.App.Services?.GetService<IDialogService>();

        TxtCharCount.Text = Loc.GetF("label_char_count_of_max", 0, 30);

        Loaded += (_, _) =>
        {
            TxtUsername.Focus();
        };
    }

    private void TxtUsername_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var length = TxtUsername.Text?.Trim().Length ?? 0;
        TxtCharCount.Text = Loc.GetF("label_char_count_of_max", length, 30);
        BtnConfirm.IsEnabled = length >= 2;
    }

    private void TxtUsername_KeyDown(object? sender, KeyEventArgs e)
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
        var name = TxtUsername.Text?.Trim() ?? "";
        if (name.Length < 2)
        {
            if (_dialogService != null)
            {
                await _dialogService.ShowMessageAsync(
                    Loc.Get("title_invalid_name"),
                    Loc.Get("msg_enter_name_min_2_chars"),
                    DialogSeverity.Warning);
            }
            return;
        }

        Username = name;
        Close(true);
    }
}
