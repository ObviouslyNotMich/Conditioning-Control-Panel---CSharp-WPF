using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Services.Moderation;

using IModerationLog = ConditioningControlPanel.IModerationLog;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Dialog for editing AI companion prompt settings.
/// </summary>
public partial class CompanionPromptEditorDialog : Window
{
    private readonly global::ConditioningControlPanel.IAppLogger _logger;
    private readonly global::ConditioningControlPanel.Core.Services.Settings.ISettingsService _settings;


    private readonly CompanionPromptSettings _defaults;
    private readonly IPromptValidator _promptValidator;
    private readonly IModerationLog _moderationLog;
    private readonly IDialogService? _dialogService;
    private bool _hasUnsavedChanges;
    private readonly ObservableCollection<KnowledgeBaseLink> _knowledgeLinks = new();

    public CompanionPromptEditorDialog()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
        _settings = App.Services.GetRequiredService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>();
_defaults = CompanionPromptSettings.GetDefaults();
        _promptValidator = App.Services.GetRequiredService<IPromptValidator>();
        _moderationLog = App.Services.GetRequiredService<IModerationLog>();
        _dialogService = App.Services?.GetService<IDialogService>();
        LoadCurrentSettings();
        LoadKnowledgeLinks();
        UpdateActivePromptDisplay();
        ApplyPolicyBannerState();
    }

    private void ApplyPolicyBannerState()
    {
        var acked = _settings?.Current?.CompanionPrompt?.PromptEditorDisclaimerAcknowledged == true;
        if (PolicyBannerFull != null)
            PolicyBannerFull.IsVisible = !acked;
        if (PolicyBannerSlim != null)
            PolicyBannerSlim.IsVisible = acked;
    }

    private void BtnPolicyGotIt_Click(object? sender, RoutedEventArgs e)
    {
        var settings = _settings?.Current?.CompanionPrompt;
        if (settings != null)
        {
            settings.PromptEditorDisclaimerAcknowledged = true;
            _settings?.Save();
        }
        ApplyPolicyBannerState();
    }

    private void BtnPolicyRead_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(
                "https://app.cclabs.app/policies/prohibited-content") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "CompanionPromptEditorDialog: failed to open policy URL");
        }
    }

    private void LoadKnowledgeLinks()
    {
        _knowledgeLinks.Clear();
        var links = _settings?.Current?.GlobalKnowledgeBaseLinks;
        if (links != null)
        {
            foreach (var link in links)
                _knowledgeLinks.Add(link);
        }
        LstKnowledgeLinks.ItemsSource = _knowledgeLinks;
    }

    private void SaveKnowledgeLinks()
    {
        if (_settings?.Current == null) return;

        _settings.Current.GlobalKnowledgeBaseLinks.Clear();
        foreach (var link in _knowledgeLinks)
            _settings.Current.GlobalKnowledgeBaseLinks.Add(link);
    }

    private void UpdateActivePromptDisplay()
    {
        var activePromptId = _settings?.Current?.ActiveCommunityPromptId;

        if (!string.IsNullOrEmpty(activePromptId))
        {
            // TODO: CommunityPrompts service is not yet in CCP.Core.
            TxtActivePromptName.Text = Loc.Get("label_unknown_prompt");
            TxtActivePromptName.Foreground = new SolidColorBrush((Color)global::Avalonia.Application.Current!.Resources["Danger"]!);
        }
        else if (_settings?.Current?.CompanionPrompt?.UseCustomPrompt == true)
        {
            TxtActivePromptName.Text = Loc.Get("label_custom");
            TxtActivePromptName.Foreground = new SolidColorBrush((Color)global::Avalonia.Application.Current!.Resources["PinkColor"]!);
        }
        else
        {
            TxtActivePromptName.Text = Loc.Get("label_default");
            TxtActivePromptName.Foreground = new SolidColorBrush(Color.Parse("#707070"));
        }
    }

    private void LoadCurrentSettings()
    {
        var settings = _settings?.Current?.CompanionPrompt ?? new CompanionPromptSettings();

        ChkUseCustom.IsChecked = settings.UseCustomPrompt;

        TxtPersonality.Text = string.IsNullOrWhiteSpace(settings.Personality)
            ? _defaults.Personality : settings.Personality;
        TxtExplicitReaction.Text = string.IsNullOrWhiteSpace(settings.ExplicitReaction)
            ? _defaults.ExplicitReaction : settings.ExplicitReaction;
        TxtSlutMode.Text = string.IsNullOrWhiteSpace(settings.SlutModePersonality)
            ? _defaults.SlutModePersonality : settings.SlutModePersonality;
        TxtKnowledgeBase.Text = string.IsNullOrWhiteSpace(settings.KnowledgeBase)
            ? _defaults.KnowledgeBase : settings.KnowledgeBase;
        TxtContextReactions.Text = string.IsNullOrWhiteSpace(settings.ContextReactions)
            ? _defaults.ContextReactions : settings.ContextReactions;
        TxtOutputRules.Text = string.IsNullOrWhiteSpace(settings.OutputRules)
            ? _defaults.OutputRules : settings.OutputRules;

        UpdateEnabledState();
        _hasUnsavedChanges = false;
    }

    private void SaveSettings()
    {
        if (_settings?.Current == null) return;

        var settings = _settings.Current.CompanionPrompt;
        settings.UseCustomPrompt = ChkUseCustom.IsChecked == true;
        settings.Personality = TxtPersonality.Text ?? "";
        settings.ExplicitReaction = TxtExplicitReaction.Text ?? "";
        settings.SlutModePersonality = TxtSlutMode.Text ?? "";
        settings.KnowledgeBase = TxtKnowledgeBase.Text ?? "";
        settings.ContextReactions = TxtContextReactions.Text ?? "";
        settings.OutputRules = TxtOutputRules.Text ?? "";

        SaveKnowledgeLinks();

        _settings.Save();
        _hasUnsavedChanges = false;

        _logger?.Information("Companion prompt settings saved. UseCustomPrompt={UseCustom}, GlobalLinks={LinkCount}",
            settings.UseCustomPrompt, _knowledgeLinks.Count);
    }

    private void UpdateEnabledState()
    {
        var isEnabled = ChkUseCustom.IsChecked == true;
        ContentPanel.IsEnabled = isEnabled;
        ContentPanel.Opacity = isEnabled ? 1.0 : 0.5;
    }

    private void ChkUseCustom_Changed(object? sender, RoutedEventArgs e)
    {
        UpdateEnabledState();
        _hasUnsavedChanges = true;
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        _hasUnsavedChanges = true;
    }

    private void ResetPersonality_Click(object? sender, RoutedEventArgs e)
    {
        TxtPersonality.Text = _defaults.Personality;
    }

    private void ResetExplicitReaction_Click(object? sender, RoutedEventArgs e)
    {
        TxtExplicitReaction.Text = _defaults.ExplicitReaction;
    }

    private void ResetSlutMode_Click(object? sender, RoutedEventArgs e)
    {
        TxtSlutMode.Text = _defaults.SlutModePersonality;
    }

    private void ResetKnowledgeBase_Click(object? sender, RoutedEventArgs e)
    {
        TxtKnowledgeBase.Text = _defaults.KnowledgeBase;
    }

    private void ResetContextReactions_Click(object? sender, RoutedEventArgs e)
    {
        TxtContextReactions.Text = _defaults.ContextReactions;
    }

    private void ResetOutputRules_Click(object? sender, RoutedEventArgs e)
    {
        TxtOutputRules.Text = _defaults.OutputRules;
    }

    private async void AddKnowledgeLink_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new KnowledgeLinkEditorDialog();
        var result = await dialog.ShowDialog<bool?>(this);
        if (result == true && dialog.Result != null)
        {
            _knowledgeLinks.Add(dialog.Result);
            _hasUnsavedChanges = true;
        }
    }

    private async void RemoveKnowledgeLink_Click(object? sender, RoutedEventArgs e)
    {
        if (LstKnowledgeLinks.SelectedItem is KnowledgeBaseLink link)
        {
            _knowledgeLinks.Remove(link);
            _hasUnsavedChanges = true;
        }
        else
        {
            if (_dialogService != null)
            {
                await _dialogService.ShowMessageAsync(
                    Loc.Get("title_no_selection"),
                    Loc.Get("msg_please_select_a_link_to_remove"),
                    DialogSeverity.Info);
            }
        }
    }

    private async void ResetAll_Click(object? sender, RoutedEventArgs e)
    {
        var confirmed = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("title_reset_all_prompts"),
            Loc.Get("msg_reset_all_prompts_to_defaults")) ?? Task.FromResult(false));

        if (confirmed)
        {
            TxtPersonality.Text = _defaults.Personality;
            TxtExplicitReaction.Text = _defaults.ExplicitReaction;
            TxtSlutMode.Text = _defaults.SlutModePersonality;
            TxtKnowledgeBase.Text = _defaults.KnowledgeBase;
            TxtContextReactions.Text = _defaults.ContextReactions;
            TxtOutputRules.Text = _defaults.OutputRules;
        }
    }

    private void BtnSave_Click(object? sender, RoutedEventArgs e)
    {
        RunPromptValidation();
        SaveSettings();
        Close(true);
    }

    private void RunPromptValidation()
    {
        var fields = new (string FieldName, TextBox Box)[]
        {
            ("Personality", TxtPersonality),
            ("ExplicitReaction", TxtExplicitReaction),
            ("SlutModePersonality", TxtSlutMode),
            ("KnowledgeBase", TxtKnowledgeBase),
            ("ContextReactions", TxtContextReactions),
            ("OutputRules", TxtOutputRules),
        };

        var cleanBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
        var flaggedBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xC7, 0x6B));

        var flaggedNames = new List<string>();
        foreach (var (fieldName, box) in fields)
        {
            if (box == null) continue;
            var result = _promptValidator.Validate(box.Text ?? string.Empty);
            if (result.Clean)
            {
                box.BorderBrush = cleanBrush;
                box.BorderThickness = new Thickness(1);
                ToolTip.SetTip(box, null);
            }
            else
            {
                box.BorderBrush = flaggedBrush;
                box.BorderThickness = new Thickness(2);
                var tip = string.Format(
                    Loc.Get("prompt_validator_warning"),
                    result.MatchedPatterns.Count);
                ToolTip.SetTip(box, tip);
                flaggedNames.Add(fieldName);
                // TODO: ModerationLog service is not yet in CCP.Core.
                TryRecordModerationEdit(fieldName, result.MatchedPatterns.Count);
            }
        }

        if (flaggedNames.Count == 0)
        {
            ValidatorBanner.IsVisible = false;
        }
        else
        {
            TxtValidatorBanner.Text = string.Format(
                Loc.Get("prompt_validator_banner"),
                flaggedNames.Count);
            ValidatorBanner.IsVisible = true;
            _logger?.Information(
                "PromptValidator flagged {Count} field(s) in CompanionPromptEditorDialog",
                flaggedNames.Count);
        }
    }

    private void TryRecordModerationEdit(string fieldName, int count)
    {
        try
        {
            _moderationLog.RecordEdit(fieldName, count, "companion_prompt");
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "CompanionPromptEditorDialog: failed to record moderation edit");
        }
    }

    private async void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        if (_hasUnsavedChanges)
        {
            var confirmed = await (_dialogService?.ShowConfirmationAsync(
                Loc.Get("title_unsaved_changes"),
                Loc.Get("msg_discard_changes")) ?? Task.FromResult(false));

            if (!confirmed)
                return;
        }

        Close(false);
    }
}
