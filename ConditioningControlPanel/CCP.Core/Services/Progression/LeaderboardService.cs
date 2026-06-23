using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Core.Services.Progression;

/// <summary>
/// Cross-platform leaderboard client. Fetches leaderboard data from the server,
/// caches it, and raises <see cref="LeaderboardUpdated"/> when data changes.
/// </summary>
public sealed class LeaderboardService : ILeaderboardService, IDisposable
{
    private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";

    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settings;
    private readonly IUserIdentityProvider _identity;
    private readonly ILogger<LeaderboardService>? _logger;
    private readonly ISeasonRecapService? _seasonRecap;
    private readonly Timer? _refreshTimer;
    private readonly object _lock = new();
    private bool _disposed;

    private List<LeaderboardEntry> _entries = new();

    public IReadOnlyList<LeaderboardEntry> Entries
    {
        get
        {
            lock (_lock) return _entries;
        }
    }

    public int TotalUsers { get; private set; }
    public int OnlineUsers { get; private set; }
    public int? YourRank { get; private set; }
    public int? YourTotal { get; private set; }
    public string CurrentSortBy { get; private set; } = "level";
    public string CurrentMode { get; private set; } = "monthly";
    public DateTime? LastRefreshTime { get; private set; }
    public string? LastRefreshError { get; private set; }
    public bool IsRefreshing { get; private set; }

    public event EventHandler? LeaderboardUpdated;

    public LeaderboardService(
        ISettingsService settings,
        IUserIdentityProvider identity,
        string appVersion,
        ILogger<LeaderboardService>? logger = null,
        ISeasonRecapService? seasonRecap = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _logger = logger;
        _seasonRecap = seasonRecap;

        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _httpClient.DefaultRequestHeaders.Add("X-Client-Version", appVersion);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{appVersion}");

        // Auto-refresh every 30 minutes (server caches leaderboard in memory for 30s).
        _refreshTimer = new Timer(
            async _ => await RefreshAsync().ConfigureAwait(false),
            null,
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(30));

        _logger?.LogInformation("LeaderboardService initialized with 30-minute auto-refresh");
    }

    public async Task<bool> RefreshAsync(string? sortBy = null, string? mode = null)
    {
        if (_settings.Current?.OfflineMode == true)
        {
            _logger?.LogDebug("Offline mode enabled, skipping leaderboard refresh");
            return false;
        }

        if (IsRefreshing) return false;

        sortBy ??= CurrentSortBy;
        mode ??= CurrentMode;
        IsRefreshing = true;

        try
        {
            _logger?.LogDebug("Fetching leaderboard with sort_by={SortBy}, mode={Mode}", sortBy, mode);

            var season = mode == "all-time" ? "all-time" : DateTime.UtcNow.ToString("yyyy-MM");
            var unifiedId = _identity.UnifiedUserId;
            var url = $"{ProxyBaseUrl}/v3/leaderboard?season={season}&limit=200";
            if (!string.IsNullOrEmpty(unifiedId))
                url += $"&unified_id={Uri.EscapeDataString(unifiedId)}";

            var response = await _httpClient.GetAsync(url).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                _logger?.LogWarning("Leaderboard fetch failed: {Status} - {Error}", response.StatusCode, errorBody);
                LastRefreshError = $"Server returned {response.StatusCode}";
                return false;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = JsonConvert.DeserializeObject<LeaderboardResponse>(json);

            if (result?.Entries != null)
            {
                lock (_lock)
                {
                    _entries = result.Entries;
                }
                TotalUsers = result.TotalUsers;
                OnlineUsers = result.OnlineUsers;
                YourRank = result.YourRank;
                YourTotal = result.YourTotal;

                // Season Recap: client-sampled season peak rank. Only the monthly board maps to a season.
                if (mode != "all-time" && YourRank.HasValue)
                    _seasonRecap?.SampleRank(YourRank.Value, YourTotal ?? TotalUsers);

                CurrentSortBy = sortBy;
                CurrentMode = mode;
                LastRefreshTime = DateTime.Now;
                LastRefreshError = null;

                _logger?.LogInformation(
                    "Leaderboard refreshed: {Count} entries, {Total} total users, {Online} online, sorted by {SortBy}",
                    result.Entries.Count, TotalUsers, OnlineUsers, sortBy);

                LeaderboardUpdated?.Invoke(this, EventArgs.Empty);
                return true;
            }

            LastRefreshError = "Invalid response from server";
            return false;
        }
        catch (TaskCanceledException)
        {
            _logger?.LogWarning("Leaderboard fetch timed out");
            LastRefreshError = "Request timed out";
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to fetch leaderboard");
            LastRefreshError = ex.Message;
            return false;
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    public async Task<UserLookupResult?> LookupUserAsync(string displayName)
    {
        try
        {
            var url = $"{ProxyBaseUrl}/user/lookup?display_name={Uri.EscapeDataString(displayName)}";
            var response = await _httpClient.GetAsync(url).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("User lookup failed: {Status} for {Name}", response.StatusCode, displayName);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = JsonConvert.DeserializeObject<UserLookupResult>(json);

            _logger?.LogDebug("User lookup successful: {Name}, Online={Online}, Avatar={HasAvatar}",
                displayName, result?.IsOnline, !string.IsNullOrEmpty(result?.AvatarUrl));

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "User lookup failed for {Name}", displayName);
            return null;
        }
    }

    public int GetPlayerPercentile()
    {
        try
        {
            if (YourRank.HasValue && YourTotal.HasValue && YourTotal.Value > 0)
            {
                var percentile = (int)Math.Ceiling((double)YourRank.Value / YourTotal.Value * 100);
                var clampedPercentile = Math.Min(99, Math.Max(1, percentile));

                _logger?.LogDebug("GetPlayerPercentile: Server rank {Position}/{Total} = Top {Percentile}%",
                    YourRank.Value, YourTotal.Value, clampedPercentile);

                return clampedPercentile;
            }

            List<LeaderboardEntry> localEntries;
            lock (_lock) localEntries = _entries.ToList();

            if (localEntries.Count == 0 || TotalUsers == 0)
            {
                _logger?.LogDebug("GetPlayerPercentile: No entries ({Count}) or users ({Total})", localEntries.Count, TotalUsers);
                return 0;
            }

            var unifiedId = _identity.UnifiedUserId;
            var discordId = _identity.DiscordId;
            var displayName = _identity.DisplayName;

            int position = -1;
            for (int i = 0; i < localEntries.Count; i++)
            {
                var entry = localEntries[i];
                if (!string.IsNullOrEmpty(unifiedId) && entry.UnifiedId == unifiedId)
                {
                    position = i + 1;
                    break;
                }
                if (!string.IsNullOrEmpty(discordId) && entry.DiscordId == discordId)
                {
                    position = i + 1;
                    break;
                }
                if (!string.IsNullOrEmpty(displayName) && string.Equals(entry.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    position = i + 1;
                    break;
                }
            }

            if (position <= 0)
            {
                _logger?.LogDebug("GetPlayerPercentile: Player not found in leaderboard");
                return 0;
            }

            var fallbackPercentile = (int)Math.Ceiling((double)position / TotalUsers * 100);
            var clampedFallback = Math.Min(99, Math.Max(1, fallbackPercentile));

            _logger?.LogDebug("GetPlayerPercentile: Fallback scan rank {Position}/{Total} = Top {Percentile}%",
                position, TotalUsers, clampedFallback);

            return clampedFallback;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to calculate player percentile");
            return 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _refreshTimer?.Dispose();
        _httpClient.Dispose();
        _logger?.LogDebug("LeaderboardService disposed");
    }

    private class LeaderboardResponse
    {
        [JsonProperty("entries")]
        public List<LeaderboardEntry>? Entries { get; set; }

        [JsonProperty("total_users")]
        public int TotalUsers { get; set; }

        [JsonProperty("online_users")]
        public int OnlineUsers { get; set; }

        [JsonProperty("sort_by")]
        public string? SortBy { get; set; }

        [JsonProperty("fetched_at")]
        public string? FetchedAt { get; set; }

        [JsonProperty("your_rank")]
        public int? YourRank { get; set; }

        [JsonProperty("your_total")]
        public int? YourTotal { get; set; }
    }
}
