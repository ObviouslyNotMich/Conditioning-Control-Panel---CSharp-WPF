using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Services.Mod;
using ConditioningControlPanel.Core.Platform;
using LibVLCSharp.Shared;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// One-shot SFX player for the Avalonia head.
/// Resolves files via <see cref="AvaloniaModResourceResolver"/> (active mod override first,
/// then the embedded <c>Resources/sounds/</c> copy) and plays them through a dedicated
/// LibVLC <see cref="MediaPlayer"/> so short effects can overlap speech/audio.
/// Supports both .wav and .mp3, matching the WPF asset layout.
/// </summary>
public sealed class AvaloniaSfxPlayer : ISfxPlayer
{
    private readonly ILibVlcProvider _libVlcProvider;
    private readonly AvaloniaModResourceResolver _resolver;
    private readonly IAudioDeviceService? _audioDeviceService;
    private readonly ILogger<AvaloniaSfxPlayer>? _logger;
    private readonly Random _random = new();

    public AvaloniaSfxPlayer(
        ILibVlcProvider libVlcProvider,
        AvaloniaModResourceResolver resolver,
        IAudioDeviceService? audioDeviceService = null,
        ILogger<AvaloniaSfxPlayer>? logger = null)
    {
        _libVlcProvider = libVlcProvider;
        _resolver = resolver;
        _audioDeviceService = audioDeviceService;
        _logger = logger;
    }

    public void Play(string name, float volume = 0.6f)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        var path = ResolvePath(name);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            _logger?.LogDebug("SFX path not found for {Name}", name);
            return;
        }

        _ = Task.Run(() => PlayWithLibVlc(path, Math.Clamp(volume, 0f, 1f)));
    }

    private string? ResolvePath(string name)
    {
        var normalized = name.Trim().Replace('\\', '/');
        string relative;

        // Logical SFX names map to the same files the WPF head uses.
        // Randomize each call so pops, chimes and giggles stay varied.
        var lowered = normalized.ToLowerInvariant();
        if (lowered == "pop")
            relative = $"bubbles/Pop{_random.Next(1, 4)}.mp3";
        else if (lowered == "chime")
            relative = $"chime{_random.Next(1, 4)}.mp3";
        else if (lowered == "giggle")
            relative = $"giggle{_random.Next(1, 9)}.mp3";
        else
            relative = normalized;

        if (Path.HasExtension(relative))
            return _resolver.ResolveAudioPath(relative);

        return _resolver.ResolveAudioPath(relative + ".mp3")
               ?? _resolver.ResolveAudioPath(relative + ".wav");
    }

    private void PlayWithLibVlc(string path, float volume)
    {
        MediaPlayer? player = null;
        Media? media = null;
        try
        {
            var libVlc = _libVlcProvider.Value;
            // Use FromType.FromPath so LibVLC treats the string as a local file,
            // not an MRL/URI that may be parsed differently in v12.
            media = new Media(libVlc, path, FromType.FromPath);
            player = new MediaPlayer(libVlc)
            {
                Volume = (int)(volume * 100)
            };
            ApplyPreferredDevice(player);
            player.Play(media);

            // Short sound effects: wait for playback to finish or error.
            var sw = Stopwatch.StartNew();
            const int maxWaitMs = 8000;
            const int spinMs = 30;
            // Give LibVLC a moment to leave the idle state before polling.
            Thread.Sleep(spinMs);
            while (player.State != VLCState.Ended
                   && player.State != VLCState.Error
                   && player.State != VLCState.Stopped
                   && sw.ElapsedMilliseconds < maxWaitMs)
            {
                Thread.Sleep(spinMs);
            }

            if (player.State == VLCState.Error)
                _logger?.LogDebug("SFX playback entered error state for {Path}", path);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "AvaloniaSfxPlayer failed for {Path}", path);
        }
        finally
        {
            player?.Stop();
            player?.Dispose();
            media?.Dispose();
        }
    }

    private void ApplyPreferredDevice(MediaPlayer player)
    {
        try
        {
            var deviceId = _audioDeviceService?.GetDefaultOutputDeviceId();
            if (!string.IsNullOrEmpty(deviceId))
                player.SetOutputDevice(deviceId);
        }
        catch
        {
            // Device selection is best-effort.
        }
    }
}
