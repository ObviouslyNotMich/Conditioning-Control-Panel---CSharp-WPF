using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ConditioningControlPanel.Avalonia.Services.Video;
using ConditioningControlPanel.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Services;

/// <summary>Stub Discord auth provider for the Avalonia head.</summary>
public sealed class AvaloniaDiscordProvider : IAuthProvider
{
    public string ProviderName => "discord";
    public bool IsLoggedIn => !string.IsNullOrEmpty(UnifiedUserId);
    public bool HasPremiumAccess => false;
    public string? UnifiedUserId { get; set; }
    public string? DisplayName { get; set; }

    public Task StartOAuthFlowAsync() => Task.CompletedTask;
    public string? GetAccessToken() => null;
    public void Logout() => UnifiedUserId = null;
}

/// <summary>Stub Patreon auth provider for the Avalonia head.</summary>
public sealed class AvaloniaPatreonProvider : IAuthProvider
{
    public string ProviderName => "patreon";
    public bool IsLoggedIn => !string.IsNullOrEmpty(UnifiedUserId);
    public bool HasPremiumAccess => false;
    public string? UnifiedUserId { get; set; }
    public string? DisplayName { get; set; }

    public Task StartOAuthFlowAsync() => Task.CompletedTask;
    public string? GetAccessToken() => null;
    public void Logout() => UnifiedUserId = null;
}

/// <summary>Stub SubscribeStar auth provider for the Avalonia head.</summary>
public sealed class AvaloniaSubscribeStarProvider : IAuthProvider
{
    public string ProviderName => "substar";
    public bool IsLoggedIn => !string.IsNullOrEmpty(UnifiedUserId);
    public bool HasPremiumAccess => false;
    public string? UnifiedUserId { get; set; }
    public string? DisplayName { get; set; }

    public Task StartOAuthFlowAsync() => Task.CompletedTask;
    public string? GetAccessToken() => null;
    public void Logout() => UnifiedUserId = null;
}

/// <summary>In-memory unified user ID store for the Avalonia head.</summary>
public sealed class AvaloniaUnifiedUserService : IUnifiedUserService
{
    public string? UnifiedUserId { get; set; }
}

/// <summary>Stub Chaos engine service for the Avalonia head.</summary>
public sealed class AvaloniaChaosService : IChaosService
{
    public bool IsRunning => false;
    public bool IsManuallyPaused => false;

    public void ShowLoadoutSidebar() { }
    public void CloseLoadoutSidebar() { }
    public void NotifyLoadoutChanged() { }
    public void StartRun(object cfg) { }
    public void StartRunFromSidebar() { }
    public void ToggleManualPause() { }
    public void RequestStop() { }
    public void CloseWarrenPhase() { }
    public void OpenWarrenAt(string tag) { }
    public void UnequipFromSidebar(string id) { }
    public void UseToyById(string id) { }
}

/// <summary>Avalonia avatar-window service. Lazily creates the avatar tube window
/// and exposes chat/hotkey integration.</summary>
public sealed class AvaloniaAvatarWindowService : IAvatarWindowService
{
    private readonly global::ConditioningControlPanel.IAppLogger? _logger;
    private readonly Window? _parentWindow;
    private AvatarTube.AvatarTubeWindow? _window;
    private bool _isMuted;
    private bool _chaosRunActive;

    public AvaloniaAvatarWindowService()
    {
        _logger = global::ConditioningControlPanel.Avalonia.App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>();

        if (global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            _parentWindow = desktop.MainWindow;
        }
    }

    public bool IsMuted => _isMuted;

    public void SetMuteAvatar(bool muted)
    {
        _isMuted = muted;
        if (_window != null)
        {
            _window.SetMuted(muted);
        }
    }

    public void SetChaosRunActive(bool active)
    {
        _chaosRunActive = active;
        if (_window != null)
        {
            _window.SetChaosRunActive(active);
        }
    }

    public void OpenChatWindow()
    {
        try
        {
            if (_window == null)
            {
                _window = new AvatarTube.AvatarTubeWindow(_parentWindow);
                _window.Closed += (_, _) => _window = null;
                _window.SetMuted(_isMuted);
                _window.SetChaosRunActive(_chaosRunActive);
            }

            _window.ShowTube();
            _window.OpenChatInput();
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to open avatar chat window");
        }
    }
}

/// <summary>Stub bark/notification service for the Avalonia head.</summary>
public sealed class AvaloniaBarkService : IBarkService
{
    public void NotifyChaosDollhouseFirstOpen() { }
    public void NotifyChaosRevealFlash(string id) { }
    public void NotifyChaosResultsShown(double score, double best, double delta, bool pb,
                                        int defused, int detonated, int bestCombo, string difficulty) { }
    public void NotifyChaosRankUp(string rankName) { }
    public void NotifyChaosGiftGiven() { }
    public void NotifyChaosDraftAutopick() { }
}

/// <summary>Video state for the Avalonia head, backed by the dual-monitor video service.</summary>
public sealed class AvaloniaVideoInfo : IVideoInfo
{
    private readonly AvaloniaDualMonitorVideoService? _videoService;

    public AvaloniaVideoInfo(AvaloniaDualMonitorVideoService? videoService = null)
    {
        _videoService = videoService;
    }

    public bool IsPlaying => _videoService?.IsPlaying ?? false;
}

/// <summary>Exposes the Avalonia desktop main window without coupling Core to Avalonia.</summary>
public sealed class AvaloniaMainWindowService : IMainWindowService
{
    public object? MainWindow =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
}

/// <summary>Stub session-log service for the Avalonia head.</summary>
public sealed class AvaloniaSessionLogService : ISessionLogService
{
    public IReadOnlyList<SessionLog> LoadRecentLogs()
    {
        return new List<SessionLog>
        {
            new()
            {
                SessionName = "Morning Drift",
                SessionIcon = "🌅",
                StartedAt = DateTime.Now.AddDays(-1),
                Duration = TimeSpan.FromMinutes(30),
                Completed = true,
                XPEarned = 400,
                Media = new List<MediaLogEntry>()
            },
            new()
            {
                SessionName = "Gamer Girl",
                SessionIcon = "🎮",
                StartedAt = DateTime.Now.AddDays(-2),
                Duration = TimeSpan.FromMinutes(45),
                Completed = false,
                XPEarned = 0,
                Media = new List<MediaLogEntry>()
            }
        };
    }
}
