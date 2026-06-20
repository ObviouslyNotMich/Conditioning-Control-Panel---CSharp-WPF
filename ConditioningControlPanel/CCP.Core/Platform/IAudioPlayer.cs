namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Cross-platform audio playback abstraction.
/// </summary>
public interface IAudioPlayer : IAsyncDisposable
{
    Task PlayAsync(string filePath, CancellationToken cancellationToken = default);
    Task PlayLoopAsync(string filePath, CancellationToken cancellationToken = default);
    void Stop();
    void SetVolume(double volume);
}
