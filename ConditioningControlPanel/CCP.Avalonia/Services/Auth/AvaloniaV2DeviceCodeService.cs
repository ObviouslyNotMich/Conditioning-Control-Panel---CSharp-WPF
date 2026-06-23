using System.Net.Http;
using System.Text;
using ConditioningControlPanel.Core.Services.Auth;
using ConditioningControlPanel.Core.Services.Update;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Services.Auth;

/// <summary>
/// Avalonia (Core-aware) implementation of the SP3 device-code login service.
/// Mirrors the legacy WPF <c>V2DeviceCodeService</c> behavior while using the cross-platform
/// <see cref="IUpdateService"/> and <see cref="ILogger{T}"/> seams.
/// </summary>
public sealed class AvaloniaV2DeviceCodeService : IV2DeviceCodeService
{
    private const string ServerUrl = "https://codebambi-proxy.vercel.app";

    // Hardcoded: /v2/auth/device/initiate doesn't return a verification_url.
    // /dashboard/link-device is the canonical pairing page on cclabs-web;
    // the layout's auth gate handles the not-signed-in case via a ?next=
    // round-trip back to this path.
    public string VerificationUrl => "https://app.cclabs.app/dashboard/link-device";

    private readonly HttpClient _http = new();
    private readonly IUpdateService _updateService;
    private readonly ILogger<AvaloniaV2DeviceCodeService> _logger;

    public AvaloniaV2DeviceCodeService(IUpdateService updateService, ILogger<AvaloniaV2DeviceCodeService> logger)
    {
        _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.Add("X-Client-Version", _updateService.CurrentVersion);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{_updateService.CurrentVersion}");
    }

    // Internal DTO for JsonConvert.DeserializeObject — typed deserialization
    // with [JsonProperty] handles ISO 8601 + 'Z' suffix → DateTimeOffset cleanly,
    // unlike JObject.Parse + JValue<DateTime>.ToString round-trip which strips
    // timezone info on non-UTC systems.
    private class InitiateRaw
    {
        [JsonProperty("code")] public string? Code { get; set; }
        [JsonProperty("session_id")] public string? SessionId { get; set; }
        [JsonProperty("expires_at")] public DateTimeOffset ExpiresAt { get; set; }
    }

    /// <inheritdoc />
    public async Task<InitiateResponse> InitiateAsync(CancellationToken ct = default)
    {
        try
        {
            var payload = new JObject
            {
                ["client"] = "ccp-desktop",
                ["version"] = _updateService.CurrentVersion
            };

            var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{ServerUrl}/v2/auth/device/initiate", content, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = ParseErrorMessage(json, response.StatusCode);
                _logger.LogWarning("[DeviceCode] Initiate failed: {Status} {Error}", (int)response.StatusCode, error);
                return new InitiateResponse { Success = false, Error = error };
            }

            InitiateRaw? raw;
            try
            {
                raw = JsonConvert.DeserializeObject<InitiateRaw>(json);
            }
            catch (Exception ex)
            {
                return new InitiateResponse { Success = false, Error = "Bad response: " + ex.Message };
            }

            if (raw == null || string.IsNullOrEmpty(raw.Code))
                return new InitiateResponse { Success = false, Error = "Invalid response" };

            if (raw.ExpiresAt == default)
                return new InitiateResponse { Success = false, Error = "Invalid expiry" };

            _logger.LogInformation("[DeviceCode] Initiated session={Session}", raw.SessionId?.Substring(0, Math.Min(8, raw.SessionId.Length)));
            return new InitiateResponse
            {
                Success = true,
                Code = raw.Code,
                SessionId = raw.SessionId,
                ExpiresAt = raw.ExpiresAt
            };
        }
        catch (OperationCanceledException)
        {
            return new InitiateResponse { Success = false, Error = "cancelled" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DeviceCode] Initiate exception");
            return new InitiateResponse { Success = false, Error = ex.Message };
        }
    }

    /// <inheritdoc />
    public async Task<PollResponse> PollAsync(string code, CancellationToken ct = default)
    {
        try
        {
            var payload = new JObject { ["code"] = code };
            var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{ServerUrl}/v2/auth/device/poll", content, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            int statusCode = (int)response.StatusCode;

            switch (statusCode)
            {
                case 200:
                {
                    var obj = JObject.Parse(json);
                    var authToken = obj["auth_token"]?.ToString();
                    var unifiedId = obj["unified_id"]?.ToString();
                    var isNewUser = (bool?)obj["is_new_user"] ?? false;

                    V2User? user = null;
                    var userToken = obj["user"];
                    if (userToken != null && userToken.Type == JTokenType.Object)
                    {
                        try
                        {
                            user = userToken.ToObject<V2User>();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[DeviceCode] Failed to parse user from poll response");
                        }
                    }

                    _logger.LogInformation("[DeviceCode] Confirmed uid={Uid} new={New} hasUser={HasUser}", unifiedId, isNewUser, user != null);
                    return new PollResponse
                    {
                        Status = PollStatus.Confirmed,
                        AuthToken = authToken,
                        UnifiedId = unifiedId,
                        IsNewUser = isNewUser,
                        User = user
                    };
                }
                case 202:
                    return new PollResponse { Status = PollStatus.Pending };
                case 400:
                    return new PollResponse { Status = PollStatus.BadRequest };
                case 401:
                    return new PollResponse { Status = PollStatus.Unauthorized };
                case 404:
                    return new PollResponse { Status = PollStatus.NotFound };
                case 410:
                    return new PollResponse { Status = PollStatus.Expired };
                case 429:
                    return new PollResponse { Status = PollStatus.RateLimited };
                case 503:
                    return new PollResponse { Status = PollStatus.ServiceUnavailable };
                default:
                    _logger.LogWarning("[DeviceCode] Poll unexpected status {Status}", statusCode);
                    return new PollResponse { Status = PollStatus.Unknown };
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[DeviceCode] Poll exception: {Message}", ex.Message);
            return new PollResponse { Status = PollStatus.Unknown };
        }
    }

    private static string ParseErrorMessage(string body, System.Net.HttpStatusCode statusCode)
    {
        try
        {
            var obj = JObject.Parse(body);
            return obj["error"]?.ToString() ?? $"HTTP {(int)statusCode}";
        }
        catch
        {
            return $"HTTP {(int)statusCode}";
        }
    }
}
