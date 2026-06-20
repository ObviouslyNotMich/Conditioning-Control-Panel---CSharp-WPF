using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Avalonia asset loader shim using Avalonia's <see cref="Avalonia.Platform.AssetLoader"/>.
/// </summary>
public sealed class AvaloniaAssetLoader : IAssetLoader
{
    public Stream Open(Uri uri) => global::Avalonia.Platform.AssetLoader.Open(uri);

    public bool Exists(Uri uri) => global::Avalonia.Platform.AssetLoader.Exists(uri);

    public async Task<string> ReadTextAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        using var stream = Open(uri);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }
}
