using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Core.Services.RemoteControl;

/// <summary>
/// Platform-specific executor for remote-control commands received from the server.
/// The Core <see cref="RemoteControlService"/> parses poll responses and delegates
/// command actions to this seam so each head can map them to its own service stack.
/// </summary>
public interface IRemoteCommandExecutor
{
    /// <summary>
    /// Executes a remote command. Implementations should be safe to call from any thread
    /// and must marshal UI-affecting work to the UI thread internally.
    /// </summary>
    /// <param name="action">The command action name (e.g. "trigger_flash").</param>
    /// <param name="parameters">Optional command parameters supplied by the controller.</param>
    Task ExecuteCommandAsync(string action, JObject? parameters);
}
