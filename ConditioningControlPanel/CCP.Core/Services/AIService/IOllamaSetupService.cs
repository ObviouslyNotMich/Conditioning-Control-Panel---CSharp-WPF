using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Core.Services.AIService
{
    /// <summary>
    /// Installation state of the local Ollama runtime as observed by the setup wizard.
    /// </summary>
    public enum OllamaInstallStatus
    {
        NotInstalled,
        InstalledNotRunning,
        RunningNoModel,
        Ready
    }

    /// <summary>
    /// Snapshot returned by <see cref="IOllamaSetupService.DetectAsync"/>.
    /// </summary>
    public sealed class OllamaStatusSnapshot
    {
        public OllamaInstallStatus Status { get; init; }
        public bool ServiceReachable { get; init; }
        public bool ExecutableFound { get; init; }
        public string? ExecutablePath { get; init; }
        public List<string> InstalledModels { get; init; } = new();
        public bool TargetModelInstalled { get; init; }
    }

    /// <summary>
    /// Progress reported while downloading the official Ollama installer.
    /// </summary>
    public sealed class OllamaDownloadProgress
    {
        public long BytesReceived { get; init; }
        public long? TotalBytes { get; init; }
        public double? PercentComplete =>
            TotalBytes.HasValue && TotalBytes.Value > 0
                ? (double)BytesReceived / TotalBytes.Value * 100.0
                : null;
        public double BytesPerSecond { get; init; }
    }

    /// <summary>
    /// Progress reported while pulling a model via Ollama's /api/pull endpoint.
    /// </summary>
    public sealed class OllamaPullProgress
    {
        public string Status { get; init; } = "";
        public string? Digest { get; init; }
        public long? Total { get; init; }
        public long? Completed { get; init; }
        public double? PercentComplete =>
            Total.HasValue && Total.Value > 0 && Completed.HasValue
                ? (double)Completed.Value / Total.Value * 100.0
                : null;
    }

    /// <summary>
    /// Cross-platform abstraction over Ollama detection, installation, model pulling,
    /// and smoke testing used by the local-AI setup wizard.
    /// </summary>
    public interface IOllamaSetupService
    {
        /// <summary>
        /// Probes the system for Ollama: checks the standard install path, queries the
        /// service if reachable, and lists installed models.
        /// </summary>
        Task<OllamaStatusSnapshot> DetectAsync(
            string? host = null,
            string? targetModel = null,
            CancellationToken ct = default);

        /// <summary>
        /// Streams the official Ollama installer to a temp file and reports byte progress.
        /// </summary>
        Task<string> DownloadInstallerAsync(
            IProgress<OllamaDownloadProgress>? progress = null,
            CancellationToken ct = default);

        /// <summary>
        /// Runs OllamaSetup.exe silently and waits for the service to come up.
        /// </summary>
        Task<bool> RunInstallerSilentAsync(
            string installerPath,
            string? host = null,
            CancellationToken ct = default);

        /// <summary>
        /// If Ollama is installed but no service is listening, spawn
        /// <c>ollama.exe serve</c> and wait for the HTTP server to respond.
        /// </summary>
        Task<bool> StartServiceAsync(
            string? host = null,
            CancellationToken ct = default);

        /// <summary>
        /// Terminates the headless <c>ollama serve</c> process this app spawned, if any.
        /// </summary>
        void StopSpawnedServer();

        /// <summary>
        /// Streams Ollama's /api/pull NDJSON output and reports per-event progress.
        /// </summary>
        Task PullModelAsync(
            string model,
            string? host = null,
            IProgress<OllamaPullProgress>? progress = null,
            CancellationToken ct = default);

        /// <summary>
        /// Sends a one-token chat call to /api/chat to confirm the model is wired up.
        /// </summary>
        Task<(bool ok, TimeSpan elapsed, string reply)> SmokeTestAsync(
            string model,
            string? host = null,
            CancellationToken ct = default);

        /// <summary>
        /// Formats a byte count as a human-readable string.
        /// </summary>
        string FormatBytes(long bytes);

        /// <summary>
        /// Formats a bytes-per-second rate as a human-readable string.
        /// </summary>
        string FormatRate(double bytesPerSecond);
    }
}
