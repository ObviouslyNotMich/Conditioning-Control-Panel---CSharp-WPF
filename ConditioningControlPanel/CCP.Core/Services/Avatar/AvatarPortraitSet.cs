using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Core.Services.Avatar
{
    /// <summary>
    /// A loaded portrait avatar: the manifest plus lazy, cached absolute-path buckets keyed by
    /// (skin index, emotion). Missing files are skipped; an empty bucket falls back to the same
    /// emotion in skin 0, then to the idle emotion, so the avatar never shows nothing.
    /// </summary>
    public class AvatarPortraitSet : IAvatarPortraitSet
    {
        private readonly AvatarPortraitManifest _m;
        private readonly string _baseDir;
        private readonly IAppLogger? _logger;
        private readonly Dictionary<string, string[]> _cache = new();

        public AvatarPortraitSet(AvatarPortraitManifest manifest, string baseDir, IAppLogger? logger = null)
        {
            _m = manifest;
            _baseDir = baseDir;
            _logger = logger;
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

        /// <summary>
        /// Fallback emotion for a bark whose stem has no explicit <c>lines{}</c> override: map its
        /// free-text <c>mood</c> to a portrait emotion. Compound moods are split on comma and the
        /// FIRST recognized token wins; anything unrecognized defaults to <c>neutral</c>.
        /// </summary>
        public string EmotionForMood(string? mood)
        {
            if (string.IsNullOrWhiteSpace(mood)) return "neutral";
            foreach (var raw in mood.Split(','))
            {
                var token = raw.Trim().ToLowerInvariant();
                if (token.Length == 0) continue;
                if (MoodEmotionMap.TryGetValue(token, out var emo)) return emo;
            }
            return "neutral";
        }

        /// <summary>
        /// mood-token → portrait-emotion lookup. Includes the emotion names themselves (self-map) plus
        /// the descriptive mood words the bark manifests actually use.
        /// </summary>
        private static readonly Dictionary<string, string> MoodEmotionMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["neutral"] = "neutral",
            ["happy"] = "happy",
            ["excited"] = "excited",
            ["praise"] = "praise",
            ["affectionate"] = "affectionate",
            ["teasing"] = "teasing",
            ["giggly"] = "giggly",
            ["wink"] = "wink",
            ["alluring"] = "alluring",
            ["inviting"] = "inviting",
            ["entrancing"] = "entrancing",
            ["dreamy"] = "dreamy",
            ["shy"] = "shy",
            ["tender"] = "tender",
            ["adoring"] = "adoring",
            ["surprised"] = "surprised",
            ["curious"] = "curious",
            ["greeting"] = "greeting",
            ["pleased"] = "pleased",
            ["overwhelmed"] = "overwhelmed",

            ["soft"] = "tender",
            ["possessive"] = "adoring",
            ["knowing"] = "teasing",
            ["warm"] = "affectionate",
            ["playful"] = "giggly",
            ["vain"] = "wink",
            ["amused"] = "giggly",
            ["indulgent"] = "pleased",
            ["patient"] = "tender",
            ["intimate"] = "alluring",
            ["deepening"] = "entrancing",
            ["affirming"] = "praise",
            ["smug"] = "wink",
            ["fond"] = "adoring",
            ["reverent"] = "adoring",
            ["afterglow"] = "dreamy",
            ["seductive"] = "alluring",
            ["proud"] = "praise",
            ["dominant"] = "adoring",
        };

        /// <summary>Fx tags (blush/hearts) declared for an emotion.</summary>
        public IReadOnlyList<string> FxForEmotion(string emotion) =>
            _m.Emotions.TryGetValue(emotion, out var e) ? e.Fx : Array.Empty<string>();

        /// <summary>
        /// Get the absolute portrait paths for (skin, emotion). Lazy-loaded and cached. Skips files
        /// that don't exist; if the bucket ends up empty, falls back to skin 0, then to the idle emotion.
        /// </summary>
        public IReadOnlyList<string> GetBucketPaths(int skinIndex, string emotion)
        {
            skinIndex = ClampSkin(skinIndex);
            var key = skinIndex + ":" + emotion;
            if (_cache.TryGetValue(key, out var cached)) return cached;

            var paths = LoadBucketPaths(skinIndex, emotion);

            // Fallback 1: same emotion in skin 0.
            if (paths.Length == 0 && skinIndex != 0)
                paths = (string[])GetBucketPaths(0, emotion);

            // Fallback 2: the idle emotion (guard against recursing on idle itself).
            if (paths.Length == 0 && !string.Equals(emotion, _m.IdleEmotion, StringComparison.OrdinalIgnoreCase))
                paths = (string[])GetBucketPaths(skinIndex, _m.IdleEmotion);

            _cache[key] = paths;
            return paths;
        }

        private string[] LoadBucketPaths(int skinIndex, string emotion)
        {
            if (!_m.Emotions.TryGetValue(emotion, out var emo) || emo.Portraits.Count == 0)
                return Array.Empty<string>();
            if (skinIndex < 0 || skinIndex >= _m.Skins.Count)
                return Array.Empty<string>();

            var skinDir = Path.Combine(_baseDir, _m.Skins[skinIndex].Dir.Replace('/', Path.DirectorySeparatorChar));
            var list = new List<string>(emo.Portraits.Count);
            foreach (var p in emo.Portraits)
            {
                if (string.IsNullOrEmpty(p.File)) continue;
                var abs = Path.Combine(skinDir, p.File);
                if (!File.Exists(abs)) continue;
                list.Add(abs);
            }
            return list.ToArray();
        }
    }
}
