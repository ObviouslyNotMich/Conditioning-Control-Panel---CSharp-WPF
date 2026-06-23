using System;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Services.Mod;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Helpers;

/// <summary>
/// Static helper for loading bitmaps in Avalonia code-behind. Routes
/// <c>Resources/</c> requests through <see cref="AvaloniaModResourceResolver"/>
/// so mod overrides are respected, and falls back to the Avalonia asset loader
/// when the resolver is unavailable.
/// </summary>
public static class AvaloniaBitmapHelper
{
    /// <summary>
    /// Loads a bitmap from a file path, <c>file://</c>, <c>pack://</c>,
    /// <c>avares://</c> or a raw <c>Resources/</c> relative path.
    /// Returns <c>null</c> if the asset cannot be found or loaded.
    /// </summary>
    public static Bitmap? Load(string? uriOrPath)
    {
        if (string.IsNullOrWhiteSpace(uriOrPath)) return null;

        var resolver = App.Services?.GetService<AvaloniaModResourceResolver>();
        if (resolver != null)
        {
            var resolved = resolver.LoadFromUriOrPath(uriOrPath);
            if (resolved != null) return resolved;
        }

        // Fallback when the resolver has not been registered yet.
        return LoadFallback(uriOrPath);
    }

    /// <summary>
    /// Loads a bitmap for a relative <c>Resources/</c> path (e.g. "features/flash.png"),
    /// applying the active mod's override if present.
    /// </summary>
    public static Bitmap? LoadResource(string? resourcePath)
    {
        if (string.IsNullOrWhiteSpace(resourcePath)) return null;

        var resolver = App.Services?.GetService<AvaloniaModResourceResolver>();
        if (resolver != null)
            return resolver.ResolveBitmap(resourcePath);

        return LoadFallback($"avares://CCP.Avalonia/Assets/{resourcePath.Replace('\\', '/')}");
    }

    private static Bitmap? LoadFallback(string uriOrPath)
    {
        try
        {
            if (File.Exists(uriOrPath))
                return new Bitmap(uriOrPath);

            if (uriOrPath.StartsWith("file://", StringComparison.Ordinal))
            {
                var path = uriOrPath.Substring(7);
                if (File.Exists(path))
                    return new Bitmap(path);
                return null;
            }

            var assetUri = uriOrPath.StartsWith("pack://application:,,,/Resources/", StringComparison.OrdinalIgnoreCase)
                ? $"avares://CCP.Avalonia/Assets/{uriOrPath.Substring("pack://application:,,,/Resources/".Length)}"
                : uriOrPath;

            if (assetUri.StartsWith("avares://", StringComparison.Ordinal))
            {
                using var stream = AssetLoader.Open(new Uri(assetUri));
                return new Bitmap(stream);
            }
        }
        catch
        {
            // Fail-soft for missing or unsupported assets.
        }

        return null;
    }
}
