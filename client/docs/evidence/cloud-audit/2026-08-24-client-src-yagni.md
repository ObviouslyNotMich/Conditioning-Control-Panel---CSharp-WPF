# Cloud audit — client/src YAGNI/efficiency pass, 2026-08-24

Static-only pass (no `dotnet` SDK in this sandbox — confirmed via `which dotnet` /
`dotnet --list-sdks`, both fail; this repo's gates cannot run from Linux per
`docs/constitution.md` and the (currently missing, see below) cloud-audit brief). No files
under `client/src` were changed. This PR's only content is this report.

## Cut-list

`yagni:` Five `Win32*Presence`/`Win32*Surface` classes each carry a verbatim-duplicated
`ZOrderPosition` record struct + `ReadZOrder` static method (walk `GetTopWindow` →
`GetWindow(..., GwHwndnext)`, skip non-visible, find the caller's index and the first
non-topmost window via `GetWindowLongPtrW`/`WsExTopmost`), and four of the five also carry a
duplicated `Describe(nint window)` (class-name lookup via `GetClassNameW`). Only the
`Win32*Interop` P/Invoke class name differs between copies — constants (`GwHwndnext`,
`GwlExstyle`, `WsExTopmost`) and call shape are identical. Replacement: one shared
internal helper taking the small P/Invoke surface each file already re-declares (or a
common `IWin32ZOrderInterop`-style seam), called from all five sites instead of
reimplemented in each.

Verified sites (grep-confirmed, not just declaration-counted):
- `client/src/CcpClient.Desktop/Overlay/Win32OverlayPresence.cs:915` (`ReadZOrder`), `:949`
  (`ZOrderPosition`), `:1006` (`Describe`)
- `client/src/CcpClient.Desktop/Pointer/Win32PointerSurface.cs:562` (`ReadZOrder`), `:596`
  (`ZOrderPosition`), `:882` (`Describe`)
- `client/src/CcpClient.Desktop/Glyph/Win32GlyphSurface.cs:865` (`ReadZOrder`), `:899`
  (`ZOrderPosition`), `:954` (`Describe`)
- `client/src/CcpClient.Desktop/Input/Win32InputPresence.cs:1029` (`ZOrderPosition`), `:1038`
  (`ReadZOrder`), `:1120` (`Describe`)
- `client/src/CcpClient.Desktop/Video/Win32VideoPresence.cs:627` (`WalkZOrder`, a 2-field
  variant without `VisibleCount`/`Describe` — same walk, smaller record)

net: **-150 lines possible** (not applied here — a real edit touches Windows-only P/Invoke
code path shared by overlay, pointer, glyph, input and video presence, and this sandbox has
no `dotnet` SDK at all, so the change cannot be built or test-verified from Linux; per the
constitution, unverifiable work does not land).

No other findings survived verification. Specifically checked and ruled out:
- **Unused private methods** (`IDE0051`-class, the axis the standing brief calls out as
  genuinely unmeasured): all 865 private method/local-function declarations in `client/src`
  have at least one real call site elsewhere in the repo. Checked whole-repo, not
  same-file-only, to rule out XAML binding / event wiring / reflection. Clean.
- Interfaces with one production impl: already known to have a test double or second impl
  in 58/66 cases; not re-litigated.
- `[ComImport]` interop declarations: zero-impl by construction, not a finding.
- Spot-checked a few other candidates in passing (`OverlaySurfaceRequest.Validate`/
  `ValidateOpacity`, its `Alpha` getter) — already idiomatic (`Math.Clamp`,
  `ArgumentOutOfRangeException.ThrowIfLessThanOrEqual`), not findings.

Correctness/security/performance: out of scope for this pass, none observed in passing.

## Process note for the routine owner

`client/docs/cloud-audit-brief.md` — the file this scheduled routine is instructed to read —
does not exist on `feat/crossplatform`. It was added in `7e9d3b377` (2026-08-22 20:06 JST)
and removed as part of the broad `1bdf998e4` "Refactor tests and documentation for clarity
and accuracy" commit (2026-08-22 22:48 JST) roughly 40 minutes later; that commit's message
only calls out deleting `port-audit-prompt.md`, so the brief's removal looks incidental
rather than intentional. This run proceeded using the brief's content recovered from
`git show 7e9d3b377:client/docs/cloud-audit-brief.md` (its hard limit and delivery format
are also restated directly in the routine's own stored prompt), but future firings will hit
the same missing-file gap unless the brief is restored.
