namespace ConditioningControlPanel.Core.Services.BlinkTrainer;

/// <summary>
/// Cross-platform seam for the translucent gaze-debug dot.
/// Reference-counted: the cursor stays visible while any key has called <see cref="Show"/>.
/// </summary>
public interface IGazeDebugCursorService
{
    /// <summary>Request that the cursor be visible while <paramref name="key"/> is active.</summary>
    void Show(string key);

    /// <summary>Release the request for <paramref name="key"/>.</summary>
    void Hide(string key);

    /// <summary>Tints the cursor to indicate dwell lock-on.</summary>
    void SetLocked(bool locked);
}
