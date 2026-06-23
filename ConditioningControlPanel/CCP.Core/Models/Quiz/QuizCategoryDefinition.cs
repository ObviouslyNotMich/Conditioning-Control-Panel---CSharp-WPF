using System.Linq;
using System.Text.Json.Serialization;

namespace ConditioningControlPanel.Models.Quiz;

/// <summary>
/// Definition of a quiz category, including prompt template and archetype bands.
/// </summary>
public class QuizCategoryDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SystemPromptTemplate { get; set; } = string.Empty;
    public string Color { get; set; } = "#FF69B4";
    public bool IsBuiltIn { get; set; }
    public List<QuizArchetypeDefinition> Archetypes { get; set; } = new();

    /// <summary>Maps to QuizCategory enum for built-in categories, or null for custom.</summary>
    [JsonIgnore]
    public QuizCategory? EnumCategory { get; set; }

    public string GetArchetypeName(double percentage)
    {
        // Archetypes are sorted by MinPercentage ascending
        for (int i = Archetypes.Count - 1; i >= 0; i--)
        {
            if (percentage >= Archetypes[i].MinPercentage)
                return Archetypes[i].Name;
        }
        return Archetypes.Count > 0 ? Archetypes[0].Name : "Unknown";
    }

    public string GetFallbackProfile(int totalScore, int maxScore)
    {
        var pct = maxScore > 0 ? (double)totalScore / maxScore * 100 : 0;
        var archetype = GetArchetypeName(pct);
        var archetypeDef = Archetypes.FirstOrDefault(a => a.Name == archetype);
        var desc = archetypeDef?.Description ?? "Your answers reveal a unique personality.";
        return $"You are a {archetype}. {desc}";
    }
}
