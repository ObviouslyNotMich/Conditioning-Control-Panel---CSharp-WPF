using System.Security.Cryptography;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// In-memory secret store fallback. This is intentionally simple: portable code should not
/// rely on OS keychain APIs until a platform-specific implementation is wired in.
/// </summary>
public sealed class AvaloniaSecretStore : ISecretStore
{
    private readonly Dictionary<string, byte[]> _store = new();

    public void Store(string key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        _store[key] = value.ToArray();
    }

    public byte[]? Retrieve(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _store.TryGetValue(key, out var value) ? value.ToArray() : null;
    }

    public void Delete(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _store.Remove(key);
    }
}
