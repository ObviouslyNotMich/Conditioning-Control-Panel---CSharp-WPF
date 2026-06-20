# AvaloniaDualMonitorVideoService Porting TODO

1. [x] Read original WPF `Services/Video/DualMonitorVideoService.cs` and existing Avalonia video/bitmap patterns.
2. [x] Create `CCP.Avalonia/Services/Video/AvaloniaDualMonitorVideoService.cs` using Avalonia `WriteableBitmap`, `LibVLC` memory callbacks (RV32), and cross-platform fullscreen windows.
3. [x] Register the service in `CCP.Avalonia/ServiceCollectionExtensions.cs`.
4. [x] Build `CCP.Avalonia/CCP.Avalonia.csproj` (Release).
5. [x] Build `CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj` (Release).
6. [x] Build `CCP.Avalonia.Desktop.Linux/CCP.Avalonia.Desktop.Linux.csproj` (Release).
7. [x] Run `dotnet test tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release`.
8. [x] No build/test failures.
