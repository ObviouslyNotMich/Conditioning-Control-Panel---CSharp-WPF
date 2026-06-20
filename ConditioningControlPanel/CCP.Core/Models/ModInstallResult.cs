namespace ConditioningControlPanel.Core.Models
{
    /// <summary>
    /// Outcome of an attempt to install a .ccpmod package.
    /// Lives in CCP.Core so <see cref="IModService"/> can reference it without a circular
    /// dependency on the Avalonia head.
    /// </summary>
    public enum ModInstallStatus
    {
        Success,
        InvalidPackage,
        InvalidManifest,
        InvalidId,
        AlreadyInstalled,
        IOFailure,
        UnknownError
    }

    /// <summary>
    /// Result of a mod installation attempt.
    /// </summary>
    public sealed class ModInstallResult
    {
        public ModInstallStatus Status { get; }
        public string? ErrorMessage { get; }
        public ModPackage? InstalledMod { get; }

        private ModInstallResult(ModInstallStatus status, string? errorMessage, ModPackage? installedMod)
        {
            Status = status;
            ErrorMessage = errorMessage;
            InstalledMod = installedMod;
        }

        public static ModInstallResult Success(ModPackage installedMod)
            => new(ModInstallStatus.Success, null, installedMod);

        public static ModInstallResult Failure(ModInstallStatus status, string errorMessage)
            => new(status, errorMessage, null);
    }
}
