# CCP Portability Rubric & OpenSpec Convention

Generated 2026-06-15. Purpose: classify every capability in the Conditioning Control Panel
(WPF + WinForms, .NET 8, Windows-only) as **Portable**, **OS-Specific**, or **Mixed**, expressed
as OpenSpec-style capability specs.

## Portability classes

- **PORTABLE** — Pure C# / .NET domain logic. Compiles & runs unchanged on a plain `net8.0`
  (Linux/macOS) build. No WPF/WinForms/Win32/WinRT/native-Windows dependency. Examples: models,
  JSON serialization, gamification math, HTTP/network clients, AI orchestration over HTTP,
  string/text processing.

- **OS-SPECIFIC** — Hard-bound to Windows. Cannot run off-Windows without a full rewrite.
  Blocking dependencies include: WPF (`System.Windows.*`), WinForms, Win32 P/Invoke
  (`user32`/`gdi32`/`dwmapi`), DPAPI (`ProtectedData`), WinRT, DXGI/SharpDX, Direct3D,
  registry, global keyboard/mouse hooks, system tray, wallpaper, screen enumeration,
  `LibVLCSharp.WPF`, WebView2.

- **MIXED** — Portable domain logic tangled with OS-specific I/O or rendering. Becomes portable
  after extracting an interface / platform shim. Note the specific seam that needs extracting.

## OpenSpec capability spec template

```
## Capability: <name>
**Files:** <comma-separated key files>
**Class:** PORTABLE | OS-SPECIFIC | MIXED
**Blocking deps:** <none | the specific Windows API/framework that blocks portability>
**Seam (if MIXED):** <the interface/abstraction that would unlock portability>

### Requirement: <SHALL statement>
The system SHALL <behavior>.

#### Scenario: <name>
- WHEN <trigger>
- THEN <expected outcome>
```

Keep specs at capability granularity (one per service/subsystem), not per-method. Aim for the
requirements that define WHAT the capability does, so they survive a UI re-platform.
