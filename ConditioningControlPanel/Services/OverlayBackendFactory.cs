using System;

namespace ConditioningControlPanel.Services;

public static class OverlayBackendFactory
{
    private const string BackendEnvVar = "CCP_OVERLAY_BACKEND";

    public static IOverlayService Create()
    {
        var requested = Environment.GetEnvironmentVariable(BackendEnvVar)?.Trim();

        if (string.Equals(requested, "native", StringComparison.OrdinalIgnoreCase))
        {
            App.Logger?.Information("Overlay backend selected: native");
            return new NativeOverlayService();
        }

        if (!string.IsNullOrWhiteSpace(requested) &&
            !string.Equals(requested, "wpf", StringComparison.OrdinalIgnoreCase))
        {
            App.Logger?.Warning("Unknown overlay backend '{Backend}' in {Var}; falling back to wpf", requested, BackendEnvVar);
        }

        App.Logger?.Information("Overlay backend selected: wpf");
        return new OverlayService();
    }
}
