using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// Loads the user-editable window-title keyword lists used by <see cref="WindowAwarenessService"/>.
    /// The file lives next to the companion audio resources so users and mods can extend it without
    /// changing code.
    /// </summary>
    public static class AwarenessAppListLoader
    {
        public const string FileName = "awareness_apps.json";

        public static string FilePath =>
            Path.Combine(CompanionPhraseService.CompanionAudioFolder, FileName);

        private static readonly object Lock = new();
        private static Dictionary<string, Dictionary<string, string>>? _categories;

        /// <summary>
        /// Returns the keyword -> display-name map for a category (gaming, social, shopping,
        /// media, learning, working). Empty if the file is missing or the category is unknown.
        /// </summary>
        public static IReadOnlyDictionary<string, string> GetCategoryApps(string category)
        {
            EnsureLoaded();
            if (_categories != null && _categories.TryGetValue(category, out var dict))
                return dict;

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static void EnsureLoaded()
        {
            if (_categories != null) return;

            lock (Lock)
            {
                if (_categories != null) return;

                var loaded = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

                try
                {
                    var path = FilePath;
                    if (File.Exists(path))
                    {
                        var json = File.ReadAllText(path);
                        var root = JsonConvert.DeserializeObject<JObject>(json);
                        var section = root?["categories"] as JObject ?? root;

                        if (section != null)
                        {
                            foreach (var prop in section.Properties())
                            {
                                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                if (prop.Value is JObject obj)
                                {
                                    foreach (var entry in obj.Properties())
                                        map[entry.Name] = entry.Value?.ToString() ?? entry.Name;
                                }

                                loaded[prop.Name] = map;
                            }
                        }

                        App.Logger?.Information(
                            "AwarenessAppListLoader: loaded {Count} categories from {Path}",
                            loaded.Count, path);
                    }
                    else
                    {
                        App.Logger?.Warning(
                            "AwarenessAppListLoader: {File} not found; awareness category lists will be empty",
                            path);
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "AwarenessAppListLoader: failed to load {File}", FilePath);
                }

                _categories = loaded;
            }
        }
    }
}
