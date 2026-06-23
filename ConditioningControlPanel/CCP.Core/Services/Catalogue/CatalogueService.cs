using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Core.Services.Catalogue;

/// <summary>
/// Deeper enhancement catalogue submission client (cross-platform port of the
/// WPF <c>CatalogueService</c>).
///
/// Two endpoints, one auth boundary:
///   1. POST https://app.cclabs.app/api/auth/token-exchange — converts the
///      CCP-server-issued AuthToken into a short-lived Supabase access token,
///      cached in-memory and refreshed when it nears expiry. Never persisted to disk.
///   2. POST https://app.cclabs.app/api/enhancements — the actual catalogue
///      submission, authenticated with the Supabase token in an
///      Authorization: Bearer header.
/// </summary>
public sealed class CatalogueService : ICatalogueService, IDisposable
{
    private const string CclabsBaseUrl = "https://app.cclabs.app";
    private const string GuidelinesVersion = "1.0";

    // Refresh when the cached token is within this many seconds of expiry.
    private const int ExpiryMarginSeconds = 60;

    private readonly HttpClient _http;
    private readonly ISettingsService _settings;
    private readonly IUserIdentityProvider _identity;
    private readonly ILogger<CatalogueService>? _logger;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _cachedExpiry = DateTimeOffset.MinValue;
    private bool _disposed;

    public event EventHandler<SubmissionResult.Success>? SubmissionSucceeded;

    public CatalogueService(
        ISettingsService settings,
        IUserIdentityProvider identity,
        string appVersion,
        ILogger<CatalogueService>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _logger = logger;

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Add("X-Client-Version", appVersion);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{appVersion}");
    }

    public async Task<SubmissionResult> SubmitEnhancementAsync(string ccpenhJsonPath, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ccpenhJsonPath) || !File.Exists(ccpenhJsonPath))
            {
                return new SubmissionResult.UnknownError(0, $"file_not_found: {ccpenhJsonPath}");
            }

            string fileContents;
            try
            {
                fileContents = await File.ReadAllTextAsync(ccpenhJsonPath, Encoding.UTF8, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[CatalogueService] Read file failed");
                return new SubmissionResult.UnknownError(0, $"read_error: {ex.Message}");
            }

            JToken bundle;
            try
            {
                bundle = JToken.Parse(fileContents);
            }
            catch (JsonReaderException)
            {
                return new SubmissionResult.ValidationError("invalid_json");
            }

            var envelope = new JObject
            {
                ["bundle"] = bundle,
                ["affirmation"] = new JObject
                {
                    ["guidelines_version"] = GuidelinesVersion,
                    ["affirmed"] = true,
                },
            };
            var envelopeJson = envelope.ToString(Formatting.None);

            var token = await GetSupabaseTokenAsync(ct).ConfigureAwait(false);
            if (token == null)
            {
                return new SubmissionResult.AuthFailed();
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{CclabsBaseUrl}/api/enhancements")
            {
                Content = new StringContent(envelopeJson, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            return MapResponse(status, body, response.Headers);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[CatalogueService] Submit threw");
            return new SubmissionResult.UnknownError(0, ex.Message);
        }
    }

    public async Task<SubmissionResult> SubmitCatalogueAssetAsync(
        string kind,
        JToken asset,
        string schemaTag,
        string creator,
        IReadOnlyList<string> tags,
        CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(kind) || asset == null)
            {
                return new SubmissionResult.UnknownError(0, "invalid_asset");
            }

            var tagArray = new JArray();
            if (tags != null)
            {
                foreach (var t in tags)
                {
                    if (!string.IsNullOrWhiteSpace(t)) tagArray.Add(t.Trim());
                }
            }

            var envelope = new JObject
            {
                ["affirmation"] = new JObject
                {
                    ["guidelines_version"] = GuidelinesVersion,
                    ["affirmed"] = true,
                },
                ["bundle"] = new JObject
                {
                    ["$schema"] = schemaTag,
                    ["version"] = 1,
                    ["metadata"] = new JObject
                    {
                        ["creator"] = creator ?? "",
                        ["tags"] = tagArray,
                    },
                    ["asset"] = asset,
                },
            };
            var envelopeJson = envelope.ToString(Formatting.None);

            var token = await GetSupabaseTokenAsync(ct).ConfigureAwait(false);
            if (token == null)
            {
                return new SubmissionResult.AuthFailed();
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{CclabsBaseUrl}/api/catalogue/{kind}")
            {
                Content = new StringContent(envelopeJson, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            return MapResponse(status, body, response.Headers, fireSuccessEvent: false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[CatalogueService] SubmitCatalogueAsset threw");
            return new SubmissionResult.UnknownError(0, ex.Message);
        }
    }

    public Task<Dictionary<string, string>?> FetchMyCatalogueAssetsAsync(string kind, CancellationToken ct = default)
        => FetchMineAsync($"{CclabsBaseUrl}/api/catalogue/{kind}/mine", "assets", ct);

    public Task<Dictionary<string, string>?> FetchMySubmissionsAsync(CancellationToken ct = default)
        => FetchMineAsync($"{CclabsBaseUrl}/api/enhancements/mine", "enhancements", ct);

    public void InvalidateCachedToken()
    {
        _cachedToken = null;
        _cachedExpiry = DateTimeOffset.MinValue;
    }

    private async Task<Dictionary<string, string>?> FetchMineAsync(string url, string arrayKey, CancellationToken ct)
    {
        try
        {
            var token = await GetSupabaseTokenAsync(ct).ConfigureAwait(false);
            if (token == null) return null;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                InvalidateCachedToken();
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogDebug("[CatalogueService] FetchMine non-success status={Status} url={Url}", (int)response.StatusCode, url);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            JObject parsed;
            try { parsed = JObject.Parse(body); }
            catch { return null; }

            if (parsed[arrayKey] is not JArray arr)
            {
                arr = parsed.Properties().Select(p => p.Value).OfType<JArray>().FirstOrDefault();
                if (arr == null) return null;
            }

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in arr)
            {
                var id = item["id"]?.ToString();
                var status = item["status"]?.ToString();
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(status))
                    map[id] = status;
            }
            return map;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("[CatalogueService] FetchMine threw: {Error}", ex.Message);
            return null;
        }
    }

    private SubmissionResult MapResponse(int status, string body, System.Net.Http.Headers.HttpResponseHeaders headers, bool fireSuccessEvent = true)
    {
        JObject? parsed = null;
        try { parsed = JObject.Parse(body); }
        catch { /* leave parsed null */ }

        switch (status)
        {
            case 201:
            {
                var id = parsed?["id"]?.ToString() ?? "";
                var rowStatus = parsed?["status"]?.ToString() ?? "pending";
                _logger?.LogInformation("[CatalogueService] Submission succeeded id={Id}", id);
                var success = new SubmissionResult.Success(id, rowStatus);
                if (fireSuccessEvent)
                {
                    try { SubmissionSucceeded?.Invoke(this, success); }
                    catch (Exception ex) { _logger?.LogDebug("SubmissionSucceeded subscriber error: {Error}", ex.Message); }
                }
                return success;
            }
            case 409:
            {
                var existingId = parsed?["existing_id"]?.ToString() ?? "";
                var existingStatus = parsed?["existing_status"]?.ToString() ?? "pending";
                _logger?.LogInformation("[CatalogueService] Submission duplicate existing_id={Id}", existingId);
                return new SubmissionResult.Duplicate(existingId, existingStatus);
            }
            case 400:
            {
                var errorCode = parsed?["error"]?.ToString() ?? "invalid_request";
                _logger?.LogWarning("[CatalogueService] Submission rejected: {ErrorCode}", errorCode);
                return new SubmissionResult.ValidationError(errorCode);
            }
            case 401:
            {
                _logger?.LogWarning("[CatalogueService] Auth failed, invalidating cached token");
                InvalidateCachedToken();
                return new SubmissionResult.AuthFailed();
            }
            case 403:
            {
                _logger?.LogWarning("[CatalogueService] no_profile, invalidating cached token");
                InvalidateCachedToken();
                return new SubmissionResult.AuthFailed();
            }
            case 413:
            {
                _logger?.LogWarning("[CatalogueService] file too large");
                return new SubmissionResult.TooLarge();
            }
            case 429:
            {
                int? retryAfter = null;
                var retryHeader = headers.RetryAfter?.Delta?.TotalSeconds;
                if (retryHeader.HasValue)
                {
                    retryAfter = (int)retryHeader.Value;
                }
                else if (parsed?["retry_after"] != null && int.TryParse(parsed["retry_after"]!.ToString(), out var ra))
                {
                    retryAfter = ra;
                }
                _logger?.LogWarning("[CatalogueService] Submission rate-limited retry_after={RetryAfter}", retryAfter);
                return new SubmissionResult.RateLimited(retryAfter);
            }
            default:
            {
                _logger?.LogWarning("[CatalogueService] Submission unknown status={Status} body={Body}", status, body);
                return new SubmissionResult.UnknownError(status, body);
            }
        }
    }

    private async Task<string?> GetSupabaseTokenAsync(CancellationToken ct)
    {
        if (_cachedToken != null && _cachedExpiry > DateTimeOffset.UtcNow.AddSeconds(ExpiryMarginSeconds))
        {
            return _cachedToken;
        }

        await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cachedToken != null && _cachedExpiry > DateTimeOffset.UtcNow.AddSeconds(ExpiryMarginSeconds))
            {
                return _cachedToken;
            }

            var ccpToken = _settings.Current?.AuthToken;
            var unifiedId = _identity.UnifiedUserId;
            if (string.IsNullOrEmpty(ccpToken) || string.IsNullOrEmpty(unifiedId))
            {
                _logger?.LogWarning("[CatalogueService] Token exchange skipped: missing CCP auth state");
                return null;
            }

            _logger?.LogInformation("[CatalogueService] Token exchange called");

            var requestBody = JsonConvert.SerializeObject(new { unified_id = unifiedId });
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{CclabsBaseUrl}/api/auth/token-exchange")
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-CCP-Auth-Token", ccpToken);

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("[CatalogueService] Token exchange failed: {Status}", response.StatusCode);
                InvalidateCachedToken();
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var parsed = JObject.Parse(body);
            var accessToken = parsed["access_token"]?.ToString();
            var expiresAtStr = parsed["expires_at"]?.ToString();
            if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(expiresAtStr) ||
                !DateTimeOffset.TryParse(expiresAtStr, out var expiresAt))
            {
                _logger?.LogWarning("[CatalogueService] Token exchange returned malformed payload");
                return null;
            }

            _cachedToken = accessToken;
            _cachedExpiry = expiresAt;
            _logger?.LogInformation("[CatalogueService] Token exchanged, expires_at={ExpiresAt}", _cachedExpiry);
            return _cachedToken;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[CatalogueService] Token exchange threw");
            InvalidateCachedToken();
            return null;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tokenLock.Dispose();
        _http.Dispose();
    }
}
