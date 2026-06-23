using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Core.Localization;

using IModService = ConditioningControlPanel.IModService;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Avalonia port of the Lovense / Buttplug.io setup guide window.
/// </summary>
public partial class HapticsSetupWindow : Window
{
    private readonly ILogger<HapticsSetupWindow> _logger;


    private enum Provider { None, Lovense, Buttplug }
    private Provider _selectedProvider = Provider.None;
    private int _currentSlide = 1;
    private const int TotalSlides = 3;

    public HapticsSetupWindow()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<ILogger<HapticsSetupWindow>>();
_mods = App.Services.GetRequiredService<IModService>();
        LoadGuideImages();
    }

    private readonly IModService _mods;

    private void LoadGuideImages()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            LoadImage(ImgLovenseStep1, Path.Combine(baseDir, "Resources", "haptics_guide", "lovense_step1.png"));
            LoadImage(ImgLovenseStep2, Path.Combine(baseDir, "Resources", "haptics_guide", "lovense_step2.png"));
            LoadImage(ImgButtplugStep1, Path.Combine(baseDir, "Resources", "haptics_guide", "buttplug_step1.png"));
            LoadImage(ImgButtplugStep2, Path.Combine(baseDir, "Resources", "haptics_guide", "buttplug_step2.png"));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "HapticsSetupWindow: failed to load guide images");
        }
    }

    private static void LoadImage(Image image, string path)
    {
        if (File.Exists(path))
        {
            try
            {
                image.Source = new Bitmap(path);
            }
            catch { /* ignore invalid image */ }
        }
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnSelectLovense_Click(object? sender, RoutedEventArgs e)
    {
        _selectedProvider = Provider.Lovense;
        _currentSlide = 1;
        ShowTutorial();
    }

    private void BtnSelectButtplug_Click(object? sender, RoutedEventArgs e)
    {
        _selectedProvider = Provider.Buttplug;
        _currentSlide = 1;
        ShowTutorial();
    }

    private void ShowTutorial()
    {
        ProviderSelectionGrid.IsVisible = false;

        if (_selectedProvider == Provider.Lovense)
        {
            TxtTitle.Text = Loc.Get("label_lovense_setup_guide");
            var accent = ParseColor(_mods.GetAccentColorHex(), Color.FromRgb(255, 105, 180));
            TxtTitle.Foreground = new SolidColorBrush(accent);
            LovenseSlides.IsVisible = true;
            ButtplugSlides.IsVisible = false;

            SetIndicatorColor(new SolidColorBrush(accent));
            BtnNext.Background = new SolidColorBrush(accent);
        }
        else
        {
            TxtTitle.Text = Loc.Get("label_buttplug_io_setup_guide");
            var secondary = ParseColor(_mods.GetSecondaryColorHex(), Color.FromRgb(155, 89, 182));
            TxtTitle.Foreground = new SolidColorBrush(secondary);
            LovenseSlides.IsVisible = false;
            ButtplugSlides.IsVisible = true;

            SetIndicatorColor(new SolidColorBrush(secondary));
            BtnNext.Background = new SolidColorBrush(secondary);
        }

        ShowNavigationControls();
        UpdateSlideVisibility();
    }

    private void SetIndicatorColor(IBrush activeColor)
    {
        Dot1.Tag = activeColor;
    }

    private void ShowNavigationControls()
    {
        BtnPrevious.IsVisible = true;
        BtnNext.IsVisible = true;
        SlideIndicators.IsVisible = true;
    }

    private void BtnPrevious_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentSlide == 1)
        {
            _selectedProvider = Provider.None;
            ProviderSelectionGrid.IsVisible = true;
            LovenseSlides.IsVisible = false;
            ButtplugSlides.IsVisible = false;
            BtnPrevious.IsVisible = false;
            BtnNext.IsVisible = false;
            BtnDone.IsVisible = false;
            SlideIndicators.IsVisible = false;
            TxtTitle.Text = Loc.Get("dialog_haptics_setup_guide");
            var accent = ParseColor(_mods.GetAccentColorHex(), Color.FromRgb(255, 105, 180));
            TxtTitle.Foreground = new SolidColorBrush(accent);
        }
        else
        {
            _currentSlide--;
            UpdateSlideVisibility();
        }
    }

    private void BtnNext_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentSlide < TotalSlides)
        {
            _currentSlide++;
            UpdateSlideVisibility();
        }
    }

    private void UpdateSlideVisibility()
    {
        BtnPrevious.Content = _currentSlide == 1 ? Loc.Get("btn_back") : Loc.Get("btn_previous");

        if (_currentSlide == TotalSlides)
        {
            BtnNext.IsVisible = false;
            BtnDone.IsVisible = true;
        }
        else
        {
            BtnNext.IsVisible = true;
            BtnDone.IsVisible = false;
        }

        var activeColor = Dot1.Tag as IBrush ?? (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!;
        var inactiveColor = this.FindResource("PanelAccentBrush") as IBrush ?? Brushes.Gray;

        Dot1.Fill = _currentSlide >= 1 ? activeColor : inactiveColor;
        Dot2.Fill = _currentSlide >= 2 ? activeColor : inactiveColor;
        Dot3.Fill = _currentSlide >= 3 ? activeColor : inactiveColor;

        if (_selectedProvider == Provider.Lovense)
        {
            LovenseSlide1.IsVisible = _currentSlide == 1;
            LovenseSlide2.IsVisible = _currentSlide == 2;
            LovenseSlide3.IsVisible = _currentSlide == 3;
        }
        else
        {
            ButtplugSlide1.IsVisible = _currentSlide == 1;
            ButtplugSlide2.IsVisible = _currentSlide == 2;
            ButtplugSlide3.IsVisible = _currentSlide == 3;
        }
    }

    private static Color ParseColor(object? hexObj, Color fallback)
    {
        try
        {
            var hex = hexObj?.ToString();
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return Color.Parse(hex);
        }
        catch
        {
            return fallback;
}
    }
}
