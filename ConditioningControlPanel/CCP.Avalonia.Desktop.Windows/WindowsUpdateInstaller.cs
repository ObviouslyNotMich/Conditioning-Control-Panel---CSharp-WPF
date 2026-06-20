using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Win32;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows
{
    /// <summary>
    /// Windows head for <see cref="IUpdateInstaller"/>.
    /// Downloads the Inno Setup installer using the shared Avalonia downloader and,
    /// on install, runs it silently with the existing install directory and exits.
    /// </summary>
    public sealed class WindowsUpdateInstaller : AvaloniaUpdateInstaller
    {
        private readonly ISettingsService _settingsService;
        private readonly IAppLogger? _logger;

        public WindowsUpdateInstaller(ISettingsService settingsService, IAppLogger? logger = null)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _logger = logger;
        }

        public override Task InstallUpdateAsync()
        {
            var installerPath = GetDownloadedPackagePath();
            if (string.IsNullOrEmpty(installerPath))
                throw new InvalidOperationException("No update package has been downloaded.");
            if (!File.Exists(installerPath))
                throw new FileNotFoundException("Update installer not found", installerPath);

            RunInstallerSilentlyAndExit(installerPath);
            return Task.CompletedTask;
        }

        private void RunInstallerSilentlyAndExit(string installerPath)
        {
            var installPath = GetInstalledPath()
                ?? Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);

            _logger?.Information("Launching installer for silent update: {Path}, InstallDir: {Dir}", installerPath, installPath);

            try
            {
                _settingsService.SaveImmediate(suppressCloudBackup: false);
            }
            catch (Exception ex)
            {
                _logger?.Warning(ex, "Failed to save settings before update");
            }

            CleanupBeforeUpdate();

            var args = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS";
            if (!string.IsNullOrEmpty(installPath))
                args += $" /DIR=\"{installPath}\"";

            _logger?.Information("Installer arguments: {Args}", args);

            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = args,
                UseShellExecute = true
            });

            _logger?.Information("Exiting application for silent update...");

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
            else
            {
                Environment.Exit(0);
            }
        }

        private static string? GetInstalledPath()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\CodeBambi\Conditioning Control Panel");
                return key?.GetValue("InstallPath")?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private void CleanupBeforeUpdate()
        {
            try
            {
                _logger?.Information("Cleaning up before update...");

                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;

                var installDir = Path.GetDirectoryName(exePath);
                if (string.IsNullOrEmpty(installDir)) return;

                KillWebView2Processes(installDir);

                var browserDataPath = Path.Combine(installDir, "browser_data");
                if (Directory.Exists(browserDataPath))
                {
                    try { Directory.Delete(browserDataPath, true); } catch { }
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning(ex, "Cleanup before update failed");
            }
        }

        private void KillWebView2Processes(string installDir)
        {
            try
            {
                // Use PowerShell/CIM instead of the deprecated wmic.exe; works on Windows 10/11
                // and returns the command line for each msedgewebview2.exe process.
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -Command \"Get-CimInstance Win32_Process -Filter \\\"Name='msedgewebview2.exe'\\\" | Select-Object ProcessId,CommandLine | ConvertTo-Csv -NoTypeInformation\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var psProcess = Process.Start(startInfo);
                if (psProcess == null) return;

                var output = psProcess.StandardOutput.ReadToEnd();
                psProcess.WaitForExit(5000);

                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var headerSeen = false;
                foreach (var line in lines)
                {
                    if (!headerSeen)
                    {
                        headerSeen = true;
                        continue;
                    }

                    var parts = line.Split(',', 3);
                    if (parts.Length < 2) continue;

                    var pidString = parts[0].Trim().Trim('"');
                    var commandLine = parts[1].Trim().Trim('"');

                    if (!commandLine.Contains(installDir, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!int.TryParse(pidString, out var pid)) continue;

                    try
                    {
                        var proc = Process.GetProcessById(pid);
                        proc.Kill();
                        proc.WaitForExit(2000);
                    }
                    catch { /* swallow per-process errors */ }
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning(ex, "Failed to kill WebView2 processes");
            }
        }
    }
}
