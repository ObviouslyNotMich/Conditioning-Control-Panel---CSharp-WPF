using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.BouncingText;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.LockCard;
using ConditioningControlPanel.Core.Services.MindWipe;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.RemoteControl;
using ConditioningControlPanel.Core.Services.SessionLog;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Subliminal;
using ConditioningControlPanel.Core.Services.Video;

namespace ConditioningControlPanel.Avalonia.Services.RemoteControl;

/// <summary>
/// Avalonia implementation of <see cref="IRemoteStatusProvider"/>.
/// Reads live service state and current session progress for remote status pushes.
/// </summary>
public sealed class AvaloniaRemoteStatusProvider : IRemoteStatusProvider
{
    private readonly ISettingsService _settingsService;
    private readonly IFlashService? _flash;
    private readonly ISubliminalService? _subliminal;
    private readonly IOverlayService? _overlay;
    private readonly IBubbleService? _bubbles;
    private readonly IVideoService? _video;
    private readonly IMindWipeService? _mindWipe;
    private readonly IBouncingTextService? _bouncingText;
    private readonly ILockCardService? _lockCard;
    private readonly ISessionService? _session;
    private readonly ISessionManager? _sessionManager;
    private readonly ISessionLogService? _sessionLog;

    public AvaloniaRemoteStatusProvider(
        ISettingsService settingsService,
        IFlashService? flash = null,
        ISubliminalService? subliminal = null,
        IOverlayService? overlay = null,
        IBubbleService? bubbles = null,
        IVideoService? video = null,
        IMindWipeService? mindWipe = null,
        IBouncingTextService? bouncingText = null,
        ILockCardService? lockCard = null,
        ISessionService? session = null,
        ISessionManager? sessionManager = null,
        ISessionLogService? sessionLog = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _flash = flash;
        _subliminal = subliminal;
        _overlay = overlay;
        _bubbles = bubbles;
        _video = video;
        _mindWipe = mindWipe;
        _bouncingText = bouncingText;
        _lockCard = lockCard;
        _session = session;
        _sessionManager = sessionManager;
        _sessionLog = sessionLog;
    }

    public IReadOnlyList<string> GetActiveServices()
    {
        var services = new List<string>();
        try
        {
            var settings = _settingsService.Current;
            if (settings == null) return services;

            if (settings.PinkFilterEnabled) services.Add("pink_filter");
            if (settings.SpiralEnabled) services.Add("spiral");
            if (settings.StrictLockEnabled) services.Add("strict_lock");
            if (settings.PanicKeyEnabled == false) services.Add("no_panic");
            if (settings.AutonomyModeEnabled) services.Add("autonomy");
            if (_session?.State == SessionState.Running) services.Add("session");
            if (_flash?.IsRunning == true) services.Add("flash_loop");
            if (_video?.IsRunning == true) services.Add("video_loop");
            if (_subliminal?.IsRunning == true) services.Add("subliminal_loop");
            if (_lockCard?.IsRunning == true) services.Add("lock_card");
            if (_mindWipe?.IsRunning == true) services.Add("mind_wipe");
            if (_bouncingText?.IsRunning == true) services.Add("bounce_text");
        }
        catch { }

        return services;
    }

    public object? GetSessionProgress()
    {
        try
        {
            var session = _session?.CurrentSession;
            if (session == null || _session == null) return null;

            var currentPhase = "";
            if (_session.CurrentPhaseIndex >= 0 && _session.CurrentPhaseIndex < session.Phases.Count)
            {
                currentPhase = session.Phases[_session.CurrentPhaseIndex].Name;
            }

            return new SessionProgressInfo
            {
                Name = session.Name,
                Icon = session.Icon,
                ElapsedSeconds = (int)_session.ElapsedTime.TotalSeconds,
                TotalSeconds = session.DurationMinutes * 60,
                IsPaused = _session.State == SessionState.Paused,
                CurrentPhase = currentPhase
            };
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<object>? GetAvailableSessions()
    {
        try
        {
            var sessions = _sessionManager?.AllSessions;
            if (sessions == null || sessions.Count == 0) return null;

            return sessions
                .Select(s => new
                {
                    id = s.Id,
                    name = s.Name,
                    icon = s.Icon,
                    duration_minutes = s.DurationMinutes,
                    difficulty = s.Difficulty.ToString()
                })
                .Cast<object>()
                .ToList();
        }
        catch
        {
            return null;
        }
    }
}
