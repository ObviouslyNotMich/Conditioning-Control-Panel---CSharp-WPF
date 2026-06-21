namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// Platform-agnostic callbacks used by <see cref="BubbleEngine"/> to drive visuals.
/// All calls are made on the engine's scheduling thread (typically the UI thread).
/// </summary>
public interface IBubbleRenderer
{
    /// <summary>Creates the visual for a newly spawned bubble.</summary>
    void Create(BubbleState state);

    /// <summary>Updates the visual position/opacity/scale for a bubble.</summary>
    void Move(BubbleState state);

    /// <summary>Plays the pop animation for a bubble, invoking <paramref name="onComplete"/> when finished.</summary>
    void Pop(BubbleState state, Action onComplete);

    /// <summary>Immediately destroys the visual for a bubble.</summary>
    void Destroy(Guid id);

    /// <summary>Updates the label displayed on a bubble.</summary>
    void SetLabel(Guid id, string label);

    /// <summary>Updates the fuse progress indicator (0..1).</summary>
    void SetFuse(Guid id, double fraction);
}
