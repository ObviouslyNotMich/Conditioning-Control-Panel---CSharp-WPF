using System.Diagnostics;

namespace ConditioningControlPanel;

/// <summary>
/// Simple <see cref="IAppLogger"/> implementation that writes to <see cref="Debug"/>.
/// This is the fallback logger for the Avalonia head until a richer logger is wired.
/// </summary>
public sealed class DebugLogger : IAppLogger
{
    public void Debug(string messageTemplate, params object?[] propertyValues)
        => System.Diagnostics.Debug.WriteLine("[DEBUG] " + Format(messageTemplate, propertyValues));

    public void Debug(Exception exception, string messageTemplate, params object?[] propertyValues)
        => System.Diagnostics.Debug.WriteLine("[DEBUG] " + Format(messageTemplate, propertyValues) + "\n" + exception);

    public void Information(string messageTemplate, params object?[] propertyValues)
        => System.Diagnostics.Debug.WriteLine("[INFO] " + Format(messageTemplate, propertyValues));

    public void Information(Exception exception, string messageTemplate, params object?[] propertyValues)
        => System.Diagnostics.Debug.WriteLine("[INFO] " + Format(messageTemplate, propertyValues) + "\n" + exception);

    public void Warning(string messageTemplate, params object?[] propertyValues)
        => System.Diagnostics.Debug.WriteLine("[WARN] " + Format(messageTemplate, propertyValues));

    public void Warning(Exception exception, string messageTemplate, params object?[] propertyValues)
        => System.Diagnostics.Debug.WriteLine("[WARN] " + Format(messageTemplate, propertyValues) + "\n" + exception);

    public void Error(string messageTemplate, params object?[] propertyValues)
        => System.Diagnostics.Debug.WriteLine("[ERROR] " + Format(messageTemplate, propertyValues));

    public void Error(Exception exception, string messageTemplate, params object?[] propertyValues)
        => System.Diagnostics.Debug.WriteLine("[ERROR] " + Format(messageTemplate, propertyValues) + "\n" + exception);

    private static string Format(string template, object?[] args)
    {
        if (args == null || args.Length == 0) return template;
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }
}
