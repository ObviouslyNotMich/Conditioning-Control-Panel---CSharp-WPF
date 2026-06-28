# UCE — Runnable Goal (loop until done)

A self-contained driver for finishing the Unified Compositor Engine. Feed this file's mission to a
loop or a fresh agent; it assumes **no prior conversation context** and resumes from the plan's
checkboxes each iteration.

- **Plan + checkboxes (the worklist):** [`unified-compositor-engine-plan.md`](unified-compositor-engine-plan.md)
- **Design reference:** `.pi/skills/unified-compositor-engine/SKILL.md`
- **Parity method + Avalonia v12 doc research:** invoke the `wpf-avalonia-parity` skill.

## Mission

Make the Avalonia head render **all** video + overlays through the one `CompositorEngine`, at **1:1
behavioral parity with the WPF head** and at least as fast/light, then **delete** the legacy
`AvaloniaMultiMonitorVideoService` and per-overlay `*Window` classes. Done = plan Phases A→E all
checked with running-app evidence, legacy code deleted, `CCP.Desktop.slnf` builds 0 errors.

## Each iteration — do exactly one unchecked task

1. **Read** `unified-compositor-engine-plan.md`. Pick the **first unchecked `[ ]` task in phase order
   (A→B→C→D→E)**. Never start a later phase while an earlier one has open boxes.
2. **Spec it (WPF first).** For a parity task, read the WPF behavior — *what* and *when* (call sites,
   triggers, threading) — before touching Avalonia. Most "broken port" bugs are a behavior fired at a
   moment the port forgot.
3. **Confirm the v12 API online, not from memory** (via `wpf-avalonia-parity` step 4 /
   `docs.avaloniaui.net`). Record the API + version in the commit/notes.
4. **Implement parity first, perf second — as separate edits.** Keep diffs small and in surrounding
   style (YAGNI; a platform seam earns its place only for a real divergence).
5. **Build:** `dotnet build ConditioningControlPanel/CCP.Desktop.slnf -c Debug` → must be 0 errors.
   (If the app is running and locks DLLs, build `CCP.Avalonia/CCP.Avalonia.csproj` to confirm compile,
   and note the head needs a restart.)
6. **Verify by running**, not by reading: `dotnet run --project
   ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj`, drive
   the feature end-to-end **including the "when" case** (e.g. trigger twice, switch monitor), and check
   `ccp-run.log`.
7. **Record evidence:** tick the box in the plan with one line — what was fixed, what v12 API was
   confirmed, what you observed running. Mark the matching `avalonia-ui-parity-matrix.md` row.
8. **Commit** (`feat:`/`fix:` conventional, branch `feat/crossplatform`). One task per commit.

## Hard guardrails (never violate)

- **Do not delete the legacy multi-monitor / `*Window` code until Phase E**, and only after UCE video
  is *proven by running*. It is the working fallback — deleting early leaves the app with no video.
- **Do not modify the WPF head** (`ConditioningControlPanel/` paths without `CCP.Avalonia`/`CCP.Core`).
- **Parity is the floor.** No behavior change vs WPF on the Windows target. If an idea genuinely must
  change, **stop and flag it** — that's a product decision, not a port.
- **Don't claim parity you didn't exercise.** "Code looks right" is a hypothesis; running it is evidence.
- Preserve the repo's hard constraints: webcam frames never persisted/sent; enhancement-file
  validation; CLI path validation.

## Stop / hand back to human when

- A task needs the running app on screen and you can't observe it (e.g. "does the video visibly play?").
- Root cause is a **product decision** or a behavior that would diverge from WPF.
- A task is blocked on something outside the repo (LibVLC runtime, codecs, hardware).
- Phase A can't be reproduced from logs after the diagnostics are in — report the captured
  `VideoLayer:` lines and ask.

State the blocker plainly and stop; don't churn or fake progress.

## Definition of done (stop the loop)

- [ ] Plan Phases A, B, C, D, E all checked with running-app evidence.
- [ ] `AvaloniaMultiMonitorVideoService` + legacy per-overlay `*Window` classes deleted; 9
      `IMultiMonitorVideoService` references rehomed/removed.
- [ ] `dotnet build ConditioningControlPanel/CCP.Desktop.slnf -c Debug` → 0 errors.
- [ ] Every video/overlay feature works UCE-only; heavy-load memory ≤ the old multi-window path.
- [ ] `avalonia-ui-parity-matrix.md` rows for these features marked verified.

When all boxes hold, the UCE is done — end the loop.
