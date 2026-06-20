using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Models;
using MsBox.Avalonia;

namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Simple dialog for editing a session's top-level metadata.
/// Phases and detailed settings are out of scope for this editor.
/// </summary>
public partial class SessionEditDialog : Window
{
    private readonly bool _isNew;

    /// <summary>
    /// The session being edited. On save, the values are copied back into this instance.
    /// </summary>
    public Session EditedSession { get; }

    /// <summary>
    /// Parameterless constructor required by the Avalonia XAML runtime loader.
    /// Use <see cref="SessionEditDialog(Session, bool)"/> in application code.
    /// </summary>
    public SessionEditDialog()
    {
        InitializeComponent();
        EditedSession = new Session();
        _isNew = true;
    }

    public SessionEditDialog(Session session, bool isNew)
    {
        InitializeComponent();
        EditedSession = session;
        _isNew = isNew;

        TxtTitle.Text = isNew ? "New Session" : "Edit Session";
        Title = isNew ? "New Session" : "Edit Session";

        CmbDifficulty.ItemsSource = Enum.GetValues(typeof(SessionDifficulty)).Cast<SessionDifficulty>().ToList();

        TxtName.Text = session.Name;
        TxtIcon.Text = session.Icon;
        NumDuration.Value = session.DurationMinutes;
        CmbDifficulty.SelectedItem = session.Difficulty;
        NumBonusXP.Value = session.BonusXP;
        ChkAvailable.IsChecked = session.IsAvailable;
        TxtDescription.Text = session.Description;
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async void BtnSave_Click(object? sender, RoutedEventArgs e)
    {
        var name = TxtName.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            await ShowErrorAsync("Name is required.");
            return;
        }

        if (!int.TryParse(NumDuration.Text, out var duration) || duration <= 0)
        {
            await ShowErrorAsync("Duration must be a positive integer.");
            return;
        }

        if (!int.TryParse(NumBonusXP.Text, out var bonusXp) || bonusXp < 0)
        {
            await ShowErrorAsync("Bonus XP must be a non-negative integer.");
            return;
        }

        if (CmbDifficulty.SelectedItem is not SessionDifficulty difficulty)
        {
            await ShowErrorAsync("Please select a difficulty.");
            return;
        }

        EditedSession.Name = name;
        EditedSession.Icon = TxtIcon.Text ?? "\U0001f3af";
        EditedSession.DurationMinutes = duration;
        EditedSession.Difficulty = difficulty;
        EditedSession.BonusXP = bonusXp;
        EditedSession.IsAvailable = ChkAvailable.IsChecked == true;
        EditedSession.Description = TxtDescription.Text ?? "";

        if (_isNew)
        {
            EditedSession.Source = SessionSource.Custom;
        }

        Close(true);
    }

    private async Task ShowErrorAsync(string message)
    {
        TxtError.Text = message;
        TxtError.IsVisible = true;

        var box = MessageBoxManager.GetMessageBoxStandard("Validation Error", message, MsBox.Avalonia.Enums.ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Warning);
        if (Owner is Window ownerWindow)
        {
            await box.ShowWindowDialogAsync(ownerWindow);
        }
        else
        {
            await box.ShowAsync();
        }
    }
}
