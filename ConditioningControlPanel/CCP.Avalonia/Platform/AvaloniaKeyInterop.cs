using System;
using System.Linq;
using Avalonia.Input;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Maps Avalonia <see cref="Key"/> names to Win32 virtual-key codes and
    /// string modifier descriptions to <see cref="ModifierKeys"/> flags.
    /// This is intentionally kept in the Avalonia head because the mapping is
    /// UI-framework specific; <see cref="IHotkeyProvider"/> consumes virtual keys.
    /// </summary>
    public static class AvaloniaKeyInterop
    {
        /// <summary>
        /// Tries to convert a key name (e.g. "T", "F2", "Escape", "NumPad0")
        /// into a Win32 virtual-key code suitable for <see cref="IHotkeyProvider"/>.
        /// </summary>
        public static bool TryGetVirtualKeyCode(string keyName, out int virtualKeyCode)
        {
            virtualKeyCode = 0;
            if (string.IsNullOrWhiteSpace(keyName)) return false;

            if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key))
                return false;

            virtualKeyCode = MapKeyToVirtualKeyCode(key);
            return virtualKeyCode != 0;
        }

        /// <summary>
        /// Parses a comma-separated modifier string (e.g. "Control" or "Control,Shift")
        /// into <see cref="ModifierKeys"/> flags.
        /// </summary>
        public static ModifierKeys ParseModifiers(string? modifiers)
        {
            if (string.IsNullOrWhiteSpace(modifiers)) return ModifierKeys.None;

            var result = ModifierKeys.None;
            foreach (var part in modifiers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Enum.TryParse<ModifierKeys>(part, ignoreCase: true, out var mod))
                    result |= mod;
            }
            return result;
        }

        private static int MapKeyToVirtualKeyCode(Key key)
        {
            // Alphanumeric ranges
            if (key >= Key.A && key <= Key.Z)
                return 0x41 + ((int)key - (int)Key.A);
            if (key >= Key.D0 && key <= Key.D9)
                return 0x30 + ((int)key - (int)Key.D0);
            if (key >= Key.NumPad0 && key <= Key.NumPad9)
                return 0x60 + ((int)key - (int)Key.NumPad0);
            if (key >= Key.F1 && key <= Key.F24)
                return 0x70 + ((int)key - (int)Key.F1);

            return key switch
            {
                Key.Enter or Key.Return => 0x0D,
                Key.Escape => 0x1B,
                Key.Space => 0x20,
                Key.Tab => 0x09,
                Key.Back => 0x08,
                Key.Insert => 0x2D,
                Key.Delete => 0x2E,
                Key.Home => 0x24,
                Key.End => 0x23,
                Key.PageUp or Key.Prior => 0x21,
                Key.PageDown or Key.Next => 0x22,
                Key.Left => 0x25,
                Key.Up => 0x26,
                Key.Right => 0x27,
                Key.Down => 0x28,
                Key.LeftShift => 0xA0,
                Key.RightShift => 0xA1,
                Key.LeftCtrl => 0xA2,
                Key.RightCtrl => 0xA3,
                Key.LeftAlt => 0xA4,
                Key.RightAlt => 0xA5,
                Key.LWin => 0x5B,
                Key.RWin => 0x5C,
                Key.Apps => 0x5D,
                Key.CapsLock or Key.Capital => 0x14,
                Key.NumLock => 0x90,
                Key.Scroll => 0x91,
                Key.PrintScreen or Key.Snapshot => 0x2C,
                Key.Pause => 0x13,
                Key.Sleep => 0x5F,
                Key.Multiply => 0x6A,
                Key.Add => 0x6B,
                Key.Subtract => 0x6D,
                Key.Decimal => 0x6E,
                Key.Divide => 0x6F,
                Key.Separator => 0x6C,
                Key.OemSemicolon or Key.Oem1 => 0xBA,
                Key.OemPlus => 0xBB,
                Key.OemComma => 0xBC,
                Key.OemMinus => 0xBD,
                Key.OemPeriod => 0xBE,
                Key.OemQuestion or Key.Oem2 => 0xBF,
                Key.OemTilde or Key.Oem3 => 0xC0,
                Key.OemOpenBrackets or Key.Oem4 => 0xDB,
                Key.OemPipe or Key.Oem5 => 0xDC,
                Key.OemCloseBrackets or Key.Oem6 => 0xDD,
                Key.OemQuotes or Key.Oem7 => 0xDE,
                Key.OemBackslash or Key.Oem102 => 0xE2,
                Key.BrowserBack => 0xA6,
                Key.BrowserForward => 0xA7,
                Key.BrowserRefresh => 0xA8,
                Key.BrowserStop => 0xA9,
                Key.BrowserSearch => 0xAA,
                Key.BrowserFavorites => 0xAB,
                Key.BrowserHome => 0xAC,
                Key.VolumeMute => 0xAD,
                Key.VolumeDown => 0xAE,
                Key.VolumeUp => 0xAF,
                Key.MediaNextTrack => 0xB0,
                Key.MediaPreviousTrack => 0xB1,
                Key.MediaStop => 0xB2,
                Key.MediaPlayPause => 0xB3,
                Key.LaunchMail => 0xB4,
                Key.SelectMedia => 0xB5,
                Key.LaunchApplication1 => 0xB6,
                Key.LaunchApplication2 => 0xB7,
                _ => 0
            };
        }
    }
}
