# Port Batch 1 TODO

## Scope
Port WPF MainWindow partial logic into Avalonia ViewModels/commands:
- MainWindow.AccountShell.cs
- MainWindow.Settings.cs
- MainWindow.CloudBackup.cs
- MainWindow.SessionIO.cs
- MainWindow.Roadmap.cs

## TODO

### 1. Infrastructure / shared VM layer
- [x] Read source WPF partials and existing Avalonia files
- [x] Add localization helper access in Avalonia VMs (`Loc.Get` pattern)
- [x] Add dialog-service / file-picker abstractions to CCP.Core if missing
- [x] Add `IUiDispatcher` usage pattern for async UI callbacks
- [x] Add `IAppLogger` fallback (`DebugLogger`)
- [x] Add `IRoadmapService` abstraction + Avalonia in-memory stub

### 2. MainWindowViewModel (AccountShell + Settings top-level commands)
- [x] Bind `PlayerTitle`, `LevelText`, `UpdateButtonText` to runtime services
- [x] Implement `CheckUpdatesCommand` (open URL or call installer service)
- [x] Implement `SaveSettingsCommand` (persist settings, preset prompt simplified)
- [x] Implement `ExitApplicationCommand` (engine check, save, shutdown)
- [x] Add language selector VM + command (in AppInfoTabViewModel)

### 3. AppInfoTabViewModel (AccountShell + CloudBackup)
- [x] Login/logout commands for Patreon / Discord (unified-login stubs)
- [x] Open Discord invite / privacy policy / support link commands
- [x] Cloud backup commands: Backup, Restore, Export
- [x] Bind backup status text
- [x] Rich Presence toggle command

### 4. SettingsTabViewModel (Settings partial load/save bridge)
- [x] Surface settings properties for the Dashboard/Settings tab
- [x] Add Save / Help / Bug report commands

### 5. PresetsTabViewModel (SessionIO)
- [x] Inject `ISessionManager`, `SessionFileService`
- [x] Commands: Import, Export, Create, Edit, Delete session
- [x] Bind custom sessions list + selected session detail
- [x] Drag-drop handling delegate (UI passes files; VM runs import)

### 6. QuestsTabViewModel (Roadmap)
- [x] Inject `IRoadmapService` (Avalonia in-memory stub)
- [x] Commands: Switch sub-tab (Daily/Roadmap), select track
- [x] Bind roadmap nodes + stats
- [x] Commands: Start step, submit photo, view diary

### 7. XAML bindings
- [x] Bind header account shell buttons to MainWindowViewModel commands
- [x] Bind tab DataTemplates for new VM types
- [x] Keep existing FeatureControl bindings intact

### 8. Build & test
- [x] `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`
- [x] `dotnet build CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj -c Release`
- [x] `dotnet build CCP.Avalonia.Desktop.Linux/CCP.Avalonia.Desktop.Linux.csproj -c Release`
- [x] `dotnet test tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release`
- [x] Fix any build errors / warnings
