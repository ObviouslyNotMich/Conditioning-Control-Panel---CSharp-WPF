using Newtonsoft.Json;

namespace ConditioningControlPanel.Core.Services.Auth;

/// <summary>
/// Response from a v2 authentication endpoint (Discord, Patreon, SubscribeStar, register, login).
/// </summary>
public class V2AuthResponse
{
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("is_new_user")]
    public bool IsNewUser { get; set; }

    [JsonProperty("needs_registration")]
    public bool NeedsRegistration { get; set; }

    [JsonProperty("is_legacy_user")]
    public bool IsLegacyUser { get; set; }

    [JsonProperty("unified_id")]
    public string? UnifiedId { get; set; }

    [JsonProperty("legacy_data")]
    public LegacyData? LegacyData { get; set; }

    [JsonProperty("user")]
    public V2User? User { get; set; }

    [JsonProperty("discord")]
    public DiscordInfo? Discord { get; set; }

    [JsonProperty("patreon")]
    public PatreonInfo? Patreon { get; set; }

    [JsonProperty("error")]
    public string? Error { get; set; }

    [JsonProperty("auth_token")]
    public string? AuthToken { get; set; }
}

/// <summary>
/// Legacy account data returned for Season 0 OG users.
/// </summary>
public class LegacyData
{
    [JsonProperty("display_name")]
    public string? DisplayName { get; set; }

    [JsonProperty("highest_level_ever")]
    public int HighestLevelEver { get; set; }

    [JsonProperty("achievements_count")]
    public int AchievementsCount { get; set; }

    [JsonProperty("unlocks")]
    public Unlocks? Unlocks { get; set; }
}

/// <summary>
/// v2 user profile returned by the proxy API.
/// </summary>
public class V2User
{
    [JsonProperty("unified_id")]
    public string? UnifiedId { get; set; }

    [JsonProperty("display_name")]
    public string? DisplayName { get; set; }

    [JsonProperty("discord_id")]
    public string? DiscordId { get; set; }

    [JsonProperty("patreon_id")]
    public string? PatreonId { get; set; }

    [JsonProperty("level")]
    public int Level { get; set; }

    [JsonProperty("xp")]
    public int Xp { get; set; }

    [JsonProperty("current_season")]
    public string? CurrentSeason { get; set; }

    [JsonProperty("highest_level_ever")]
    public int HighestLevelEver { get; set; }

    [JsonProperty("unlocks")]
    public Unlocks? Unlocks { get; set; }

    [JsonProperty("achievements")]
    public string[]? Achievements { get; set; }

    [JsonProperty("is_season0_og")]
    public bool IsSeason0Og { get; set; }

    [JsonProperty("patreon_tier")]
    public int PatreonTier { get; set; }

    [JsonProperty("patreon_is_active")]
    public bool PatreonIsActive { get; set; }

    [JsonProperty("patreon_is_whitelisted")]
    public bool PatreonIsWhitelisted { get; set; }
}

/// <summary>
/// Feature unlock flags returned by the v2 API.
/// </summary>
public class Unlocks
{
    [JsonProperty("avatars")]
    public bool Avatars { get; set; }

    [JsonProperty("autonomy_mode")]
    public bool AutonomyMode { get; set; }

    [JsonProperty("takeover_mode")]
    public bool TakeoverMode { get; set; }

    [JsonProperty("ai_companion")]
    public bool AiCompanion { get; set; }
}

/// <summary>
/// Discord profile data attached to a v2 account.
/// </summary>
public class DiscordInfo
{
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("username")]
    public string? Username { get; set; }

    [JsonProperty("global_name")]
    public string? GlobalName { get; set; }

    [JsonProperty("avatar")]
    public string? Avatar { get; set; }
}

/// <summary>
/// Patreon profile data attached to a v2 account.
/// </summary>
public class PatreonInfo
{
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("tier")]
    public int Tier { get; set; }

    [JsonProperty("is_active")]
    public bool IsActive { get; set; }
}

/// <summary>
/// Response from linking a second OAuth provider to an existing unified account.
/// </summary>
public class LinkResponse
{
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("unified_id")]
    public string? UnifiedId { get; set; }

    [JsonProperty("linked_provider")]
    public string? LinkedProvider { get; set; }

    [JsonProperty("error")]
    public string? Error { get; set; }

    [JsonProperty("auth_token")]
    public string? AuthToken { get; set; }
}

/// <summary>
/// Status of a device-code poll request.
/// </summary>
public enum PollStatus
{
    Pending,
    Confirmed,
    Expired,
    NotFound,
    RateLimited,
    ServiceUnavailable,
    BadRequest,
    Unauthorized,
    Unknown
}

/// <summary>
/// Response from initiating a device-code login session.
/// </summary>
public class InitiateResponse
{
    public bool Success { get; set; }
    public string? Code { get; set; }
    public string? SessionId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Response from polling a device-code login session.
/// </summary>
public class PollResponse
{
    public PollStatus Status { get; set; }
    public string? AuthToken { get; set; }
    public string? UnifiedId { get; set; }
    public bool IsNewUser { get; set; }

    /// <summary>
    /// Server-provided user object on confirmed responses. Null on older proxy builds
    /// or if the server's user-fetch failed.
    /// </summary>
    public V2User? User { get; set; }
}
