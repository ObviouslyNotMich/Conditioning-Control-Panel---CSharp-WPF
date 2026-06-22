using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
using CoreApp = ConditioningControlPanel.CoreApp;

namespace ConditioningControlPanel.Avalonia.Windows;

/// <summary>
/// Popup for editing feature settings on timeline events.
/// Avalonia port of the WPF FeatureSettingsPopup.
/// </summary>
public partial class FeatureSettingsPopup : UserControl
{
    public event EventHandler<TimelineEvent>? SettingsChanged;
    public event EventHandler<TimelineEvent>? DeleteRequested;
    public event EventHandler? CloseRequested;

    private TimelineEvent? _event;
    private FeatureDefinition? _feature;
    private int _maxMinute = 120;
    private TimelineSession? _parentSession;
    private readonly Dictionary<string, Control> _settingControls = new();

    public FeatureSettingsPopup()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Load an event for editing.
    /// </summary>
    public void LoadEvent(TimelineEvent evt, int durationMinutes, TimelineSession session)
    {
        _event = evt;
        _maxMinute = durationMinutes;
        _parentSession = session;
        _feature = FeatureDefinition.GetById(evt.FeatureId);

        if (_feature == null) return;

        TxtIcon.Text = _feature.Icon;
        TxtFeatureName.Text = _feature.Name;
        TxtEventType.Text = evt.EventType == TimelineEventType.Start
            ? LocalizationManager.Instance["feature_settings_start_event"]
            : LocalizationManager.Instance["feature_settings_stop_event"];

        SliderMinute.Maximum = durationMinutes;
        SliderMinute.Value = evt.Minute;
        TxtMinuteValue.Text = evt.Minute.ToString();

        GenerateSettingsControls();
    }

    private void GenerateSettingsControls()
    {
        SettingsPanel.Children.Clear();
        _settingControls.Clear();

        if (_event == null || _feature == null) return;

        // Stop events have no configurable settings.
        if (_event.EventType != TimelineEventType.Start)
        {
            var noSettingsText = new TextBlock
            {
                Text = LocalizationManager.Instance["feature_settings_stop_no_settings"],
                Foreground = FindBrush("TextMutedBrush", new Color(255, 136, 136, 136)),
                FontSize = 12,
                FontStyle = FontStyle.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 10)
            };
            SettingsPanel.Children.Add(noSettingsText);
            return;
        }

        if (_feature.SupportsRamping)
        {
            AddRampingControls();
        }

        foreach (var setting in _feature.Settings)
        {
            AddSettingControl(setting);
        }

        if (_event.FeatureId == "subliminal" && _parentSession != null)
        {
            AddPhraseManagement(
                LocalizationManager.Instance["feature_settings_phrase_subliminal_title"],
                _parentSession.SubliminalPhrases,
                true);
        }
        else if (_event.FeatureId == "bouncing_text" && _parentSession != null)
        {
            AddPhraseManagement(
                LocalizationManager.Instance["feature_settings_phrase_bouncing_title"],
                _parentSession.BouncingTextPhrases,
                false);
        }
    }

    private void AddRampingControls()
    {
        if (_event == null || _feature == null) return;

        var rampSetting = _feature.Settings.Find(s => s.SupportsRamp);
        if (rampSetting == null) return;

        var accentBrush = GetAccentBrush();
        var rampName = GetSettingLabel(rampSetting);

        var header = new TextBlock
        {
            Text = string.Format(LocalizationManager.Instance["feature_settings_ramping_header_fmt"], rampName),
            Foreground = accentBrush,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 5, 0, 8)
        };
        SettingsPanel.Children.Add(header);

        var startValue = _event.StartValue ?? (int)Convert.ToDouble(rampSetting.Default ?? rampSetting.Min);
        AddSlider(
            string.Format(LocalizationManager.Instance["feature_settings_start_value_fmt"], rampName),
            "ramp_start",
            (int)rampSetting.Min,
            (int)rampSetting.Max,
            startValue);

        var endValue = _event.EndValue ?? startValue;
        AddSlider(
            string.Format(LocalizationManager.Instance["feature_settings_end_value_fmt"], rampName),
            "ramp_end",
            (int)rampSetting.Min,
            (int)rampSetting.Max,
            endValue);

        var separator = new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(60, 60, 80)),
            Margin = new Thickness(0, 10, 0, 10)
        };
        SettingsPanel.Children.Add(separator);
    }

    private void AddSettingControl(FeatureSettingDefinition setting)
    {
        if (_event == null) return;

        // Skip ramp-supporting settings if already handled above.
        if (setting.SupportsRamp && _feature?.SupportsRamping == true) return;

        var label = GetSettingLabel(setting);

        switch (setting.Type)
        {
            case SettingType.Slider:
                var intValue = _event.GetSetting<int>(setting.Key, (int)Convert.ToDouble(setting.Default ?? setting.Min));
                AddSlider(label, setting.Key, (int)setting.Min, (int)setting.Max, intValue);
                break;

            case SettingType.Toggle:
                var boolValue = _event.GetSetting<bool>(setting.Key, (bool)(setting.Default ?? false));
                AddToggle(label, setting.Key, boolValue);
                break;

            case SettingType.Dropdown:
                var stringValue = _event.GetSetting<string>(setting.Key, setting.Default?.ToString() ?? "");
                AddDropdown(label, setting.Key, setting.Options ?? Array.Empty<string>(), stringValue);
                break;

            case SettingType.FilePicker:
                var pathValue = _event.GetSetting<string>(setting.Key, setting.Default?.ToString() ?? "");
                AddFilePicker(label, setting.Key, pathValue);
                break;
        }
    }

    private void AddSlider(string name, string key, int min, int max, int value)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var label = new TextBlock
        {
            Text = name,
            Foreground = FindBrush("TextMutedBrush", new Color(255, 176, 176, 176)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = key
        };
        Grid.SetColumn(slider, 1);
        grid.Children.Add(slider);

        var valueText = new TextBlock
        {
            Text = value.ToString(),
            Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10, 0, 0, 0)
        };
        Grid.SetColumn(valueText, 2);
        grid.Children.Add(valueText);

        slider.ValueChanged += (s, e) =>
        {
            var newValue = (int)e.NewValue;
            valueText.Text = newValue.ToString();
            SaveSetting(key, newValue);
        };

        _settingControls[key] = slider;
        SettingsPanel.Children.Add(grid);
    }

    private void AddToggle(string name, string key, bool value)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var checkBox = new CheckBox
        {
            IsChecked = value,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = key
        };
        checkBox.IsCheckedChanged += (s, e) => SaveSetting(key, checkBox.IsChecked == true);

        var label = new TextBlock
        {
            Text = name,
            Foreground = FindBrush("TextMutedBrush", new Color(255, 176, 176, 176)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };

        panel.Children.Add(checkBox);
        panel.Children.Add(label);

        _settingControls[key] = checkBox;
        SettingsPanel.Children.Add(panel);
    }

    private void AddDropdown(string name, string key, string[] options, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        var label = new TextBlock
        {
            Text = name,
            Foreground = FindBrush("TextMutedBrush", new Color(255, 176, 176, 176)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var comboBox = new ComboBox
        {
            Tag = key,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var option in options)
        {
            comboBox.Items.Add(option);
        }
        comboBox.SelectedItem = value;
        comboBox.SelectionChanged += (s, e) =>
        {
            if (comboBox.SelectedItem != null)
                SaveSetting(key, comboBox.SelectedItem.ToString() ?? "");
        };

        Grid.SetColumn(comboBox, 1);
        grid.Children.Add(comboBox);

        _settingControls[key] = comboBox;
        SettingsPanel.Children.Add(grid);
    }

    private void AddFilePicker(string name, string key, string value)
    {
        var stackPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

        var label = new TextBlock
        {
            Text = name,
            Foreground = FindBrush("TextMutedBrush", new Color(255, 176, 176, 176)),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 5)
        };
        stackPanel.Children.Add(label);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var textBox = new TextBox
        {
            Text = value,
            Background = FindBrush("PanelBgBrush", new Color(255, 37, 37, 66)),
            Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!,
            BorderBrush = new SolidColorBrush(Color.FromRgb(53, 53, 85)),
            Padding = new Thickness(8, 5, 8, 5),
            FontSize = 11,
            IsReadOnly = true,
            Tag = key
        };
        Grid.SetColumn(textBox, 0);
        grid.Children.Add(textBox);

        var browseButton = new Button
        {
            Content = LocalizationManager.Instance["feature_settings_browse"],
            Width = 34,
            Margin = new Thickness(5, 0, 0, 0)
        };
        browseButton.Click += async (s, e) => await BrowseForFileAsync(textBox, key, name);
        Grid.SetColumn(browseButton, 1);
        grid.Children.Add(browseButton);

        stackPanel.Children.Add(grid);

        _settingControls[key] = textBox;
        SettingsPanel.Children.Add(stackPanel);
    }

    private async Task BrowseForFileAsync(TextBox textBox, string key, string name)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var options = new FilePickerOpenOptions
        {
            Title = string.Format(LocalizationManager.Instance["feature_settings_select_file_fmt"], name),
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new FilePickerFileType("Image/GIF Files")
                {
                    Patterns = new[] { "*.gif", "*.png", "*.jpg", "*.jpeg" }
                },
                new FilePickerFileType("All Files")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);
        if (files.Count > 0)
        {
            textBox.Text = files[0].Path.LocalPath;
            SaveSetting(key, files[0].Path.LocalPath);
        }
    }

    private void SaveSetting(string key, object value)
    {
        if (_event == null) return;

        if (key == "ramp_start")
        {
            _event.StartValue = (int)value;
        }
        else if (key == "ramp_end")
        {
            _event.EndValue = (int)value;
        }
        else
        {
            _event.SetSetting(key, value);
        }

        SettingsChanged?.Invoke(this, _event);
    }

    private void SliderMinute_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_event == null) return;

        _event.Minute = (int)e.NewValue;
        TxtMinuteValue.Text = _event.Minute.ToString();
        SettingsChanged?.Invoke(this, _event);
    }

    private void BtnDelete_Click(object? sender, RoutedEventArgs e)
    {
        if (_event == null) return;
        DeleteRequested?.Invoke(this, _event);
    }

    private void BtnDone_Click(object? sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AddPhraseManagement(string title, List<string> phrases, bool isSubliminal)
    {
        var accentBrush = GetAccentBrush();

        var separator = new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(60, 60, 80)),
            Margin = new Thickness(0, 10, 0, 10)
        };
        SettingsPanel.Children.Add(separator);

        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var headerText = new TextBlock
        {
            Text = title,
            Foreground = accentBrush,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(headerText, 0);
        headerGrid.Children.Add(headerText);

        var countText = new TextBlock
        {
            Text = phrases.Count == 0
                ? LocalizationManager.Instance["feature_settings_phrase_using_global"]
                : string.Format(LocalizationManager.Instance["feature_settings_phrase_count_fmt"], phrases.Count),
            Foreground = FindBrush("TextMutedBrush", new Color(255, 136, 136, 136)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(countText, 1);
        headerGrid.Children.Add(countText);

        SettingsPanel.Children.Add(headerGrid);

        var listBox = new ListBox
        {
            MaxHeight = 80,
            Margin = new Thickness(0, 0, 0, 8)
        };

        foreach (var phrase in phrases)
        {
            listBox.Items.Add(phrase);
        }

        if (phrases.Count == 0)
        {
            listBox.Items.Add(LocalizationManager.Instance["feature_settings_phrase_list_empty"]);
            listBox.IsEnabled = false;
        }

        SettingsPanel.Children.Add(listBox);

        var buttonGrid = new Grid();
        buttonGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        buttonGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        buttonGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        var addButton = new Button
        {
            Content = LocalizationManager.Instance["feature_settings_phrase_add"],
            Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
            Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 4, 0)
        };
        addButton.Click += async (s, e) => await AddPhraseAsync(phrases, listBox, countText);
        Grid.SetColumn(addButton, 0);
        buttonGrid.Children.Add(addButton);

        var removeButton = new Button
        {
            Content = LocalizationManager.Instance["feature_settings_phrase_remove"],
            Background = new SolidColorBrush(Color.FromRgb(244, 67, 54)),
            Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(2, 0, 2, 0)
        };
        removeButton.Click += (s, e) => RemovePhrase(phrases, listBox, countText);
        Grid.SetColumn(removeButton, 1);
        buttonGrid.Children.Add(removeButton);

        var clearButton = new Button
        {
            Content = LocalizationManager.Instance["feature_settings_phrase_clear"],
            Background = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
            Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(4, 0, 0, 0)
        };
        clearButton.Click += (s, e) => ClearPhrases(phrases, listBox, countText);
        Grid.SetColumn(clearButton, 2);
        buttonGrid.Children.Add(clearButton);

        SettingsPanel.Children.Add(buttonGrid);

        var importButton = new Button
        {
            Content = LocalizationManager.Instance["feature_settings_phrase_import_global"],
            Background = new SolidColorBrush(Color.FromRgb(53, 53, 85)),
            Foreground = (SolidColorBrush)global::Avalonia.Application.Current!.Resources["TextLightBrush"]!,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        importButton.Click += (s, e) => ImportFromGlobal(phrases, listBox, countText, isSubliminal);
        SettingsPanel.Children.Add(importButton);
    }

    private async Task AddPhraseAsync(List<string> phrases, ListBox listBox, TextBlock countText)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        var dialog = new InputDialog(
            LocalizationManager.Instance["feature_settings_phrase_add_title"],
            LocalizationManager.Instance["feature_settings_phrase_add_prompt"]);

        var result = await dialog.ShowDialog<bool?>(owner);
        if (result == true && !string.IsNullOrWhiteSpace(dialog.ResultText))
        {
            if (phrases.Count == 0)
            {
                listBox.Items.Clear();
                listBox.IsEnabled = true;
            }

            phrases.Add(dialog.ResultText);
            listBox.Items.Add(dialog.ResultText);
            countText.Text = string.Format(LocalizationManager.Instance["feature_settings_phrase_count_fmt"], phrases.Count);

            if (_event != null)
                SettingsChanged?.Invoke(this, _event);
        }
    }

    private void RemovePhrase(List<string> phrases, ListBox listBox, TextBlock countText)
    {
        if (listBox.SelectedIndex >= 0 && listBox.SelectedIndex < phrases.Count)
        {
            phrases.RemoveAt(listBox.SelectedIndex);
            listBox.Items.RemoveAt(listBox.SelectedIndex);

            if (phrases.Count == 0)
            {
                listBox.Items.Add(LocalizationManager.Instance["feature_settings_phrase_list_empty"]);
                listBox.IsEnabled = false;
                countText.Text = LocalizationManager.Instance["feature_settings_phrase_using_global"];
            }
            else
            {
                countText.Text = string.Format(LocalizationManager.Instance["feature_settings_phrase_count_fmt"], phrases.Count);
            }

            if (_event != null)
                SettingsChanged?.Invoke(this, _event);
        }
    }

    private void ClearPhrases(List<string> phrases, ListBox listBox, TextBlock countText)
    {
        phrases.Clear();
        listBox.Items.Clear();
        listBox.Items.Add(LocalizationManager.Instance["feature_settings_phrase_list_empty"]);
        listBox.IsEnabled = false;
        countText.Text = LocalizationManager.Instance["feature_settings_phrase_using_global"];

        if (_event != null)
            SettingsChanged?.Invoke(this, _event);
    }

    private void ImportFromGlobal(List<string> phrases, ListBox listBox, TextBlock countText, bool isSubliminal)
    {
        var globalPool = isSubliminal
            ? CoreApp.Settings?.Current.SubliminalPool
            : CoreApp.Settings?.Current.BouncingTextPool;

        var enabledPhrases = globalPool?.Where(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();

        if (enabledPhrases == null || enabledPhrases.Count == 0)
        {
            var dialog = App.Services?.GetService<IDialogService>();
            _ = dialog?.ShowMessageAsync(
                LocalizationManager.Instance["feature_settings_phrase_import_title"],
                LocalizationManager.Instance["feature_settings_phrase_import_none"]);
            return;
        }

        phrases.Clear();
        phrases.AddRange(enabledPhrases);

        listBox.Items.Clear();
        listBox.IsEnabled = true;
        foreach (var phrase in phrases)
        {
            listBox.Items.Add(phrase);
        }

        countText.Text = string.Format(LocalizationManager.Instance["feature_settings_phrase_count_fmt"], phrases.Count);

        if (_event != null)
            SettingsChanged?.Invoke(this, _event);
    }

    private string GetSettingLabel(FeatureSettingDefinition setting)
    {
        var key = $"feature_setting_{setting.Key}";
        var localized = LocalizationManager.Instance.Get(key);
        return !string.IsNullOrEmpty(localized) && localized != key
            ? localized
            : setting.Name;
    }

    private IBrush GetAccentBrush()
    {
        var accentHex = CoreApp.Mods?.GetAccentColorHex() ?? "#FF69B4";
        if (Color.TryParse(accentHex, out var color))
            return new SolidColorBrush(color);
        return new SolidColorBrush((Color)global::Avalonia.Application.Current!.Resources["PinkColor"]!);
    }

    private IBrush FindBrush(string resourceKey, Color fallback)
    {
        if (Application.Current?.Resources[resourceKey] is IBrush brush)
            return brush;
        return new SolidColorBrush(fallback);
    }
}
