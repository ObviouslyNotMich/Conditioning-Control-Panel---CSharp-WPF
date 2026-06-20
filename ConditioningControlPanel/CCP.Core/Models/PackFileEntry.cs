namespace ConditioningControlPanel.Core.Models;

/// <summary>
/// Entry for a file in an installed content pack.
/// Mirrored from the legacy WPF Services.Content namespace for Core portability.
/// </summary>
public class PackFileEntry
{
    public string OriginalName { get; set; } = "";
    public string ObfuscatedName { get; set; } = "";
    public string FileType { get; set; } = ""; // "image" or "video"
    public string Extension { get; set; } = "";
}
