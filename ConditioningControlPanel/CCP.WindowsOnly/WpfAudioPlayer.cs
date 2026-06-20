using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.WindowsOnly;

/// <summary>
/// NAudio-backed shim for <see cref="IAudioPlayer"/>.
/// </summary>
public sealed class WpfAudioPlayer : IAudioPlayer
{
    private readonly object _lock = new();
    private IWavePlayer? _outputDevice;
    private AudioFileReader? _fileReader;
    private TaskCompletionSource? _playbackCompletion;
    private CancellationTokenSource? _cts;
    private bool _loop;
    private bool _disposed;

    public Task PlayAsync(string filePath, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            StopInternal();

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _playbackCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            _outputDevice = new WaveOutEvent();
            _fileReader = new AudioFileReader(filePath);
            _outputDevice.Init(_fileReader);
            _outputDevice.PlaybackStopped += OnPlaybackStopped;
            _outputDevice.Play();

            return _playbackCompletion.Task;
        }
    }

    public Task PlayLoopAsync(string filePath, CancellationToken cancellationToken = default)
    {
        _loop = true;
        return PlayAsync(filePath, cancellationToken);
    }

    public void Stop()
    {
        lock (_lock)
        {
            StopInternal();
        }
    }

    public void SetVolume(double volume)
    {
        lock (_lock)
        {
            if (_fileReader != null)
            {
                _fileReader.Volume = (float)Math.Clamp(volume, 0.0, 1.0);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            StopInternal();
        }
    }

    private void StopInternal()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        if (_outputDevice != null)
        {
            _outputDevice.PlaybackStopped -= OnPlaybackStopped;
            _outputDevice.Stop();
            _outputDevice.Dispose();
            _outputDevice = null;
        }

        _fileReader?.Dispose();
        _fileReader = null;

        _playbackCompletion?.TrySetResult();
        _playbackCompletion = null;
        _loop = false;
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        lock (_lock)
        {
            if (_disposed || _cts?.IsCancellationRequested == true)
            {
                _playbackCompletion?.TrySetResult();
                return;
            }

            if (_loop && _fileReader != null && !string.IsNullOrEmpty(_fileReader.FileName))
            {
                try
                {
                    _fileReader.Position = 0;
                    _outputDevice?.Play();
                    return;
                }
                catch (Exception)
                {
                    // Fall through and complete on loop failure.
                }
            }

            _playbackCompletion?.TrySetResult();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WpfAudioPlayer));
    }
}
