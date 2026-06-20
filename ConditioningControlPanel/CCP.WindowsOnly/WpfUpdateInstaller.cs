using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.WindowsOnly;

/// <summary>
/// Stub WPF update installer shim for <see cref="IUpdateInstaller"/>.
/// </summary>
public sealed class WpfUpdateInstaller : IUpdateInstaller
{
    public Task<bool> DownloadUpdateAsync(Uri downloadUri, IProgress<double> progress, CancellationToken cancellationToken = default)
    {
        // Stub: real implementation would download the update package.
        progress?.Report(1.0);
        return Task.FromResult(true);
    }

    public Task InstallUpdateAsync()
    {
        // Stub: real implementation would run the installer.
        return Task.CompletedTask;
    }

    public string? GetInstalledVersion()
    {
        return Assembly.GetEntryAssembly()?.GetName().Version?.ToString();
    }
}
