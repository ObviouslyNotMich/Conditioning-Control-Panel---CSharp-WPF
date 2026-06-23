namespace ConditioningControlPanel.Models.Quiz;

/// <summary>
/// Historical record of a completed quiz run.
/// </summary>
public class QuizHistoryEntry
{
    public DateTime TakenAt { get; set; }
    public QuizCategory Category { get; set; }
    public int TotalScore { get; set; }
    public int MaxScore { get; set; }
    public string ProfileText { get; set; } = string.Empty;
    public List<QuizAnswerRecord> Answers { get; set; } = new();

    /// <summary>String category ID for custom categories. Falls back to Category enum name for built-in.</summary>
    public string CategoryId { get; set; } = string.Empty;

    /// <summary>Display name for the category (useful for custom categories where enum doesn't apply).</summary>
    public string CategoryName { get; set; } = string.Empty;
}
