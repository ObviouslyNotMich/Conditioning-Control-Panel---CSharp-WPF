using System;
using System.IO;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Services.Mod;

/// <summary>
/// Avalonia equivalent of the WPF <c>ModResourceResolver</c>.
/// Resolves a relative <c>Resources/</c> path by checking the active mod's
/// <c>resources/</c> folder first, then falling back to the embedded Avalonia
/// assets. Also exposes helpers for loading legacy <c>pack://</c>,
/// <c>avares://</c> and plain file paths.
/// </summary>
public sealed class AvaloniaModResourceResolver : IDisposable
{
    private readonly IModService _modService;
    private readonly IAssetLoader _assetLoader;
    private readonly ILogger<AvaloniaModResourceResolver>? _logger;
    private bool _disposed;

    public AvaloniaModResourceResolver(IModService modService, IAssetLoader assetLoader, ILogger<AvaloniaModResourceResolver>? logger = null)
    {
        _modService = modService ?? throw new ArgumentNullException(nameof(modService));
        _assetLoader = assetLoader ?? throw new ArgumentNullException(nameof(assetLoader));
        _logger = logger;
        _modService.ActiveModChanged += OnActiveModChanged;
    }

    /// <summary>
    /// Raised when the active mod changes and any cached mod-aware bitmaps
    /// should be re-resolved.
    /// </summary>
    public event EventHandler? ResourcesChanged;

    /// <summary>
    /// Resolves a relative path inside <c>Resources/</c> (e.g. "features/flash.png")
    /// to a <see cref="Bitmap"/>, preferring the active mod's override.
    /// </summary>
    public Bitmap? ResolveBitmap(string resourcePath)
    {
        if (string.IsNullOrWhiteSpace(resourcePath)) return null;
        resourcePath = Normalize(resourcePath);

        try
        {
            var modPath = _modService.ActiveMod?.InstalledPath;
            if (!string.IsNullOrEmpty(modPath))
            {
                var overridePath = Path.Combine(modPath, "resources", resourcePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(overridePath))
                {
                    _logger?.LogDebug("ModResourceResolver: loaded override {Path}", overridePath);
                    return LoadBitmapFromFile(overridePath);
                }
            }

            var avares = ToAvaresUri(resourcePath);
            if (_assetLoader.Exists(avares))
            {
                using var stream = _assetLoader.Open(avares);
                return new Bitmap(stream);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "ModResourceResolver: failed to resolve {Path}", resourcePath);
        }

        return null;
    }

    /// <summary>
    /// Resolves a relative <c>Resources/</c> path to a loadable URI string.
    /// Returns a <c>file://</c> URI for mod overrides and an
    /// <c>avares://CCP.Avalonia/Assets/</c> URI for embedded assets.
    /// </summary>
    public string ResolveUri(string resourcePath)
    {
        if (string.IsNullOrWhiteSpace(resourcePath)) return "";
        resourcePath = Normalize(resourcePath);

        var modPath = _modService.ActiveMod?.InstalledPath;
        if (!string.IsNullOrEmpty(modPath))
        {
            var overridePath = Path.Combine(modPath, "resources", resourcePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(overridePath))
                return new Uri(overridePath, UriKind.Absolute).AbsoluteUri;
        }

        return ToAvaresString(resourcePath);
    }

    /// <summary>
    /// Loads a bitmap from a legacy URI/path, applying mod override resolution
    /// when the value points into <c>Resources/</c>.
    /// Handles <c>file://</c>, plain files, <c>pack://application:,,,/Resources/</c>
    /// and <c>avares://CCP.Avalonia/Assets/</c>.
    /// </summary>
    public Bitmap? LoadFromUriOrPath(string? uriOrPath)
    {
        if (string.IsNullOrWhiteSpace(uriOrPath)) return null;

        try
        {
            // Plain file path.
            if (File.Exists(uriOrPath))
                return LoadBitmapFromFile(uriOrPath);

            // file:// URI.
            if (uriOrPath.StartsWith("file://", StringComparison.Ordinal))
            {
                var filePath = uriOrPath.Substring(7);
                if (File.Exists(filePath))
                    return LoadBitmapFromFile(filePath);
                return null;
            }

            // pack://application:,,,/Resources/... -> resolve mod override then embedded assets.
            if (uriOrPath.StartsWith("pack://application:,,,", StringComparison.Ordinal))
            {
                var relative = uriOrPath.Substring("pack://application:,,,".Length).TrimStart('/');
                if (relative.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase))
                    relative = relative.Substring("Resources/".Length);
                return ResolveBitmap(relative);
            }

            // avares://CCP.Avalonia/Assets/... -> resolve mod override then embedded assets.
            if (uriOrPath.StartsWith("avares://CCP.Avalonia/Assets/", StringComparison.OrdinalIgnoreCase))
            {
                var relative = uriOrPath.Substring("avares://CCP.Avalonia/Assets/".Length);
                return ResolveBitmap(relative);
            }

            // Any other avares:// URI.
            if (uriOrPath.StartsWith("avares://", StringComparison.Ordinal))
            {
                var uri = new Uri(uriOrPath, UriKind.Absolute);
                if (_assetLoader.Exists(uri))
                {
                    using var stream = _assetLoader.Open(uri);
                    return new Bitmap(stream);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "ModResourceResolver: failed to load {Uri}", uriOrPath);
        }

        return null;
    }

    /// <summary>
    /// Checks whether the active mod has a resource override for the given path.
    /// </summary>
    public bool HasModOverride(string resourcePath)
    {
        var modPath = _modService.ActiveMod?.InstalledPath;
        if (string.IsNullOrEmpty(modPath)) return false;

        resourcePath = Normalize(resourcePath);
        var overridePath = Path.Combine(modPath, "resources", resourcePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(overridePath);
    }

    /// <summary>
    /// Resolves a sound file path. Prefers the active mod's <c>resources/sounds/</c>
    /// override, then falls back to the embedded <c>Resources/sounds/</c> copy in the
    /// output directory. Automatically tries the alternate extension (.wav ↔ .mp3)
    /// so mods and embedded assets can use either format.
    /// </summary>
    /// <param name="soundRelativePath">
    /// Relative path within <c>sounds/</c>, e.g. "giggle5.mp3" or "bubbles/Pop.mp3".
    /// </param>
    /// <returns>Absolute file path, or <c>null</c> if no matching file exists.</returns>
    public string? ResolveAudioPath(string soundRelativePath)
    {
        if (string.IsNullOrWhiteSpace(soundRelativePath)) return null;

        var relative = Normalize(soundRelativePath);
        if (relative.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return null;

        string? TryExtensions(string basePath)
        {
            if (File.Exists(basePath)) return basePath;

            var ext = Path.GetExtension(basePath).ToLowerInvariant();
            var altExt = ext == ".mp3" ? ".wav" : ".mp3";
            var alt = Path.ChangeExtension(basePath, altExt);
            return File.Exists(alt) ? alt : null;
        }

        // Active mod override first.
        var modPath = _modService.ActiveMod?.InstalledPath;
        if (!string.IsNullOrEmpty(modPath))
        {
            var modSoundsDir = Path.Combine(modPath, "resources", "sounds");
            var modPathFull = Path.Combine(modSoundsDir, relative.Replace('/', Path.DirectorySeparatorChar));
            var found = TryExtensions(modPathFull);
            if (found != null) return found;
        }

        // Fallback to the embedded Resources/sounds copy next to the executable.
        var fallback = Path.Combine(AppContext.BaseDirectory, "Resources", "sounds", relative.Replace('/', Path.DirectorySeparatorChar));
        return TryExtensions(fallback);
    }

    private void OnActiveModChanged(object? sender, ModPackage e)
    {
        ResourcesChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Bitmap LoadBitmapFromFile(string path)
    {
        // Open a copy stream so the file is not locked by the bitmap.
        using var fs = File.OpenRead(path);
        var ms = new MemoryStream((int)fs.Length);
        fs.CopyTo(ms);
        ms.Position = 0;
        return new Bitmap(ms);
    }

    private static Uri ToAvaresUri(string resourcePath)
        => new($"avares://CCP.Avalonia/Assets/{resourcePath}", UriKind.Absolute);

    private static string ToAvaresString(string resourcePath)
        => $"avares://CCP.Avalonia/Assets/{resourcePath}";

    private static string Normalize(string resourcePath)
        => resourcePath.Replace('\\', '/').TrimStart('/');

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _modService.ActiveModChanged -= OnActiveModChanged;
    }
}
