using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Thin reusable accessor over the HypnoTube link pool so Chaos payloads don't
/// depend on the avatar window. The canonical pool currently lives as
/// <c>AvatarTubeWindow.KnownVideoLinks</c> (reloaded per-mod via its
/// <c>ReloadVideoLinks()</c>); per-mod/user overrides come from
/// <c>AppSettings.VideoLinksByMod</c>. This helper just reads whatever is
/// currently loaded and picks one.
/// </summary>
public static class HtLinkPool
{
    private static readonly Random _rng = new();

    /// <summary>All currently-known HTTPS video URLs (mod overrides + builtin pool).</summary>
    public static IReadOnlyList<string> AllUrls()
    {
        var urls = new List<string>();
        try
        {
            // Per-mod / user overrides take precedence and are merged in first.
            var mod = App.Settings?.Current?.ActiveModId;
            var byMod = App.Settings?.Current?.VideoLinksByMod;
            if (!string.IsNullOrEmpty(mod) && byMod != null &&
                byMod.TryGetValue(mod!, out var overrides) && overrides != null)
            {
                foreach (var kv in overrides)
                    if (IsHttps(kv.Value)) urls.Add(kv.Value);
            }
        }
        catch (Exception ex) { App.Logger?.Debug("HtLinkPool overrides: {E}", ex.Message); }

        try
        {
            foreach (var kv in AvatarTubeWindow.KnownVideoLinks)
                if (IsHttps(kv.Value)) urls.Add(kv.Value);
        }
        catch (Exception ex) { App.Logger?.Debug("HtLinkPool builtin: {E}", ex.Message); }

        return urls.Distinct().ToList();
    }

    /// <summary>Pick a random pool URL, or null if the pool is empty.</summary>
    public static string? PickRandom()
    {
        var urls = AllUrls();
        if (urls.Count == 0) return null;
        return urls[_rng.Next(urls.Count)];
    }

    private static bool IsHttps(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        url!.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
