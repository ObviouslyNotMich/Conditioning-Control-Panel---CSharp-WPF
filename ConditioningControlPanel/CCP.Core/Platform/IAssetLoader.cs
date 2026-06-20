namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Cross-platform asset/resource loader.
/// </summary>
public interface IAssetLoader
{
    Stream Open(Uri uri);
    bool Exists(Uri uri);
    Task<string> ReadTextAsync(Uri uri, CancellationToken cancellationToken = default);
}
