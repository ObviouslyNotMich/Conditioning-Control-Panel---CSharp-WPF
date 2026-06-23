namespace ConditioningControlPanel.Core.Services.BlinkTrainer;

/// <summary>
/// Default blink-pulse fallback for <see cref="Platform.IHapticsService"/> implementations
/// that do not expose a dedicated blink pulse API.
/// </summary>
public static class HapticsServiceExtensions
{
    /// <summary>
    /// Fire a short, gentle haptic pulse on a detected blink. Safe to call on any
    /// <see cref="Platform.IHapticsService"/>; no-ops if not connected.
    /// </summary>
    public static Task BlinkPulseAsync(this Platform.IHapticsService? service)
    {
        if (service == null) return Task.CompletedTask;
        return Task.Run(async () =>
        {
            try
            {
                await service.TestAsync(40, 120).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort; never block blink handling on a haptic failure.
            }
        });
    }
}
