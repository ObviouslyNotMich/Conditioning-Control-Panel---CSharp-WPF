using System;
using NAudio.Wave;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Plays a single, long MUSIC track (a story "popping session" soundtrack) with a queryable
    /// playback <see cref="Position"/> — the one thing <see cref="AudioService"/> deliberately does
    /// not expose (it is built for short, fire-and-forget whisper/pop clips). A story song is the
    /// bed for a song-synced Chaos run: <see cref="Chaos.ChaosMusicalDirector"/> reads
    /// <see cref="Position"/> every tick to drive bubble spawn off the song's loudness envelope and
    /// to fire scripted events at exact song timestamps.
    ///
    /// Uses its own dedicated <see cref="WaveOutEvent"/> (separate from AudioService's short-sound
    /// player, so the two never stomp each other) created via <see cref="AudioService.CreateWaveOut"/>
    /// so it honours the user's chosen output device. Single-threaded use from the UI thread.
    /// </summary>
    public sealed class MusicService : IDisposable
    {
        private readonly AudioService _audio;
        private WaveOutEvent? _out;
        private AudioFileReader? _reader;
        private bool _ducked;
        private long _duckGen = -1;

        public MusicService(AudioService audio) => _audio = audio;

        public bool IsPlaying => _out?.PlaybackState == PlaybackState.Playing;

        /// <summary>Current playback position. Reset to ~zero on each loop. Zero when stopped.</summary>
        public TimeSpan Position => _reader?.CurrentTime ?? TimeSpan.Zero;

        /// <summary>Total length of the loaded track (one loop's worth). Zero when nothing is loaded.</summary>
        public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

        /// <summary>
        /// Start a track. Replaces anything already playing. <paramref name="volumePercent"/> is 0..100.
        /// When <paramref name="duckOthers"/> is true, other applications are softened so the song reads
        /// as the bed (restored on <see cref="Stop"/>). Returns false if the file couldn't be opened.
        /// </summary>
        public bool Play(string path, bool loop = false, int volumePercent = 100, bool duckOthers = true, int duckStrength = 55)
        {
            Stop();
            try
            {
                _reader = new AudioFileReader(path)
                {
                    Volume = Math.Clamp(volumePercent, 0, 100) / 100f
                };
                IWaveProvider source = loop ? new LoopStream(_reader) : _reader;

                _out = _audio.CreateWaveOut() ?? new WaveOutEvent();
                _out.Init(source);
                _out.Play();

                if (duckOthers)
                {
                    try { _audio.Duck(duckStrength); _duckGen = _audio.DuckGeneration; _ducked = true; }
                    catch (Exception ex) { App.Logger?.Debug("MusicService duck: {E}", ex.Message); }
                }
                App.Logger?.Information("MusicService playing {Path} ({Dur:0.0}s, loop={Loop})", path, Duration.TotalSeconds, loop);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("MusicService.Play failed for {Path}: {E}", path, ex.Message);
                Stop();
                return false;
            }
        }

        public void Stop()
        {
            if (_ducked)
            {
                try { _audio.Unduck(_duckGen); } catch (Exception ex) { App.Logger?.Debug("MusicService unduck: {E}", ex.Message); }
                _ducked = false; _duckGen = -1;
            }
            try { _out?.Stop(); } catch { }
            try { _out?.Dispose(); } catch { }
            try { _reader?.Dispose(); } catch { }
            _out = null;
            _reader = null;
        }

        public void Dispose() => Stop();

        /// <summary>Wraps a reader so playback seeks back to the start at EOF — endless loop.</summary>
        private sealed class LoopStream : WaveStream
        {
            private readonly AudioFileReader _src;
            public LoopStream(AudioFileReader src) => _src = src;
            public override WaveFormat WaveFormat => _src.WaveFormat;
            public override long Length => _src.Length;
            public override long Position { get => _src.Position; set => _src.Position = value; }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int total = 0;
                while (total < count)
                {
                    int read = _src.Read(buffer, offset + total, count - total);
                    if (read == 0)
                    {
                        if (_src.Position == 0) break;   // empty/zero-length source — avoid a spin
                        _src.Position = 0;               // loop back to the top
                    }
                    total += read;
                }
                return total;
            }
        }
    }
}
