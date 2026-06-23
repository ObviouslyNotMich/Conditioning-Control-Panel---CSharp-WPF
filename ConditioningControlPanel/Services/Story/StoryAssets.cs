using System;
using System.IO;

namespace ConditioningControlPanel.Services.Story
{
    /// <summary>
    /// Resolves story-relative asset references (as written by the Python VN editor into
    /// <c>opening.json</c>) to absolute paths under the app's bundled <c>assets/story/</c> tree.
    /// Backgrounds and character art are authored with CCP-Trailer-relative or Desktop-absolute
    /// paths; in-app we resolve them by BASENAME under <c>backgrounds/</c> / <c>characters/</c>,
    /// with a fallback to the existing Chaos backdrop art (so scenes that are already chaos
    /// backdrops — e.g. <c>dollhouse_background.png</c> — need not be duplicated).
    /// </summary>
    public static class StoryAssets
    {
        public static string Root => Path.Combine(AppContext.BaseDirectory, "assets", "story");
        public static string OpeningJson => Path.Combine(Root, "opening.json");

        private static string BaseName(string p) => Path.GetFileName(p.Replace('\\', '/'));

        /// <summary>Resolve a beat <c>background</c> to an absolute file path, or null if not found.</summary>
        public static string? ResolveBackground(string? authored)
        {
            if (string.IsNullOrWhiteSpace(authored)) return null;
            var name = BaseName(authored);
            var local = Path.Combine(Root, "backgrounds", name);
            if (File.Exists(local)) return local;
            // Fallback: an existing Chaos backdrop plate of the same stem.
            var stem = Path.GetFileNameWithoutExtension(name);
            var chaos = Services.Chaos.ChaosArt.PathFor("backdrops", stem);
            if (chaos != null && File.Exists(chaos)) return chaos;
            return File.Exists(local) ? local : null;
        }

        /// <summary>Resolve a character <c>image</c> to an absolute file path, or null if not found.</summary>
        public static string? ResolveCharacter(string? authored)
        {
            if (string.IsNullOrWhiteSpace(authored)) return null;
            var local = Path.Combine(Root, "characters", BaseName(authored));
            return File.Exists(local) ? local : null;
        }

        /// <summary>Resolve a session <c>song</c>/<c>envelope</c> (a path relative to the story root).</summary>
        public static string ResolveStoryPath(string relative)
        {
            if (Path.IsPathRooted(relative)) return relative;
            return Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
