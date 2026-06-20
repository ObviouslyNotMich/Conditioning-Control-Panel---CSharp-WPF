using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// No-op global hotkey provider for mobile. System-level global hotkeys are not supported
/// on Android or iOS, so registration always returns false and no events are raised.
/// </summary>
public sealed class MobileHotkeyProvider : IHotkeyProvider, IDisposable
{
    public event EventHandler<string>? HotkeyPressed { add { } remove { } }

    public bool RegisterHotkey(string id, ModifierKeys modifiers, int key) => false;

    public void UnregisterHotkey(string id)
    {
    }

    public void Dispose()
    {
    }
}
