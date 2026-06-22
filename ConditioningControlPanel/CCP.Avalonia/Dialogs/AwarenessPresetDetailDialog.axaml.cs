using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Styling;
using global::Avalonia.Platform.Storage;

using IModerationLog = ConditioningControlPanel.IModerationLog;
using ConditioningControlPanel.Avalonia.Services.Companion;
using ConditioningControlPanel.Avalonia.Services.KeywordTriggers;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Moderation;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Modal view of an Awareness Engine preset pack with full inline action editing.
/// </summary>
public partial class AwarenessPresetDetailDialog : Window
{
    private readonly global::ConditioningControlPanel.IAppLogger _logger;
    private readonly global::ConditioningControlPanel.Core.Services.Settings.ISettingsService _settings;


    private readonly KeywordTriggerPreset _preset;
    private readonly IPromptValidator _promptValidator;
    private readonly IKeywordTriggerPresetService _keywordPresetService;
    private readonly IKeywordTriggerService _keywordTriggerService;
    private readonly ICompanionPhraseService _companionPhraseService;
    private readonly IModerationLog _moderationLog;
    private readonly IDialogService? _dialogService;
    private bool _isCustomPresetUnsaved;
    public bool Changed { get; private set; }

    public AwarenessPresetDetailDialog()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
        _settings = App.Services.GetRequiredService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>();
_preset = new KeywordTriggerPreset();
        _promptValidator = App.Services.GetRequiredService<IPromptValidator>();
        _keywordPresetService = App.Services.GetRequiredService<IKeywordTriggerPresetService>();
        _keywordTriggerService = App.Services.GetRequiredService<IKeywordTriggerService>();
        _companionPhraseService = App.Services.GetRequiredService<ICompanionPhraseService>();
        _moderationLog = App.Services.GetRequiredService<IModerationLog>();
        _dialogService = App.Services?.GetService<IDialogService>();
    }

    public AwarenessPresetDetailDialog(KeywordTriggerPreset preset)
        : this(preset, isNewCustomPreset: false) { }

    public AwarenessPresetDetailDialog(KeywordTriggerPreset preset, bool isNewCustomPreset)
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
        _settings = App.Services.GetRequiredService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>();
_preset = preset;
        _promptValidator = App.Services.GetRequiredService<IPromptValidator>();
        _keywordPresetService = App.Services.GetRequiredService<IKeywordTriggerPresetService>();
        _keywordTriggerService = App.Services.GetRequiredService<IKeywordTriggerService>();
        _companionPhraseService = App.Services.GetRequiredService<ICompanionPhraseService>();
        _moderationLog = App.Services.GetRequiredService<IModerationLog>();
        _dialogService = App.Services?.GetService<IDialogService>();
        _isCustomPresetUnsaved = isNewCustomPreset;

        if (preset.IsBuiltIn)
        {
            TxtIcon.Text = preset.Icon;
            TxtName.Text = preset.Name;
            TxtAuthor.Text = preset.Author;
            TxtDescription.Text = string.IsNullOrEmpty(preset.LongDescription)
                ? preset.Description
                : preset.LongDescription;
        }
        else
        {
            TxtIcon.IsVisible = false;
            NameReadPanel.IsVisible = false;
            TxtDescription.IsVisible = false;

            TxtIconEdit.IsVisible = true;
            NameEditPanel.IsVisible = true;
            TxtDescriptionEdit.IsVisible = true;

            TxtIconEdit.Text = preset.Icon;
            TxtNameEdit.Text = preset.Name;
            TxtDescriptionEdit.Text = string.IsNullOrEmpty(preset.LongDescription)
                ? preset.Description
                : preset.LongDescription;

            TxtIconEdit.LostFocus += (_, _) =>
            {
                preset.Icon = (TxtIconEdit.Text ?? "").Trim();
                PersistAndMaybeCreate();
            };
            TxtNameEdit.LostFocus += (_, _) =>
            {
                var name = (TxtNameEdit.Text ?? "").Trim();
                preset.Name = string.IsNullOrEmpty(name) ? Loc.Get("dialog_awareness_preset_detail_untitled_preset") : name;
                TxtNameEdit.Text = preset.Name;
                PersistAndMaybeCreate();
            };
            TxtDescriptionEdit.LostFocus += (_, _) =>
            {
                var text = (TxtDescriptionEdit.Text ?? "").Trim();
                preset.Description = text;
                preset.LongDescription = text;
                PersistAndMaybeCreate();
            };
        }

        if (preset.RequiresAi)
            BrdAiBadge.IsVisible = true;

        ApplyPolicyBannerState();
        RebuildRows();
        UpdateInstallButton();
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
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "https://app.cclabs.app/policies/prohibited-content") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger?.Warning(ex, "AwarenessPresetDetailDialog: failed to open policy URL");
        }
    }

    private void PersistAndMaybeCreate()
    {
        if (_isCustomPresetUnsaved)
        {
            var list = _settings?.Current?.KeywordTriggerPresets;
            if (list != null && !list.Any(p => p.Id == _preset.Id))
                list.Add(_preset);
            _isCustomPresetUnsaved = false;
            Changed = true;
        }
        _settings?.Save();
    }

    private bool IsEditable()
    {
        if (_keywordPresetService.IsInstalled(_preset.Id)) return true;
        if (!_preset.IsBuiltIn) return true;
        return false;
    }

    private void MirrorLiveClonesToCustomSource()
    {
        if (_preset.IsBuiltIn) return;
        if (!_keywordPresetService.IsInstalled(_preset.Id)) return;

        var settings = _settings?.Current;
        if (settings == null) return;

        var prefix = "preset:" + _preset.Id + ":";
        var live = settings.KeywordTriggers
            .Where(t => t?.Id?.StartsWith(prefix, StringComparison.Ordinal) == true)
            .ToList();

        var synced = new List<KeywordTrigger>();
        foreach (var clone in live)
        {
            var copy = clone.Clone();
            copy.Id = clone.Id!.Substring(prefix.Length);
            copy.LastTriggeredAt = DateTime.MinValue;
            synced.Add(copy);
        }
        _preset.Triggers = synced;
    }

    private void RebuildRows()
    {
        if (_preset is null) return;

        TriggerStack.Children.Clear();

        bool installed = _keywordPresetService.IsInstalled(_preset.Id);
        var editable = IsEditable();
        var settings = _settings?.Current;
        if (settings is null) return;
        List<KeywordTrigger> triggers;

        if (installed)
        {
            var prefix = "preset:" + _preset.Id + ":";
            triggers = settings.KeywordTriggers
                .Where(t => t?.Id?.StartsWith(prefix, StringComparison.Ordinal) == true)
                .ToList();
        }
        else
        {
            triggers = _preset.Triggers?.Where(t => t != null).ToList() ?? new List<KeywordTrigger>();
        }

        foreach (var trigger in triggers)
            TriggerStack.Children.Add(BuildTriggerBorder(trigger, editable));

        if (editable)
            TriggerStack.Children.Add(BuildAddTriggerRow());
        else if (triggers.Count == 0)
            TriggerStack.Children.Add(BuildEmptyStateNotice());

        BtnClone.IsVisible = _preset.IsBuiltIn;
        BtnDeletePreset.IsVisible = !_preset.IsBuiltIn;
        TxtFooterNote.IsVisible = editable;
    }

    private void UpdateInstallButton()
    {
        var installed = _keywordPresetService.IsInstalled(_preset.Id);
        if (installed)
        {
            BtnInstall.Content = Loc.Get("dialog_awareness_preset_detail_deactivate_content");
            BtnInstall.Background = new SolidColorBrush(Color.FromRgb(0x5A, 0x30, 0x30));
        }
        else
        {
            BtnInstall.Content = Loc.Get("dialog_awareness_preset_detail_activate_content");
            BtnInstall.Background = this.TryGetResource("PinkBrush", ThemeVariant.Default, out var res) && res is IBrush b
                ? b
                : new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
        }
    }

    private Control BuildAddTriggerRow()
    {
        var btn = new Button
        {
            Content = "＋ " + Loc.Get("dialog_awareness_preset_detail_add_trigger_content"),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 10, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x44)),
            Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!,
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Left,
            FontSize = 11,
        };
        ToolTip.SetTip(btn, Loc.Get("dialog_awareness_preset_detail_add_trigger_tooltip"));
        btn.Click += (_, _) => AddNewTrigger();
        return btn;
    }

    private static Control BuildEmptyStateNotice()
    {
        return new TextBlock
        {
            Text = Loc.Get("dialog_awareness_preset_detail_no_triggers_text"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0xA0)),
            FontStyle = FontStyle.Italic,
            FontSize = 11,
            Margin = new Thickness(4, 8, 0, 4),
        };
    }

    private void AddNewTrigger()
    {
        var settings = _settings?.Current;
        if (settings == null) return;

        var newTrigger = new KeywordTrigger
        {
            Keyword = "",
            MatchType = KeywordMatchType.PlainText,
            Enabled = true,
            CooldownSeconds = 30,
            AudioVolume = 80,
            VisualEffect = KeywordVisualEffect.SubliminalFlash,
            HapticEnabled = false,
            HapticIntensity = 0.3,
            DuckAudio = true,
            XPAward = 0,
        };
        newTrigger.Actions = new List<KeywordAction>();

        var installed = _keywordPresetService.IsInstalled(_preset.Id);
        if (installed)
        {
            var prefix = "preset:" + _preset.Id + ":";
            var sourceId = Guid.NewGuid().ToString("N")[..8];
            newTrigger.Id = prefix + sourceId;
            settings.KeywordTriggers.Add(newTrigger);

            if (!_preset.IsBuiltIn)
            {
                var sourceCopy = newTrigger.Clone();
                sourceCopy.Id = sourceId;
                _preset.Triggers ??= new List<KeywordTrigger>();
                _preset.Triggers.Add(sourceCopy);
            }
        }
        else
        {
            _preset.Triggers ??= new List<KeywordTrigger>();
            _preset.Triggers.Add(newTrigger);
        }

        PersistAndMaybeCreate();
        Changed = true;
        RebuildRows();
    }

    private void DeleteTrigger(KeywordTrigger trigger)
    {
        var settings = _settings?.Current;
        if (settings == null) return;

        var installed = _keywordPresetService.IsInstalled(_preset.Id);
        if (installed)
        {
            settings.KeywordTriggers.RemoveAll(t => t.Id == trigger.Id);

            if (!_preset.IsBuiltIn)
            {
                var prefix = "preset:" + _preset.Id + ":";
                if (trigger.Id?.StartsWith(prefix, StringComparison.Ordinal) == true)
                {
                    var sourceId = trigger.Id.Substring(prefix.Length);
                    _preset.Triggers?.RemoveAll(t => t.Id == sourceId);
                }
            }
        }
        else
        {
            _preset.Triggers?.RemoveAll(t => t.Id == trigger.Id);
        }

        PersistAndMaybeCreate();
        Changed = true;
        RebuildRows();
    }

    private Border BuildTriggerBorder(KeywordTrigger trigger, bool editable)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x3A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x48)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 4, 0, 4),
        };

        var stack = new StackPanel();
        border.Child = stack;

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var enableBox = new CheckBox
        {
            IsChecked = trigger.Enabled,
            IsEnabled = editable,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        ToolTip.SetTip(enableBox, Loc.Get("dialog_awareness_preset_detail_trigger_enabled_tooltip"));
        enableBox.IsCheckedChanged += (_, _) =>
        {
            trigger.Enabled = enableBox.IsChecked == true;
            if (editable) PersistAndMaybeCreate();
        };
        Grid.SetColumn(enableBox, 0);
        headerGrid.Children.Add(enableBox);

        if (editable)
        {
            var keywordBox = new TextBox
            {
                Text = trigger.Keyword,
                Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x32)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5A)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 3, 6, 3),
                MinWidth = 140,
            };
            ToolTip.SetTip(keywordBox, Loc.Get("dialog_awareness_preset_detail_keyword_tooltip"));
            keywordBox.LostFocus += (_, _) =>
            {
                var newKeyword = (keywordBox.Text ?? "").Trim();
                if (newKeyword == trigger.Keyword) return;
                trigger.Keyword = newKeyword;
                PersistAndMaybeCreate();
            };
            Grid.SetColumn(keywordBox, 1);
            headerGrid.Children.Add(keywordBox);

            var addBtn = new Button
            {
                Content = "＋ " + Loc.Get("dialog_awareness_preset_detail_add_action_content"),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(6, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x44)),
                Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!,
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                FontSize = 11,
            };
            addBtn.Click += (_, _) => ShowAddActionMenu(addBtn, trigger, border);
            Grid.SetColumn(addBtn, 2);
            headerGrid.Children.Add(addBtn);

            var deleteTriggerBtn = new Button
            {
                Content = "×",
                Width = 26,
                Height = 26,
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.FromRgb(0x40, 0x20, 0x20)),
                Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!,
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                FontSize = 14,
            };
            ToolTip.SetTip(deleteTriggerBtn, Loc.Get("dialog_awareness_preset_detail_delete_trigger_tooltip"));
            deleteTriggerBtn.Click += async (_, _) =>
            {
                var label = string.IsNullOrWhiteSpace(trigger.Keyword) ? Loc.Get("dialog_awareness_preset_detail_unnamed_trigger") : $"\"{trigger.Keyword}\"";
                var confirmed = await (_dialogService?.ShowConfirmationAsync(
                    Loc.Get("dialog_awareness_preset_detail_delete_trigger_title"),
                    string.Format(Loc.Get("dialog_awareness_preset_detail_delete_trigger_message_fmt"), label)) ?? Task.FromResult(false));
                if (confirmed)
                    DeleteTrigger(trigger);
            };
            Grid.SetColumn(deleteTriggerBtn, 3);
            headerGrid.Children.Add(deleteTriggerBtn);
        }
        else
        {
            var keywordText = new TextBlock
            {
                Text = trigger.Keyword,
                Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(keywordText, 1);
            headerGrid.Children.Add(keywordText);

            var chipText = new TextBlock
            {
                Text = BuildActionChips(trigger),
                Foreground = this.TryGetResource("TextMutedBrush", ThemeVariant.Default, out var muted) && muted is IBrush mb
                    ? mb
                    : new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0xA0)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(chipText, 2);
            headerGrid.Children.Add(chipText);
        }

        stack.Children.Add(headerGrid);

        if (trigger.Actions != null)
        {
            foreach (var action in trigger.Actions.ToList())
            {
                var row = BuildActionRow(trigger, action, editable, border);
                if (row != null) stack.Children.Add(row);
            }
        }

        return border;
    }

    private void RebuildTriggerBorder(KeywordTrigger trigger, Border triggerBorder, bool editable)
    {
        if (triggerBorder.Child is not StackPanel stack) return;
        while (stack.Children.Count > 1) stack.Children.RemoveAt(1);
        if (trigger.Actions != null)
        {
            foreach (var action in trigger.Actions.ToList())
            {
                var row = BuildActionRow(trigger, action, editable, triggerBorder);
                if (row != null) stack.Children.Add(row);
            }
        }
    }

    private Control? BuildActionRow(KeywordTrigger trigger, KeywordAction action, bool editable, Border parentBorder)
    {
        return action switch
        {
            PlayAudioAction pa => BuildPlayAudioRow(trigger, pa, editable, parentBorder),
            VisualEffectAction ve => BuildVisualEffectRow(trigger, ve, editable, parentBorder),
            HighlightAction hi => BuildSimpleRow("👁", Loc.Get("dialog_awareness_preset_detail_highlight_description"), trigger, hi, editable, parentBorder),
            HapticAction h => BuildHapticRow(trigger, h, editable, parentBorder),
            AvatarCommentAction ac => BuildAvatarCommentRow(trigger, ac, editable, parentBorder),
            ExtendSessionAction es => BuildExtendSessionRow(trigger, es, editable, parentBorder),
            ChasterAddTimeAction ct => BuildChasterAddTimeRow(trigger, ct, editable, parentBorder),
            _ => null,
        };
    }

    private Border ActionRowFrame(string icon, Control body, KeywordTrigger trigger, KeywordAction action, bool editable, Border parentBorder)
    {
        var rowBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x2E)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x48)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 5, 6, 5),
            Margin = new Thickness(24, 6, 0, 0),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconText = new TextBlock
        {
            Text = icon,
            FontSize = 14,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(iconText, 0);
        grid.Children.Add(iconText);

        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        if (editable)
        {
            var removeBtn = new Button
            {
                Content = "×",
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.FromRgb(0x40, 0x20, 0x20)),
                Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!,
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                FontSize = 13,
            };
            ToolTip.SetTip(removeBtn, Loc.Get("dialog_awareness_preset_detail_remove_action_tooltip"));
            removeBtn.Click += (_, _) =>
            {
                trigger.Actions?.Remove(action);
                _settings?.Save();
                RebuildTriggerBorder(trigger, parentBorder, editable);
            };
            Grid.SetColumn(removeBtn, 2);
            grid.Children.Add(removeBtn);
        }

        rowBorder.Child = grid;
        return rowBorder;
    }

    private Border BuildPlayAudioRow(KeywordTrigger trigger, PlayAudioAction pa, bool editable, Border parentBorder)
    {
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var fileText = new TextBlock
        {
            Text = DescribeAudioFile(pa.FilePath),
            Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB8)),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(fileText, 0);
        body.Children.Add(fileText);

        var browseBtn = MakeChipButton(Loc.Get("btn_browse"), editable);
        browseBtn.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { } provider) return;
            var result = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Loc.Get("dialog_awareness_preset_detail_select_sound_title"),
                FileTypeFilter = new[]
                {
                    new FilePickerFileType(Loc.Get("dialog_awareness_preset_detail_audio_files_filter")) { Patterns = new[] { "*.mp3", "*.wav", "*.ogg" } },
                    new FilePickerFileType(Loc.Get("dialog_awareness_preset_detail_all_files_filter")) { Patterns = new[] { "*.*" } }
                }
            });
            var file = result?.FirstOrDefault();
            if (file != null)
            {
                pa.FilePath = file.TryGetLocalPath() ?? file.Path.AbsoluteUri;
                fileText.Text = DescribeAudioFile(pa.FilePath);
                _settings?.Save();
            }
        };
        Grid.SetColumn(browseBtn, 1);
        body.Children.Add(browseBtn);

        var testBtn = MakeChipButton("▶ " + Loc.Get("btn_test"), editable);
        testBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(pa.FilePath))
                _keywordTriggerService.PreviewAudioClip(pa.FilePath, pa.Volume);
        };
        Grid.SetColumn(testBtn, 2);
        body.Children.Add(testBtn);

        var volLabel = new TextBlock
        {
            Text = Loc.Get("dialog_awareness_preset_detail_volume_label"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0xA0)),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 4, 0),
        };
        Grid.SetColumn(volLabel, 3);
        body.Children.Add(volLabel);

        var volSlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = pa.Volume,
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = editable,
            Margin = new Thickness(0, 0, 4, 0),
        };
        var volValueText = new TextBlock
        {
            Text = $"{pa.Volume}%",
            Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB8)),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 30,
        };
        volSlider.ValueChanged += (_, _) =>
        {
            pa.Volume = (int)Math.Round(volSlider.Value);
            volValueText.Text = $"{pa.Volume}%";
            _settings?.Save();
        };
        Grid.SetColumn(volSlider, 4);
        body.Children.Add(volSlider);
        Grid.SetColumn(volValueText, 5);
        body.Children.Add(volValueText);

        return ActionRowFrame("🔊", body, trigger, pa, editable, parentBorder);
    }

    private Border BuildVisualEffectRow(KeywordTrigger trigger, VisualEffectAction ve, bool editable, Border parentBorder)
    {
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock
        {
            Text = Loc.Get("dialog_awareness_preset_detail_effect_label"),
            Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB8)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        Grid.SetColumn(label, 0);
        body.Children.Add(label);

        var combo = new ComboBox
        {
            MinWidth = 160,
            IsEnabled = editable,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x32)),
            Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5C)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 3),
        };
        var effectValues = new[]
        {
            KeywordVisualEffect.SubliminalFlash,
            KeywordVisualEffect.ExactSubliminal,
            KeywordVisualEffect.ImageFlash,
            KeywordVisualEffect.OverlayPulse,
            KeywordVisualEffect.MindWipe,
            KeywordVisualEffect.Bubbles,
        };
        foreach (var v in effectValues)
            combo.Items.Add(new ComboBoxItem { Content = DescribeVisualEffect(v), Tag = v });
        combo.SelectedIndex = Math.Max(0, Array.IndexOf(effectValues, ve.Effect));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem item && item.Tag is KeywordVisualEffect eff)
            {
                ve.Effect = eff;
                _settings?.Save();
            }
        };
        Grid.SetColumn(combo, 1);
        body.Children.Add(combo);

        return ActionRowFrame(IconForVisualEffect(ve.Effect), body, trigger, ve, editable, parentBorder);
    }

    private Border BuildHapticRow(KeywordTrigger trigger, HapticAction h, bool editable, Border parentBorder)
    {
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = Loc.Get("setting_intensity"),
            Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB8)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        Grid.SetColumn(label, 0);
        body.Children.Add(label);

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            TickFrequency = 0.05,
            IsSnapToTickEnabled = true,
            Value = h.Intensity,
            IsEnabled = editable,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var valueText = new TextBlock
        {
            Text = $"{h.Intensity:F2}",
            Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB8)),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 30,
            Margin = new Thickness(6, 0, 0, 0),
        };
        slider.ValueChanged += (_, _) =>
        {
            h.Intensity = slider.Value;
            valueText.Text = $"{h.Intensity:F2}";
            _settings?.Save();
        };
        Grid.SetColumn(slider, 1);
        body.Children.Add(slider);
        Grid.SetColumn(valueText, 2);
        body.Children.Add(valueText);

        return ActionRowFrame("💥", body, trigger, h, editable, parentBorder);
    }

    private Border BuildAvatarCommentRow(KeywordTrigger trigger, AvatarCommentAction ac, bool editable, Border parentBorder)
    {
        var body = new StackPanel { Orientation = Orientation.Vertical };

        var exampleText = new TextBlock
        {
            Text = Loc.Get("dialog_awareness_preset_detail_avatar_example"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x9A)),
            FontSize = 10,
            FontStyle = FontStyle.Italic,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 3),
        };
        body.Children.Add(exampleText);

        var promptRow = new Grid();
        promptRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        promptRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var promptLabel = new TextBlock
        {
            Text = Loc.Get("dialog_awareness_preset_detail_prompt_label"),
            Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB8)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        Grid.SetColumn(promptLabel, 0);
        promptRow.Children.Add(promptLabel);

        var promptBox = new TextBox
        {
            Text = ac.PromptTemplate ?? "",
            FontSize = 11,
            IsEnabled = editable,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x32)),
            Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5A)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 2, 4, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(promptBox, Loc.Get("dialog_awareness_preset_detail_prompt_tooltip"));
        promptBox.LostFocus += (_, _) =>
        {
            ac.PromptTemplate = string.IsNullOrWhiteSpace(promptBox.Text) ? null : promptBox.Text;
            _settings?.Save();
            RunAwarenessPromptValidation(promptBox);
        };
        Grid.SetColumn(promptBox, 1);
        promptRow.Children.Add(promptBox);
        body.Children.Add(promptRow);

        var flagsRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        flagsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        flagsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        flagsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var fallbackLabel = new TextBlock
        {
            Text = Loc.Get("dialog_awareness_preset_detail_fallback_pool_label"),
            Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB8)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        Grid.SetColumn(fallbackLabel, 0);
        flagsRow.Children.Add(fallbackLabel);

        var fallbackCombo = new ComboBox
        {
            IsEnabled = editable,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 140,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x32)),
            Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5C)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 3),
        };
        ToolTip.SetTip(fallbackCombo, Loc.Get("dialog_awareness_preset_detail_fallback_pool_tooltip"));

        fallbackCombo.Items.Add(new ComboBoxItem { Content = Loc.Get("dialog_awareness_preset_detail_fallback_none"), Tag = null });

        var currentCat = ac.FallbackPhraseCategory ?? "";
        int selectedIndex = 0;
        int idx = 1;
        foreach (var cat in GetAvailableFallbackCategories())
        {
            fallbackCombo.Items.Add(new ComboBoxItem { Content = cat, Tag = cat });
            if (cat.Equals(currentCat, StringComparison.OrdinalIgnoreCase))
                selectedIndex = idx;
            idx++;
        }

        if (selectedIndex == 0 && !string.IsNullOrEmpty(currentCat))
        {
            fallbackCombo.Items.Add(new ComboBoxItem
            {
                Content = string.Format(Loc.Get("dialog_awareness_preset_detail_fallback_unavailable_fmt"), currentCat),
                Tag = currentCat,
            });
            selectedIndex = fallbackCombo.Items.Count - 1;
        }

        fallbackCombo.SelectedIndex = selectedIndex;
        fallbackCombo.SelectionChanged += (_, _) =>
        {
            if (fallbackCombo.SelectedItem is ComboBoxItem item)
            {
                ac.FallbackPhraseCategory = item.Tag as string;
                _settings?.Save();
            }
        };
        Grid.SetColumn(fallbackCombo, 1);
        flagsRow.Children.Add(fallbackCombo);

        var aiCheck = new CheckBox
        {
            Content = Loc.Get("dialog_awareness_preset_detail_require_ai_content"),
            Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!,
            FontSize = 11,
            IsChecked = ac.RequireAiAvailable,
            IsEnabled = editable,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };
        ToolTip.SetTip(aiCheck, Loc.Get("dialog_awareness_preset_detail_require_ai_tooltip"));
        aiCheck.IsCheckedChanged += (_, _) =>
        {
            ac.RequireAiAvailable = aiCheck.IsChecked == true;
            _settings?.Save();
        };
        Grid.SetColumn(aiCheck, 2);
        flagsRow.Children.Add(aiCheck);

        body.Children.Add(flagsRow);

        return ActionRowFrame("💬", body, trigger, ac, editable, parentBorder);
    }

    private IEnumerable<string> GetAvailableFallbackCategories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        try
        {
            foreach (var cat in _companionPhraseService.GetCategoryNames())
            {
                if (!string.IsNullOrEmpty(cat) && seen.Add(cat))
                    result.Add(cat);
            }
        }
        catch { }

        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private Border BuildExtendSessionRow(KeywordTrigger trigger, ExtendSessionAction es, bool editable, Border parentBorder)
    {
        return BuildMinutesRow("⏱", Loc.Get("dialog_awareness_preset_detail_extend_session_label"), es.Minutes, v => es.Minutes = v, trigger, es, editable, parentBorder);
    }

    private Border BuildChasterAddTimeRow(KeywordTrigger trigger, ChasterAddTimeAction ct, bool editable, Border parentBorder)
    {
        return BuildMinutesRow("🔒", Loc.Get("dialog_awareness_preset_detail_chaster_label"), ct.Minutes, v => ct.Minutes = v, trigger, ct, editable, parentBorder);
    }

    private Border BuildMinutesRow(string icon, string label, int current, Action<int> setter, KeywordTrigger trigger, KeywordAction action, bool editable, Border parentBorder)
    {
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var lbl = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB8)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        Grid.SetColumn(lbl, 0);
        body.Children.Add(lbl);

        var tb = new TextBox
        {
            Text = current.ToString(),
            FontSize = 11,
            IsEnabled = editable,
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x32)),
            Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5A)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 2, 4, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        tb.LostFocus += (_, _) =>
        {
            if (int.TryParse(tb.Text, out var v))
            {
                var clamped = Math.Clamp(v, 0, 1440);
                setter(clamped);
                tb.Text = clamped.ToString();
                _settings?.Save();
            }
            else
            {
                tb.Text = current.ToString();
            }
        };
        Grid.SetColumn(tb, 1);
        body.Children.Add(tb);

        var suffix = new TextBlock
        {
            Text = Loc.Get("dialog_awareness_preset_detail_minutes_suffix"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0xA0)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(suffix, 2);
        body.Children.Add(suffix);

        return ActionRowFrame(icon, body, trigger, action, editable, parentBorder);
    }

    private Border BuildSimpleRow(string icon, string description, KeywordTrigger trigger, KeywordAction action, bool editable, Border parentBorder)
    {
        var body = new TextBlock
        {
            Text = description,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB8)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        return ActionRowFrame(icon, body, trigger, action, editable, parentBorder);
    }

    private void ShowAddActionMenu(Button anchor, KeywordTrigger trigger, Border parentBorder)
    {
        var menu = new ContextMenu();
        var existing = new HashSet<string>();
        foreach (var a in trigger.Actions ?? new List<KeywordAction>())
        {
            if (a is VisualEffectAction ve) existing.Add("VisualEffect:" + ve.Effect);
            else existing.Add(a.GetType().Name);
        }

        AddMenuItem(menu, "🔊 " + Loc.Get("dialog_awareness_preset_detail_menu_play_audio"), () => AddAction(trigger, new PlayAudioAction { Volume = 70 }, parentBorder),
            existing.Contains(nameof(PlayAudioAction)) ? Loc.Get("dialog_awareness_preset_detail_already_added_suffix") : null);
        AddMenuItem(menu, "👁 " + Loc.Get("dialog_awareness_preset_detail_menu_highlight_matched_words"), () => AddAction(trigger, new HighlightAction(), parentBorder),
            existing.Contains(nameof(HighlightAction)) ? Loc.Get("dialog_awareness_preset_detail_already_added_suffix") : null);
        AddMenuItem(menu, "💥 " + Loc.Get("dialog_awareness_preset_detail_menu_haptic"), () => AddAction(trigger, new HapticAction { Intensity = 0.3 }, parentBorder),
            existing.Contains(nameof(HapticAction)) ? Loc.Get("dialog_awareness_preset_detail_already_added_suffix") : null);
        AddMenuItem(menu, "💬 " + Loc.Get("dialog_awareness_preset_detail_menu_avatar_comment"), () => AddAction(trigger, new AvatarCommentAction(), parentBorder),
            existing.Contains(nameof(AvatarCommentAction)) ? Loc.Get("dialog_awareness_preset_detail_already_added_suffix") : null);

        menu.Items.Add(new Separator());

        AddMenuItem(menu, "✨ " + Loc.Get("dialog_awareness_preset_detail_menu_subliminal_flash"), () => AddAction(trigger, new VisualEffectAction { Effect = KeywordVisualEffect.SubliminalFlash }, parentBorder),
            existing.Contains("VisualEffect:" + KeywordVisualEffect.SubliminalFlash) ? Loc.Get("dialog_awareness_preset_detail_already_added_suffix") : null);
        AddMenuItem(menu, "🔤 " + Loc.Get("dialog_awareness_preset_detail_menu_exact_subliminal"), () => AddAction(trigger, new VisualEffectAction { Effect = KeywordVisualEffect.ExactSubliminal }, parentBorder),
            existing.Contains("VisualEffect:" + KeywordVisualEffect.ExactSubliminal) ? Loc.Get("dialog_awareness_preset_detail_already_added_suffix") : null);
        AddMenuItem(menu, "⚡ " + Loc.Get("dialog_awareness_preset_detail_menu_image_flash"), () => AddAction(trigger, new VisualEffectAction { Effect = KeywordVisualEffect.ImageFlash }, parentBorder),
            existing.Contains("VisualEffect:" + KeywordVisualEffect.ImageFlash) ? Loc.Get("dialog_awareness_preset_detail_already_added_suffix") : null);
        AddMenuItem(menu, "🌫 " + Loc.Get("dialog_awareness_preset_detail_menu_overlay_pulse"), () => AddAction(trigger, new VisualEffectAction { Effect = KeywordVisualEffect.OverlayPulse }, parentBorder),
            existing.Contains("VisualEffect:" + KeywordVisualEffect.OverlayPulse) ? Loc.Get("dialog_awareness_preset_detail_already_added_suffix") : null);
        AddMenuItem(menu, "🧠 " + Loc.Get("dialog_awareness_preset_detail_menu_mind_wipe"), () => AddAction(trigger, new VisualEffectAction { Effect = KeywordVisualEffect.MindWipe }, parentBorder),
            existing.Contains("VisualEffect:" + KeywordVisualEffect.MindWipe) ? Loc.Get("dialog_awareness_preset_detail_already_added_suffix") : null);
        AddMenuItem(menu, "🫧 " + Loc.Get("dialog_awareness_preset_detail_menu_bubbles"), () => AddAction(trigger, new VisualEffectAction { Effect = KeywordVisualEffect.Bubbles }, parentBorder),
            existing.Contains("VisualEffect:" + KeywordVisualEffect.Bubbles) ? Loc.Get("dialog_awareness_preset_detail_already_added_suffix") : null);

        menu.Open(anchor);
    }

    private void AddMenuItem(ContextMenu menu, string header, Action onClick, string? disabledReason = null)
    {
        var item = new MenuItem { Header = header };
        if (disabledReason != null)
        {
            item.IsEnabled = false;
            item.Header = header + " " + disabledReason;
        }
        else
        {
            item.Click += (_, _) => onClick();
        }
        menu.Items.Add(item);
    }

    private void AddAction(KeywordTrigger trigger, KeywordAction newAction, Border parentBorder)
    {
        trigger.Actions ??= new List<KeywordAction>();
        trigger.Actions.Add(newAction);
        _settings?.Save();
        RebuildTriggerBorder(trigger, parentBorder, editable: true);
    }

    private void BtnInstall_Click(object? sender, RoutedEventArgs e)
    {
        if (_isCustomPresetUnsaved)
            PersistAndMaybeCreate();

        if (_keywordPresetService.IsInstalled(_preset.Id))
        {
            if (!_preset.IsBuiltIn)
                MirrorLiveClonesToCustomSource();
            _keywordPresetService.UninstallPreset(_preset.Id);
        }
        else
        {
            _keywordPresetService.InstallPreset(_preset.Id);
        }

        Changed = true;
        RebuildRows();
        UpdateInstallButton();
    }

    private async void BtnClone_Click(object? sender, RoutedEventArgs e)
    {
        var copy = _keywordPresetService.CloneToCustom(_preset.Id);
        Changed = true;
        if (copy == null)
        {
            if (_dialogService != null)
            {
                await _dialogService.ShowMessageAsync(
                    Loc.Get("dialog_awareness_preset_detail_copy_failed_title"),
                    Loc.Get("dialog_awareness_preset_detail_copy_failed_message"),
                    DialogSeverity.Warning);
            }
            return;
        }

        var owner = Owner as Window;
        Close();
        var dlg = new AwarenessPresetDetailDialog(copy) { Owner = owner };
        if (owner != null)
            await dlg.ShowDialog<bool?>(owner);
    }

    private async void BtnDeletePreset_Click(object? sender, RoutedEventArgs e)
    {
        if (_preset is null) return;
        if (_preset.IsBuiltIn) return;

        var label = string.IsNullOrWhiteSpace(_preset.Name) ? Loc.Get("dialog_awareness_preset_detail_this_preset") : $"\"{_preset.Name}\"";
        var confirmed = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("dialog_awareness_preset_detail_delete_preset_content"),
            string.Format(Loc.Get("dialog_awareness_preset_detail_delete_preset_message_fmt"), label)) ?? Task.FromResult(false));
        if (!confirmed) return;

        if (_keywordPresetService.IsInstalled(_preset.Id))
            _keywordPresetService.UninstallPreset(_preset.Id);

        var list = _settings?.Current?.KeywordTriggerPresets;
        if (list != null)
            list.RemoveAll(p => p.Id == _preset.Id);
        _settings?.Save();

        Changed = true;
        Close();
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RunAwarenessPromptValidation(TextBox promptBox)
    {
        var text = promptBox.Text ?? string.Empty;
        var result = _promptValidator.Validate(text);
        if (result.Clean)
        {
            promptBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5A));
            promptBox.BorderThickness = new Thickness(1);
            ToolTip.SetTip(promptBox, null);
            return;
        }

        promptBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xC7, 0x6B));
        promptBox.BorderThickness = new Thickness(2);
        ToolTip.SetTip(promptBox, string.Format(
            Loc.Get("prompt_validator_warning"),
            result.MatchedPatterns.Count));

        _moderationLog.RecordEdit(
            "avatarPromptTemplate",
            result.MatchedPatterns.Count,
            "awareness_preset");
        _logger?.Information(
            "PromptValidator flagged AwarenessPresetDetailDialog avatar prompt ({Count} matches)",
            result.MatchedPatterns.Count);
    }

    private static Button MakeChipButton(string label, bool editable)
    {
        return new Button
        {
            Content = label,
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 4, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x44)),
            Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!,
            BorderThickness = new Thickness(0),
            Cursor = new Cursor(StandardCursorType.Hand),
            FontSize = 11,
            IsEnabled = editable,
        };
    }

    private static string DescribeAudioFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return Loc.Get("dialog_awareness_preset_detail_no_file");
        try { return Path.GetFileName(path) ?? path; }
        catch { return path; }
    }

    private static string DescribeVisualEffect(KeywordVisualEffect e) => e switch
    {
        KeywordVisualEffect.SubliminalFlash => Loc.Get("dialog_awareness_preset_detail_effect_subliminal_flash"),
        KeywordVisualEffect.ExactSubliminal => Loc.Get("dialog_awareness_preset_detail_effect_exact_subliminal"),
        KeywordVisualEffect.ImageFlash => Loc.Get("dialog_awareness_preset_detail_effect_image_flash"),
        KeywordVisualEffect.OverlayPulse => Loc.Get("dialog_awareness_preset_detail_effect_overlay_pulse"),
        KeywordVisualEffect.MindWipe => Loc.Get("dialog_awareness_preset_detail_effect_mind_wipe"),
        KeywordVisualEffect.Bubbles => Loc.Get("dialog_awareness_preset_detail_effect_bubbles"),
        _ => e.ToString(),
    };

    private static string IconForVisualEffect(KeywordVisualEffect e) => e switch
    {
        KeywordVisualEffect.SubliminalFlash => "✨",
        KeywordVisualEffect.ExactSubliminal => "🔤",
        KeywordVisualEffect.ImageFlash => "⚡",
        KeywordVisualEffect.OverlayPulse => "🌫",
        KeywordVisualEffect.MindWipe => "🧠",
        KeywordVisualEffect.Bubbles => "🫧",
        _ => "✨",
    };

    internal static string BuildActionChips(KeywordTrigger trigger)
    {
        if (trigger.Actions == null || trigger.Actions.Count == 0) return "";
        var sb =
new StringBuilder();
        foreach (var a in trigger.Actions)
        {
            if (a == null) continue;
            switch (a)
            {
                case PlayAudioAction: sb.Append("🔊 "); break;
                case VisualEffectAction v: sb.Append(IconForVisualEffect(v.Effect)).Append(' '); break;
                case HighlightAction: sb.Append("👁 "); break;
                case HapticAction: sb.Append("💥 "); break;
                case AvatarCommentAction: sb.Append("💬 "); break;
                case ExtendSessionAction: sb.Append("⏱ "); break;
                case ChasterAddTimeAction: sb.Append("🔒 "); break;
            }
        }
        return sb.ToString().TrimEnd();
    }
}
