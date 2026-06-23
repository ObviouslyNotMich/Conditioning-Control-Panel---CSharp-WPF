namespace ConditioningControlPanel.Models.Quiz;

/// <summary>
/// An archetype band within a quiz category (e.g. score percentile tiers).
/// </summary>
public class QuizArchetypeDefinition
{
    public string Name { get; set; } = string.Empty;
    public int MinPercentage { get; set; }
    public int MaxPercentage { get; set; }
    public string Description { get; set; } = string.Empty;
}
