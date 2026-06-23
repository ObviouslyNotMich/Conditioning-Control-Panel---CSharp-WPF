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
    private readonly IAudioDeviceService? _audioDeviceService;
    private Media? _currentMedia;

    public AvaloniaAudioPlayer(LibVLC libVlc, IAudioDeviceService? audioDeviceService = null)
    {
        _libVlc = libVlc;
        _audioDeviceService = audioDeviceService;
        _player = new MediaPlayer(_libVlc);

        if (_audioDeviceService != null)
        {
            _audioDeviceService.PreferredDeviceChanged += OnPreferredDeviceChanged;
        }
    }

    private void OnPreferredDeviceChanged(object? sender, EventArgs e)
    {
        ApplyPreferredOutputDevice();
    }

    public Task PlayAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StopInternal();
        ApplyPreferredOutputDevice();
        _currentMedia = new Media(_libVlc, filePath);
        _player.Play(_currentMedia);
        return Task.CompletedTask;
    }

    public Task PlayLoopAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StopInternal();
        ApplyPreferredOutputDevice();
        _currentMedia = new Media(_libVlc, filePath);
        _currentMedia.AddOption(":input-repeat=-1");
        _player.Play(_currentMedia);
        return Task.CompletedTask;
    }

    public void Stop() => StopInternal();

    public void SetVolume(double volume) => _player.Volume = (int)(volume * 100);

    private void ApplyPreferredOutputDevice()
    {
        try
        {
            var deviceId = _audioDeviceService?.GetDefaultOutputDeviceId();
            if (string.IsNullOrEmpty(deviceId))
                return;

            // LibVLCSharp provides SetOutputDevice on MediaPlayer for LibVLC 3.x+.
            // Failure is non-fatal: playback falls back to the default endpoint.
            _player.SetOutputDevice(deviceId);
        }
        catch
        {
            // Device enumeration or selection may not be supported on this platform.
        }
    }

    private void StopInternal()
    {
        _player.Stop();
        _currentMedia?.Dispose();
        _currentMedia = null;
    }

    public ValueTask DisposeAsync()
    {
        if (_audioDeviceService != null)
        {
            _audioDeviceService.PreferredDeviceChanged -= OnPreferredDeviceChanged;
        }

        StopInternal();
        _player.Dispose();
        // Do not dispose _libVlc: it is a shared singleton owned by the DI container.
        return ValueTask.CompletedTask;
    }
}
