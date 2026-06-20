using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;

namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Dangerous-feature warning dialog.
/// </summary>
public partial class WarningDialog : Window
{
    public bool Confirmed { get; private set; }

    public WarningDialog()
    {
        InitializeComponent();
    }

    public WarningDialog(string title, string message, string confirmText = "I understand the risks")
    {
        InitializeComponent();

        TxtTitle.Text = title;
        TxtMessage.Text = message;
        TxtConfirmLabel.Text = confirmText;
    }

    private void ChkConfirm_Changed(object? sender, RoutedEventArgs e)
    {
        BtnConfirm.IsEnabled = ChkConfirm.IsChecked == true;
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close(false);
    }

    private void BtnConfirm_Click(object? sender, RoutedEventArgs e)
    {
        if (ChkConfirm.IsChecked == true)
        {
            Confirmed = true;
            Close(true);
        }
    }

    /// <summary>
    /// Shows a double warning dialog for dangerous features.
    /// </summary>
    public static async Task<bool> ShowDoubleWarning(Window owner, string feature, string consequences)
    {
        var title = Loc.GetF("warning_enable_feature_title", feature);
        var message = Loc.GetF("warning_enable_feature_body", feature, consequences);

        var dialog = new WarningDialog(title, message, Loc.GetF("warning_enable_feature_confirm", feature))
        {
            Topmost = true
        };

        var result = await dialog.ShowDialog<bool?>(owner);
        return result == true && dialog.Confirmed;
    }
}
