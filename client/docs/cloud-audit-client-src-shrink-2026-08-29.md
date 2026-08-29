# Cloud audit — client/src shrink pass (2026-08-29)

Scheduled cloud YAGNI/efficiency audit pass over `client/src`, run from a Linux sandbox per
`client/docs/cloud-audit-brief.md` (recovered from history — see below). Audited commit `1d033c85f`
is identical to PR #28's base — zero commits have landed on `feat/crossplatform` since the last pass.

## Why a `shrink:` pass

PR #28's own delta-scan against the same commit found only two small `yagni:` visibility items.
28 prior passes (#1–#28) have exhaustively covered dead code, unused private methods, CA1823/CA1515,
Win32 interop duplication, single-implementation interfaces, and `JsonElement` reader duplication.
None of their titles or scopes targeted `shrink:` (same-logic, fewer-lines) specifically, so this pass
took that angle instead of re-treading ground #1–#28 already covered on an unchanged tree.

## Method

Read every `.cs` file under `client/src` for manual loops, verbose null-check chains, old-style
switches, and constructor patterns that collapse to an equivalent, behavior-preserving shorter form.
Rejected a candidate whenever the codebase's own established local style already differs deliberately
(e.g. sibling methods in `IntakeProfiler.cs` use manual loops throughout; converting only one there
would create inconsistency for no real gain) or where the change would need to touch multiple call
sites for a marginal saving. Every finding below was checked against the declared/inferred types at
each site (not guessed) to confirm the rewrite preserves order, null-handling, and exception semantics
exactly.

## Cut-list (ranked biggest cut first)

### 1. `shrink:` manual-loop-to-collection-init, `ArcademyLocalAssets.cs`

`client/src/CcpClient.Desktop/Features/Arcademy/ArcademyLocalAssets.cs:69-78`

```csharp
// current
private static JsonArray ToArray(List<string> urls)
{
    var array = new JsonArray();
    foreach (var url in urls)
    {
        array.Add(JsonValue.Create(url));
    }

    return array;
}
```

```csharp
// proposed
private static JsonArray ToArray(List<string> urls) =>
    new(urls.Select(url => (JsonNode?)JsonValue.Create(url)).ToArray());
```

Uses the `JsonArray(params JsonNode?[])` constructor; the `JsonValue.Create(url)` call is verbatim
from the original. **net: -8 lines.**

### 2. `shrink:` nested-loop-to-SelectMany, `CompositeHapticSink.cs`

`client/src/CcpClient.Desktop/Haptics/CompositeHapticSink.cs:198-205`

```csharp
// current
var keys = new List<string>();
foreach (var (route, observation) in observations)
{
    foreach (var key in observation.DeviceKeys)
    {
        keys.Add(RouteKey(route) + ":" + key);
    }
}
```

```csharp
// proposed
var keys = observations
    .SelectMany(pair => pair.observation.DeviceKeys.Select(key => RouteKey(pair.route) + ":" + key))
    .ToList();
```

`observations` is `(string route, HapticServerObservation observation)[]` from the `Task.WhenAll`
above (confirmed at :184-196); `SelectMany` preserves the same iteration order as the nested loop.
**net: -5 lines.**

### 3. `shrink:` manual-loop-to-AddRange(Select), `HapticEnvelope.cs`

`client/src/CcpClient.Desktop/Haptics/HapticEnvelope.cs:323-326` (inside `Append`)

```csharp
// current
foreach (var step in steps)
{
    into.Add(new HapticStep(step.DelayMs + offsetMs, step.Pulse));
}
```

```csharp
// proposed
into.AddRange(steps.Select(step => new HapticStep(step.DelayMs + offsetMs, step.Pulse)));
```

`into` is `List<HapticStep>` (method signature at :319); the two `ArgumentNullException.ThrowIfNull`
guards above stay untouched. **net: -3 lines.**

### 4. `shrink:` manual-loop-to-AddRange, `AssetManifest.cs` (copied failures)

`client/src/CcpClient.Desktop/Manifest/AssetManifest.cs:428-431`

```csharp
// current
foreach (var failure in copiedFailures)
{
    failures.Add(failure);
}
```

```csharp
// proposed
failures.AddRange(copiedFailures);
```

`failures` is `List<AssetVerificationFailure>`; `copiedFailures` (from `AssetVerifier.VerifyCopied`)
exposes `.Count` at :433 so it is already a concrete collection, which `List<T>.AddRange` accepts
directly. **net: -3 lines.**

### 5. `shrink:` manual-loop-to-AddRange(Select), `AssetManifest.cs` (unmanifested embedded)

`client/src/CcpClient.Desktop/Manifest/AssetManifest.cs:278-281`

```csharp
// current
foreach (var actual in unmatchedActual)
{
    failures.Add(new AssetVerificationFailure(actual, actual, "unmanifested-embedded-asset"));
}
```

```csharp
// proposed
failures.AddRange(unmatchedActual.Select(actual =>
    new AssetVerificationFailure(actual, actual, "unmanifested-embedded-asset")));
```

`unmatchedActual` is stable by this point (the case-mismatch loop above it removes matched entries via
`RemoveAt` before this block runs), so order is preserved. **net: -2 to -3 lines.**

**Total: roughly -21 to -22 lines. Not applied**: unverified against a real compile (no .NET SDK in
this sandbox — confirmed `dotnet` is not installed at all). A Windows-capable reviewer applying any row
still owes `check-warnings.mjs --cold` then `check-floor.mjs`.

## Checked, rejected as not a strict shrink

Old-style switch candidates in `RampCurves.cs`, `AiCommandEnvelope.cs`, `BubbleCountEffect.cs`; verbose
`?.`/`??` chains; constructor-to-primary-constructor candidates in `ChaosTunnelService.cs` and others —
each either carries multi-statement/side-effecting bodies that don't collapse cleanly, has inline
documentation whose placement would be disrupted, or would need multi-site changes for a marginal
saving. None met the "confident, strict same-behavior" bar this pass used.

## Checked and rejected (already covered by open PRs)

Dead code, CA1823/CA1515, Win32 interop/chrome duplication, single-implementation interfaces,
`JsonElement` reader duplication, stdlib candidates — all previously flagged across PRs #1–#28, not
re-added here.

## Correctness findings

None new. Out of scope for this pass regardless.

## Brief status (unresolved since 2026-08-22)

`client/docs/cloud-audit-brief.md` is still absent from `feat/crossplatform`'s tree — deleted in
`1bdf998e4` alongside unrelated doc cleanup, flagged by every audit pass since (#6, #13, #18, #19,
#21, #26, #27, #28). This run again recovered its text via
`git show 7e9d3b377:client/docs/cloud-audit-brief.md` and followed it, including its hard limit: this
sandbox has no .NET SDK, confirming the brief's premise that this repo's gates cannot run from Linux.

## Backlog status

28 prior `audit:` PRs (#1–#28) are open against `feat/crossplatform`, none merged or closed. This is
#29. Not this pass's call to consolidate or close the backlog, but a 29th unreviewed PR landing on an
already-unreviewed 28-PR pile, all opened since 2026-08-22 and all on largely the same one or two
commits toward the end of that range, is itself the single largest signal this routine produces.

## Verification

Nothing above has been applied, built, or tested. No test, pin, or gate was touched or weakened.
