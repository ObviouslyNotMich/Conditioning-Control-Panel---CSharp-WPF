using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// No-op input hook for mobile. Global low-level keyboard/mouse hooks are not available
/// on Android or iOS, so this implementation safely ignores registration and disposal.
/// </summary>
public sealed class MobileInputHook : IInputHook
{
    public event EventHandler<KeyboardHookEventArgs>? KeyPressed { add { } remove { } }
    public event EventHandler<MouseHookEventArgs>? MouseMoved { add { } remove { } }

    public bool CanSuppressKeys => false;

    public bool SuppressKey(int virtualKeyCode) => false;

    public void Dispose()
    {
    }
}
