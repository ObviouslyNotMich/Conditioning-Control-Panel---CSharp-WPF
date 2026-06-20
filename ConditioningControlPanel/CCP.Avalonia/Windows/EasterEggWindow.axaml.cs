using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the hidden credits / rant window.
/// </summary>
public partial class EasterEggWindow : Window
{
    public EasterEggWindow()
    {
        InitializeComponent();
    }

    public EasterEggWindow(int readerCount) : this()
    {
        if (readerCount > 0)
        {
            TxtReaderCount.Text = $"This rant has been read {readerCount} times";
            TxtReaderCount.IsVisible = true;
        }
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
