using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Fine-grained app/window classification for the awareness-gated bark rules: maps a window-title
    /// substring to an <c>app_cluster</c> id (game_competitive, site_doomscroll, …) or, for bespoke
    /// single titles, to an <c>app</c> id (hades, obs, discord). This is a layer ON TOP of
    /// <see cref="WindowAwarenessService"/>'s broad <see cref="ActivityCategory"/>.
    ///
    /// The table is data-driven so it can be extended WITHOUT touching bark logic: drop an
    /// <c>app_clusters.json</c> into the companion-audio resource folder (it auto-deploys with the
    /// other Resources\sounds content). When that file is present it is authoritative; otherwise the
    /// embedded defaults below are used. Matching is case-insensitive, longest-substring-wins, with
    /// bespoke <c>apps</c> taking precedence over <c>clusters</c>.
    ///
    /// Privacy: only the resolved id is ever surfaced — the raw window title is never stored or logged
    /// (consistent with WindowAwarenessService). All of this is reached only while AwarenessMode is on,
    /// because that toggle is what runs WindowAwarenessService at all.
    /// </summary>
    public static class AppClusterMap
    {
        public const string FileName = "app_clusters.json";

        // id -> lowercased title substrings. Insertion order is irrelevant (longest match wins).
        private static Dictionary<string, string[]> _clusters = DefaultClusters;
        private static Dictionary<string, string[]> _apps = DefaultApps;
        private static bool _loaded;

        private static string FilePath =>
            Path.Combine(CompanionPhraseService.CompanionAudioFolder, FileName);

        /// <summary>Load the external override once (if present). Falls back to embedded defaults on any error.</summary>
        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                var path = FilePath;
                if (!File.Exists(path)) return; // keep embedded defaults
                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return;
                var root = JObject.Parse(json);
                var clusters = ParseSection(root["clusters"] as JObject);
                var apps = ParseSection(root["apps"] as JObject);
                if (clusters.Count > 0) _clusters = clusters;
                if (apps.Count > 0) _apps = apps;
                App.Logger?.Information("AppClusterMap: loaded {Clusters} clusters, {Apps} bespoke apps from {Path}",
                    _clusters.Count, _apps.Count, path);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AppClusterMap: failed to load override — using embedded defaults");
            }
        }

        private static Dictionary<string, string[]> ParseSection(JObject? section)
        {
            var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (section == null) return map;
            foreach (var prop in section.Properties())
            {
                var arr = (prop.Value as JArray)?.Select(t => (t?.ToString() ?? "").ToLowerInvariant())
                              .Where(s => s.Length > 0).ToArray();
                if (arr is { Length: > 0 }) map[prop.Name] = arr;
            }
            return map;
        }

        /// <summary>
        /// Classify a raw window title into (cluster, app) ids. Either may be null. Bespoke apps win over
        /// clusters; within each, the longest matching substring wins (so "youtube music" beats "youtube").
        /// </summary>
        public static (string? cluster, string? app) Classify(string? windowTitle)
        {
            if (string.IsNullOrWhiteSpace(windowTitle)) return (null, null);
            EnsureLoaded();
            var t = windowTitle.ToLowerInvariant();
            string? app = BestMatch(t, _apps);
            string? cluster = BestMatch(t, _clusters);
            return (cluster, app);
        }

        /// <summary>Id whose longest substring is contained in <paramref name="title"/>, or null.</summary>
        private static string? BestMatch(string title, Dictionary<string, string[]> table)
        {
            string? bestId = null;
            int bestLen = 0;
            foreach (var kvp in table)
                foreach (var needle in kvp.Value)
                    if (needle.Length > bestLen && title.Contains(needle))
                    {
                        bestLen = needle.Length;
                        bestId = kvp.Key;
                    }
            return bestId;
        }

        // ----- embedded defaults (mirrored by the shipped app_clusters.json) -----

        private static readonly Dictionary<string, string[]> DefaultClusters = new(StringComparer.OrdinalIgnoreCase)
        {
            ["game_competitive"] = new[] { "valorant", "league of legends", "counter-strike", "cs2", "csgo",
                "overwatch", "apex legends", "rainbow six", "dota 2", "rocket league", "fortnite", "call of duty", "warzone" },
            ["game_cozy"] = new[] { "stardew valley", "animal crossing", "minecraft", "terraria", "the sims",
                "cozy grove", "spiritfarer", "unpacking", "powerwash" },
            ["game_rpg"] = new[] { "elden ring", "baldur's gate", "skyrim", "the witcher", "cyberpunk",
                "final fantasy", "persona", "dark souls", "fallout", "diablo", "path of exile" },
            ["game_gacha"] = new[] { "genshin impact", "honkai", "star rail", "fate/grand", "arknights",
                "blue archive", "nikke", "wuthering waves", "zenless" },
            ["game_mmo"] = new[] { "world of warcraft", "final fantasy xiv", "ffxiv", "lost ark",
                "guild wars", "new world", "runescape", "black desert" },
            ["game_social_vr"] = new[] { "vrchat", "chilloutvr", "resonite", "rec room", "neos" },
            ["site_doomscroll"] = new[] { "twitter", "x.com", "reddit", "tiktok", "tumblr", "facebook",
                "instagram", "threads", "bluesky" },
            ["site_video"] = new[] { "youtube", "netflix", "twitch", "hulu", "disney+", "hbo max",
                "crunchyroll", "prime video" },
            ["site_music"] = new[] { "spotify", "soundcloud", "apple music", "youtube music" },
            ["site_shopping"] = new[] { "amazon", "ebay", "etsy", "aliexpress", "shein", "throne", "wishtender", "wish.com" },
            ["site_eh"] = new[] { "pornhub", "xvideos", "xhamster", "e-hentai", "nhentai", "rule34",
                "hypnotube", "bambicloud", "adult content" },
        };

        private static readonly Dictionary<string, string[]> DefaultApps = new(StringComparer.OrdinalIgnoreCase)
        {
            ["hades"] = new[] { "hades" },
            ["obs"] = new[] { "obs studio", "obs " },
            ["discord"] = new[] { "discord" },
        };
    }
}
