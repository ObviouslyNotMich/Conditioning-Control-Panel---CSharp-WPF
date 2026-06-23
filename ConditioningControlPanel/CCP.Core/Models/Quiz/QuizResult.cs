namespace ConditioningControlPanel.Models.Quiz;

/// <summary>
/// Result of a completed quiz.
/// </summary>
public class QuizResult
{
    public int TotalScore { get; set; }
    public int MaxScore { get; set; }
    public string ProfileText { get; set; } = string.Empty;
    public QuizCategory Category { get; set; }
}
