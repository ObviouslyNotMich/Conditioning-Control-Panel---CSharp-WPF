using ConditioningControlPanel.Core.Platform;
using CoreApp = global::ConditioningControlPanel.App;


namespace ConditioningControlPanel.Avalonia.Services.AttentionCheck;

/// <summary>
/// Cross-platform attention-check service stub for the Avalonia head.
/// The legacy service is WPF-only; this implementation removes the dynamic
/// CoreApp.AttentionCheck call sites.
/// </summary>
public sealed class AvaloniaAttentionCheckService : IAttentionCheckService
{
    private readonly IAppLogger _logger;

    public AvaloniaAttentionCheckService(IAppLogger logger)
    {
        _logger = logger;
    }

    public void FireNow()
    {
        _logger.Debug("AttentionCheck.FireNow (no-op on Avalonia)");
    }
}
