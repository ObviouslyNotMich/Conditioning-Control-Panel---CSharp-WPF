using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Core.Localization;

using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Dialog for editing lock card colors.
/// </summary>
public partial class LockCardColorDialog : Window
{
    private readonly ILogger<LockCardColorDialog> _logger;
    private readonly global::ConditioningControlPanel.Core.Services.Settings.ISettingsService _settings;


    private Color _bgColor;
    private Color _textColor;
    private Color _inputBgColor;
    private Color _inputTextColor;
    private Color _accentColor;

    public LockCardColorDialog()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<LockCardColorDialog>>();
        _settings = App.Services.GetRequiredService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>();
LoadCurrentSettings();
        UpdatePreview();
    }

    private void LoadCurrentSettings()
    {
        var settings = _settings?.Current;
        if (settings is null)
            return;

        _bgColor = ParseColor(settings.LockCardBackgroundColor, Color.FromRgb(26, 26, 46));
        _textColor = ParseColor(settings.LockCardTextColor, ParseAccentOrFallback());
        _inputBgColor = ParseColor(settings.LockCardInputBackgroundColor, Color.FromRgb(37, 37, 66));
        _inputTextColor = ParseColor(settings.LockCardInputTextColor, Colors.White);
        _accentColor = ParseColor(settings.LockCardAccentColor, ParseAccentOrFallback());

        UpdateColorButtons();
    }

    private static Color ParseAccentOrFallback()
    {
        if (Application.Current?.TryFindResource("PinkColor", out var res) == true && res is Color color)
            return color;
        return Color.Parse("#FF69B4");
    }

    private void UpdateColorButtons()
    {
        BtnBgColor.Background = new SolidColorBrush(_bgColor);
        BtnTextColor.Background = new SolidColorBrush(_textColor);
        BtnInputBgColor.Background = new SolidColorBrush(_inputBgColor);
        BtnInputTextColor.Background = new SolidColorBrush(_inputTextColor);
        BtnAccentColor.Background = new SolidColorBrush(_accentColor);
    }

    private void UpdatePreview()
    {
        PreviewBorder.Background = new SolidColorBrush(_bgColor);
        PreviewPhrase.Foreground = new SolidColorBrush(_textColor);

        PreviewInputBorder.Background = new SolidColorBrush(_inputBgColor);
        PreviewInputBorder.BorderBrush = new SolidColorBrush(_accentColor);
        PreviewInputText.Foreground = new SolidColorBrush(_inputTextColor);

        PreviewProgress.Foreground = new SolidColorBrush(_accentColor);
        PreviewProgressBar.Background = new SolidColorBrush(_accentColor);
    }

    private async void BtnBgColor_Click(object? sender, RoutedEventArgs e)
    {
        var color = await ShowColorPickerAsync(_bgColor);
        if (color.HasValue) { _bgColor = color.Value; UpdateColorButtons(); UpdatePreview(); }
    }
    private async void BtnTextColor_Click(object? sender, RoutedEventArgs e)
    {
        var color = await ShowColorPickerAsync(_textColor);
        if (color.HasValue) { _textColor = color.Value; UpdateColorButtons(); UpdatePreview(); }
    }
    private async void BtnInputBgColor_Click(object? sender, RoutedEventArgs e)
    {
        var color = await ShowColorPickerAsync(_inputBgColor);
        if (color.HasValue) { _inputBgColor = color.Value; UpdateColorButtons(); UpdatePreview(); }
    }
    private async void BtnInputTextColor_Click(object? sender, RoutedEventArgs e)
    {
        var color = await ShowColorPickerAsync(_inputTextColor);
        if (color.HasValue) { _inputTextColor = color.Value; UpdateColorButtons(); UpdatePreview(); }
    }
    private async void BtnAccentColor_Click(object? sender, RoutedEventArgs e)
    {
        var color = await ShowColorPickerAsync(_accentColor);
        if (color.HasValue) { _accentColor = color.Value; UpdateColorButtons(); UpdatePreview(); }
    }

    private async Task<Color?> ShowColorPickerAsync(Color currentColor)
    {
        var dialog = new ColorPickerDialog(currentColor);
        return await dialog.ShowDialog<Color?>(this);
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

        settings.LockCardBackgroundColor = ColorToHex(_bgColor);
        settings.LockCardTextColor = ColorToHex(_textColor);
        settings.LockCardInputBackgroundColor = ColorToHex(_inputBgColor);
        settings.LockCardInputTextColor = ColorToHex(_inputTextColor);
        settings.LockCardAccentColor = ColorToHex(_accentColor);

        _logger?.LogInformation("Lock card colors updated");

        Close(true);
    }

    private static Color ParseColor(string? hex, Color fallback)
    {
        try
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return Color.TryParse(hex, out var color) ? color : fallback;
        }
        catch
        {
            return fallback;
}
    }

    private static string ColorToHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
