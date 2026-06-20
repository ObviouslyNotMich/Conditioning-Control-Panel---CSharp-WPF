using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.WindowsOnly;

/// <summary>
/// Win32 global hotkey shim for <see cref="IHotkeyProvider"/>.
/// </summary>
public sealed class WpfHotkeyProvider : NativeWindow, IHotkeyProvider
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Dictionary<string, int> _ids = new();
    private int _nextId = 1;

    public event EventHandler<string>? HotkeyPressed;

    public WpfHotkeyProvider()
    {
        CreateHandle(new CreateParams
        {
            ExStyle = 0,
            Style = 0x800000 // WS_BORDER minimal
        });
    }

    public bool RegisterHotkey(string id, ModifierKeys modifiers, int key)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        UnregisterHotkey(id);

        var nativeId = _nextId++;
        uint fsMods = MOD_NOREPEAT;
        if ((modifiers & ModifierKeys.Alt) != 0) fsMods |= MOD_ALT;
        if ((modifiers & ModifierKeys.Control) != 0) fsMods |= MOD_CONTROL;
        if ((modifiers & ModifierKeys.Shift) != 0) fsMods |= MOD_SHIFT;
        if ((modifiers & ModifierKeys.Windows) != 0) fsMods |= MOD_WIN;

        if (!RegisterHotKey(Handle, nativeId, fsMods, (uint)key))
            return false;

        _ids[id] = nativeId;
        return true;
    }

    public void UnregisterHotkey(string id)
    {
        if (_ids.TryGetValue(id, out var nativeId))
        {
            UnregisterHotKey(Handle, nativeId);
            _ids.Remove(id);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            var nativeId = m.WParam.ToInt32();
            foreach (var pair in _ids)
            {
                if (pair.Value == nativeId)
                {
                    HotkeyPressed?.Invoke(this, pair.Key);
                    break;
                }
            }
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (var nativeId in _ids.Values)
        {
            UnregisterHotKey(Handle, nativeId);
        }
        _ids.Clear();
        DestroyHandle();
    }
}
