using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Loads a mod's emotive-portrait avatar manifest (<c>avatar_manifest.json</c>) from disk,
    /// mirroring <see cref="Bark.BarkRuleLoader"/>'s two-tier path resolution. Returns an
    /// <see cref="AvatarPortraitSet"/> (manifest + on-disk portrait loader) or <c>null</c> when the
    /// active mod ships no manifest — in which case the avatar falls back to the legacy 4-pose path.
    /// Portraits are loaded as frozen <see cref="BitmapImage"/> straight from the absolute file path
    /// (NOT via pack:// — these are on-disk Content, like bark audio).
    /// </summary>
    public static class AvatarPortraitLoader
    {
        public const string ManifestFileName = "avatar_manifest.json";

        /// <summary>Packaged mod's manifest (InstalledPath/resources/sounds/companion_audio/). Null if none.</summary>
        public static string? ActiveModManifestPath
        {
            get
            {
                var modPath = App.Mods?.ActiveMod?.InstalledPath;
                if (string.IsNullOrEmpty(modPath)) return null;
                var p = Path.Combine(modPath, "resources", "sounds", "companion_audio", ManifestFileName);
                return File.Exists(p) ? p : null;
            }
        }

        /// <summary>Built-in mod's manifest from the embedded per-mod folder (.../companion_audio/mods/{modId}/). Null if none.</summary>
        public static string? EmbeddedModManifestPath
        {
            get
            {
                var modId = App.Mods?.ActiveModId;
                if (string.IsNullOrEmpty(modId)) return null;
                var p = Path.Combine(CompanionPhraseService.CompanionAudioFolder, "mods", modId, ManifestFileName);
                return File.Exists(p) ? p : null;
            }
        }

        /// <summary>Resolve the active mod's manifest path (packaged first, then embedded). Null = no portrait system.</summary>
        public static string? ResolveManifestPath() => ActiveModManifestPath ?? EmbeddedModManifestPath;

        /// <summary>True if the active mod ships a portrait manifest (cheap existence check, no parse).</summary>
        public static bool HasManifestForActiveMod() => ResolveManifestPath() != null;

        /// <summary>
        /// Load + parse the active mod's manifest into an <see cref="AvatarPortraitSet"/>. Never throws —
        /// a missing/garbled manifest returns null and the caller uses the legacy avatar path.
        /// </summary>
        public static AvatarPortraitSet? Load()
        {
            var path = ResolveManifestPath();
            if (path == null) return null;
            try
            {
                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;
                var manifest = JsonConvert.DeserializeObject<AvatarPortraitManifest>(json);
                if (manifest == null || manifest.Emotions.Count == 0 || manifest.Skins.Count == 0)
                {
                    App.Logger?.Warning("AvatarPortraitLoader: manifest at {Path} is empty/invalid", path);
                    return null;
                }
                var baseDir = Path.GetDirectoryName(path) ?? "";
                App.Logger?.Information(
                    "AvatarPortraitLoader: loaded {Emotions} emotions, {Skins} skins, {Lines} lines from {Path}",
                    manifest.Emotions.Count, manifest.Skins.Count, manifest.Lines.Count, path);
                return new AvatarPortraitSet(manifest, baseDir);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AvatarPortraitLoader: failed to load manifest at {Path}", path);
                return null;
            }
        }
    }

    /// <summary>
    /// A loaded portrait avatar: the manifest plus lazy, frozen <see cref="BitmapImage"/> buckets
    /// keyed by (skin index, emotion). Missing files are skipped; an empty bucket falls back to the
    /// same emotion in skin 0, then to the idle emotion, so the avatar never shows nothing.
    /// </summary>
    public class AvatarPortraitSet
    {
        private readonly AvatarPortraitManifest _m;
        private readonly string _baseDir;
        private readonly Dictionary<string, BitmapImage[]> _cache = new();

        public AvatarPortraitSet(AvatarPortraitManifest manifest, string baseDir)
        {
            _m = manifest;
            _baseDir = baseDir;
        }

        public AvatarPortraitManifest Manifest => _m;
        public IReadOnlyList<AvatarSkin> Skins => _m.Skins;
        public int SkinCount => _m.Skins.Count;
        public string IdleEmotion => _m.IdleEmotion;
        public string DefaultEmotion => _m.DefaultEmotion;
        public AvatarDirector Director => _m.Director;

        /// <summary>Clamp an avatar-set index (0-based) into the valid skin range.</summary>
        public int ClampSkin(int skinIndex) =>
            SkinCount == 0 ? 0 : Math.Clamp(skinIndex, 0, SkinCount - 1);

        /// <summary>Emotion for a bark line (audio-filename stem), or null if unknown.</summary>
        public string? EmotionForLine(string? lineId)
        {
            if (string.IsNullOrEmpty(lineId)) return null;
            return _m.Lines.TryGetValue(lineId, out var line) ? line.Emotion : null;
        }

        /// <summary>Fx tags (blush/hearts) declared for an emotion — parsed now, rendered later.</summary>
        public IReadOnlyList<string> FxForEmotion(string emotion) =>
            _m.Emotions.TryGetValue(emotion, out var e) ? e.Fx : Array.Empty<string>();

        /// <summary>
        /// Get the frozen portrait array for (skin, emotion). Lazy-loaded and cached. Skips files that
        /// don't exist; if the bucket ends up empty, falls back to skin 0, then to the idle emotion.
        /// Returns an empty array only if even the idle emotion has no files (degenerate manifest).
        /// </summary>
        public BitmapImage[] GetBucket(int skinIndex, string emotion)
        {
            skinIndex = ClampSkin(skinIndex);
            var key = skinIndex + ":" + emotion;
            if (_cache.TryGetValue(key, out var cached)) return cached;

            var images = LoadBucket(skinIndex, emotion);

            // Fallback 1: same emotion in skin 0.
            if (images.Length == 0 && skinIndex != 0)
                images = GetBucket(0, emotion);

            // Fallback 2: the idle emotion (guard against recursing on idle itself).
            if (images.Length == 0 && !string.Equals(emotion, _m.IdleEmotion, StringComparison.OrdinalIgnoreCase))
                images = GetBucket(skinIndex, _m.IdleEmotion);

            _cache[key] = images;
            return images;
        }

        private BitmapImage[] LoadBucket(int skinIndex, string emotion)
        {
            if (!_m.Emotions.TryGetValue(emotion, out var emo) || emo.Portraits.Count == 0)
                return Array.Empty<BitmapImage>();
            if (skinIndex < 0 || skinIndex >= _m.Skins.Count)
                return Array.Empty<BitmapImage>();

            var skinDir = Path.Combine(_baseDir, _m.Skins[skinIndex].Dir.Replace('/', Path.DirectorySeparatorChar));
            var list = new List<BitmapImage>(emo.Portraits.Count);
            foreach (var p in emo.Portraits)
            {
                if (string.IsNullOrEmpty(p.File)) continue;
                var abs = Path.Combine(skinDir, p.File);
                if (!File.Exists(abs)) continue;          // skip per-skin gaps (e.g. L3)
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(abs, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    list.Add(bmp);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "AvatarPortraitSet: failed to load portrait {Path}", abs);
                }
            }
            return list.ToArray();
        }
    }
}
