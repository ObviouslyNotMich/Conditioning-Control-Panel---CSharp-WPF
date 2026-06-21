namespace ConditioningControlPanel.Core.Services.AvatarTube;

/// <summary>
/// Minimal seam for toggling the Awareness Engine from the AvatarTube companion menu.
/// The real implementation should be registered by the host; until then the window
/// falls back to mutating <see cref="Models.AppSettings.AwarenessModeEnabled"/>.
/// </summary>
public interface IAwarenessEngine
{
    bool IsEnabled { get; set; }
}

/// <summary>
/// Minimal seam for toggling Bambi Takeover / autonomy mode from the AvatarTube menu.
/// The real implementation should be registered by the host; until then the window
/// falls back to <see cref="Models.AppSettings.AutonomyModeEnabled"/>.
/// </summary>
public interface IBambiTakeoverService
{
    bool IsActive { get; set; }
}

/// <summary>
/// Minimal seam for muting sub-audio whispers from the AvatarTube menu.
/// The real implementation should be registered by the host; until then the window
/// falls back to <see cref="Models.AppSettings.SubAudioEnabled"/>.
/// </summary>
public interface IWhisperService
{
    bool IsMuted { get; set; }
}

/// <summary>
/// Minimal seam for pausing in-app browser/video audio from the AvatarTube menu.
/// The real implementation should be registered by the host; until then the window
/// tracks a local flag only.
/// </summary>
public interface IBrowserAudioService
{
    bool IsPaused { get; set; }
}
