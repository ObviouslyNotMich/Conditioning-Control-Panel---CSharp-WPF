using System;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Deeper;
using LibVLCSharp.Shared;

namespace ConditioningControlPanel.Avalonia.Services.Deeper
{
    /// <summary>
    /// Cross-platform <see cref="IPlaybackTimeSource"/> implementation backed by
    /// a LibVLC <see cref="MediaPlayer"/>. Drives the Deeper enhancement engine
    /// from the Avalonia/LibVLC player window.
    /// </summary>
    public sealed class AvaloniaLibVlcTimeSource : IPlaybackTimeSource, IDisposable
    {
        private readonly MediaPlayer _player;
        private readonly IUiDispatcher _dispatcher;
        private readonly DispatcherTimer _timer;
        private readonly Control? _videoViewHost;
        private bool _disposed;

        public AvaloniaLibVlcTimeSource(MediaPlayer player, IUiDispatcher dispatcher, Control? videoViewHost = null)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _videoViewHost = videoViewHost;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _timer.Tick += OnTimerTick;
        }

        public event Action<double>? PlaybackTimeChanged;

        public double GetCurrentTimeSeconds() => _player.Time / 1000.0;

        public double GetDurationSeconds() => _player.Length / 1000.0;

        public bool IsPlaying => _player.IsPlaying;

        public void Seek(double seconds) => _player.SeekTo(TimeSpan.FromSeconds(seconds));

        public void Pause() => _player.Pause();

        public void Play() => _player.Play();

        public PixelRect GetVideoRect()
        {
            // TODO: compute screen-space bounds of _videoViewHost when available.
            // Returning Empty causes the engine to skip gaze-target evaluation.
            return PixelRect.Empty;
        }

        public void StartTicking()
        {
            if (_disposed) return;
            _timer.Start();
        }

        public void StopTicking()
        {
            if (_disposed) return;
            _timer.Stop();
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            if (!_player.IsPlaying || _disposed) return;
            try
            {
                PlaybackTimeChanged?.Invoke(GetCurrentTimeSeconds());
            }
            catch
            {
                // Best-effort: never let a subscriber crash the timer tick.
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
        }
    }
}
