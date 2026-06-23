namespace ConditioningControlPanel.Core.Services.Autonomy;

/// <summary>
/// Types of autonomous actions the companion can take.
/// </summary>
public enum AutonomyActionType
{
    Flash,
    Video,
    Subliminal,
    BrainDrainPulse,
    StartBubbles,
    Comment,
    MindWipe,
    LockCard,
    SpiralPulse,
    PinkFilterPulse,
    BouncingText,
    BubbleCount,
    WebVideo,
    WallpaperShuffle
}

/// <summary>
/// What triggered an autonomous action.
/// </summary>
public enum AutonomyTriggerSource
{
    Idle,
    Random,
    Context,
    TimeOfDay
}

/// <summary>
/// Time-of-day mood affecting autonomy behavior style.
/// </summary>
public enum AutonomyMood
{
    Gentle,     // Morning - softer, less frequent
    Attentive,  // Afternoon - moderate
    Playful,    // Evening - more active
    Mischievous // Night - most active
}
