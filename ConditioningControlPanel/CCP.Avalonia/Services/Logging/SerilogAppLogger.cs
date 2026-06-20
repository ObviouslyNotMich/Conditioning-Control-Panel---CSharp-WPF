using Serilog;

namespace ConditioningControlPanel.Avalonia.Services.Logging;

/// <summary>
/// <see cref="IAppLogger"/> wrapper around a Serilog <see cref="ILogger"/>.
/// </summary>
public sealed class SerilogAppLogger : IAppLogger
{
    private readonly ILogger _logger;

    public SerilogAppLogger(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Debug(string messageTemplate, params object?[] propertyValues)
        => _logger.Debug(messageTemplate, propertyValues);

    public void Debug(Exception exception, string messageTemplate, params object?[] propertyValues)
        => _logger.Debug(exception, messageTemplate, propertyValues);

    public void Information(string messageTemplate, params object?[] propertyValues)
        => _logger.Information(messageTemplate, propertyValues);

    public void Information(Exception exception, string messageTemplate, params object?[] propertyValues)
        => _logger.Information(exception, messageTemplate, propertyValues);

    public void Warning(string messageTemplate, params object?[] propertyValues)
        => _logger.Warning(messageTemplate, propertyValues);

    public void Warning(Exception exception, string messageTemplate, params object?[] propertyValues)
        => _logger.Warning(exception, messageTemplate, propertyValues);

    public void Error(string messageTemplate, params object?[] propertyValues)
        => _logger.Error(messageTemplate, propertyValues);

    public void Error(Exception exception, string messageTemplate, params object?[] propertyValues)
        => _logger.Error(exception, messageTemplate, propertyValues);
}
