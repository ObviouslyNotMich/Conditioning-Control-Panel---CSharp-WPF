namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Platform-agnostic full-screen or shaped overlay window.
/// </summary>
public interface IOverlaySurface
{
    void Show();
    void Hide();
    void Close();
    bool IsVisible { get; }
    void SetClickThrough(bool enabled);
    void SetBounds(PixelRect rect);
}
