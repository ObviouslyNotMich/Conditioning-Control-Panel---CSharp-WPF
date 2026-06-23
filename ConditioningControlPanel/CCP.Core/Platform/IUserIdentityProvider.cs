namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Provides the current user's identity values without depending on a specific
/// auth stack. These values are used by cross-platform services that need to
/// identify the player to the server (leaderboard, catalogue, remote control).
/// </summary>
public interface IUserIdentityProvider
{
    /// <summary>The backend unified user ID, if known.</summary>
    string? UnifiedUserId { get; }

    /// <summary>The user's chosen display name, if known.</summary>
    string? DisplayName { get; }

    /// <summary>The Discord user ID, if linked.</summary>
    string? DiscordId { get; }
}
