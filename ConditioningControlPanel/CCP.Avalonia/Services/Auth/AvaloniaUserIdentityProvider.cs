using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.Services.Auth;

/// <summary>
/// Avalonia identity provider that surfaces the logged-in user's unified ID,
/// display name, and Discord ID from the active auth provider and settings.
/// </summary>
public sealed class AvaloniaUserIdentityProvider : IUserIdentityProvider
{
    private readonly IAuthProvider _authProvider;
    private readonly ISettingsService _settingsService;

    public AvaloniaUserIdentityProvider(IAuthProvider authProvider, ISettingsService settingsService)
    {
        _authProvider = authProvider;
        _settingsService = settingsService;
    }

    public string? UnifiedUserId =>
        !string.IsNullOrEmpty(_authProvider.UnifiedUserId)
            ? _authProvider.UnifiedUserId
            : _settingsService.Current?.UnifiedId;

    public string? DisplayName =>
        !string.IsNullOrEmpty(_authProvider.DisplayName)
            ? _authProvider.DisplayName
            : _settingsService.Current?.UserDisplayName;

    // Avalonia auth providers do not currently track a separate Discord ID;
    // the leaderboard percentile fallback will match on unified ID or display name.
    public string? DiscordId => null;
}
