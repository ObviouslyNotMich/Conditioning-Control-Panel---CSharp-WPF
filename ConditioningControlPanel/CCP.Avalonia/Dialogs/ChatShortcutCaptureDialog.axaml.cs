using System;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ConditioningControlPanel.Core.Localization;

namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Modal dialog that listens for the next keypress and captures it as a chat shortcut.
/// Returns with <see cref="CapturedKey"/>, <see cref="CapturedModifiers"/> and
/// <see cref="ResetToDefault"/> set on success, or a null result on cancel.
/// </summary>
public partial class ChatShortcutCaptureDialog : Window
{
    public Key CapturedKey { get; private set; }
    public KeyModifiers CapturedModifiers { get; private set; }
    public bool ResetToDefault { get; private set; }

    /// <summary>
    /// State of the "activate from any app" checkbox at dialog close.
    /// </summary>
    public bool GlobalHotkey
    {
        get => ChkGlobal?.IsChecked == true;
        set { if (ChkGlobal != null) ChkGlobal.IsChecked = value; }
    }

    public ChatShortcutCaptureDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => Focus();
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        // Ignore modifier-only keys; we want a "real" key to bind to.
        var key = e.Key;
        if (IsModifierOnly(key))
        {
            e.Handled = true;
            return;
        }

        // Escape cancels.
        if (key == Key.Escape)
        {
            e.Handled = true;
            Close(false);
            return;
        }

        // A bare letter/digit/symbol with no modifier would steal that key globally.
        // Require at least one modifier; F-keys are accepted bare since they rarely
        // collide with text input.
        var mods = e.KeyModifiers;
        if (mods == KeyModifiers.None && !IsFunctionKey(key))
        {
            e.Handled = true;
            if (TxtCaptured != null)
                TxtCaptured.Text = Loc.Get("label_chat_shortcut_needs_modifier");
            return;
        }

        CapturedKey = key;
        CapturedModifiers = mods;
        ResetToDefault = false;

        e.Handled = true;
        Close(true);
    }

    private void BtnReset_Click(object? sender, RoutedEventArgs e)
    {
        ResetToDefault = true;
        Close(true);
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private static bool IsModifierOnly(Key k)
    {
        return k is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin
            or Key.None;
    }

    private static bool IsFunctionKey(Key k) => k >= Key.F1 && k <= Key.F24;

    /// <summary>
    /// Formats the captured shortcut as a human-readable string.
    /// </summary>
    public static string FormatShortcut(Key key, KeyModifiers mods)
    {
        var parts = ""
            + (mods.HasFlag(KeyModifiers.Control) ? "Ctrl+" : "")
            + (mods.HasFlag(KeyModifiers.Shift) ? "Shift+" : "")
            + (mods.HasFlag(KeyModifiers.Alt) ? "Alt+" : "")
            + (mods.HasFlag(KeyModifiers.Meta) ? "Win+" : "");
        return parts + key.ToString();
    }

    /// <summary>
    /// Serializes the captured shortcut to the settings format used by
    /// <see cref="Core.Models.CompanionPromptSettings.ChatShortcutKey"/>
    /// and <see cref="Core.Models.CompanionPromptSettings.ChatShortcutModifiers"/>.
    /// </summary>
    public static (string Key, string Modifiers) ToSettings(Key key, KeyModifiers mods)
    {
        var modifiers = string.Join(",", Enum.GetValues<KeyModifiers>()
            .Where(m => m != KeyModifiers.None && mods.HasFlag(m))
            .Select(m => m.ToString()));
        return (key.ToString(), modifiers);
    }
}
