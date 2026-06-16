using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Models;
using Microsoft.Win32;

namespace ConditioningControlPanel
{
    public partial class CompanionPhraseEditorDialog : Window
    {
        // Filter sentinel that matches every bark line (Category == "Bark"), so the dropdown needs
        // one "Bark Lines" entry instead of ~80 per-trigger entries; per-rule headers come from grouping.
        private const string BarkCategory = "Bark";

        private ObservableCollection<CompanionPhrase> _phrases = new();
        private readonly CollectionViewSource _view;
        private string _currentFilter = "All Categories";
        private string _searchTerm = "";
        // Set while a bulk op mutates many rows so per-row persistence defers to a single Save() at the end.
        private bool _bulkUpdating;

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
            _view = (CollectionViewSource)Resources["PhrasesView"];
            _view.Filter += PhrasesView_Filter;
            PopulateCategoryFilter();
            RefreshPhraseList();
        }

        private void PopulateCategoryFilter()
        {
            CmbCategoryFilter.Items.Add(new ComboBoxItem { Content = "All Categories", Tag = "All Categories" });
            foreach (var cat in Services.CompanionPhraseService.GetCategoryNames())
                CmbCategoryFilter.Items.Add(new ComboBoxItem { Content = GetDisplayName(cat), Tag = cat });
            CmbCategoryFilter.Items.Add(new ComboBoxItem { Content = GetDisplayName("Custom"), Tag = "Custom" });
            // One entry for the ~1,200 bark lines; the list still groups them per rule via GroupLabel.
            CmbCategoryFilter.Items.Add(new ComboBoxItem { Content = GetDisplayName(BarkCategory), Tag = BarkCategory });
            CmbCategoryFilter.SelectedIndex = 0;
        }

        /// <summary>
        /// Rebuilds the full phrase set (built-in + voice lines + custom + bark lines) and rebinds the
        /// grouped, virtualized view. Only called when the row SET changes (add/remove); plain
        /// enable/disable/select toggles mutate the bound rows in place and skip this.
        /// </summary>
        private void RefreshPhraseList()
        {
            // Preserve selection across the rebuild (rows are fresh CompanionPhrase instances).
            var selected = new HashSet<string>(_phrases.Where(p => p.IsSelected).Select(p => p.Id));

            var all = App.CompanionPhrases?.GetAllPhrases() ?? new List<CompanionPhrase>();
            var fresh = new ObservableCollection<CompanionPhrase>();
            foreach (var p in all)
            {
                // Barks arrive with their own "Bark · {trigger}" GroupLabel; give everything else one.
                if (!p.IsBark) p.GroupLabel = GetDisplayName(p.Category);
                p.IsSelected = selected.Contains(p.Id);
                p.PropertyChanged += Phrase_PropertyChanged;
                fresh.Add(p);
            }

            _phrases = fresh;
            _view.Source = _phrases; // single reset → one regroup, not 1,500 collection-changed events
            UpdateTotalCount();
        }

        // ============================================================
        // Filtering (category dropdown + search box)
        // ============================================================

        private void PhrasesView_Filter(object sender, FilterEventArgs e)
        {
            if (e.Item is not CompanionPhrase p) { e.Accepted = false; return; }

            if (_currentFilter != "All Categories" && p.Category != _currentFilter)
            {
                e.Accepted = false;
                return;
            }

            if (!string.IsNullOrWhiteSpace(_searchTerm) &&
                (p.Text == null || p.Text.IndexOf(_searchTerm, StringComparison.OrdinalIgnoreCase) < 0))
            {
                e.Accepted = false;
                return;
            }

            e.Accepted = true;
        }

        private void CmbCategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbCategoryFilter.SelectedItem is ComboBoxItem item && item.Tag is string filter)
            {
                _currentFilter = filter;
                _view.View?.Refresh();
            }
        }

        private void TxtSearch_Changed(object sender, TextChangedEventArgs e)
        {
            _searchTerm = TxtSearch.Text ?? "";
            if (TxtSearchPlaceholder != null)
                TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(_searchTerm)
                    ? Visibility.Visible : Visibility.Collapsed;
            _view.View?.Refresh();
        }

        // ============================================================
        // Per-row handlers (resolve the row's phrase via DataContext)
        // ============================================================

        private static CompanionPhrase? PhraseOf(object sender) =>
            (sender as FrameworkElement)?.DataContext as CompanionPhrase;

        private void RowBorder_Click(object sender, MouseButtonEventArgs e)
        {
            if (PhraseOf(sender) is not CompanionPhrase p) return;
            // Let the interactive controls handle their own clicks. e.OriginalSource is an INNER visual of
            // the control (e.g. the checkbox's bullet), so walk up to find a ButtonBase/TextBox ancestor —
            // otherwise a click on the select/enable box would also flip selection and cancel itself out.
            if (HitsInteractive(e.OriginalSource as DependencyObject, sender as DependencyObject)) return;
            p.IsSelected = !p.IsSelected;
        }

        private static bool HitsInteractive(DependencyObject? source, DependencyObject? stopAt)
        {
            for (var d = source; d != null && d != stopAt; d = VisualTreeHelper.GetParent(d))
                if (d is ButtonBase || d is TextBox) return true;
            return false;
        }

        /// <summary>Fires when a bound row's IsEnabled flips (user toggled the On/Off box) — persists it.</summary>
        private void Phrase_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not CompanionPhrase p) return;
            if (e.PropertyName == nameof(CompanionPhrase.IsEnabled))
                PersistEnabled(p);
        }

        /// <summary>
        /// Persist a row's enabled state. Built-in phrases, voice lines and bark lines all share
        /// <see cref="AppSettings.DisabledPhraseIds"/> (the "Bark:" id prefix keeps them distinct);
        /// custom phrases store it on their own model.
        /// </summary>
        private void PersistEnabled(CompanionPhrase p)
        {
            var settings = App.Settings?.Current;
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
                App.Settings?.Save();
                UpdateTotalCount();
            }
        }

        private void UpdateTotalCount()
        {
            var active = _phrases.Count(p => p.IsEnabled);
            var total = _phrases.Count;
            TxtTotalCount.Text = $"{active}/{total} phrases active";
        }

        private void BtnRemovePhrase_Click(object sender, RoutedEventArgs e)
        {
            if (PhraseOf(sender) is not CompanionPhrase phrase) return;
            var settings = App.Settings?.Current;
            if (settings == null) return;

            if (phrase.IsBuiltIn)
                settings.RemovedPhraseIds.Add(phrase.Id);   // bark + built-in: hide (also silences barks)
            else
                settings.CustomCompanionPhrases.RemoveAll(c => c.Id == phrase.Id);

            App.Settings?.Save();
            RefreshPhraseList();
        }

        private void BtnBrowseAudio_Click(object sender, RoutedEventArgs e)
        {
            if (PhraseOf(sender) is CompanionPhrase p) BrowseAndSetAudio(p);
        }

        private void BtnClearAudio_Click(object sender, RoutedEventArgs e)
        {
            if (PhraseOf(sender) is not CompanionPhrase phrase) return;
            var settings = App.Settings?.Current;
            if (settings == null) return;

            if (phrase.IsBuiltIn)
                settings.PhraseAudioOverrides.Remove(phrase.Id);
            else
            {
                var custom = settings.CustomCompanionPhrases.FirstOrDefault(c => c.Id == phrase.Id);
                if (custom != null) custom.AudioFileName = null;
            }

            App.Settings?.Save();
            RefreshPhraseList();
        }

        private void BrowseAndSetAudio(CompanionPhrase phrase)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var dialog = new OpenFileDialog
            {
                Filter = "Audio Files|*.mp3;*.wav;*.ogg;*.flac|All Files|*.*",
                Title = "Select audio file for phrase"
            };

            if (dialog.ShowDialog() != true) return;

            var fileName = App.CompanionPhrases?.CopyAudioToFolder(dialog.FileName, phrase.Text);
            if (fileName == null) return;

            if (phrase.IsBuiltIn)
                settings.PhraseAudioOverrides[phrase.Id] = fileName;
            else
            {
                var custom = settings.CustomCompanionPhrases.FirstOrDefault(c => c.Id == phrase.Id);
                if (custom != null) custom.AudioFileName = fileName;
            }

            App.Settings?.Save();
            RefreshPhraseList();
        }

        private void TxtCustomPhrase_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox txt || PhraseOf(sender) is not CompanionPhrase p) return;
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var custom = settings.CustomCompanionPhrases.FirstOrDefault(c => c.Id == p.Id);
            if (custom != null && custom.Text != txt.Text)
            {
                custom.Text = txt.Text;
                App.Settings?.Save();
            }
        }

        // ============================================================
        // Toolbar / bulk actions
        // ============================================================

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            // Only the rows currently visible through the filter, so "select all" respects search/category.
            foreach (var p in VisiblePhrases()) p.IsSelected = true;
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var p in _phrases) p.IsSelected = false;
        }

        private void BtnEnableSelected_Click(object sender, RoutedEventArgs e) => SetSelectedEnabled(true);

        private void BtnDisableSelected_Click(object sender, RoutedEventArgs e) => SetSelectedEnabled(false);

        private void SetSelectedEnabled(bool enabled)
        {
            var selected = _phrases.Where(p => p.IsSelected).ToList();
            if (selected.Count == 0) return;

            _bulkUpdating = true;
            foreach (var p in selected) p.IsEnabled = enabled; // PropertyChanged → PersistEnabled (Save deferred)
            _bulkUpdating = false;

            App.Settings?.Save();
            UpdateTotalCount();
        }

        private void BtnRemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = _phrases.Where(p => p.IsSelected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "No phrases selected.", "Remove Selected", MessageBoxButton.OK);
                return;
            }

            var result = MessageBox.Show(this,
                $"Remove {selected.Count} selected phrase(s)?\n\nBuilt-in phrases and bark lines will be hidden (can be restored by clearing settings).\nCustom phrases will be permanently deleted.",
                "Remove Selected",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            var settings = App.Settings?.Current;
            if (settings == null) return;

            foreach (var phrase in selected)
            {
                if (phrase.IsBuiltIn)
                    settings.RemovedPhraseIds.Add(phrase.Id);
                else
                    settings.CustomCompanionPhrases.RemoveAll(c => c.Id == phrase.Id);
            }

            App.Settings?.Save();
            RefreshPhraseList();
        }

        private IEnumerable<CompanionPhrase> VisiblePhrases()
        {
            var view = _view.View;
            return view == null ? _phrases : view.Cast<CompanionPhrase>();
        }

        private void BtnAddPhrase_Click(object sender, RoutedEventArgs e)
        {
            var inputWindow = new Window
            {
                Title = "Add Custom Phrase",
                Width = 450,
                Height = 240,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)),
                ResizeMode = ResizeMode.NoResize
            };

            var stack = new StackPanel { Margin = new Thickness(15) };
            stack.Children.Add(new TextBlock
            {
                Text = "Enter phrase text:",
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var inputBox = new TextBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x42)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x70)),
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 13
            };
            stack.Children.Add(inputBox);

            stack.Children.Add(new TextBlock
            {
                Text = "Category:",
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 10, 0, 6)
            });

            var categoryCombo = new ComboBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x42)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x70)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 5, 8, 5),
                FontSize = 13
            };
            if (TryFindResource("DarkComboBox") is Style darkStyle)
                categoryCombo.Style = darkStyle;

            foreach (var cat in Services.CompanionPhraseService.GetCategoryNames())
                categoryCombo.Items.Add(new ComboBoxItem { Content = GetDisplayName(cat), Tag = cat });

            // Custom phrases can't be authored into the bark system, so pre-select the current filter
            // only when it's a real authorable category; otherwise default to VoiceLine.
            var preselect = (_currentFilter != "All Categories" && _currentFilter != BarkCategory)
                ? _currentFilter : "VoiceLine";
            for (int i = 0; i < categoryCombo.Items.Count; i++)
            {
                if (categoryCombo.Items[i] is ComboBoxItem ci && ci.Tag is string tag && tag == preselect)
                {
                    categoryCombo.SelectedIndex = i;
                    break;
                }
            }
            if (categoryCombo.SelectedIndex < 0) categoryCombo.SelectedIndex = categoryCombo.Items.Count - 1;

            stack.Children.Add(categoryCombo);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Background = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x50)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(16, 6, 16, 6),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancelBtn.Click += (s, ev) => inputWindow.DialogResult = false;

            var okBtn = new Button
            {
                Content = "Add",
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(16, 6, 16, 6),
                Cursor = Cursors.Hand
            };
            okBtn.Click += (s, ev) => inputWindow.DialogResult = true;

            btnPanel.Children.Add(cancelBtn);
            btnPanel.Children.Add(okBtn);
            stack.Children.Add(btnPanel);
            inputWindow.Content = stack;

            inputBox.Focus();

            if (inputWindow.ShowDialog() != true) return;

            var text = inputBox.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return;

            var settings = App.Settings?.Current;
            if (settings == null) return;

            var selectedCategory = (categoryCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Custom";

            var newPhrase = new CustomCompanionPhrase
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Text = text,
                Category = selectedCategory,
                Enabled = true
            };

            var result = MessageBox.Show(this,
                "Would you like to connect an audio file to this phrase?",
                "Audio File",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "Audio Files|*.mp3;*.wav;*.ogg;*.flac|All Files|*.*",
                    Title = "Select audio file for phrase"
                };

                if (dialog.ShowDialog() == true)
                {
                    var fileName = App.CompanionPhrases?.CopyAudioToFolder(dialog.FileName, text);
                    if (fileName != null) newPhrase.AudioFileName = fileName;
                }
            }

            settings.CustomCompanionPhrases.Add(newPhrase);
            App.Settings?.Save();
            RefreshPhraseList();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
