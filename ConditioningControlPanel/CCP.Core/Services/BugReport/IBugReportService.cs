namespace ConditioningControlPanel.Core.Services.BugReport;

/// <summary>
/// Cross-platform bug-report service. Collects metadata, scrubs logs,
/// HMAC-signs the payload, and POSTs to the proxy /bug/upload endpoint.
/// </summary>
public interface IBugReportService
{
    BugReportDraft CreateDraft(string description, string steps, bool includeAppLog);
    string RenderPreview(BugReportDraft draft);
    Task<SubmitResult> SubmitAsync(BugReportDraft draft);
}

/// <summary>
/// Result of collecting a bug report ready for preview + submission.
/// </summary>
public sealed class BugReportDraft
{
    public BugMetadata Metadata { get; set; } = new();
    public string Description { get; set; } = string.Empty;
    public string Steps { get; set; } = string.Empty;
    public string ScrubbedCrashLog { get; set; } = string.Empty;
    public string ScrubbedAppLog { get; set; } = string.Empty;
    public bool IncludeAppLog { get; set; }
    public ScrubberCounts Counts { get; set; } = ScrubberCounts.Empty;
}

public sealed class BugMetadata
{
    public string AppVersion { get; set; } = string.Empty;
    public string Os { get; set; } = string.Empty;
    public string Dotnet { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string ActiveModId { get; set; } = string.Empty;
}

public enum SubmitOutcome
{
    Success,
    SavedPending,
    ValidationFailed,
    NetworkError
}

public sealed class SubmitResult
{
    public SubmitOutcome Outcome { get; set; }
    public string? Token { get; set; }
    public string? ErrorMessage { get; set; }

    public SubmitResult(SubmitOutcome outcome, string? token = null, string? errorMessage = null)
    {
        Outcome = outcome;
        Token = token;
        ErrorMessage = errorMessage;
    }
}
