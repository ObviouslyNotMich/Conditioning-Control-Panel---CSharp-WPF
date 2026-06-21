using System.Drawing;

namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Cross-platform sampling of the global pointer state.
/// Non-Windows heads may degrade to null/false.
/// </summary>
public interface IPointerState
{
    /// <summary>
    /// Returns the current cursor position in physical screen coordinates, or null if unavailable.
    /// </summary>
    System.Drawing.Point? GetCursorPosition();

    /// <summary>
    /// Returns true if the specified mouse button is currently held down.
    /// </summary>
    bool IsMouseButtonPressed(MouseButton button);
}
