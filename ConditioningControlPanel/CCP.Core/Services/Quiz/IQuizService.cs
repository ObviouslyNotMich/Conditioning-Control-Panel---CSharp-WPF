namespace ConditioningControlPanel.Core.Services.Quiz;

/// <summary>
/// Cross-platform service for running personality quizzes, managing categories,
/// persisting history, and computing score trends.
/// </summary>
public interface IQuizService : IDisposable
{
    /// <summary>Raised when a quiz run finalizes.</summary>
    event EventHandler<Models.Quiz.QuizCompletedEventArgs>? QuizCompleted;

    Models.Quiz.QuizCategoryDefinition? CurrentCategoryDefinition { get; }
    int QuestionNumber { get; }
    int TotalScore { get; }
    int MaxPossibleScore { get; }
    bool IsActive { get; }

    void Reset();

    Task<Models.Quiz.QuizQuestion?> StartQuizAsync(Models.Quiz.QuizCategoryDefinition categoryDef);
    Task<Models.Quiz.QuizQuestion?> StartQuizAsync(Models.Quiz.QuizCategory category);
    Task<Models.Quiz.QuizQuestion?> SubmitAnswerAndGetNextAsync(int answerIndex, int points);
    Task<Models.Quiz.QuizResult?> SubmitFinalAnswerAndGetResultAsync(int answerIndex, int points);
    Task<Models.Quiz.SessionTextContent?> GenerateSessionContentAsync();

    List<Models.Quiz.QuizCategoryDefinition> GetAllCategories();
    List<Models.Quiz.QuizCategoryDefinition> GetBuiltInCategories();
    Models.Quiz.QuizCategoryDefinition? FindCategory(string id);
    void SaveCustomCategory(Models.Quiz.QuizCategoryDefinition category);
    void DeleteCustomCategory(string id);
    void SaveEntry(Models.Quiz.QuizHistoryEntry entry);
    List<Models.Quiz.QuizHistoryEntry> LoadHistory();
    Models.Quiz.QuizScoreTrend? GetScoreTrend(List<Models.Quiz.QuizHistoryEntry> history, Models.Quiz.QuizCategory category);
    void RaiseQuizCompleted(int score, bool passed, bool perfect, string categoryId);
}
