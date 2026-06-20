using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Dialog that reports update download progress (0-100).
/// </summary>
public partial class UpdateProgressDialog : Window
{
    public UpdateProgressDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Update the progress display (0-100).
    /// </summary>
    public void SetProgress(int progress)
    {
        progress = int.Clamp(progress, 0, 100);
        TxtProgress.Text = $"{progress}%";

        if (ProgressFill.Parent is Grid grid && grid.Parent is Border border)
        {
            double maxWidth = border.Bounds.Width - 6;
            if (maxWidth > 0)
            {
                ProgressFill.Width = (maxWidth * progress) / 100.0;
            }
        }
    }
}
