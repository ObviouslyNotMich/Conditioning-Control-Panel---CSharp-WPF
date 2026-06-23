using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;

using IModService = ConditioningControlPanel.IModService;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Dialog for customizing attention target appearance.
/// </summary>
public partial class AttentionTargetEditorDialog : Window
{
    private readonly ILogger<AttentionTargetEditorDialog> _logger;
    private readonly global::ConditioningControlPanel.Core.Services.Settings.ISettingsService? _settings;


    private string _color1 = GetThemeHex("DarkPinkColor", "#FF1493");
    private string _color2 = GetThemeHex("PinkColor", "#FF69B4");
    private string _textColor = GetThemeHex("DarkPinkColor", "#FF1493");
    private string _borderColor = GetThemeHex("DarkPinkColor", "#FF1493");
    private bool _showBorder;
    private bool _floatingText = true;
    private string _font = "Segoe UI";
    private readonly IModService _mods;

    private static string GetThemeHex(string key, string fallback)
    {
        if (Application.Current?.TryFindResource(key, out var res) == true && res is Color c)
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        return fallback;
    }

    public AttentionTargetEditorDialog()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<AttentionTargetEditorDialog>>();
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





    private async void BtnColor1_Click(object? sender, RoutedEventArgs e)
    {
        var color = await PickColorAsync(_color1);
        if (!string.IsNullOrEmpty(color))
        {
            _color1 = color;
            UpdateColorButtons();
            UpdatePreview();
        }
    }

    private async void BtnColor2_Click(object? sender, RoutedEventArgs e)
    {
        var color = await PickColorAsync(_color2);
        if (!string.IsNullOrEmpty(color))
        {
            _color2 = color;
            UpdateColorButtons();
            UpdatePreview();
        }
    }

    private async void BtnTextColor_Click(object? sender, RoutedEventArgs e)
    {
        var color = await PickColorAsync(_textColor);
        if (!string.IsNullOrEmpty(color))
        {
            _textColor = color;
            UpdateColorButtons();
            UpdatePreview();
        }
    }

    private async void BtnBorderColor_Click(object? sender, RoutedEventArgs e)
    {
        var color = await PickColorAsync(_borderColor);
        if (!string.IsNullOrEmpty(color))
        {
            _borderColor = color;
            UpdateColorButtons();
            UpdatePreview();
        }
    }

    private async Task<string?> PickColorAsync(string currentColor)
    {
        var dialog = new ColorPickerDialog(ParseColor(currentColor));
        var result = await dialog.ShowDialog<Color?>(this);
        return result.HasValue ? $"#{result.Value.R:X2}{result.Value.G:X2}{result.Value.B:X2}" : null;
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

        var pool = settings.AttentionPool;
        string text = pool.FirstOrDefault(kvp => kvp.Value).Key ?? "GOOD GIRL";
        ShowFloatingPreview(text);
        _logger?.LogInformation("Test attention target requested for {Text}", text);
    }

    private void ShowFloatingPreview(string text)
    {
        var screen = this.Screens?.Primary;
        if (screen == null) return;

        var color1 = ParseColor(_color1);
        var color2 = ParseColor(_color2);
        var textColor = ParseColor(_textColor);
        var borderColor = ParseColor(_borderColor);

        const double width = 420;
        const double height = 140;
        var scaledW = width * screen.Scaling;
        var scaledH = height * screen.Scaling;
        var x = screen.Bounds.X + (screen.Bounds.Width - scaledW) / 2;
        var y = screen.Bounds.Y + (screen.Bounds.Height - scaledH) / 2;

        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = 36,
            FontWeight = FontWeight.Bold,
            FontFamily = string.IsNullOrWhiteSpace(_font) ? FontFamily.Default : new FontFamily(_font),
            Foreground = new SolidColorBrush(textColor),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
        };

        var border = new Border
        {
            Background = _floatingText ? null : new LinearGradientBrush
            {
                GradientStops = new GradientStops { new GradientStop(color1, 0), new GradientStop(color2, 1) }
            },
            CornerRadius = _floatingText ? new CornerRadius(0) : new CornerRadius(20),
            BorderBrush = (_showBorder && !_floatingText) ? new SolidColorBrush(borderColor) : null,
            BorderThickness = (_showBorder && !_floatingText) ? new Thickness(3) : new Thickness(0),
            Padding = _floatingText ? new Thickness(0) : new Thickness(20, 10, 20, 10),
            Child = textBlock
        };

        var preview = new Window
        {
            WindowDecorations = WindowDecorations.None,
            Background = Brushes.Transparent,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            ShowInTaskbar = false,
            ShowActivated = false,
            Focusable = false,
            IsHitTestVisible = false,
            CanResize = false,
            Topmost = true,
            Width = width,
            Height = height,
            Position = new PixelPoint((int)x, (int)y),
            Content = new Panel { Children = { border } }
        };

        preview.Opened += (_, _) =>
        {
            if (preview.TryGetPlatformHandle() is { } handle && OperatingSystem.IsWindows())
            {
                try
                {
                    const int gwlExStyle = -20;
                    const uint wsExToolWindow = 0x00000080;
                    const uint wsExNoActivate = 0x08000000;
                    const uint wsExTransparent = 0x00000020;
                    var ex = GetWindowLong(handle.Handle, gwlExStyle);
                    ex |= wsExToolWindow | wsExNoActivate | wsExTransparent;
                    SetWindowLong(handle.Handle, gwlExStyle, ex);
                }
                catch { }
            }
        };

        preview.Show();

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await System.Threading.Tasks.Task.Delay(3000);
            await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => preview.Close());
        });
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowLong(System.IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(System.IntPtr hWnd, int nIndex, uint dwNewLong);

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
