using System;
using System.Linq;
using ConditioningControlPanel.Avalonia.Services.BubbleCount;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.BouncingText;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.LockCard;
using ConditioningControlPanel.Core.Services.MindWipe;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.SessionLog;
using ConditioningControlPanel.Core.Services.Subliminal;
using ConditioningControlPanel.Core.Services.Video;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Services.Sessions;

/// <summary>
/// Avalonia implementation of <see cref="ISessionEffectOrchestrator"/>.
/// Starts/stops feature services according to the current session settings.
/// Individual services are stubs while their full engines are being ported from WPF.
/// </summary>
public sealed class AvaloniaSessionEffectOrchestrator : ISessionEffectOrchestrator
{
    private readonly ISettingsService _settings;
    private readonly IFlashService? _flash;
    private readonly IVideoService? _video;
    private readonly ISubliminalService? _subliminal;
    private readonly IMindWipeService? _mindWipe;
    private readonly IBouncingTextService? _bouncingText;
    private readonly IOverlayService? _overlay;
    private readonly ILockCardService? _lockCard;
    private readonly IBubbleService? _bubbles;
    private readonly IBubbleCountService? _bubbleCount;
    private readonly ISessionLogService? _sessionLog;
    private readonly IAppLogger? _logger;

    public AvaloniaSessionEffectOrchestrator(
        ISettingsService settings,
        IFlashService? flash = null,
        IVideoService? video = null,
        ISubliminalService? subliminal = null,
        IMindWipeService? mindWipe = null,
        IBouncingTextService? bouncingText = null,
        IOverlayService? overlay = null,
        ILockCardService? lockCard = null,
        IBubbleService? bubbles = null,
        IBubbleCountService? bubbleCount = null,
        ISessionLogService? sessionLog = null,
        IAppLogger? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _flash = flash;
        _video = video;
        _subliminal = subliminal;
        _mindWipe = mindWipe;
        _bouncingText = bouncingText;
        _overlay = overlay;
        _lockCard = lockCard;
        _bubbles = bubbles;
        _bubbleCount = bubbleCount;
        _sessionLog = sessionLog;
        _logger = logger;
    }

    public void StartEffects(Session session)
    {
        if (session?.Settings == null) return;
        var s = session.Settings;

        _logger?.Information("Starting session effects for {SessionName}", session.Name);
        TryRun("session log begin", () => _sessionLog?.BeginSession(session));

        TryRun("overlay", () => _overlay?.Start());

        if (s.FlashEnabled) TryRun("flash", () => _flash?.Start());
        if (s.MandatoryVideosEnabled) TryRun("video", () => _video?.Start());
        if (s.SubliminalEnabled) TryRun("subliminal", () => _subliminal?.Start());
        if (s.MindWipeEnabled)
        {
            var appSettings = _settings.Current;
            TryRun("mindwipe", () => _mindWipe?.Start(appSettings.MindWipeFrequency, appSettings.MindWipeVolume / 100.0));
        }
        if (s.BouncingTextEnabled) TryRun("bouncing text", () => _bouncingText?.Start(s.BouncingTextPhrases));
        if (s.BubblesEnabled) TryRun("bubbles", () => _bubbles?.Start());
        if (s.BubbleCountEnabled) TryRun("bubble count", () => _bubbleCount?.Start());
        if (s.LockCardEnabled) TryRun("lock card", () => _lockCard?.Start());

        // Refresh overlays after individual effect services have started so that
        // pink/spiral/brain-drain windows reflect current settings.
        TryRun("overlay refresh", () => _overlay?.RefreshOverlays());
    }

    public void StopEffects()
    {
        _logger?.Information("Stopping all session effects");

        // Note: session log end is handled by SessionService.SessionCompleted so we get
        // the real duration/XP/completed flag. Stopping effects alone does not end the log.
        TryRun("flash", () => _flash?.Stop());
        TryRun("video", () => _video?.Stop());
        TryRun("subliminal", () => _subliminal?.Stop());
        TryRun("mindwipe", () => _mindWipe?.Stop());
        TryRun("bouncing text", () => _bouncingText?.Stop());
        TryRun("bubble count", () => _bubbleCount?.Stop());
        TryRun("lock card", () => _lockCard?.Stop());
        TryRun("bubbles", () => _bubbles?.Stop());
        TryRun("overlay", () => _overlay?.Stop());
    }

    private void TryRun(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "Session effect '{Effect}' start/stop failed", name);
        }
    }
}
