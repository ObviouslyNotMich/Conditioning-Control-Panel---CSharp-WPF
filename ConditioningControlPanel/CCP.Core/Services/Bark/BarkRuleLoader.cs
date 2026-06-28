using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Core.Services.Bark;

/// <summary>
/// Loads bark rule manifests from disk: embedded base manifest + the active mod's overlay, field-level
/// merged by rule id (a mod rule supplying only { id, variant_pool } inherits the rest from the base
/// rule of the same id; array fields are REPLACED). Ported from the WPF BarkRuleLoader, parameterized
/// on the resolved folders/mod info so it stays platform-agnostic — the head supplies these from
/// <c>IModService</c> + the app base dir. Never throws; a missing/garbled manifest yields whatever
/// loaded successfully (possibly empty).
/// </summary>
public static class BarkRuleLoader
{
    public const string ManifestFileName = "bark_rules.json";

    private static readonly JsonMergeSettings MergeSettings = new()
    {
        MergeArrayHandling = MergeArrayHandling.Replace,      // skin owns its variant_pool outright
        MergeNullValueHandling = MergeNullValueHandling.Ignore // explicit null in mod won't blank a base field
    };

    /// <param name="companionAudioFolder">Embedded base folder: &lt;appBase&gt;/Resources/sounds/companion_audio.</param>
    /// <param name="activeModInstalledPath">Active packaged mod's InstalledPath, or null.</param>
    /// <param name="activeModId">Active mod id (for the embedded per-mod folder), or null.</param>
    /// <param name="logWarn">Optional warning sink (the head wires its logger).</param>
    public static BarkRuleSet Load(string companionAudioFolder, string? activeModInstalledPath, string? activeModId, Action<string>? logWarn = null)
    {
        // Merge at the JSON-object level so we can tell "field omitted" from "field = default".
        var merged = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);

        var embedded = Path.Combine(companionAudioFolder, ManifestFileName);
        MergeFile(merged, embedded, "embedded", logWarn);

        // Prefer a packaged-mod manifest (InstalledPath); otherwise the embedded per-mod folder.
        string? modManifest = null;
        if (!string.IsNullOrEmpty(activeModInstalledPath))
        {
            var p = Path.Combine(activeModInstalledPath, "resources", "sounds", "companion_audio", ManifestFileName);
            if (File.Exists(p)) modManifest = p;
        }
        if (modManifest == null && !string.IsNullOrEmpty(activeModId))
        {
            var p = Path.Combine(companionAudioFolder, "mods", activeModId, ManifestFileName);
            if (File.Exists(p)) modManifest = p;
        }
        if (modManifest != null)
            MergeFile(merged, modManifest, "mod", logWarn);

        var rules = new List<BarkRule>();
        foreach (var obj in merged.Values)
        {
            try
            {
                var rule = obj.ToObject<BarkRule>();
                if (rule != null && rule.IsValid())
                    rules.Add(rule);
            }
            catch (Exception ex)
            {
                logWarn?.Invoke($"BarkRuleLoader: failed to materialize a merged rule: {ex.Message}");
            }
        }

        return new BarkRuleSet(rules);
    }

    private static void MergeFile(Dictionary<string, JObject> merged, string path, string source, Action<string>? logWarn)
    {
        try
        {
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return;

            var arr = JArray.Parse(json);
            foreach (var token in arr)
            {
                if (token is not JObject obj) continue;
                var id = obj.Value<string>("id");
                if (string.IsNullOrWhiteSpace(id)) continue;

                if (merged.TryGetValue(id, out var existing))
                    existing.Merge(obj, MergeSettings); // mutates existing in place; mod fields override base
                else
                    merged[id] = obj;
            }
        }
        catch (Exception ex)
        {
            logWarn?.Invoke($"BarkRuleLoader: failed to read {source} manifest at {path}: {ex.Message}");
        }
    }
}
