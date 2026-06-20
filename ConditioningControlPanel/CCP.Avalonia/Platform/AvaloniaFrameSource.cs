using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Desktop frame capture stub. Cross-platform capture is not implemented in Avalonia core;
/// desktop heads need platform-specific capture APIs.
/// </summary>
public sealed class AvaloniaFrameSource : IFrameSource
{
    public Task<RawFrame> CaptureAsync(ScreenInfo screen, CancellationToken cancellationToken = default)
        => throw new PlatformNotSupportedException(
            "Desktop frame capture is not implemented in the Avalonia core shim.");
}
