using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.Assets partial.
/// Manages the local asset tree, disabled-asset selection, asset presets,
/// and content-pack UI. Pack/thumbnail/preview services are not abstracted
/// in Core yet, so those commands are stubbed with TODOs.
/// </summary>
public partial class AssetsTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly IAppEnvironment? _appEnvironment;
    private readonly IPlatformCapabilities? _platformCapabilities;
    private readonly IFlashService? _flashService;
    private readonly IAppLogger? _logger;

    private static readonly string[] ValidExtensions =
    {
        ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".heic", ".avif",
        ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm"
    };

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };
    private static readonly string[] VideoExtensions = { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm" };

    private bool _isUpdatingCheckState;

    public AssetsTabViewModel() : base("assets", "Assets", "📁")
    {
        _assetTree = new ObservableCollection<AssetTreeItem>();
        _currentFiles = new ObservableCollection<AssetFileItem>();
        _availablePacks = new ObservableCollection<PackCardViewModel>();
        _assetPresets = new ObservableCollection<AssetPreset>();
        InitializeDesignTimeData();
    }

    public AssetsTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        IAppEnvironment appEnvironment,
        IPlatformCapabilities platformCapabilities,
        IFlashService flashService,
        IAppLogger logger) : base("assets", "Assets", "📁")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _appEnvironment = appEnvironment;
        _platformCapabilities = platformCapabilities;
        _flashService = flashService;
        _logger = logger;
        _assetTree = new ObservableCollection<AssetTreeItem>();
        _currentFiles = new ObservableCollection<AssetFileItem>();
        _availablePacks = new ObservableCollection<PackCardViewModel>();
        _assetPresets = new ObservableCollection<AssetPreset>();

        InitializeDefaultPreset();
        RefreshAssetTree();
        RefreshAssetPresets();
    }

    [ObservableProperty]
    private ObservableCollection<AssetTreeItem> _assetTree;

    [ObservableProperty]
    private ObservableCollection<AssetFileItem> _currentFiles;

    [ObservableProperty]
    private AssetTreeItem? _selectedFolder;

    [ObservableProperty]
    private ObservableCollection<PackCardViewModel> _availablePacks;

    [ObservableProperty]
    private ObservableCollection<AssetPreset> _assetPresets;

    [ObservableProperty]
    private AssetPreset? _selectedAssetPreset;

    [ObservableProperty]
    private string _assetCountsText = "0 images, 0 videos active";

    [ObservableProperty]
    private string _emptyThumbnailsText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isPacksSectionVisible;

    [ObservableProperty]
    private bool _hasCurrentFiles;

    private bool _isLoadingPreset;

    partial void OnSelectedFolderChanged(AssetTreeItem? value)
    {
        LoadFolderFiles(value);
    }

    partial void OnSelectedAssetPresetChanged(AssetPreset? value)
    {
        if (_isLoadingPreset || value == null) return;
        ApplyAssetPreset(value);
    }

    #region Asset Tree

    [RelayCommand]
    private void RefreshAssetTree()
    {
        AssetTree.Clear();
        var assetsPath = _appEnvironment?.EffectiveAssetsPath;
        if (string.IsNullOrWhiteSpace(assetsPath)) return;

        try
        {
            Directory.CreateDirectory(assetsPath);
            Directory.CreateDirectory(Path.Combine(assetsPath, "images"));
            Directory.CreateDirectory(Path.Combine(assetsPath, "videos"));
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Failed to ensure asset directories");
        }

        var imagesFolder = Path.Combine(assetsPath, "images");
        if (Directory.Exists(imagesFolder))
        {
            var imagesNode = BuildFolderTree(imagesFolder, Loc.Get("asset_folder_images"));
            imagesNode.IsExpanded = true;
            AssetTree.Add(imagesNode);
        }

        var videosFolder = Path.Combine(assetsPath, "videos");
        if (Directory.Exists(videosFolder))
        {
            var videosNode = BuildFolderTree(videosFolder, Loc.Get("asset_folder_videos"));
            videosNode.IsExpanded = true;
            AssetTree.Add(videosNode);
        }

        // TODO: add content-pack virtual folders once IContentPackService is extracted to Core.

        UpdateAssetCounts();
    }

    private AssetTreeItem BuildFolderTree(string path, string name)
    {
        var node = new AssetTreeItem
        {
            Name = name,
            FullPath = path,
            IsChecked = true
        };

        List<string> files;
        try
        {
            files = Directory.GetFiles(path)
                .Where(f => ValidExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            _logger?.Warning("BuildFolderTree: skipping unreadable folder {Path}: {Error}", path, ex.Message);
            node.UpdateCheckState();
            return node;
        }

        node.FileCount = files.Count;
        var basePath = _appEnvironment?.EffectiveAssetsPath ?? "";
        node.CheckedFileCount = files.Count(f =>
        {
            var relativePath = GetRelativePath(basePath, f);
            return !(_settingsService?.Current?.DisabledAssetPaths.Contains(relativePath) ?? false);
        });

        string[] subDirs;
        try
        {
            subDirs = Directory.GetDirectories(path);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            _logger?.Warning("BuildFolderTree: cannot enumerate subfolders of {Path}: {Error}", path, ex.Message);
            subDirs = Array.Empty<string>();
        }

        foreach (var dir in subDirs)
        {
            var child = BuildFolderTree(dir, Path.GetFileName(dir));
            child.Parent = node;
            node.Children.Add(child);
        }

        node.UpdateCheckState();
        return node;
    }

    private void LoadFolderFiles(AssetTreeItem? folder)
    {
        CurrentFiles.Clear();
        EmptyThumbnailsText = "";

        if (folder == null)
        {
            EmptyThumbnailsText = Loc.Get("label_select_a_folder_to_view_its_contents");
            HasCurrentFiles = false;
            return;
        }

        if (folder.IsPackFolder)
        {
            EmptyThumbnailsText = Loc.Get("label_no_files_in_this_pack_folder");
            HasCurrentFiles = false;
            // TODO: load pack folder thumbnails once IContentPackService is extracted.
            return;
        }

        if (string.IsNullOrWhiteSpace(folder.FullPath) || !Directory.Exists(folder.FullPath))
        {
            EmptyThumbnailsText = Loc.Get("label_folder_does_not_exist");
            HasCurrentFiles = false;
            return;
        }

        var files = Directory.GetFiles(folder.FullPath)
            .Where(f => ValidExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(Path.GetFileName)
            .ToList();

        if (files.Count == 0)
        {
            EmptyThumbnailsText = Loc.Get("label_no_media_files_in_this_folder");
            HasCurrentFiles = false;
            return;
        }

        var basePath = _appEnvironment?.EffectiveAssetsPath ?? "";
        foreach (var file in files)
        {
            var relativePath = GetRelativePath(basePath, file);
            var isActive = !(_settingsService?.Current?.DisabledAssetPaths.Contains(relativePath) ?? false);
            var item = new AssetFileItem
            {
                FullPath = file,
                RelativePath = relativePath,
                IsChecked = isActive
            };
            try { item.SizeBytes = new FileInfo(file).Length; }
            catch { /* ignore */ }

            CurrentFiles.Add(item);
            // TODO: load thumbnail asynchronously once IThumbnailService is extracted.
        }

        HasCurrentFiles = CurrentFiles.Count > 0;
    }

    [RelayCommand]
    private void ToggleFolderCheck(AssetTreeItem? folder)
    {
        if (folder == null || _isUpdatingCheckState) return;

        _isUpdatingCheckState = true;
        try
        {
            SetFolderAndChildrenChecked(folder, folder.IsChecked);
            UpdateFolderFilesCheckState(folder, folder.IsChecked);
            folder.Parent?.UpdateCheckStateFromChildren();
            UpdateAssetCounts();
            RefreshThumbnailCheckboxes();
            InvalidateAssetPools();
        }
        finally
        {
            _isUpdatingCheckState = false;
        }
    }

    [RelayCommand]
    private void ToggleFileCheck(AssetFileItem? file)
    {
        if (file == null) return;
        UpdateFileCheckState(file);
    }

    [RelayCommand]
    private void SelectAllAssets()
    {
        if (SelectedFolder == null) return;
        _isUpdatingCheckState = true;
        try
        {
            UpdateFolderFilesCheckState(SelectedFolder, true);
            SetFolderAndChildrenChecked(SelectedFolder, true);
            SelectedFolder.Parent?.UpdateCheckStateFromChildren();
            RefreshThumbnailCheckboxes();
            UpdateAssetCounts();
            InvalidateAssetPools();
        }
        finally
        {
            _isUpdatingCheckState = false;
        }
    }

    [RelayCommand]
    private void DeselectAllAssets()
    {
        if (SelectedFolder == null) return;
        _isUpdatingCheckState = true;
        try
        {
            UpdateFolderFilesCheckState(SelectedFolder, false);
            SetFolderAndChildrenChecked(SelectedFolder, false);
            SelectedFolder.Parent?.UpdateCheckStateFromChildren();
            RefreshThumbnailCheckboxes();
            UpdateAssetCounts();
            InvalidateAssetPools();
        }
        finally
        {
            _isUpdatingCheckState = false;
        }
    }

    [RelayCommand]
    private async Task SaveAssetSelectionAsync()
    {
        try
        {
            _settingsService?.Save();
            InvalidateAssetPools(fullReload: true);

            var disabledCount = _settingsService?.Current?.DisabledAssetPaths.Count ?? 0;
            var message = disabledCount > 0
                ? string.Format(Loc.Get("msg_selection_saved_disabled_fmt"), disabledCount)
                : Loc.Get("msg_selection_saved_active");

            await (_dialogService?.ShowMessageAsync(Loc.Get("title_selection_saved"), message) ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to save asset selection");
        }
    }

    private void UpdateFolderFilesCheckState(AssetTreeItem folder, bool isChecked)
    {
        var basePath = _appEnvironment?.EffectiveAssetsPath ?? "";
        if (!string.IsNullOrWhiteSpace(folder.FullPath) && Directory.Exists(folder.FullPath))
        {
            var files = Directory.GetFiles(folder.FullPath)
                .Where(f => ValidExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
            foreach (var file in files)
            {
                var relativePath = GetRelativePath(basePath, file);
                var disabledPaths = _settingsService?.Current?.DisabledAssetPaths;
                if (disabledPaths == null) continue;

                if (isChecked) disabledPaths.Remove(relativePath);
                else disabledPaths.Add(relativePath);
            }
        }

        foreach (var child in folder.Children)
        {
            UpdateFolderFilesCheckState(child, isChecked);
        }

        folder.CheckedFileCount = isChecked ? folder.FileCount : 0;
    }

    private void SetFolderAndChildrenChecked(AssetTreeItem folder, bool isChecked)
    {
        folder.IsChecked = isChecked;
        folder.CheckedFileCount = isChecked ? folder.FileCount : 0;
        foreach (var child in folder.Children)
        {
            SetFolderAndChildrenChecked(child, isChecked);
        }
    }

    private void UpdateFileCheckState(AssetFileItem file)
    {
        var disabledPaths = _settingsService?.Current?.DisabledAssetPaths;
        if (disabledPaths == null) return;

        if (file.IsChecked) disabledPaths.Remove(file.RelativePath);
        else disabledPaths.Add(file.RelativePath);

        InvalidateAssetPools();

        _isUpdatingCheckState = true;
        try
        {
            UpdateParentFolderCheckState();
            UpdateAssetCounts();
        }
        finally
        {
            _isUpdatingCheckState = false;
        }
    }

    private void UpdateParentFolderCheckState()
    {
        if (SelectedFolder == null) return;
        var checkedCount = CurrentFiles.Count(f => f.IsChecked);
        SelectedFolder.CheckedFileCount = checkedCount;
        SelectedFolder.UpdateCheckState();
    }

    private void RefreshThumbnailCheckboxes()
    {
        var disabledPaths = _settingsService?.Current?.DisabledAssetPaths;
        if (disabledPaths == null) return;

        foreach (var item in CurrentFiles)
        {
            item.IsChecked = !disabledPaths.Contains(item.RelativePath);
        }
    }

    private void InvalidateAssetPools(bool fullReload = false)
    {
        try
        {
            if (fullReload)
                _flashService?.LoadAssets();
            else
                _flashService?.ClearFileCache();
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "InvalidateAssetPools: flash service cache clear failed");
        }
        _logger?.Information("Asset selection changed; pool invalidation requested (fullReload={FullReload})", fullReload);
    }

    private void UpdateAssetCounts()
    {
        var totalImages = 0;
        var totalVideos = 0;
        var activeImages = 0;
        var activeVideos = 0;
        CountAssetsRecursive(AssetTree, ref totalImages, ref totalVideos, ref activeImages, ref activeVideos);
        AssetCountsText = $"{activeImages} images, {activeVideos} videos active";
    }

    private void CountAssetsRecursive(IEnumerable<AssetTreeItem> items, ref int totalImages, ref int totalVideos, ref int activeImages, ref int activeVideos)
    {
        var disabledPaths = _settingsService?.Current?.DisabledAssetPaths;

        foreach (var folder in items)
        {
            if (folder.IsPackFolder)
            {
                // TODO: count pack files once IContentPackService is extracted.
                continue;
            }

            if (!string.IsNullOrWhiteSpace(folder.FullPath) && Directory.Exists(folder.FullPath))
            {
                var basePath = _appEnvironment?.EffectiveAssetsPath ?? "";
                var files = Directory.GetFiles(folder.FullPath);
                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    var isImage = ImageExtensions.Contains(ext);
                    var isVideo = VideoExtensions.Contains(ext);
                    if (isImage) totalImages++;
                    if (isVideo) totalVideos++;

                    var relativePath = GetRelativePath(basePath, file);
                    var isActive = !(disabledPaths?.Contains(relativePath) ?? false);
                    if (isActive && isImage) activeImages++;
                    if (isActive && isVideo) activeVideos++;
                }
            }

            CountAssetsRecursive(folder.Children, ref totalImages, ref totalVideos, ref activeImages, ref activeVideos);
        }
    }

    #endregion

    #region Asset Presets

    private void InitializeDefaultPreset()
    {
        var presets = _settingsService?.Current?.AssetPresets;
        if (presets == null) return;

        if (!presets.Any(p => p.IsDefault))
        {
            presets.Insert(0, AssetPreset.CreateDefault());
        }

        var defaultPreset = presets.FirstOrDefault(p => p.IsDefault);
        if (defaultPreset != null)
        {
            var (activeImages, activeVideos) = GetCurrentActiveAssetCounts();
            defaultPreset.EnabledImageCount = activeImages;
            defaultPreset.EnabledVideoCount = activeVideos;
        }
    }

    private void RefreshAssetPresets()
    {
        _isLoadingPreset = true;
        AssetPresets.Clear();
        var presets = _settingsService?.Current?.AssetPresets;
        if (presets != null)
        {
            foreach (var preset in presets)
            {
                AssetPresets.Add(preset);
            }
        }

        var currentId = _settingsService?.Current?.CurrentAssetPresetId;
        if (!string.IsNullOrWhiteSpace(currentId))
        {
            SelectedAssetPreset = AssetPresets.FirstOrDefault(p => p.Id == currentId);
        }
        else
        {
            SelectedAssetPreset = AssetPresets.FirstOrDefault(p => p.IsDefault);
        }

        _isLoadingPreset = false;
    }

    private void ApplyAssetPreset(AssetPreset preset)
    {
        if (_settingsService?.Current == null) return;
        preset.ApplyToSettings();
        _settingsService.Current.CurrentAssetPresetId = preset.Id;
        _settingsService.Save();

        RefreshAssetTree();
        RefreshThumbnailCheckboxes();
        UpdateAssetCounts();
        InvalidateAssetPools();

        var (activeImages, activeVideos) = GetCurrentActiveAssetCounts();
        _logger?.Information("Loaded asset preset: {Name} ({Images} images, {Videos} videos active)",
            preset.Name, activeImages, activeVideos);
    }

    [RelayCommand]
    private async Task SaveAssetPresetAsync()
    {
        if (_settingsService?.Current == null) return;
        var (imageCount, videoCount) = GetCurrentActiveAssetCounts();

        // TODO: replace with IDialogService.ShowInputDialogAsync once available.
        var name = await PromptNameAsync("Save Asset Preset", $"Preset {_settingsService.Current.AssetPresets.Count}");
        if (string.IsNullOrWhiteSpace(name)) return;

        var disabledCount = _settingsService.Current.DisabledAssetPaths.Count;
        var preset = AssetPreset.FromCurrentSettings(name.Trim(), imageCount, videoCount);
        _settingsService.Current.AssetPresets.Add(preset);
        _settingsService.Current.CurrentAssetPresetId = preset.Id;
        _settingsService.Save();

        RefreshAssetPresets();
        SelectedAssetPreset = AssetPresets.FirstOrDefault(p => p.Id == preset.Id);
        InvalidateAssetPools();

        await (_dialogService?.ShowMessageAsync(
            "Preset Saved",
            $"Preset '{preset.Name}' saved!\n\n{imageCount} images, {videoCount} videos enabled.\n{disabledCount} assets disabled.") ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task UpdateAssetPresetAsync()
    {
        var preset = SelectedAssetPreset;
        if (preset == null)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("msg_please_select_a_preset_to_update"),
                DialogSeverity.Warning) ?? Task.CompletedTask);
            return;
        }

        if (preset.IsDefault)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("msg_cannot_update_the_default_all_assets_preset_n"),
                DialogSeverity.Warning) ?? Task.CompletedTask);
            return;
        }

        var confirm = await (_dialogService?.ShowConfirmationAsync(
            "Update Preset",
            $"Update preset '{preset.Name}' with the current selection?") ?? Task.FromResult(false));
        if (!confirm) return;

        var (imageCount, videoCount) = GetCurrentActiveAssetCounts();
        var disabledCount = _settingsService?.Current?.DisabledAssetPaths.Count ?? 0;
        preset.UpdateFromCurrentSettings(imageCount, videoCount);
        _settingsService?.Save();

        RefreshAssetPresets();
        SelectedAssetPreset = AssetPresets.FirstOrDefault(p => p.Id == preset.Id);
        InvalidateAssetPools();

        await (_dialogService?.ShowMessageAsync(
            "Preset Updated",
            $"Preset '{preset.Name}' updated!\n\n{imageCount} images, {videoCount} videos enabled.\n{disabledCount} assets disabled.") ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task DeleteAssetPresetAsync()
    {
        var preset = SelectedAssetPreset;
        if (preset == null)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("msg_please_select_a_preset_to_delete"),
                DialogSeverity.Warning) ?? Task.CompletedTask);
            return;
        }

        if (preset.IsDefault)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("msg_cannot_delete_the_default_all_assets_preset"),
                DialogSeverity.Warning) ?? Task.CompletedTask);
            return;
        }

        var confirm = await (_dialogService?.ShowConfirmationAsync(
            "Delete Preset",
            $"Delete preset '{preset.Name}'?\n\nThis cannot be undone.") ?? Task.FromResult(false));
        if (!confirm) return;

        _settingsService?.Current?.AssetPresets.Remove(preset);
        var defaultPreset = _settingsService?.Current?.AssetPresets.FirstOrDefault(p => p.IsDefault);
        if (_settingsService?.Current != null)
        {
            _settingsService.Current.CurrentAssetPresetId = defaultPreset?.Id;
        }
        _settingsService?.Save();
        RefreshAssetPresets();
    }

    private (int images, int videos) GetCurrentActiveAssetCounts()
    {
        var totalImages = 0;
        var totalVideos = 0;
        var activeImages = 0;
        var activeVideos = 0;
        CountAssetsRecursive(AssetTree, ref totalImages, ref totalVideos, ref activeImages, ref activeVideos);
        return (activeImages, activeVideos);
    }

    #endregion

    #region Content Packs

    [RelayCommand]
    private async Task DeleteDownloadedPacksAsync()
    {
        var installedIds = _settingsService?.Current?.InstalledPackIds;
        if (installedIds == null || installedIds.Count == 0)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_delete_downloaded_packs"),
                Loc.Get("msg_no_downloaded_packs_to_delete")) ?? Task.CompletedTask);
            return;
        }

        var confirm = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("title_delete_downloaded_packs"),
            Loc.GetF("msg_delete_downloaded_packs_confirm_0", installedIds.Count)) ?? Task.FromResult(false));
        if (!confirm) return;

        // TODO: wire to IContentPackService.UninstallPack() once extracted.
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            "Pack uninstallation is not yet ported to Avalonia.") ?? Task.CompletedTask);
    }

    [RelayCommand]
    private void TogglePacksSection()
    {
        IsPacksSectionVisible = !IsPacksSectionVisible;
    }

    [RelayCommand]
    private async Task RefreshPacksAsync()
    {
        IsBusy = true;
        try
        {
            _logger?.Information("Refreshing content packs");
            AvailablePacks.Clear();
            // TODO: wire to IContentPackService.GetAvailablePacksAsync() once extracted.
            await Task.Delay(250);
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_not_implemented"),
                "Content pack refresh is not yet ported to Avalonia.") ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Failed to refresh packs");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task TogglePackAsync(PackCardViewModel? pack)
    {
        if (pack == null) return;
        _logger?.Information("Pack toggle requested: {PackId}", pack.Id);

        if (pack.IsDownloaded)
        {
            var confirm = await (_dialogService?.ShowConfirmationAsync(
                "Uninstall Content Pack",
                $"Uninstall '{pack.Name}'?\n\nThis will delete downloaded content from your computer.") ?? Task.FromResult(false));
            if (!confirm) return;

            // TODO: wire to IContentPackService.UninstallPack() once extracted.
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_not_implemented"),
                "Pack uninstallation is not yet ported to Avalonia.") ?? Task.CompletedTask);
        }
        else
        {
            if (pack.IsExternal)
            {
                // TODO: wire to IContentPackService.GetExternalPackDownloadUrlAsync() once extracted.
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("title_not_implemented"),
                    "External pack download is not yet ported to Avalonia.") ?? Task.CompletedTask);
                return;
            }

            var sizeStr = pack.Pack.SizeBytes > 0 ? $" ({pack.Pack.SizeBytes / (1024.0 * 1024):F0} MB)" : "";
            var confirm = await (_dialogService?.ShowConfirmationAsync(
                "Install Content Pack",
                $"Download and install '{pack.Name}'?{sizeStr}\n\nThis will download encrypted content to a secure folder on your computer.") ?? Task.FromResult(false));
            if (!confirm) return;

            // TODO: wire to IContentPackService.InstallPackAsync() once extracted.
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_not_implemented"),
                "Pack installation is not yet ported to Avalonia.") ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task ActivatePackAsync(PackCardViewModel? pack)
    {
        if (pack == null) return;
        _logger?.Information("Pack activation requested: {PackId}", pack.Id);

        // TODO: wire to IContentPackService.ActivatePackAsync() once extracted.
        pack.Pack.IsActive = !pack.Pack.IsActive;

        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            $"Pack '{pack.Name}' is now {(pack.Pack.IsActive ? "active" : "inactive")}.") ?? Task.CompletedTask);
    }

    #endregion

    #region Actions

    [RelayCommand]
    private async Task OpenAssetsFolderAsync()
    {
        var path = _appEnvironment?.EffectiveAssetsPath;
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            Directory.CreateDirectory(Path.Combine(path, "images"));
            Directory.CreateDirectory(Path.Combine(path, "videos"));

            if (_platformCapabilities?.IsWindows == true)
            {
                Process.Start("explorer.exe", path);
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }

            _logger?.Information("Opened assets folder: {Path}", path);
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Failed to open assets folder");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                ex.Message,
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task OpenAssetPreviewAsync(AssetFileItem? file)
    {
        if (file == null) return;
        _logger?.Information("Open asset preview requested: {File}", file.Name);
        // TODO: open an Avalonia media preview window once ported.
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            "Asset preview is not yet ported to Avalonia.") ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task OpenAssetInExplorerAsync(AssetFileItem? file)
    {
        if (file == null || !File.Exists(file.FullPath)) return;
        try
        {
            if (_platformCapabilities?.IsWindows == true)
            {
                Process.Start("explorer.exe", $"/select,\"{file.FullPath}\"");
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.GetDirectoryName(file.FullPath),
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Failed to open file in Explorer");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                ex.Message,
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task OpenCreatorDiscordAsync()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://discord.gg/YxVAMt4qaZ",
                UseShellExecute = true
            });
            _logger?.Information("Opened creator Discord link");
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Failed to open Discord link");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                ex.Message,
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
    }

    #endregion

    #region Design-Time Data

    private void InitializeDesignTimeData()
    {
        if (!global::Avalonia.Controls.Design.IsDesignMode) return;

        AssetCountsText = "12 images, 4 videos active";
        EmptyThumbnailsText = Loc.Get("label_select_a_folder_to_view_its_contents");

        var samplePack = new PackCardViewModel(new ContentPack
        {
            Id = "sample-pack",
            Name = "Sample Pack",
            Description = "A sample content pack for design-time preview.",
            SizeBytes = 15 * 1024 * 1024,
            ImageCount = 24,
            VideoCount = 6,
            IsDownloaded = true,
            IsActive = true
        })
        {
            InstallCommand = TogglePackCommand,
            ActivateCommand = ActivatePackCommand,
            InstallCommandParameter = null,
            ActivateCommandParameter = null
        };
        AvailablePacks.Add(samplePack);

        var externalPack = new PackCardViewModel(new ContentPack
        {
            Id = "external-pack",
            Name = "External Pack",
            Description = "Manual download pack preview.",
            SizeBytes = 42 * 1024 * 1024,
            ImageCount = 50,
            VideoCount = 10,
            IsExternalFlag = true,
            ExternalUrl = "https://example.com/pack"
        })
        {
            InstallCommand = TogglePackCommand,
            ActivateCommand = ActivatePackCommand
        };
        AvailablePacks.Add(externalPack);

        var treeRoot = new AssetTreeItem
        {
            Name = Loc.Get("asset_folder_images"),
            FullPath = "/assets/images",
            FileCount = 12,
            CheckedFileCount = 10,
            IsExpanded = true
        };
        treeRoot.Children.Add(new AssetTreeItem
        {
            Name = "spirals",
            FullPath = "/assets/images/spirals",
            FileCount = 5,
            CheckedFileCount = 5
        });
        AssetTree.Add(treeRoot);

        AssetTree.Add(new AssetTreeItem
        {
            Name = Loc.Get("asset_folder_videos"),
            FullPath = "/assets/videos",
            FileCount = 4,
            CheckedFileCount = 4
        });

        AssetPresets.Add(AssetPreset.CreateDefault());
        AssetPresets.Add(new AssetPreset
        {
            Id = "preset-1",
            Name = "Only Spirals",
            EnabledImageCount = 10,
            EnabledVideoCount = 0
        });
        SelectedAssetPreset = AssetPresets.FirstOrDefault();
    }

    #endregion

    private async Task<string?> PromptNameAsync(string title, string defaultValue)
    {
        // Avalonia's IDialogService does not expose an input dialog yet.
        // TODO: replace with a proper input dialog once available.
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            "Input dialogs are not yet available in the Avalonia head.") ?? Task.CompletedTask);
        return defaultValue;
    }

    private static string GetRelativePath(string basePath, string filePath)
    {
        var relative = string.IsNullOrWhiteSpace(basePath)
            ? filePath
            : Path.GetRelativePath(basePath, filePath);
        return relative.Replace('\\', '/');
    }
}
