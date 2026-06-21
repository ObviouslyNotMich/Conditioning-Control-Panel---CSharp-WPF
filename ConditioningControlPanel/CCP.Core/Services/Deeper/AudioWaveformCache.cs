using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Core.Services.Deeper;

/// <summary>
/// Disk-cached waveform summary for local audio files. Delegates actual decoding to
/// <see cref="IAudioWaveformProvider"/> so the Core project stays free of platform-specific
/// audio libraries.
/// </summary>
public sealed class AudioWaveformCache
{
    private const string CacheMagic = "DPK2";

    private readonly IAppEnvironment _environment;
    private readonly IAudioWaveformProvider _provider;

    public AudioWaveformCache(IAppEnvironment environment, IAudioWaveformProvider provider)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public string CacheFolder => Path.Combine(_environment.UserDataPath, "deeper-cache");

    /// <summary>
    /// Loads peaks for the given audio file, using a disk cache when available.
    /// Returns a flat result when the file is missing or the provider cannot decode it.
    /// </summary>
    public Task<AudioWaveformResult> LoadAsync(string audioPath, CancellationToken ct = default)
        => Task.Run(() => Load(audioPath, ct), ct);

    public AudioWaveformResult Load(string audioPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
            return FlatResult();

        try { Directory.CreateDirectory(CacheFolder); } catch { /* best-effort */ }

        var cacheKey = ComputeCacheKey(audioPath);
        var cachePath = Path.Combine(CacheFolder, cacheKey + ".peaks");

        if (File.Exists(cachePath))
        {
            try
            {
                var cached = ReadCache(cachePath);
                if (cached != null) return cached;
            }
            catch (Exception)
            {
                // Ignore stale/broken cache and re-decode.
            }
        }

        if (!_provider.CanDecode(audioPath))
            return FlatResult();

        try
        {
            var fresh = _provider.DecodeAsync(audioPath, ct).GetAwaiter().GetResult();
            try { WriteCache(cachePath, fresh); }
            catch { /* best-effort */ }
            return fresh;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return FlatResult();
        }
    }

    private static AudioWaveformResult FlatResult()
        => new() { Peaks = new float[64], DurationSeconds = 0 };

    private static string ComputeCacheKey(string audioPath)
    {
        var info = new FileInfo(audioPath);
        var key = $"{audioPath.ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static AudioWaveformResult? ReadCache(string cachePath)
    {
        using var fs = File.OpenRead(cachePath);
        using var br = new BinaryReader(fs);
        var magic = new string(br.ReadChars(4));
        if (magic != CacheMagic) return null;
        var duration = br.ReadDouble();
        var count = br.ReadInt32();
        if (count <= 0 || count > 16384) return null;
        var peaks = new float[count];
        for (int i = 0; i < count; i++) peaks[i] = br.ReadSingle();
        return new AudioWaveformResult { Peaks = peaks, DurationSeconds = duration };
    }

    private static void WriteCache(string cachePath, AudioWaveformResult result)
    {
        using var fs = File.Create(cachePath);
        using var bw = new BinaryWriter(fs);
        bw.Write(CacheMagic.ToCharArray());
        bw.Write(result.DurationSeconds);
        bw.Write(result.Peaks.Length);
        foreach (var p in result.Peaks) bw.Write(p);
    }
}
