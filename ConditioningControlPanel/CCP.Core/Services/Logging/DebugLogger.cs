using System;

namespace ConditioningControlPanel;

/// <summary>
/// Simple <see cref="ILogger{TCategoryName}"/> implementation that writes to <see cref="Debug"/>.
/// This is the fallback logger for tests and the Avalonia head until a richer logger is wired.
/// </summary>
public sealed class DebugLogger<TCategoryName> : ILogger<TCategoryName>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        var prefix = logLevel switch
        {
            LogLevel.Trace => "[TRACE]",
            LogLevel.Debug => "[DEBUG]",
            LogLevel.Information => "[INFO]",
            LogLevel.Warning => "[WARN]",
            LogLevel.Error => "[ERROR]",
            LogLevel.Critical => "[CRITICAL]",
            _ => "[LOG]"
        };
        System.Diagnostics.Debug.WriteLine($"{prefix} {message}" + (exception is null ? "" : "\n" + exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
