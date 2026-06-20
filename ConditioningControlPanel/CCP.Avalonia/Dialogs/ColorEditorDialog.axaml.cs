using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Models;

using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Dialog for editing subliminal text colors.
/// </summary>
public partial class ColorEditorDialog : Window
{
    private readonly global::ConditioningControlPanel.IAppLogger _logger;
    private readonly global::ConditioningControlPanel.Core.Services.Settings.ISettingsService _settings;


    private Color _bgColor;
    private Color _textColor;
    private Color _borderColor;

    public ColorEditorDialog()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
        _settings = App.Services.GetRequiredService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>();
LoadCurrentSettings();
        UpdatePreview();
    }

    private void LoadCurrentSettings()
    {
        var settings = _settings?.Current;
        if (settings is null)
            return;

        _bgColor = ParseColor(settings.SubBackgroundColor, Color.FromRgb(0, 0, 0));
        _textColor = ParseColor(settings.SubTextColor, Color.FromRgb(255, 0, 255));
        _borderColor = ParseColor(settings.SubBorderColor, Color.FromRgb(255, 255, 255));

        ChkBgTransparent.IsChecked = settings.SubBackgroundTransparent;
        ChkTextTransparent.IsChecked = settings.SubTextTransparent;
        ChkStealsFocus.IsChecked = settings.SubliminalStealsFocus;

        UpdateColorButtons();
    }

    private void UpdateColorButtons()
    {
        BtnBgColor.Background = new SolidColorBrush(_bgColor);
        BtnTextColor.Background = new SolidColorBrush(_textColor);
        BtnBorderColor.Background = new SolidColorBrush(_borderColor);
    }

    private void UpdatePreview()
    {
        if (ChkBgTransparent.IsChecked == true)
        {
            PreviewBorder.Background = Application.Current?.Resources["DarkerBgBrush"] as IBrush
                ?? new SolidColorBrush(Color.Parse("#1A1A2E"));
        }
        else
        {
            PreviewBorder.Background = new SolidColorBrush(_bgColor);
        }

        PreviewText.Foreground = new SolidColorBrush(_textColor);

        // Approximate the legacy WPF DropShadowEffect outline with a BoxShadow.
        PreviewTextHost.BoxShadow = new BoxShadows(
            new BoxShadow
            {
                Color = _borderColor,
                Blur = 3,
                Spread = 0,
                OffsetX = 0,
                OffsetY = 0
            });
    }

    private void BtnBgColor_Click(object? sender, RoutedEventArgs e)
    {
        var color = ShowColorPicker(_bgColor);
        if (color.HasValue)
        {
            _bgColor = color.Value;
            UpdateColorButtons();
            UpdatePreview();
        }
    }

    private void BtnTextColor_Click(object? sender, RoutedEventArgs e)
    {
        var color = ShowColorPicker(_textColor);
        if (color.HasValue)
        {
            _textColor = color.Value;
            UpdateColorButtons();
            UpdatePreview();
        }
    }

    private void BtnBorderColor_Click(object? sender, RoutedEventArgs e)
    {
        var color = ShowColorPicker(_borderColor);
        if (color.HasValue)
        {
            _borderColor = color.Value;
            UpdateColorButtons();
            UpdatePreview();
        }
    }

    private void ChkBgTransparent_Changed(object? sender, RoutedEventArgs e)
    {
        UpdatePreview();
    }

    private void ChkStealsFocus_Changed(object? sender, RoutedEventArgs e)
    {
        // No live preview effect for this toggle.
    }

    private Color? ShowColorPicker(Color currentColor)
    {
        // TODO: replace the Windows Forms color dialog with a cross-platform Avalonia color picker.
        return null;
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void BtnSave_Click(object? sender, RoutedEventArgs e)
    {
        var settings = _settings?.Current;
        if (settings is null)
        {
            Close(false);
            return;
        }

        settings.SubBackgroundColor = ColorToHex(_bgColor);
        settings.SubTextColor = ColorToHex(_textColor);
        settings.SubBorderColor = ColorToHex(_borderColor);
        settings.SubBackgroundTransparent = ChkBgTransparent.IsChecked ?? false;
        settings.SubTextTransparent = ChkTextTransparent.IsChecked ?? false;
        settings.SubliminalStealsFocus = ChkStealsFocus.IsChecked ?? false;

        _logger?.Information("Subliminal settings updated");

        Close(true);
    }

    private static Color ParseColor(string? hex, Color fallback)
    {
        if (string.IsNullOrEmpty(hex))
            return fallback;
if (!hex.StartsWith("#"))
            hex = "#" + hex;

        return Color.TryParse(hex, out var color) ? color : fallback;
    }

    private static string ColorToHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
