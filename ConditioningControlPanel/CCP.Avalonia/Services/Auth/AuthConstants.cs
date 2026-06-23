using System;
using System.Reflection;

namespace ConditioningControlPanel.Avalonia.Services.Auth;

internal static class AuthConstants
{
    public const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";
    public const int CacheHours = 24;
    public const int OAuthTimeoutMinutes = 5;

    public const int PatreonCallbackPort = 47832;
    public const int DiscordCallbackPort = 47833;
    public const int SubscribeStarCallbackPort = 47834;

    public static string ClientVersion
    {
        get
        {
            var assembly = Assembly.GetExecutingAssembly();
            var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(infoVersion))
            {
                var plus = infoVersion.IndexOf('+');
                var clean = plus > 0 ? infoVersion.Substring(0, plus) : infoVersion;
                if (System.Version.TryParse(clean, out _))
                    return clean;
            }

            var version = assembly.GetName().Version;
            if (version != null)
                return $"{version.Major}.{version.Minor}.{version.Build}";

            return "6.1.6";
        }
    }
}
