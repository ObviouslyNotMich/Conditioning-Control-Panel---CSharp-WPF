namespace ConditioningControlPanel.Core.Services.Progression;

/// <summary>
/// Fetches and caches leaderboard data from the server.
/// </summary>
public interface ILeaderboardService
{
    /// <summary>Current leaderboard entries.</summary>
    IReadOnlyList<LeaderboardEntry> Entries { get; }

    /// <summary>Total number of users on the leaderboard.</summary>
    int TotalUsers { get; }

    /// <summary>Number of users currently online (active in last minute).</summary>
    int OnlineUsers { get; }

    /// <summary>Server-provided rank for the current player (1-indexed), or null if not available.</summary>
    int? YourRank { get; }

    /// <summary>Total number of season leaderboard members (for percentile calculation).</summary>
    int? YourTotal { get; }

    /// <summary>Current sort field.</summary>
    string CurrentSortBy { get; }

    /// <summary>Current leaderboard mode (monthly or all-time).</summary>
    string CurrentMode { get; }

    /// <summary>Last successful refresh time.</summary>
    DateTime? LastRefreshTime { get; }

    /// <summary>Last refresh error message (if any).</summary>
    string? LastRefreshError { get; }

    /// <summary>Whether a refresh is currently in progress.</summary>
    bool IsRefreshing { get; }

    /// <summary>Fired when leaderboard data is updated.</summary>
    event EventHandler? LeaderboardUpdated;

    /// <summary>
    /// Refresh leaderboard data from the server.
    /// </summary>
    /// <param name="sortBy">Field to sort by.</param>
    /// <param name="mode">Leaderboard mode: "monthly" (default) or "all-time".</param>
    /// <returns>True if successful.</returns>
    Task<bool> RefreshAsync(string? sortBy = null, string? mode = null);

    /// <summary>
    /// Look up a specific user's fresh profile data by display name.
    /// </summary>
    Task<UserLookupResult?> LookupUserAsync(string displayName);

    /// <summary>
    /// Get the current player's rank percentile (1-99), or 0 if not found.
    /// </summary>
    int GetPlayerPercentile();
}
