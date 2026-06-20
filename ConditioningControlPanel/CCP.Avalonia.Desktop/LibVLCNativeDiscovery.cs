using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace ConditioningControlPanel.Avalonia.Desktop;

/// <summary>
/// Discovers the native LibVLC libraries on Linux and macOS and initializes
/// LibVLCSharp with an explicit path when found.
/// </summary>
/// <remarks>
/// Windows relies on the <c>VideoLAN.LibVLC.Windows</c> NuGet package, which copies
/// native libraries to the output directory automatically, so this helper mainly
/// targets Linux/macOS desktop heads where LibVLC is typically provided by the
/// system package manager or a runtime-specific NuGet package.
/// </remarks>
public static class LibVLCNativeDiscovery
{
    /// <summary>
    /// Attempts to locate native LibVLC libraries and initializes LibVLCSharp.
    /// If no native library is found, initialization is skipped so the Linux head
    /// can still build and start; consumers must handle the missing LibVLC instance
    /// gracefully (e.g. by disabling video playback).
    /// </summary>
    public static void Initialize()
    {
        if (TryDiscoverNativePath(out var nativePath))
        {
            try
            {
                global::LibVLCSharp.Shared.Core.Initialize(nativePath);
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LibVLCNativeDiscovery] Core.Initialize({nativePath}) failed: {ex.Message}");
            }
        }

        // Last resort: let LibVLCSharp try its default discovery. This usually
        // succeeds on Windows (package-provided libs) and on Linux/macOS when
        // libvlc is installed in a standard system location and the loader can
        // find it without an explicit path.
        try
        {
            global::LibVLCSharp.Shared.Core.Initialize();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LibVLCNativeDiscovery] Default Core.Initialize() failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Searches runtime-specific output folders and standard system paths for
    /// the LibVLC native shared library.
    /// </summary>
    private static bool TryDiscoverNativePath(out string? nativePath)
    {
        nativePath = null;

        var baseDir = AppContext.BaseDirectory;
        var rid = RuntimeInformation.RuntimeIdentifier;
        var architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

        // 1. NuGet runtimes folder (e.g. VideoLAN.LibVLC.Linux / VideoLAN.LibVLC.Mac).
        var runtimeCandidates = new List<string>();

        if (!string.IsNullOrEmpty(rid))
        {
            runtimeCandidates.Add(Path.Combine(baseDir, "runtimes", rid, "native"));
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            runtimeCandidates.Add(Path.Combine(baseDir, "runtimes", $"linux-{architecture}", "native"));
            runtimeCandidates.Add(Path.Combine(baseDir, "runtimes", "linux-x64", "native"));
            runtimeCandidates.Add(Path.Combine(baseDir, "runtimes", "linux-arm64", "native"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            runtimeCandidates.Add(Path.Combine(baseDir, "runtimes", $"osx-{architecture}", "native"));
            runtimeCandidates.Add(Path.Combine(baseDir, "runtimes", "osx-x64", "native"));
            runtimeCandidates.Add(Path.Combine(baseDir, "runtimes", "osx-arm64", "native"));
        }

        foreach (var candidate in runtimeCandidates)
        {
            if (Directory.Exists(candidate) && ContainsLibVLC(candidate))
            {
                nativePath = candidate;
                return true;
            }
        }

        // 2. System paths (Linux distributions and macOS Homebrew/MacPorts).
        var systemPaths = new List<string>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            systemPaths.AddRange(new[]
            {
                "/usr/lib/x86_64-linux-gnu",
                "/usr/lib/aarch64-linux-gnu",
                "/usr/lib64",
                "/usr/lib",
                "/usr/local/lib",
                "/usr/local/lib64",
                "/lib/x86_64-linux-gnu",
                "/lib/aarch64-linux-gnu",
                "/lib64",
                "/opt/vlc/lib",
            });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            systemPaths.AddRange(new[]
            {
                "/opt/homebrew/lib",
                "/usr/local/lib",
                "/usr/lib",
                "/opt/local/lib",
                "/Applications/VLC.app/Contents/MacOS/lib",
            });
        }

        foreach (var candidate in systemPaths)
        {
            if (Directory.Exists(candidate) && ContainsLibVLC(candidate))
            {
                nativePath = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true if the directory contains a LibVLC shared library file.
    /// </summary>
    private static bool ContainsLibVLC(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory).Any(file =>
            {
                var name = Path.GetFileName(file).ToLowerInvariant();

                if (!name.StartsWith("libvlc"))
                    return false;

                // Linux: libvlc.so, libvlc.so.5, libvlc.so.5.6.0
                // macOS: libvlc.dylib, libvlc.5.dylib
                return name.EndsWith(".so") ||
                       name.Contains(".so.") ||
                       name.EndsWith(".dylib") ||
                       name.Contains(".dylib.");
            });
        }
        catch
        {
            return false;
        }
    }
}
