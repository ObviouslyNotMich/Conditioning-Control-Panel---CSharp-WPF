using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Models;

using IModService = ConditioningControlPanel.IModService;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Dialog for customizing attention target appearance.
/// </summary>
public partial class AttentionTargetEditorDialog : Window
{
    private readonly global::ConditioningControlPanel.IAppLogger _logger;
    private readonly global::ConditioningControlPanel.Core.Services.Settings.ISettingsService? _settings;


    private string _color1 = "#FF1493";
    private string _color2 = "#FF69B4";
    private string _textColor = "#FF1493";
    private string _borderColor = "#FF1493";
    private bool _showBorder;
    private bool _floatingText = true;
    private string _font = "Segoe UI";
    private readonly IModService _mods;

    public AttentionTargetEditorDialog()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
        _settings = App.Services.GetRequiredService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>();
_mods = App.Services.GetRequiredService<IModService>();

        var settings = _settings?.Current;
        if (settings is not null)
        {
            _color1 = settings.AttentionColor1;
            _color2 = settings.AttentionColor2;
            _textColor = settings.AttentionTextColor;
            _borderColor = settings.AttentionBorderColor;
            _showBorder = settings.AttentionShowBorder;
            _floatingText = settings.AttentionFloatingText;
            _font = settings.AttentionFont;
        }

        UpdateColorButtons();
        ChkFloatingText.IsChecked = _floatingText;
        ChkShowBorder.IsChecked = _showBorder;
        UpdateRowVisibility();
        SelectFontInCombo(_font);
        UpdatePreview();
    }

    private void SelectFontInCombo(string fontName)
    {
        foreach (var item in CmbFont.Items)
        {
            if (item is ComboBoxItem cbi && cbi.Tag?.ToString() == fontName)
            {
                CmbFont.SelectedItem = cbi;
                return;
            }
        }
        CmbFont.SelectedIndex = 0;
    }

    private void UpdateColorButtons()
    {
        try
        {
            BtnColor1.Background = new SolidColorBrush(ParseColor(_color1));
            TxtColor1.Text = _color1;

            BtnColor2.Background = new SolidColorBrush(ParseColor(_color2));
            TxtColor2.Text = _color2;

            BtnTextColor.Background = new SolidColorBrush(ParseColor(_textColor));
            TxtTextColor.Text = _textColor;

            BtnBorderColor.Background = new SolidColorBrush(ParseColor(_borderColor));
            TxtBorderColor.Text = _borderColor;
        }
        catch
        {
            // Ignore invalid color strings during initialization.
        }
    }

    private void UpdateRowVisibility()
    {
        BorderToggleRow.IsVisible = !_floatingText;
        BorderColorRow.IsVisible = _showBorder && !_floatingText;
    }

    private void UpdatePreview()
    {
        try
        {
            var color1 = ParseColor(_color1);
            var color2 = ParseColor(_color2);
            var textColor = ParseColor(_textColor);
            var borderColor = ParseColor(_borderColor);

            if (_floatingText)
            {
                PreviewBorder.Background = Brushes.Transparent;
                PreviewBorder.BorderBrush = Brushes.Transparent;
                PreviewBorder.BorderThickness = new Thickness(0);
            }
            else
            {
                PreviewBorder.Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = new GradientStops
                    {
                        new GradientStop(color1, 0),
                        new GradientStop(color2, 1)
                    }
                };

                if (_showBorder)
                {
                    PreviewBorder.BorderBrush = new SolidColorBrush(borderColor);
                    PreviewBorder.BorderThickness = new Thickness(3);
                }
                else
                {
                    PreviewBorder.BorderBrush = Brushes.Transparent;
                    PreviewBorder.BorderThickness = new Thickness(0);
                }
            }

            PreviewText.Foreground = new SolidColorBrush(textColor);
            PreviewText.FontFamily = new FontFamily(_font);

            var shadowBase = _floatingText ? textColor : color1;
            var shadowColor = Color.FromRgb(
                (byte)(shadowBase.R * 0.4),
                (byte)(shadowBase.G * 0.4),
                (byte)(shadowBase.B * 0.4));

            PreviewTextHost.BoxShadow = new BoxShadows(
                new BoxShadow
                {
                    Color = shadowColor,
                    Blur = 3,
                    OffsetX = 2,
                    OffsetY = 2
                });
        }
        catch
        {
            // Ignore preview update failures from bad color data.
        }
    }

    private string? PickColor(string currentColor)
    {
        // TODO: replace the Windows Forms color dialog with a cross-platform Avalonia color picker.
        return null;
    }

    private void BtnColor1_Click(object? sender, RoutedEventArgs e)
    {
        var color = PickColor(_color1);
        if (!string.IsNullOrEmpty(color))
        {
            _color1 = color;
            UpdateColorButtons();
            UpdatePreview();
        }
    }

    private void BtnColor2_Click(object? sender, RoutedEventArgs e)
    {
        var color = PickColor(_color2);
        if (!string.IsNullOrEmpty(color))
        {
            _color2 = color;
            UpdateColorButtons();
            UpdatePreview();
        }
    }

    private void BtnTextColor_Click(object? sender, RoutedEventArgs e)
    {
        var color = PickColor(_textColor);
        if (!string.IsNullOrEmpty(color))
        {
            _textColor = color;
            UpdateColorButtons();
            UpdatePreview();
        }
    }

    private void BtnBorderColor_Click(object? sender, RoutedEventArgs e)
    {
        var color = PickColor(_borderColor);
        if (!string.IsNullOrEmpty(color))
        {
            _borderColor = color;
            UpdateColorButtons();
            UpdatePreview();
        }
    }

    private void ChkFloatingText_Changed(object? sender, RoutedEventArgs e)
    {
        _floatingText = ChkFloatingText.IsChecked == true;
        UpdateRowVisibility();
        UpdatePreview();
    }

    private void ChkShowBorder_Changed(object? sender, RoutedEventArgs e)
    {
        _showBorder = ChkShowBorder.IsChecked == true;
        UpdateRowVisibility();
        UpdatePreview();
    }

    private void CmbFont_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CmbFont.SelectedItem is ComboBoxItem item && item.Tag is string font)
        {
            _font = font;
            UpdatePreview();
        }
    }

    #region Presets

    private void PresetPurple_Click(object? sender, RoutedEventArgs e)
    {
        _color1 = _mods.MakeModAware("#9B59B6");
        _color2 = "#8E44AD";
        _textColor = "#FFFFFF";
        _showBorder = false;
        _floatingText = false;
        _font = "Segoe UI";
        ApplyPreset();
    }

    private void PresetPink_Click(object? sender, RoutedEventArgs e)
    {
        _color1 = "#FF64C8";
        _color2 = "#FF3296";
        _textColor = "#FFFFFF";
        _showBorder = true;
        _floatingText = false;
        _borderColor = "#FFFFFF";
        _font = "Comic Sans MS";
        ApplyPreset();
    }

    private void PresetGreen_Click(object? sender, RoutedEventArgs e)
    {
        _color1 = "#2ECC71";
        _color2 = "#27AE60";
        _textColor = "#FFFFFF";
        _showBorder = false;
        _floatingText = false;
        _font = "Impact";
        ApplyPreset();
    }

    private void PresetBlue_Click(object? sender, RoutedEventArgs e)
    {
        _color1 = "#3498DB";
        _color2 = "#2980B9";
        _textColor = "#FFFFFF";
        _showBorder = false;
        _floatingText = false;
        _font = "Arial Black";
        ApplyPreset();
    }

    private void ApplyPreset()
    {
        ChkFloatingText.IsChecked = _floatingText;
        ChkShowBorder.IsChecked = _showBorder;
        UpdateRowVisibility();
        SelectFontInCombo(_font);
        UpdateColorButtons();
        UpdatePreview();
    }

    #endregion

    private void BtnTest_Click(object? sender, RoutedEventArgs e)
    {
        var settings = _settings?.Current;
        if (settings is null)
            return;

        var oldC1 = settings.AttentionColor1;
        var oldC2 = settings.AttentionColor2;
        var oldText = settings.AttentionTextColor;
        var oldBorder = settings.AttentionBorderColor;
        var oldShowBorder = settings.AttentionShowBorder;
        var oldFloating = settings.AttentionFloatingText;
        var oldFont = settings.AttentionFont;

        try
        {
            settings.AttentionColor1 = _color1;
            settings.AttentionColor2 = _color2;
            settings.AttentionTextColor = _textColor;
            settings.AttentionBorderColor = _borderColor;
            settings.AttentionShowBorder = _showBorder;
            settings.AttentionFloatingText = _floatingText;
            settings.AttentionFont = _font;

            var pool = settings.AttentionPool;
            string text = pool.FirstOrDefault(kvp => kvp.Value).Key ?? "GOOD GIRL";

            // TODO: spawn a cross-platform floating attention target preview.
            // The legacy implementation used System.Windows.Forms.Screen and Services.FloatingText.
            _logger?.Information("Test attention target requested for {Text}", text);
        }
        finally
        {
            settings.AttentionColor1 = oldC1;
            settings.AttentionColor2 = oldC2;
            settings.AttentionTextColor = oldText;
            settings.AttentionBorderColor = oldBorder;
            settings.AttentionShowBorder = oldShowBorder;
            settings.AttentionFloatingText = oldFloating;
            settings.AttentionFont = oldFont;
        }
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void BtnSave_Click(object? sender, RoutedEventArgs e)
    {
        var settings =
_settings?.Current;
        if (settings is null)
        {
            Close(false);
            return;
        }

        settings.AttentionColor1 = _color1;
        settings.AttentionColor2 = _color2;
        settings.AttentionTextColor = _textColor;
        settings.AttentionBorderColor = _borderColor;
        settings.AttentionShowBorder = _showBorder;
        settings.AttentionFloatingText = _floatingText;
        settings.AttentionFont = _font;

        Close(true);
    }

    private static Color ParseColor(string hex)
    {
        if (string.IsNullOrEmpty(hex))
            return Colors.Transparent;

        if (!hex.StartsWith("#"))
            hex = "#" + hex;

        return Color.TryParse(hex, out var color) ? color : Colors.Transparent;
    }
}
