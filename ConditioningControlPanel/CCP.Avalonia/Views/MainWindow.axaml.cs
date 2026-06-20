using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Avalonia.ViewModels;
using ConditioningControlPanel.Avalonia.Windows;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Remove the native title bar and extend the client area on all platforms.
        App.Services?.GetService<IWindowChrome>()?.ExtendClientArea(this, true);

        // Window chrome: drag from the custom title bar and double-click to maximize.
        if (TitleBarGrid != null)
        {
            TitleBarGrid.PointerPressed += TitleBar_PointerPressed;
        }

        Closing += OnClosing;
        KeyDown += OnKeyDown;
        Opened += OnOpened;

        AddHandler(DragDrop.DragEnterEvent, MainWindow_DragEnter);
        AddHandler(DragDrop.DragLeaveEvent, MainWindow_DragLeave);
        AddHandler(DragDrop.DragOverEvent, MainWindow_DragOver);
        AddHandler(DragDrop.DropEvent, MainWindow_Drop);
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2)
        {
            // Double-click toggles maximize.
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            // Restore to normal before dragging; center the window on the cursor.
            var point = e.GetPosition(this) * PixelDensity;
            WindowState = WindowState.Normal;
            Position = new PixelPoint(
                (int)(point.X - Width / 2),
                (int)(point.Y - 15));
        }

        BeginMoveDrag(e);
    }

    private double PixelDensity
    {
        get
        {
            try
            {
                var screen = Screens.ScreenFromWindow(this);
                return screen?.Scaling ?? 1.0;
            }
            catch
            {
                return 1.0;
            }
        }
    }

    private void ResizeBorder_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var edge = control.Tag?.ToString() switch
        {
            "NorthWest" => WindowEdge.NorthWest,
            "NorthEast" => WindowEdge.NorthEast,
            "SouthWest" => WindowEdge.SouthWest,
            "SouthEast" => WindowEdge.SouthEast,
            "North" => WindowEdge.North,
            "South" => WindowEdge.South,
            "West" => WindowEdge.West,
            "East" => WindowEdge.East,
            _ => (WindowEdge?)null
        };

        if (edge.HasValue)
        {
            BeginResizeDrag(edge.Value, e);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // Forward tab hotkeys to the view model.
        if (vm.HandleKeyGesture(e.KeyModifiers, e.Key))
        {
            e.Handled = true;
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Unless the user holds Shift or the view model is forcing shutdown,
        // minimize to tray instead of closing.
        if (DataContext is MainWindowViewModel { IsEngineRunning: true } vm)
        {
            e.Cancel = true;
            _ = vm.ExitApplicationCommand.ExecuteAsync(null);
            return;
        }

        if (!e.IsProgrammatic)
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
        }
    }

    private async void BugReport_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var bugReport = new Windows.BugReportWindow();
            await bugReport.ShowDialog(this);
        }
        catch (Exception ex)
        {
            var logger = App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>();
            logger?.Error(ex, "Failed to open bug report window");
        }
    }

    private void WebcamActivePill_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.StopWebcamCommand?.Execute(null);
        }
    }

    #region Drag-and-Drop Import

    private void MainWindow_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Formats.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
            if (DataContext is MainWindowViewModel vm)
            {
                vm.DropOverlayVisible = true;
            }
        }
    }

    private void MainWindow_DragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Formats.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
            if (DataContext is MainWindowViewModel vm)
            {
                vm.DropOverlayVisible = true;
            }
        }
    }

    private void MainWindow_DragLeave(object? sender, DragEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.DropOverlayVisible = false;
        }
    }

    private void MainWindow_Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.DropOverlayVisible = false;
        }

        if (!e.DataTransfer.Formats.Contains(DataFormat.File))
            return;

        var files = e.DataTransfer.TryGetFiles()?.ToList();
        if (files == null || files.Count == 0)
            return;

        if (DataContext is MainWindowViewModel vm2)
        {
            vm2.AddNotification("Import", $"Dropped {files.Count} items");
        }
    }

    #endregion

    #region Startup Dialogs

    private async void OnOpened(object? sender, EventArgs e)
    {
        try
        {
            await TryShowWelcomeDialogAsync();
            await TryShowSeasonRecapAsync();
        }
        catch (Exception ex)
        {
            var logger = App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>();
            logger?.Warning(ex, "Failed to present startup dialogs");
        }
    }

    private async Task TryShowWelcomeDialogAsync()
    {
        if (App.Services?.GetService<ISettingsService>() is not { } settingsService)
            return;

        if (settingsService.Current is not { } settings || settings.Welcomed)
            return;

        var dialog = new WelcomeDialog();
        await dialog.ShowDialog<bool?>(this);

        settings.Welcomed = true;
        settingsService.Save();
    }

    private async Task TryShowSeasonRecapAsync()
    {
        if (App.Services?.GetService<ISettingsService>() is not { } settingsService)
            return;

        if (settingsService.Current is not { } settings)
            return;

        var currentSeason = DateTime.UtcNow.ToString("yyyy-MM");
        if (!settings.SeasonResetPending && settings.LastSeasonResetSeen == currentSeason)
            return;

        // Only surface the recap for users who have progression to lose.
        if (settings.HighestLevelEver < 2)
        {
            settings.LastSeasonResetSeen = currentSeason;
            settings.SeasonResetPending = false;
            settingsService.Save();
            return;
        }

        var snapshot = SeasonRecapService.CaptureAndRollover(currentSeason);
        if (snapshot == null)
        {
            settings.LastSeasonResetSeen = currentSeason;
            settings.SeasonResetPending = false;
            settingsService.Save();
            return;
        }

        var vm = new SeasonRecapViewModel(snapshot);
        var window = new SeasonRecapWindow(vm);
        await window.ShowDialog(this);

        settings.LastSeasonResetSeen = currentSeason;
        settings.SeasonResetPending = false;
        settingsService.Save();
    }

    #endregion
}
