# client/src YAGNI/efficiency audit — 2026-08-24

Scheduled cloud audit pass, run from a Linux sandbox per the (now-deleted, see note below)
`client/docs/cloud-audit-brief.md`. No gates were run — this repo's floor and headed-verification
gates cannot execute from Linux, and no code below was changed on that basis. Correctness, security,
and performance are out of scope for this pass by design.

Seven parallel reads covered all of `client/src` (~74,500 lines, 319 files): `Effects/`,
`Features/{Arcademy,AvatarTube,Chaos,Companion}`, `Features/{Dtrh,Goon,Intake,Mantra,Progression}` +
loose `Features/` files, `Session/`, `Views/`, `Ai/` + `Haptics/`, and the remaining smaller
directories (`Video`, `Audio`, `Camera`, `Overlay`, `Tray`, `Lifecycle`, `Glyph`, `Entitlement`,
`Scheduling`, `Pointer`, `Input`, `Persistence`, `Navigation`, `Storage`, `Companion`, `Capabilities`,
`Manifest`, `Motion`, and loose root files). Each pass cross-checked every private method for a real
call site repo-wide and every interface for a second implementation or test double before flagging
anything, per the already-measured baseline (507 public types, 0 dead; CA1823 = 0; 58/66 interfaces
are legitimate test seams).

`Views/` and the remaining-smaller-directories sweep (119 files, ~26,400 lines) returned **no
findings** — both are unusually thoroughly documented, and every private method, interface, and
config knob checked had a live call site or a legitimate test double.

## Cut-list, biggest cut first

| Tag | What to cut | Replacement | Location | Est. lines |
|---|---|---|---|---|
| yagni | `IBubblePopSurface` interface — one implementation, no test double (sibling surface interfaces `ISpiralSurface`/`IPinkFilterSurface` both have `Null*`/`Recording*` fakes) | Use `BubblePopSurfacePresenter` directly, or add the missing fake to match the sibling pattern | `client/src/CcpClient.Desktop/Effects/BubblePopSurfacePresenter.cs:14-55` | ~42 |
| shrink | Hand-rolled whitespace-collapse loop | `Regex.Replace(stripped, @"\s+", " ")` — the same method already uses `Regex` for the two prior sanitize steps | `client/src/CcpClient.Desktop/Ai/AiPrivacyFilters.cs:168-185` | ~16 |
| yagni | `ILockCardPhrasePool` interface — one implementation, no test double (sibling `ISubliminalPhrasePool` has a `StubPhrases` double) | Use `LockCardPhrasePool` directly, or add the missing fake | `client/src/CcpClient.Desktop/Effects/LockCardPhrasePool.cs:7-19` | ~14 |
| stdlib | Hand-rolled little-endian int/short read/write | `System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian` / `ReadInt32LittleEndian` / `ReadInt16LittleEndian` | `client/src/CcpClient.Desktop/Features/AvatarTube/BmpCodec.cs:117-129` | ~10 |
| shrink | `SubliminalPresetDocument.BuildDefaultPool()` reimplements a 10-line loop for what `LockCardPresetDocument.cs:61` already does as one `ToDictionary` call | `DefaultPhrases.ToDictionary(p => p, _ => true, StringComparer.Ordinal)` at both call sites | `client/src/CcpClient.Desktop/Session/SubliminalPresetDocument.cs:168-177` (+ call sites at 97, 155) | ~9 |
| shrink | Manual `foreach` building a `JsonArray` from `List<string>` | `new JsonArray(urls.Select(u => (JsonNode?)JsonValue.Create(u)).ToArray())` | `client/src/CcpClient.Desktop/Features/Arcademy/ArcademyLocalAssets.cs:69-77` | ~7 |
| yagni | `MarshalCopyRow` — private wrapper around one `Marshal.Copy` call, exactly one call site, no added logic | Inline `Marshal.Copy(...)` directly at the call site | `client/src/CcpClient.Desktop/Features/AvatarTube/AvatarBitmapCache.cs:50-53` (def), `:39` (call site) | ~4 |
| stdlib | Manual min/max clamp where the same file already uses `Math.Clamp` two lines below | `Math.Clamp(desiredHeightDip, MinHeightDip, CapHeightDip(...))` | `client/src/CcpClient.Desktop/Features/PopupPlacement.cs:31` | 0 (consistency only) |

Net: ~102 lines identified as cuttable, none applied.

**Confidence note:** the two `yagni`-tagged interface findings (`IBubblePopSurface`,
`ILockCardPhrasePool`) are the least certain items here. Every sibling interface in the same
surface/pool architectural pattern *does* carry a test double, which reads more like a missing fake
than an unwanted abstraction — flagging as "worth a second look," not a confident delete. Treat them
accordingly before acting.

## Out of scope for this pass, not audited

- `client/tests` (~88,000 lines) and `spine-tasks/` — priority 2 and 3 in the brief; this run covered
  only priority 1 (`client/src`).
- Correctness, security, and performance issues — none were pursued; none were incidentally noticed
  worth reporting.

## Note: the brief this pass follows no longer exists on this branch

`client/docs/cloud-audit-brief.md` — the standing brief this scheduled routine is supposed to read
each run — was deleted in commit `1bdf998e4` ("Refactor tests and documentation for clarity and
accuracy", 2026-08-22), swept up in a bulk documentation cleanup that doesn't mention it by name in
the commit message. This run recovered its content from git history (`git show
1bdf998e4^:client/docs/cloud-audit-brief.md`) and followed it from there. A future run starting from
zero context, as this routine is designed to, will not find the file and will have nothing to follow.
If the deletion was intentional, the routine's stored prompt should be updated to match; if not, the
file is worth restoring.
