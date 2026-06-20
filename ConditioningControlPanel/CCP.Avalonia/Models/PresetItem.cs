using ConditioningControlPanel.Core.Models;

namespace ConditioningControlPanel.Avalonia.Models;

/// <summary>
/// Lightweight runtime item for the header quick preset selector.
/// Wraps a <see cref="Preset"/> so selecting it can apply the source preset.
/// </summary>
public class PresetItem
{
    public string Id { get; }
    public string Name { get; }

    /// <summary>
    /// The underlying preset whose values are applied when this item is selected.
    /// </summary>
    public Preset Source { get; }

    public PresetItem(string id, string name, Preset source)
    {
        Id = id;
        Name = name;
        Source = source;
    }

    public static PresetItem FromPreset(Preset preset)
        => new(preset.Id, preset.Name, preset);

    public override string ToString() => Name;
}
