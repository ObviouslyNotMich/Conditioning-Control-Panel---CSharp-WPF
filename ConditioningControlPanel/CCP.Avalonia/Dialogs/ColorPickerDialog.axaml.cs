using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Cross-platform color picker dialog wrapping Avalonia's <see cref="ColorPicker"/>.
/// </summary>
public partial class ColorPickerDialog : Window
{
    public ColorPickerDialog()
    {
        InitializeComponent();
    }

    public ColorPickerDialog(Color initialColor) : this()
    {
        Picker.Color = initialColor;
    }

    private void BtnOk_Click(object? sender, RoutedEventArgs e)
    {
        Close(Picker.Color);
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
