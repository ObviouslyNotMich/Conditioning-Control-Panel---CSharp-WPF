using System;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Lazily exposes the shared <see cref="LibVLC"/> instance registered in DI.
/// Consumers that do not actually play media never pay the cost of loading the
/// native LibVLC runtime and its plugins.
/// </summary>
public interface ILibVlcProvider
{
    /// <summary>True when the LibVLC native runtime has already been initialized.</summary>
    bool IsValueCreated { get; }

    /// <summary>The shared LibVLC instance. Created on first access.</summary>
    LibVLC Value { get; }
}

/// <summary>
/// Provider implementation. Keeps LibVLC initialization lazy while still resolving
/// the same singleton instance that direct <see cref="LibVLC"/> consumers use.
/// </summary>
public sealed class LibVlcProvider : ILibVlcProvider
{
    private readonly Lazy<LibVLC> _lazy;

    public LibVlcProvider(IServiceProvider services)
    {
        _lazy = new Lazy<LibVLC>(() => services.GetRequiredService<LibVLC>());
    }

    public bool IsValueCreated => _lazy.IsValueCreated;

    public LibVLC Value => _lazy.Value;
}
