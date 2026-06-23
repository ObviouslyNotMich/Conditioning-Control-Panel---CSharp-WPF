using System;

namespace ConditioningControlPanel.Avalonia.Helpers;

/// <summary>
/// Shared HypnoTube URL validation used by the Avalonia head. Mirrors the WPF
/// <c>Helpers.HtUrlHelper</c> logic so Deeper catalogue eligibility and remote
/// commands agree on what counts as an HT video URL.
/// </summary>
public static class HtUrlHelper
{
    public static bool IsEligibleHtUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url == "*") return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        var host = uri.Host;
        if (!host.Equals("hypnotube.com", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".hypnotube.com", StringComparison.OrdinalIgnoreCase))
            return false;

        return IsVideoPath(uri.AbsolutePath);
    }

    private static bool IsVideoPath(string path)
    {
        const string Prefix = "/video/";
        if (!path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var rest = path.AsSpan(Prefix.Length);
        if (rest.IsEmpty) return false;

        int digitCount = 0;
        while (digitCount < rest.Length && char.IsDigit(rest[digitCount]))
            digitCount++;

        if (digitCount == 0) return false;

        // /video/123, /video/123/, /video/123-suffix, /video/123.html
        if (digitCount == rest.Length) return true;
        var next = rest[digitCount];
        if (next == '/' || next == '.' || next == '-') return true;

        // /video/some-title-123.html
        if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            var withoutExt = path.AsSpan(0, path.Length - 5);
            int dash = withoutExt.LastIndexOf('-');
            if (dash > Prefix.Length)
            {
                var id = withoutExt.Slice(dash + 1);
                if (!id.IsEmpty && AllDigits(id)) return true;
            }
        }

        return false;
    }

    private static bool AllDigits(ReadOnlySpan<char> span)
    {
        foreach (var c in span)
        {
            if (!char.IsDigit(c)) return false;
        }
        return !span.IsEmpty;
    }
}
