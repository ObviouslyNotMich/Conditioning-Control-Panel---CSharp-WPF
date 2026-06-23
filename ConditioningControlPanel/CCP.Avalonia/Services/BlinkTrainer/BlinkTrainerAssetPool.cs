using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Avalonia.Services.BlinkTrainer;

/// <summary>
/// Flat shuffled-on-demand pool of media paths drawn from a user-selected list of folders.
/// Mirrors the WPF <c>BlinkTrainerAssetPool</c> without taking a dependency on the WPF
/// <c>AssetPack</c> helper.
/// </summary>
public sealed class BlinkTrainerAssetPool
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp",
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mov", ".avi", ".mkv",
    };

    private readonly List<string> _paths;

    public int ImageCount { get; }
    public int VideoCount { get; }
    public bool IsEmpty => _paths.Count == 0;
    public IReadOnlyList<string> Paths => _paths;

    private BlinkTrainerAssetPool(List<string> paths, int imageCount, int videoCount)
    {
        _paths = paths;
        ImageCount = imageCount;
        VideoCount = videoCount;
    }

    /// <summary>
    /// Builds a pool from the given folder list. Each folder is enumerated directly plus
    /// one level of immediate subfolders. Missing or empty folders are skipped silently.
    /// </summary>
    public static BlinkTrainerAssetPool Build(IReadOnlyList<string>? folders, bool includeVideos)
    {
        var paths = new List<string>();
        int images = 0;
        int videos = 0;

        if (folders != null)
        {
            foreach (var folder in folders)
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;

                CollectMedia(folder, paths, out var i, out var v);
                images += i;
                videos += v;

                try
                {
                    foreach (var sub in Directory.EnumerateDirectories(folder))
                    {
                        CollectMedia(sub, paths, out var si, out var sv);
                        images += si;
                        videos += sv;
                    }
                }
                catch (Exception ex)
                {
                    // best effort; a locked subfolder should not kill the whole pool
                    System.Diagnostics.Debug.WriteLine($"BlinkTrainerAssetPool: subfolder scan failed for {folder}: {ex.Message}");
                }
            }
        }

        if (includeVideos)
            return new BlinkTrainerAssetPool(paths, images, videos);

        var onlyImages = paths.Where(p => !IsVideo(p)).ToList();
        return new BlinkTrainerAssetPool(onlyImages, images, 0);
    }

    private static void CollectMedia(string dir, List<string> paths, out int images, out int videos)
    {
        images = 0;
        videos = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var ext = Path.GetExtension(file);
                if (string.IsNullOrEmpty(ext)) continue;

                if (ImageExtensions.Contains(ext))
                {
                    paths.Add(file);
                    images++;
                }
                else if (VideoExtensions.Contains(ext))
                {
                    paths.Add(file);
                    videos++;
                }
            }
        }
        catch
        {
            // skip unreadable folders
        }
    }

    /// <summary>
    /// Returns a random path from the pool, avoiding the same path as
    /// <paramref name="lastPickedPath"/> when the pool has more than one entry.
    /// Returns null when the pool is empty.
    /// </summary>
    public string? PickRandom(string? lastPickedPath = null)
    {
        if (_paths.Count == 0) return null;
        if (_paths.Count == 1) return _paths[0];

        int idx = Random.Shared.Next(_paths.Count);
        var chosen = _paths[idx];
        if (!string.IsNullOrEmpty(lastPickedPath)
            && string.Equals(chosen, lastPickedPath, StringComparison.OrdinalIgnoreCase))
        {
            idx = (idx + 1) % _paths.Count;
            chosen = _paths[idx];
        }
        return chosen;
    }

    /// <summary>True if the given path's extension marks it as a supported video.</summary>
    public static bool IsVideo(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && VideoExtensions.Contains(ext);
    }
}
