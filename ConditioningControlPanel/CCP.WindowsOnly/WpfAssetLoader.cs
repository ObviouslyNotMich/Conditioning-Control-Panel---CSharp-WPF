using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.WindowsOnly;

/// <summary>
/// WPF pack:// and file-based asset loader shim for <see cref="IAssetLoader"/>.
/// </summary>
public sealed class WpfAssetLoader : IAssetLoader
{
    public bool Exists(Uri uri)
    {
        if (uri.IsAbsoluteUri && uri.Scheme.Equals("pack", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return System.Windows.Application.GetResourceStream(uri) != null;
            }
            catch
            {
                return false;
            }
        }

        if (uri.IsAbsoluteUri && uri.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase))
        {
            return File.Exists(uri.LocalPath);
        }

        return File.Exists(Path.Combine(AppContext.BaseDirectory, uri.OriginalString));
    }

    public Stream Open(Uri uri)
    {
        if (uri.IsAbsoluteUri && uri.Scheme.Equals("pack", StringComparison.OrdinalIgnoreCase))
        {
            var info = System.Windows.Application.GetResourceStream(uri)
                ?? throw new FileNotFoundException($"Resource not found: {uri}");
            return info.Stream;
        }

        if (uri.IsAbsoluteUri && uri.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase))
        {
            return File.OpenRead(uri.LocalPath);
        }

        return File.OpenRead(Path.Combine(AppContext.BaseDirectory, uri.OriginalString));
    }

    public async Task<string> ReadTextAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        using var stream = Open(uri);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
