namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// System tray icon. Desktop only.
/// </summary>
public interface ITrayIcon : IDisposable
{
    void Show();
    void Hide();
    void SetTooltip(string text);
    ITrayMenu Menu { get; }
}

public interface ITrayMenu
{
    void AddItem(string label, Action callback, bool isSeparator = false);
    void Clear();
}
