namespace ConditioningControlPanel.Core.Services.BugReport;

/// <summary>
/// Per-category counts from a scrubber pass. Shown in the bug-report preview
/// so the user can see at a glance how many things were redacted.
/// </summary>
public sealed record ScrubberCounts(int Paths, int Emails, int Tokens, int AppData)
{
    public static ScrubberCounts Empty { get; } = new(0, 0, 0, 0);

    public ScrubberCounts Add(ScrubberCounts other) =>
        new(Paths + other.Paths, Emails + other.Emails, Tokens + other.Tokens, AppData + other.AppData);
}
