using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Update;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Core.Services.BugReport;

/// <summary>
/// Cross-platform implementation of <see cref="IBugReportService"/>.
/// Client service for submitting bug reports. Collects metadata (with a hard
/// allowlist), runs the scrubber on logs, HMAC-signs the payload with a
/// separate embedded secret, and POSTs to the proxy /bug/upload endpoint.
/// </summary>
public sealed class BugReportService : IBugReportService
{
    private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";
    private const string BugUploadPath = "/bug/upload";

    // Separate embedded secret — NOT the anti-cheat HMAC key, NOT tied to UnifiedId.
    private const string EmbeddedClientSecret = "067dd64f975e275fd4ba5dd06febaa63f95dcdb93f667ae44716046a30d14b95";

    private const int MaxDescriptionChars = 4000;
    private const int MaxStepsChars = 4000;
    private const int MaxCrashLogChars = 120_000;
    private const int MaxAppLogLines = 100;

    private readonly IAppEnvironment _environment;
    private readonly ISettingsService _settings;
    private readonly IModService _mods;
    private readonly ILogger<BugReportService>? _logger;
    private readonly HttpClient _httpClient;

    public BugReportService(
        IAppEnvironment environment,
        ISettingsService settings,
        IModService mods,
        ILogger<BugReportService>? logger = null)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _mods = mods ?? throw new ArgumentNullException(nameof(mods));
        _logger = logger;

        var version = UpdateService.GetCurrentVersion().ToString();
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        _httpClient.DefaultRequestHeaders.Add("X-Client-Version", version);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{version}");
    }

    /// <inheritdoc />
    public BugReportDraft CreateDraft(string description, string steps, bool includeAppLog)
    {
        var metadata = new BugMetadata
        {
            AppVersion = SafeToString(() => UpdateService.GetCurrentVersion().ToString()),
            Os = SafeToString(() => Environment.OSVersion.ToString()),
            Dotnet = SafeToString(() => Environment.Version.ToString()),
            Language = _settings.Current?.Language ?? "en",
            ActiveModId = ResolveActiveModId(),
        };

        var crashLogRaw = TryReadCrashLog();
        var (scrubbedCrash, crashCounts) = LogScrubber.Scrub(crashLogRaw);
        if (scrubbedCrash.Length > MaxCrashLogChars)
        {
            scrubbedCrash = scrubbedCrash.Substring(scrubbedCrash.Length - MaxCrashLogChars);
        }

        var scrubbedApp = string.Empty;
        var appCounts = ScrubberCounts.Empty;
        if (includeAppLog)
        {
            var appLogRaw = TryReadRecentAppLog(MaxAppLogLines);
            (scrubbedApp, appCounts) = LogScrubber.Scrub(appLogRaw);
        }

        var totalCounts = crashCounts.Add(appCounts);

        return new BugReportDraft
        {
            Metadata = metadata,
            Description = Truncate(description ?? string.Empty, MaxDescriptionChars),
            Steps = Truncate(steps ?? string.Empty, MaxStepsChars),
            ScrubbedCrashLog = scrubbedCrash,
            ScrubbedAppLog = scrubbedApp,
            IncludeAppLog = includeAppLog,
            Counts = totalCounts,
        };
    }

    /// <inheritdoc />
    public string RenderPreview(BugReportDraft draft)
    {
        var payload = BuildPayload(draft);
        return JsonConvert.SerializeObject(payload, Formatting.Indented);
    }

    /// <inheritdoc />
    public async Task<SubmitResult> SubmitAsync(BugReportDraft draft)
    {
        try
        {
            var payload = BuildPayload(draft);
            var body = JsonConvert.SerializeObject(payload);

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var signature = ComputeSignature(timestamp, body);

            using var req = new HttpRequestMessage(HttpMethod.Post, ProxyBaseUrl + BugUploadPath)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("X-Bug-Timestamp", timestamp);
            req.Headers.Add("X-Bug-Signature", signature);

            using var res = await _httpClient.SendAsync(req).ConfigureAwait(false);
            var responseText = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

            if ((int)res.StatusCode == 202)
            {
                var token202 = TryExtractToken(responseText);
                return new SubmitResult(SubmitOutcome.SavedPending, token202, "bot_unreachable");
            }

            if (!res.IsSuccessStatusCode)
            {
                _logger?.LogWarning("[BugReport] Upload failed: {Status} {Body}", res.StatusCode, responseText);
                return new SubmitResult(SubmitOutcome.ValidationFailed, null, $"HTTP {(int)res.StatusCode}");
            }

            var token = TryExtractToken(responseText);
            if (string.IsNullOrEmpty(token))
            {
                return new SubmitResult(SubmitOutcome.ValidationFailed, null, "missing_token_in_response");
            }

            _logger?.LogInformation("[BugReport] Submitted successfully: {Token}", token);
            return new SubmitResult(SubmitOutcome.Success, token);
        }
        catch (TaskCanceledException ex)
        {
            _logger?.LogWarning(ex, "[BugReport] Upload timed out");
            return new SubmitResult(SubmitOutcome.NetworkError, null, "timeout");
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogWarning(ex, "[BugReport] Network error");
            return new SubmitResult(SubmitOutcome.NetworkError, null, ex.Message);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[BugReport] Unexpected error");
            return new SubmitResult(SubmitOutcome.ValidationFailed, null, ex.Message);
        }
    }

    private Dictionary<string, object> BuildPayload(BugReportDraft draft)
    {
        return new Dictionary<string, object>
        {
            ["metadata"] = new Dictionary<string, string>
            {
                ["app_version"] = draft.Metadata.AppVersion,
                ["os"] = draft.Metadata.Os,
                ["dotnet"] = draft.Metadata.Dotnet,
                ["language"] = draft.Metadata.Language,
                ["active_mod_id"] = draft.Metadata.ActiveModId,
            },
            ["description"] = draft.Description,
            ["steps"] = draft.Steps,
            ["crash_log"] = draft.ScrubbedCrashLog,
            ["app_log"] = draft.IncludeAppLog ? draft.ScrubbedAppLog : string.Empty,
            ["scrubber_counts"] = new Dictionary<string, int>
            {
                ["paths"] = draft.Counts.Paths,
                ["emails"] = draft.Counts.Emails,
                ["tokens"] = draft.Counts.Tokens,
                ["appdata"] = draft.Counts.AppData,
            },
        };
    }

    private static string ComputeSignature(string timestamp, string body)
    {
        var keyBytes = Encoding.UTF8.GetBytes(EmbeddedClientSecret);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? TryExtractToken(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText)) return null;
        try
        {
            var obj = JObject.Parse(responseText);
            return obj["token"]?.Value<string>();
        }
        catch
        {
            return null;
        }
    }

    private string ResolveActiveModId()
    {
        try
        {
            var mod = _mods.ActiveMod;
            if (mod == null) return "unknown";
            if (!mod.IsBuiltIn) return "custom-mod";
            return string.IsNullOrEmpty(mod.Id) ? "unknown" : mod.Id;
        }
        catch
        {
            return "unknown";
        }
    }

    private static string SafeToString(Func<string?> fn)
    {
        try { return fn() ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max);

    private string TryReadCrashLog()
    {
        try
        {
            var path = Path.Combine(_environment.UserDataPath, "logs", "crash.log");
            if (!File.Exists(path)) return string.Empty;
            var info = new FileInfo(path);
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long tail = Math.Min(info.Length, MaxCrashLogChars);
            fs.Seek(info.Length - tail, SeekOrigin.Begin);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            return FilterBenignCrashes(sr.ReadToEnd());
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("[BugReport] crash log read failed: {Msg}", ex.Message);
            return string.Empty;
        }
    }

    private static string FilterBenignCrashes(string log)
    {
        if (string.IsNullOrEmpty(log)) return log;
        const string marker = "CRASH REPORT - ";
        var entries = log.Split(new[] { marker }, StringSplitOptions.None);
        if (entries.Length <= 1) return log;

        var kept = new StringBuilder(entries[0]);
        int dropped = 0;
        for (int i = 1; i < entries.Length; i++)
        {
            var entry = entries[i];
            bool benign = entry.Contains("System.DllNotFoundException", StringComparison.Ordinal)
                && (entry.Contains("__std_type_info_destroy_list", StringComparison.Ordinal)
                    || entry.Contains("_app_exit_callback", StringComparison.Ordinal));
            if (benign)
            {
                dropped++;
                continue;
            }
            kept.Append(marker).Append(entry);
        }

        if (dropped > 0)
            // Intentionally no structured logging here to keep the helper pure static-friendly.
            ;
        return kept.ToString();
    }

    private string TryReadRecentAppLog(int maxLines)
    {
        try
        {
            var logDir = Path.Combine(_environment.UserDataPath, "logs");
            if (!Directory.Exists(logDir)) return string.Empty;
            var files = Directory.GetFiles(logDir, "app-*.log");
            if (files.Length == 0) return string.Empty;
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            var latest = files[^1];

            using var fs = new FileStream(latest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            var allLines = new List<string>();
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                allLines.Add(line);
                if (allLines.Count > maxLines * 4)
                {
                    allLines.RemoveRange(0, allLines.Count - maxLines * 2);
                }
            }
            int start = Math.Max(0, allLines.Count - maxLines);
            return string.Join(Environment.NewLine, allLines.GetRange(start, allLines.Count - start));
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("[BugReport] app log read failed: {Msg}", ex.Message);
            return string.Empty;
        }
    }
}
