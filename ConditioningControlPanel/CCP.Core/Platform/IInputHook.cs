namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Low-level input hooks. Not available on all platforms.
/// </summary>
public interface IInputHook : IDisposable
{
    event EventHandler<KeyboardHookEventArgs>? KeyPressed;
    event EventHandler<MouseHookEventArgs>? MouseMoved;
    bool CanSuppressKeys { get; }
    bool SuppressKey(int virtualKeyCode);
}

public sealed record KeyboardHookEventArgs(int VirtualKeyCode, bool Alt, bool Control, bool Shift, bool Windows);
public sealed record MouseHookEventArgs(double X, double Y);
