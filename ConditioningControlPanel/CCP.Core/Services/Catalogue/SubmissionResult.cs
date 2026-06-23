namespace ConditioningControlPanel.Core.Services.Catalogue;

/// <summary>
/// Discriminated union for catalogue submission outcomes. Pattern-match on the
/// concrete subtype in UI code.
/// </summary>
public abstract record SubmissionResult
{
    public sealed record Success(string Id, string Status) : SubmissionResult;
    public sealed record Duplicate(string ExistingId, string ExistingStatus) : SubmissionResult;
    public sealed record ValidationError(string ErrorCode) : SubmissionResult;
    public sealed record AuthFailed : SubmissionResult;
    public sealed record TooLarge : SubmissionResult;
    public sealed record RateLimited(int? RetryAfterSeconds) : SubmissionResult;
    public sealed record UnknownError(int StatusCode, string Body) : SubmissionResult;
}
