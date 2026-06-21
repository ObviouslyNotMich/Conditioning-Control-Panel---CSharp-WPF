using Avalonia.Controls;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

/// <summary>
/// Avalonia port of the Deeper enhancement editor.
/// </summary>
public partial class DeeperEditorWindow : Window
{
    private readonly Enhancement _enhancement;
    private readonly string? _filePath;

    /// <summary>
    /// Exposes the on-disk path of the currently loaded project (null for a new/unsaved one).
    /// </summary>
    public string? LoadedFilePath => _filePath;

    public DeeperEditorWindow()
    {
        InitializeComponent();
        _enhancement = new Enhancement();
    }

    public DeeperEditorWindow(Enhancement enhancement, string? filePath) : this()
    {
        _enhancement = enhancement ?? new Enhancement();
        _filePath = filePath;
    }
}
