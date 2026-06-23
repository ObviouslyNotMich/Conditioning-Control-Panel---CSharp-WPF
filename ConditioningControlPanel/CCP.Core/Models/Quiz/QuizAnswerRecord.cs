namespace ConditioningControlPanel.Models.Quiz;

/// <summary>
/// Record of a single answer chosen during a quiz.
/// </summary>
public class QuizAnswerRecord
{
    public int QuestionNumber { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public string[] AllAnswers { get; set; } = new string[4];
    public int[] AllPoints { get; set; } = new int[4];
    public int ChosenIndex { get; set; }
    public int PointsEarned { get; set; }
}
