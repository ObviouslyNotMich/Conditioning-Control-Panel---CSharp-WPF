namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Desktop frame capture source for screen OCR/effects.
/// </summary>
public interface IFrameSource
{
    Task<RawFrame> CaptureAsync(ScreenInfo screen, CancellationToken cancellationToken = default);
}

public sealed record RawFrame(int Width, int Height, byte[] BgraData);
