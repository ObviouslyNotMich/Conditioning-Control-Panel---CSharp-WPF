namespace ConditioningControlPanel.Avalonia.Models;

/// <summary>
/// Lightweight model for a single pop-quiz question used by the Avalonia
/// <see cref="Windows.PopQuizWindow"/> port.
/// </summary>
public class PopQuizQuestion
{
    public string QuestionText { get; }
    public string[] Answers { get; }
    public string[] Affirmations { get; }

    public PopQuizQuestion(string questionText, string[] answers, string[] affirmations)
    {
        QuestionText = questionText;
        Answers = answers;
        Affirmations = affirmations;
    }
}
