using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Dialog for editing text pools (subliminals, attention targets, etc.)
/// </summary>
public partial class TextEditorDialog : Window
{
    private Dictionary<string, bool> _originalData = new();
    private ObservableCollection<TextItem> _items = new();
    private bool _hasChanges;
    private bool? _dialogResult;
    private readonly IDialogService? _dialogService;

    /// <summary>
    /// The edited data after Save is clicked.
    /// </summary>
    public Dictionary<string, bool>? ResultData { get; private set; }

    public TextEditorDialog()
    {
        InitializeComponent();
        _dialogService = global::ConditioningControlPanel.Avalonia.App.Services?.GetService<IDialogService>();
    }

    public TextEditorDialog(string title, Dictionary<string, bool> data)
    {
        InitializeComponent();
        _dialogService = global::ConditioningControlPanel.Avalonia.App.Services?.GetService<IDialogService>();

        TxtTitle.Text = $"📝 {title}";
        Title = Loc.GetF("title_manager", title);

        _originalData = new Dictionary<string, bool>(data);
        _items = new ObservableCollection<TextItem>(
            data.Select(kvp => new TextItem { Text = kvp.Key, IsEnabled = kvp.Value })
                .OrderBy(x => x.Text));

        ItemList.ItemsSource = _items;
    }

    private void BtnAdd_Click(object? sender, RoutedEventArgs e)
    {
        AddNewItem();
    }

    private void TxtNewItem_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddNewItem();
        }
    }

    private async void AddNewItem()
    {
        var text = TxtNewItem.Text?.Trim().ToUpperInvariant();

        if (string.IsNullOrEmpty(text))
            return;

        if (_items.Any(x => x.Text.Equals(text, StringComparison.OrdinalIgnoreCase)))
        {
            if (_dialogService != null)
            {
                await _dialogService.ShowMessageAsync(
                    Loc.Get("title_duplicate"),
                    Loc.Get("msg_this_item_already_exists"),
                    DialogSeverity.Warning);
            }
            return;
        }

        _items.Add(new TextItem { Text = text, IsEnabled = true });
        TxtNewItem.Clear();
        TxtNewItem.Focus();
        _hasChanges = true;
    }

    private void BtnSort_Click(object? sender, RoutedEventArgs e)
    {
        var sorted = _items.OrderBy(x => x.Text).ToList();
        _items.Clear();
        foreach (var item in sorted)
        {
            _items.Add(item);
        }
    }

    private void BtnToggleAll_Click(object? sender, RoutedEventArgs e)
    {
        bool allEnabled = _items.All(x => x.IsEnabled);
        bool newState = !allEnabled;

        foreach (var item in _items)
        {
            item.IsEnabled = newState;
        }

        ItemList.ItemsSource = null;
        ItemList.ItemsSource = _items;
        _hasChanges = true;
    }

    private void Item_Click(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: TextItem item })
        {
            item.IsSelected = !item.IsSelected;
            ItemList.ItemsSource = null;
            ItemList.ItemsSource = _items;
        }
    }

    private void Toggle_Changed(object? sender, RoutedEventArgs e)
    {
        _hasChanges = true;
    }

    private async void BtnRemove_Click(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;

        if (sender is Control { DataContext: TextItem item })
        {
            var confirmed = await (_dialogService?.ShowConfirmationAsync(
                Loc.Get("title_confirm"),
                Loc.GetF("msg_confirm_remove_item", item.Text)) ?? Task.FromResult(false));

            if (confirmed)
            {
                _items.Remove(item);
                _hasChanges = true;
            }
        }
    }

    private async void BtnRemoveSelected_Click(object? sender, RoutedEventArgs e)
    {
        var selected = _items.Where(x => x.IsSelected).ToList();

        if (selected.Count == 0)
        {
            if (_dialogService != null)
            {
                await _dialogService.ShowMessageAsync(
                    Loc.Get("title_no_selection"),
                    Loc.Get("msg_no_items_selected_n_nclick_on_items_to_select"),
                    DialogSeverity.Info);
            }
            return;
        }

        var confirmed = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("title_confirm"),
            Loc.GetF("msg_confirm_remove_selected", selected.Count)) ?? Task.FromResult(false));

        if (confirmed)
        {
            foreach (var item in selected)
            {
                _items.Remove(item);
            }
            _hasChanges = true;
        }
    }

    private async void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        if (_hasChanges)
        {
            var confirmed = await (_dialogService?.ShowConfirmationAsync(
                Loc.Get("title_unsaved_changes"),
                Loc.Get("msg_discard_changes")) ?? Task.FromResult(false));

            if (!confirmed)
                return;
        }

        ResultData = null;
        _dialogResult = false;
        Close(false);
    }

    private void BtnSave_Click(object? sender, RoutedEventArgs e)
    {
        ResultData = _items.ToDictionary(x => x.Text, x => x.IsEnabled);
        _dialogResult = true;
        Close(true);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_dialogResult.HasValue && _hasChanges)
        {
            // IDialogService only supports two-way confirmation, so Cancel is mapped to No (close without saving).
            e.Cancel = true;
            _ = HandleClosingAsync();
            return;
        }

        base.OnClosing(e);
    }

    private async Task HandleClosingAsync()
    {
        var confirmed = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("title_unsaved_changes"),
            Loc.Get("msg_save_changes_before_closing")) ?? Task.FromResult(false));

        if (confirmed)
        {
            ResultData = _items.ToDictionary(x => x.Text, x => x.IsEnabled);
            _dialogResult = true;
            Close(true);
        }
        else
        {
            _dialogResult = false;
            Close(false);
        }
    }
}

/// <summary>
/// Represents a text item in the list.
/// </summary>
public class TextItem : INotifyPropertyChanged
{
    private string _text = "";
    private bool _isEnabled = true;
    private bool _isSelected;

    public string Text
    {
        get => _text;
        set { _text = value; OnPropertyChanged(nameof(Text)); }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(nameof(IsEnabled)); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
