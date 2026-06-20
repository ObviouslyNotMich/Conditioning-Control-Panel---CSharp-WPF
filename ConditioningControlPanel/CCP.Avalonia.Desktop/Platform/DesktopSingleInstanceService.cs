using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Desktop.Platform;

/// <summary>
/// Cross-platform single-instance service for the Avalonia desktop heads.
/// Uses a file lock to decide which process is the first instance and a named
/// pipe to hand command-line arguments from later instances back to the first.
/// </summary>
public sealed class DesktopSingleInstanceService : ISingleInstanceService, IDisposable
{
    private readonly string _lockFilePath;
    private readonly string _pipeName;
    private FileStream? _lockStream;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;

    public bool IsFirstInstance { get; }

    public event EventHandler<string[]>? ArgumentsReceived;

    public DesktopSingleInstanceService()
    {
        _lockFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConditioningControlPanel",
            ".instance.lock");

        _pipeName = GetPipeName();
        IsFirstInstance = TryAcquireLock();

        if (IsFirstInstance)
        {
            StartListener();
        }
    }

    /// <summary>
    /// Sends the supplied arguments to the first instance over the named pipe.
    /// Best-effort; failures are swallowed so a second instance can still exit cleanly.
    /// </summary>
    public void SignalFirstInstance(string[] args)
    {
        if (IsFirstInstance || args is null)
        {
            return;
        }

        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(JsonSerializer.Serialize(args));
        }
        catch
        {
            // Ignore: the first instance may not have finished starting its listener yet.
        }
    }

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
            try { _listenerTask?.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignored */ }

            _lockStream?.Dispose();
        }
        catch
        {
            // ignored
        }
        finally
        {
            _cts?.Dispose();
        }
    }

    private bool TryAcquireLock()
    {
        try
        {
            var directory = Path.GetDirectoryName(_lockFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Opening the lock file with FileShare.None is enough for cross-process
            // exclusion on Windows, Linux, and macOS. FileStream.Lock/Unlock is not
            // available on every platform and is unnecessary when we own the handle.
            _lockStream = new FileStream(
                _lockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);

            return true;
        }
        catch (IOException)
        {
            _lockStream?.Dispose();
            _lockStream = null;
            return false;
        }
    }

    private void StartListener()
    {
        _cts = new CancellationTokenSource();
        _listenerTask = Task.Run(async () => await ListenAsync(_cts.Token).ConfigureAwait(false));
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8);
                var json = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(json))
                {
                    var args = JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
                    ArgumentsReceived?.Invoke(this, args);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Keep the listener alive across transient errors.
            }
        }
    }

    private static string GetPipeName()
    {
        var user = Environment.UserName;
        var machine = Environment.MachineName;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{user}@{machine}"));
        return $"CCP_SI_{Convert.ToHexString(hash)[..16]}";
    }
}
