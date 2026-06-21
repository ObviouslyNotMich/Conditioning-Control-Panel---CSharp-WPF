using System.Reflection;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// One-shot SFX player for the Avalonia head.
/// Resolves files under assets/sfx/ and Resources/; on Windows it tries to play via NAudio
/// if the assembly is already loaded (no new NuGet packages), otherwise no-op.
/// </summary>
public sealed class AvaloniaSfxPlayer : ISfxPlayer
{
    private readonly IAppLogger? _logger;
    private readonly Dictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public AvaloniaSfxPlayer(IAppLogger? logger = null)
    {
        _logger = logger;
    }

    public void Play(string name, float volume = 0.6f)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!OperatingSystem.IsWindows()) return;

        var path = ResolvePath(name);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        _ = Task.Run(() => PlayWithNaudio(path, Math.Clamp(volume, 0f, 1f)));
    }

    private string? ResolvePath(string name)
    {
        if (_cache.TryGetValue(name, out var cached)) return cached;

        var roots = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "assets", "sfx"),
            Path.Combine(AppContext.BaseDirectory, "Resources")
        };

        string? found = null;
        foreach (var root in roots)
        {
            var path = Path.Combine(root, name + ".wav");
            if (File.Exists(path))
            {
                found = path;
                break;
            }
        }

        _cache[name] = found;
        return found;
    }

    private void PlayWithNaudio(string path, float volume)
    {
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "NAudio", StringComparison.OrdinalIgnoreCase))
                ?? Assembly.Load("NAudio");

            var waveOutType = asm.GetType("NAudio.Wave.WaveOutEvent");
            var readerType = asm.GetType("NAudio.Wave.WaveFileReader");
            var playbackStateType = asm.GetType("NAudio.Wave.PlaybackState");
            if (waveOutType == null || readerType == null || playbackStateType == null) return;

            var waveOut = Activator.CreateInstance(waveOutType);
            using var reader = (IDisposable?)Activator.CreateInstance(readerType, path);
            if (waveOut == null || reader == null) return;

            waveOutType.GetMethod("Init")?.Invoke(waveOut, new object?[] { reader });
            readerType.GetProperty("Volume")?.SetValue(reader, volume);
            waveOutType.GetMethod("Play")?.Invoke(waveOut, null);

            var playbackStateProp = waveOutType.GetProperty("PlaybackState");
            if (playbackStateProp != null)
            {
                // Wait for playback to finish before disposing.
                while (true)
                {
                    var state = playbackStateProp.GetValue(waveOut);
                    if (state?.ToString() == "Stopped") break;
                    Thread.Sleep(50);
                }
            }
            else
            {
                Thread.Sleep(500);
            }

            (waveOut as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.Debug(ex, "AvaloniaSfxPlayer failed for {Path}", path);
        }
    }
}
