namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Cross-platform low-level mouse hook. On Windows this captures WH_MOUSE_LL events;
/// on other platforms the events simply never fire.
/// </summary>
public interface IMouseHook
{
    /// <summary>Raised when the left mouse button is pressed anywhere on the system.</summary>
    event EventHandler<HookPoint>? LeftButtonDown;

    /// <summary>Raised when the right mouse button is pressed anywhere on the system.</summary>
    event EventHandler<HookPoint>? RightButtonDown;

    /// <summary>Installs the global hook. Safe to call multiple times.</summary>
    void Install();

    /// <summary>Uninstalls the global hook and releases native resources.</summary>
    void Uninstall();
}
