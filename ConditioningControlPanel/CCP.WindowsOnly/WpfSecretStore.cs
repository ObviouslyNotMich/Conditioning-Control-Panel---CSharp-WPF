using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.WindowsOnly;

/// <summary>
/// DPAPI-backed shim for <see cref="ISecretStore"/>.
/// </summary>
public sealed class WpfSecretStore : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CCP_WindowsOnly_SecretStore_v1");

    private static string StorageDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ConditioningControlPanel",
        "secrets");

    public void Store(string key, byte[] value)
    {
        var path = GetPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encrypted = ProtectedData.Protect(value, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, encrypted);
    }

    public byte[]? Retrieve(string key)
    {
        var path = GetPath(key);
        if (!File.Exists(path))
            return null;

        try
        {
            var encrypted = File.ReadAllBytes(path);
            return ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        }
        catch
        {
            return null;
        }
    }

    public void Delete(string key)
    {
        var path = GetPath(key);
        if (File.Exists(path))
            File.Delete(path);
    }

    private static string GetPath(string key)
    {
        var safeKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(key));
        return Path.Combine(StorageDirectory, $"{safeKey}.bin");
    }
}
