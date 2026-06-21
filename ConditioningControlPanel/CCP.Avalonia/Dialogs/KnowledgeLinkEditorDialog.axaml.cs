using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.DependencyInjection;

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

    private readonly IDialogService? _dialogService;

    public KnowledgeLinkEditorDialog()
    {
        InitializeComponent();
        _dialogService = global::ConditioningControlPanel.Avalonia.App.Services?.GetService<IDialogService>();
        TxtUrl.Focus();
    }

    private async void BtnAdd_Click(object? sender, RoutedEventArgs e)
    {
        var url = TxtUrl.Text?.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            if (_dialogService != null)
            {
                await _dialogService.ShowMessageAsync(
                    Loc.Get("title_validation_error"),
                    Loc.Get("msg_enter_url"),
                    DialogSeverity.Warning);
            }
            TxtUrl.Focus();
            return;
        }

        var title = TxtTitle.Text?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            if (_dialogService != null)
            {
                await _dialogService.ShowMessageAsync(
                    Loc.Get("title_validation_error"),
                    Loc.Get("msg_enter_title"),
                    DialogSeverity.Warning);
            }
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
