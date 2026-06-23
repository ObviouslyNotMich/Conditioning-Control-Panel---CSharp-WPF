# Avalonia Ponytail-Audit Queue

This file tracks findings from the focused ponytail-audit of the Avalonia cross-platform port.
All items from the active goal have been completed and verified against the four gates.

## Completed Items

1. ~~Move the production-head smoke-test harness out of the Windows head source tree while keeping `dotnet run --project CCP.Avalonia.Desktop.Windows -- --smoke-test` working (Debug-only linked sources)~~
2. ~~Refactor `AvaloniaChaosStubs.cs` and `AvaloniaChaosCompat.cs` static facades into DI-injected services/state (`IChaosEnvironment`, `IChaosModeState`, `IChaosMetaService`, `IRevealService`) while preserving the existing static API as pass-throughs~~
3. ~~Delete Mobile platform stub classes under `CCP.Avalonia/Platform`~~
4. ~~Remove `WpfInputHook`/`WpfHotkeyProvider` from `CCP.Avalonia.Desktop.Windows` and use the Avalonia input/hotkey providers on Windows~~
5. ~~Remove `CoreApp` static service-locator writes: replaced per-service assignments with a single DI-backed `CoreApp.Services` assignment; legacy WPF setters remain as fallbacks~~
6. ~~Replace `IAppLogger` custom interface + `SerilogAppLogger` wrapper with `Microsoft.Extensions.Logging.ILogger<T>`~~
7. ~~Replace hand-rolled `LibVLCNativeDiscovery` with LibVLCSharp runtime discovery~~
8. ~~Remove `IUiDispatcher` and `IScheduler` one-line wrappers; use `Dispatcher.UIThread`/`DispatcherTimer` or `System.Threading.Timer` directly~~
9. ~~Delete `AvaloniaFrameSource` `PlatformNotSupportedException` stub and register `IFrameSource` only where implemented~~
10. ~~Replace `ConsoleLogSink` with Avalonia's built-in logging sink~~
11. ~~Replace `AvaloniaBitmapHelper` hand-rolled URI mapping with `AssetLoader` URIs~~
12. ~~Replace `AuthSecurityHelper.SecureCompare` with `CryptographicOperations.FixedTimeEquals`~~
13. ~~Inline `AvaloniaUnifiedUserService` single-property stub~~
14. ~~Simplify `HtUrlHelper` from regexes to `Uri` + string checks~~
15. ~~Fix `CCP.Avalonia.Desktop.csproj` `OutputType` so a library project is not marked `WinExe`~~
16. ~~Fold duplicated Linux/macOS `Program.cs` single-instance setup into `ProgramShared`~~

## Skipped Items

None.
