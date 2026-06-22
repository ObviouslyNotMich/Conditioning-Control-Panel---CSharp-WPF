using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Deeper;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Avalonia.Services.Tutorial;
using ConditioningControlPanel.Avalonia.Windows;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

/// <summary>
/// Avalonia port of the Deeper enhancement editor.
/// First-pass UI: TabControl with simple list + property panels for metadata,
/// regions, rules, and haptic events.
/// </summary>
public partial class DeeperEditorWindow : Window
{
    private readonly Enhancement _enhancement;
    private string? _filePath;
    private bool _isDirty;
    private bool _populating;

    private readonly List<NamedOption> _triggerOptions = new();
    private readonly List<NamedOption> _actionOptions = new();
    private readonly List<NamedOption> _seekTargetOptions = new();
    private readonly List<NamedOption> _effectTypeOptions = new();
    private readonly List<NamedOption> _mediaTypeOptions = new();
    private readonly List<string> _stockHapticPatterns = new();
    private const string CustomPatternLabel = "Custom…";

    private TutorialOverlay? _activeTutorialOverlay;

    public string? LoadedFilePath => _filePath;

    public DeeperEditorWindow()
    {
        InitializeComponent();
        _enhancement = new Enhancement();
        InitializeOptions();
        WireEvents();
        InitializeTimeline();
        LoadEnhancementIntoUi();
    }

    public DeeperEditorWindow(Enhancement enhancement, string? filePath) : this()
    {
        _filePath = filePath;
        if (enhancement != null)
        {
            // Copy the loaded enhancement into our working instance so edits don't leak
            // back to the caller until an explicit save.
            var json = EnhancementSerializer.Save(enhancement);
            _enhancement = EnhancementSerializer.Load(json);
            // First-pass editor only edits the legacy regions/rules/haptic_tracks collections.
            // Clear any loader-projected timeline items so they don't stale-dup the saved file.
            _enhancement.TimelineItems.Clear();
        }
        LoadEnhancementIntoUi();
    }

    private void InitializeOptions()
    {
        _mediaTypeOptions.Add(new NamedOption(MediaTypes.Audio, Loc.Get("deeper_editor_media_type_audio")));
        _mediaTypeOptions.Add(new NamedOption(MediaTypes.Video, Loc.Get("deeper_editor_media_type_video")));
        CmbMediaType.ItemsSource = _mediaTypeOptions;

        _triggerOptions.Add(new NamedOption(TriggerTypes.TimeReached, Loc.Get("deeper_friendly_trigger_time_reached")));
        _triggerOptions.Add(new NamedOption(TriggerTypes.RegionEntered, Loc.Get("deeper_friendly_trigger_region_entered")));
        _triggerOptions.Add(new NamedOption(TriggerTypes.RegionExited, Loc.Get("deeper_friendly_trigger_region_exited")));
        _triggerOptions.Add(new NamedOption(TriggerTypes.GazeTarget, Loc.Get("deeper_friendly_trigger_gaze_target")));
        _triggerOptions.Add(new NamedOption(TriggerTypes.GazeAvoid, Loc.Get("deeper_friendly_trigger_gaze_avoid")));
        _triggerOptions.Add(new NamedOption(TriggerTypes.AttentionLost, Loc.Get("deeper_friendly_trigger_attention_lost")));
        _triggerOptions.Add(new NamedOption(TriggerTypes.BlinkDetected, Loc.Get("deeper_friendly_trigger_blink_detected")));
        _triggerOptions.Add(new NamedOption(TriggerTypes.MouthOpen, Loc.Get("deeper_friendly_trigger_mouth_open")));
        CmbTriggerType.ItemsSource = _triggerOptions;

        _actionOptions.Add(new NamedOption(ActionTypes.Seek, Loc.Get("deeper_friendly_action_seek")));
        _actionOptions.Add(new NamedOption(ActionTypes.LoopRegion, Loc.Get("deeper_friendly_action_loop_region")));
        _actionOptions.Add(new NamedOption(ActionTypes.Pause, Loc.Get("deeper_friendly_action_pause")));
        _actionOptions.Add(new NamedOption(ActionTypes.PlayAudio, Loc.Get("deeper_friendly_action_play_audio")));
        _actionOptions.Add(new NamedOption(ActionTypes.TriggerHaptic, Loc.Get("deeper_friendly_action_trigger_haptic")));
        _actionOptions.Add(new NamedOption(ActionTypes.TriggerEffect, Loc.Get("deeper_friendly_action_trigger_effect")));
        _actionOptions.Add(new NamedOption(ActionTypes.ScreenShake, Loc.Get("deeper_friendly_action_screen_shake")));
        _actionOptions.Add(new NamedOption(ActionTypes.SetIntensity, Loc.Get("deeper_friendly_action_set_intensity")));
        CmbActionType.ItemsSource = _actionOptions;

        _seekTargetOptions.Add(new NamedOption(SeekTargets.Time, Loc.Get("deeper_editor_action_seek_time")));
        _seekTargetOptions.Add(new NamedOption(SeekTargets.RegionStart, "Region start"));
        _seekTargetOptions.Add(new NamedOption(SeekTargets.RegionEnd, "Region end"));
        CmbSeekTarget.ItemsSource = _seekTargetOptions;

        _effectTypeOptions.Add(new NamedOption(EffectTypes.Haptic, Loc.Get("deeper_friendly_effect_haptic")));
        _effectTypeOptions.Add(new NamedOption(EffectTypes.Flash, Loc.Get("deeper_friendly_effect_flash")));
        _effectTypeOptions.Add(new NamedOption(EffectTypes.Subliminal, Loc.Get("deeper_friendly_effect_subliminal")));
        _effectTypeOptions.Add(new NamedOption(EffectTypes.Overlay, Loc.Get("deeper_friendly_effect_overlay")));
        _effectTypeOptions.Add(new NamedOption(EffectTypes.Bubble, Loc.Get("deeper_friendly_effect_bubble")));
        CmbEffectType.ItemsSource = _effectTypeOptions;

        var overlayOptions = new List<NamedOption>
        {
            new(OverlayKinds.PinkFilter, "Pink filter"),
            new(OverlayKinds.Spiral, "Spiral"),
            new(OverlayKinds.BrainDrain, "Brain drain"),
        };
        CmbEffectOverlayKind.ItemsSource = overlayOptions;

        _stockHapticPatterns.AddRange(StockHapticPatterns.Names);
        _stockHapticPatterns.Add(CustomPatternLabel);

        CmbHapticPattern.ItemsSource = _stockHapticPatterns;
        CmbActionHapticPattern.ItemsSource = _stockHapticPatterns;
        CmbEffectHapticPattern.ItemsSource = _stockHapticPatterns;
    }

    private void WireEvents()
    {
        Closing += Window_Closing;
        Opened += DeeperEditorWindow_Opened;
        Closed += DeeperEditorWindow_Closed;
    }

    private void DeeperEditorWindow_Opened(object? sender, EventArgs e)
    {
        InitializePreview();
        _ = StartPendingTutorialPart2Async();
    }

    private async Task StartPendingTutorialPart2Async()
    {
        try
        {
            if (TutorialEventBus.PendingPart2Tutorial is not { } pendingType) return;
            TutorialEventBus.PendingPart2Tutorial = null;

            await Task.Delay(800);

            if (App.Tutorial == null) return;
            if (!IsLoaded) return;

            try { if (App.Tutorial.IsActive) App.Tutorial.Skip(); } catch { }
            App.Tutorial.Start(pendingType);
            _activeTutorialOverlay = new TutorialOverlay(this, App.Tutorial);
            _activeTutorialOverlay.Show();
        }
        catch (Exception ex)
        {
            App.Services?.GetService<IAppLogger>()?.Warning(ex, "Failed to start interactive tutorial Part 2");
        }
    }

    private void DeeperEditorWindow_Closed(object? sender, EventArgs e)
    {
        try { _activeTutorialOverlay?.Close(); } catch { }
        _activeTutorialOverlay = null;
    }


    private void LoadEnhancementIntoUi()
    {
        _populating = true;
        try
        {
            var meta = _enhancement.Metadata;
            TxtMetaName.Text = meta.Name ?? "";
            TxtMetaCreator.Text = meta.Creator ?? "";
            TxtMetaDescription.Text = meta.Description ?? "";
            TxtMetaMediaSource.Text = _enhancement.MediaSource ?? "";
            CmbMediaType.SelectedItem = _mediaTypeOptions.FirstOrDefault(o => o.Key == _enhancement.MediaType);
            UpdateCreatorLockUi();

            RefreshAllLists();
            UpdateTitle();
            RefreshValidationStatus();
        }
        finally
        {
            _populating = false;
        }
    }

    private void RefreshAllLists()
    {
        _populating = true;
        try
        {
            var selectedRegion = LstRegions.SelectedItem as Region;
            LstRegions.ItemsSource = null;
            LstRegions.ItemsSource = _enhancement.Regions;
            if (selectedRegion != null && _enhancement.Regions.Contains(selectedRegion))
                LstRegions.SelectedItem = selectedRegion;
            else if (_enhancement.Regions.Count > 0)
                LstRegions.SelectedIndex = 0;

            var selectedRule = LstRules.SelectedItem as EnhancementRule;
            LstRules.ItemsSource = null;
            LstRules.ItemsSource = _enhancement.Rules;
            if (selectedRule != null && _enhancement.Rules.Contains(selectedRule))
                LstRules.SelectedItem = selectedRule;
            else if (_enhancement.Rules.Count > 0)
                LstRules.SelectedIndex = 0;

            var selectedHaptic = LstHaptics.SelectedItem as HapticEvent;
            var events = GetPrimaryHapticEvents();
            LstHaptics.ItemsSource = null;
            LstHaptics.ItemsSource = events;
            if (selectedHaptic != null && events.Contains(selectedHaptic))
                LstHaptics.SelectedItem = selectedHaptic;
            else if (events.Count > 0)
                LstHaptics.SelectedIndex = 0;

            PopulateRegionDetail(LstRegions.SelectedItem as Region);
            PopulateRuleDetail(LstRules.SelectedItem as EnhancementRule);
            PopulateHapticDetail(LstHaptics.SelectedItem as HapticEvent);

            RefreshRegionDependentCombos();
            RebuildTimeline();
        }
        finally
        {
            _populating = false;
        }
    }

    private void RefreshRegionDependentCombos()
    {
        var regionOptions = new List<NamedOption> { new(null, Loc.Get("deeper_editor_rule_region_none")) };
        foreach (var r in _enhancement.Regions)
            regionOptions.Add(new NamedOption(r.Id, string.IsNullOrEmpty(r.Label) ? r.Id : $"{r.Label} ({r.Id})"));

        var selectedConstraint = CmbRuleRegionConstraint.SelectedItem as NamedOption;
        CmbRuleRegionConstraint.ItemsSource = regionOptions;
        CmbRuleRegionConstraint.SelectedItem = regionOptions.FirstOrDefault(o => o.Key == (selectedConstraint?.Key));

        var selectedTriggerRegion = CmbTriggerRegion.SelectedItem as NamedOption;
        CmbTriggerRegion.ItemsSource = regionOptions.Skip(1).ToList();
        CmbTriggerRegion.SelectedItem = regionOptions.Skip(1).FirstOrDefault(o => o.Key == (selectedTriggerRegion?.Key));

        var selectedSeekRegion = CmbSeekRegion.SelectedItem as NamedOption;
        CmbSeekRegion.ItemsSource = regionOptions.Skip(1).ToList();
        CmbSeekRegion.SelectedItem = regionOptions.Skip(1).FirstOrDefault(o => o.Key == (selectedSeekRegion?.Key));

        var selectedLoopRegion = CmbLoopRegion.SelectedItem as NamedOption;
        CmbLoopRegion.ItemsSource = regionOptions.Skip(1).ToList();
        CmbLoopRegion.SelectedItem = regionOptions.Skip(1).FirstOrDefault(o => o.Key == (selectedLoopRegion?.Key));
    }

    // ========================================================================
    // Metadata
    // ========================================================================

    private void Metadata_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_populating) return;
        _enhancement.Metadata.Name = TxtMetaName.Text ?? "";
        _enhancement.Metadata.Creator = TxtMetaCreator.Text ?? "";
        _enhancement.Metadata.Description = TxtMetaDescription.Text ?? "";
        _enhancement.MediaSource = TxtMetaMediaSource.Text ?? "";
        MarkDirty();
    }

    private void CmbMediaType_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_populating) return;
        var selected = CmbMediaType.SelectedItem as NamedOption;
        if (selected != null)
        {
            _enhancement.MediaType = selected.Key;
            MarkDirty();
        }
    }

    private bool _creatorLocked;

    private void BtnCreatorLockToggle_Click(object? sender, RoutedEventArgs e)
    {
        _creatorLocked = BtnCreatorLockToggle.IsChecked == true;
        UpdateCreatorLockUi();
    }

    private void UpdateCreatorLockUi()
    {
        if (BtnCreatorLockToggle == null || TxtMetaCreator == null) return;
        BtnCreatorLockToggle.IsChecked = _creatorLocked;
        BtnCreatorLockToggle.Content = _creatorLocked ? "🔒" : "🔓";
        TxtMetaCreator.IsReadOnly = _creatorLocked;
        TxtMetaCreator.Foreground = _creatorLocked
            ? (IBrush?)this.FindResource("TextDimBrush")
            : (IBrush?)this.FindResource("TextLightBrush");
    }

    public void SelectMetadataTab() => EditorTabControl.SelectedIndex = 2;
    public void SelectRulesTab() => EditorTabControl.SelectedIndex = 4;
    public void SelectHapticsTab() => EditorTabControl.SelectedIndex = 5;

    // ========================================================================
    // Regions
    // ========================================================================

    private void LstRegions_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_populating) return;
        CommitRegion();
        PopulateRegionDetail(LstRegions.SelectedItem as Region);
    }

    private void PopulateRegionDetail(Region? region)
    {
        RegionDetailPanel.IsVisible = region != null;
        if (region == null) return;

        _populating = true;
        try
        {
            TxtRegionLabel.Text = region.Label ?? "";
            TxtRegionStart.Text = region.Start.ToString("G6");
            TxtRegionEnd.Text = region.End.ToString("G6");
            TxtRegionColor.Text = region.Color ?? "";
        }
        finally
        {
            _populating = false;
        }
    }

    private void RegionField_LostFocus(object? sender, RoutedEventArgs e)
    {
        CommitRegion();
    }

    private void RegionField_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_populating) return;
        MarkDirty();
    }

    private void CommitRegion()
    {
        if (LstRegions.SelectedItem is not Region region) return;
        region.Label = TxtRegionLabel.Text ?? "";
        if (double.TryParse(TxtRegionStart.Text, out var start)) region.Start = start;
        if (double.TryParse(TxtRegionEnd.Text, out var end)) region.End = end;
        region.Color = TxtRegionColor.Text ?? "";
        RefreshValidationStatus();
    }

    private void BtnAddRegion_Click(object? sender, RoutedEventArgs e)
    {
        var start = _enhancement.Regions.LastOrDefault()?.End ?? 0.0;
        var region = new Region
        {
            Id = NewId(),
            Label = $"Region {_enhancement.Regions.Count + 1}",
            Start = start,
            End = start + 5.0,
            Color = "#7B5CFF",
        };
        _enhancement.Regions.Add(region);
        MarkDirty();
        RefreshAllLists();
        LstRegions.SelectedItem = region;
    }

    private void BtnDeleteRegion_Click(object? sender, RoutedEventArgs e)
    {
        if (LstRegions.SelectedItem is not Region region) return;
        _enhancement.Regions.Remove(region);
        MarkDirty();
        RefreshAllLists();
    }

    // ========================================================================
    // Rules
    // ========================================================================

    private void LstRules_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_populating) return;
        CommitRule();
        PopulateRuleDetail(LstRules.SelectedItem as EnhancementRule);
    }

    private void PopulateRuleDetail(EnhancementRule? rule)
    {
        RuleDetailPanel.IsVisible = rule != null;
        if (rule == null) return;

        _populating = true;
        try
        {
            ChkRuleEnabled.IsChecked = rule.Enabled;
            CmbTriggerType.SelectedItem = _triggerOptions.FirstOrDefault(o => o.Key == rule.Trigger?.Type);
            CmbActionType.SelectedItem = _actionOptions.FirstOrDefault(o => o.Key == rule.Action?.Type);
            TxtCooldownMs.Text = rule.CooldownMs.ToString();

            var constraintKey = string.IsNullOrEmpty(rule.RegionConstraint) ? null : rule.RegionConstraint;
            CmbRuleRegionConstraint.SelectedItem = (CmbRuleRegionConstraint.ItemsSource as IEnumerable<NamedOption>)?.FirstOrDefault(o => o.Key == constraintKey);

            RefreshRuleTriggerPanel(rule.Trigger);
            RefreshRuleActionPanel(rule.Action);
        }
        finally
        {
            _populating = false;
        }
    }

    private void RefreshRuleTriggerPanel(EnhancementTrigger? trigger)
    {
        PanelTriggerTime.IsVisible = false;
        PanelTriggerRegion.IsVisible = false;
        PanelTriggerRect.IsVisible = false;
        PanelTriggerMinDwell.IsVisible = false;
        PanelTriggerMinDuration.IsVisible = false;
        TxtTriggerNoParams.IsVisible = false;

        switch (trigger)
        {
            case TimeReachedTrigger t:
                PanelTriggerTime.IsVisible = true;
                TxtTriggerTime.Text = t.Time.ToString("G6");
                break;
            case RegionEnteredTrigger t:
            {
                PanelTriggerRegion.IsVisible = true;
                CmbTriggerRegion.SelectedItem = (CmbTriggerRegion.ItemsSource as IEnumerable<NamedOption>)?.FirstOrDefault(o => o.Key == t.RegionId);
                break;
            }
            case RegionExitedTrigger t:
            {
                PanelTriggerRegion.IsVisible = true;
                CmbTriggerRegion.SelectedItem = (CmbTriggerRegion.ItemsSource as IEnumerable<NamedOption>)?.FirstOrDefault(o => o.Key == t.RegionId);
                break;
            }
            case GazeTargetTrigger t:
            {
                PanelTriggerRect.IsVisible = true;
                PanelTriggerMinDwell.IsVisible = true;
                TxtTriggerRect.Text = string.Join(", ", t.Rect);
                TxtTriggerMinDwell.Text = t.MinDwellMs.ToString();
                break;
            }
            case GazeAvoidTrigger t:
            {
                PanelTriggerRect.IsVisible = true;
                PanelTriggerMinDwell.IsVisible = true;
                TxtTriggerRect.Text = string.Join(", ", t.Rect);
                TxtTriggerMinDwell.Text = t.MinDwellMs.ToString();
                break;
            }
            case AttentionLostTrigger t:
                PanelTriggerMinDuration.IsVisible = true;
                TxtTriggerMinDuration.Text = t.MinDurationMs.ToString();
                break;
            case BlinkDetectedTrigger:
            case MouthOpenTrigger:
                TxtTriggerNoParams.IsVisible = true;
                break;
        }
    }

    private void RefreshRuleActionPanel(EnhancementAction? action)
    {
        PanelActionSeek.IsVisible = false;
        PanelActionLoopRegion.IsVisible = false;
        PanelActionPause.IsVisible = false;
        PanelActionPlayAudio.IsVisible = false;
        PanelActionTriggerHaptic.IsVisible = false;
        PanelActionTriggerEffect.IsVisible = false;
        PanelActionScreenShake.IsVisible = false;
        PanelActionSetIntensity.IsVisible = false;

        switch (action)
        {
            case SeekAction a:
                PanelActionSeek.IsVisible = true;
                CmbSeekTarget.SelectedItem = _seekTargetOptions.FirstOrDefault(o => o.Key == a.Target);
                TxtSeekTime.Text = (a.Time ?? 0).ToString("G6");
                CmbSeekRegion.SelectedItem = (CmbSeekRegion.ItemsSource as IEnumerable<NamedOption>)?.FirstOrDefault(o => o.Key == a.RegionId);
                RefreshSeekPanels();
                break;
            case LoopRegionAction a:
                PanelActionLoopRegion.IsVisible = true;
                CmbLoopRegion.SelectedItem = (CmbLoopRegion.ItemsSource as IEnumerable<NamedOption>)?.FirstOrDefault(o => o.Key == a.RegionId);
                break;
            case PauseAction:
                PanelActionPause.IsVisible = true;
                break;
            case PlayAudioAction a:
                PanelActionPlayAudio.IsVisible = true;
                TxtAudioPath.Text = a.Path ?? "";
                TxtAudioVolume.Text = a.Volume.ToString();
                ChkAudioDuck.IsChecked = a.DuckOtherAudio;
                break;
            case TriggerHapticAction a:
                PanelActionTriggerHaptic.IsVisible = true;
                SetHapticPatternCombo(CmbActionHapticPattern, a.PatternName);
                PanelActionHapticCustom.IsVisible = IsCustomPattern(a.PatternName);
                TxtActionHapticCustom.Text = SerializeCustomPattern(a.CustomPattern);
                TxtActionHapticIntensity.Text = a.Intensity.ToString("G6");
                TxtActionHapticDurationMs.Text = a.DurationMs.ToString();
                break;
            case TriggerEffectAction a:
                PanelActionTriggerEffect.IsVisible = true;
                CmbEffectType.SelectedItem = _effectTypeOptions.FirstOrDefault(o => o.Key == a.EffectType);
                TxtEffectIntensity.Text = a.Intensity.ToString("G6");
                TxtEffectDurationMs.Text = a.DurationMs.ToString();
                SetHapticPatternCombo(CmbEffectHapticPattern, a.PatternName);
                TxtEffectImagePath.Text = a.ImagePath ?? "";
                ChkEffectPlaySound.IsChecked = a.PlaySound;
                TxtEffectText.Text = a.Text ?? "";
                CmbEffectOverlayKind.SelectedItem = (CmbEffectOverlayKind.ItemsSource as IEnumerable<NamedOption>)?.FirstOrDefault(o => o.Key == a.OverlayKind);
                TxtEffectOpacity.Text = a.Opacity.ToString("G6");
                TxtEffectMaxBubbles.Text = a.MaxBubbles.ToString();
                RefreshEffectPanels(a.EffectType);
                break;
            case ScreenShakeAction a:
                PanelActionScreenShake.IsVisible = true;
                TxtScreenShakeIntensity.Text = a.Intensity.ToString("G6");
                TxtScreenShakeDurationMs.Text = a.DurationMs.ToString();
                break;
            case SetIntensityAction a:
                PanelActionSetIntensity.IsVisible = true;
                TxtSetIntensityValue.Text = a.Value.ToString("G6");
                break;
        }
    }

    private void RefreshSeekPanels()
    {
        var target = (CmbSeekTarget.SelectedItem as NamedOption)?.Key;
        PanelSeekTime.IsVisible = target == SeekTargets.Time;
        PanelSeekRegion.IsVisible = target == SeekTargets.RegionStart || target == SeekTargets.RegionEnd;
    }

    private void RefreshEffectPanels(string effectType)
    {
        PanelEffectHaptic.IsVisible = effectType == EffectTypes.Haptic;
        PanelEffectFlash.IsVisible = effectType == EffectTypes.Flash;
        PanelEffectSubliminal.IsVisible = effectType == EffectTypes.Subliminal;
        PanelEffectOverlay.IsVisible = effectType == EffectTypes.Overlay;
        PanelEffectBubble.IsVisible = effectType == EffectTypes.Bubble;
    }

    private void ChkRuleEnabled_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_populating) return;
        CommitRule();
    }

    private void CmbTriggerType_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_populating) return;
        if (LstRules.SelectedItem is not EnhancementRule rule) return;

        var option = CmbTriggerType.SelectedItem as NamedOption;
        if (option == null) return;

        rule.Trigger = CreateTrigger(option.Key);
        MarkDirty();
        _populating = true;
        try
        {
            RefreshRuleTriggerPanel(rule.Trigger);
        }
        finally
        {
            _populating = false;
        }
        RefreshValidationStatus();
    }

    private void CmbActionType_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_populating) return;
        if (LstRules.SelectedItem is not EnhancementRule rule) return;

        var option = CmbActionType.SelectedItem as NamedOption;
        if (option == null) return;

        rule.Action = CreateAction(option.Key);
        MarkDirty();
        _populating = true;
        try
        {
            RefreshRuleActionPanel(rule.Action);
        }
        finally
        {
            _populating = false;
        }
        RefreshValidationStatus();
    }

    private void CmbRuleRegionConstraint_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_populating) return;
        CommitRule();
    }

    private void RuleField_LostFocus(object? sender, RoutedEventArgs e)
    {
        CommitRule();
    }

    private void RuleField_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_populating) return;
        MarkDirty();
    }

    private void RuleField_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_populating) return;
        CommitRule();
    }

    private void ChkAudioDuck_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_populating) return;
        CommitRule();
    }

    private void ChkEffectPlaySound_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_populating) return;
        CommitRule();
    }

    private void CommitRule()
    {
        if (LstRules.SelectedItem is not EnhancementRule rule) return;

        rule.Enabled = ChkRuleEnabled.IsChecked ?? true;
        if (int.TryParse(TxtCooldownMs.Text, out var cd)) rule.CooldownMs = cd;

        var constraint = CmbRuleRegionConstraint.SelectedItem as NamedOption;
        rule.RegionConstraint = string.IsNullOrEmpty(constraint?.Key) ? null : constraint.Key;

        CommitTrigger(rule.Trigger);
        CommitAction(rule.Action);
        RefreshValidationStatus();
    }

    private void CommitTrigger(EnhancementTrigger? trigger)
    {
        switch (trigger)
        {
            case TimeReachedTrigger t:
                if (double.TryParse(TxtTriggerTime.Text, out var time)) t.Time = time;
                break;
            case RegionEnteredTrigger t:
            {
                t.RegionId = (CmbTriggerRegion.SelectedItem as NamedOption)?.Key ?? "";
                break;
            }
            case RegionExitedTrigger t:
            {
                t.RegionId = (CmbTriggerRegion.SelectedItem as NamedOption)?.Key ?? "";
                break;
            }
            case GazeTargetTrigger t:
            {
                t.Rect = ParseRect(TxtTriggerRect.Text);
                if (int.TryParse(TxtTriggerMinDwell.Text, out var dwell)) t.MinDwellMs = dwell;
                break;
            }
            case GazeAvoidTrigger t:
            {
                t.Rect = ParseRect(TxtTriggerRect.Text);
                if (int.TryParse(TxtTriggerMinDwell.Text, out var dwell)) t.MinDwellMs = dwell;
                break;
            }
            case AttentionLostTrigger t:
                if (int.TryParse(TxtTriggerMinDuration.Text, out var dur)) t.MinDurationMs = dur;
                break;
        }
    }

    private void CommitAction(EnhancementAction? action)
    {
        switch (action)
        {
            case SeekAction a:
                a.Target = (CmbSeekTarget.SelectedItem as NamedOption)?.Key ?? SeekTargets.Time;
                if (double.TryParse(TxtSeekTime.Text, out var st)) a.Time = st;
                else a.Time = null;
                a.RegionId = (CmbSeekRegion.SelectedItem as NamedOption)?.Key;
                break;
            case LoopRegionAction a:
                a.RegionId = (CmbLoopRegion.SelectedItem as NamedOption)?.Key;
                break;
            case PlayAudioAction a:
                a.Path = TxtAudioPath.Text ?? "";
                if (int.TryParse(TxtAudioVolume.Text, out var vol)) a.Volume = Math.Clamp(vol, 0, 100);
                a.DuckOtherAudio = ChkAudioDuck.IsChecked ?? true;
                break;
            case TriggerHapticAction a:
                ApplyHapticPatternFromCombo(CmbActionHapticPattern, TxtActionHapticCustom, a);
                if (double.TryParse(TxtActionHapticIntensity.Text, out var hi)) a.Intensity = hi;
                if (int.TryParse(TxtActionHapticDurationMs.Text, out var hd)) a.DurationMs = hd;
                break;
            case TriggerEffectAction a:
                a.EffectType = (CmbEffectType.SelectedItem as NamedOption)?.Key ?? EffectTypes.Haptic;
                if (double.TryParse(TxtEffectIntensity.Text, out var ei)) a.Intensity = ei;
                if (int.TryParse(TxtEffectDurationMs.Text, out var ed)) a.DurationMs = ed;
                var effectPattern = CmbEffectHapticPattern.SelectedItem as string;
                if (IsCustomPattern(effectPattern))
                {
                    a.PatternName = null;
                    a.CustomPattern = null;
                }
                else
                {
                    a.PatternName = effectPattern;
                    a.CustomPattern = null;
                }
                a.ImagePath = TxtEffectImagePath.Text;
                a.PlaySound = ChkEffectPlaySound.IsChecked ?? true;
                a.Text = TxtEffectText.Text;
                a.OverlayKind = (CmbEffectOverlayKind.SelectedItem as NamedOption)?.Key;
                if (double.TryParse(TxtEffectOpacity.Text, out var eo)) a.Opacity = eo;
                if (int.TryParse(TxtEffectMaxBubbles.Text, out var mb)) a.MaxBubbles = mb;
                break;
            case ScreenShakeAction a:
                if (double.TryParse(TxtScreenShakeIntensity.Text, out var ssi)) a.Intensity = ssi;
                if (int.TryParse(TxtScreenShakeDurationMs.Text, out var ssd)) a.DurationMs = ssd;
                break;
            case SetIntensityAction a:
                if (double.TryParse(TxtSetIntensityValue.Text, out var siv)) a.Value = siv;
                break;
        }
    }

    private void BtnAddRule_Click(object? sender, RoutedEventArgs e)
    {
        var rule = new EnhancementRule
        {
            Trigger = new TimeReachedTrigger { Time = 0 },
            Action = new SeekAction { Target = SeekTargets.Time, Time = 0 },
            CooldownMs = 1000,
            Enabled = true,
        };
        _enhancement.Rules.Add(rule);
        MarkDirty();
        RefreshAllLists();
        LstRules.SelectedItem = rule;
        TutorialEventBus.Emit("RuleAdded");
    }

    private void BtnDeleteRule_Click(object? sender, RoutedEventArgs e)
    {
        if (LstRules.SelectedItem is not EnhancementRule rule) return;
        _enhancement.Rules.Remove(rule);
        MarkDirty();
        RefreshAllLists();
    }

    // ========================================================================
    // Haptics
    // ========================================================================

    private void LstHaptics_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_populating) return;
        CommitHaptic();
        PopulateHapticDetail(LstHaptics.SelectedItem as HapticEvent);
    }

    private void PopulateHapticDetail(HapticEvent? ev)
    {
        HapticDetailPanel.IsVisible = ev != null;
        if (ev == null)
        {
            CurveEditorPanel.IsVisible = false;
            return;
        }

        _populating = true;
        try
        {
            TxtHapticStart.Text = ev.Start.ToString("G6");
            TxtHapticDuration.Text = ev.Duration.ToString("G6");
            TxtHapticIntensity.Text = ev.Intensity.ToString("G6");
            SetHapticPatternCombo(CmbHapticPattern, ev.PatternName);
            var isCustom = IsCustomPattern(ev.PatternName);
            PanelHapticCustom.IsVisible = isCustom;
            CurveEditorPanel.IsVisible = isCustom;
            if (isCustom)
            {
                EnsureCurveSeed(ev);
                TxtHapticCustom.Text = SerializeCustomPattern(ev.CustomPattern);
                RebuildCurveEditor();
            }
        }
        finally
        {
            _populating = false;
        }
    }

    private void HapticField_LostFocus(object? sender, RoutedEventArgs e)
    {
        CommitHaptic();
    }

    private void HapticField_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_populating) return;
        MarkDirty();
        if (sender == TxtHapticCustom && LstHaptics.SelectedItem is HapticEvent ev)
        {
            ev.CustomPattern = DeserializeCustomPattern(TxtHapticCustom.Text);
            RebuildCurveEditor();
        }
    }

    private void CmbHapticPattern_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_populating) return;
        var isCustom = IsCustomPattern(CmbHapticPattern.SelectedItem as string);
        PanelHapticCustom.IsVisible = isCustom;
        CurveEditorPanel.IsVisible = isCustom;
        if (isCustom && LstHaptics.SelectedItem is HapticEvent ev)
        {
            EnsureCurveSeed(ev);
            TxtHapticCustom.Text = SerializeCustomPattern(ev.CustomPattern);
            RebuildCurveEditor();
        }
        CommitHaptic();
    }

    private void CommitHaptic()
    {
        if (LstHaptics.SelectedItem is not HapticEvent ev) return;
        if (double.TryParse(TxtHapticStart.Text, out var start)) ev.Start = start;
        if (double.TryParse(TxtHapticDuration.Text, out var dur)) ev.Duration = dur;
        if (double.TryParse(TxtHapticIntensity.Text, out var intensity)) ev.Intensity = intensity;
        ApplyHapticPatternFromCombo(CmbHapticPattern, TxtHapticCustom, ev);
        RefreshValidationStatus();
    }

    private void BtnAddHaptic_Click(object? sender, RoutedEventArgs e)
    {
        var track = GetOrCreatePrimaryTrack();
        var ev = new HapticEvent
        {
            Start = track.Events.LastOrDefault()?.Start ?? 0.0,
            Duration = 1.0,
            Intensity = 1.0,
            PatternName = StockHapticPatterns.Names.FirstOrDefault() ?? "Pulse",
        };
        track.Events.Add(ev);
        MarkDirty();
        RefreshAllLists();
        LstHaptics.SelectedItem = ev;
        TutorialEventBus.Emit("EffectAdded");
    }

    private void BtnDeleteHaptic_Click(object? sender, RoutedEventArgs e)
    {
        if (LstHaptics.SelectedItem is not HapticEvent ev) return;
        var track = GetPrimaryTrack();
        track?.Events.Remove(ev);
        MarkDirty();
        RefreshAllLists();
    }

    private async void BtnHapticTest_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var service = App.Services?.GetService<IHapticsService>();
            if (service is { IsConnected: true })
            {
                await service.TestAsync(50, 500);
            }
            else
            {
                var dialog = App.Services?.GetService<IDialogService>();
                if (dialog != null)
                    await dialog.ShowMessageAsync(Loc.Get("deeper_editor_haptic_test"), Loc.Get("deeper_editor_haptic_test_no_device"));
            }
        }
        catch { }
    }

    // ========================================================================
    // Save / Preview
    // ========================================================================

    private async void BtnSave_Click(object? sender, RoutedEventArgs e) => await SaveAsync(false);

    private async void BtnSaveAs_Click(object? sender, RoutedEventArgs e) => await SaveAsync(true);

    private async Task SaveAsync(bool forceDialog)
    {
        CommitAll();
        var path = _filePath;
        if (forceDialog || string.IsNullOrEmpty(path))
        {
            var dialog = App.Services?.GetService<IDialogService>();
            if (dialog == null) return;

            var filters = new[] { new FileFilter("Deeper enhancement (.ccpenh.json)", new[] { "ccpenh.json" }) };
            var defaultName = $"{(_enhancement.Metadata.Name ?? Loc.Get("deeper_editor_untitled")).Replace(' ', '_')}.ccpenh.json";
            path = await dialog.ShowSaveFileDialogAsync(Loc.Get("deeper_editor_save_dialog_title"), filters, defaultName);
            if (string.IsNullOrEmpty(path)) return;
            if (!path.EndsWith(".ccpenh.json", StringComparison.OrdinalIgnoreCase))
                path += ".ccpenh.json";
        }

        var issues = EnhancementValidator.Validate(_enhancement);
        var errors = issues.Count(i => i.Severity == ValidationSeverity.Error);
        if (errors > 0)
        {
            var dialog = App.Services?.GetService<IDialogService>();
            if (dialog != null)
            {
                var confirmed = await dialog.ShowConfirmationAsync(
                    Loc.Get("deeper_editor_save_invalid_title"),
                    string.Format(Loc.Get("deeper_editor_save_invalid_prompt_fmt"), errors));
                if (!confirmed) return;
            }
        }

        try
        {
            var json = EnhancementSerializer.Save(_enhancement);
            File.WriteAllText(path, json);
            _filePath = path;
            _isDirty = false;
            UpdateTitle();
            RefreshValidationStatus();
            TutorialEventBus.LastSavedEnhancementPath = path;
            TutorialEventBus.Emit("FileSaved");
        }
        catch (Exception ex)
        {
            var dialog = App.Services?.GetService<IDialogService>();
            if (dialog != null)
                await dialog.ShowMessageAsync(Loc.Get("deeper_editor_save_invalid_title"), string.Format(Loc.Get("deeper_editor_save_failed_fmt"), ex.Message));
        }
    }

    private void BtnPreview_Click(object? sender, RoutedEventArgs e)
    {
        CommitAll();
        try
        {
            var player = new EnhancementPlayerWindow(_enhancement, "editor-preview");
            player.Show();
        }
        catch (Exception ex)
        {
            var dialog = App.Services?.GetService<IDialogService>();
            _ = dialog?.ShowMessageAsync(Loc.Get("deeper_editor_preview_failed_title"), string.Format(Loc.Get("deeper_editor_preview_open_failed_fmt"), ex.Message));
        }
    }

    private async void Window_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (!_isDirty) return;
        e.Cancel = true;

        var dialog = App.Services?.GetService<IDialogService>();
        if (dialog == null) return;

        var result = await dialog.ShowConfirmationAsync(
            Loc.Get("deeper_editor_close_unsaved_title"),
            Loc.Get("deeper_editor_close_unsaved_prompt"));
        if (result)
        {
            await SaveAsync(false);
            if (!_isDirty)
                Close();
        }
        else
        {
            _isDirty = false;
            Close();
        }
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private void CommitAll()
    {
        CommitMetadata();
        CommitRegion();
        CommitRule();
        CommitHaptic();
    }

    private void CommitMetadata()
    {
        _enhancement.Metadata.Name = TxtMetaName.Text ?? "";
        _enhancement.Metadata.Creator = TxtMetaCreator.Text ?? "";
        _enhancement.Metadata.Description = TxtMetaDescription.Text ?? "";
        _enhancement.MediaSource = TxtMetaMediaSource.Text ?? "";
        var media = CmbMediaType.SelectedItem as NamedOption;
        if (media != null) _enhancement.MediaType = media.Key;
    }

    private void MarkDirty()
    {
        _isDirty = true;
        UpdateTitle();
        RefreshValidationStatus();
    }

    private void UpdateTitle()
    {
        var name = string.IsNullOrWhiteSpace(_enhancement.Metadata.Name)
            ? Loc.Get("deeper_editor_untitled")
            : _enhancement.Metadata.Name;
        var file = string.IsNullOrEmpty(_filePath) ? "" : $" - {Path.GetFileName(_filePath)}";
        var unsaved = _isDirty ? $" * {Loc.Get("deeper_editor_unsaved")}" : "";
        TxtWindowTitle.Text = $"{name}{file}{unsaved}";
        Title = TxtWindowTitle.Text;
    }

    private void RefreshValidationStatus()
    {
        var issues = EnhancementValidator.Validate(_enhancement);
        var errors = issues.Count(i => i.Severity == ValidationSeverity.Error);
        var warnings = issues.Count(i => i.Severity == ValidationSeverity.Warning);
        if (errors == 0 && warnings == 0)
        {
            TxtValidationStatus.Text = Loc.Get("deeper_editor_validation_clean");
            TxtValidationStatus.Foreground = this.FindResource("DeeperAccentBrush") as IBrush
                ?? this.FindResource("TextMutedBrush") as IBrush;
        }
        else
        {
            var parts = new List<string>();
            if (errors > 0) parts.Add(string.Format(Loc.Get("deeper_editor_validation_errors_fmt"), errors));
            if (warnings > 0) parts.Add(string.Format(Loc.Get("deeper_editor_validation_warnings_fmt"), warnings));
            TxtValidationStatus.Text = string.Join("  ·  ", parts);
            TxtValidationStatus.Foreground = this.FindResource("DangerBrush") as IBrush
                ?? this.FindResource("TextMutedBrush") as IBrush;
        }
    }

    private HapticTrack GetOrCreatePrimaryTrack()
    {
        var track = _enhancement.HapticTracks.FirstOrDefault();
        if (track == null)
        {
            track = new HapticTrack { Id = "primary" };
            _enhancement.HapticTracks.Add(track);
        }
        return track;
    }

    private HapticTrack? GetPrimaryTrack() => _enhancement.HapticTracks.FirstOrDefault();

    private List<HapticEvent> GetPrimaryHapticEvents()
    {
        var track = GetPrimaryTrack();
        return track?.Events ?? new List<HapticEvent>();
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..8];

    private static EnhancementTrigger CreateTrigger(string type) => type switch
    {
        TriggerTypes.TimeReached => new TimeReachedTrigger { Time = 0 },
        TriggerTypes.RegionEntered => new RegionEnteredTrigger { RegionId = "" },
        TriggerTypes.RegionExited => new RegionExitedTrigger { RegionId = "" },
        TriggerTypes.GazeTarget => new GazeTargetTrigger { Rect = new[] { 0.0, 0.0, 1.0, 1.0 }, MinDwellMs = 500 },
        TriggerTypes.GazeAvoid => new GazeAvoidTrigger { Rect = new[] { 0.0, 0.0, 1.0, 1.0 }, MinDwellMs = 500 },
        TriggerTypes.AttentionLost => new AttentionLostTrigger { MinDurationMs = 1500 },
        TriggerTypes.BlinkDetected => new BlinkDetectedTrigger(),
        TriggerTypes.MouthOpen => new MouthOpenTrigger(),
        _ => new NeverFiringTrigger { OriginalType = type },
    };

    private static EnhancementAction CreateAction(string type) => type switch
    {
        ActionTypes.Seek => new SeekAction { Target = SeekTargets.Time, Time = 0 },
        ActionTypes.LoopRegion => new LoopRegionAction(),
        ActionTypes.Pause => new PauseAction(),
        ActionTypes.PlayAudio => new PlayAudioAction { Path = "", Volume = 80, DuckOtherAudio = true },
        ActionTypes.TriggerHaptic => new TriggerHapticAction { PatternName = StockHapticPatterns.Names.FirstOrDefault(), Intensity = 1.0, DurationMs = 1000 },
        ActionTypes.TriggerEffect => new TriggerEffectAction { EffectType = EffectTypes.Haptic, PatternName = StockHapticPatterns.Names.FirstOrDefault(), Intensity = 1.0, DurationMs = 1000 },
        ActionTypes.ScreenShake => new ScreenShakeAction { Intensity = 0.5, DurationMs = 500 },
        ActionTypes.SetIntensity => new SetIntensityAction { Value = 0.5 },
        _ => new NoOpEnhancementAction { OriginalType = type },
    };

    private static double[] ParseRect(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new[] { 0.0, 0.0, 1.0, 1.0 };
        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new[] { 0.0, 0.0, 1.0, 1.0 };
        for (int i = 0; i < Math.Min(parts.Length, 4); i++)
        {
            if (double.TryParse(parts[i], out var v))
                result[i] = v;
        }
        return result;
    }

    private static bool IsCustomPattern(string? patternName) =>
        string.IsNullOrEmpty(patternName) || patternName == CustomPatternLabel;

    private void SetHapticPatternCombo(ComboBox combo, string? patternName)
    {
        if (string.IsNullOrEmpty(patternName))
            combo.SelectedItem = CustomPatternLabel;
        else
            combo.SelectedItem = patternName;
    }

    private void ApplyHapticPatternFromCombo(ComboBox combo, TextBox? customText, IHapticPatternTarget target)
    {
        var selected = combo.SelectedItem as string;
        if (IsCustomPattern(selected))
        {
            target.PatternName = null;
            target.CustomPattern = DeserializeCustomPattern(customText?.Text);
        }
        else
        {
            target.PatternName = selected;
            target.CustomPattern = null;
        }
    }

    private static string SerializeCustomPattern(List<double[]>? pattern)
    {
        if (pattern == null || pattern.Count == 0)
            return "[[0,0],[0.5,1],[1,0]]";
        try
        {
            return JsonConvert.SerializeObject(pattern);
        }
        catch
        {
            return "[[0,0],[0.5,1],[1,0]]";
        }
    }

    private static List<double[]>? DeserializeCustomPattern(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var result = JsonConvert.DeserializeObject<List<double[]>>(text);
            return result is { Count: > 0 } ? result : null;
        }
        catch
        {
            return null;
        }
    }

    private async void BtnPickTriggerRect_Click(object? sender, RoutedEventArgs e)
    {
        var rect = ParseRect(TxtTriggerRect.Text);
        var picker = new GazePickerWindow(rect, _enhancement.MediaSource)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 800,
            Height = 600
        };

        // For a first-pass editor without an embedded video preview, open the
        // picker centered over this window so the user can still draw a
        // normalized rect. They can refine the numeric values afterwards.
        var committed = await picker.ShowDialog<bool?>(this);
        if (committed == true)
        {
            TxtTriggerRect.Text = string.Join(", ", picker.ResultRect.Select(v => v.ToString("F3", CultureInfo.InvariantCulture)));
            RuleField_LostFocus(TxtTriggerRect, new RoutedEventArgs());
        }
    }

    private sealed class NamedOption
    {
        public string Key { get; }
        public string Label { get; }
        public NamedOption(string? key, string label)
        {
            Key = key ?? "";
            Label = label;
        }
        public override string ToString() => Label;
    }
}
