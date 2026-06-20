using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Features;

/// <summary>
/// Generic modeless popup window that hosts a feature control.
/// Borderless, pink-themed titlebar, drag-to-move, Escape-to-close, centered on owner.
/// </summary>
public partial class FeaturePopupWindow : Window
{
    public FeaturePopupWindow()
    {
        InitializeComponent();
    }

    public FeaturePopupWindow(Control content, string title, IImage? icon = null, string? glyph = null)
        : this()
    {

        TxtTitle.Text = title;
        Title = title; // also set Window.Title for accessibility

        if (icon is not null)
        {
            ImgIcon.Source = icon;
            ImgIcon.IsVisible = true;
            TxtGlyph.IsVisible = false;
        }
        else if (!string.IsNullOrEmpty(glyph))
        {
            TxtGlyph.Text = glyph;
            TxtGlyph.IsVisible = true;
            ImgIcon.IsVisible = false;
        }
        else
        {
            ImgIcon.IsVisible = false;
            TxtGlyph.IsVisible = false;
        }

        ContentHost.Content = content;

        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // TODO: reintroduce the panic-key capture check once the Avalonia main window
            // exposes an equivalent of MainWindow.IsCapturingPanicKey. Without that check,
            // pressing Escape will close this popup even while a panic-key picker is waiting
            // for a key, so capture should be restored when the port reaches that feature.
            Close();
            e.Handled = true;
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
