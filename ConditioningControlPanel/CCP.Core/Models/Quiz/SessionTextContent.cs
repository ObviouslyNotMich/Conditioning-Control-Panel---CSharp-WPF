namespace ConditioningControlPanel.Models.Quiz;

/// <summary>
/// Text content generated from a quiz result to inspire a session.
/// </summary>
public class SessionTextContent
{
    public string Title { get; set; } = string.Empty;

    /// <summary>Alias for <see cref="Title"/>, used by session-generation templates.</summary>
    public string Name
    {
        get => Title;
        set => Title = value;
    }

    public string Description { get; set; } = string.Empty;
    public string Phrase { get; set; } = string.Empty;

    public List<string> SubliminalPhrases { get; set; } = new();
    public List<string> BouncingTextPhrases { get; set; } = new();
    public List<string> LockCardPhrases { get; set; } = new();
}
