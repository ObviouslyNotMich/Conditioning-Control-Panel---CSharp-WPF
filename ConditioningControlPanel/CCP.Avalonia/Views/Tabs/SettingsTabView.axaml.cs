using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class SettingsTabView : UserControl
{
    public SettingsTabView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void CardFlash_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: open Flash feature popup
    }

    private void CardVisuals_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: open Visuals feature popup
    }

    private void CardVideo_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: open Mandatory Video feature popup
    }

    private void CardSubliminal_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: open Subliminals feature popup
    }

    private void CardSpiral_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: open Spiral Overlay feature popup
    }

    private void CardLockCard_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: open Lock Card feature popup
    }

    private void CardPinkFilter_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: open Pink Filter feature popup
    }

    private void CardMindWipe_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: open Mind Wipe feature popup
    }

    private void CardBubblePop_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: open Bubble Pop feature popup
    }

    private void CardBouncingText_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: open Bouncing Text feature popup
    }

    private void CardSystem_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: open System feature popup
    }

    private void CardBubbleCount_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: open Bubble Count feature popup
    }

    private void ImgLogo_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // TODO: logo rapid-click easter egg / season recap trigger
    }

    private void BrowserLoadingText_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ViewModels.Tabs.SettingsTabViewModel vm)
            return;

        // Ask the platform browser host for an Avalonia control.  If it returns one,
        // replace the placeholder text with the embedded browser; otherwise leave the
        // placeholder in place and let the host open a window or system browser.
        if (vm.BrowserHost?.CreateBrowserControl() is Control browserControl)
        {
            BrowserContainer.Child = browserControl;
        }

        vm.InitializeBrowserCommand.Execute(null);
    }

    private void VelvetBtnWebcam_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: navigate to Lab / webcam page
    }

    private void VelvetBtnAppInfo_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: open App Info dialog
    }

    private void VelvetBtnSchedulerRamp_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: open Scheduler + Intensity Ramp popup
    }

    private void VelvetBtnCatalogue_Click(object? sender, RoutedEventArgs e)
    {
        // TODO: open CCP Catalogue in browser
    }
}
