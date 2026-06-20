namespace ConditioningControlPanel.Avalonia.Models;

/// <summary>
/// Lightweight model for a single toast notification shown in the
/// <see cref="Views.MainWindow"/> notification host.
/// </summary>
public class NotificationItem
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public NotificationItem()
    {
    }

    public NotificationItem(string title, string message)
    {
        Title = title;
        Message = message;
    }
}
