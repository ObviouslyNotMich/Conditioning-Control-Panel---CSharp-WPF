using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Services.AIService;
using ConditioningControlPanel.Core.Services.Moderation;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using ConditioningControlPanel;

namespace ConditioningControlPanel.Avalonia.Services.Quiz
{
    /// <summary>
    /// Minimal Avalonia <see cref="IAiService"/> implementation that powers the quiz's
    /// local Ollama path. It advertises <see cref="IsAvailable"/> as <c>false</c> so the
    /// companion chat UI does not route normal chat through this stub; only the raw
    /// chat-completion seam is implemented.
    /// </summary>
    public sealed class AvaloniaQuizAiService : IAiService
    {
        private readonly ISettingsService _settingsService;
        private readonly ILogger<AvaloniaQuizAiService>? _logger;

        public AvaloniaQuizAiService(ISettingsService settingsService, ILogger<AvaloniaQuizAiService>? logger = null)
        {
            _settingsService = settingsService;
            _logger = logger;
        }

        public bool IsAvailable => false;

        public int DailyRequestsRemaining => 0;

        public Task<string> GetBambiReplyAsync(string userInput, bool isUserMessage = false)
            => Task.FromResult(string.Empty);

        public Task<AiReplyResult> GetBambiReplyExAsync(string userInput, bool isUserMessage = false)
            => Task.FromResult(new AiReplyResult(string.Empty, IsAiGenerated: false, Refusal: null));

        public Task<string?> GetAwarenessReactionAsync(string detectedName, string category, string serviceName = "",
            string pageTitle = "")
            => Task.FromResult<string?>(null);

        public Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration)
            => Task.FromResult<string?>(null);

        public Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null)
            => Task.FromResult<string?>(null);

        public Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null)
            => Task.FromResult<string?>(null);

        public Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null)
            => Task.FromResult<string?>(null);

        /// <summary>
        /// Stateless raw Ollama chat completion for the quiz. Mirrors the companion's
        /// local provider settings (host/model from <see cref="CompanionPromptSettings"/>)
        /// without bringing in the companion persona or persistent history.
        /// </summary>
        public async Task<string?> GetRawChatCompletionAsync(IEnumerable<(string role, string content)> messages, double temperature = 0.8)
        {
            var settings = _settingsService.Current?.CompanionPrompt;
            if (settings == null)
            {
                _logger?.LogDebug("AvaloniaQuizAiService: no companion prompt settings available");
                return null;
            }

            var host = NormalizeHost(settings.AiOllamaHost);
            var model = settings.AiModel;
            if (string.IsNullOrWhiteSpace(model))
            {
                _logger?.LogDebug("AvaloniaQuizAiService: no local model configured");
                return null;
            }

            try
            {
                using var http = new HttpClient
                {
                    BaseAddress = new Uri(host),
                    Timeout = TimeSpan.FromSeconds(45)
                };

                var payload = new
                {
                    model,
                    messages = messages.Select(m => new { role = m.role, content = m.content ?? string.Empty }).ToArray(),
                    stream = false,
                    think = false,
                    options = new { temperature }
                };
                var json = JsonSerializer.Serialize(payload);

                using var req = new HttpRequestMessage(HttpMethod.Post, "api/chat")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                using var resp = await http.SendAsync(req);
                var body = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    _logger?.LogWarning("AvaloniaQuizAiService: Ollama returned HTTP {Status}: {Body}",
                        (int)resp.StatusCode, Truncate(body, 200));
                    return null;
                }

                var content = ExtractContent(body);
                return string.IsNullOrWhiteSpace(content) ? null : content;
            }
            catch (TaskCanceledException)
            {
                _logger?.LogWarning("AvaloniaQuizAiService: request timed out");
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "AvaloniaQuizAiService: raw chat completion failed");
                return null;
            }
        }

        public void Dispose()
        {
            // No unmanaged/stateful resources.
        }

        private static string NormalizeHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return "http://localhost:11434/";
            return host.EndsWith("/", StringComparison.Ordinal) ? host : host + "/";
        }

        private static string? ExtractContent(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("message", out var msg) &&
                    msg.TryGetProperty("content", out var c))
                {
                    return c.GetString();
                }
            }
            catch { }
            return null;
        }

        private static string Truncate(string s, int n)
            => s.Length <= n ? s : s.Substring(0, n) + "...";
    }
}
