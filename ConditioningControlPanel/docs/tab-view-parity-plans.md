# Tab View Parity Plans

Detailed implementation plans produced by planning sub-agents for the remaining Avalonia tab views.

---

## PresetsTabView

1. **Restructure `PresetsTabView.axaml` to the WPF two-column layout**
   - File: `CCP.Avalonia/Views/Tabs/PresetsTabView.axaml`
   - Replace stub with `Grid` columns `*,5,430` and `GridSplitter`.
   - Left: preset strip, sessions list, drop-zone + editor row.
   - Right: details panel with preset/session views and action buttons.

2. **Create reusable `PresetCard` control**
   - Files: `CCP.Avalonia/Features/PresetCard.axaml`, `.axaml.cs`
   - Show preset name, DEFAULT/CUSTOM badge, feature stat glyphs.
   - Support selection via pink border, `IsSelected`, `Click`/`Command`.

3. **Build horizontal preset strip with “New Preset” tile**
   - Use `ItemsControl` in horizontal `ScrollViewer` for `Presets`.
   - Fixed “New Preset” card bound to `SaveNewPresetCommand`.

4. **Build rich session list**
   - Replace simple `ListBox` with styled rows: name, duration/difficulty/XP badges, description, Edit/Export buttons.
   - Bind row `PointerPressed` to `SelectSessionCommand`.

5. **Add drop zone and session editor mini-panel**
   - Drag-drop `Border` with status text, editor panel with Create/Export buttons.
   - Add drag event handlers in code-behind.

6. **Port right-side details panel**
   - Preset view: title/subtitle + feature detail labels.
   - Session view: duration/reward/difficulty/description, corner-GIF panel, spoiler warning, Reveal Details button.

7. **Add preset/session action buttons**
   - Load Preset, Save, Delete, Export Preset, Share to Catalogue, Start Session.

8. **Wire code-behind event handlers to ViewModel commands**

9. **Add missing ViewModel properties/commands**
   - `SelectedSession`, `StartSessionCommand`, `SelectSessionCommand`, `RevealSpoilers`, corner-GIF options.

10. **Replace hard-coded English with localization bindings**

11. **Set up design-time data**

12. **Compile and designer sanity check**

---

## QuestsTabView

1. **Create reusable `QuestCard` control**
   - Files: `CCP.Avalonia/Features/QuestCard.axaml`, `.axaml.cs`
   - 150×150 image area, checkmark overlay, progress track/fill, XP badge, reroll button.
   - Properties: `Title`, `Description`, `Glyph`, `ImageUri`, `ProgressFraction`, `ProgressText`, `XpText`, `IsCompleted`, `RerollCommand`, etc.

2. **Add missing bindable properties to `QuestsTabViewModel`**
   - Localized labels, image URIs, progress fractions, login-overlay visibility, segment completion states.

3. **Rewrite `QuestsTabView.axaml` to match WPF layout**
   - Sub-tab navigation, season banner, daily/weekly `QuestCard`s, all-completed message, quest-complete banner, streak calendar, quest statistics, roadmap panel, track selector, roadmap nodes.

4. **Build rich roadmap node cards with lock overlays**
   - Extend `FeatureCard` or create `RoadmapNodeCard`.

5. **Wire all commands and interactions**
   - Sub-tabs, reroll, Fix Streak, calendar days, track tabs, node selection.

6. **Replace hard-coded English with localization bindings**

7. **Populate design-time data**

8. **Build and validate**

---

## EnhancementsTabView

1. **Expose skill images as Avalonia assets**
   - Add `AvaloniaResource` for `Resources\skills\**\*` → `Assets\skills\...`.

2. **Create reusable `SkillNodeCard` control**
   - Files: `CCP.Avalonia/Features/SkillNodeCard.axaml`, `.axaml.cs`
   - Full-bleed skill image, title strip, cost/status button, lock overlay, state-based border/glow.

3. **Extend `SkillNodeViewModel` for card binding**
   - Add `IconUri`, `PrerequisiteName`, `PositionX`, `PositionY`.

4. **Update `EnhancementsTabViewModel`**
   - Login state, header, skill-tree layout, connections collection.

5. **Add connection-line rendering**
   - `SkillConnectionViewModel` with coordinates and stroke by unlock state.

6. **Rewrite `EnhancementsTabView.axaml`**
   - Header border, horizontal scrollable skill tree `Canvas`, login overlay.

7. **Add horizontal mouse-wheel scrolling**

8. **Verify design-time data and build**

---

## AssetsTabView

1. **Extend `AssetsTabViewModel` for pack-card parity**
   - Pack-card properties: `IsDownloading`, `DownloadProgress`, `CurrentPreviewImage`, `ActivateButtonText`, etc.
   - Missing commands: `ActivatePackCommand`, `OpenAssetPreviewCommand`, `OpenAssetInExplorerCommand`.

2. **Create reusable `ContentPackCard` control**
   - Files: `CCP.Avalonia/Features/ContentPackCard.axaml`, `.axaml.cs`
   - 192×240 card with preview image, badges, title, description, install/activate buttons, progress bar.

3. **Localize all hard-coded strings**

4. **Rebuild tab header**

5. **Port Content Packs section**
   - Collapsible `PacksSection`, horizontal `ScrollViewer`, `ContentPackCard` items.

6. **Port Asset Browser split view**
   - `TreeView` with checkboxes + thumbnail `ItemsControl`/`WrapPanel`.
   - Select/Deselect and Preset action bars.

7. **Add design-time data context**

8. **Build and smoke-test**

---

## DeeperTabView

1. **Extend `DeeperTabViewModel` with WPF-equivalent hub state**
   - Search, filters, sort, filtered entries, welcome card visibility, webcam state, commands.

2. **Create/extend per-row ViewModel**
   - `DeeperLibraryRowViewModel` with media badges, tags, submission state.

3. **Rewrite `DeeperTabView.axaml` to match WPF layout**
   - Header, welcome card, search + filter/sort strip, library list border, webcam setup card.

4. **Style filter pills and row action buttons**
   - Add `DeeperPillStyle` and `DeeperRowActionBtnStyle` themes.

5. **Wire every button to commands and drop placeholder sections**

6. **Replace hard-coded English with localization bindings**

7. **Add rich cards, images, and lock-overlay support**

8. **Add design-time sample data**

9. **Clean up code-behind and verify**

---

## LevelFeaturesTabView

1. **Expose lock state and feature metadata in ViewModel**
   - `PlayerLevel`, `IsBubbleCountLocked`, `IsBouncingTextLocked`, `IsBrainDrainLocked`, `IsMindWipeLocked`, lock levels.

2. **Localize all hard-coded strings in the view**

3. **Add `FeatureCard` dashboard at the top of the tab**
   - 2×2 grid of `FeatureCard`s with images, active states, lock overlays.

4. **Restructure existing settings stack into a detail card**
   - Horizontal card layout with image thumbnail, settings stack, locked overlay.

5. **Wire up functional buttons and commands**
   - Route `FeatureCard.Click` and `ToggleRequested` to ViewModel.

6. **Add design-time data context**

7. **Build and verify**

---

## Recommended Execution Order

1. `LevelFeaturesTabView` — simplest, validates `FeatureCard` + lock overlay pattern.
2. `QuestsTabView` — high user visibility, uses `QuestCard` + roadmap nodes.
3. `EnhancementsTabView` — skill tree with images and connections.
4. `DeeperTabView` — hub layout with filter pills and rich rows.
5. `PresetsTabView` — two-column layout with cards and details.
6. `AssetsTabView` — most complex (tree + thumbnails + context menus).
