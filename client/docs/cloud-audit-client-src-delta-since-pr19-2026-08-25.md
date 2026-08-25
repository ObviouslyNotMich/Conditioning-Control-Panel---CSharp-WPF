# client/src YAGNI/efficiency audit — delta beyond PR #19 — 2026-08-25

Read-only pass from an Anthropic cloud sandbox (Linux, no `dotnet` on `PATH` — checked before
starting). No gates run, no code changed, nothing pushed to `feat/crossplatform`.

**This run found PR #19 (`audit: client/src YAGNI and efficiency pass`) already open, covering a
full pass over the same scope with 14 ranked findings, still open/unmerged, base commit essentially
current (only one unrelated camera-enumeration commit has landed since).** Redoing that pass would
duplicate it, so this PR is scoped to items PR #19's four readers did not surface, found and
independently re-verified (grep + read) here. PR #19's list is assumed to stand as-is; nothing below
repeats or contradicts it.

`client/docs/cloud-audit-brief.md` is likewise absent from the current tree (deleted in `1bdf998e4`
alongside unrelated doc cleanup); its content is still recoverable via
`git show 7e9d3b377:client/docs/cloud-audit-brief.md` and this pass follows it from that copy, same
as PR #19 did.

## Ranked cut-list (biggest first)

### 1. `shrink:` repeated 4-arm `CapabilityState` switch, ~15 files

`CapabilityState.Unavailable`, `.DependencyMissing`, `.PermissionRequired`, and `.Faulted` reduce to
the same `{prefix}: {x.Reason.Detail}` shape at nearly every presentation-layer call site. Verified
directly in `Views/Pages/PopQuizPanelNotices.cs:198-206` and `Views/Pages/VisualsPanelNotices.cs:76-85`;
the same 4-arm shape appears in `AudioPanelNotices.cs`, `BubbleCountPanelNotices.cs`,
`InputPanelNotices.cs`, `PointerPanelNotices.cs`, `HapticsPanelNotices.cs`, `VideoPanelNotices.cs`,
`StudioPage.axaml.cs`, `SystemPage.axaml.cs`, `DtrhHostWindow.axaml.cs`, `IntakeHostWindow.axaml.cs`,
`ChaosTunnelService.cs`. Only `Degraded`/`Available` genuinely differ per call site.

Replacement — one extension method on `CapabilityState`:

```csharp
public static CapabilityReason? ReasonOrNull(this CapabilityState s) => s switch
{
    CapabilityState.Unavailable u => u.Reason,
    CapabilityState.PermissionRequired p => p.Reason,
    CapabilityState.DependencyMissing m => m.Reason,
    CapabilityState.Faulted f => f.Reason,
    _ => null,
};
```

Each call site becomes `state.ReasonOrNull() is { } r ? $"{prefix}: {r.Detail}" : …` instead of four
branches. `[client/src/CcpClient.Desktop/Capabilities/CapabilityState.cs:54]`

net: roughly **-35 to -45 lines** across call sites, no behavior change.

### 2. `yagni:` `IAiDiagnosticsSink` — single implementation, zero test doubles

`interface IAiDiagnosticsSink` has exactly one implementation, `CollectingAiDiagnosticsSink`. Every
one of its ~13 test call sites (`AiAwarenessTests.cs`, `AiOperationPipelineTests.cs`,
`AiMemoryPipelineTests.cs`, `AiOfflineIntegrationTests.cs`, `AiReplyHygienePipelineTests.cs`,
`AiModerationCoverageTests.cs`, `AiModerationPipelineBoundaryTests.cs`,
`AiMemoryPromptAssemblyTests.cs`, `AiTitleAllowListTests.cs`, `CompanionMemoryRearmTests.cs`, etc.)
constructs the concrete class directly rather than a fake against the interface, and
`CompanionParticipant.cs:68,125` holds it as the concrete type. Unlike the neighboring
`IAiEndpointAdmissionPolicy` (documented as an owner-pending placeholder for a future allow-list
policy — a real seam, not flagged), no second sink or documented future implementation exists.

Replacement: drop the interface; have `AiAwarenessService` and `AiOperationPipeline` take a concrete
`CollectingAiDiagnosticsSink`. `[client/src/CcpClient.Desktop/Ai/AiDiagnostics.cs:11]`
net: interface + two constructor-param type swaps, roughly **-10 lines**.

### 3. `yagni:` `IDuckHandle` — single implementation, zero external references

`IDisposable`-derived interface with one `Generation` getter; its only implementation is a `private
sealed class DuckHandle` nested inside `SoundArbitration` itself, and no caller outside that file
(production or test) references `.Generation` or the interface type.

Replacement: expose `DuckAttempt.Handle` as plain `IDisposable?`, or make `DuckHandle` a public
sealed class — the interface indirection is unused since construction is entirely internal to
`SoundArbitration`. `[client/src/CcpClient.Desktop/Audio/SoundArbitration.cs:57-63, impl at :1405]`
net: roughly **-7 lines**.

### 4. `shrink:` manual counting loop → `Count(predicate)`

```csharp
public int LiveSurfaces
{
    get
    {
        var live = 0;
        for (var i = 0; i < _slots.Count; i++)
            if (_slots[i].Live) live++;
        return live;
    }
}
```

→ `public int LiveSurfaces => _slots.Count(s => s.Live);` (small list of overlay slots — no perf
concern). `[client/src/CcpClient.Desktop/Effects/OverlaySurfaceSet.cs:166-180]` net: **-6 lines**.

**Total: roughly -58 to -68 lines**, on top of PR #19's -610 to -670.

## Checked and rejected (false positive worth naming so it isn't re-flagged)

`ProgressionLedger.ownsStore` looked always-`true` in `client/src` (only the `Open()` factory calls
it there), but `client/tests/MantraSessionTests.cs:74` and `client/tests/ProgressionLedgerTests.cs:60`
construct it with the `false` default — the branch is test-pinned, not speculative. Not included.

Also checked and clean: no unused private methods (`IDE0051`-class dead code) found anywhere in
`client/src/CcpClient.Desktop` — every private method/local function/constructor has a real in-file
call site. No hand-rolled reimplementations of BCL/Avalonia features found (debounce, JSON handling,
hex-color parsing, and null-guards all checked and are either idiomatic or documented deliberate
deviations).

## Out of scope

Correctness, security, and performance were not the target of this pass; none surfaced during the
read.

## Verification

Nothing above has been applied, built, or tested — this sandbox has no `dotnet` at all and cannot
build the shipping WPF tree. A reviewer applying any row still owes `check-warnings.mjs` and
`check-floor.mjs` before it counts as done.
