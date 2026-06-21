using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Fallback startup registration for platforms without a dedicated implementation.
/// </summary>
public sealed class AvaloniaStartupRegistration : IStartupRegistration
{
    public bool IsRegistered => false;

    public void SetRegistered(bool value)
    {
        // No-op on unsupported platforms.
    }
}
