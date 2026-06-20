# Dialog Porting TODO

## Batch: LoginDialog, DisplayNameDialog, UsernamePickerDialog, AttentionCheckSettingsDialog, AwarenessPresetDetailDialog

- [x] Read source WPF files and existing Avalonia dialog patterns
- [x] Extend `CCP.Core/App.cs` with missing nullable service hooks needed by ported dialogs
- [x] Port `AttentionCheckSettingsDialog` (embed `AttentionCheckFeatureControl`, Test now)
- [x] Port `DisplayNameDialog` (create/change/delete modes, validation)
- [x] Port `UsernamePickerDialog` (availability check)
- [x] Temporarily exclude broken `ChaosGifCascadeOverlay` from CCP.Avalonia build (AvaloniaGif 1.0 is incompatible with Avalonia 12)
- [ ] Port `LoginDialog` (providers, account, device-code)
- [ ] Port `AwarenessPresetDetailDialog` (read/edit preset, triggers, actions)
- [ ] Run `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`
