using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.WindowsOnly;

/// <summary>
/// No-op system audio ducker shim for <see cref="ISystemAudioDucker"/>.
/// </summary>
public sealed class WpfSystemAudioDucker : ISystemAudioDucker
{
    public void Duck()
    {
        // No-op on WPF seam.
    }

    public void Unduck()
    {
        // No-op on WPF seam.
    }
}
