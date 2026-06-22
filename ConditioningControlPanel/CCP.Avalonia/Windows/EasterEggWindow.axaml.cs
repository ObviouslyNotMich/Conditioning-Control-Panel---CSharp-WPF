using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
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
        ApplyThemeShadow();
    }

    public EasterEggWindow(int readerCount) : this()
    {
        if (readerCount > 0)
        {
            TxtReaderCount.Text = $"This rant has been read {readerCount} times";
            TxtReaderCount.IsVisible = true;
        }
    }

    private void ApplyThemeShadow()
    {
        if (RootBorder == null) return;
        var accent = (Application.Current?.TryFindResource("PinkColor", out var res) == true && res is Color c)
            ? c
            : Color.Parse("#FF69B4");
        RootBorder.BoxShadow = new BoxShadows(new BoxShadow
        {
            OffsetX = 0, OffsetY = 0, Blur = 30, Spread = 0,
            Color = Color.FromArgb(0x80, accent.R, accent.G, accent.B)
        });
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
