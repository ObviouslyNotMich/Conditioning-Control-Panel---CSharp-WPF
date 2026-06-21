using System;
using System.Diagnostics;
using System.IO;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Win32;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows;

/// <summary>
/// Windows startup registration using the HKCU Run registry key.
/// </summary>
public sealed class WindowsStartupRegistration : IStartupRegistration
{
    private const string RegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ConditioningControlPanel";

    public bool IsRegistered
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, false);
                var value = key?.GetValue(AppName);
                return value is string s && !string.IsNullOrEmpty(s);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WindowsStartupRegistration.IsRegistered failed: {ex.Message}");
                return false;
            }
        }
    }

    public void SetRegistered(bool value)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, true);
            if (key == null) return;

            if (value)
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                    exePath = Path.Combine(AppContext.BaseDirectory, $"{AppName}.exe");

                if (File.Exists(exePath))
                    key.SetValue(AppName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(AppName, false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WindowsStartupRegistration.SetRegistered failed: {ex.Message}");
        }
    }
}
