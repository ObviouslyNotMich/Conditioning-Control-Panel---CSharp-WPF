using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Desktop file/folder picker implementation that delegates to the shared
/// <see cref="IDialogService"/>. This keeps the existing dialog flows unchanged
/// while letting the same <see cref="IFilePickerService"/> interface work on mobile.
/// </summary>
public sealed class DesktopFilePickerService : IFilePickerService
{
    private readonly IDialogService _dialogService;

    public DesktopFilePickerService(IDialogService dialogService)
    {
        _dialogService = dialogService;
    }

    public Task<IReadOnlyList<string>> PickFilesAsync(
        string title,
        IReadOnlyList<FileFilter> filters,
        bool allowMultiple = false)
        => _dialogService.ShowOpenFileDialogAsync(title, filters, allowMultiple);

    public Task<string?> PickFolderAsync(string title)
        => _dialogService.ShowOpenFolderDialogAsync(title);
}
