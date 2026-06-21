using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Services.Companion;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using CoreApp = global::ConditioningControlPanel.App;
using Microsoft.Extensions.DependencyInjection;
namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Dialog for managing companion speech-bubble phrases.
/// </summary>
public partial class CompanionPhraseEditorDialog : Window
{
    private readonly global::ConditioningControlPanel.IAppLogger _logger;
    private readonly global::ConditioningControlPanel.Core.Services.Settings.ISettingsService _settings;


    private const string BarkCategory = "Bark";

    private ObservableCollection<CompanionPhrase> _phrases = new();
    private ObservableCollection<CompanionPhrase> _filtered = new();
    private string _currentFilter = "All Categories";
    private string _searchTerm = "";
    private bool _bulkUpdating;
    private readonly ICompanionPhraseService _companionPhraseService;

    /// <summary>
    /// Optional dialog service for file pickers. When null, audio browsing is stubbed.
    /// </summary>
    public IDialogService? DialogService { get; set; }

    private static readonly Dictionary<string, string> _categoryDisplayNames = new()
    {
        { "Greeting", "Greeting" },
        { "StartupGreeting", "Startup Greeting" },
        { "Idle", "Idle" },
        { "RandomFloating", "Random Floating" },
        { "Generic", "Generic" },
        { "Gaming", "Gaming" },
        { "Browsing", "Browsing" },
        { "Shopping", "Shopping" },
        { "Social", "Social Media" },
        { "Discord", "Discord" },
        { "TrainingSite", "Training Site" },
        { "HypnoContent", "Hypno Content" },
        { "Working", "Working" },
        { "Media", "Media" },
        { "Learning", "Learning" },
        { "WindowAwarenessIdle", "Idle Detection" },
        { "EngineStop", "Engine Stop" },
        { "FlashPre", "Flash (Pre)" },
        { "SubliminalAck", "Subliminal Reaction" },
        { "RandomBubble", "Random Bubble" },
        { "BubbleCountMercy", "Bubble Count Mercy" },
        { "BubblePop", "Bubble Pop" },
        { "GameFailed", "Game Failed" },
        { "BubbleMissed", "Bubble Missed" },
        { "FlashClicked", "Flash Clicked" },
        { "LevelUp", "Level Up" },
        { "MindWipe", "Mind Wipe" },
        { "BrainDrain", "Brain Drain" },
        { "VoiceLine", "Voice Line" },
        { "Custom", "Custom (General)" },
        { BarkCategory, "🐰 Bark Lines" },
    };

    private static string GetDisplayName(string category) =>
        _categoryDisplayNames.TryGetValue(category, out var dn) ? dn : category;

    public CompanionPhraseEditorDialog()
    {
        InitializeComponent();

        _logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
        _settings = App.Services.GetRequiredService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>();
_companionPhraseService = App.Services.GetRequiredService<ICompanionPhraseService>();
        PopulateCategoryFilter();
        RefreshPhraseList();
    }

    public CompanionPhraseEditorDialog(IDialogService dialogService)
        : this()
    {
        DialogService = dialogService;
    }

    private void PopulateCategoryFilter()
    {
        CmbCategoryFilter.Items.Add("All Categories");
        foreach (var cat in _companionPhraseService.GetCategoryNames())
            CmbCategoryFilter.Items.Add(GetDisplayName(cat));
        CmbCategoryFilter.Items.Add(GetDisplayName("Custom"));
        CmbCategoryFilter.Items.Add(GetDisplayName(BarkCategory));
        CmbCategoryFilter.SelectedIndex = 0;
    }

    private void RefreshPhraseList()
    {
        var selected = new HashSet<string>(_phrases.Where(p => p.IsSelected).Select(p => p.Id));

        var all = GetAllPhrases();
        var fresh = new ObservableCollection<CompanionPhrase>();
        foreach (var p in all)
        {
            if (!p.IsBark) p.GroupLabel = GetDisplayName(p.Category);
            p.IsSelected = selected.Contains(p.Id);
            p.PropertyChanged -= Phrase_PropertyChanged;
            p.PropertyChanged += Phrase_PropertyChanged;
            fresh.Add(p);
        }

        _phrases = fresh;
        ApplyFilter();
        UpdateTotalCount();
    }

    private static IEnumerable<CompanionPhrase> GetAllPhrases()
    {
        var logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
        // TODO: App.CompanionPhrases service is not yet in CCP.Core; return placeholder data.
        if (CoreApp.CompanionPhrases != null)
        {
            try
            {
                dynamic svc = CoreApp.CompanionPhrases;
                var list = svc.GetAllPhrases() as IEnumerable<CompanionPhrase>;
                if (list != null) return list;
            }
            catch (Exception ex)
            {
                App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Warning(ex, "CompanionPhraseEditorDialog: failed to load phrases");
            }
        }

        return new List<CompanionPhrase>
        {
            new() { Id = "demo-1", Text = "Good girls obey.", Category = "Generic", IsBuiltIn = true },
            new() { Id = "demo-2", Text = "Bambi loves pink.", Category = "Generic", IsBuiltIn = true },
            new() { Id = "demo-3", Text = "Drop for cock.", Category = "VoiceLine", IsBuiltIn = true, AudioFileName = "drop.wav" }
        };
    }

    private void ApplyFilter()
    {
        _filtered.Clear();
        foreach (var p in _phrases)
        {
            if (!MatchesFilter(p)) continue;
            _filtered.Add(p);
        }
        PhraseList.ItemsSource = _filtered;
    }

    private bool MatchesFilter(CompanionPhrase p)
    {
        if (_currentFilter != "All Categories" && p.Category != _currentFilter)
            return false;

        if (!string.IsNullOrWhiteSpace(_searchTerm) &&
            (p.Text == null || p.Text.IndexOf(_searchTerm, StringComparison.OrdinalIgnoreCase) < 0))
            return false;

        return true;
    }

    private void CmbCategoryFilter_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var display = CmbCategoryFilter.SelectedItem as string ?? "All Categories";
        _currentFilter = display == "All Categories"
            ? "All Categories"
            : _categoryDisplayNames.FirstOrDefault(kvp => kvp.Value == display).Key ?? display;
        ApplyFilter();
    }

    private void TxtSearch_Changed(object? sender, TextChangedEventArgs e)
    {
        _searchTerm = TxtSearch.Text ?? "";
        if (TxtSearchPlaceholder != null)
            TxtSearchPlaceholder.IsVisible = string.IsNullOrEmpty(_searchTerm);
        ApplyFilter();
    }

    private static CompanionPhrase? PhraseOf(object? sender)
    {
        if (sender is Control { Tag: CompanionPhrase p }) return p;
        if (sender is Control { DataContext: CompanionPhrase dp }) return dp;
        return null;
    }

    private void Phrase_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not CompanionPhrase p) return;
        if (e.PropertyName == nameof(CompanionPhrase.IsEnabled))
            PersistEnabled(p);
    }

    private void PersistEnabled(CompanionPhrase p)
    {
        var settings = _settings?.Current;
        if (settings == null) return;

        if (p.IsBuiltIn)
        {
            if (p.IsEnabled) settings.DisabledPhraseIds.Remove(p.Id);
            else settings.DisabledPhraseIds.Add(p.Id);
        }
        else
        {
            var custom = settings.CustomCompanionPhrases.FirstOrDefault(c => c.Id == p.Id);
            if (custom != null) custom.Enabled = p.IsEnabled;
        }

        if (!_bulkUpdating)
        {
            _settings?.Save();
            UpdateTotalCount();
        }
    }

    private void UpdateTotalCount()
    {
        var active = _phrases.Count(p => p.IsEnabled);
        var total = _phrases.Count;
        TxtTotalCount.Text = $"{active}/{total} phrases active";
    }

    private void BtnRemovePhrase_Click(object? sender, RoutedEventArgs e)
    {
        if (PhraseOf(sender) is not CompanionPhrase phrase) return;
        var settings = _settings?.Current;
        if (settings == null) return;

        if (phrase.IsBuiltIn)
            settings.RemovedPhraseIds.Add(phrase.Id);
        else
            settings.CustomCompanionPhrases.RemoveAll(c => c.Id == phrase.Id);

        _settings?.Save();
        RefreshPhraseList();
    }

    private async void BtnBrowseAudio_Click(object? sender, RoutedEventArgs e)
    {
        if (PhraseOf(sender) is not CompanionPhrase p) return;
        await BrowseAndSetAudioAsync(p);
    }

    private async Task BrowseAndSetAudioAsync(CompanionPhrase phrase)
    {
        var settings = _settings?.Current;
        if (settings == null) return;

        string? fileName = null;
        if (DialogService != null)
        {
            var files = await DialogService.ShowOpenFileDialogAsync(
                "Select audio file for phrase",
                new[]
                {
                    new FileFilter("Audio Files", new[] { "mp3", "wav", "ogg", "flac" }),
                    new FileFilter("All Files", new[] { "*" })
                });
            fileName = files.FirstOrDefault();
        }
        else
        {
            MessageBoxStub.Show(
                "Audio browsing requires a registered IDialogService. Please wire up AvaloniaDialogService.",
                "Not Available",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrEmpty(fileName)) return;

        // TODO: App.CompanionPhrases.CopyAudioToFolder is not yet in CCP.Core.
        var copied = CopyAudioPlaceholder(fileName, phrase.Text);
        if (copied == null) return;

        if (phrase.IsBuiltIn)
            settings.PhraseAudioOverrides[phrase.Id] = copied;
        else
        {
            var custom = settings.CustomCompanionPhrases.FirstOrDefault(c => c.Id == phrase.Id);
            if (custom != null) custom.AudioFileName = copied;
        }

        _settings?.Save();
        RefreshPhraseList();
    }

    private static string? CopyAudioPlaceholder(string sourcePath, string phraseText)
    {
        var logger = App.Services.GetRequiredService<global::ConditioningControlPanel.IAppLogger>();
        // TODO: replace with App.CompanionPhrases.CopyAudioToFolder once ported.
        try
        {
            var folder = CompanionPhrase.DefaultAudioFolder;
            Directory.CreateDirectory(folder);
            var name = $"custom_{Guid.NewGuid():N}.mp3";
            var dest = Path.Combine(folder, name);
            File.Copy(sourcePath, dest, overwrite: true);
            return name;
        }
        catch (Exception ex)
        {
            App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Warning(ex, "CompanionPhraseEditorDialog: failed to copy audio placeholder");
            return null;
        }
    }

    private void BtnClearAudio_Click(object? sender, RoutedEventArgs e)
    {
        if (PhraseOf(sender) is not CompanionPhrase phrase) return;
        var settings = _settings?.Current;
        if (settings == null) return;

        if (phrase.IsBuiltIn)
            settings.PhraseAudioOverrides.Remove(phrase.Id);
        else
        {
            var custom = settings.CustomCompanionPhrases.FirstOrDefault(c => c.Id == phrase.Id);
            if (custom != null) custom.AudioFileName = null;
        }

        _settings?.Save();
        RefreshPhraseList();
    }

    private void BtnSelectAll_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var p in _filtered) p.IsSelected = true;
    }

    private void BtnDeselectAll_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var p in _phrases) p.IsSelected = false;
    }

    private void BtnEnableSelected_Click(object? sender, RoutedEventArgs e) => SetSelectedEnabled(true);
    private void BtnDisableSelected_Click(object? sender, RoutedEventArgs e) => SetSelectedEnabled(false);

    private void SetSelectedEnabled(bool enabled)
    {
        var selected = _phrases.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0) return;

        _bulkUpdating = true;
        foreach (var p in selected) p.IsEnabled = enabled;
        _bulkUpdating = false;

        _settings?.Save();
        UpdateTotalCount();
    }

    private void BtnRemoveSelected_Click(object? sender, RoutedEventArgs e)
    {
        var selected = _phrases.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBoxStub.Show("No phrases selected.", "Remove Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBoxStub.Show(
            $"Remove {selected.Count} selected phrase(s)?\n\nBuilt-in phrases and bark lines will be hidden (can be restored by clearing settings).\nCustom phrases will be permanently deleted.",
            "Remove Selected",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        var settings = _settings?.Current;
        if (settings == null) return;

        foreach (var phrase in selected)
        {
            if (phrase.IsBuiltIn)
                settings.RemovedPhraseIds.Add(phrase.Id);
            else
                settings.CustomCompanionPhrases.RemoveAll(c => c.Id == phrase.Id);
        }

        _settings?.Save();
        RefreshPhraseList();
    }

    private async void BtnAddPhrase_Click(object? sender, RoutedEventArgs e)
    {
        var settings = _settings?.Current;
        if (settings == null) return;

        // Build a small inline add-phrase dialog.
        var inputWindow = new Window
        {
            Title = "Add Custom Phrase",
            Width = 480,
            Height = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#1A1A2E")),
            CanResize = false,
        };

        var stack = new StackPanel { Margin = new Thickness(15), Spacing = 10 };
        stack.Children.Add(new TextBlock
        {
            Text = "Enter phrase text:",
            Foreground = Brushes.White,
            FontSize = 13
        });

        var inputBox = new TextBox
        {
            Background = new SolidColorBrush(Color.Parse("#252542")),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.Parse("#505070")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6),
            FontSize = 13
        };
        stack.Children.Add(inputBox);

        stack.Children.Add(new TextBlock
        {
            Text = "Category:",
            Foreground = Brushes.White,
            FontSize = 13
        });

        var categoryCombo = new ComboBox
        {
            Background = new SolidColorBrush(Color.Parse("#252542")),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.Parse("#505070")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 5),
            FontSize = 13
        };
        foreach (var cat in _companionPhraseService.GetCategoryNames())
            categoryCombo.Items.Add(new ComboBoxItem { Content = GetDisplayName(cat), Tag = cat });
        categoryCombo.SelectedIndex = 0;
        stack.Children.Add(categoryCombo);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Theme = this.FindResource("SecondaryButton") as ControlTheme,
        };
        cancelBtn.Click += (_, _) => inputWindow.Close(false);
        var okBtn = new Button
        {
            Content = "Add",
            Theme = this.FindResource("ActionButton") as ControlTheme,
        };
        okBtn.Click += (_, _) => inputWindow.Close(true);
        btnPanel.Children.Add(cancelBtn);
        btnPanel.Children.Add(okBtn);
        stack.Children.Add(btnPanel);

        inputWindow.Content = stack;
        inputBox.Focus();

        if (await inputWindow.ShowDialog<bool?>(this) != true) return;

        var text = inputBox.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        var selectedCategory = (categoryCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Custom";

        var newPhrase = new CustomCompanionPhrase
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Text = text,
            Category = selectedCategory,
            Enabled = true
        };

        var addAudio = MessageBoxStub.Show(
            "Would you like to connect an audio file to this phrase?",
            "Audio File",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (addAudio == MessageBoxResult.Yes && DialogService != null)
        {
            var files = await DialogService.ShowOpenFileDialogAsync(
                "Select audio file for phrase",
                new[]
                {
                    new FileFilter("Audio Files", new[] { "mp3", "wav", "ogg", "flac" }),
                    new FileFilter("All Files", new[] { "*" })
                });
            var audioFile = files.FirstOrDefault();
            if (audioFile != null)
            {
                var copied =
CopyAudioPlaceholder(audioFile, text);
                if (copied != null) newPhrase.AudioFileName = copied;
            }
        }

        settings.CustomCompanionPhrases.Add(newPhrase);
        _settings?.Save();
        RefreshPhraseList();
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }
}
