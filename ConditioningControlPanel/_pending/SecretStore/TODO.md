# ISecretStore Desktop Porting TODO

- [x] Create `CCP.Avalonia.Desktop/Platform/DesktopSecretStore.cs`
  - Windows: DPAPI via `System.Security.Cryptography.ProtectedData`
  - Linux: encrypted file fallback (AES-GCM with user/machine derived key)
  - macOS: Keychain via `security` CLI
- [x] Add `System.Security.Cryptography.ProtectedData` NuGet package to `CCP.Avalonia.Desktop`
- [x] Add `AddDesktopSecretStore()` extension in `CCP.Avalonia.Desktop/DesktopServiceCollectionExtensions.cs`
- [x] Register `ISecretStore` replacement in `CCP.Avalonia.Desktop.Windows/Program.cs`
- [x] Register `ISecretStore` replacement in `CCP.Avalonia.Desktop.Linux/Program.cs`
- [x] Register `ISecretStore` replacement in `CCP.Avalonia.Desktop/Program.cs` (generic desktop head)
- [x] Build `CCP.Avalonia` shared UI project
- [x] Build `CCP.Avalonia.Desktop.Windows`
- [x] Build `CCP.Avalonia.Desktop.Linux`
