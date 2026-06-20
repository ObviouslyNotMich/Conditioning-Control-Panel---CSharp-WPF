using ConditioningControlPanel.Core.Platform;
using CoreApp = global::ConditioningControlPanel.App;


namespace ConditioningControlPanel.Avalonia.Services.LockCard;

/// <summary>
/// Cross-platform lock-card service stub for the Avalonia head.
/// The legacy service is WPF-only; this implementation removes the dynamic
/// CoreApp.LockCard call sites.
/// </summary>
public sealed class AvaloniaLockCardService : ILockCardService
{
    private readonly IAppLogger _logger;

    public AvaloniaLockCardService(IAppLogger logger)
    {
        _logger = logger;
    }

    public void NotifyCompleted(string phrase, int totalErrors, int requiredRepeats)
    {
        _logger.Information(
            "LockCard.NotifyCompleted: {Phrase} ({Errors} errors, {Repeats} repeats) (no-op on Avalonia)",
            phrase, totalErrors, requiredRepeats);
    }
}
