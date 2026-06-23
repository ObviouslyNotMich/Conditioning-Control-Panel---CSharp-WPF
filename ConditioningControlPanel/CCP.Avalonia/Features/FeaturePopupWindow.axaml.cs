using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Dialogs;

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
            // Don't steal Escape while a panic-key picker is open.
            if (IsCapturingPanicKey())
                return;

            Close();
            e.Handled = true;
        }
    }

    private static bool IsCapturingPanicKey()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return false;

        return desktop.Windows.OfType<ChatShortcutCaptureDialog>().Any(w => w.IsVisible);
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
