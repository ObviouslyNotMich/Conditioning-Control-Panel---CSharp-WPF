using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CorePixelRect = ConditioningControlPanel.Core.Platform.PixelRect;
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
        private readonly DispatcherTimer _timer;
        private readonly Control? _videoViewHost;
        private bool _disposed;

        public AvaloniaLibVlcTimeSource(MediaPlayer player, Control? videoViewHost = null)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
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

        public CorePixelRect GetVideoRect()
        {
            if (_videoViewHost == null) return CorePixelRect.Empty;

            try
            {
                var topLevel = TopLevel.GetTopLevel(_videoViewHost) as Window;
                if (topLevel == null) return CorePixelRect.Empty;

                var position = topLevel.Position;
                var point = _videoViewHost.TranslatePoint(new global::Avalonia.Point(0, 0), topLevel) ?? new global::Avalonia.Point(0, 0);
                var bounds = _videoViewHost.Bounds;
                if (bounds.Width <= 0 || bounds.Height <= 0) return CorePixelRect.Empty;

                var scaling = 1.0;
                try
                {
                    var screen = topLevel.Screens?.ScreenFromWindow(topLevel);
                    if (screen != null) scaling = screen.Scaling;
                }
                catch { /* fallback to 1.0 */ }

                var x = (position.X + point.X) * scaling;
                var y = (position.Y + point.Y) * scaling;
                var w = bounds.Width * scaling;
                var h = bounds.Height * scaling;
                return new CorePixelRect(x, y, w, h);
            }
            catch
            {
                return CorePixelRect.Empty;
            }
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
