using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Core.Services.AvailableSubjects
{
    /// <summary>
    /// SP5 layer 3 — Available Subjects directory client. Polls
    /// /v2/directory/list every 15s while the tab is visible (StartPolling)
    /// and surfaces an ObservableCollection&lt;AvailableSubject&gt; the UI
    /// DataTemplate binds to. Claim flow returns the one-click pairing URL
    /// from the proxy; callers open it via the platform browser host and
    /// never log it.
    ///
    /// Auth: dual-header X-Auth-Token + X-Caller-Unified-Id (the proxy's
    /// SP5L3 retrofit accepts this in addition to Supabase JWT). The caller's
    /// own unified_id goes in X-Caller-Unified-Id; body.unified_id (claim
    /// only) is the SUBJECT being claimed — separate identity.
    /// </summary>
    public sealed class AvailableSubjectsService : IAvailableSubjectsService
    {
        private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";
        private const double PollIntervalSeconds = 15.0;

        private readonly HttpClient _httpClient;
        private readonly ISettingsService _settingsService;
        private readonly IAppLogger _logger;
        private readonly IUiDispatcher _uiDispatcher;
        private readonly IScheduler _scheduler;
        private readonly IUpdateInstaller? _updateInstaller;

        private IDisposable? _pollTimer;
        private CancellationTokenSource? _inFlightCts;
        private bool _disposed;

        public ObservableCollection<AvailableSubject> Entries { get; } = new();

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            private set { _isLoading = value; OnPropertyChanged(); }
        }

        private bool _isEmpty = true;
        public bool IsEmpty
        {
            get => _isEmpty;
            private set { _isEmpty = value; OnPropertyChanged(); }
        }

        private bool _hasError;
        public bool HasError
        {
            get => _hasError;
            private set { _hasError = value; OnPropertyChanged(); }
        }

        public bool IsPolling => _pollTimer != null;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public AvailableSubjectsService(
            ISettingsService settingsService,
            IAppLogger logger,
            IUiDispatcher uiDispatcher,
            IScheduler scheduler,
            IUpdateInstaller? updateInstaller = null)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            _updateInstaller = updateInstaller;

            var version = _updateInstaller?.GetInstalledVersion() ?? "6.1.4";
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _httpClient.DefaultRequestHeaders.Add("X-Client-Version", version);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{version}");
        }

        /// <inheritdoc />
        public void StartPolling()
        {
            if (_disposed) return;
            if (_pollTimer != null) return;

            _ = RefreshAsync();
            _pollTimer = _scheduler.StartPeriodicTimer(TimeSpan.FromSeconds(PollIntervalSeconds), () =>
            {
                _ = RefreshAsync();
            });
            _logger.Information("[AvailableSubjects] polling started");
        }

        /// <inheritdoc />
        public void StopPolling()
        {
            if (_pollTimer != null)
            {
                _pollTimer.Dispose();
                _pollTimer = null;
            }
            try { _inFlightCts?.Cancel(); } catch { /* swallow */ }
        }

        /// <inheritdoc />
        public async Task RefreshAsync()
        {
            if (_disposed) return;

            var unifiedId = _settingsService.Current?.UnifiedId;
            var token = _settingsService.Current?.AuthToken;
            if (string.IsNullOrEmpty(unifiedId) || string.IsNullOrEmpty(token))
            {
                _uiDispatcher.Post(() =>
                {
                    HasError = true;
                    IsEmpty = true;
                });
                return;
            }

            try { _inFlightCts?.Cancel(); } catch { }
            _inFlightCts = new CancellationTokenSource();
            var ct = _inFlightCts.Token;

            _uiDispatcher.Post(() => IsLoading = true);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{ProxyBaseUrl}/v2/directory/list");
                request.Headers.Add("X-Auth-Token", token);
                request.Headers.Add("X-Caller-Unified-Id", unifiedId);

                using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested) return;

                if (!response.IsSuccessStatusCode)
                {
                    _logger.Warning("[AvailableSubjects] list failed: {Status}", response.StatusCode);
                    _uiDispatcher.Post(() => HasError = true);
                    return;
                }

                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested) return;

                var parsed = JObject.Parse(body);
                var arr = parsed["entries"] as JArray ?? new JArray();
                var newEntries = arr.Select(ParseEntry).Where(e => e != null).Cast<AvailableSubject>().ToList();

                _uiDispatcher.Post(() =>
                {
                    ReconcileEntries(newEntries);
                    IsEmpty = Entries.Count == 0;
                    HasError = false;
                });
            }
            catch (TaskCanceledException) { /* expected on cancel */ }
            catch (OperationCanceledException) { /* expected on cancel */ }
            catch (Exception ex)
            {
                _logger.Warning(ex, "[AvailableSubjects] refresh error");
                _uiDispatcher.Post(() => HasError = true);
            }
            finally
            {
                _uiDispatcher.Post(() => IsLoading = false);
            }
        }

        /// <inheritdoc />
        public async Task<string?> TryClaimAsync(string subjectUnifiedId)
        {
            if (_disposed) return null;

            var unifiedId = _settingsService.Current?.UnifiedId;
            var token = _settingsService.Current?.AuthToken;
            if (string.IsNullOrEmpty(unifiedId) || string.IsNullOrEmpty(token))
            {
                _logger.Warning("[AvailableSubjects] claim called without auth state");
                return null;
            }
            if (string.IsNullOrEmpty(subjectUnifiedId)) return null;

            try
            {
                var jsonBody = JsonConvert.SerializeObject(new { unified_id = subjectUnifiedId });
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}/v2/directory/claim")
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("X-Auth-Token", token);
                request.Headers.Add("X-Caller-Unified-Id", unifiedId);

                using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

                if ((int)response.StatusCode == 409)
                {
                    _ = RefreshAsync();
                    return null;
                }
                if (!response.IsSuccessStatusCode)
                {
                    _logger.Warning("[AvailableSubjects] claim failed: {Status}", response.StatusCode);
                    return null;
                }

                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var parsed = JObject.Parse(body);
                var url = parsed["session_url"]?.ToString();
                if (string.IsNullOrEmpty(url))
                {
                    _logger.Warning("[AvailableSubjects] claim 200 without session_url");
                    return null;
                }

                _ = RefreshAsync();
                return url;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "[AvailableSubjects] claim error");
                return null;
            }
        }

        private static AvailableSubject? ParseEntry(JToken t)
        {
            try
            {
                var tagsArr = t["tags"] as JArray;
                var tags = tagsArr != null
                    ? new ObservableCollection<string>(tagsArr.Select(x => x.ToString()).Where(s => !string.IsNullOrEmpty(s)))
                    : new ObservableCollection<string>();

                return new AvailableSubject
                {
                    UnifiedId = t["unified_id"]?.ToString() ?? "",
                    DisplayName = t["display_name"]?.ToString() ?? "Anonymous",
                    Level = t["level"]?.Value<int>() ?? 1,
                    Tags = tags,
                    StatusText = t["status_text"]?.ToString() ?? "",
                    Tier = t["tier"]?.ToString() ?? "light",
                    Claimed = t["claimed"]?.Value<bool>() ?? false
                };
            }
            catch { return null; }
        }

        private void ReconcileEntries(List<AvailableSubject> newEntries)
        {
            var existing = Entries.ToDictionary(e => e.UnifiedId, e => e);

            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (!newEntries.Any(n => n.UnifiedId == Entries[i].UnifiedId))
                    Entries.RemoveAt(i);
            }

            for (int idx = 0; idx < newEntries.Count; idx++)
            {
                var n = newEntries[idx];
                if (existing.TryGetValue(n.UnifiedId, out var e))
                {
                    e.Claimed = n.Claimed;
                    e.StatusText = n.StatusText;
                    e.Tags = n.Tags;

                    var currentIndex = Entries.IndexOf(e);
                    if (currentIndex != idx && currentIndex >= 0 && idx < Entries.Count)
                    {
                        Entries.Move(currentIndex, idx);
                    }
                }
                else
                {
                    if (idx >= Entries.Count) Entries.Add(n);
                    else Entries.Insert(idx, n);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopPolling();
            try { _inFlightCts?.Dispose(); } catch { }
            try { _httpClient?.Dispose(); } catch { }
        }
    }
}
