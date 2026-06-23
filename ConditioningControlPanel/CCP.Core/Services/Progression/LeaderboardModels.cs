using Newtonsoft.Json;

namespace ConditioningControlPanel.Core.Services.Progression;

/// <summary>
/// Represents a single entry on the leaderboard.
/// </summary>
public class LeaderboardEntry
{
    [JsonProperty("rank")]
    public int Rank { get; set; }

    [JsonProperty("unified_id")]
    public string? UnifiedId { get; set; }

    [JsonProperty("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonProperty("level")]
    public int Level { get; set; }

    [JsonProperty("xp")]
    public int Xp { get; set; }

    /// <summary>
    /// Formatted XP display (e.g., "100.3k" or "1.2M").
    /// </summary>
    public string XpDisplay
    {
        get
        {
            if (Xp >= 1_000_000)
                return $"{Xp / 1_000_000.0:F1}M";
            if (Xp >= 1_000)
                return $"{Xp / 1_000.0:F1}k";
            return Xp.ToString();
        }
    }

    [JsonProperty("total_bubbles_popped")]
    public int BubblesPopped { get; set; }

    /// <summary>
    /// Formatted bubbles display (e.g., "100.3k" or "1.2M").
    /// </summary>
    public string BubblesPoppedDisplay => FormatLargeNumber(BubblesPopped);

    [JsonProperty("total_flashes")]
    public int GifsSpawned { get; set; }

    /// <summary>
    /// Formatted GIFs display (e.g., "100.3k" or "1.2M").
    /// </summary>
    public string GifsSpawnedDisplay => FormatLargeNumber(GifsSpawned);

    private static string FormatLargeNumber(int value)
    {
        if (value >= 1_000_000)
            return $"{value / 1_000_000.0:F1}M";
        if (value >= 1_000)
            return $"{value / 1_000.0:F1}k";
        return value.ToString();
    }

    [JsonProperty("total_video_minutes")]
    public double VideoMinutes { get; set; }

    [JsonProperty("total_lock_cards_completed")]
    public int LockCardsCompleted { get; set; }

    [JsonProperty("achievements_count")]
    public int AchievementsCount { get; set; }

    [JsonProperty("has_trophy_case")]
    public bool HasTrophyCase { get; set; }

    [JsonProperty("longest_session_minutes")]
    public double LongestSessionMinutes { get; set; }

    /// <summary>
    /// Formatted longest session display — blank if user doesn't have trophy_case skill.
    /// </summary>
    public string LongestSessionDisplay => HasTrophyCase ? $"{LongestSessionMinutes:F1}" : "";

    [JsonProperty("highest_streak")]
    public int HighestStreak { get; set; }

    /// <summary>
    /// Formatted highest streak display — blank if user doesn't have trophy_case skill.
    /// </summary>
    public string HighestStreakDisplay => HasTrophyCase ? HighestStreak.ToString() : "";

    [JsonProperty("seasons_completed")]
    public int SeasonsCompleted { get; set; }

    [JsonProperty("total_xp_earned")]
    public long TotalXpEarned { get; set; }

    /// <summary>
    /// Formatted total XP earned display (e.g., "100.3k" or "1.2M").
    /// </summary>
    public string TotalXpEarnedDisplay => FormatLargeNumber((int)Math.Min(TotalXpEarned, int.MaxValue));

    [JsonProperty("highest_level_ever")]
    public int HighestLevelEver { get; set; }

    [JsonProperty("is_online", NullValueHandling = NullValueHandling.Ignore)]
    public bool IsOnline { get; set; }

    [JsonProperty("is_patreon", NullValueHandling = NullValueHandling.Ignore)]
    public bool IsPatreon { get; set; }

    [JsonProperty("patreon_tier")]
    public int PatreonTier { get; set; }

    [JsonProperty("discord_id")]
    public string? DiscordId { get; set; }

    /// <summary>
    /// Whether this user has a Discord ID available for DM.
    /// </summary>
    public bool HasDiscord => !string.IsNullOrEmpty(DiscordId);

    [JsonProperty("is_season0_og")]
    public bool IsSeason0Og { get; set; }

    /// <summary>
    /// Display name with OG star prefix if applicable.
    /// </summary>
    public string DisplayNameWithFlair => DisplayName;

    /// <summary>
    /// Display string for achievements (X / Y format).
    /// Uses the total earnable achievement count from the Achievement model.
    /// </summary>
    public string AchievementsDisplay => $"{AchievementsCount} / {System.Linq.Enumerable.Count(Models.Achievement.All.Values, a => !a.IsHidden)}";
}

/// <summary>
/// Result of looking up a specific user's profile.
/// </summary>
public class UserLookupResult
{
    [JsonProperty("display_name")]
    public string? DisplayName { get; set; }

    [JsonProperty("level")]
    public int Level { get; set; }

    [JsonProperty("xp")]
    public int Xp { get; set; }

    [JsonProperty("total_bubbles_popped")]
    public int BubblesPopped { get; set; }

    [JsonProperty("total_flashes")]
    public int GifsSpawned { get; set; }

    [JsonProperty("total_video_minutes")]
    public double VideoMinutes { get; set; }

    [JsonProperty("total_lock_cards_completed")]
    public int LockCardsCompleted { get; set; }

    [JsonProperty("achievements_count")]
    public int AchievementsCount { get; set; }

    [JsonProperty("achievements")]
    public List<string>? Achievements { get; set; }

    [JsonProperty("is_online")]
    public bool IsOnline { get; set; }

    [JsonProperty("is_patreon")]
    public bool IsPatreon { get; set; }

    [JsonProperty("patreon_tier")]
    public int PatreonTier { get; set; }

    [JsonProperty("discord_id")]
    public string? DiscordId { get; set; }

    [JsonProperty("avatar_url")]
    public string? AvatarUrl { get; set; }

    [JsonProperty("last_seen")]
    public string? LastSeen { get; set; }

    [JsonProperty("is_season0_og")]
    public bool IsSeason0Og { get; set; }

    /// <summary>
    /// Display name with OG star prefix if applicable.
    /// </summary>
    public string DisplayNameWithFlair => DisplayName ?? "";
}
