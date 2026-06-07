using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.AiEnrichment;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.AIService.Enrichment;
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
        public enum DiagnosticCategory
        {
            Success,
            MissingConfiguration,
            Endpoint,
            Authentication,
            Model,
            Timeout,
            Connection,
            Http,
            Unknown
        }

        public sealed record ConnectionDiagnosticResult(
            bool Success,
            DiagnosticCategory Category,
            string Message,
            int? HttpStatusCode = null,
            long? ElapsedMs = null);

        private readonly HttpClient _httpClient;
        private readonly BambiSprite _bambiSprite;
        private readonly IAiResponseParser _parser;
        private readonly KnowledgeService _knowledgeService;
        private readonly IPromptService _promptService;

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

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };

            // Mirrors LocalAiService: parse replies for the { response, effects } shape
            // and reuse the same enrichment prompt the local provider injects when the
            // Beta Lab AI-effects master toggle is on. This is the only path that turns
            // the OpenAI-compatible provider into an effects-capable companion; awareness
            // / lockscreen / keyword paths below continue to use the plain SendChatAsync
            // overload because they don't carry user intent to trigger effects.
            _parser = new AiResponseParser(GetFallbackResponse);
            _knowledgeService = new KnowledgeService();
            _promptService = new PromptService();
        }

        private static Uri GetConfiguredEndpointBaseUri()
        {
            var raw = Settings?.OpenAiCompatibleEndpoint?.Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                // Fallback: standard OpenAI base, though without a key it will not be available.
                return new Uri("https://api.openai.com/v1/");
            }

            if (!Uri.TryCreate(raw, UriKind.Absolute, out var parsed))
            {
                App.Logger?.Warning("OpenAiCompatibleService: invalid endpoint '{Endpoint}', falling back to OpenAI base", raw);
                return new Uri("https://api.openai.com/v1/");
            }

            // Some users paste the full chat-completions URL. Normalize to its base so
            // both ".../api/v1" and ".../api/v1/" (and full endpoint forms) work.
            var path = parsed.AbsolutePath.TrimEnd('/');
            if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(0, path.Length - "/chat/completions".Length);
            }

            if (string.IsNullOrEmpty(path))
            {
                path = "/";
            }

            if (!path.EndsWith("/", StringComparison.Ordinal))
            {
                path += "/";
            }

            var builder = new UriBuilder(parsed)
            {
                Path = path,
                Query = string.Empty,
                Fragment = string.Empty
            };

            return builder.Uri;
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

        public async Task<ConnectionDiagnosticResult> TestEndpointAsync(CancellationToken cancellationToken = default)
        {
            var endpointRaw = Settings?.OpenAiCompatibleEndpoint?.Trim();
            if (string.IsNullOrWhiteSpace(endpointRaw))
            {
                return new ConnectionDiagnosticResult(
                    Success: false,
                    Category: DiagnosticCategory.MissingConfiguration,
                    Message: "Endpoint is missing");
            }

            var model = GetConfiguredModel();
            if (string.IsNullOrWhiteSpace(model))
            {
                return new ConnectionDiagnosticResult(
                    Success: false,
                    Category: DiagnosticCategory.MissingConfiguration,
                    Message: "Model is missing");
            }

            var apiKey = GetApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new ConnectionDiagnosticResult(
                    Success: false,
                    Category: DiagnosticCategory.Authentication,
                    Message: "API key is missing or could not be decrypted");
            }

            if (!Uri.TryCreate(endpointRaw, UriKind.Absolute, out _))
            {
                return new ConnectionDiagnosticResult(
                    Success: false,
                    Category: DiagnosticCategory.Endpoint,
                    Message: "Endpoint URL is invalid");
            }

            var baseUri = GetConfiguredEndpointBaseUri();
            var endpointUri = new Uri(baseUri, "chat/completions");

            var payload = new
            {
                model = model,
                max_tokens = 1,
                temperature = 0,
                messages = new[]
                {
                    new { role = "user", content = "ping" }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                sw.Stop();

                if (response.IsSuccessStatusCode)
                {
                    return new ConnectionDiagnosticResult(
                        Success: true,
                        Category: DiagnosticCategory.Success,
                        Message: "Connected",
                        HttpStatusCode: (int)response.StatusCode,
                        ElapsedMs: sw.ElapsedMilliseconds);
                }

                var status = (int)response.StatusCode;
                var bodyLower = body?.ToLowerInvariant() ?? string.Empty;

                if (status == 401 || status == 403)
                {
                    return new ConnectionDiagnosticResult(
                        Success: false,
                        Category: DiagnosticCategory.Authentication,
                        Message: "Authentication failed (invalid API key or unauthorized endpoint)",
                        HttpStatusCode: status,
                        ElapsedMs: sw.ElapsedMilliseconds);
                }

                if (status == 404)
                {
                    return new ConnectionDiagnosticResult(
                        Success: false,
                        Category: DiagnosticCategory.Endpoint,
                        Message: "Endpoint not found (check base URL path, e.g. /api/v1)",
                        HttpStatusCode: status,
                        ElapsedMs: sw.ElapsedMilliseconds);
                }

                if (status == 400 && (bodyLower.Contains("model") || bodyLower.Contains("unknown_model") || bodyLower.Contains("not found")))
                {
                    return new ConnectionDiagnosticResult(
                        Success: false,
                        Category: DiagnosticCategory.Model,
                        Message: "Model is invalid or unavailable on this endpoint",
                        HttpStatusCode: status,
                        ElapsedMs: sw.ElapsedMilliseconds);
                }

                return new ConnectionDiagnosticResult(
                    Success: false,
                    Category: DiagnosticCategory.Http,
                    Message: $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}",
                    HttpStatusCode: status,
                    ElapsedMs: sw.ElapsedMilliseconds);
            }
            catch (TaskCanceledException)
            {
                sw.Stop();
                return new ConnectionDiagnosticResult(
                    Success: false,
                    Category: DiagnosticCategory.Timeout,
                    Message: "Request timed out",
                    ElapsedMs: sw.ElapsedMilliseconds);
            }
            catch (HttpRequestException ex)
            {
                sw.Stop();
                return new ConnectionDiagnosticResult(
                    Success: false,
                    Category: DiagnosticCategory.Connection,
                    Message: $"Connection failed: {ex.Message}",
                    ElapsedMs: sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new ConnectionDiagnosticResult(
                    Success: false,
                    Category: DiagnosticCategory.Unknown,
                    Message: $"Unexpected error: {ex.GetType().Name}",
                    ElapsedMs: sw.ElapsedMilliseconds);
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
            // Legacy two-message path used by awareness / lockscreen / keyword / video
            // reactions. The effects-aware chat path uses <see cref="SendChatWithMessagesAsync"/>
            // so it can include the enrichment block from PromptService and parse the
            // { response, effects } reply shape.
            var messages = new List<MessageDto>
            {
                new("system", systemPrompt),
                new("user", userInput ?? string.Empty)
            };
            return await SendChatWithMessagesAsync(messages).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a multi-turn chat to the configured OpenAI-compatible endpoint. Used by
        /// <see cref="GetBambiReplyExAsync"/> when the Beta Lab AI-effects master toggle
        /// is on, so we can prepend the same <c>[CONTEXT BLOCK — NOT DIALOGUE]</c>
        /// enrichment message the local provider uses (via
        /// <see cref="IPromptService.BuildEnrichmentMessage"/>). Returns the raw assistant
        /// content, or <c>null</c> on transport / HTTP / parse failure. Caller is
        /// responsible for moderation, command parsing, and command dispatch.
        /// </summary>
        private async Task<string?> SendChatWithMessagesAsync(IList<MessageDto> messages)
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
                messages = messages.Select(m => new { role = m.Role, content = m.Content ?? string.Empty }).ToArray()
            };

            var baseUri = GetConfiguredEndpointBaseUri();
            var endpointUri = new Uri(baseUri, "chat/completions");

            BumpDailyCounter();

            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                    };
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                    App.Logger?.Debug("OpenAiCompatibleService: request to {Url} (attempt {Attempt}, msgs={MsgCount})",
                        request.RequestUri, attempt + 1, messages.Count);

                    using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        var status = (int)response.StatusCode;
                        var retryableStatus = status == 429 || status >= 500;

                        if (attempt == 0 && retryableStatus)
                        {
                            await Task.Delay(1200).ConfigureAwait(false);
                            continue;
                        }

                        App.Logger?.Warning("OpenAiCompatibleService: HTTP {Status} from {Endpoint}: {Body}",
                            status,
                            endpointUri,
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
                catch (HttpRequestException) when (attempt == 0)
                {
                    await Task.Delay(1200).ConfigureAwait(false);
                }
                catch (TaskCanceledException) when (attempt == 0)
                {
                    await Task.Delay(1200).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "OpenAiCompatibleService: request failed");
                    return null;
                }
            }

            App.Logger?.Warning("OpenAiCompatibleService: request failed after retry");
            return null;
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

            if (App.Settings?.Current?.OfflineMode == true)
                return new AiReplyResult(GetFallbackResponse(), IsAiGenerated: false, Refusal: null);

            // INPUT MODERATION (Layer 1 — code-side, prompt cannot bypass). Same
            // semantics as the local provider: hard categories block before the HTTP
            // call; refusal sentinel surfaces only when the caller is the chat UI.
            var guard = App.ModerationGuard;
            var modelName = GetConfiguredModel();
            var modelHint = "openai-compatible:" + (string.IsNullOrWhiteSpace(modelName) ? "unknown" : modelName);
            if (guard != null)
            {
                var inputCheck = guard.CheckInput(userInput ?? string.Empty);
                if (!inputCheck.Allow && inputCheck.Category.HasValue)
                {
                    App.ModerationLog?.Record(inputCheck.Category.Value, source: "input", modelHint: modelHint);
                    App.ModerationCounter?.RecordHit(inputCheck.Category.Value, "input:openai-compatible");
                    App.Logger?.Information("OpenAiCompatibleService: input blocked by ModerationGuard (category={Cat})",
                        inputCheck.Category);
                    return new AiReplyResult(
                        string.Empty,
                        IsAiGenerated: false,
                        Refusal: new ModerationRefusalInfo(Category: inputCheck.Category, Source: ModerationSource.Input));
                }
                if (inputCheck.Allow && inputCheck.Category == ProhibitedCategory.ProfessionalAdvice)
                {
                    App.ModerationLog?.Record(ProhibitedCategory.ProfessionalAdvice, source: "input", modelHint: modelHint);
                }
            }

            // Build the messages list. Mirrors LocalAiService: system prompt at index 0,
            // optional enrichment at index 1 (only when effects are on), then the user
            // turn. The OpenAI-compatible provider is stateless across calls, so we don't
            // carry any prior user/assistant turns here — each request is independent.
            var effectsEnabled = App.Settings?.Current?.CompanionPrompt?.AllowAiToControlEffects == true;
            var prompt = _bambiSprite.GetSystemPrompt();

            var messages = new List<MessageDto>
            {
                new("system", prompt)
            };

            if (effectsEnabled)
            {
                var currentTime = DateTime.Now.ToString("yyyy-M-dd dddd h:mm:ss tt");
                var facts = _knowledgeService.GetKnowledge("");
                var factsJson = JsonSerializer.Serialize(facts);
                var enrichment = _promptService.BuildEnrichmentMessage(factsJson, currentTime);
                messages.Add(new MessageDto("user", enrichment.Content));
            }

            messages.Add(new MessageDto("user", userInput ?? string.Empty));

            var reply = await SendChatWithMessagesAsync(messages).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(reply))
                return new AiReplyResult(GetFallbackResponse(), IsAiGenerated: false, Refusal: null);

            // Parse the { response, effects } shape. When the model returns plain text
            // (the fallback case the plan explicitly calls out) the parser leaves
            // CleanText as the original reply and Commands empty — that's the
            // supported degradation path. We do NOT dispatch commands before the
            // output moderation check below.
            var parsed = _parser.Parse(reply);
            var commands = parsed.Commands;

            if (effectsEnabled)
            {
                App.Logger?.Information("OpenAiCompatibleService: parsed {Count} command(s) from response", commands.Count);
            }

            // OUTPUT MODERATION (Layer 1). Scan the user-visible text — the JSON
            // effects wrapper is already stripped by the parser. If the model produced
            // prohibited content we discard the whole turn (no commands executed, no
            // text displayed). Matches LocalAiService ordering so effects cannot fire
            // from a reply that would otherwise be rejected.
            if (guard != null)
            {
                var outputCheck = guard.CheckOutput(parsed.CleanText ?? string.Empty);
                if (!outputCheck.Allow && outputCheck.Category.HasValue)
                {
                    App.ModerationLog?.Record(outputCheck.Category.Value, source: "output", modelHint: modelHint);
                    App.Logger?.Information("OpenAiCompatibleService: output blocked by ModerationGuard (category={Cat})",
                        outputCheck.Category);
                    return new AiReplyResult(
                        string.Empty,
                        IsAiGenerated: false,
                        Refusal: new ModerationRefusalInfo(Category: outputCheck.Category, Source: ModerationSource.Output));
                }
                if (outputCheck.Allow && outputCheck.Category == ProhibitedCategory.ProfessionalAdvice)
                {
                    App.ModerationLog?.Record(ProhibitedCategory.ProfessionalAdvice, source: "output", modelHint: modelHint);
                }
            }

            // Dispatch any parsed effects commands. Per-effect gating, master toggle,
            // and the per-response cap are all enforced inside AiCommandService — the
            // provider must NOT re-implement them.
            if (commands.Count > 0 && App.Commands != null)
            {
                App.Commands.BeginBatch();
                foreach (var cmd in commands) App.Commands.ExecuteCommand(cmd);
            }

            return new AiReplyResult(parsed.CleanText, IsAiGenerated: true, Refusal: null);
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
