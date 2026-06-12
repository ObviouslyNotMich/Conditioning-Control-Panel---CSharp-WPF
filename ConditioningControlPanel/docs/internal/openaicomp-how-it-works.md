# OpenAI-Compatible Provider — How It Works (current `feature/openaicomp`)

> Snapshot of the branch state before splitting D3D overlay work out.

## 1. High-level architecture

The app uses a single `IAiService` abstraction that the rest of the UI calls into. `AiServiceStrategy` picks the concrete implementation based on `App.Settings.Current.CompanionPrompt.AiProvider`:

```
UI / Autonomy / KeywordTrigger / etc.
    │
    ▼
AiServiceStrategy (IAiService)
    │
    ├── Cloud ──► AiService (hosted proxy)
    ├── Local ──► LocalAiService (Ollama)
    └── Custom ──► OpenAiCompatibleService (OpenAI-compatible HTTP endpoint)
```

Switching providers is live; the strategy lazily constructs the chosen provider on first use.

## 2. Settings model

`CompanionPromptSettings` (in `Models/CompanionPromptSettings.cs`) stores the provider-specific fields:

| Field | Purpose |
|-------|---------|
| `AiProvider` | `Cloud`, `Local`, or `OpenAiCompatible` |
| `OpenAiCompatibleEndpoint` | Base URL, e.g. `https://api.openai.com/v1` |
| `OpenAiCompatibleApiKey` | Bearer token, encrypted at rest with `SecureStringHelper.Protect` |
| `OpenAiCompatibleModel` | Model name, e.g. `gpt-4o-mini` |
| `DailyRequestLimit` | Client-side daily cap; `0` = unlimited |

The API key is stored encrypted and decrypted on demand in `OpenAiCompatibleService.GetApiKey()`.

## 3. Endpoint normalization

`OpenAiCompatibleService.GetConfiguredEndpointBaseUri()` handles messy user input:

- Falls back to `https://api.openai.com/v1/` if blank/invalid.
- Strips a trailing `/chat/completions` segment if the user pasted the full endpoint URL.
- Ensures the path ends with `/`.

All requests are then sent to `new Uri(baseUri, "chat/completions")`.

## 4. Request flow

`OpenAiCompatibleService.SendChatAsync(systemPrompt, userInput)` builds a minimal JSON payload:

```json
{
  "model": "<model>",
  "messages": [
    { "role": "system", "content": "<systemPrompt>" },
    { "role": "user", "content": "<userInput>" }
  ]
}
```

It adds a `Bearer <key>` authorization header, retries once on `429`/`5xx`/`HttpRequestException`/`TaskCanceledException`, and extracts `choices[0].message.content`.

`TestEndpointAsync()` sends a tiny ping (`max_tokens: 1`, `temperature: 0`, `messages: [{user:"ping"}]`) and returns a typed diagnostic result (`ConnectionDiagnosticResult`) covering missing config, auth failures, 404, invalid model, timeouts, and connection errors.

## 5. UI wiring

`MainWindow.xaml` exposes the provider controls inside `OpenAiCompatibleConfigPanel`:

- Endpoint URL text box
- API key password box
- Model text box
- **Test connection** button → `BtnTestOpenAiConnection_Click`
- **Daily request limit** input (visible only when the custom provider is selected)

Radio buttons (`RadioAiOff`, `RadioAiCloud`, `RadioAiLocal`, `RadioAiOpenAiCompatible`) switch `AiProvider` and toggle panel visibility.

The test-connection button uses `TestEndpointAsync()` and shows latency / HTTP status / error messages in `TxtOpenAiHealthStatus`.

## 6. Daily request limit

`DailyRequestLimit` is enforced client-side in `OpenAiCompatibleService.IsAvailable` and `DailyRequestsRemaining`. The counter resets at local midnight.

## 7. Moderation / effects

Currently **no effects parser** runs on the OpenAI-compatible provider. `LocalAiService` parses JSON effect commands from model output and executes them via `App.Commands`; the cloud provider returns sanitized text; `OpenAiCompatibleService` returns the raw assistant content directly. This is a known v1 limitation and is tracked as a follow-up.

## 8. Known limitations

- Effects/commands are not parsed from model output.
- No sampler parameters (`temperature`, `top_p`, etc.) are sent with requests.
- Some tokenizers emit `Ġ` for leading spaces; the response is not cleaned up.
