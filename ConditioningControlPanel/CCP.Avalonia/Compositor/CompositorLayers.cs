namespace ConditioningControlPanel.Avalonia.Compositor;

/// <summary>
/// Authoritative z-layer constants for the unified compositor engine.
/// Lower values are rendered first (behind higher values).
/// These override any z-ordering found in the legacy Avalonia codebase.
/// </summary>
public static class CompositorLayers
{
    /// <summary>Video decoder frame (bottom-most content layer).</summary>
    public const int Video = 10;

    /// <summary>Mandatory video attention-check layer.</summary>
    public const int MandatoryVideo = 15;

    /// <summary>Lock card overlay.</summary>
    public const int LockCard = 20;

    /// <summary>Flash image popups.</summary>
    public const int Flash = 30;

    /// <summary>Subliminal text flashes.</summary>
    public const int Subliminal = 40;

    /// <summary>Chaos / clickable bubbles.</summary>
    public const int Bubbles = 45;

    /// <summary>Bouncing text phrases.</summary>
    public const int BouncingText = 50;

    /// <summary>Brain drain blur overlay.</summary>
    public const int BrainDrain = 55;

    /// <summary>Spiral animation overlay.</summary>
    public const int Spiral = 60;

    /// <summary>Full-screen pink color tint (top-most).</summary>
    public const int PinkTint = 70;
}
