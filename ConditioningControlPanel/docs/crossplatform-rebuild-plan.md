# Cross-Platform Rebuild Plan — Conditioning Control Panel

**Goal:** Rebuild Conditioning Control Panel (CCP) for deployment on Windows, Linux, macOS, iOS, and Android using **Avalonia UI v12** and **LibVLCSharp**.

**Current state:** .NET 8 WPF/WinForms desktop app, Windows-only (`net8.0-windows10.0.19041.0`), single-file publish for `win-x64`.

**Target state:** Multi-head Avalonia v12 solution with a shared .NET 8/10 Core, per-platform desktop and mobile heads, and LibVLCSharp for cross-platform media.

---

## 1. Executive Summary

The migration is not a retarget-and-rebuild. It is a **UI rewrite plus platform abstraction**. The largest cost is the WPF UI layer (~121 XAML files, ~483 C# files, ~133k LOC of UI-related code). The engine logic (models, AI orchestration, gamification, sessions, networking) is largely portable once the right seams are introduced.

The recommended strategy is **engine-first extraction**:

1. Extract a cross-platform `CCP.Core` class library.
2. Introduce platform seams (`IUiDispatcher`, `IVideoSurface`, `IAudioPlayer`, `ISecretStore`, `IScreenProvider`, etc.).
3. Keep the WPF app running during migration by implementing seams with Windows shims.
4. Build a new Avalonia UI shell on top of `CCP.Core`.
5. Add mobile heads only after desktop parity is reached.

**Biggest risks:** WPF/Win32 windowing (HWND, layered overlays, z-order), NAudio/WASAPI audio, WebView2 browser, global input hooks, DPAPI-encrypted secrets, GDI/desktop capture, and desktop wallpaper override.

**Easiest wins:** LibVLCSharp core is already cross-platform; only the WPF `VideoView` binding must be replaced.

---

## 2. Current State Analysis

### 2.1 Project Profile

| Property | Current Value | Migration Note |
|---|---|---|
| `OutputType` | `WinExe` | Change to `Exe` for desktop heads; mobile heads use their own templates. |
| `TargetFramework` | `net8.0-windows10.0.19041.0` | Use `net8.0`/`net10.0` for shared Core; platform TFMs only in head projects. |
| `UseWPF` / `UseWindowsForms` | `true` / `true` | Remove from Core and Avalonia heads. |
| `RuntimeIdentifier` | `win-x64` | Use `RuntimeIdentifiers` per head. |
| `PublishSingleFile` / `SelfContained` | `true` / `true` | Supported for Avalonia desktop; not for mobile. Native libs must remain on disk. |
| `NoWarn` | `CA1416` | Remove once platform-specific code is properly gated. |
| Custom `Main` + `STAThread` | `App.xaml.cs` | Replace with Avalonia `BuildAvaloniaApp` / platform lifecycles. |

### 2.2 Code Volume

| Layer | Files | Approx. LOC | Verdict |
|---|---|---|---|
| XAML markup | 121 `.xaml` | ~30,400 | Rewrite as `.axaml`. |
| XAML code-behind | 115 `.xaml.cs` | ~78,400 | Rewrite against Avalonia APIs. |
| Non-XAML WPF partials | ~20 | ~25,000 | Rewrite. |
| Models / JSON DTOs | ~66 | ~17,800 | **Portable** — move to Core. |
| Localization runtime | 2 | ~180 | **Portable** — move to Core. |
| ViewModels | 1 | 201 | Minor fixes (`Visibility` → `bool`). |

### 2.3 P/Invoke Surface Area

There are **~200 `DllImport` declarations** across the codebase, concentrated in:

| DLL | Approx. Imports | Used For |
|---|---|---|
| `user32.dll` | 108 | Window styles, z-order, focus, hooks, cursor, keys, display |
| `gdi32.dll` | 10+ | Regions, bitmap blitting, device contexts |
| `dwmapi.dll` | 4 | Dark title bar, rounded corners, window attributes |
| `shcore.dll` | 1+ | Per-monitor DPI |
| `shell32.dll` | 1 | Shell thumbnails |
| `kernel32.dll` | 7 | Thread IDs, module handles, memory copy |
| `dbghelp.dll` | 1 | Crash minidumps |

Every one of these must move behind a platform interface with Windows, Linux, macOS, and mobile implementations.

### 2.4 Key Windows-Only Dependencies

| Package | Purpose | Migration |
|---|---|---|
| `LibVLCSharp.WPF` | Video surface | `LibVLCSharp.Avalonia` (desktop) or platform-specific mobile surfaces |
| `VideoLAN.LibVLC.Windows` | Native VLC engine | Keep for Windows; add per-platform native packages |
| `Microsoft.Web.WebView2` / `WebView2.Wpf` | Embedded browser | Abstract `IBrowserHost`; use Avalonia webview wrappers or native webviews |
| `NAudio` / `NAudio.Wasapi` | Audio playback / ducking | Abstract `IAudioPlayer`; use LibVLC, ManagedBass, or platform audio APIs |
| `Hardcodet.NotifyIcon.Wpf` / `System.Windows.Forms.NotifyIcon` | System tray | Avalonia built-in `TrayIcon` + `NativeMenu` |
| `MahApps.Metro` / `IconPacks` | UI theme / icons | Avalonia Fluent/Simple theme + `Material.Icons.Avalonia` / custom icons |
| `XamlAnimatedGif` | Animated GIFs | `AvaloniaGif` or custom SkiaSharp/ImageSharp frame animation |
| `SharpVectors` | SVG → WPF | `Svg.Skia` / `Avalonia.Svg.Skia` |
| `OpenCvSharp4.runtime.win` | OpenCV native | Add Linux/macOS runtimes; mobile uses native camera APIs |
| `System.Security.Cryptography.ProtectedData` | DPAPI secrets | Abstract `ISecretStore` (Keychain on macOS/iOS, libsecret on Linux, DPAPI on Windows) |
| `SharpDX.*` | Direct3D/DXGI | Dead dependency — remove |

---

## 3. Target Architecture

### 3.1 Solution Layout

```
ConditioningControlPanel/
├── CCP.Core/                       # net8.0 — engine, models, portable services
├── CCP.Avalonia/                   # net8.0 — shared Avalonia UI, Views, ViewModels
├── CCP.Avalonia.Desktop/           # net8.0 — desktop head (Win/Linux/Mac)
├── CCP.Avalonia.iOS/               # net10.0-ios — iOS head
├── CCP.Avalonia.Android/           # net10.0-android — Android head
├── CCP.WindowsOnly/                # net8.0-windows — optional Windows-specific helpers
└── ConditioningControlPanel.sln
```

> **Note:** Avalonia v12 mobile heads require .NET 10 (`net10.0-ios` / `net10.0-android`). Desktop heads can target `net8.0` or `net10.0`.

### 3.2 Project Responsibilities

| Project | Responsibility |
|---|---|
| `CCP.Core` | Models, settings, session/gamification logic, AI/LLM orchestration, networking, mod/catalogue logic, JSON contracts, localization runtime. No UI framework references. |
| `CCP.Avalonia` | `App.axaml`, `MainWindow.axaml`, Views, UserControls, ViewModels, converters, platform-agnostic styles. References `CCP.Core` and Avalonia packages. |
| `CCP.Avalonia.Desktop` | Desktop `Program.cs`, tray icon wiring, desktop-specific service registration. |
| `CCP.Avalonia.iOS` | `AppDelegate.cs`, `Main.cs`, iOS lifecycle, native service implementations. |
| `CCP.Avalonia.Android` | `MainActivity.cs`, Android lifecycle, native service implementations. |
| `CCP.WindowsOnly` | Win32 P/Invoke helpers, WebView2 host, NAudio implementation, DWM chrome. Optional; only referenced by desktop Windows builds. |

### 3.3 Platform Seams (Interfaces)

Introduce these interfaces in `CCP.Core` and implement per platform:

| Interface | Replaces |
|---|---|
| `IUiDispatcher` | `Application.Current.Dispatcher` |
| `IScheduler` / `IUiTimer` | `DispatcherTimer` |
| `IVideoSurface` | `LibVLCSharp.WPF.VideoView` |
| `IAudioPlayer` / `IAudioDeviceService` / `ISystemAudioDucker` | NAudio/WASAPI |
| `IScreenProvider` | `System.Windows.Forms.Screen` |
| `IWindowChrome` / `IOverlaySurface` | Win32 DWM, layered windows, z-order |
| `IHotkeyProvider` / `IInputHook` | `RegisterHotKey`, `SetWindowsHookEx` |
| `ITrayIcon` | `NotifyIcon` |
| `IBrowserHost` | WebView2 |
| `ISecretStore` | DPAPI |
| `IWallpaperProvider` | `SystemParametersInfo` wallpaper |
| `IUpdateInstaller` | Inno Setup updater |
| `IThumbnailProvider` | `IShellItemImageFactory` |
| `IFrameSource` / `ICaptureService` | GDI desktop capture |
| `IImageDecoder` / `IImageSourceFactory` | `BitmapImage`, `pack://` URIs |

---

## 4. Avalonia UI v12 Migration

### 4.1 Package Map

| Current (WPF) | Avalonia v12 Replacement | Version |
|---|---|---|
| WPF SDK (`UseWPF=true`) | Avalonia SDK packages | — |
| `MahApps.Metro` | `Avalonia.Themes.Fluent` or `Avalonia.Themes.Simple` | 12.0.x |
| `MahApps.Metro.IconPacks` | `Material.Icons.Avalonia`, `FluentIcons.Avalonia`, or custom SVG | — |
| `Hardcodet.NotifyIcon.Wpf` | Built-in `TrayIcon` + `NativeMenu` | 12.0.x |
| `XamlAnimatedGif` | `AvaloniaGif` or SkiaSharp animation | — |
| `SharpVectors` | `Svg.Skia` / `Avalonia.Svg.Skia` | — |
| WPF `DataGrid` | `Avalonia.Controls.DataGrid` | 12.0.x |
| WPF `Behavior` / `Interaction` | `Avalonia.Xaml.Interactions` | — |

Core Avalonia packages to add:

```xml
<PackageReference Include="Avalonia" Version="12.0.4" />
<PackageReference Include="Avalonia.Desktop" Version="12.0.4" />
<PackageReference Include="Avalonia.Themes.Fluent" Version="12.0.4" />
<PackageReference Include="Avalonia.Fonts.Inter" Version="12.0.4" />
<PackageReference Include="Avalonia.Skia" Version="12.0.4" />
<PackageReference Include="Avalonia.HarfBuzz" Version="12.0.4" />
<PackageReference Include="Avalonia.Diagnostics" Version="12.0.4" Condition="'$(Configuration)' == 'Debug'" />
```

Mobile heads:

```xml
<!-- iOS -->
<PackageReference Include="Avalonia.iOS" Version="12.0.4" />

<!-- Android -->
<PackageReference Include="Avalonia.Android" Version="12.0.4" />
```

### 4.2 Major v12 Changes Affecting the Migration

| Feature | Change | Impact |
|---|---|---|
| Compiled bindings | Enabled by default | Every XAML binding needs `x:DataType`; use `{ReflectionBinding ...}` only for dynamic paths. |
| `IBinding` removed | Use `BindingBase` | Custom markup extensions (e.g., `{loc:Str}`) must be rewritten. |
| Clipboard / drag-drop | `IDataObject` removed | File-open handoff and drag-drop must move to async typed APIs. |
| `SystemDecorations` renamed | Now `WindowDecorations` | Custom chrome must be reimplemented. |
| Window state from styles | Cannot set `WindowState` from styles | Set in code or initialization only. |
| Dispatcher model | `Dispatcher.CurrentDispatcher`, `Yield`, `Resume` added | WPF-like dispatcher code is easier, but timers must be created on the intended UI thread. |
| `Avalonia.Diagnostics` | Removed from core | Use `Avalonia.Diagnostics` package and `AttachDeveloperTools()`. |
| .NET Standard dropped | No `netstandard2.0` assets | Class libraries must target `net8.0`/`net10.0`. |

### 4.3 XAML Namespace & File Changes

| WPF | Avalonia |
|---|---|
| `.xaml` | `.axaml` |
| `xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"` | `xmlns="https://github.com/avaloniaui"` |
| `Window`, `UserControl`, `Page` | `Window`, `UserControl` (no `Page`/`Frame` by default) |
| `ResourceDictionary.MergedDictionaries` | `Application.Styles`, `ResourceDictionary` |
| `DynamicResource` / `StaticResource` | Same names, but lookup semantics differ |
| `pack://application:,,,/...` | `avares://CCP.Avalonia/...` |
| `<Resource Include="..." />` | `<AvaloniaResource Include="..." />` |

### 4.4 Control Replacements

| WPF Pattern | Avalonia Equivalent |
|---|---|
| `WindowChrome` custom chrome | `ExtendClientAreaToDecorationsHint`, `WindowDecorations`, `SystemDecorations` |
| `WindowStyle="None" AllowsTransparency="True"` | `WindowDecorations="None"`, `TransparencyLevelHint="Transparent"`, `Background="Transparent"` |
| `Topmost`, `ShowInTaskbar`, `ResizeMode`, `WindowState` | Similar properties; behavior varies on Linux/macOS WMs |
| `Viewbox Stretch="Fill"` | Avalonia `Viewbox` supports `Stretch`; test HiDPI |
| WPF `Style`, `Trigger`, `DataTrigger`, `EventSetter`, `Storyboard` | Avalonia style selectors (`:pointerover`, `:checked`) + `Avalonia.Animation` |
| `FocusVisualStyle`, `Cursor="Hand"` | `:focus` selector, `Cursor` enum |
| `ToolTip HasDropShadow="False"` | No direct equivalent; Avalonia `ToolTip` has no `HasDropShadow` |
| `CommandBinding`, `RoutedCommand`, `InputBinding` | Avalonia commands/bindings; routed-event model differs |
| `System.Windows.Shapes` | `Avalonia.Controls.Shapes` |
| `System.Windows.Media.Effects.DropShadowEffect` | `BoxShadow` |
| `System.Windows.Media.Imaging.BitmapImage` / `WriteableBitmap` | `Avalonia.Media.Imaging.Bitmap` / `WriteableBitmap` |

### 4.5 Dispatcher & Threading

| WPF | Avalonia |
|---|---|
| `Application.Current.Dispatcher.BeginInvoke(...)` | `Avalonia.Threading.Dispatcher.UIThread.Post(...)` |
| `Dispatcher.Invoke(...)` | `Dispatcher.UIThread.Invoke(...)` |
| `DispatcherTimer` | `Avalonia.Threading.DispatcherTimer` |
| `DispatcherPriority` | `Avalonia.Threading.DispatcherPriority` |

### 4.6 Localization Markup Extension

Current (`Localization/LocExtension.cs`):

```csharp
public override object ProvideValue(IServiceProvider serviceProvider)
{
    var binding = new Binding($"[{Key}]") { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay };
    return binding.ProvideValue(serviceProvider);
}
```

Avalonia rewrite: create an Avalonia `MarkupExtension` returning a `Binding`, or replace usages with `{Binding [Key], Source={x:Static loc:LocalizationManager.Instance}}`.

---

## 5. LibVLCSharp Cross-Platform Media Migration

### 5.1 Package Changes

Remove:

```xml
<PackageReference Include="LibVLCSharp.WPF" Version="3.8.5" />
<PackageReference Include="Microsoft.WindowsAppSDK" Version="1.1.1" ExcludeAssets="all" PrivateAssets="all" />
```

Add managed packages:

```xml
<PackageReference Include="LibVLCSharp" Version="3.9.7.1" />
<PackageReference Include="LibVLCSharp.Avalonia" Version="3.9.7.1" />
```

Add native engine packages per head:

| Platform | Package | Notes |
|---|---|---|
| Windows x64/x86/ARM64 | `VideoLAN.LibVLC.Windows` 3.0.23.1 | Drop-in replacement |
| Windows GPL | `VideoLAN.LibVLC.Windows.GPL` | Only if GPL codecs needed |
| macOS x64 | `VideoLAN.LibVLC.Mac` 3.1.3.1 | Old; no ARM64 build |
| macOS ARM64 | **Manual** | Extract `libvlc.dylib` + plugins from modern VLC.app |
| Linux | **No official NuGet** | Install `libvlc`/`libvlccore` via package manager or ship custom `.so` |
| Android | `VideoLAN.LibVLC.Android` 3.6.5 / 3.7.0-beta | Add `LibVLCSharp.Android.AWindowModern` |
| iOS | `VideoLAN.LibVLC.iOS` 3.6.1 | Works with .NET iOS workload |

> **Important:** `LibVLCSharp.Avalonia` officially supports Windows, macOS, and Linux. For Android/iOS, use platform-specific video surfaces.

### 5.2 VideoView Migration

WPF XAML:

```xml
xmlns:vlc="clr-namespace:LibVLCSharp.WPF;assembly=LibVLCSharp.WPF"
<vlc:VideoView x:Name="VideoView" />
```

Avalonia XAML:

```xml
xmlns:vlc="clr-namespace:LibVLCSharp.Avalonia;assembly=LibVLCSharp.Avalonia"
<vlc:VideoView MediaPlayer="{Binding Player}" />
```

Code-behind remains conceptually identical:

```csharp
var player = new MediaPlayer(libVLC);
videoView.MediaPlayer = player;
player.Play(media);
```

### 5.3 Memory-Render Surfaces

`DualMonitorVideoService` and `InlineLoopVideo` use LibVLC memory callbacks (`SetVideoCallbacks` / `SetVideoFormat` with `RV32`) and WPF `WriteableBitmap`.

Avalonia equivalent:

```csharp
using var framebuffer = bitmap.Lock();
unsafe
{
    Buffer.MemoryCopy(_frameBuffer.ToPointer(), framebuffer.Address.ToPointer(), size, size);
}
```

Use `Avalonia.Media.Imaging.WriteableBitmap` and `CompositionTarget.Rendering` for per-frame invalidation.

### 5.4 Native Library Packaging

- Reference per-RID native packages and let NuGet/build place them in output.
- For `PublishSingleFile`, mark native libraries with `<ExcludeFromSingleFile>true</ExcludeFromSingleFile>`.
- Remove or gate the manual `CopyLibVLCAfterPublish` target to Windows only.
- For Linux/macOS, use explicit `Core.Initialize(path)` derived from `AppContext.BaseDirectory` + RID.

### 5.5 Audio Abstraction

NAudio is Windows-only. Strategy:

1. Define `IAudioPlayer`, `IAudioCapture`, `IAudioDeviceEnumerator`, `ISystemAudioDucker`.
2. On Windows, keep NAudio behind the interface as an optional implementation.
3. On Linux/macOS/mobile, use:
   - LibVLC for simple file playback.
   - `ManagedBass` / `Bass.Net` for lower-level mixing.
   - `OpenAL` / `Silk.NET.OpenAL` for playback/capture.
   - Platform APIs for ducking (PulseAudio/PipeWire on Linux, CoreAudio on macOS/iOS, AudioManager on Android).

### 5.6 WebView2 Video in Deeper

`Views/Deeper/EnhancementPlayerWindow.xaml` uses WebView2 for video. Replace with the same LibVLC `VideoView` used everywhere else, unifying the video stack.

---

## 6. Dependency Migration Matrix

| Current Package | Current Version | Action | Replacement / Notes |
|---|---|---|---|
| `Buttplug` | 3.0.1 | Keep | Test on Linux/mac; likely desktop-only. |
| `Buttplug.Client.Connectors.WebsocketConnector` | 3.0.1 | Keep | Same as above. |
| `CommunityToolkit.Mvvm` | 8.2.2 | Keep | Fully portable. |
| `DiscordRichPresence` | 1.6.1.70 | Keep | Cross-platform transport. |
| `Hardcodet.NotifyIcon.Wpf` | 1.1.0 | Remove | Avalonia `TrayIcon`. |
| `LibVLCSharp.WPF` | 3.8.5 | Remove | `LibVLCSharp` + `LibVLCSharp.Avalonia`. |
| `MahApps.Metro` | 2.4.10 | Remove | Avalonia Fluent/Simple theme. |
| `MahApps.Metro.IconPacks` | 5.0.0 | Remove | `Material.Icons.Avalonia` or custom icons. |
| `Microsoft.ML.OnnxRuntime` | 1.20.1 | Keep + add runtimes | Add Linux/mac/mobile runtime packages. |
| `Microsoft.Web.WebView2` | 1.0.2535.41 | Remove from shared | Keep only in Windows-only head; abstract `IBrowserHost`. |
| `Microsoft.WindowsAppSDK` | 1.1.1 (excluded) | Remove | Not needed. |
| `NAudio` / `NAudio.Wasapi` | 2.2.1 | Abstract | Replace with `IAudioPlayer`; use LibVLC/ManagedBass/OpenAL. |
| `Newtonsoft.Json` | 13.0.3 | Keep | Portable. |
| `OllamaSharp` | 5.4.16 | Remove | Vestigial import only. |
| `OpenAI-DotNet` | 8.6.2 | Remove | Dead dependency. |
| `OpenCvSharp4` | 4.9.0.20240103 | Keep + add runtimes | Add Linux/mac/mobile native runtimes. |
| `OpenCvSharp4.runtime.win` | 4.9.0.20240103 | Move to Windows head | |
| `QRCoder` | 1.6.0 | Keep | Portable. |
| `Serilog` + sinks | 3.1.1 / 5.0.0 | Keep | Portable. |
| `SharpDX` / `.DXGI` / `.Direct3D11` | 4.2.0 | Remove | Zero references. |
| `SharpVectors` | 1.8.4.2 | Remove | `Svg.Skia` / `Avalonia.Svg.Skia`. |
| `System.Security.Cryptography.ProtectedData` | 8.0.0 | Abstract | `ISecretStore` with DPAPI/Keychain/libsecret. |
| `VideoLAN.LibVLC.Windows` | 3.0.21 | Keep + add runtimes | Add Mac/Android/iOS packages; Linux via system or custom. |
| `XamlAnimatedGif` | 2.3.0 | Remove | `AvaloniaGif` or custom frame animation. |

---

## 7. Subsystem Migration Plan

### 7.1 Application Bootstrap

Current: custom `[STAThread] Main`, `Mutex`, `EventWaitHandle`, WPF `Dispatcher`, `System.Windows.MessageBox`.

Target:
- Avalonia `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)` on desktop.
- Platform lifecycles for iOS/Android.
- Cross-platform single-instance via file lock or platform-specific service.
- Replace `Environment.SpecialFolder.LocalApplicationData` assumptions with proper paths (`XDG_DATA_HOME` on Linux, `~/Library/Application Support` on macOS).
- Replace `MessageBox.Show` with `IDialogService` (`MsBox.Avalonia` or custom dialogs).

### 7.2 System Tray

Current (`Services/Notifications/TrayIconService.cs`): `System.Windows.Forms.NotifyIcon`, `ContextMenuStrip`, `ToolStripMenuItem`, `SetForegroundWindow`.

Target: Avalonia built-in `TrayIcon` + `NativeMenu` in desktop head. Not applicable on mobile.

```xml
<TrayIcon Icon="/Assets/app-icon.ico" ToolTipText="CCP">
  <TrayIcon.Menu>
    <NativeMenu>
      <NativeMenuItem Header="Show Dashboard" Command="{Binding ShowWindowCommand}" />
      <NativeMenuItemSeparator />
      <NativeMenuItem Header="Exit" Command="{Binding QuitCommand}" />
    </NativeMenu>
  </TrayIcon.Menu>
</TrayIcon>
```

### 7.3 Global Input Hooks

Current: `SetWindowsHookEx` low-level keyboard/mouse hooks, `RegisterHotKey`, `GetAsyncKeyState`.

Target: abstract `IHotkeyProvider` / `IInputHook`. Windows keeps Win32 implementation. Linux uses X11/wayland evdev. macOS uses `CGEventTap` + accessibility permission. Mobile: impossible; disable.

Lockdown mode system-key suppression (`Alt+Tab`, `Win`, `Esc`, `Ctrl+Shift+Esc`) is **impossible on macOS/iOS/Android** and requires root/udev on Linux.

### 7.4 Window Chrome / Overlays

Current: `WindowChrome`, `dwmapi.dll` for dark title bars, `SetWindowLong`/`SetWindowPos` for tool windows and z-order, `AllowsTransparency` for layered overlays.

Target:
- Avalonia `WindowDecorations="None"`, `TransparencyLevelHint="Transparent"`, `Topmost="True"`, `SystemDecorations`.
- Click-through/input passthrough requires platform-specific code on Linux/macOS.
- DWM tinting is Windows-only; use Avalonia client-side decorations for cross-platform chrome.

### 7.5 Screen / Monitor APIs

Current: `System.Windows.Forms.Screen.AllScreens`, `SystemParameters.WorkArea`, `GetDpiForMonitor`, `SetDisplayConfig`.

Target: Avalonia `Window.Screens` / `TopLevel.GetTopLevel(this).Screens`. Abstract `IScreenProvider` for headless Core.

Display mirroring (`SetDisplayConfig`) has no cross-platform API; gate as Windows-only.

### 7.6 Desktop Wallpaper

Current: `SystemParametersInfo` (`SPI_SETDESKWALLPAPER`).

Target: abstract `IWallpaperProvider`. Windows only. macOS can use AppleScript/NSWorkspace; Linux uses `gsettings`/`feh` per DE.

### 7.7 Embedded Browser

Current: `Microsoft.Web.WebView2` in `Services/Browser/BrowserService.cs`.

Target options:
1. **Avalonia.Controls.WebView** — official cross-platform WebView (WebView2 on Windows, WKWebView on macOS/iOS, WPE WebKit on Linux, Android WebView).
2. **CEF wrapper** (`CefGlue.Avalonia`, `CefNet.Avalonia`) for desktop Linux/macOS.
3. **System browser** launch via `xdg-open`/`open` where an embedded browser is unnecessary.
4. Keep WebView2 only in `CCP.WindowsOnly` for Windows parity.

Introduce `IBrowserHost` abstraction.

### 7.8 Imaging / Computer Vision

Current: OpenCvSharp4 + `runtime.win`, DirectShow/WinRT enumerators, ONNX Runtime CPU x64.

Target:
- Add platform runtimes: `OpenCvSharp4.runtime.ubuntu.*`, `OpenCvSharp4.runtime.osx.*`.
- On mobile, OpenCvSharp does not ship runtimes; use platform APIs (Android Camera2 / iOS AVFoundation).
- Replace DirectShow/WinRT enumerators with V4L2 on Linux, AVFoundation on macOS/iOS.
- Add ONNX Runtime mobile runtimes.
- Replace `System.Drawing` with SkiaSharp / ImageSharp.

### 7.9 Secure Storage

Current: `ProtectedData.Protect/Unprotect` with `DataProtectionScope.CurrentUser`.

Target: `ISecretStore` abstraction.
- Windows: DPAPI (keep existing).
- macOS/iOS: Keychain (`Security` framework).
- Linux: libsecret/secret-tool or encrypted file with user-only permissions.

Existing encrypted tokens will not decrypt on other OSs; plan re-authentication or migration path.

### 7.10 File Dialogs

Current: `Microsoft.Win32.OpenFileDialog` / `SaveFileDialog`, `System.Windows.Forms.FolderBrowserDialog`.

Target: Avalonia `IStorageProvider` (`OpenFilePickerAsync`, `SaveFilePickerAsync`, `OpenFolderPickerAsync`).

### 7.11 Updates

Current: Inno Setup installer, Registry path discovery, `wmic` to kill WebView2 processes.

Target: `IUpdateInstaller` abstraction.
- Windows: keep installer path discovery (without `wmic`).
- macOS: Sparkle or manual DMG.
- Linux: AppImage/snap/flatpak.
- iOS/Android: app stores.

---

## 8. Phase-by-Phase Migration Roadmap

### Phase 0 — Cleanup (Days)

1. Remove dead packages: `SharpDX.*`, `OpenAI-DotNet`, `OllamaSharp`.
2. Verify `MahApps.Metro` / `IconPacks` usage; remove if unused.
3. Remove `Microsoft.WindowsAppSDK` exclusion hack.
4. Delete `CopyLibVLCAfterPublish` and `IncludeWebView2LoaderInPublish` from shared project; move to Windows head later.
5. Add platform analyzers; remove `NoWarn$(NoWarn);CA1416`.
6. Document feature matrix: mark features as portable vs. Windows-only.

### Phase 1 — Carve Out `CCP.Core` (1–2 Weeks)

1. Create `CCP.Core` class library targeting `net8.0`.
2. Move into Core:
   - All POCO models (`Models/`).
   - Localization runtime (`Loc.cs`, `LocalizationManager.cs`, language JSON as content).
   - AI orchestration (`Services/AIService/*`).
   - Gamification, Chaos economy rules, session engine.
   - Networking/auth transport, crypto, moderation.
   - Content/mod system logic.
   - JSON contracts and settings POCOs.
3. Introduce platform seams (see section 3.3).
4. Replace `DispatcherTimer` with `IScheduler`/`IUiTimer` abstraction.
5. Replace `MessageBox.Show` with `IDialogService`.
6. Replace `pack://` URI loading with `IAssetLoader`.
7. Replace DPAPI with `ISecretStore`.
8. Keep WPF app compiling by implementing seams with WPF/Windows shims in a temporary `CCP.WpfShim`.

### Phase 2 — Prove Core Off-Windows (1 Week)

1. Build `CCP.Core` on Linux and macOS in CI.
2. Run headless unit/integration tests for session engine, AI parsing, gamification, mod loading.
3. Ensure DPAPI, screen, audio, and UI dispatch seams are the only Windows leaks.

### Phase 3 — Create Avalonia Solution (1 Week)

1. Create projects:
   - `CCP.Avalonia` (shared UI, `net8.0` or `net10.0`)
   - `CCP.Avalonia.Desktop`
   - `CCP.Avalonia.iOS`
   - `CCP.Avalonia.Android`
2. Add Avalonia v12 + LibVLCSharp packages per head.
3. Implement `App.axaml`, `MainWindow.axaml`, platform `Program.cs` / `MainActivity.cs` / `AppDelegate.cs`.
4. Set up solution build for all heads.

### Phase 4 — Migrate XAML & UI (4–8 Weeks)

1. Convert `Window`/`UserControl` files from WPF to Avalonia namespaces.
2. Replace MahApps controls with Avalonia equivalents.
3. Convert `ResourceDictionary` entries to Avalonia `Styles`/`ResourceDictionary` syntax.
4. Replace `pack://application:,,,/...` with `avares://CCP.Avalonia/...`.
5. Mark assets as `AvaloniaResource` where appropriate.
6. Port `MainWindow.xaml` (13,333 LOC) by splitting into views/view models.
7. Port dialogs, feature controls, avatar window, chaos overlays.
8. Implement `IUiDispatcher`, `IOverlaySurface`, `IWindowChrome` for Avalonia.

### Phase 5 — Replace Media & Audio (2–3 Weeks)

1. Replace `LibVLCSharp.WPF.VideoView` with `LibVLCSharp.Avalonia.VideoView` in desktop UI.
2. Implement `IVideoSurface` for mobile using native `VideoView` wrappers.
3. Replace NAudio with `IAudioPlayer` + cross-platform implementation.
4. Port `DualMonitorVideoService` memory-render path.
5. Replace WebView2 video in `EnhancementPlayerWindow` with LibVLC `VideoView`.
6. Replace `XamlAnimatedGif` with `AvaloniaGif` or SkiaSharp animation.
7. Replace `SharpVectors` with `Svg.Skia`.

### Phase 6 — Replace OS-Shell Features (2–3 Weeks)

1. `ITrayIcon` → Avalonia `TrayIcon` on desktop, no-op on mobile.
2. `IHotkeyProvider` / `IInputHook` → Windows-only Win32 hooks, limited macOS/Linux, no-op mobile.
3. `IWallpaperProvider` → Windows-only.
4. `IBrowserHost` → WebView2 on Windows, Avalonia WebView or system browser elsewhere.
5. `IWindowChrome` / `IOverlaySurface` → Avalonia window styling + platform click-through.
6. `IThumbnailProvider` → SkiaSharp decoding + optional OS thumbnails.
7. `IFrameSource` / `ICaptureService` → Windows GDI, Linux/macOS platform APIs.

### Phase 7 — Build & Publish Pipeline (1–2 Weeks)

1. Define `RuntimeIdentifiers` per head:
   ```xml
   <RuntimeIdentifiers>win-x64;win-arm64;linux-x64;linux-arm64;osx-x64;osx-arm64</RuntimeIdentifiers>
   ```
2. Desktop: keep `PublishSingleFile` + `SelfContained` optional.
3. Mobile: standard `net10.0-ios` / `net10.0-android` builds; no single-file; enable trimming with Avalonia trimming roots.
4. Add CI matrix builds for each RID and mobile simulator tests.
5. Set up code signing and notarization for macOS/iOS.
6. Set up Android keystore and app bundle publishing.

### Phase 8 — Mobile Feature Gating & Adaptation (2–4 Weeks)

1. Disable on iOS/Android:
   - Overlays (full-screen effects, bubbles, subliminals).
   - Global hooks and system-key suppression.
   - System tray.
   - Desktop wallpaper override.
   - GDI/desktop capture.
   - Screen OCR.
   - Multi-monitor video mirroring.
2. Design mobile-first shell:
   - Tabs or single-window navigation.
   - Touch-optimized controls.
   - Native camera + LibVLC for Lab/webcam features.
3. Adapt background audio/execution restrictions.
4. Address app-store content policies early.

---

## 9. Platform-Specific Deployment Notes

### Windows

- Easiest path: keep many Win32 features behind `CCP.WindowsOnly`.
- Can retain WebView2, global hooks, DWM tinting, wallpaper override, NAudio.
- Use `net8.0-windows10.0.19041.0` for the Windows head/shim.
- Single-file publish with native libs excluded works as today.

### Linux

- No official `VideoLAN.LibVLC.Linux` NuGet; rely on system `libvlc` or bundle native `.so` files.
- Tray icon works via `StatusNotifierItem`/AppIndicator but requires `libdbusmenu` on some distros.
- Window transparency and click-through depend on compositor (X11/Wayland); may be limited.
- Global hooks are not reliable; lockdown/security features must degrade gracefully.
- Use `xdg-open` for system browser fallback.

### macOS

- `VideoLAN.LibVLC.Mac` provides native libvlc for x64.
- For Apple Silicon (ARM64), ship a custom `libvlc.dylib` + plugins extracted from VLC.app.
- Title-bar theming and transparent overlays require native NSWindow interop.
- Global hotkeys possible via `NSEvent.AddGlobalMonitorForEventsMatchingMask`; system-key suppression is not.
- Secrets: Keychain.

### iOS

- Avalonia iOS head requires `Avalonia.iOS` and platform lifecycle code.
- No multi-window overlays, no global hooks, no tray.
- Background audio/execution heavily restricted.
- Use `VideoLAN.LibVLC.iOS` and `Microsoft.ML.OnnxRuntime` iOS runtime.
- App Store policies around adult content may block distribution; plan accordingly.

### Android

- Avalonia Android head requires `Avalonia.Android` and `MainActivity`.
- No system tray, no global hooks, no wallpaper override.
- Use `VideoLAN.LibVLC.Android`.
- Camera/ML inference via native bindings or platform APIs.

---

## 10. Risk Register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| `LibVLCSharp.Avalonia` not yet compatible with Avalonia v12 | Medium | High | Build a spike immediately; if needed, compile from source or wait for updated package. |
| 133k LOC UI rewrite is larger than estimated | High | High | Extract Core first; port screen-by-screen; keep WPF app alive during migration. |
| NAudio audio ducking has no cross-platform equivalent | High | Medium | Abstract `IAudioDucker`; disable or implement per-platform. |
| WebView2 browser features break on Linux/macOS | High | High | Abstract `IBrowserHost`; use Avalonia WebView or system browser; feature-gate if needed. |
| Global hooks / lockdown suppression impossible on macOS/Linux/mobile | High | High | Document limitations; gate lockdown as Windows-only. |
| DPAPI-encrypted tokens fail on other OSs | High | Medium | Force re-authentication or migrate to OS keychain abstraction. |
| Native library discovery fails on Linux/macOS | Medium | High | Use explicit `Core.Initialize(path)`; test on clean VMs. |
| macOS ARM64 lacks official LibVLC NuGet | Medium | High | Ship custom ARM64 native build extracted from VLC.app. |
| Single-file + native libs incompatible with mobile | Medium | Medium | Use normal publish for mobile; exclude native libs from single-file on desktop. |
| Mobile app-store content policies | Medium | High | Review policies early; prepare feature gating and age-gating. |
| Layered/click-through windows fail on Linux/Wayland | Medium | Medium | Gate overlay transparency as desktop-only; provide degraded mode. |
| Trimming breaks Avalonia XAML/reflection | Medium | Medium | Add trimming roots/AOT configuration; test per platform. |

---

## 11. Quick Win Checklist

| Task | Effort | Value |
|---|---|---|
| Remove SharpDX / OpenAI-DotNet / OllamaSharp packages | Hours | Cleans native footprint. |
| Create `CCP.Core` and move portable models + AI + gamification | Days–1 week | Biggest leverage; enables headless tests. |
| Introduce `IUiDispatcher` + scheduler abstraction | Days | Unlocks nearly every mixed service. |
| Abstract `ISecretStore` away from DPAPI | 1–2 days | Unlocks Linux/macOS auth. |
| Replace `LibVLCSharp.WPF` with `LibVLCSharp.Avalonia` in a spike | 1–2 days | Proves cross-platform video. |
| Rewrite `App.xaml`/`App.xaml.cs` for Avalonia | Days | Foundation of new UI. |
| Port `MainWindow.xaml` to Avalonia (split into views) | Weeks | Largest piece of work. |

---

## 12. Recommended First Steps

1. **Read existing portability specs:** `openspec/PORTABILITY_REPORT.md` and `openspec/PORTABILITY_RUBRIC.md` already identify exact seams.
2. **Remove dead dependencies:** `SharpDX.*`, `OpenAI-DotNet`, `OllamaSharp`.
3. **Create `CCP.Core`:** move engine/models and introduce platform seams.
4. **Compile Core on Linux/macOS:** cheapest proof of portability.
5. **Build an Avalonia spike:** single window playing a LibVLC video in a transparent topmost window to validate overlay assumptions.
6. **Decide browser strategy early:** WebView2 on Windows only vs. true cross-platform control affects Deeper/auto-discovery heavily.

---

## 13. Conclusion

Avalonia UI v12 + LibVLCSharp is a capable cross-platform target for Conditioning Control Panel. The dominant cost is the WPF UI rewrite, not the engine. The safest path is:

1. **Engine-first extraction** into `CCP.Core` behind clean platform seams.
2. **Avalonia desktop parity** on Windows first, then Linux/macOS.
3. **Mobile heads** with a reduced, feature-gated companion experience.

This plan intentionally gates or redesigns Windows-only features (global hooks, system-key suppression, desktop wallpaper, WebView2 on non-Windows, NAudio/WASAPI ducking, GDI capture) rather than pretending they can be mechanically ported. The result will be a maintainable, testable, cross-platform codebase.
