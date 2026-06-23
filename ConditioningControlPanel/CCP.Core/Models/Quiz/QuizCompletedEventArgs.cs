namespace ConditioningControlPanel.Models.Quiz;

/// <summary>
/// Payload for the QuizCompleted event.
/// </summary>
public class QuizCompletedEventArgs : EventArgs
{
    public int Score { get; init; }
    public bool Passed { get; init; }
    public bool Perfect { get; init; }
    public string Category { get; init; } = string.Empty;
}
