using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Resolves optional Chaos art with a graceful fallback. Every UI slot (upgrade
/// icons, branch crests, boon chips, portrait, banner, bubble sprites) calls
/// <see cref="TryLoad"/>; a null return means "no art present, draw the vector
/// placeholder". The game is fully playable with zero art files.
///
/// The path convention (resolved in phase 5) lives here as <see cref="PathFor"/>:
///   assets/Chaos/{kind}/{id}.png  under <see cref="App.UserAssetsPath"/> first,
///   then the app's bundled Assets folder.
/// </summary>
public static class ChaosArt
{
    // Frozen decoded bitmaps are immutable and safely shareable across every visual that
    // shows the same art. WITHOUT this cache, each call decoded a fresh native MIL bitmap —
    // and per-bubble-spawn callers (ChaosBubbleVariants, several per second during a run)
    // piled up hundreds of decoded sprites in unmanaged memory, climbing to an OOM hard
    // crash (working set 2GB+, GC heap flat — see chaos-bubble-oom-leak). One shared frozen
    // bitmap per distinct file collapses that to a handful. Keyed by full path so user-asset
    // overrides and bundled art never collide.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ImageSource?> _cache = new();

    /// <summary>Load an image from an explicit path, or null if absent/blank/unreadable.
    /// Results (including the null "no such file") are cached and the bitmap is frozen +
    /// shared across all callers — never decode the same sprite twice.</summary>
    public static ImageSource? TryLoad(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return _cache.GetOrAdd(path!, static p =>
        {
            try
            {
                if (!File.Exists(p)) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(p, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        });
    }

    /// <summary>
    /// Resolve a Chaos art file by convention: <c>assets/Chaos/{kind}/{id}.png</c>,
    /// checked under the user assets folder first then the bundled app folder.
    /// Returns the loaded image or null when no file is present.
    /// </summary>
    public static ImageSource? Resolve(string kind, string id)
    {
        foreach (var root in Roots())
        {
            var p = Path.Combine(root, "assets", "Chaos", kind, id + ".png");
            var img = TryLoad(p);
            if (img != null) return img;
        }
        return null;
    }

    /// <summary>The hero banner at <c>Assets/Chaos/banner.png</c>, or null when absent.</summary>
    public static ImageSource? ResolveBanner()
    {
        foreach (var root in Roots())
        {
            var img = TryLoad(Path.Combine(root, "assets", "Chaos", "banner.png"));
            if (img != null) return img;
        }
        return null;
    }

    /// <summary>The recap-card hero banner at <c>assets/Chaos/recap.png</c>, or null when absent.</summary>
    public static ImageSource? ResolveRecap()
    {
        foreach (var root in Roots())
        {
            var img = TryLoad(Path.Combine(root, "assets", "Chaos", "recap.png"));
            if (img != null) return img;
        }
        return null;
    }

    /// <summary>The first existing convention path for a kind/id, or null. Used by callers that need the path itself.</summary>
    public static string? PathFor(string kind, string id)
    {
        foreach (var root in Roots())
        {
            var p = Path.Combine(root, "assets", "Chaos", kind, id + ".png");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private static System.Collections.Generic.IEnumerable<string> Roots()
    {
        string? user = null;
        try { user = App.UserAssetsPath; } catch { }
        if (!string.IsNullOrEmpty(user)) yield return user!;
        yield return AppContext.BaseDirectory;
    }
}
