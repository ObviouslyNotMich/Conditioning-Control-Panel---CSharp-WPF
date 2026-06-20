using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Simple single-line input dialog.
/// </summary>
public partial class InputDialog : Window
{
    public string ResultText { get; private set; } = "";

    public InputDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            TxtInput.Focus();
            TxtInput.SelectAll();
        };
    }

    public InputDialog(string title, string prompt, string defaultValue = "")
        : this()
    {
        TxtTitle.Text = title;
        TxtPrompt.Text = prompt;
        TxtInput.Text = defaultValue;
    }

    private void TxtInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
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

    private void BtnOK_Click(object? sender, RoutedEventArgs e)
    {
        Accept();
    }

    private void Accept()
    {
        ResultText = TxtInput.Text ?? "";
        Close(true);
    }
}
