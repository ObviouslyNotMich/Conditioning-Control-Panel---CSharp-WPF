using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Mobile file/folder picker implementation backed by Avalonia's cross-platform
/// <see cref="IStorageProvider"/>. This is used on Android/iOS where the shared
/// desktop dialog service is not available or not appropriate.
/// </summary>
public sealed class MobileFilePicker : IFilePickerService
{
    public async Task<IReadOnlyList<string>> PickFilesAsync(
        string title,
        IReadOnlyList<FileFilter> filters,
        bool allowMultiple = false)
    {
        var topLevel = GetCurrentTopLevel();
        if (topLevel is null) return Array.Empty<string>();

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple,
            FileTypeFilter = MapFilters(filters)
        };

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(options);
        return result.Select(r => r.Path.LocalPath).ToList();
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var topLevel = GetCurrentTopLevel();
        if (topLevel is null) return null;

        var options = new FolderPickerOpenOptions { Title = title };
        var result = await topLevel.StorageProvider.OpenFolderPickerAsync(options);
        return result.FirstOrDefault()?.Path.LocalPath;
    }

    private static IReadOnlyList<FilePickerFileType> MapFilters(IReadOnlyList<FileFilter> filters)
    {
        return filters
            .Select(f => new FilePickerFileType(f.Name)
            {
                Patterns = f.Extensions.Select(e => $"*.{e}").ToList()
            })
            .ToList();
    }

    private static TopLevel? GetCurrentTopLevel()
    {
        var lifetime = Application.Current?.ApplicationLifetime;

        if (lifetime is ISingleViewApplicationLifetime single && single.MainView is { } view)
        {
            return TopLevel.GetTopLevel(view);
        }

        if (lifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is { } window)
        {
            return window;
        }

        return null;
    }
}
