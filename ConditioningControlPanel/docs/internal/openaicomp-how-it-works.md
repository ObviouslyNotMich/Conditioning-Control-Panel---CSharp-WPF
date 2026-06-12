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

## 7. Effects parser

When `AllowAiToControlEffects` is enabled, `OpenAiCompatibleService` mirrors the local provider behavior:

1. An enrichment message is inserted after the system prompt. It instructs the model to wrap its reply in a JSON object with `response` and `effects` fields.
2. The raw assistant content is run through `AiResponseParser` to extract `CleanText` and any `AiCommandData` commands.
3. Valid commands are executed through `App.Commands` (the command service enforces the per-effect permission toggles).

If effects are disabled, the raw text is returned unchanged (minus tokenizer cleanup).

## 8. Sampler settings

`OpenAiCompatibleService` reads the nullable sampler fields in `CompanionPromptSettings`. When `OpenAiCompatibleUseCustomSamplerSettings` is true, only the populated fields are included in the request payload:

- `temperature`
- `top_p`
- `top_k`
- `frequency_penalty`
- `presence_penalty`
- `repetition_penalty`
- `min_p`

Empty fields are omitted so strict OpenAI endpoints do not receive unsupported keys like `top_k` or `min_p`.

## 9. Tokenizer cleanup

Some tokenizers (e.g., GPT-2 / GPT-Neo / llama.cpp) emit `Ġ` (U+0120) for leading spaces. `CleanTokenizerArtifacts` replaces these with normal spaces before the response is parsed or displayed.

## 10. Known limitations

- No client-side moderation guard is applied before/after the request (unlike the local and cloud providers).
- The enrichment prompt assumes the model can follow the JSON output format; smaller or non-instruction-tuned models may ignore it.
