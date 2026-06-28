using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Bark;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Core.Services.Mantra;

/// <summary>
/// Loads and serves the per-mod Spoken Mantras dataset for the Takeover "say it for me" mechanic.
/// Ported from the WPF MantraVoiceService: <c>App.Mods</c> → injected <see cref="IModService"/>,
/// audio resolution delegated to <see cref="IBarkManifestService.ResolveModAudio"/> (same tiered
/// lookup), and clip duration via the <see cref="IAudioDurationProvider"/> seam (NAudio on Windows).
/// Empty/missing <c>mantras.json</c> ⇒ <see cref="HasMantras"/> false, so the feature self-skips.
/// </summary>
public interface IMantraVoiceService
{
    bool HasMantras();
    MantraEntry? NextMantra();
    MantraLine? GetRetry();
    MantraLine? GetTimeout();
    string? ResolveAudio(string? file);
    TimeSpan? GetAudioDuration(string? fullPath);
}

/// <inheritdoc cref="IMantraVoiceService"/>
public sealed class MantraVoiceService : IMantraVoiceService
{
    private readonly IModService? _mods;
    private readonly IBarkManifestService _barkManifest;
    private readonly IAudioDurationProvider _durations;
    private readonly Action<string>? _logInfo;

    private readonly Random _random = new();
    private readonly object _lock = new();

    // Cache the parsed set per active mod; reload when the active mod changes.
    private string? _loadedModId;
    private MantraSet? _set;

    // No-repeat rotation: remember recent ids so she doesn't ask the same thing twice in a row.
    private readonly Queue<string> _recent = new();
    private const int RecentMemory = 8;

    private static string CompanionAudioFolder =>
        Path.Combine(AppContext.BaseDirectory, "Resources", "sounds", "companion_audio");

    public MantraVoiceService(IBarkManifestService barkManifest, IAudioDurationProvider durations,
        IModService? mods = null, Action<string>? logInfo = null)
    {
        _barkManifest = barkManifest ?? throw new ArgumentNullException(nameof(barkManifest));
        _durations = durations ?? new NullAudioDurationProvider();
        _mods = mods;
        _logInfo = logInfo;
    }

    public bool HasMantras()
    {
        var set = ActiveSet();
        return set != null && set.Mantras.Any(m => m.Enabled && !string.IsNullOrWhiteSpace(m.Phrase));
    }

    public MantraEntry? NextMantra()
    {
        lock (_lock)
        {
            var set = ActiveSet();
            if (set == null) return null;

            var pool = set.Mantras.Where(m => m.Enabled && !string.IsNullOrWhiteSpace(m.Phrase)).ToList();
            if (pool.Count == 0) return null;

            // Prefer entries not in the recent set; fall back to the full pool if all are recent.
            var fresh = pool.Where(m => !_recent.Contains(m.Id)).ToList();
            var choices = fresh.Count > 0 ? fresh : pool;
            var pick = choices[_random.Next(choices.Count)];

            if (!string.IsNullOrEmpty(pick.Id))
            {
                _recent.Enqueue(pick.Id);
                while (_recent.Count > Math.Min(RecentMemory, Math.Max(1, pool.Count - 1)))
                    _recent.Dequeue();
            }
            return pick;
        }
    }

    public MantraLine? GetRetry() => PickLine(ActiveSet()?.Retry);

    public MantraLine? GetTimeout() => PickLine(ActiveSet()?.Timeout);

    private MantraLine? PickLine(List<MantraLine>? lines)
    {
        if (lines == null || lines.Count == 0) return null;
        return lines[_random.Next(lines.Count)];
    }

    /// <summary>Resolve a clip filename to a full path under the active mod (delegates to the bark resolver).</summary>
    public string? ResolveAudio(string? file) => _barkManifest.ResolveModAudio(file);

    public TimeSpan? GetAudioDuration(string? fullPath) => _durations.GetDuration(fullPath);

    private MantraSet? ActiveSet()
    {
        lock (_lock)
        {
            var modId = _mods?.ActiveMod?.Id ?? "";
            if (_set != null && _loadedModId == modId) return _set;

            _set = Load(modId);
            _loadedModId = modId;
            _recent.Clear(); // rotation is per-mod
            return _set;
        }
    }

    private MantraSet? Load(string modId)
    {
        try
        {
            var path = ResolveSetPath(modId);
            if (path == null)
            {
                _logInfo?.Invoke($"MantraVoiceService: no mantras.json for mod {modId}");
                return null;
            }
            var json = File.ReadAllText(path);
            var set = JsonConvert.DeserializeObject<MantraSet>(json);
            _logInfo?.Invoke($"MantraVoiceService: loaded {set?.Mantras.Count ?? 0} mantras for mod {modId} from {path}");
            return set;
        }
        catch (Exception ex)
        {
            _logInfo?.Invoke($"MantraVoiceService: failed to load mantras for mod {modId}: {ex.Message}");
            return null;
        }
    }

    private string? ResolveSetPath(string modId)
    {
        // 1) packaged mod (InstalledPath)
        var modPath = _mods?.ActiveMod?.InstalledPath;
        if (!string.IsNullOrEmpty(modPath))
        {
            var p = Path.Combine(modPath, "resources", "sounds", "companion_audio", "mantras.json");
            if (File.Exists(p)) return p;
        }
        // 2) embedded per-mod folder
        if (!string.IsNullOrEmpty(modId))
        {
            var pm = Path.Combine(CompanionAudioFolder, "mods", modId, "mantras.json");
            if (File.Exists(pm)) return pm;
        }
        return null;
    }
}
