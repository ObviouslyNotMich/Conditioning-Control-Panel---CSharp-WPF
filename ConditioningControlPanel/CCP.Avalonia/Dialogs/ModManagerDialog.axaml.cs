using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Avalonia.Windows;
using AvaloniaApp = global::ConditioningControlPanel.Avalonia.App;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Avalonia port of the mod browser/manager dialog.
/// Supports installing, uninstalling, and activating mods via <see cref="IModService"/>.
/// </summary>
public partial class ModManagerDialog : Window
{
    /// <summary>
    /// True if the user activated a different mod during this session.
    /// </summary>
    public bool ModWasChanged { get; private set; }

    private readonly IDialogService? _dialogService;
    private readonly IModService? _modService;
    private readonly ILogger<ModManagerDialog>? _logger;
    private readonly ObservableCollection<ModPackage> _mods = new();

    public ModManagerDialog()
    {
        InitializeComponent();

        _dialogService = AvaloniaApp.Services?.GetService<IDialogService>();
        _modService = AvaloniaApp.Services?.GetService<IModService>();
        _logger = AvaloniaApp.Services?.GetRequiredService<ILogger<ModManagerDialog>>();

        ModList.ItemsSource = _mods;
        RefreshModList();
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        Close(ModWasChanged);
    }

    private void ModList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshDetails();
    }

    private void BtnActivate_Click(object? sender, RoutedEventArgs e)
    {
        if (_modService == null || ModList.SelectedItem is not ModPackage package) return;

        if (_modService.ActivateMod(package.Id))
        {
            ModWasChanged = true;
        }

        RefreshDetails();
    }

    private async void BtnUninstall_Click(object? sender, RoutedEventArgs e)
    {
        if (_modService == null || ModList.SelectedItem is not ModPackage package) return;

        if (package.IsBuiltIn)
        {
            await ShowMessageAsync(Loc.Get("dialog_mod_manager_built_in_cannot_uninstall"));
            return;
        }

        var confirmed = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("title_confirm_uninstall"),
            Loc.GetF("dialog_mod_manager_uninstall_confirm_fmt", package.Name)) ?? Task.FromResult(false));

        if (!confirmed) return;

        if (_modService.UninstallMod(package.Id))
        {
            RefreshModList();
            DetailsPanel.IsVisible = false;
        }
        else
        {
            await ShowMessageAsync(Loc.Get("dialog_mod_manager_uninstall_failed"));
        }
    }

    private async void BtnInstall_Click(object? sender, RoutedEventArgs e)
    {
        if (_modService == null) return;

        var filters = new[]
        {
            new FileFilter("CCP Mod Files", new[] { "ccpmod" }),
            new FileFilter("All Files", new[] { "*" })
        };

        IReadOnlyList<string> files = Array.Empty<string>();
        if (_dialogService != null)
        {
            BtnInstall.IsEnabled = false;
            try
            {
                files = await _dialogService.ShowOpenFileDialogAsync(
                    Loc.Get("title_install_mod"),
                    filters);
            }
            finally
            {
                BtnInstall.IsEnabled = true;
            }
        }

        if (files.Count == 0) return;

        var result = await _modService.InstallModAsync(files[0]);
        if (result.Status == ModInstallStatus.Success && result.InstalledMod != null)
        {
            RefreshModList();
            ModList.SelectedItem = result.InstalledMod;
            RefreshDetails();
        }
        else
        {
            await ShowMessageAsync(result.ErrorMessage ?? Loc.Get("dialog_mod_manager_installation_failed"));
        }
    }

    private async void BtnExport_Click(object? sender, RoutedEventArgs e)
    {
        if (_modService == null) return;

        var filters = new[]
        {
            new FileFilter("CCP Mod Files", new[] { "ccpmod" })
        };

        var defaultName = $"{_modService.ActiveMod.Name.Replace(" ", "-").ToLowerInvariant()}-export.ccpmod";
        string? path = null;
        if (_dialogService != null)
        {
            BtnExport.IsEnabled = false;
            try
            {
                path = await _dialogService.ShowSaveFileDialogAsync(
                    Loc.Get("title_export_config_as_mod"),
                    filters,
                    defaultName);
            }
            finally
            {
                BtnExport.IsEnabled = true;
            }
        }

        if (string.IsNullOrEmpty(path)) return;

        BtnExport.IsEnabled = false;
        try
        {
            await _modService.ExportCurrentAsModAsync(
                path,
                _modService.ActiveMod.Name + " Export",
                _modService.ActiveMod.Manifest.Author);

            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_export_complete"),
                Loc.GetF("msg_mod_exported_to", path)) ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to export mod to {Path}", path);
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_export_error"),
                Loc.GetF("msg_export_failed", ex.Message),
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
        finally
        {
            BtnExport.IsEnabled = true;
        }
    }

    private async void BtnCreate_Click(object? sender, RoutedEventArgs e)
    {
        var creator = new ModCreatorWindow();
        await creator.ShowDialog(this);
    }

    private void RefreshModList()
    {
        _mods.Clear();
        if (_modService != null)
        {
            foreach (var mod in _modService.InstalledMods)
                _mods.Add(mod);
        }

        TxtNoMods.IsVisible = _mods.Count == 0;
    }

    private void RefreshDetails()
    {
        if (ModList.SelectedItem is not ModPackage package)
        {
            DetailsPanel.IsVisible = false;
            return;
        }

        TxtModName.Text = package.Name;
        TxtModAuthor.Text = Loc.GetF("dialog_mod_manager_author_fmt", package.Manifest.Author);
        TxtModVersion.Text = Loc.GetF("dialog_mod_manager_version_fmt", package.Manifest.Version);
        TxtModDescription.Text = package.Manifest.Description ?? "";

        var accent = package.Manifest.Theme?.AccentColor ?? "#FF69B4";
        ThemeColorPreview.Background = BrushFromHex(accent);
        TxtThemeColor.Text = accent;

        TxtCompanion.Text = package.Manifest.Identity?.CompanionName ?? "-";
        TxtActiveIndicator.IsVisible = _modService?.ActiveMod?.Id == package.Id;

        DetailsPanel.IsVisible = true;
    }

    private static IBrush BrushFromHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!;
        try { return Brush.Parse(hex); }
        catch { return (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!; }
    }

    private async Task ShowMessageAsync(string message)
    {
        if (_dialogService != null)
        {
            await _dialogService.ShowMessageAsync(Loc.Get("title_mod_manager"), message);
        }
        else
        {
            await ShowTodoAsync(message);
        }
    }

    private async Task ShowTodoAsync(string feature)
    {
        if (_dialogService != null)
        {
            await _dialogService.ShowMessageAsync(
                Loc.Get("title_todo"),
                Loc.GetF("msg_todo_not_yet_wired_up", feature),
                DialogSeverity.Info);
        }
    }
}
