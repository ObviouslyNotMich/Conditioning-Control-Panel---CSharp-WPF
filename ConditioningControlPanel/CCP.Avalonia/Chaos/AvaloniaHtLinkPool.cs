using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of HtLinkPool: thin accessor over the mod/video link pool.
/// Replaces the WPF dependency on AvatarTubeWindow.KnownVideoLinks with the
/// cross-platform IModService.GetVideoLinks() seam.
/// </summary>
public static class AvaloniaHtLinkPool
{
    private static readonly Random _rng = new();

    /// <summary>All currently-known HTTPS video URLs (mod pool + user overrides).</summary>
    public static IReadOnlyList<string> AllUrls()
    {
        var urls = new List<string>();
        try
        {
            var modService = App.Services?.GetService<ConditioningControlPanel.IModService>();
            var links = modService?.GetVideoLinks();
            if (links != null)
            {
                foreach (var kv in links)
                    if (IsHttps(kv.Value)) urls.Add(kv.Value);
            }
        }
        catch { }
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
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
