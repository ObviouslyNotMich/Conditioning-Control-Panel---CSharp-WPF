using System.Runtime.InteropServices;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Global hotkey provider for the Avalonia head.
/// On Windows it listens to the low-level keyboard stream (via <see cref="IInputHook"/>)
/// and raises <see cref="HotkeyPressed"/> when a registered key + modifier combination is pressed.
/// On Linux/macOS/mobile it degrades to a no-op because global hotkeys require platform-native
/// interop that is not available in the shared Avalonia project.
/// </summary>
public sealed class AvaloniaHotkeyProvider : IHotkeyProvider, IDisposable
{
    private readonly IInputHook? _inputHook;
    private readonly ILogger<AvaloniaHotkeyProvider>? _logger;
    private readonly Dictionary<string, HotkeyRegistration> _registrations = new();
    private readonly object _lock = new();

    public AvaloniaHotkeyProvider(IInputHook? inputHook = null, ILogger<AvaloniaHotkeyProvider>? logger = null)
    {
        _inputHook = inputHook;
        _logger = logger;

        if (_inputHook != null && OperatingSystem.IsWindows())
        {
            _inputHook.KeyPressed += OnKeyPressed;
        }
    }

    public event EventHandler<string>? HotkeyPressed;

    public bool RegisterHotkey(string id, ModifierKeys modifiers, int key)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger?.LogInformation("Global hotkeys are only supported on Windows in the Avalonia head.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(id))
            return false;

        lock (_lock)
        {
            _registrations[id] = new HotkeyRegistration(id, modifiers, key);
        }

        _logger?.LogInformation("Registered global hotkey '{Id}' ({Modifiers}+{Key})", id, modifiers, key);
        return true;
    }

    public void UnregisterHotkey(string id)
    {
        lock (_lock)
        {
            _registrations.Remove(id);
        }

        _logger?.LogInformation("Unregistered global hotkey '{Id}'", id);
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (!OperatingSystem.IsWindows()) return;

        HotkeyRegistration[] registrations;
        lock (_lock)
        {
            if (_registrations.Count == 0) return;
            registrations = _registrations.Values.ToArray();
        }

        foreach (var registration in registrations)
        {
            if (registration.Key != e.VirtualKeyCode)
                continue;

            if (!AreModifiersPressed(registration.Modifiers))
                continue;

            _logger?.LogInformation("Global hotkey '{Id}' pressed", registration.Id);
            try
            {
                HotkeyPressed?.Invoke(this, registration.Id);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Exception in global hotkey handler for '{Id}'", registration.Id);
            }
        }
    }

    private static bool AreModifiersPressed(ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.None)
            return true;

        if ((modifiers & ModifierKeys.Alt) != 0 && !IsKeyPressed(VK_MENU) && !IsKeyPressed(VK_LMENU) && !IsKeyPressed(VK_RMENU))
            return false;

        if ((modifiers & ModifierKeys.Control) != 0 && !IsKeyPressed(VK_CONTROL) && !IsKeyPressed(VK_LCONTROL) && !IsKeyPressed(VK_RCONTROL))
            return false;

        if ((modifiers & ModifierKeys.Shift) != 0 && !IsKeyPressed(VK_SHIFT) && !IsKeyPressed(VK_LSHIFT) && !IsKeyPressed(VK_RSHIFT))
            return false;

        if ((modifiers & ModifierKeys.Windows) != 0 && !IsKeyPressed(VK_LWIN) && !IsKeyPressed(VK_RWIN))
            return false;

        // If a modifier is required, make sure *only* the required modifiers are held so we don't fire on unrelated chords.
        if ((modifiers & ModifierKeys.Alt) == 0 && (IsKeyPressed(VK_MENU) || IsKeyPressed(VK_LMENU) || IsKeyPressed(VK_RMENU)))
            return false;

        if ((modifiers & ModifierKeys.Control) == 0 && (IsKeyPressed(VK_CONTROL) || IsKeyPressed(VK_LCONTROL) || IsKeyPressed(VK_RCONTROL)))
            return false;

        if ((modifiers & ModifierKeys.Shift) == 0 && (IsKeyPressed(VK_SHIFT) || IsKeyPressed(VK_LSHIFT) || IsKeyPressed(VK_RSHIFT)))
            return false;

        if ((modifiers & ModifierKeys.Windows) == 0 && (IsKeyPressed(VK_LWIN) || IsKeyPressed(VK_RWIN)))
            return false;

        return true;
    }

    private static bool IsKeyPressed(int virtualKeyCode)
    {
        try
        {
            return (GetAsyncKeyState(virtualKeyCode) & 0x8000) != 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_inputHook != null)
        {
            _inputHook.KeyPressed -= OnKeyPressed;
        }

        lock (_lock)
        {
            _registrations.Clear();
        }
    }

    private sealed record HotkeyRegistration(string Id, ModifierKeys Modifiers, int Key);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_MENU = 0x12;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;
    private const int VK_CONTROL = 0x11;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_SHIFT = 0x10;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
}
