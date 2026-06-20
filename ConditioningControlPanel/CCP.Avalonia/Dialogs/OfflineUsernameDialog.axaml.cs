using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;

namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Dialog for choosing an offline username.
/// </summary>
public partial class OfflineUsernameDialog : Window
{
    public string Username { get; private set; } = "";

    public OfflineUsernameDialog()
    {
        InitializeComponent();

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

    private void Accept()
    {
        var name = TxtUsername.Text?.Trim() ?? "";
        if (name.Length < 2)
        {
            MessageBoxStub.Show(
                Loc.Get("msg_enter_name_min_2_chars"),
                Loc.Get("title_invalid_name"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Username = name;
        Close(true);
    }
}
