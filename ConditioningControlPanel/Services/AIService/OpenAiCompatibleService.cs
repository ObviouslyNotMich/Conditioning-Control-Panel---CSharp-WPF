using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Moderation;

namespace ConditioningControlPanel.Services.AIService
{
    /// <summary>
    /// IAiService implementation that talks to an OpenAI-compatible chat completions endpoint.
    /// Uses the same BambiSprite system prompt and awareness formatting as the cloud provider,
    /// but sends requests directly to a user-configured HTTP endpoint with a bearer API key.
    /// Daily limits are controlled via CompanionPromptSettings.DailyRequestLimit (0 = unlimited).
    /// </summary>
    public sealed class OpenAiCompatibleService : IAiService
    {
        private readonly HttpClient _httpClient;
        private readonly BambiSprite _bambiSprite;

        private int _dailyRequestCount;
        private DateTime _lastResetDate;

        private static CompanionPromptSettings? Settings => App.Settings?.Current?.CompanionPrompt;

        public bool IsAvailable
        {
            get
            {
                if (App.Settings?.Current?.OfflineMode == true) return false;

                var s = Settings;
                if (s == null) return false;

                if (string.IsNullOrWhiteSpace(s.OpenAiCompatibleEndpoint)) return false;
                if (string.IsNullOrWhiteSpace(s.OpenAiCompatibleApiKey)) return false;

                ResetDailyCounterIfNeeded();

                var limit = s.DailyRequestLimit;
                if (limit <= 0) return true; // unlimited

                return _dailyRequestCount < limit;
            }
        }

        public int DailyRequestsRemaining
        {
            get
            {
                var s = Settings;
                if (s == null) return 0;

                ResetDailyCounterIfNeeded();

                var limit = s.DailyRequestLimit;
                if (limit <= 0) return -1; // unlimited

                var remaining = limit - _dailyRequestCount;
                return remaining < 0 ? 0 : remaining;
            }
        }

        public OpenAiCompatibleService()
        {
            _bambiSprite = new BambiSprite();
            _lastResetDate = DateTime.Today;
            _dailyRequestCount = 0;

            var baseUrl = GetConfiguredEndpoint();

            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(60)
            };
        }

        private static string GetConfiguredEndpoint()
        {
            var endpoint = Settings?.OpenAiCompatibleEndpoint?.Trim();
            if (string.IsNullOrEmpty(endpoint))
            {
                // Fallback: standard OpenAI base, though without a key it will not be available.
                return "https://api.openai.com/v1";
            }

            // Ensure no trailing spaces; HttpClient will validate the URI format.
            return endpoint;
        }

        private static string GetConfiguredModel()
        {
            var model = Settings?.OpenAiCompatibleModel;
            if (string.IsNullOrWhiteSpace(model))
            {
                // Reasonable default; kept here only as a suggestion.
                return "gpt-4o-mini";
            }

            return model;
        }

        private static string? GetApiKey()
        {
            var raw = Settings?.OpenAiCompatibleApiKey;
            if (string.IsNullOrWhiteSpace(raw)) return null;

            try
            {
                // Value is stored encrypted at rest; decrypt on use.
                return SecureStringHelper.Unprotect(raw);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "OpenAiCompatibleService: failed to decrypt API key");
                return null;
            }
        }

        private void ResetDailyCounterIfNeeded()
        {
            if (DateTime.Today <= _lastResetDate) return;

            _dailyRequestCount = 0;
            _lastResetDate = DateTime.Today;
            App.Logger?.Debug("OpenAiCompatibleService: Daily request count reset");
        }

        private void BumpDailyCounter()
        {
            ResetDailyCounterIfNeeded();
            _dailyRequestCount++;
        }

        private async Task<string?> SendChatAsync(string systemPrompt, string userInput)
        {
            if (App.Settings?.Current?.OfflineMode == true)
            {
                App.Logger?.Debug("OpenAiCompatibleService: Offline mode enabled, skipping AI request");
                return null;
            }

            var apiKey = GetApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                App.Logger?.Debug("OpenAiCompatibleService: missing API key");
                return null;
            }

            ResetDailyCounterIfNeeded();
            var limit = Settings?.DailyRequestLimit ?? 0;
            if (limit > 0 && _dailyRequestCount >= limit)
            {
                App.Logger?.Debug("OpenAiCompatibleService: daily limit reached ({Limit})", limit);
                return null;
            }

            var model = GetConfiguredModel();

            var payload = new
            {
                model = model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userInput }
                }
            };

            var baseUri = _httpClient.BaseAddress ?? new Uri("https://api.openai.com/v1/");

            // Ensure the base URI is treated as a directory, not a file, so that
            // a path like "/api/v1" is preserved when appending additional segments.
            Uri effectiveBaseUri;
            if (!baseUri.AbsoluteUri.EndsWith("/"))
            {
                effectiveBaseUri = new Uri(baseUri.AbsoluteUri + "/");
            }
            else
            {
                effectiveBaseUri = baseUri;
            }

            var endpointUri = new Uri(effectiveBaseUri, "chat/completions");

            using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            try
            {
                BumpDailyCounter();

                App.Logger?.Debug("OpenAiCompatibleService: request to {Url}", request.RequestUri);

                using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    App.Logger?.Warning("OpenAiCompatibleService: HTTP {Status} from {Endpoint}: {Body}",
                        (int)response.StatusCode,
                        _httpClient.BaseAddress,
                        json);
                    return null;
                }

                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                {
                    App.Logger?.Warning("OpenAiCompatibleService: response has no choices");
                    return null;
                }

                var first = choices[0];
                if (!first.TryGetProperty("message", out var message) ||
                    !message.TryGetProperty("content", out var contentElement))
                {
                    App.Logger?.Warning("OpenAiCompatibleService: response missing message.content");
                    return null;
                }

                var content = contentElement.GetString();
                return string.IsNullOrWhiteSpace(content) ? null : content;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "OpenAiCompatibleService: request failed");
                return null;
            }
        }

        private static string GetFallbackResponse()
        {
            if (App.Mods?.IsBambiMode == true)
                return "Bambi's head is so empty right now~ *giggles*";
            return "...";
        }

        public async Task<string> GetBambiReplyAsync(string userInput, bool isUserMessage = false)
        {
            var result = await GetBambiReplyExAsync(userInput, isUserMessage).ConfigureAwait(false);
            if (result.Refusal != null)
            {
                return result.Refusal.Source == ModerationSource.Input
                    ? ModerationRefusal.InputSentinel
                    : ModerationRefusal.OutputSentinel;
            }
            return result.Text;
        }

        public async Task<AiReplyResult> GetBambiReplyExAsync(string userInput, bool isUserMessage = false)
        {
            _ = isUserMessage; // queueing semantics are local-only

            var prompt = _bambiSprite.GetSystemPrompt();
            var reply = await SendChatAsync(prompt, userInput).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(reply))
                return new AiReplyResult(GetFallbackResponse(), IsAiGenerated: false, Refusal: null);

            return new AiReplyResult(reply, IsAiGenerated: true, Refusal: null);
        }

        public async Task<string?> GetAwarenessReactionAsync(string detectedName, string category, string serviceName = "", string pageTitle = "")
        {
            var prompt = _bambiSprite.GetSystemPrompt();

            var website = string.IsNullOrEmpty(serviceName) ? detectedName : serviceName;
            var tabName = string.IsNullOrEmpty(pageTitle) ? detectedName : pageTitle;

            var userInput = $"[Category: {category} | App: {website} | Title: {tabName} | Duration: 0m]";

            return await SendChatAsync(prompt, userInput).ConfigureAwait(false);
        }

        public async Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration)
        {
            var prompt = _bambiSprite.GetSystemPrompt();

            string durationText;
            if (duration.TotalMinutes < 1)
                durationText = $"{(int)duration.TotalSeconds}s";
            else if (duration.TotalMinutes < 60)
                durationText = $"{(int)duration.TotalMinutes}m";
            else
                durationText = $"{(int)duration.TotalHours}h";

            var userInput = $"[Category: {category} | App: {displayName} | Title: {displayName} | Duration: {durationText}]";

            return await SendChatAsync(prompt, userInput).ConfigureAwait(false);
        }

        public async Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null)
        {
            var systemPrompt = _bambiSprite.GetSystemPrompt();
            var userInput = string.IsNullOrEmpty(promptTemplate)
                ? $"You just caught the user on the word '{keyword}'. React in character, one short line."
                : promptTemplate.Replace("{keyword}", keyword);

            return await SendChatAsync(systemPrompt, userInput).ConfigureAwait(false);
        }

        public async Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null)
        {
            var systemPrompt = _bambiSprite.GetSystemPrompt();
            string userInput;
            if (string.IsNullOrEmpty(promptTemplate))
            {
                userInput = $"The user made {mistakes} mistakes in '{sentance}' for the lock screen. They had to type it {amount} of time. React in character, one short line.";
            }
            else
            {
                userInput = promptTemplate.Replace("{sentance}", sentance);
                userInput = userInput.Replace("{mistakes}", mistakes.ToString());
                userInput = userInput.Replace("{amount}", amount.ToString());
            }

            return await SendChatAsync(systemPrompt, userInput).ConfigureAwait(false);
        }

        public async Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null)
        {
            var systemPrompt = _bambiSprite.GetSystemPrompt();
            var userInput = string.IsNullOrEmpty(promptTemplate)
                ? $"The user has just finished the mandatory video {title}. React in character, one short line."
                : promptTemplate.Replace("{title}", title);

            return await SendChatAsync(systemPrompt, userInput).ConfigureAwait(false);
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
