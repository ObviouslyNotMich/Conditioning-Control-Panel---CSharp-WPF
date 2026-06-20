using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Services.Moderation;

/// <summary>
/// Cross-platform moderation log stub for the Avalonia head.
/// Records flagged editor changes to the application log so the user knows
/// their edit was flagged without requiring the legacy WPF service.
/// </summary>
public sealed class AvaloniaModerationLog : IModerationLog
{
    private readonly IAppLogger _logger;

    public AvaloniaModerationLog(IAppLogger logger)
    {
        _logger = logger;
    }

    public void RecordEdit(string fieldName, int count, string source)
    {
        _logger.Information(
            "Moderation edit recorded: field={FieldName}, matches={Count}, source={Source}",
            fieldName, count, source);
    }
}
