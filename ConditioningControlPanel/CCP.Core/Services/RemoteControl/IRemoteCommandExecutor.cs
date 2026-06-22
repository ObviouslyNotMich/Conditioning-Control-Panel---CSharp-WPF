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

    /// <summary>
    /// Stops all effects that may have been started by a remote controller.
    /// Called when the session ends or a panic command is received.
    /// </summary>
    Task StopAllRemoteEffectsAsync();

    /// <summary>
    /// Called when the controller disconnects. Implementations should stop remote-triggered
    /// effects and, if the user has opted in, restore the local engine state.
    /// </summary>
    Task HandleControllerDisconnectAsync();
}
