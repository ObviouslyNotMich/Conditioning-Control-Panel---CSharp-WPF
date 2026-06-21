using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Core.Services.Avatar;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Avalonia.Services.Avatar
{
    /// <summary>
    /// Avalonia-head resolver for a mod's emotive-portrait avatar manifest (<c>avatar_manifest.json</c>).
    /// Mirrors the legacy WPF loader's two-tier path resolution: packaged mod first, then built-in per-mod folder.
    /// </summary>
    public sealed class AvaloniaAvatarPortraitService : IAvatarPortraitService
    {
        public const string ManifestFileName = "avatar_manifest.json";

        private readonly IModService _modService;
        private readonly IAppLogger? _logger;

        public AvaloniaAvatarPortraitService(IModService modService, IAppLogger? logger = null)
        {
            _modService = modService;
            _logger = logger;
        }

        /// <summary>True if the active mod ships a portrait manifest (cheap existence check, no parse).</summary>
        public bool HasManifestForActiveMod() => ResolveManifestPath() != null;

        /// <summary>
        /// Load + parse the active mod's manifest into an <see cref="AvatarPortraitSet"/>. Never throws —
        /// a missing/garbled manifest returns null and the caller uses the legacy avatar path.
        /// </summary>
        public IAvatarPortraitSet? Load()
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
                    _logger?.Warning("AvaloniaAvatarPortraitService: manifest at {Path} is empty/invalid", path);
                    return null;
                }
                var baseDir = Path.GetDirectoryName(path) ?? "";
                _logger?.Information(
                    "AvaloniaAvatarPortraitService: loaded {Emotions} emotions, {Skins} skins, {Lines} lines from {Path}",
                    manifest.Emotions.Count, manifest.Skins.Count, manifest.Lines.Count, path);
                return new AvatarPortraitSet(manifest, baseDir, _logger);
            }
            catch (Exception ex)
            {
                _logger?.Warning(ex, "AvaloniaAvatarPortraitService: failed to load manifest at {Path}", path);
                return null;
            }
        }

        private string? ResolveManifestPath() => ActiveModManifestPath ?? EmbeddedModManifestPath;

        private string? ActiveModManifestPath
        {
            get
            {
                var modPath = _modService.ActiveMod.InstalledPath;
                if (string.IsNullOrEmpty(modPath)) return null;
                var p = Path.Combine(modPath, "resources", "sounds", "companion_audio", ManifestFileName);
                return File.Exists(p) ? p : null;
            }
        }

        private string? EmbeddedModManifestPath
        {
            get
            {
                var modId = _modService.ActiveMod.Id;
                if (string.IsNullOrEmpty(modId)) return null;
                var p = Path.Combine(CompanionPhrase.DefaultAudioFolder, "mods", modId, ManifestFileName);
                return File.Exists(p) ? p : null;
            }
        }
    }
}
