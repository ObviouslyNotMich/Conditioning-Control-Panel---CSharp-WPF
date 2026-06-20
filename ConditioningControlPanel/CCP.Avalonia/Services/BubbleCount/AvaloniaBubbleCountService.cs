using ConditioningControlPanel.Core.Platform;
using CoreApp = global::ConditioningControlPanel.App;


namespace ConditioningControlPanel.Avalonia.Services.BubbleCount;

/// <summary>
/// Cross-platform bubble-count service stub for the Avalonia head.
/// The legacy service is WPF-only; this implementation removes the dynamic
/// CoreApp.BubbleCount call sites.
/// </summary>
public sealed class AvaloniaBubbleCountService : IBubbleCountService
{
    private readonly IAppLogger _logger;

    public AvaloniaBubbleCountService(IAppLogger logger)
    {
        _logger = logger;
    }

    public void ResetBusyState()
    {
        _logger.Debug("BubbleCount.ResetBusyState (no-op on Avalonia)");
    }
}
