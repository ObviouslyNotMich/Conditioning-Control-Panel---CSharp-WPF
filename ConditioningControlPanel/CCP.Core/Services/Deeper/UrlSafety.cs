using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Core.Services.Deeper;

/// <summary>
/// Defensive URL/path checks for the Deeper editor. The editor accepts user-
/// supplied URLs and file paths from shared .ccpenh.json files; both are untrusted
/// and need to be filtered before any network or filesystem hit.
/// </summary>
public static class UrlSafety
{
    /// <summary>True when the parsed URI's host equals one of <paramref name="domains"/>
    /// or is a subdomain of one. Comparison is case-insensitive on the parsed host.</summary>
    public static bool HostMatches(Uri uri, params string[] domains)
    {
        if (uri == null || domains == null) return false;
        var host = uri.Host;
        if (string.IsNullOrEmpty(host)) return false;
        foreach (var d in domains)
        {
            if (string.IsNullOrEmpty(d)) continue;
            if (host.Equals(d, StringComparison.OrdinalIgnoreCase)) return true;
            if (host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Returns "host/path" with the query string and fragment removed.</summary>
    public static string RedactUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "[invalid url]";
        var host = uri.Host;
        var path = uri.AbsolutePath;
        if (string.IsNullOrEmpty(path) || path == "/") return host;
        return host + path;
    }

    /// <summary>Rejects non-https URLs and any host that resolves to a loopback,
    /// link-local, private, or unique-local-address.</summary>
    public static async Task<bool> IsSafePublicHttpsAsync(Uri uri, CancellationToken ct)
    {
        if (uri == null) return false;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        if (string.IsNullOrEmpty(uri.Host)) return false;

        try
        {
            IPAddress[] addrs;
            if (IPAddress.TryParse(uri.Host, out var literal))
            {
                addrs = new[] { literal };
            }
            else
            {
                addrs = await Dns.GetHostAddressesAsync(uri.Host, ct).ConfigureAwait(false);
            }
            if (addrs == null || addrs.Length == 0) return false;
            foreach (var ip in addrs)
            {
                if (IsPrivateOrReservedIp(ip)) return false;
            }
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch
        {
            return false;
        }
    }

    /// <summary>True for any IP we never want the editor to talk to.</summary>
    public static bool IsPrivateOrReservedIp(IPAddress ip)
    {
        if (ip == null) return true;
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 10) return true;
            if (b[0] == 172 && (b[1] & 0xF0) == 16) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            if (b[0] == 169 && b[1] == 254) return true;
            if (b[0] == 0) return true;
            if (b[0] == 100 && (b[1] & 0xC0) == 64) return true;
            if (b[0] >= 224 && b[0] <= 239) return true;
            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal) return true;
            if (ip.IsIPv6SiteLocal) return true;
            if (ip.IsIPv6Multicast) return true;
            var b = ip.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return true;
            if (ip.IsIPv4MappedToIPv6)
            {
                return IsPrivateOrReservedIp(ip.MapToIPv4());
            }
            return false;
        }

        return true;
    }

    /// <summary>
    /// Build a SocketsHttpHandler whose ConnectCallback rejects any private/reserved IP.
    /// </summary>
    public static SocketsHttpHandler CreateGuardedHandler()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
        handler.ConnectCallback = async (ctx, ct) =>
        {
            var ep = ctx.DnsEndPoint;
            IPAddress[] addrs;
            if (IPAddress.TryParse(ep.Host, out var literal))
            {
                addrs = new[] { literal };
            }
            else
            {
                addrs = await Dns.GetHostAddressesAsync(ep.Host, ct).ConfigureAwait(false);
            }
            if (addrs == null || addrs.Length == 0)
                throw new IOException($"DNS resolution failed for {ep.Host}.");
            foreach (var ip in addrs)
            {
                if (IsPrivateOrReservedIp(ip))
                    throw new IOException($"Refusing to connect to private/reserved IP {ip} for {ep.Host}.");
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(addrs, ep.Port, ct).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        };
        return handler;
    }

    /// <summary>True when <paramref name="path"/> is a relative, well-formed local
    /// path that resolves under <paramref name="baseDir"/>.</summary>
    public static bool TryResolveLocalPath(string? path, string? baseDir, out string resolved)
    {
        resolved = "";
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (string.IsNullOrWhiteSpace(baseDir)) return false;

        if (path.StartsWith("\\\\", StringComparison.Ordinal)) return false;
        if (path.StartsWith("//", StringComparison.Ordinal)) return false;
        if (System.IO.Path.IsPathRooted(path)) return false;

        try
        {
            var combined = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, path));
            var baseFull = System.IO.Path.GetFullPath(baseDir);
            if (!baseFull.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                baseFull += System.IO.Path.DirectorySeparatorChar;
            if (!combined.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase)) return false;
            resolved = combined;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True when the supplied absolute local path is safe to open directly.</summary>
    public static bool IsSafeLocalAbsolute(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (path.StartsWith("\\\\", StringComparison.Ordinal)) return false;
        if (path.StartsWith("//", StringComparison.Ordinal)) return false;
        if (path.IndexOf("://", StringComparison.Ordinal) >= 0) return false;
        try
        {
            var full = System.IO.Path.GetFullPath(path);
            if (full.StartsWith("\\\\", StringComparison.Ordinal)) return false;
            return System.IO.Path.IsPathRooted(full);
        }
        catch
        {
            return false;
        }
    }
}
