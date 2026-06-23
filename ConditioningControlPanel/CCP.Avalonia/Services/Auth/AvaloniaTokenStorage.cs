using System;
using System.Reflection;
using System.Text;
using ConditioningControlPanel.Core.Platform;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Avalonia.Services.Auth;

/// <summary>
/// Cross-platform token and cached-state persistence for Avalonia auth providers.
/// Stores provider-specific token data and validation cache in the platform
/// <see cref="ISecretStore"/> (DPAPI/keychain/encrypted file) keyed by the provider prefix.
/// </summary>
public sealed class AvaloniaTokenStorage
{
    private readonly ISecretStore _secretStore;
    private readonly string _providerKey;
    private readonly ILogger<AvaloniaTokenStorage>? _logger;

    private string TokensKey => $"{_providerKey}:tokens";
    private string CacheKey => $"{_providerKey}:cache";

    public AvaloniaTokenStorage(ISecretStore secretStore, string providerKey, ILogger<AvaloniaTokenStorage>? logger = null)
    {
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _providerKey = providerKey ?? throw new ArgumentNullException(nameof(providerKey));
        _logger = logger;
    }

    /// <summary>
    /// Store strongly-typed token data.
    /// </summary>
    public void StoreTokens<TToken>(TToken tokens)
        where TToken : class
    {
        var json = JsonConvert.SerializeObject(tokens);
        _secretStore.Store(TokensKey, Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Retrieve strongly-typed token data, or null if missing/unreadable.
    /// </summary>
    public TToken? RetrieveTokens<TToken>()
        where TToken : class
    {
        var bytes = _secretStore.Retrieve(TokensKey);
        if (bytes == null || bytes.Length == 0)
            return null;

        try
        {
            var json = Encoding.UTF8.GetString(bytes);
            return JsonConvert.DeserializeObject<TToken>(json);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to deserialize {Provider} tokens", _providerKey);
            return null;
        }
    }

    /// <summary>
    /// True if non-expired token data is present.
    /// </summary>
    public bool HasValidTokens<TToken>()
        where TToken : class
    {
        var tokens = RetrieveTokens<TToken>();
        if (tokens == null)
            return false;

        var isExpiredProperty = typeof(TToken).GetProperty("IsExpired", BindingFlags.Public | BindingFlags.Instance);
        if (isExpiredProperty?.GetValue(tokens) is bool expired)
            return !expired;

        return true;
    }

    /// <summary>
    /// Delete stored token data.
    /// </summary>
    public void ClearTokens()
    {
        try
        {
            _secretStore.Delete(TokensKey);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to clear {Provider} tokens", _providerKey);
        }
    }

    /// <summary>
    /// Store strongly-typed cached validation state.
    /// </summary>
    public void StoreCachedState<TCached>(TCached state)
        where TCached : class
    {
        var json = JsonConvert.SerializeObject(state);
        _secretStore.Store(CacheKey, Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Retrieve strongly-typed cached validation state, or null if missing/unreadable.
    /// </summary>
    public TCached? RetrieveCachedState<TCached>()
        where TCached : class
    {
        var bytes = _secretStore.Retrieve(CacheKey);
        if (bytes == null || bytes.Length == 0)
            return null;

        try
        {
            var json = Encoding.UTF8.GetString(bytes);
            return JsonConvert.DeserializeObject<TCached>(json);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to deserialize {Provider} cached state", _providerKey);
            return null;
        }
    }

    /// <summary>
    /// Delete cached validation state.
    /// </summary>
    public void ClearCachedState()
    {
        try
        {
            _secretStore.Delete(CacheKey);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to clear {Provider} cached state", _providerKey);
        }
    }
}
