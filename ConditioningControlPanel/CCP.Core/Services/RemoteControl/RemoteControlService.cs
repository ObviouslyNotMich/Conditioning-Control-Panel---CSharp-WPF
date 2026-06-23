using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using ConditioningControlPanel.Core.Services.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Core.Services.RemoteControl;

/// <summary>
/// Serializable snapshot of the current session progress for the remote controller.
/// </summary>
public sealed class SessionProgressInfo
{
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("icon")]
    public string Icon { get; set; } = "";

    [JsonProperty("elapsed_seconds")]
    public int ElapsedSeconds { get; set; }

    [JsonProperty("total_seconds")]
    public int TotalSeconds { get; set; }

    [JsonProperty("is_paused")]
    public bool IsPaused { get; set; }

    [JsonProperty("current_phase")]
    public string CurrentPhase { get; set; } = "";
}

/// <summary>
/// Cross-platform remote-control service. Manages the server session at
/// <c>codebambi-proxy.vercel.app</c>, polls for controller commands, and delegates
/// command execution to an <see cref="IRemoteCommandExecutor"/> provided by the UI head.
/// </summary>
public sealed class RemoteControlService : IRemoteControlService, IDisposable
{
    private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";
    private const double PollIntervalSeconds = 5.0;
    private const double StatusPushIntervalSeconds = 15.0;
    private const double StatusBackoffSeconds = 60.0;
    private const double MaxBackoffSeconds = 60.0;
    private const int HealthLogIntervalSeconds = 30;
    private const double IdleAutoDisconnectSeconds = 120.0;
    private const int EmoteDebounceMs = 300;

    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<RemoteControlService>? _logger;
    private readonly IRemoteCommandExecutor? _commandExecutor;
    private readonly IRemoteStatusProvider? _statusProvider;

    private readonly Random _pinRng = new();
    private readonly object _sync = new();

    private bool _isDisposed;
    private bool _isActive;
    private bool _controllerConnected;
    private bool _controllerIdle;
    private string? _sessionCode;
    private string? _connectPin;
    private string? _tier;
    private Timer? _pollTimer;
    private bool _pollInProgress;

    private int _consecutivePollFailures;
    private int _consecutivePollSuccesses;
    private int _totalCommandsReceived;
    private DateTime _lastHealthLog = DateTime.MinValue;
    private DateTime _sessionStartTime = DateTime.MinValue;
    private double _currentPollInterval = PollIntervalSeconds;
    private DateTime? _controllerIdleSince;
    private bool _controllerAutoDisconnected;
    private DateTime _lastStatusPushUtc = DateTime.MinValue;
    private DateTime _statusBackoffUntil = DateTime.MinValue;
    private DateTime _lastEmoteSentUtc = DateTime.MinValue;
    private bool _engineStoppedForController;
    private List<string>? _lastOptInTags;
    private string? _lastOptInStatus;

    public RemoteControlService(
        ISettingsService settingsService,
        ILogger<RemoteControlService>? logger = null,
        IRemoteCommandExecutor? commandExecutor = null,
        IRemoteStatusProvider? statusProvider = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger;
        _commandExecutor = commandExecutor;
        _statusProvider = statusProvider;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        var version = GetCurrentVersion();
        _httpClient.DefaultRequestHeaders.Add("X-Client-Version", version);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{version}");
    }

    public bool IsActive
    {
        get { lock (_sync) return _isActive; }
        private set
        {
            bool changed;
            lock (_sync)
            {
                changed = _isActive != value;
                _isActive = value;
            }

            if (changed && value)
                SessionStarted?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool ControllerConnected
    {
        get { lock (_sync) return _controllerConnected; }
        private set
        {
            bool changed;
            lock (_sync)
            {
                changed = _controllerConnected != value;
                _controllerConnected = value;
            }

            if (changed)
            {
                _logger?.LogInformation("Remote controller connected changed: {Connected}", value);
                ControllerConnectedChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool ControllerIdle
    {
        get { lock (_sync) return _controllerIdle; }
        private set
        {
            bool changed;
            lock (_sync)
            {
                changed = _controllerIdle != value;
                _controllerIdle = value;
            }

            if (changed)
                ControllerIdleChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? SessionCode
    {
        get { lock (_sync) return _sessionCode; }
        private set { lock (_sync) _sessionCode = value; }
    }

    public string? ConnectPin
    {
        get { lock (_sync) return _connectPin; }
        private set { lock (_sync) _connectPin = value; }
    }

    public string? Tier
    {
        get { lock (_sync) return _tier; }
        private set { lock (_sync) _tier = value; }
    }

    public event EventHandler? ControllerConnectedChanged;
    public event EventHandler? ControllerIdleChanged;
    public event EventHandler? SessionStarted;
    public event EventHandler? SessionEnded;
    public event EventHandler<string>? CommandReceived;

    public bool IsWithinDebounceWindow =>
        (DateTime.UtcNow - _lastEmoteSentUtc).TotalMilliseconds < EmoteDebounceMs;

    public async Task<string?> StartSessionAsync(string tier)
    {
        var unifiedId = _settingsService.Current?.UnifiedId;
        if (string.IsNullOrEmpty(unifiedId))
        {
            _logger?.LogWarning("[RemoteControl] Cannot start: no unified ID");
            return null;
        }

        try
        {
            var pin = _pinRng.Next(0, 10000).ToString("D4");
            var body = JsonConvert.SerializeObject(new { unified_id = unifiedId, tier, connect_pin = pin });

            using var response = await AuthPostAsync($"{ProxyBaseUrl}/v2/remote/start", body).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("[RemoteControl] Start failed: {Status} {Body}", response.StatusCode, json);
                return null;
            }

            var result = JObject.Parse(json);
            var code = result["code"]?.ToString();

            lock (_sync)
            {
                _consecutivePollFailures = 0;
                _consecutivePollSuccesses = 0;
                _totalCommandsReceived = 0;
                _lastHealthLog = DateTime.MinValue;
                _sessionStartTime = DateTime.UtcNow;
                _currentPollInterval = PollIntervalSeconds;
                _lastStatusPushUtc = DateTime.MinValue;
                _statusBackoffUntil = DateTime.MinValue;
                _controllerIdleSince = null;
                _controllerAutoDisconnected = false;
                _engineStoppedForController = false;
                _lastOptInTags = null;
                _lastOptInStatus = null;
            }

            SessionCode = code;
            ConnectPin = pin;
            Tier = tier;
            IsActive = true;
            ControllerConnected = false;
            ControllerIdle = false;

            SchedulePoll(PollIntervalSeconds);

            _logger?.LogInformation("[RemoteControl] Session started: {Code}, tier: {Tier}", code, tier);
            return code;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[RemoteControl] Start error");
            return null;
        }
    }

    public async Task StopSessionAsync()
    {
        if (!IsActive) return;

        var unifiedId = _settingsService.Current?.UnifiedId;
        if (!string.IsNullOrEmpty(unifiedId))
        {
            try
            {
                var body = JsonConvert.SerializeObject(new { unified_id = unifiedId });
                using var response = await AuthPostAsync($"{ProxyBaseUrl}/v2/remote/stop", body).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[RemoteControl] Stop request failed");
            }
        }

        CleanupSession();
        _logger?.LogInformation("[RemoteControl] Session stopped");
    }

    public async Task OptInToDirectoryAsync(List<string> tags, string statusText)
    {
        if (!IsActive || string.IsNullOrEmpty(SessionCode) || string.IsNullOrEmpty(ConnectPin))
        {
            _logger?.LogWarning("[RemoteControl] OptIn called without active session");
            return;
        }

        var unifiedId = _settingsService.Current?.UnifiedId;
        if (string.IsNullOrEmpty(unifiedId)) return;

        try
        {
            var body = JsonConvert.SerializeObject(new
            {
                unified_id = unifiedId,
                code = SessionCode,
                pin = ConnectPin,
                tags = tags ?? new List<string>(),
                status_text = statusText ?? ""
            });

            using var response = await AuthPostAsync($"{ProxyBaseUrl}/v2/directory/opt-in", body).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("[RemoteControl] Directory opt-in failed: {Status}", response.StatusCode);
                return;
            }

            _lastOptInTags = tags ?? new List<string>();
            _lastOptInStatus = statusText ?? "";
            _logger?.LogInformation("[RemoteControl] Directory opt-in OK ({TagCount} tags, status={StatusLen}c)",
                (tags ?? new List<string>()).Count, (statusText ?? "").Length);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[RemoteControl] Directory opt-in error");
        }
    }

    public async Task DisconnectControllerAsync()
    {
        _logger?.LogInformation("Remote controller disconnect requested.");

        var unifiedId = _settingsService.Current?.UnifiedId;
        if (!string.IsNullOrEmpty(unifiedId) && !string.IsNullOrEmpty(SessionCode))
        {
            try
            {
                var body = JsonConvert.SerializeObject(new { unified_id = unifiedId });
                using var response = await AuthPostAsync($"{ProxyBaseUrl}/v2/remote/disconnect", body).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[RemoteControl] Disconnect request failed");
            }
        }

        ControllerConnected = false;
        ControllerIdle = false;
    }

    public Task PushStatusNowAsync()
    {
        if (!IsActive) return Task.CompletedTask;
        return SendStatusAsync();
    }

    public async Task<(bool ok, string? error, int? retryAfterSeconds)> SendEmoteAsync(string text, string icon, string kind)
    {
        if (!IsActive) return (false, "session not active", null);

        var unifiedId = _settingsService.Current?.UnifiedId;
        if (string.IsNullOrEmpty(unifiedId)) return (false, "no unified id", null);

        if (IsWithinDebounceWindow)
            return (false, "debounced", null);

        _lastEmoteSentUtc = DateTime.UtcNow;

        var trimmed = (text ?? "").Trim();
        if (trimmed.Length == 0) return (false, "text required", null);
        if (trimmed.Length > 60) trimmed = trimmed.Substring(0, 60);
        var safeIcon = icon ?? "";
        if (safeIcon.Length > 8) safeIcon = safeIcon.Substring(0, 8);
        if (kind != "preset" && kind != "custom") return (false, "invalid kind", null);

        try
        {
            var body = JsonConvert.SerializeObject(new
            {
                unified_id = unifiedId,
                text = trimmed,
                icon = safeIcon,
                kind
            });

            using var response = await AuthPostAsync($"{ProxyBaseUrl}/v2/remote/emote", body).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                _logger?.LogInformation("[RemoteControl] Emote sent (kind={Kind}, len={Len})", kind, trimmed.Length);
                return (true, null, null);
            }

            var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (response.StatusCode == (System.Net.HttpStatusCode)429)
            {
                int? retryAfter = null;
                try
                {
                    var obj = JObject.Parse(raw);
                    var ra = obj["retry_after_seconds"];
                    if (ra != null && int.TryParse(ra.ToString(), out var n)) retryAfter = n;
                }
                catch { /* keep null */ }

                _logger?.LogWarning("[RemoteControl] Emote rate limited (retry_after={Retry}s)", retryAfter?.ToString() ?? "?");
                return (false, "rate_limited", retryAfter);
            }

            _logger?.LogWarning("[RemoteControl] Emote send failed: {Status} {Body}", response.StatusCode, raw);
            return (false, $"http {(int)response.StatusCode}", null);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[RemoteControl] Emote send error");
            return (false, ex.Message, null);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_isDisposed) return;
            _isDisposed = true;
        }

        _pollTimer?.Dispose();
        _httpClient.Dispose();
        CleanupSession();
    }

    private void CleanupSession()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;

        lock (_sync)
        {
            _consecutivePollFailures = 0;
            _consecutivePollSuccesses = 0;
            _currentPollInterval = PollIntervalSeconds;
            _controllerIdleSince = null;
            _controllerAutoDisconnected = false;
            _lastStatusPushUtc = DateTime.MinValue;
            _statusBackoffUntil = DateTime.MinValue;
            _engineStoppedForController = false;
            _lastOptInTags = null;
            _lastOptInStatus = null;
        }

        var wasActive = IsActive;
        IsActive = false;
        SessionCode = null;
        ConnectPin = null;
        Tier = null;
        ControllerConnected = false;
        ControllerIdle = false;

        if (wasActive)
        {
            _logger?.LogInformation("[RemoteControl] Session stopped.");
            if (_commandExecutor != null)
                _ = _commandExecutor.StopAllRemoteEffectsAsync();
            SessionEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SchedulePoll(double intervalSeconds)
    {
        if (_isDisposed || !IsActive) return;

        _pollTimer?.Dispose();
        _pollTimer = new Timer(_ => _ = PollForCommandsAsync(), null, TimeSpan.FromSeconds(intervalSeconds), Timeout.InfiniteTimeSpan);
    }

    private async Task PollForCommandsAsync()
    {
        if (!IsActive) return;
        if (_pollInProgress) return;

        _pollInProgress = true;
        try
        {
            var unifiedId = _settingsService.Current?.UnifiedId;
            if (string.IsNullOrEmpty(unifiedId)) return;

            try
            {
                var body = JsonConvert.SerializeObject(new { unified_id = unifiedId });
                using var response = await AuthPostAsync($"{ProxyBaseUrl}/v2/remote/poll", body).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    await HandlePollErrorAsync(response).ConfigureAwait(false);
                    return;
                }

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JObject.Parse(json);

                RecoverFromBackoff();
                LogHealthIfNeeded();

                await UpdateControllerStateAsync(result).ConfigureAwait(false);
                await ExecuteCommandsAsync(result).ConfigureAwait(false);
                await MaybePushStatusAsync(result).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                _consecutivePollFailures++;
                if (_consecutivePollFailures >= 3)
                    _logger?.LogWarning("[RemoteControl] Poll timeout (consecutive: {Count})", _consecutivePollFailures);
            }
            catch (Exception ex)
            {
                _consecutivePollFailures++;
                _logger?.LogWarning(ex, "[RemoteControl] Poll error (consecutive: {Count})", _consecutivePollFailures);
            }
        }
        finally
        {
            _pollInProgress = false;
            if (IsActive)
            {
                SchedulePoll(_currentPollInterval);
            }
        }
    }

    private async Task HandlePollErrorAsync(HttpResponseMessage response)
    {
        _consecutivePollFailures++;
        _consecutivePollSuccesses = 0;

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger?.LogWarning("[RemoteControl] Session expired during poll");
            CleanupSession();
        }
        else if (response.StatusCode == (System.Net.HttpStatusCode)429)
        {
            _currentPollInterval = Math.Min(_currentPollInterval * 2, MaxBackoffSeconds);
            var (cap, count) = await Read429CapAsync(response).ConfigureAwait(false);
            _logger?.LogWarning("[RemoteControl] Rate limited (429) [code={Code} cap={Cap} count={Count}], backing off to {Interval}s",
                SessionCode ?? "?", cap, count, _currentPollInterval);
        }
        else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger?.LogWarning("[RemoteControl] Auth failure (401), consecutive: {Count}", _consecutivePollFailures);
            if (_consecutivePollFailures >= 3)
            {
                _logger?.LogError("[RemoteControl] 3 consecutive auth failures — terminating session");
                CleanupSession();
            }
        }
        else if (_consecutivePollFailures >= 5)
        {
            _logger?.LogError("[RemoteControl] Poll failed: {Status} (consecutive failures: {Count})", response.StatusCode, _consecutivePollFailures);
        }
        else
        {
            _logger?.LogWarning("[RemoteControl] Poll failed: {Status}", response.StatusCode);
        }
    }

    private void RecoverFromBackoff()
    {
        var wasBackedOff = _currentPollInterval > PollIntervalSeconds;
        var wasFailingConsecutively = _consecutivePollFailures > 0;
        _consecutivePollSuccesses++;
        _consecutivePollFailures = 0;

        if (wasBackedOff)
        {
            _currentPollInterval = PollIntervalSeconds;
            _logger?.LogInformation("[RemoteControl] Recovered from backoff, restoring {Interval}s poll interval", PollIntervalSeconds);
        }
        else if (wasFailingConsecutively && _consecutivePollSuccesses == 1)
        {
            _logger?.LogInformation("[RemoteControl] Poll recovered after failures");
        }
    }

    private void LogHealthIfNeeded()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastHealthLog).TotalSeconds < HealthLogIntervalSeconds) return;

        _lastHealthLog = now;
        var uptime = now - _sessionStartTime;
        _logger?.LogInformation(
            "[RemoteControl] Health: code={Code} uptime={Uptime} polls_ok={Successes} cmds_total={Cmds} controller={Status}",
            SessionCode,
            $"{(int)uptime.TotalMinutes}m{uptime.Seconds}s",
            _consecutivePollSuccesses,
            _totalCommandsReceived,
            ControllerConnected ? (ControllerIdle ? "idle" : "active") : "disconnected");
    }

    private async Task UpdateControllerStateAsync(JObject result)
    {
        var serverConnected = result["controller_connected"]?.Value<bool>() ?? false;
        var idle = result["controller_idle"]?.Value<bool>() ?? false;

        var connected = serverConnected;

        if (connected && _controllerAutoDisconnected)
        {
            connected = !idle;
            if (!idle)
            {
                _controllerAutoDisconnected = false;
                _controllerIdleSince = null;
            }
        }

        if (!serverConnected)
            _controllerAutoDisconnected = false;

        var controllerConnectedChanged = connected != ControllerConnected;
        _controllerConnectedChangedThisPoll = controllerConnectedChanged;

        if (controllerConnectedChanged)
        {
            var wasConnected = ControllerConnected;
            ControllerConnected = connected;
            if (!connected && wasConnected)
            {
                if (_commandExecutor != null)
                    _ = _commandExecutor.HandleControllerDisconnectAsync();
                _ = RepublishDirectoryIfOptedInAsync();
            }
        }

        if (idle != ControllerIdle)
        {
            ControllerIdle = idle;
            _controllerIdleSince = idle ? DateTime.UtcNow : null;
        }

        if (ControllerConnected && ControllerIdle && _controllerIdleSince.HasValue)
        {
            var idleDuration = (DateTime.UtcNow - _controllerIdleSince.Value).TotalSeconds;
            if (idleDuration >= IdleAutoDisconnectSeconds)
            {
                _logger?.LogInformation("[RemoteControl] Controller idle for {Seconds:F0}s — auto-disconnecting", idleDuration);
                _controllerAutoDisconnected = true;
                ControllerConnected = false;
                ControllerIdle = false;
                _ = RepublishDirectoryIfOptedInAsync();
            }
        }
    }

    private async Task ExecuteCommandsAsync(JObject result)
    {
        string? lastCmdId = null;
        string? lastAction = null;

        var commands = result["commands"] as JArray;
        if (commands != null && commands.Count > 0)
        {
            _totalCommandsReceived += commands.Count;
            _logger?.LogInformation("[RemoteControl] Poll returned {Count} command(s), session total: {Total}", commands.Count, _totalCommandsReceived);
        }

        if (commands == null) return;

        foreach (var cmd in commands)
        {
            var action = cmd["action"]?.ToString();
            var id = cmd["id"]?.ToString();
            if (string.IsNullOrEmpty(action)) continue;

            _logger?.LogInformation("[RemoteControl] Executing: {Action} (id: {Id})", action, id);

            try
            {
                var parameters = cmd["params"] as JObject;
                if (_commandExecutor != null)
                {
                    await _commandExecutor.ExecuteCommandAsync(action, parameters).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[RemoteControl] Error executing command: {Action}", action);
            }

            CommandReceived?.Invoke(this, action);
            lastCmdId = id;
            lastAction = action;
        }

        _lastExecutedCmdId = lastCmdId;
        _lastExecutedAction = lastAction;
    }

    private string? _lastExecutedCmdId;
    private string? _lastExecutedAction;
    private bool _controllerConnectedChangedThisPoll;

    private async Task MaybePushStatusAsync(JObject result)
    {
        var statusDue = (DateTime.UtcNow - _lastStatusPushUtc).TotalSeconds >= StatusPushIntervalSeconds;

        if (_lastExecutedCmdId != null || _controllerConnectedChangedThisPoll || statusDue)
        {
            await SendStatusAsync(_lastExecutedCmdId, _lastExecutedAction).ConfigureAwait(false);
            _lastExecutedCmdId = null;
            _lastExecutedAction = null;
            _controllerConnectedChangedThisPoll = false;
        }
    }

    private async Task SendStatusAsync(string? lastCmdId = null, string? lastAction = null)
    {
        if (DateTime.UtcNow < _statusBackoffUntil) return;

        var unifiedId = _settingsService.Current?.UnifiedId;
        if (string.IsNullOrEmpty(unifiedId)) return;

        try
        {
            var activeServices = _statusProvider?.GetActiveServices() ?? new List<string>();
            var level = _settingsService.Current?.PlayerLevel ?? 1;

            object? lastExecuted = null;
            if (lastCmdId != null)
            {
                lastExecuted = new
                {
                    id = lastCmdId,
                    action = lastAction,
                    status = "ok",
                    at = DateTime.UtcNow.ToString("o")
                };
            }

            var availableSessions = _statusProvider?.GetAvailableSessions();
            var sessionInfo = _statusProvider?.GetSessionProgress();

            var body = JsonConvert.SerializeObject(new
            {
                unified_id = unifiedId,
                active_services = activeServices,
                level,
                last_executed = lastExecuted,
                available_sessions = availableSessions,
                session_info = sessionInfo,
                share_avatar = _settingsService.Current?.RemoteShareAvatar == true
            });

            using var response = await AuthPostAsync($"{ProxyBaseUrl}/v2/remote/status", body).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == (System.Net.HttpStatusCode)429)
                {
                    _statusBackoffUntil = DateTime.UtcNow.AddSeconds(StatusBackoffSeconds);
                    var (cap, count) = await Read429CapAsync(response).ConfigureAwait(false);
                    _logger?.LogWarning(
                        "[RemoteControl] Status push rate limited (429) [code={Code} cap={Cap} count={Count}] — suppressing status pushes for {Seconds}s",
                        SessionCode ?? "?", cap, count, StatusBackoffSeconds);
                }
                else
                {
                    _logger?.LogWarning(
                        "[RemoteControl] Status push failed: {Status} [code={Code}]",
                        response.StatusCode, SessionCode ?? "?");
                }
            }
            else
            {
                _lastStatusPushUtc = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[RemoteControl] Status update error");
        }
    }

    private async Task RepublishDirectoryIfOptedInAsync()
    {
        if (_lastOptInTags == null) return;
        if (!IsActive) return;
        try
        {
            await OptInToDirectoryAsync(_lastOptInTags, _lastOptInStatus ?? "").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[RemoteControl] Directory re-publish after disconnect failed");
        }
    }

    private async Task<HttpResponseMessage> AuthPostAsync(string url, string jsonBody)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };

        var token = _settingsService.Current?.AuthToken;
        if (!string.IsNullOrEmpty(token))
            request.Headers.Add("X-Auth-Token", token);

        return await _httpClient.SendAsync(request).ConfigureAwait(false);
    }

    private static async Task<(string cap, string count)> Read429CapAsync(HttpResponseMessage response)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(body)) return ("?", "?");
            var obj = JObject.Parse(body);
            return (obj["cap"]?.ToString() ?? "?", obj["count"]?.ToString() ?? "?");
        }
        catch { return ("?", "?"); }
    }

    private static string GetCurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        if (version != null && (version.Major > 0 || version.Minor > 0 || version.Build > 0))
            return $"{version.Major}.{version.Minor}.{version.Build}";

        var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(infoVersion))
        {
            var plusIndex = infoVersion.IndexOf('+');
            var clean = plusIndex > 0 ? infoVersion[..plusIndex] : infoVersion;
            if (Version.TryParse(clean, out var parsed))
                return $"{parsed.Major}.{parsed.Minor}.{parsed.Build}";
        }

        return "1.0.0";
    }
}
