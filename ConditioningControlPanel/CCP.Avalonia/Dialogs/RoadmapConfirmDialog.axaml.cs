using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Themed dialog for confirming photo submission.
/// </summary>
public partial class RoadmapConfirmDialog : Window
{
    public bool Confirmed { get; private set; }

    public RoadmapConfirmDialog()
    {
        InitializeComponent();
    }

    public RoadmapConfirmDialog(string stepTitle, string photoRequirement) : this()
    {
        TxtStepTitle.Text = $"\"{stepTitle}\"";
        TxtRequirement.Text = photoRequirement;
    }

    private void BtnNo_Click(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close(false);
    }

    private void BtnYes_Click(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close(true);
    }
}
