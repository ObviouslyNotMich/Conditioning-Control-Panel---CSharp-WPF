# Secondary Tab Richness Sprint Plan

**Goal:** restore WPF-equivalent hero images, rich cards, premium gates, and animated accents to the five highest-visibility Avalonia secondary tab views without changing business logic.

**Scope:** UI/XAML and ViewModel bindings only. Engine wiring (webcam, haptics, remote, quiz, chaos) stays stubbed where it is already stubbed.

---

## Shared Foundations

### Reusable controls to leverage
| Control | Location | Best used for |
|---|---|---|
| `FeatureCard` | `CCP.Avalonia/Features/FeatureCard.axaml` | Small image+title tiles with locked/active states |
| `ContentPackCard` | `CCP.Avalonia/Features/ContentPackCard.axaml` | Tall preview+info cards (asset packs, tier cards) |
| `LockCardFeatureControl` | `CCP.Avalonia/Features/LockCardFeatureControl.axaml` | Reference for gated setting rows |
| `LockdownTabView` gate pattern | `CCP.Avalonia/Views/Tabs/LockdownTabView.axaml` | Copy the translucent premium gate overlay |
| `AvaloniaInlineLoopVideo` | `CCP.Avalonia/Controls/AvaloniaInlineLoopVideo.cs` | Stage/video previews where WPF used `MediaElement` |
| `HelpPopover` (WPF only) | `Controls/HelpPopover.cs` | Avalonia has no port yet; use `ToolTip.Tip` or plain help buttons |

### Shared ViewModel work
- Add `IsPremiumLocked` / `HasPremiumAccess` observable to each tab VM, bound to the existing `Patreon`/`IAuthProvider` state.
- Keep all existing localized strings; only add new keys where WPF had hard-coded English or the richer UI introduces new labels.

---

## 1. HapticsTabView

### Current Avalonia state
Flat toggle rows and sliders; no hero, no algorithm cards, no premium gate.

### Visual elements to add
1. **Header with emoji icon**
   - `TextBlock` with `💜` (or an `Image` if an emoji-to-image converter is later ported).
2. **Hero description card**
   - 160×160 rounded image on the left: `Resources/features/vibe.png`.
   - Title: existing `label_what_is_this`.
   - Body: existing `desc_haptics` + `desc_haptics_bullets`.
   - Reuse `SurfaceBgBrush`/`PinkBrush` card styling.
3. **Connection card**
   - Header row with `🔌` icon, `label_haptic_connection`, and a help button.
   - Provider `ComboBox`, URL `TextBox`, URL hint, auto-connect `CheckBox`, Connect/Test buttons.
   - Add a `ToolTip.Tip` on the URL box using the existing connection-guide keys.
4. **Status + master intensity inset**
   - Status dot + `label_status` + `TxtHapticStatus`, devices label, intensity slider, test button.
5. **Per-feature intensity section**
   - `section_feature_intensity` header.
   - Two-column grid of feature rows: enable toggle, label, slider, percentage, mode `ComboBox`.
6. **Video Haptic Sync featured card**
   - Header with `🎵` icon, `label_video_haptic_sync`, master toggle.
   - Algorithm selection cards:
     - `label_audio_reactive` (selected, bordered).
     - `label_beat_detection` + `label_coming_soon` (blurred/locked look).
     - `label_ai_enhanced` + `label_coming_soon`.
   - Square image: `Resources/features/vibe.png`.
   - Delay + power sliders (existing `label_delay_2`, `label_power_2`).
7. **Premium gate overlay**
   - Full-width translucent overlay when `IsPremiumLocked`.
   - Lock icon, `gate_premium_locked`, `gate_premium_subtitle`, `gate_unlock_with_patreon` button.

### Assets required
| Asset | WPF path | Avalonia path |
|---|---|---|
| Vibe feature image | `Resources/features/vibe.png` | `avares://CCP.Avalonia/Assets/features/vibe.png` (already linked) |

### Suggested new localization keys
- `haptics_hero_title` — optional replacement for `label_what_is_this` if a feature-specific title is desired.
- `haptics_gate_headline` / `haptics_gate_body` / `haptics_gate_cta` — if you want Haptics-specific gate copy; otherwise reuse the generic `gate_*` keys.

---

## 2. BlinkTrainerTabView

### Current Avalonia state
A single debug-style stack of toggles and buttons; missing banner, stage, session/webcam cards, gate.

### Visual elements to add
1. **Hero banner**
   - `Resources/features/blink_trainer.png`, max-height 320, rounded border.
2. **Header row**
   - Animated eye logo (custom `Canvas`/`Path` with a 4 s open/closed blink loop).
   - Title `tab_blink_trainer`, `blink_trainer_beta` chip, `blink_trainer_tagline`.
   - Help button and blink-to-recal toggle.
3. **Two-column stage + gated zone**
   - Left: 448×288 stage frame with gradient border and rounded inner preview surface.
     - Use `AvaloniaInlineLoopVideo` or cross-fading `Image` controls for the demo/live preview.
   - Stage actions below: status dot + text, `blink_trainer_start_session` button, tracker start/stop button.
   - Right: gated content stack covered by the premium gate.
4. **Asset Packs card**
   - `blink_trainer_section_asset_packs` header.
   - Folder cards host (`ContentPackCard`-style small tiles or a custom dashed folder item).
   - Dashed `+ Add folder` button.
   - `blink_trainer_include_videos` toggle + subtitle.
5. **Session + Webcam cards (side-by-side)**
   - Session card: duration slider, opacity slider, mix-mode visual toggles (`blink_trainer_tiling_same` / `blink_trainer_tiling_mix`).
   - Webcam card: camera/monitor combo boxes, refresh button, consent status card, calibration/quick-recal buttons, restrict-gaze checkbox.
6. **Premium gate**
   - Cover only the right-column gated zone; leave the header, hero, and stage visible.
   - Use existing `blink_trainer_gate_headline`, `blink_trainer_gate_body`, `blink_trainer_gate_cta`.

### Assets required
| Asset | WPF path | Avalonia path |
|---|---|---|
| Blink Trainer hero | `Resources/features/blink_trainer.png` | `avares://CCP.Avalonia/Assets/features/blink_trainer.png` (already linked) |

### Suggested new localization keys
- `blink_trainer_status_stopped` / `blink_trainer_status_starting` / `blink_trainer_status_ready` (some may already exist).
- `blink_trainer_tracker_start` / `blink_trainer_tracker_stop`.
- `blink_trainer_preview_demo_label` — label for the stage preview when no session is running.

---

## 3. LabTabView

### Current Avalonia state
A simple vertical stack (192 lines vs WPF 847); missing hero, how-to-play, zone dividers, MIND/EYES cards, webcam engine bar, AI panel, wallpaper card, smokescreen.

### Visual elements to add
1. **Hero banner: “Down the Rabbit Hole”**
   - Gradient background, title + “NEW” chip, blurb.
   - Play-mode radio buttons (`Free Desktop`, `Story`).
   - `FALL IN` primary button and `Quick Drop` outline button.
   - `Announcements` checkbox.
2. **How-to-play expander**
   - `Expander` with `lab_how_to_play_title` + subtitle.
   - Sections: `lab_what_you_do`, `lab_the_two_bars`, `lab_a_descent`, `lab_what_you_keep`.
   - Emoji/icon bullet rows for left-click, press-hold, right-click, rabbits.
3. **MIND zone divider**
   - `TextBlock` “MIND” + fading gradient line.
4. **MIND cards (two-column)**
   - **Quiz Training card**: hero header `Resources/features/lab_quiz_hero.png`, `label_quiz_training`, beta badge, description, fullscreen/drone toggles, start/test buttons, Pop Quiz subsection, past quizzes list.
   - **AI Effects & Memory card**: hero header `Resources/features/lab_aimemory_hero.png`, `lab_ai_effects_memory_title`, local-AI notice, master effect toggle, effect permissions grid, chat memory section with `btn_forget_everything`.
5. **EYES zone divider**
   - “EYES” label + relocation chip to Blink Trainer.
6. **Webcam engine bar**
   - Horizontal bar with camera/monitor pickers, refresh, status pill, calibrate/quick-recal/start buttons.
   - Advanced `Expander` with tracker test, revoke consent, debug cursor, counters, log.
7. **EYES cards (two-column)**
   - **Gaze Minigame**: hero `Resources/features/lab_gaze_hero.png`, icon, title, beta badge, description, open button, “start tracking to play” hint.
   - **Focus Gaze**: hero `Resources/features/lab_focusgaze_hero.png`, icon, title, enable toggle, status, hint.
8. **Wallpaper Override card**
   - Gradient-border card, enable toggle, current wallpaper label, `btn_shuffle_wallpaper`.
9. **Smokescreen overlay**
   - Full-tab overlay when not whitelisted/T2.
   - Lock emoji, `label_beta_testing`, `label_these_experimental_features_are_currently_bei`.

### Assets required
| Asset | WPF path | Avalonia path |
|---|---|---|
| Quiz hero | `Resources/features/lab_quiz_hero.png` | already linked |
| AI memory hero | `Resources/features/lab_aimemory_hero.png` | already linked |
| Gaze hero | `Resources/features/lab_gaze_hero.png` | already linked |
| Focus gaze hero | `Resources/features/lab_focusgaze_hero.png` | already linked |

### Suggested new localization keys
All how-to-play strings were hard-coded in WPF:
- `lab_how_to_play_title`
- `lab_how_to_play_subtitle`
- `lab_what_you_do`
- `lab_verb_left_click_title` / `lab_verb_left_click_body`
- `lab_verb_hold_title` / `lab_verb_hold_body`
- `lab_verb_right_click_title` / `lab_verb_right_click_body`
- `lab_verb_rabbits_title` / `lab_verb_rabbits_body`
- `lab_the_two_bars`
- `lab_focus_bar` / `lab_heat_bar`
- `lab_a_descent_title`
- `lab_what_you_keep`
- `lab_zone_mind` / `lab_zone_eyes`
- `lab_webcam_engine_title` / `lab_webcam_engine_subtitle`
- `lab_tracker_status_stopped` / `lab_tracker_status_running`
- `lab_smokescreen_title` (or reuse `label_beta_testing`)

---

## 4. RemoteControlTabView

### Current Avalonia state
A minimal property panel; missing banner, description, tier cards, QR code, opt-in tags, emote editor, privacy toggles, gate.

### Visual elements to add
1. **Hero banner**
   - `Resources/features/remote_control.png`.
2. **Header row**
   - `🎮` icon, `tab_remote_control`, help button, master enable toggle.
3. **Description card**
   - Circular icon badge, `label_what_is_this`, `desc_remote_control`, `desc_remote_control_bullets`.
4. **Tier comparison cards (3 across)**
   - Light / Standard / Full.
   - Each with emoji icon, tier name, summary, bullet list.
   - Selected card gets the pink border; click updates `SelectedTier`.
5. **Directory opt-in panel**
   - `chk_optin_directory` toggle.
   - `desc_optin_directory_consent` body.
   - Tag wrap panel (bimbo, drone, trance, feminization, submission, degradation, audio_ok, soft_only, lockdown_ok, chastity).
   - Status textbox with character counter (`label_optin_status`, `0/80`).
   - `chk_remember_optin_details`.
6. **Pairing panel**
   - Left: “SESSION CODE” large Consolas code, PIN, Copy / Copy Link buttons, listed-in-directory confirmation.
   - Right: QR code image in a white rounded frame, `label_remote_pairing`.
7. **Status + command log**
   - Connected controller card with status dot + text.
   - Command history `ListBox`.
8. **Emote picker**
   - 5 editable preset circles (`ItemsControl` + custom preset item).
   - Custom row: icon badge, message `TextBox`, `btn_emote_send`.
   - Edit popup with icon/text fields and save/cancel.
9. **Privacy toggles**
   - `label_remote_stop_on_disconnect` + `desc_remote_stop_on_disconnect`.
   - `label_remote_share_avatar` + `desc_remote_share_avatar`.
10. **Premium gate overlay**
    - Generic gate when `IsPremiumLocked`, reusing `gate_premium_locked` etc.

### Assets required
| Asset | WPF path | Avalonia path |
|---|---|---|
| Remote Control hero | `Resources/features/remote_control.png` | already linked |

### New assets / packages
- QR code generation: WPF uses `QRCoder` 1.6.0. Decide whether to reference it in Avalonia/Core or switch to a Skia-based QR generator.

### Suggested new localization keys
- `remote_control_gate_headline` / `remote_control_gate_body` / `remote_control_gate_cta`
- `remote_session_code_label`
- `remote_pin_label`
- `remote_pairing_url_label`
- `remote_status_listed` / `remote_status_private_only`
- `remote_tier_summary_light` / `remote_tier_summary_standard` / `remote_tier_summary_full`
- `remote_tier_bullets_light` / `remote_tier_bullets_standard` / `remote_tier_bullets_full`

---

## 5. PatreonTabView

### Current Avalonia state
Plain account cards and bullet list; moved keyword triggers out; missing support-development card and tier badge visuals.

### Visual elements to add
1. **Header**
   - `label_patreon_exclusives`, `label_premium_features_for_supporters`, help button.
2. **Account login cards**
   - Patreon card (brand red `#FF424D`), SubscribeStar card (teal `#009E8F`), Discord card (blurple `#5865F2`).
   - Status, tier/info text, login/link buttons.
3. **Account linking section**
   - `label_link_accounts` + explanation, link buttons.
4. **Cloud backup section**
   - Status, backup/restore buttons.
5. **Data & privacy section**
   - Export data / privacy policy buttons.
6. **Premium benefits / support-development card**
   - Tier badge images: `Resources/Patreon tier1.png`, `Resources/Patreon tier2.png`, `Resources/Patreon tier3.png`.
   - `section_support_development`, `section_patreon_unlocks`, feature list.
   - `btn_visit_patreon`.

### Assets required
| Asset | WPF path | Note |
|---|---|---|
| Tier 1 badge | `Resources/Patreon tier1.png` | Not currently an AvaloniaResource |
| Tier 2 badge | `Resources/Patreon tier2.png` | Not currently an AvaloniaResource |
| Tier 3 badge | `Resources/Patreon tier3.png` | Not currently an AvaloniaResource |

Add these to `CCP.Avalonia/CCP.Avalonia.csproj` as `AvaloniaResource` links, e.g.:

```xml
<AvaloniaResource Include="..\Resources\Patreon tier*.png">
  <Link>Assets\patreon\%(Filename)%(Extension)</Link>
</AvaloniaResource>
```

### Suggested new localization keys
- `patreon_tier_badge_level1` / `patreon_tier_badge_level2` / `patreon_tier_badge_level3`
- `patreon_benefits_title`
- `patreon_support_card_body`
- `patreon_visit_patreon_cta`

---

## Recommended Execution Order

1. **Asset plumbing**
   - Add Patreon tier badge PNGs to Avalonia resources.
   - Confirm all `Resources/features/*.png` used below are already linked (they are).

2. **Shared gate overlay**
   - Extract the `LockdownTabView` gate pattern into a reusable `PremiumGateOverlay` control or at least copy the same XAML into each tab.
   - Add `IsPremiumLocked` to each tab VM.

3. **HapticsTabView**
   - Most isolated; service and VM already functional.
   - Add hero, algorithm cards, connection card styling, gate.

4. **RemoteControlTabView**
   - Pairing UI + QR code decision is the biggest dependency.
   - Add tier cards, opt-in tags, emote editor, privacy toggles, gate.

5. **BlinkTrainerTabView**
   - Stage frame and folder cards are UI-heavy but can be built around stubbed engine.
   - Add banner, eye animation, session/webcam cards, consent card, gate.

6. **LabTabView**
   - Largest view; do last.
   - Add hero, how-to-play expander, MIND/EYES zones/cards, webcam engine bar, wallpaper card, smokescreen.

7. **PatreonTabView**
   - Add tier badge visuals and support-development card; polish account cards.

---

## Blockers & Uncertainties

1. **Help popovers**
   - Avalonia has no `HelpPopover` port. Use `ToolTip.Tip` for now or schedule a separate “Avalonia HelpPopover” task.

2. **QR code generation**
   - WPF uses `QRCoder` 1.6.0. Verify it works in the Avalonia head without pulling in Windows-only dependencies. If not, use a Skia-compatible QR library.

3. **MediaElement / stage video**
   - Avalonia has no built-in `MediaElement`. Use `AvaloniaInlineLoopVideo` for looped clips or static image cross-fading for the demo stage.

4. **Webcam/gaze engine abstraction**
   - Blink Trainer and Lab webcam controls can be built now, but real device enumeration and tracker state will remain stubs until Core services are ported.

5. **AI effects panel**
   - The local-AI notice and effect permissions UI can be built, but live detection of local Ollama/cloud AI state needs the Companion service port.

6. **Emoji-to-image conversion**
   - WPF uses `EmojiToImageSource`. Avalonia can display emoji as text; color emoji support depends on platform font. For parity, consider porting an emoji renderer later.

7. **Expander styling**
   - Avalonia’s default `Expander` looks different from WPF `CollapsibleCard`. Budget time to create a shared `LabCard`/`CollapsibleCard` style.

8. **Localization coverage**
   - Lab how-to-play text and some Remote Control labels were hard-coded in WPF. New keys must be added to `Localization/Languages/en.json` (and ideally other languages) before XAML is merged.
