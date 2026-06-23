using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Core.Services.Catalogue;

/// <summary>
/// Client for the CCP Labs catalogue API: token exchange, submission, and
/// polling for accepted/published status updates.
/// </summary>
public interface ICatalogueService
{
    /// <summary>
    /// Fired when an enhancement submission succeeds (HTTP 201). Consumers may
    /// use this to trigger gamification/achievement tracking.
    /// </summary>
    event EventHandler<SubmissionResult.Success>? SubmissionSucceeded;

    /// <summary>
    /// Submit a .ccpenh.json bundle to the catalogue.
    /// </summary>
    Task<SubmissionResult> SubmitEnhancementAsync(string ccpenhJsonPath, CancellationToken ct = default);

    /// <summary>
    /// Submit a generalized catalogue asset (preset or session) to the catalogue.
    /// </summary>
    /// <param name="kind">Route segment ("presets" | "sessions").</param>
    /// <param name="asset">The pristine native asset object.</param>
    /// <param name="schemaTag">Bundle schema tag.</param>
    /// <param name="creator">Creator display name.</param>
    /// <param name="tags">Optional tags.</param>
    Task<SubmissionResult> SubmitCatalogueAssetAsync(
        string kind,
        JToken asset,
        string schemaTag,
        string creator,
        IReadOnlyList<string> tags,
        CancellationToken ct = default);

    /// <summary>
    /// Fetch the authenticated user's submissions for a catalogue asset kind
    /// ("presets" | "sessions").
    /// </summary>
    /// <returns>Map of submission id → status, or null when unavailable.</returns>
    Task<Dictionary<string, string>?> FetchMyCatalogueAssetsAsync(string kind, CancellationToken ct = default);

    /// <summary>
    /// Fetch the authenticated user's Deeper enhancement submissions and their
    /// current status.
    /// </summary>
    /// <returns>Map of submission id → status, or null when unavailable.</returns>
    Task<Dictionary<string, string>?> FetchMySubmissionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Wipes the in-memory token cache so the next call re-exchanges.
    /// </summary>
    void InvalidateCachedToken();
}
