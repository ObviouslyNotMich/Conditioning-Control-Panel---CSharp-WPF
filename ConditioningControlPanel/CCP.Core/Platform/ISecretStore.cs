namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Platform-specific secure credential storage.
/// </summary>
public interface ISecretStore
{
    void Store(string key, byte[] value);
    byte[]? Retrieve(string key);
    void Delete(string key);
}
