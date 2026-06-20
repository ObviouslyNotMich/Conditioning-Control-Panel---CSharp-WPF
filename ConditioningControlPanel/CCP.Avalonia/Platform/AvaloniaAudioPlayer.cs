using LibVLCSharp.Shared;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Cross-platform audio player shim using LibVLC.
/// </summary>
public sealed class AvaloniaAudioPlayer : IAudioPlayer
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _player;
    private Media? _currentMedia;

    public AvaloniaAudioPlayer(LibVLC libVlc)
    {
        _libVlc = libVlc;
        _player = new MediaPlayer(_libVlc);
    }

    public Task PlayAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StopInternal();
        _currentMedia = new Media(_libVlc, filePath);
        _player.Play(_currentMedia);
        return Task.CompletedTask;
    }

    public Task PlayLoopAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StopInternal();
        _currentMedia = new Media(_libVlc, filePath);
        _currentMedia.AddOption(":input-repeat=-1");
        _player.Play(_currentMedia);
        return Task.CompletedTask;
    }

    public void Stop() => StopInternal();

    public void SetVolume(double volume) => _player.Volume = (int)(volume * 100);

    private void StopInternal()
    {
        _player.Stop();
        _currentMedia?.Dispose();
        _currentMedia = null;
    }

    public ValueTask DisposeAsync()
    {
        StopInternal();
        _player.Dispose();
        // Do not dispose _libVlc: it is a shared singleton owned by the DI container.
        return ValueTask.CompletedTask;
    }
}
