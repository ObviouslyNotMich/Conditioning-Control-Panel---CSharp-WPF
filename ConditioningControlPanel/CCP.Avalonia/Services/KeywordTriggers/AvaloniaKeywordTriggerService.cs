using System;
using System.IO;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Services.KeywordTriggers;

/// <summary>
/// Avalonia implementation of keyword-trigger runtime helpers.
/// </summary>
public sealed class AvaloniaKeywordTriggerService : IKeywordTriggerService
{
    private readonly IAudioPlayer _audioPlayer;
    private readonly IAppEnvironment _environment;
    private readonly ILogger<AvaloniaKeywordTriggerService> _logger;

    public AvaloniaKeywordTriggerService(
        IAudioPlayer audioPlayer,
        IAppEnvironment environment,
        ILogger<AvaloniaKeywordTriggerService> logger)
    {
        _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void PreviewAudioClip(string filePath, int volume)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        var resolved = ResolveAudioPath(filePath);
        if (!File.Exists(resolved))
        {
            _logger.LogWarning("PreviewAudioClip: file not found {Path}", resolved);
            return;
        }

        try
        {
            _audioPlayer.SetVolume(Math.Clamp(volume / 100.0, 0.0, 1.0));
            _ = Task.Run(async () => await _audioPlayer.PlayAsync(resolved));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PreviewAudioClip: failed to play {Path}", resolved);
        }
    }

    private string ResolveAudioPath(string filePath)
    {
        if (Path.IsPathRooted(filePath)) return filePath;

        var normalized = filePath.Replace('\\', '/').TrimStart('/');

        const string PresetAudioPrefix = "AwarenessPresets/audio/";
        const string ResourcesPrefix = "Resources/AwarenessPresets/audio/";

        if (normalized.StartsWith(PresetAudioPrefix, StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(PresetAudioPrefix.Length);
        else if (normalized.StartsWith(ResourcesPrefix, StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(ResourcesPrefix.Length);

        return Path.Combine(_environment.BaseDirectory, "Resources", "AwarenessPresets", "audio", normalized);
    }
}
