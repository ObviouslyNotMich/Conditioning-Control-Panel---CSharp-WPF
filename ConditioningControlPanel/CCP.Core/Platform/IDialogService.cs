namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Cross-platform dialog service.
/// </summary>
public interface IDialogService
{
    Task ShowMessageAsync(string title, string message, DialogSeverity severity = DialogSeverity.Info);
    Task<bool> ShowConfirmationAsync(string title, string message);
    Task<IReadOnlyList<string>> ShowOpenFileDialogAsync(string title, IReadOnlyList<FileFilter> filters, bool allowMultiple = false, string? initialDirectory = null);
    Task<string?> ShowSaveFileDialogAsync(string title, IReadOnlyList<FileFilter> filters, string? defaultFileName = null);
    Task<string?> ShowOpenFolderDialogAsync(string title);
}

public enum DialogSeverity { Info, Warning, Error }
public sealed record FileFilter(string Name, IReadOnlyList<string> Extensions);
