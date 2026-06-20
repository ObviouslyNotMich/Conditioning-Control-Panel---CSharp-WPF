using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// No-op system-tray icon for mobile. Android and iOS do not expose a system tray
/// equivalent that the app can manipulate, so all calls are safely ignored.
/// </summary>
public sealed class MobileTrayIcon : ITrayIcon
{
    public event Action? Clicked { add { } remove { } }

    public ITrayMenu Menu { get; } = new NoOpTrayMenu();

    public void Show()
    {
    }

    public void Hide()
    {
    }

    public void SetTooltip(string text)
    {
    }

    public void Dispose()
    {
    }

    private sealed class NoOpTrayMenu : ITrayMenu
    {
        public void AddItem(string label, Action callback, bool isSeparator = false)
        {
        }

        public void Clear()
        {
        }
    }
}
