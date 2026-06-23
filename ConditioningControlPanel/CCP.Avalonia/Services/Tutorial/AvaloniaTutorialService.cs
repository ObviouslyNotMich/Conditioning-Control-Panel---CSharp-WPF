using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Windows;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Services.Deeper;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Deeper;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Services.Tutorial;

/// <summary>
/// Avalonia port of the interactive Deeper editor tutorial service.
/// Currently scoped to the three on-rails walkthroughs (HypnoTube, local audio,
/// local video) that split across <see cref="NewEnhancementDialog"/> Part 1 and
/// <see cref="DeeperEditorWindow"/> Part 2.
/// </summary>
public class AvaloniaTutorialService
{
    private List<TutorialStep> _currentSteps = new();
    private int _currentStepIndex;

    public event EventHandler<TutorialStep>? StepChanged;
    public event EventHandler? TutorialStarted;
    public event EventHandler? TutorialCompleted;

    public TutorialStep? CurrentStep =>
        _currentStepIndex >= 0 && _currentStepIndex < _currentSteps.Count
            ? _currentSteps[_currentStepIndex]
            : null;

    public int CurrentStepIndex => _currentStepIndex;
    public int TotalSteps => _currentSteps.Count;
    public IReadOnlyList<TutorialStep> CurrentSteps => _currentSteps;
    public bool IsActive { get; private set; }
    public bool IsFirstStep => _currentStepIndex == 0;
    public bool IsLastStep => _currentStepIndex == _currentSteps.Count - 1;
    public TutorialType CurrentTutorialType { get; private set; }

    public void Start(TutorialType type)
    {
        CurrentTutorialType = type;
        _currentSteps = GetStepsForTutorial(type);
        _currentStepIndex = 0;
        IsActive = true;
        TutorialStarted?.Invoke(this, EventArgs.Empty);

        if (CurrentStep != null)
        {
            CurrentStep.OnActivate?.Invoke();
            StepChanged?.Invoke(this, CurrentStep);
        }
    }

    public void Next()
    {
        if (!IsActive) return;

        if (_currentStepIndex < _currentSteps.Count - 1)
        {
            _currentStepIndex++;
            CurrentStep?.OnActivate?.Invoke();
            StepChanged?.Invoke(this, CurrentStep!);
        }
        else
        {
            Complete();
        }
    }

    public void Previous()
    {
        if (!IsActive || _currentStepIndex <= 0) return;

        _currentStepIndex--;
        CurrentStep?.OnActivate?.Invoke();
        StepChanged?.Invoke(this, CurrentStep!);
    }

    public void Skip()
    {
        Complete();
    }

    private void Complete()
    {
        if (!IsActive) return;
        IsActive = false;
        TutorialCompleted?.Invoke(this, EventArgs.Empty);
    }

    private List<TutorialStep> GetStepsForTutorial(TutorialType type) => type switch
    {
        TutorialType.Modding => CreateModdingSteps(),
        TutorialType.DeeperEditorInteractiveHT => CreateHtPart1Steps(),
        TutorialType.DeeperEditorInteractiveHTPart2 => CreateHtPart2Steps(),
        TutorialType.DeeperEditorInteractiveLocalAudio => CreateAudioPart1Steps(),
        TutorialType.DeeperEditorInteractiveLocalAudioPart2 => CreateAudioPart2Steps(),
        TutorialType.DeeperEditorInteractiveLocalVideo => CreateVideoPart1Steps(),
        TutorialType.DeeperEditorInteractiveLocalVideoPart2 => CreateVideoPart2Steps(),
        _ => new List<TutorialStep>()
    };

    private static List<TutorialStep> CreateModdingSteps() => new()
    {
        new TutorialStep
        {
            Id = "mod_welcome",
            Icon = "\uD83D\uDD27",
            Title = "Welcome to the Mod Creator!",
            Description = "This tool lets you build a complete mod visually — no manual file editing needed.\n\n" +
                          "We'll walk through each tab so you know exactly what everything does. " +
                          "You can reopen this guide anytime with the ? button in the title bar.",
            TextPosition = TutorialStepPosition.Center
        },
        new TutorialStep
        {
            Id = "mod_tab_info",
            Icon = "\u2139",
            Title = "Info",
            Description = "Start here — give your mod a name, author, version, and description.\n\n" +
                          "Add a preview image that shows in the mod manager so people know what your mod looks like at a glance.",
            RequiresTab = "mod:info",
            TextPosition = TutorialStepPosition.Center
        },
        new TutorialStep
        {
            Id = "mod_tab_theme",
            Icon = "\uD83C\uDFA8",
            Title = "Theme",
            Description = "Pick your mod's color scheme. Click any color swatch to open a color picker, or type hex codes directly.\n\n" +
                          "The preview strip at the top shows all your colors together so you can see how they look as a set.",
            RequiresTab = "mod:theme",
            TextPosition = TutorialStepPosition.Center
        },
        new TutorialStep
        {
            Id = "mod_tab_identity",
            Icon = "\uD83E\uDD16",
            Title = "Identity",
            Description = "Define who the companion is. Change their name, what they call the user, the mode name, and button labels.\n\n" +
                          "These labels appear throughout the entire app when your mod is active.",
            RequiresTab = "mod:identity",
            TextPosition = TutorialStepPosition.Center
        },
        new TutorialStep
        {
            Id = "mod_tab_achievements",
            Icon = "\uD83C\uDFC6",
            Title = "Achievements",
            Description = "Drag and drop custom achievement icons onto each slot. These replace the default badges.\n\n" +
                          "Leave slots empty to keep the originals — you only need to replace what you want to change.",
            RequiresTab = "mod:achievements",
            TextPosition = TutorialStepPosition.Center
        },
        new TutorialStep
        {
            Id = "mod_tab_features",
            Icon = "\u26A1",
            Title = "Features",
            Description = "Replace feature icons (flash, video, subliminal, etc.) with your own artwork.\n\n" +
                          "Drag PNG images onto any slot. These are the icons that appear on the main tabs and UI controls.",
            RequiresTab = "mod:features",
            TextPosition = TutorialStepPosition.Center
        },
        new TutorialStep
        {
            Id = "mod_tab_skills",
            Icon = "\uD83C\uDF32",
            Title = "Skills",
            Description = "Custom skill tree icons. Each slot maps to a skill in the progression system.\n\n" +
                          "Drag and drop your own icons to give the skill tree a completely different look.",
            RequiresTab = "mod:skills",
            TextPosition = TutorialStepPosition.Center
        },
        new TutorialStep
        {
            Id = "mod_tab_avatars",
            Icon = "\uD83D\uDC64",
            Title = "Avatars",
            Description = "Your mod can have up to 7 avatar sets with 4 poses each (Standby, Active, Alert, Override).\n\n" +
                          "Drag images to customize your companion's appearance at each evolution stage.",
            RequiresTab = "mod:avatars",
            TextPosition = TutorialStepPosition.Center
        },
        new TutorialStep
        {
            Id = "mod_tab_uiassets",
            Icon = "\uD83D\uDDBC",
            Title = "UI Assets",
            Description = "Replace bubbles, tube frames, logo, speech bubbles, and card art.\n\n" +
                          "These are the decorative elements throughout the app. Drop your images onto any slot to replace them.",
            RequiresTab = "mod:uiassets",
            TextPosition = TutorialStepPosition.Center
        },
        new TutorialStep
        {
            Id = "mod_tab_triggers",
            Icon = "\uD83D\uDCA5",
            Title = "Triggers",
            Description = "Customize trigger text — what happens on Freeze, Reset, Collapse, and Autonomy events.\n\n" +
                          "Type your own text for each trigger to match your mod's theme.",
            RequiresTab = "mod:triggers",
            TextPosition = TutorialStepPosition.Center
        },
        new TutorialStep
        {
            Id = "mod_tab_messages",
            Icon = "\uD83D\uDCE2",
            Title = "Messages",
            Description = "Set the companion's attention check messages and bubble count retry text.\n\n" +
                          "These show up during interactive moments when the app needs the user's attention.",
            RequiresTab = "mod:messages",
            TextPosition = TutorialStepPosition.Center
        },
        new TutorialStep
        {
            Id = "mod_tab_phrases",
            Icon = "\uD83D\uDCAC",
            Title = "Phrases",
            Description = "The big one! Add phrases for every situation — greetings, idle chatter, gaming, browsing, level ups, and more.\n\n" +
                          "Click a category to expand it, then add as many phrases as you want. " +
                          "The companion picks randomly from your list.",
            RequiresTab = "mod:phrases",
            TextPosition = TutorialStepPosition.Center
        },
        new TutorialStep
        {
            Id = "mod_tab_replacements",
            Icon = "\uD83D\uDD04",
            Title = "Text Replacements",
            Description = "Find-and-replace across the entire UI. Map words to your mod's equivalent.\n\n" +
                          "These apply everywhere automatically — every label, phrase, and message gets substituted.",
            RequiresTab = "mod:replacements",
            TextPosition = TutorialStepPosition.Center
        },
        new TutorialStep
        {
            Id = "mod_export",
            Icon = "\uD83D\uDCE5",
            Title = "Export Your Mod",
            Description = "When you're done, click 'Export as .ccpmod' at the bottom. Your mod is packaged into a single file ready to share!\n\n" +
                          "You can also load an existing .ccpmod to edit it using the 'Load .ccpmod' button.",
            TextPosition = TutorialStepPosition.Center
        }
    };

    #region Part 1 — NewEnhancementDialog

    private static List<TutorialStep> CreateHtPart1Steps() => new()
    {
        new TutorialStep
        {
            Id = "iht_create",
            Icon = "🎬",
            Title = Loc.Get("deeper_itut_ht_step1_title"),
            Description = Loc.Get("deeper_itut_ht_step1_body"),
            TargetElementName = "BtnCreate",
            TextPosition = TutorialStepPosition.Top,
            AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick
        }
    };

    private static List<TutorialStep> CreateAudioPart1Steps() => new()
    {
        new TutorialStep
        {
            Id = "iaud_browse",
            Icon = "📁",
            Title = Loc.Get("deeper_itut_audio_step1_title"),
            Description = Loc.Get("deeper_itut_audio_step1_body"),
            TargetElementName = "BtnBrowse",
            TextPosition = TutorialStepPosition.Top,
            AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick,
            AllowManualSkip = true,
            BlockBackgroundClicks = false
        },
        new TutorialStep
        {
            Id = "iaud_create",
            Icon = "✨",
            Title = Loc.Get("deeper_itut_audio_step2_title"),
            Description = Loc.Get("deeper_itut_audio_step2_body"),
            TargetElementName = "BtnCreate",
            TextPosition = TutorialStepPosition.Top,
            AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick,
            BlockBackgroundClicks = false
        }
    };

    private static List<TutorialStep> CreateVideoPart1Steps() => new()
    {
        new TutorialStep
        {
            Id = "ivid_browse",
            Icon = "📁",
            Title = Loc.Get("deeper_itut_video_step1_title"),
            Description = Loc.Get("deeper_itut_video_step1_body"),
            TargetElementName = "BtnBrowse",
            TextPosition = TutorialStepPosition.Top,
            AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick,
            AllowManualSkip = true,
            BlockBackgroundClicks = false
        },
        new TutorialStep
        {
            Id = "ivid_create",
            Icon = "✨",
            Title = Loc.Get("deeper_itut_video_step2_title"),
            Description = Loc.Get("deeper_itut_video_step2_body"),
            TargetElementName = "BtnCreate",
            TextPosition = TutorialStepPosition.Top,
            AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick,
            BlockBackgroundClicks = false
        }
    };

    #endregion

    #region Part 2 — DeeperEditorWindow

    private static List<TutorialStep> CreateHtPart2Steps() => new()
    {
        new TutorialStep
        {
            Id = "iht_metadata",
            Icon = "📝",
            Title = Loc.Get("deeper_itut_ht_step2_title"),
            Description = Loc.Get("deeper_itut_ht_step2_body"),
            TargetElementName = "TxtMetaName",
            TextPosition = TutorialStepPosition.Left,
            AdvanceTrigger = TutorialAdvanceTrigger.Manual,
            PrepareTargetWindowAction = DeeperTutorialPrep.ExpandMetadataDrawer
        },
        new TutorialStep
        {
            Id = "iht_lock",
            Icon = "🔒",
            Title = Loc.Get("deeper_itut_ht_step3_title"),
            Description = Loc.Get("deeper_itut_ht_step3_body"),
            TargetElementName = "BtnCreatorLockToggle",
            TextPosition = TutorialStepPosition.Left,
            AdvanceTrigger = TutorialAdvanceTrigger.Manual,
            PrepareTargetWindowAction = DeeperTutorialPrep.ExpandMetadataDrawer
        },
        new TutorialStep
        {
            Id = "iht_addeffect",
            Icon = "✨",
            Title = Loc.Get("deeper_itut_ht_step6_title"),
            Description = Loc.Get("deeper_itut_ht_step6_body"),
            TargetElementName = "BtnAddHaptic",
            TextPosition = TutorialStepPosition.Bottom,
            AdvanceTrigger = TutorialAdvanceTrigger.OnEvent,
            AdvanceEventName = "EffectAdded",
            PrepareTargetWindowAction = DeeperTutorialPrep.SelectHapticsTab
        },
        new TutorialStep
        {
            Id = "iht_intensity",
            Icon = "🎚",
            Title = Loc.Get("deeper_itut_ht_step7_title"),
            Description = Loc.Get("deeper_itut_ht_step7_body"),
            TargetElementName = "TxtHapticIntensity",
            TextPosition = TutorialStepPosition.Left,
            AdvanceTrigger = TutorialAdvanceTrigger.OnTextEquals,
            AdvanceValue = "0.5",
            PrepareTargetWindowAction = DeeperTutorialPrep.SelectHapticsTab
        },
        new TutorialStep
        {
            Id = "iht_pattern",
            Icon = "🌊",
            Title = Loc.Get("deeper_itut_ht_step8_title"),
            Description = Loc.Get("deeper_itut_ht_step8_body"),
            TargetElementName = "CmbHapticPattern",
            TextPosition = TutorialStepPosition.Left,
            AdvanceTrigger = TutorialAdvanceTrigger.OnSelectionEquals,
            AllowManualSkip = true,
            PrepareTargetWindowAction = DeeperTutorialPrep.SelectHapticsTab
        },
        new TutorialStep
        {
            Id = "iht_test",
            Icon = "🎮",
            Title = Loc.Get("deeper_itut_ht_step9_title"),
            Description = Loc.Get("deeper_itut_ht_step9_body"),
            TargetElementName = "BtnHapticTest",
            TextPosition = TutorialStepPosition.Top,
            AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick,
            AllowManualSkip = true,
            PrepareTargetWindowAction = DeeperTutorialPrep.SelectHapticsTab
        },
        new TutorialStep
        {
            Id = "iht_addrule",
            Icon = "🔗",
            Title = Loc.Get("deeper_itut_ht_step10_title"),
            Description = Loc.Get("deeper_itut_ht_step10_body"),
            TargetElementName = "BtnAddRule",
            TextPosition = TutorialStepPosition.Bottom,
            AdvanceTrigger = TutorialAdvanceTrigger.OnEvent,
            AdvanceEventName = "RuleAdded",
            PrepareTargetWindowAction = DeeperTutorialPrep.SelectRulesTab
        },
        new TutorialStep
        {
            Id = "iht_ruletime",
            Icon = "⏱",
            Title = Loc.Get("deeper_itut_ht_step11_title"),
            Description = Loc.Get("deeper_itut_ht_step11_body"),
            TargetElementName = "TxtTriggerTime",
            TextPosition = TutorialStepPosition.Left,
            AdvanceTrigger = TutorialAdvanceTrigger.OnTextEquals,
            AdvanceValue = "15",
            PrepareTargetWindowAction = DeeperTutorialPrep.SelectRulesTab
        },
        new TutorialStep
        {
            Id = "iht_ruleaction",
            Icon = "⚡",
            Title = Loc.Get("deeper_itut_ht_step12_title"),
            Description = Loc.Get("deeper_itut_ht_step12_body"),
            TargetElementName = "CmbActionType",
            TextPosition = TutorialStepPosition.Left,
            AdvanceTrigger = TutorialAdvanceTrigger.OnSelectionEquals,
            AdvanceValue = "screen_shake",
            MatchByTag = true,
            PrepareTargetWindowAction = DeeperTutorialPrep.SelectRulesTab
        },
        new TutorialStep
        {
            Id = "iht_actionintensity",
            Icon = "🎚",
            Title = Loc.Get("deeper_itut_ht_step13_title"),
            Description = Loc.Get("deeper_itut_ht_step13_body"),
            TargetElementName = "TxtScreenShakeIntensity",
            TextPosition = TutorialStepPosition.Left,
            AdvanceTrigger = TutorialAdvanceTrigger.OnTextEquals,
            AdvanceValue = "0.7",
            PrepareTargetWindowAction = DeeperTutorialPrep.SelectRulesTab
        },
        new TutorialStep
        {
            Id = "iht_save",
            Icon = "💾",
            Title = Loc.Get("deeper_itut_ht_step14_title"),
            Description = Loc.Get("deeper_itut_ht_step14_body"),
            TargetElementName = "BtnSave",
            TextPosition = TutorialStepPosition.Top,
            AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick
        },
        new TutorialStep
        {
            Id = "iht_savedialog",
            Icon = "💾",
            Title = Loc.Get("deeper_itut_ht_step15_title"),
            Description = Loc.Get("deeper_itut_ht_step15_body"),
            TextPosition = TutorialStepPosition.Center,
            AdvanceTrigger = TutorialAdvanceTrigger.OnEvent,
            AdvanceEventName = "FileSaved",
            AllowManualSkip = true,
            BlockBackgroundClicks = false
        },
        BuildDoneCard(
            "iht_done",
            Loc.Get("deeper_itut_ht_step16_title"),
            Loc.Get("deeper_itut_ht_step16_body"))
    };

    private static List<TutorialStep> CreateAudioPart2Steps() => new()
    {
        new TutorialStep
        {
            Id = "iaud_metadata",
            Icon = "📝",
            Title = Loc.Get("deeper_itut_audio_step3_title"),
            Description = Loc.Get("deeper_itut_audio_step3_body"),
            TargetElementName = "TxtMetaName",
            TextPosition = TutorialStepPosition.Left,
            AdvanceTrigger = TutorialAdvanceTrigger.Manual,
            PrepareTargetWindowAction = DeeperTutorialPrep.ExpandMetadataDrawer
        },
        new TutorialStep
        {
            Id = "iaud_addeffect",
            Icon = "✨",
            Title = Loc.Get("deeper_itut_audio_step6_title"),
            Description = Loc.Get("deeper_itut_audio_step6_body"),
            TargetElementName = "BtnAddHaptic",
            TextPosition = TutorialStepPosition.Bottom,
            AdvanceTrigger = TutorialAdvanceTrigger.OnEvent,
            AdvanceEventName = "EffectAdded",
            PrepareTargetWindowAction = DeeperTutorialPrep.SelectHapticsTab
        },
        new TutorialStep
        {
            Id = "iaud_intensity",
            Icon = "🎚",
            Title = Loc.Get("deeper_itut_audio_step7_title"),
            Description = Loc.Get("deeper_itut_audio_step7_body"),
            TargetElementName = "TxtHapticIntensity",
            TextPosition = TutorialStepPosition.Left,
            AdvanceTrigger = TutorialAdvanceTrigger.OnTextEquals,
            AdvanceValue = "0.5",
            PrepareTargetWindowAction = DeeperTutorialPrep.SelectHapticsTab
        },
        new TutorialStep
        {
            Id = "iaud_pattern",
            Icon = "🌊",
            Title = Loc.Get("deeper_itut_audio_step8_title"),
            Description = Loc.Get("deeper_itut_audio_step8_body"),
            TargetElementName = "CmbHapticPattern",
            TextPosition = TutorialStepPosition.Left,
            AdvanceTrigger = TutorialAdvanceTrigger.OnSelectionEquals,
            AllowManualSkip = true,
            PrepareTargetWindowAction = DeeperTutorialPrep.SelectHapticsTab
        },
        new TutorialStep
        {
            Id = "iaud_test",
            Icon = "🎮",
            Title = Loc.Get("deeper_itut_audio_step9_title"),
            Description = Loc.Get("deeper_itut_audio_step9_body"),
            TargetElementName = "BtnHapticTest",
            TextPosition = TutorialStepPosition.Top,
            AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick,
            AllowManualSkip = true,
            PrepareTargetWindowAction = DeeperTutorialPrep.SelectHapticsTab
        },
        new TutorialStep
        {
            Id = "iaud_addrule",
            Icon = "🔗",
            Title = Loc.Get("deeper_itut_audio_step10_title"),
            Description = Loc.Get("deeper_itut_audio_step10_body"),
            TargetElementName = "BtnAddRule",
            TextPosition = TutorialStepPosition.Bottom,
            AdvanceTrigger = TutorialAdvanceTrigger.OnEvent,
            AdvanceEventName = "RuleAdded",
            PrepareTargetWindowAction = DeeperTutorialPrep.SelectRulesTab
        },
        new TutorialStep
        {
            Id = "iaud_ruletime",
            Icon = "⏱",
            Title = Loc.Get("deeper_itut_audio_step11_title"),
            Description = Loc.Get("deeper_itut_audio_step11_body"),
            TargetElementName = "TxtTriggerTime",
            TextPosition = TutorialStepPosition.Left,
            AdvanceTrigger = TutorialAdvanceTrigger.OnTextEquals,
            AdvanceValue = "8",
            PrepareTargetWindowAction = DeeperTutorialPrep.SelectRulesTab
        },
        new TutorialStep
        {
            Id = "iaud_ruleaction",
            Icon = "⏸",
            Title = Loc.Get("deeper_itut_audio_step12_title"),
            Description = Loc.Get("deeper_itut_audio_step12_body"),
            TargetElementName = "CmbActionType",
            TextPosition = TutorialStepPosition.Left,
            AdvanceTrigger = TutorialAdvanceTrigger.OnSelectionEquals,
            AdvanceValue = "pause",
            MatchByTag = true,
            AllowManualSkip = true,
            PrepareTargetWindowAction = DeeperTutorialPrep.SelectRulesTab
        },
        new TutorialStep
        {
            Id = "iaud_save",
            Icon = "💾",
            Title = Loc.Get("deeper_itut_audio_step13_title"),
            Description = Loc.Get("deeper_itut_audio_step13_body"),
            TargetElementName = "BtnSave",
            TextPosition = TutorialStepPosition.Top,
            AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick
        },
        new TutorialStep
        {
            Id = "iaud_savedialog",
            Icon = "💾",
            Title = Loc.Get("deeper_itut_audio_step14_title"),
            Description = Loc.Get("deeper_itut_audio_step14_body"),
            TextPosition = TutorialStepPosition.Center,
            AdvanceTrigger = TutorialAdvanceTrigger.OnEvent,
            AdvanceEventName = "FileSaved",
            AllowManualSkip = true,
            BlockBackgroundClicks = false
        },
        BuildDoneCard(
            "iaud_done",
            Loc.Get("deeper_itut_audio_step15_title"),
            Loc.Get("deeper_itut_audio_step15_body"))
    };

    private static List<TutorialStep> CreateVideoPart2Steps() => new()
    {
        new TutorialStep
        {
            Id = "ivid_metadata",
            Icon = "📝",
            Title = Loc.Get("deeper_itut_video_step3_title"),
            Description = Loc.Get("deeper_itut_video_step3_body"),
            TargetElementName = "TxtMetaName",
            TextPosition = TutorialStepPosition.Left,
            AdvanceTrigger = TutorialAdvanceTrigger.Manual,
            PrepareTargetWindowAction = DeeperTutorialPrep.ExpandMetadataDrawer
        },
        new TutorialStep
        {
            Id = "ivid_addeffect",
            Icon = "✨",
            Title = Loc.Get("deeper_itut_video_step6_title"),
            Description = Loc.Get("deeper_itut_video_step6_body"),
            TargetElementName = "BtnAddHaptic",
            TextPosition = TutorialStepPosition.Bottom,
            AdvanceTrigger = TutorialAdvanceTrigger.OnEvent,
            AdvanceEventName = "EffectAdded",
            PrepareTargetWindowAction = DeeperTutorialPrep.SelectHapticsTab
        },
        new TutorialStep
        {
            Id = "ivid_intensity",
            Icon = "🎚",
            Title = Loc.Get("deeper_itut_video_step7_title"),
            Description = Loc.Get("deeper_itut_video_step7_body"),
            TargetElementName = "TxtHapticIntensity",
            TextPosition = TutorialStepPosition.Left,
            AdvanceTrigger = TutorialAdvanceTrigger.OnTextEquals,
            AdvanceValue = "0.5",
            PrepareTargetWindowAction = DeeperTutorialPrep.SelectHapticsTab
        },
        new TutorialStep
        {
            Id = "ivid_pattern",
            Icon = "🌊",
            Title = Loc.Get("deeper_itut_video_step8_title"),
            Description = Loc.Get("deeper_itut_video_step8_body"),
            TargetElementName = "CmbHapticPattern",
            TextPosition = TutorialStepPosition.Left,
            AdvanceTrigger = TutorialAdvanceTrigger.OnSelectionEquals,
            AllowManualSkip = true,
            PrepareTargetWindowAction = DeeperTutorialPrep.SelectHapticsTab
        },
        new TutorialStep
        {
            Id = "ivid_test",
            Icon = "🎮",
            Title = Loc.Get("deeper_itut_video_step9_title"),
            Description = Loc.Get("deeper_itut_video_step9_body"),
            TargetElementName = "BtnHapticTest",
            TextPosition = TutorialStepPosition.Top,
            AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick,
            AllowManualSkip = true,
            PrepareTargetWindowAction = DeeperTutorialPrep.SelectHapticsTab
        },
        new TutorialStep
        {
            Id = "ivid_addrule",
            Icon = "🔗",
            Title = Loc.Get("deeper_itut_video_step10_title"),
            Description = Loc.Get("deeper_itut_video_step10_body"),
            TargetElementName = "BtnAddRule",
            TextPosition = TutorialStepPosition.Bottom,
            AdvanceTrigger = TutorialAdvanceTrigger.OnEvent,
            AdvanceEventName = "RuleAdded",
            PrepareTargetWindowAction = DeeperTutorialPrep.SelectRulesTab
        },
        new TutorialStep
        {
            Id = "ivid_ruletrigger",
            Icon = "👁",
            Title = Loc.Get("deeper_itut_video_step11_title"),
            Description = Loc.Get("deeper_itut_video_step11_body"),
            TargetElementName = "CmbTriggerType",
            TextPosition = TutorialStepPosition.Left,
            AdvanceTrigger = TutorialAdvanceTrigger.OnSelectionEquals,
            AdvanceValue = "attention_lost",
            MatchByTag = true,
            AllowManualSkip = true,
            PrepareTargetWindowAction = DeeperTutorialPrep.SelectRulesTab
        },
        new TutorialStep
        {
            Id = "ivid_ruleaction",
            Icon = "⚡",
            Title = Loc.Get("deeper_itut_video_step12_title"),
            Description = Loc.Get("deeper_itut_video_step12_body"),
            TargetElementName = "CmbActionType",
            TextPosition = TutorialStepPosition.Left,
            AdvanceTrigger = TutorialAdvanceTrigger.OnSelectionEquals,
            AdvanceValue = "screen_shake",
            MatchByTag = true,
            AllowManualSkip = true,
            PrepareTargetWindowAction = DeeperTutorialPrep.SelectRulesTab
        },
        new TutorialStep
        {
            Id = "ivid_actionintensity",
            Icon = "🎚",
            Title = Loc.Get("deeper_itut_video_step13_title"),
            Description = Loc.Get("deeper_itut_video_step13_body"),
            TargetElementName = "TxtScreenShakeIntensity",
            TextPosition = TutorialStepPosition.Left,
            AdvanceTrigger = TutorialAdvanceTrigger.OnTextEquals,
            AdvanceValue = "0.7",
            PrepareTargetWindowAction = DeeperTutorialPrep.SelectRulesTab
        },
        new TutorialStep
        {
            Id = "ivid_save",
            Icon = "💾",
            Title = Loc.Get("deeper_itut_video_step14_title"),
            Description = Loc.Get("deeper_itut_video_step14_body"),
            TargetElementName = "BtnSave",
            TextPosition = TutorialStepPosition.Top,
            AdvanceTrigger = TutorialAdvanceTrigger.OnButtonClick
        },
        new TutorialStep
        {
            Id = "ivid_savedialog",
            Icon = "💾",
            Title = Loc.Get("deeper_itut_video_step15_title"),
            Description = Loc.Get("deeper_itut_video_step15_body"),
            TextPosition = TutorialStepPosition.Center,
            AdvanceTrigger = TutorialAdvanceTrigger.OnEvent,
            AdvanceEventName = "FileSaved",
            AllowManualSkip = true,
            BlockBackgroundClicks = false
        },
        BuildDoneCard(
            "ivid_done",
            Loc.Get("deeper_itut_video_step16_title"),
            Loc.Get("deeper_itut_video_step16_body"))
    };

    #endregion

    private static TutorialStep BuildDoneCard(string id, string title, string body) => new()
    {
        Id = id,
        Icon = "🎉",
        Title = title,
        Description = body,
        TextPosition = TutorialStepPosition.Center,
        IsFollowUpCard = true,
        FollowUpButton1Text = Loc.Get("deeper_itut_ht_followup_open_folder"),
        FollowUpAction1 = _ =>
        {
            RevealInFileManager(TutorialEventBus.LastSavedEnhancementPath);
            try { App.Tutorial?.Skip(); } catch { }
        },
        FollowUpButton2Text = Loc.Get("deeper_itut_ht_followup_open_player"),
        FollowUpAction2 = _ =>
        {
            try { OpenPlayerWithLastSavedEnhancement(); } catch { }
            try { App.Tutorial?.Skip(); } catch { }
        },
        FollowUpButton3Text = Loc.Get("deeper_itut_ht_followup_done"),
        FollowUpAction3 = _ =>
        {
            try { App.Tutorial?.Skip(); } catch { }
        }
    };

    private static void OpenPlayerWithLastSavedEnhancement()
    {
        try
        {
            var path = TutorialEventBus.LastSavedEnhancementPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            var enhancement = EnhancementSerializer.LoadFromFile(path);
            var player = new EnhancementPlayerWindow(enhancement, "tutorial-followup");
            player.Show();
        }
        catch (Exception ex)
        {
            App.Services?.GetRequiredService<ILogger<AvaloniaTutorialService>>().LogDebug("AvaloniaTutorialService: failed to open player with last saved enhancement: {Error}", ex.Message);
        }
    }

    private static void RevealInFileManager(string? path)
    {
        try
        {
            if (string.IsNullOrEmpty(path)) return;
            var full = Path.GetFullPath(path);
            if (!File.Exists(full) && !Directory.Exists(full)) return;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{full}\"",
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"-R \"{full}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                var dir = File.Exists(full) ? Path.GetDirectoryName(full) : full;
                if (!string.IsNullOrEmpty(dir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "xdg-open",
                        Arguments = $"\"{dir}\"",
                        UseShellExecute = true
                    });
                }
            }
        }
        catch { }
    }
}

/// <summary>
/// Preparation callbacks used by the Deeper editor tutorial steps. Each step that
/// targets a control inside a specific editor tab asks the window to select that
/// tab before the overlay computes spotlight bounds.
/// </summary>
internal static class DeeperTutorialPrep
{
    public static readonly Action<object> ExpandMetadataDrawer = o =>
    {
        try { (o as DeeperEditorWindow)?.SelectMetadataTab(); } catch { }
    };

    public static readonly Action<object> SelectHapticsTab = o =>
    {
        try { (o as DeeperEditorWindow)?.SelectHapticsTab(); } catch { }
    };

    public static readonly Action<object> SelectRulesTab = o =>
    {
        try { (o as DeeperEditorWindow)?.SelectRulesTab(); } catch { }
    };
}
