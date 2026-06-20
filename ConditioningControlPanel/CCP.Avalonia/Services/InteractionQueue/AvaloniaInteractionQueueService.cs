using ConditioningControlPanel.Core.Platform;
using CoreApp = global::ConditioningControlPanel.App;


namespace ConditioningControlPanel.Avalonia.Services.InteractionQueue;

/// <summary>
/// Cross-platform interaction queue stub for the Avalonia head.
/// Currently a no-op because the legacy queue is WPF-only; the call sites
/// only need a typed receiver so the dynamic CoreApp.InteractionQueue calls
/// can be removed.
/// </summary>
public sealed class AvaloniaInteractionQueueService : IInteractionQueueService
{
    private readonly IAppLogger _logger;

    public AvaloniaInteractionQueueService(IAppLogger logger)
    {
        _logger = logger;
    }

    public void Complete(string interactionType)
    {
        _logger.Debug("InteractionQueue.Complete: {InteractionType} (no-op on Avalonia)", interactionType);
    }
}
