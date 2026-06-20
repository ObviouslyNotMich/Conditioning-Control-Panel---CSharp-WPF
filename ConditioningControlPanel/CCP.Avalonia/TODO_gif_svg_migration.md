# GIF/SVG migration TODO

- [x] Inspect current Avalonia usages of XamlAnimatedGif/SharpVectors/AvaloniaGif.
- [x] Remove incompatible AvaloniaGif package from CCP.Avalonia.csproj.
- [x] Bump Svg.Skia to 5.1.1 (SkiaSharp 3 compatible).
- [x] Add SkiaSharp-based animated GIF helper.
- [x] Update ChaosFlashOverlay.axaml.cs to use the animated GIF helper for .gif files.
- [x] Update ChaosGifCascadeOverlay.axaml.cs to use the animated GIF helper for .gif files.
- [x] Update AvaloniaChaosArt.TryLoad to support SVG via Svg.Skia.
- [x] Build CCP.Avalonia (Release) after fixing pre-existing TabContentDataTemplates.axaml root issue.
- [x] Build CCP.Avalonia.Desktop.Windows (Release).
- [x] Build CCP.Avalonia.Desktop.Linux (Release).
- [x] Run CCP.Core.Tests (Release).
