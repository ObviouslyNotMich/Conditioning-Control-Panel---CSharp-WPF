using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.PresetIO partial.
/// Handles preset export, drag-drop import, and catalogue sharing.
/// </summary>
public partial class PresetIOTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly IAppLogger? _logger;

    public PresetIOTabViewModel() : base("presetio", "Preset IO", "🗂️")
    {
        Presets = new ObservableCollection<Preset>();
    }

    public PresetIOTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        IAppLogger logger) : base("presetio", "Preset IO", "🗂️")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;
        Presets = new ObservableCollection<Preset>();
        RefreshPresetsList();
    }

    [ObservableProperty]
    private ObservableCollection<Preset> _presets;

    [ObservableProperty]
    private Preset? _selectedPreset;

    [ObservableProperty]
    private string _dropZoneStatus = "";

    [ObservableProperty]
    private bool _isDropZoneError;

    [ObservableProperty]
    private bool _isDropZoneActive;

    partial void OnSelectedPresetChanged(Preset? value)
    {
        if (value != null)
        {
            _logger?.Information("Preset selected for IO: {Name}", value.Name);
        }
    }

    private void RefreshPresetsList()
    {
        Presets.Clear();
        foreach (var preset in Preset.GetDefaultPresets())
        {
            Presets.Add(preset);
        }

        var userPresets = _settingsService?.Current?.UserPresets;
        if (userPresets != null)
        {
            foreach (var preset in userPresets)
            {
                Presets.Add(preset);
            }
        }
    }

    [RelayCommand]
    private async Task ExportPresetAsync(Preset? preset)
    {
        preset ??= SelectedPreset;
        if (preset == null) return;

        var path = await (_dialogService?.ShowSaveFileDialogAsync(
            Loc.Get("title_export_preset"),
            new[] { new FileFilter("Preset files", new[] { "preset.json" }) },
            $"{preset.Name}.preset.json") ?? Task.FromResult<string?>(null));

        if (string.IsNullOrEmpty(path)) return;

        try
        {
            var json = JsonConvert.SerializeObject(preset, Formatting.Indented);
            await File.WriteAllTextAsync(path, json);

            _logger?.Information("Preset exported: {Name} to {Path}", preset.Name, path);
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_success"),
                string.Format(Loc.Get("msg_preset_exported_to_0"), path),
                DialogSeverity.Info) ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to export preset: {Name}", preset.Name);
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                string.Format(Loc.Get("msg_failed_to_export_preset_0"), ex.Message),
                DialogSeverity.Error) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task ImportPresetAsync(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            var files = await (_dialogService?.ShowOpenFileDialogAsync(
                "Import Preset",
                new[] { new FileFilter("Preset files", new[] { "preset.json" }) }) ?? Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));
            filePath = files.FirstOrDefault();
            if (string.IsNullOrEmpty(filePath)) return;
        }
        else if (!filePath.EndsWith(".preset.json", StringComparison.OrdinalIgnoreCase))
        {
            ShowDropZoneStatus(Loc.Get("msg_only_session_json_files_allowed"), isError: true);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var imported = JsonConvert.DeserializeObject<Preset>(json);
            if (imported == null)
            {
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("title_import_error"),
                    "Failed to import preset. The file may be invalid.",
                    DialogSeverity.Error) ?? Task.CompletedTask);
                return;
            }

            imported.Id = Guid.NewGuid().ToString();
            imported.IsDefault = false;

            if (_settingsService?.Current is { } settings)
            {
                imported.Name = MakeUniquePresetName(settings.UserPresets, imported.Name);
                settings.UserPresets.Add(imported);
                _settingsService.Save();
            }

            RefreshPresetsList();
            SelectedPreset = imported;

            _logger?.Information("Preset imported: {Name} from {Path}", imported.Name, filePath);
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_import_complete"),
                string.Format(Loc.Get("msg_imported_session"), imported.Name),
                DialogSeverity.Info) ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to import preset from {Path}", filePath);
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_import_error"),
                string.Format(Loc.Get("msg_import_failed_0"), ex.Message),
                DialogSeverity.Error) ?? Task.CompletedTask);
        }
    }

    private static string MakeUniquePresetName(System.Collections.Generic.List<Preset> userPresets, string name)
    {
        if (!userPresets.Any(p => p.Name == name)) return name;

        var importedName = $"{name} (Imported)";
        if (!userPresets.Any(p => p.Name == importedName)) return importedName;

        int i = 1;
        while (userPresets.Any(p => p.Name == $"{importedName} {i}"))
        {
            i++;
        }
        return $"{importedName} {i}";
    }

    [RelayCommand]
    private async Task SharePresetAsync(Preset? preset)
    {
        preset ??= SelectedPreset;
        if (preset == null || preset.IsDefault) return;

        // TODO: wire to CatalogueService and PatreonService once extracted to CCP.Core.
        _logger?.Information("Preset catalogue share requested: {Name}", preset.Name);
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            "Catalogue sharing is not yet available in the Avalonia head.") ?? Task.CompletedTask);
    }

    [RelayCommand]
    private void HandlePresetDrop(string[]? files)
    {
        if (files == null || files.Length != 1) return;
        _ = ImportPresetAsync(files[0]);
    }

    [RelayCommand]
    private void SetDropZoneActive(bool active)
    {
        IsDropZoneActive = active;
    }

    internal void ShowDropZoneStatus(string message, bool isError)
    {
        DropZoneStatus = message;
        IsDropZoneError = isError;
        IsDropZoneActive = true;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            DropZoneStatus = "";
            IsDropZoneError = false;
            IsDropZoneActive = false;
        });
    }
}
