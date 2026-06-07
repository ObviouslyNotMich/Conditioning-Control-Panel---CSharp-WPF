## Plan: OpenAI-Compatible AI Effects

Extend the existing Beta Lab AI-effects flow to the OpenAI-compatible provider by reusing the same enrichment -> parse -> dispatch pattern already implemented for local AI. The change should stay provider-local: add the missing effect-enrichment prompt injection in the OpenAI-compatible request path, parse structured responses on the way back, and preserve the existing toggle/moderation behavior.

**Steps**
1. Confirm the request/response control points in `e:\Code\Conditioning-Control-Panel\ConditioningControlPanel\Services\AIService\OpenAiCompatibleService.cs` that correspond to Local AI’s working path in `e:\Code\Conditioning-Control-Panel\ConditioningControlPanel\Services\AIService\LocalAiService.cs`. Reuse the local implementation as the source of truth for when enrichment is added, how JSON is parsed, and when commands are executed.
2. Update the OpenAI-compatible chat request builder in `e:\Code\Conditioning-Control-Panel\ConditioningControlPanel\Services\AIService\OpenAiCompatibleService.cs` to read `AllowAiToControlEffects` from `CompanionPromptSettings` and, when enabled, insert the same enrichment message produced by `PromptService.BuildEnrichmentMessage(...)` before the user chat message. This step depends on identifying or wiring any missing dependencies already used by Local AI, such as prompt-building and knowledge/facts retrieval.
3. Update the OpenAI-compatible response handling in `e:\Code\Conditioning-Control-Panel\ConditioningControlPanel\Services\AIService\OpenAiCompatibleService.cs` to mirror Local AI’s structured response flow: attempt JSON/effects parsing via the existing parser, return clean assistant text to the UI, and dispatch parsed effect commands through `AiCommandService` only after the normal safety/moderation path has accepted the reply. This depends on step 2.
4. Keep scope tight by preserving all existing effect gates instead of re-implementing them in the provider layer. The provider should only produce parsed commands; command eligibility should remain enforced centrally by `e:\Code\Conditioning-Control-Panel\ConditioningControlPanel\Services\Commands\AiCommandService.cs` and the existing settings model in `e:\Code\Conditioning-Control-Panel\ConditioningControlPanel\Models\CompanionPromptSettings.cs`.
5. Add or update narrow tests if the project already has provider/service-level coverage for AI response parsing or command dispatch. If no automated tests exist in this slice, add a minimal focused verification path and document the manual checks instead of broad new test scaffolding. This can run in parallel with step 4 once the implementation shape is clear.
6. Validate behavior with a provider-scoped smoke pass: Beta Lab toggle on/off, OpenAI-compatible mode selected, prompt that should trigger a visible effect, and confirmation that text responses still work when the model returns plain text or an empty effects list. This depends on steps 2-4.

**Relevant files**
- `e:\Code\Conditioning-Control-Panel\ConditioningControlPanel\Services\AIService\OpenAiCompatibleService.cs` — primary implementation target; add enrichment injection and structured-response parsing in the OpenAI-compatible chat path.
- `e:\Code\Conditioning-Control-Panel\ConditioningControlPanel\Services\AIService\LocalAiService.cs` — reference implementation for the working enrichment and effects execution flow.
- `e:\Code\Conditioning-Control-Panel\ConditioningControlPanel\Services\AIService\Enrichment\PromptService.cs` — reuse `BuildEnrichmentMessage(...)` rather than creating a second prompt format.
- `e:\Code\Conditioning-Control-Panel\ConditioningControlPanel\Services\AIService\AiResponseParser.cs` — reuse existing structured parsing for `response` plus `effects` output.
- `e:\Code\Conditioning-Control-Panel\ConditioningControlPanel\Services\Commands\AiCommandService.cs` — keep effect authorization and per-effect gating centralized here.
- `e:\Code\Conditioning-Control-Panel\ConditioningControlPanel\Models\CompanionPromptSettings.cs` — source of the Beta Lab master toggle and related per-effect toggles.

**Verification**
1. Build the app with the existing workspace build task for `ConditioningControlPanel.csproj` and resolve any provider-specific compile errors introduced by dependency wiring.
2. In the app, enable the Beta Lab AI-effects master toggle, switch to OpenAI-compatible mode, send a prompt that should trigger a known effect such as bubbles, and confirm the visible effect fires.
3. Disable the master toggle and repeat the same prompt to confirm the OpenAI-compatible provider still returns text but does not dispatch any effects.
4. Toggle individual effect permissions such as bubbles/flash and confirm `AiCommandService` still blocks disallowed effects in OpenAI-compatible mode.
5. Exercise a fallback case where the model returns plain text or malformed JSON and confirm the app degrades to a normal text response without crashing or firing unintended effects.

**Decisions**
- Included: parity for Beta Lab AI effects between Local AI and OpenAI-compatible AI.
- Excluded: changing the cloud `AiService` provider path unless discovery during implementation shows shared abstractions must be adjusted for compile-time consistency.
- Recommended approach: mirror the Local AI provider’s existing behavior instead of inventing a provider-agnostic abstraction first; that is the smallest, lowest-risk change and keeps the implementation easy to verify.

**Further Considerations**
1. If `OpenAiCompatibleService` currently performs moderation or refusal handling in a different order than Local AI, match Local AI’s ordering before command dispatch so effects cannot execute from a reply that would otherwise be rejected.
2. If models differ in how reliably they emit the expected JSON shape, keep parsing tolerant and treat plain text as a supported fallback rather than a hard error.
3. If the enrichment block increases prompt size noticeably for some endpoints, note that as an expected tradeoff of enabling effects rather than trying to optimize it in the same change.
