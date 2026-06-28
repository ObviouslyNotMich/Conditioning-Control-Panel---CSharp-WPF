using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace ConditioningControlPanel.Core.Services.Bark;

/// <summary>
/// Minimal bark-manifest access for inline voiced lines — the slice the "Hey Bambi" voice layer needs
/// (wake ack + voice-command confirmations + Deeper "Speak" prompts) without the full reactive bark
/// engine. Resolves the active mod's bark ruleset and serves a non-repeating voiced line per rule id.
/// </summary>
public interface IBarkManifestService
{
    /// <summary>
    /// Pick a variant (display text + resolved per-mod audio path) for the given rule id from the
    /// ACTIVE mod's loaded ruleset, avoiding an immediate repeat. Null when the rule or its pool is
    /// absent (caller falls back to its own text, unvoiced).
    /// </summary>
    (string Text, string? Audio)? PickVoiceLine(string ruleId);

    /// <summary>
    /// Resolve a voiceline filename to a full path: active packaged mod's companion_audio folder,
    /// then the embedded per-mod folder, then the embedded shared fallback. Null if absent.
    /// </summary>
    string? ResolveModAudio(string? file);

    /// <summary>Reload the ruleset for the current active mod (called on mod switch).</summary>
    void Reload();
}

/// <summary>
/// Portable implementation of <see cref="IBarkManifestService"/>. Loads the merged bark ruleset via
/// <see cref="BarkRuleLoader"/> and resolves per-mod audio. Ported from the relevant slice of the WPF
/// BarkService (PickVoiceLine / ResolveBarkAudio), with <c>App.Mods</c> replaced by injected
/// <see cref="IModService"/>.
/// </summary>
public sealed class BarkManifestService : IBarkManifestService
{
    private readonly IModService? _mods;
    private readonly object _gate = new();
    private readonly Random _rng = new();
    // Last variant index served per rule, so PickVoiceLine avoids an immediate repeat.
    private readonly Dictionary<string, int> _lastVoiceIdx = new(StringComparer.OrdinalIgnoreCase);

    private BarkRuleSet _rules = BarkRuleSet.Empty;
    private bool _loaded;

    /// <summary>Embedded base folder: &lt;appBase&gt;/Resources/sounds/companion_audio (mirrors the WPF CompanionPhraseService path).</summary>
    private static string CompanionAudioFolder =>
        Path.Combine(AppContext.BaseDirectory, "Resources", "sounds", "companion_audio");

    public BarkManifestService(IModService? mods = null)
    {
        _mods = mods;
        if (_mods != null)
            _mods.ActiveModChanged += (_, _) => Reload();
    }

    public void Reload()
    {
        lock (_gate)
        {
            _rules = BarkRuleLoader.Load(CompanionAudioFolder, _mods?.ActiveMod?.InstalledPath, _mods?.ActiveMod?.Id);
            _lastVoiceIdx.Clear();
            _loaded = true;
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        Reload();
    }

    public (string Text, string? Audio)? PickVoiceLine(string ruleId)
    {
        if (string.IsNullOrWhiteSpace(ruleId)) return null;
        EnsureLoaded();

        BarkVariant? v;
        lock (_gate)
        {
            var rule = _rules.AllRules.FirstOrDefault(
                r => string.Equals(r.Id, ruleId, StringComparison.OrdinalIgnoreCase));
            var pool = rule?.VariantPool;
            if (pool == null || pool.Count == 0) return null;

            var idx = _rng.Next(pool.Count);
            if (pool.Count > 1 && _lastVoiceIdx.TryGetValue(ruleId, out var last) && idx == last)
                idx = (idx + 1) % pool.Count;
            _lastVoiceIdx[ruleId] = idx;
            v = pool[idx];
        }
        return (v.Text, ResolveModAudio(v.Audio));
    }

    public string? ResolveModAudio(string? file)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;

        // 1) packaged mod (InstalledPath)
        var modPath = _mods?.ActiveMod?.InstalledPath;
        if (!string.IsNullOrEmpty(modPath))
        {
            var p = Path.Combine(modPath, "resources", "sounds", "companion_audio", file);
            if (File.Exists(p)) return p;
        }
        // 2) embedded per-mod folder
        var modId = _mods?.ActiveMod?.Id;
        if (!string.IsNullOrEmpty(modId))
        {
            var pm = Path.Combine(CompanionAudioFolder, "mods", modId, file);
            if (File.Exists(pm)) return pm;
        }
        // 3) embedded shared fallback
        var embedded = Path.Combine(CompanionAudioFolder, file);
        return File.Exists(embedded) ? embedded : null;
    }
}
