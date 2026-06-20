namespace ConditioningControlPanel.Avalonia.Models;

/// <summary>
/// Lightweight runtime item for the header language selector drop-down.
/// </summary>
public class LanguageItem
{
    public string Code { get; }
    public string DisplayName { get; }
    public string ShortName { get; }

    public LanguageItem(string code, string displayName, string shortName)
    {
        Code = code;
        DisplayName = displayName;
        ShortName = shortName;
    }
}
