using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Models;

namespace ConditioningControlPanel.Avalonia.Windows;

// TODO: all quiz models and services below are temporary Avalonia stubs.
// The real WPF QuizService lives in ConditioningControlPanel.Services and must be
// extracted to CCP.Core before these stubs can be removed.

public enum QuizCategory
{
    Sissy,
    Bambi,
    Obedience,
    Mindlessness,
    Submission
}

public enum TrendDirection
{
    Up,
    Down,
    Flat,
    FirstQuiz
}

public class QuizArchetypeDefinition
{
    public string Name { get; set; } = "";
    public int MinPercentage { get; set; }
    public int MaxPercentage { get; set; }
    public string Description { get; set; } = "";
}

public class QuizCategoryDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string SystemPromptTemplate { get; set; } = "";
    public string Color { get; set; } = "#FF69B4";
    public bool IsBuiltIn { get; set; }
    public List<QuizArchetypeDefinition> Archetypes { get; set; } = new();
}

public class QuizQuestion
{
    public int Number { get; set; }
    public string QuestionText { get; set; } = "";
    public string[] Answers { get; set; } = Array.Empty<string>();
    public int[] Points { get; set; } = Array.Empty<int>();
}

public class QuizAnswerRecord
{
    public int QuestionNumber { get; set; }
    public string QuestionText { get; set; } = "";
    public string[] AllAnswers { get; set; } = Array.Empty<string>();
    public int[] AllPoints { get; set; } = Array.Empty<int>();
    public int ChosenIndex { get; set; }
    public int PointsEarned { get; set; }
}

public class QuizResult
{
    public QuizCategory Category { get; set; }
    public int TotalScore { get; set; }
    public int MaxScore { get; set; }
    public string ProfileText { get; set; } = "";
}

public class QuizHistoryEntry
{
    public DateTime TakenAt { get; set; }
    public QuizCategory Category { get; set; }
    public string CategoryId { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public int TotalScore { get; set; }
    public int MaxScore { get; set; }
    public string ProfileText { get; set; } = "";
    public List<QuizAnswerRecord> Answers { get; set; } = new();
}

public class SessionTextContent
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Phrase { get; set; } = "";
}

public class ScoreTrend
{
    public TrendDirection Direction { get; set; }
    public int LatestPercent { get; set; }
    public int DeltaPercent { get; set; }
    public int AveragePercent { get; set; }
    public int QuizCount { get; set; }
}

public static class QuizSessionGenerator
{
    public static SessionTextContent GetFallbackContent(string categoryId, double scorePercent)
    {
        return new SessionTextContent
        {
            Title = $"Generated {categoryId} Session",
            Description = "A custom session inspired by your quiz result.",
            Phrase = "Good girl"
        };
    }

    public static Session GenerateSession(int totalScore, int maxScore, string categoryId, string categoryName, SessionTextContent content)
    {
        return new Session
        {
            Id = $"quiz_{categoryId}_{Guid.NewGuid():N}",
            Name = content.Title,
            Description = content.Description,
            DurationMinutes = 30,
            Difficulty = SessionDifficulty.Easy,
            BonusXP = 400,
            IsAvailable = true,
            Settings = new SessionSettings()
        };
    }
}

public class QuizService : IDisposable
{
    public QuizCategoryDefinition? CurrentCategoryDefinition { get; private set; }
    public int QuestionNumber { get; private set; }

    public void Dispose() { }

    public static List<QuizCategoryDefinition> GetAllCategories() => GetBuiltInCategories();

    public static List<QuizCategoryDefinition> GetBuiltInCategories()
    {
        return new List<QuizCategoryDefinition>
        {
            new() { Id = "sissy", Name = "Sissy", Color = "#FF69B4", IsBuiltIn = true, Archetypes = DefaultArchetypes() },
            new() { Id = "bambi", Name = "Bambi", Color = "#9B59B6", IsBuiltIn = true, Archetypes = DefaultArchetypes() },
            new() { Id = "obedience", Name = "Obedience", Color = "#E67E22", IsBuiltIn = true, Archetypes = DefaultArchetypes() },
            new() { Id = "mindlessness", Name = "Mindlessness", Color = "#3498DB", IsBuiltIn = true, Archetypes = DefaultArchetypes() },
            new() { Id = "submission", Name = "Submission", Color = "#E74C3C", IsBuiltIn = true, Archetypes = DefaultArchetypes() },
        };
    }

    public static QuizCategoryDefinition? FindCategory(string id)
        => GetBuiltInCategories().FirstOrDefault(c => c.Id == id);

    public static void SaveCustomCategory(QuizCategoryDefinition category) { }
    public static void DeleteCustomCategory(string id) { }
    public static void SaveEntry(QuizHistoryEntry entry) { }

    public static List<QuizHistoryEntry> LoadHistory() => new();

    public static ScoreTrend? GetScoreTrend(List<QuizHistoryEntry> history, QuizCategory category) => null;

    public static void RaiseQuizCompleted(int score, bool passed, bool perfect, string categoryId) { }

    public void Reset()
    {
        CurrentCategoryDefinition = null;
        QuestionNumber = 0;
    }

    public Task<QuizQuestion?> StartQuizAsync(QuizCategoryDefinition definition)
    {
        CurrentCategoryDefinition = definition;
        QuestionNumber = 1;
        return Task.FromResult<QuizQuestion?>(null);
    }

    public Task<QuizQuestion?> StartQuizAsync(QuizCategory category)
    {
        CurrentCategoryDefinition = GetBuiltInCategories().FirstOrDefault(c => c.Id == category.ToString().ToLowerInvariant());
        QuestionNumber = 1;
        return Task.FromResult<QuizQuestion?>(null);
    }

    public Task<QuizQuestion?> SubmitAnswerAndGetNextAsync(int answerIndex, int points)
    {
        QuestionNumber++;
        return Task.FromResult<QuizQuestion?>(null);
    }

    public Task<QuizResult?> SubmitFinalAnswerAndGetResultAsync(int answerIndex, int points)
    {
        return Task.FromResult<QuizResult?>(new QuizResult
        {
            Category = CurrentCategoryDefinition != null ? Enum.TryParse<QuizCategory>(CurrentCategoryDefinition.Id, true, out var c) ? c : QuizCategory.Sissy : QuizCategory.Sissy,
            TotalScore = 0,
            MaxScore = 40,
            ProfileText = "You are a curious beginner."
        });
    }

    public Task<SessionTextContent?> GenerateSessionContentAsync()
        => Task.FromResult<SessionTextContent?>(null);

    private static List<QuizArchetypeDefinition> DefaultArchetypes()
    {
        return new List<QuizArchetypeDefinition>
        {
            new() { Name = "Tier 1 (Low)", MinPercentage = 0, MaxPercentage = 25 },
            new() { Name = "Tier 2", MinPercentage = 26, MaxPercentage = 50 },
            new() { Name = "Tier 3 (Mid)", MinPercentage = 51, MaxPercentage = 70 },
            new() { Name = "Tier 4", MinPercentage = 71, MaxPercentage = 85 },
            new() { Name = "Tier 5 (Max)", MinPercentage = 86, MaxPercentage = 100 },
        };
    }
}
