using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Video;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

public class VideoMetadataCacheTests : IDisposable
{
    private readonly string _tempDir;

    public VideoMetadataCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void Constructor_BuildsCachePath_FromEnvironment()
    {
        var env = new TestEnvironment(_tempDir);

        using var cache = new VideoMetadataCache(env);

        Assert.Equal(Path.Combine(_tempDir, "video_metadata.json"), GetCacheFilePath(cache));
    }

    [Fact]
    public void TryGetDuration_NonExistentFile_ReturnsNull()
    {
        var env = new TestEnvironment(_tempDir);

        using var cache = new VideoMetadataCache(env);

        Assert.Null(cache.TryGetDuration(Path.Combine(_tempDir, "does_not_exist.mp4")));
    }

    [Fact]
    public void TryGetDuration_MissingEntry_ReturnsNull()
    {
        var env = new TestEnvironment(_tempDir);
        var path = CreateDummyFile("dummy.mp4");

        using var cache = new VideoMetadataCache(env);

        Assert.Null(cache.TryGetDuration(path));
    }

    [Fact]
    public void SaveAndLoad_RoundTrip_PreservesCachedDuration()
    {
        var env = new TestEnvironment(_tempDir);
        var path = CreateDummyFile("dummy.mp4");

        using (var cache = new VideoMetadataCache(env))
        {
            InjectDuration(cache, path, 123.45);
            cache.Save();
        }

        using var reloaded = new VideoMetadataCache(env);
        Assert.Equal(123.45, reloaded.TryGetDuration(path));
    }

    [Fact]
    public void Dispose_PersistsDirtyCache()
    {
        var env = new TestEnvironment(_tempDir);
        var path = CreateDummyFile("dummy.mp4");

        using (var cache = new VideoMetadataCache(env))
        {
            InjectDuration(cache, path, 42.0);
        }

        using var reloaded = new VideoMetadataCache(env);
        Assert.Equal(42.0, reloaded.TryGetDuration(path));
    }

    [Fact]
    public async Task GetOrComputeDurationAsync_WithoutLibVlc_ReturnsNull()
    {
        var env = new TestEnvironment(_tempDir);
        var path = CreateDummyFile("not_a_video.txt");

        using var cache = new VideoMetadataCache(env);
        var duration = await cache.GetOrComputeDurationAsync(path);

        Assert.Null(duration);
    }

    [Fact]
    public void TryGetDuration_KeyChanges_WhenFileSizeChanges()
    {
        var env = new TestEnvironment(_tempDir);
        var path = CreateDummyFile("dummy.mp4");

        using (var cache = new VideoMetadataCache(env))
        {
            InjectDuration(cache, path, 100.0);
            cache.Save();
        }

        // Modify the file size so the composite key changes.
        File.AppendAllText(path, "extra content");

        using var reloaded = new VideoMetadataCache(env);
        Assert.Null(reloaded.TryGetDuration(path));
    }

    private string CreateDummyFile(string name)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, "dummy content");
        return path;
    }

    private static string GetCacheFilePath(VideoMetadataCache cache)
    {
        var field = typeof(VideoMetadataCache).GetField("_cacheFilePath", BindingFlags.NonPublic | BindingFlags.Instance);
        return (field?.GetValue(cache) as string)!;
    }

    private static void InjectDuration(VideoMetadataCache cache, string path, double seconds)
    {
        var byKeyField = typeof(VideoMetadataCache).GetField("_byKey", BindingFlags.NonPublic | BindingFlags.Instance);
        var dict = byKeyField?.GetValue(cache) as IDictionary<string, double>
            ?? throw new InvalidOperationException("Could not access _byKey dictionary.");

        var keyMethod = typeof(VideoMetadataCache).GetMethod("ComputeKey", BindingFlags.NonPublic | BindingFlags.Static);
        var key = keyMethod?.Invoke(null, new[] { path }) as string
            ?? throw new InvalidOperationException("Could not compute cache key.");

        dict[key] = seconds;

        var dirtyField = typeof(VideoMetadataCache).GetField("_dirty", BindingFlags.NonPublic | BindingFlags.Instance);
        dirtyField?.SetValue(cache, true);
    }

    private sealed class TestEnvironment : IAppEnvironment
    {
        public TestEnvironment(string userDataPath)
        {
            BaseDirectory = userDataPath;
            UserDataPath = userDataPath;
            ApplicationDataPath = userDataPath;
            EffectiveAssetsPath = userDataPath;
        }

        public string BaseDirectory { get; }
        public string UserDataPath { get; }
        public string ApplicationDataPath { get; }
        public string EffectiveAssetsPath { get; }
    }
}
