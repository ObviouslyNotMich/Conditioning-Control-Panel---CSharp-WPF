namespace ConditioningControlPanel.Core.Services.Subliminal;

/// <summary>
/// Cross-platform seam for the subliminal-message effect engine.
/// The WPF implementation shows brief text overlays; the Avalonia head begins
/// with a no-op stub so the feature control can toggle live state.
/// </summary>
public interface ISubliminalService
{
    /// <summary>Whether the subliminal engine is currently running.</summary>
    bool IsRunning { get; }

    /// <summary>Starts the subliminal display scheduler.</summary>
    void Start();

    /// <summary>Stops the subliminal display scheduler and clears active messages.</summary>
    void Stop();

    /// <summary>
    /// Flash a random active subliminal phrase from the configured pool.
    /// </summary>
    void FlashSubliminal();

    /// <summary>
    /// Flash a single custom subliminal phrase. Used by the Deeper enhancement engine.
    /// </summary>
    void FlashSubliminalCustom(string text, int? overrideDurationMs = null, bool suppressHaptic = false);
}
