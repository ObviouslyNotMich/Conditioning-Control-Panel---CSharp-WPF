using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Models;

namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Avalonia port of the knowledge-base link editor dialog.
/// </summary>
public partial class KnowledgeLinkEditorDialog : Window
{
    /// <summary>
    /// The created link, or null if cancelled.
    /// </summary>
    public KnowledgeBaseLink? Result { get; private set; }

    public KnowledgeLinkEditorDialog()
    {
        InitializeComponent();
        TxtUrl.Focus();
    }

    private void BtnAdd_Click(object? sender, RoutedEventArgs e)
    {
        var url = TxtUrl.Text?.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBoxStub.Show(
                Loc.Get("msg_enter_url"),
                "Validation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            TxtUrl.Focus();
            return;
        }

        var title = TxtTitle.Text?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            MessageBoxStub.Show(
                Loc.Get("msg_enter_title"),
                "Validation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            TxtTitle.Focus();
            return;
        }

        Result = new KnowledgeBaseLink
        {
            Url = url,
            Title = title,
            Description = TxtDescription.Text?.Trim() ?? string.Empty
        };

        Close(true);
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close(false);
    }
}
