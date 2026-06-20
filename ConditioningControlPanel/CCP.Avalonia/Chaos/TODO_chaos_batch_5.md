# Chaos Batch 5 Porting TODO

Files to port from WPF `Chaos/` to `CCP.Avalonia/Chaos/`:

1. ChaosWaveTimerOverlay
2. ChaosVibeTrailOverlay
3. ChaosGifCascadeOverlay
4. ChaosPopText
5. ChaosUnlockCardOverlay (+ Avalonia stub for ChaosUnlockCardData/ChaosUnlockCards)

Plan:
- [x] Read source WPF files and existing Avalonia examples
- [x] Create Avalonia stubs for unlock-card data/builders (AvaloniaChaosUnlockCards.cs) — compiles, no errors in this file
- [ ] Port ChaosWaveTimerOverlay (.axaml + .axaml.cs)
- [x] Port ChaosWaveTimerOverlay (.axaml + .axaml.cs)
- [x] Port ChaosVibeTrailOverlay (.axaml + .axaml.cs)
- [x] Port ChaosGifCascadeOverlay (.axaml + .axaml.cs)
- [x] Port ChaosPopText (.axaml + .axaml.cs)
- [x] Port ChaosUnlockCardOverlay (.axaml + .axaml.cs)
- [x] Run Release build after each major file/group
- [x] Final Release build and report:
  - All 5 Chaos port files compile cleanly (0 errors/warnings in Chaos files).
  - Removed the pre-existing `ChaosGifCascadeOverlay` exclusion from `CCP.Avalonia.csproj` because the new port does not use the incompatible AvaloniaGif 1.0.0 package.
  - Full `CCP.Avalonia` Release build **fails** with 7 pre-existing errors in `Dialogs/LoginDialog.axaml` / `.axaml.cs` (TextBox.Password, IClipboard.SetTextAsync) and 66 warnings elsewhere (AvatarTube, Platform). None are in the ported Chaos files.

Blockers/notes:
- Avalonia project only references CCP.Core; WPF Services/Chaos classes (ChaosLifetimeBoons, ChaosUpgrades, ChaosMeta, ChaosArt, ChaosSfx) are unavailable. Stubs needed in AvaloniaChaosCompat / new file.
- GIF animation: AvaloniaGif package is referenced; will attempt to use AvaloniaGif.GifImage, else fall back to still Bitmap.
- Cursor position: use P/Invoke GetCursorPos on Windows; stub on other platforms.
- Outlined text: use existing AvaloniaOutlinedText.
