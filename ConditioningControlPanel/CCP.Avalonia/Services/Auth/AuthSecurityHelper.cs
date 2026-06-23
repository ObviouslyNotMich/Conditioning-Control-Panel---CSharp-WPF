using System;
using System.Security.Cryptography;
using System.Text;

namespace ConditioningControlPanel.Avalonia.Services.Auth;

internal static class AuthSecurityHelper
{
    /// <summary>
    /// Constant-time string comparison to mitigate timing attacks on OAuth state.
    /// </summary>
    public static bool SecureCompare(string? a, string? b)
    {
        if (a == null || b == null)
            return a == b;

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    }
}
