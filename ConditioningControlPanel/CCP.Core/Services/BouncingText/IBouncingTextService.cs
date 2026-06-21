namespace ConditioningControlPanel.Core.Services.BouncingText;

/// <summary>
/// Cross-platform seam for the bouncing-text overlay effect engine.
/// The WPF implementation renders drifting text phrases across the screen;
/// the Avalonia head begins with a no-op stub so the feature control can toggle
/// live state without the full engine port blocking the UI.
/// </summary>
public interface IBouncingTextService
{
    /// <summary>Whether the bouncing-text engine is currently running.</summary>
    bool IsRunning { get; }

    /// <summary>Starts the bouncing-text engine with an optional phrase pool.</summary>
    void Start(IEnumerable<string>? textPool = null);

    /// <summary>Stops the bouncing-text engine and clears active text.</summary>
    void Stop();

    /// <summary>Refreshes text phrases after settings/asset changes.</summary>
    void Refresh();
}
