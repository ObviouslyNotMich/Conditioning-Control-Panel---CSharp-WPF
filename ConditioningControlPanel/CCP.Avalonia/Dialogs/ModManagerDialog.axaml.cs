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
    private readonly IAppLogger? _logger;
    private readonly ObservableCollection<ModPackage> _mods = new();

    public ModManagerDialog()
    {
        InitializeComponent();

        _dialogService = AvaloniaApp.Services?.GetService<IDialogService>();
        _modService = AvaloniaApp.Services?.GetService<IModService>();
        _logger = AvaloniaApp.Services?.GetService<IAppLogger>();

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
        var filters = new[]
        {
            new FileFilter("CCP Mod Files", new[] { "ccpmod" })
        };

        string? path = null;
        if (_dialogService != null)
        {
            BtnExport.IsEnabled = false;
            try
            {
                path = await _dialogService.ShowSaveFileDialogAsync(
                    Loc.Get("title_export_config_as_mod"),
                    filters,
                    "export.ccpmod");
            }
            finally
            {
                BtnExport.IsEnabled = true;
            }
        }

        ShowTodo(path != null
            ? $"mod export to '{path}'"
            : "mod export");
    }

    private void BtnCreate_Click(object? sender, RoutedEventArgs e)
    {
        ShowTodo("mod creation");
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
        if (string.IsNullOrWhiteSpace(hex)) return new SolidColorBrush(Colors.White);
        try { return Brush.Parse(hex); }
        catch { return new SolidColorBrush(Colors.White); }
    }

    private async Task ShowMessageAsync(string message)
    {
        if (_dialogService != null)
        {
            await _dialogService.ShowMessageAsync("Mod Manager", message);
        }
        else
        {
            ShowTodo(message);
        }
    }

    private static void ShowTodo(string feature)
    {
        MessageBoxStub.Show(
            $"TODO: {feature} is not yet wired up in the Avalonia build.",
            "TODO",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
