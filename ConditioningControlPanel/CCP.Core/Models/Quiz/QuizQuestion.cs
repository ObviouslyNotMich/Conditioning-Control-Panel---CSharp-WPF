namespace ConditioningControlPanel.Models.Quiz;

/// <summary>
/// A single question in a quiz.
/// </summary>
public class QuizQuestion
{
    public int Number { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public string[] Answers { get; set; } = new string[4];
    public int[] Points { get; set; } = new int[4];
}
