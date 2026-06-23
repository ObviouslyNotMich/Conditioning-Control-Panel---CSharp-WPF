using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models;

/// <summary>
/// Server-controlled packs manifest. Allows enabling/disabling packs without an app update.
/// </summary>
public class PacksManifest
{
    [JsonProperty("version")]
    public string Version { get; set; } = "1.0";

    [JsonProperty("packs")]
    public List<ContentPack> Packs { get; set; } = new();
}

/// <summary>
/// Manifest for an installed pack (stored encrypted locally).
/// </summary>
public class InstalledPackManifest
{
    public string PackId { get; set; } = "";
    public string PackGuid { get; set; } = "";
    public string PackName { get; set; } = "";
    public DateTime InstalledDate { get; set; }
    public List<PackFileEntry> Files { get; set; } = new();
}

/// <summary>
/// Response from POST /pack/download-url endpoint.
/// </summary>
public class PackDownloadUrlResponse
{
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("downloadUrl")]
    public string? DownloadUrl { get; set; }

    [JsonProperty("packId")]
    public string? PackId { get; set; }

    [JsonProperty("packName")]
    public string? PackName { get; set; }

    [JsonProperty("sizeBytes")]
    public long SizeBytes { get; set; }

    [JsonProperty("expiresIn")]
    public int ExpiresIn { get; set; }

    [JsonProperty("rateLimit")]
    public PackRateLimitInfo? RateLimit { get; set; }
}

/// <summary>
/// Rate limit info from a successful download URL response.
/// </summary>
public class PackRateLimitInfo
{
    [JsonProperty("remaining")]
    public int Remaining { get; set; }

    [JsonProperty("limit")]
    public int Limit { get; set; }

    [JsonProperty("resetTime")]
    public string? ResetTime { get; set; }
}

/// <summary>
/// Error response from pack download endpoints.
/// </summary>
public class PackDownloadErrorResponse
{
    [JsonProperty("error")]
    public string? Error { get; set; }

    [JsonProperty("message")]
    public string? Message { get; set; }

    [JsonProperty("resetTime")]
    public string? ResetTime { get; set; }

    [JsonProperty("remaining")]
    public int Remaining { get; set; }
}

/// <summary>
/// Response from GET /pack/status endpoint.
/// </summary>
public class PackStatusResponse
{
    [JsonProperty("userId")]
    public string? UserId { get; set; }

    [JsonProperty("packs")]
    public Dictionary<string, PackDownloadStatus>? Packs { get; set; }

    [JsonProperty("dailyLimit")]
    public int DailyLimit { get; set; }
}

/// <summary>
/// Download status for a single pack.
/// </summary>
public class PackDownloadStatus
{
    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("sizeBytes")]
    public long SizeBytes { get; set; }

    [JsonProperty("canDownload")]
    public bool CanDownload { get; set; }

    [JsonProperty("downloadsRemaining")]
    public int DownloadsRemaining { get; set; }

    [JsonProperty("downloadsUsed")]
    public int DownloadsUsed { get; set; }

    [JsonProperty("resetTime")]
    public string? ResetTime { get; set; }
}

/// <summary>
/// Exception thrown when a pack download rate limit is exceeded.
/// </summary>
public class PackRateLimitException : Exception
{
    public DateTime ResetTime { get; }

    public PackRateLimitException(string message, DateTime resetTime)
        : base(message)
    {
        ResetTime = resetTime;
    }
}
