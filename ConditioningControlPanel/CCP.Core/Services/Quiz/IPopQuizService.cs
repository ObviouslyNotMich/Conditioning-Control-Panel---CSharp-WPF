namespace ConditioningControlPanel.Core.Services.Quiz;

/// <summary>
/// Cross-platform service that schedules and displays pop-quiz reinforcement questions.
/// </summary>
public interface IPopQuizService
{
    bool IsRunning { get; }

    /// <summary>Starts the pop-quiz scheduler if the user has the feature enabled.</summary>
    void Start();

    /// <summary>Stops the scheduler and closes any open pop-quiz window.</summary>
    void Stop();

    /// <summary>Shows a pop-quiz window immediately.</summary>
    /// <param name="isTest">When true, the quiz does not award XP or affect history.</param>
    void ShowPopQuiz(bool isTest = false);

    /// <summary>Shows a test pop-quiz window immediately.</summary>
    void TestPopQuiz();
}
