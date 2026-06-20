namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Global hotkey registration. Limited support on Linux/macOS.
/// </summary>
public interface IHotkeyProvider
{
    bool RegisterHotkey(string id, ModifierKeys modifiers, int key);
    void UnregisterHotkey(string id);
    event EventHandler<string>? HotkeyPressed;
}

[Flags]
public enum ModifierKeys
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}
