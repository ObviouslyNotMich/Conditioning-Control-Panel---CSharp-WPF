using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Moderation;

namespace ConditioningControlPanel.Avalonia.Services.Moderation;

/// <summary>
/// Cross-platform moderation log stub for the Avalonia head.
/// Records flagged editor changes to the application log so the user knows
/// their edit was flagged without requiring the legacy WPF service.
/// </summary>
public sealed class AvaloniaModerationLog : IModerationLog
{
    private readonly ILogger<AvaloniaModerationLog> _logger;

    public AvaloniaModerationLog(ILogger<AvaloniaModerationLog> logger)
    {
        _logger = logger;
    }

    public void RecordEdit(string fieldName, int count, string source)
    {
        _logger.LogInformation(
            "Moderation edit recorded: field={FieldName}, matches={Count}, source={Source}",
            fieldName, count, source);
    }

    public void Record(ProhibitedCategory category, string source, string modelHint)
    {
        _logger.LogInformation(
            "Moderation hit recorded: category={Category}, source={Source}, modelHint={ModelHint}",
            category, source, modelHint);
    }
}
