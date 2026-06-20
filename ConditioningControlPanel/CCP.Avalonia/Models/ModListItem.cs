using Avalonia.Media;
using ConditioningControlPanel.Core.Models;

namespace ConditioningControlPanel.Avalonia.Models;

/// <summary>
/// Lightweight runtime item for the header mod selector drop-down.
/// </summary>
public class ModListItem
{
    private static readonly IBrush DefaultAccentBrush = Brush.Parse("#FF69B4");

    public string Id { get; }
    public string Name { get; }
    public IBrush AccentBrush { get; }

    public ModListItem(string id, string name, string? accentColor)
    {
        Id = id;
        Name = name;
        AccentBrush = ParseAccentBrush(accentColor);
    }

    public static ModListItem FromManifest(ModManifest manifest)
        => new(manifest.Id, manifest.Name, manifest.Theme?.AccentColor);

    private static IBrush ParseAccentBrush(string? accentColor)
    {
        if (string.IsNullOrWhiteSpace(accentColor))
            return DefaultAccentBrush;

        try
        {
            return Brush.Parse(accentColor);
        }
        catch
        {
            return DefaultAccentBrush;
        }
    }
}
