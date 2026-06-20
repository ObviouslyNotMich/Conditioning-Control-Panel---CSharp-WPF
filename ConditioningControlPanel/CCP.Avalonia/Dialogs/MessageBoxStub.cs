namespace ConditioningControlPanel.Avalonia.Dialogs;

/// <summary>
/// Stubs for the legacy WPF MessageBox API. These will be replaced by a cross-platform
/// IDialogService implementation once the Avalonia shell wires up dialog hosting.
/// </summary>
public enum MessageBoxButton
{
    OK,
    OKCancel,
    YesNo,
    YesNoCancel
}

public enum MessageBoxImage
{
    None,
    Information,
    Warning,
    Question,
    Error
}

public enum MessageBoxResult
{
    None,
    OK,
    Cancel,
    Yes,
    No
}

public static class MessageBoxStub
{
    // TODO: replace with cross-platform IDialogService once available.
    public static MessageBoxResult Show(string message, string caption, MessageBoxButton button, MessageBoxImage image)
    {
        return button switch
        {
            MessageBoxButton.YesNo => MessageBoxResult.Yes,
            MessageBoxButton.YesNoCancel => MessageBoxResult.Yes,
            MessageBoxButton.OKCancel => MessageBoxResult.OK,
            _ => MessageBoxResult.OK,
        };
    }
}
