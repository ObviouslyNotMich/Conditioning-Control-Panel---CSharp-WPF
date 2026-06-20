namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Lightweight file/folder picker abstraction used by mobile and desktop heads.
/// </summary>
public interface IFilePickerService
{
    Task<IReadOnlyList<string>> PickFilesAsync(string title, IReadOnlyList<FileFilter> filters, bool allowMultiple = false);
    Task<string?> PickFolderAsync(string title);
}
