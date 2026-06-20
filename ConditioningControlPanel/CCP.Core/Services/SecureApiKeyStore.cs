namespace ConditioningControlPanel.Core.Services;

/// <summary>
/// Stub for secure OpenRouter API key storage.
/// Desktop heads should wire this to DPAPI/keychain/libsecret; mobile heads to secure enclaves.
/// </summary>
public static class SecureApiKeyStore
{
    public static string? Retrieve() => null;

    public static void Store(string? value)
    {
    }
}
