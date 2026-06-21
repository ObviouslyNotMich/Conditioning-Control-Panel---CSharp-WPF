namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>Motion pattern used by chaos-mode effect bubbles.</summary>
public enum ChaosMotion
{
    /// <summary>Drift upward from the bottom of the screen.</summary>
    FloatUp,

    /// <summary>Fall downward from the top of the screen.</summary>
    RainDown,

    /// <summary>Spawn near the center and bounce off screen edges.</summary>
    RoamBounce,

    /// <summary>Enter from the left or right edge and drift horizontally.</summary>
    SideDrift,
}
