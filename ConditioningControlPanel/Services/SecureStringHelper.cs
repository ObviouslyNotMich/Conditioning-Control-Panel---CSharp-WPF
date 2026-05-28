using System;
using System.Security.Cryptography;
using System.Text;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Small helper around DPAPI to encrypt/decrypt sensitive strings (e.g. API keys)
    /// before storing them on disk. Uses DataProtectionScope.CurrentUser so the
    /// protected value is tied to the current Windows user profile.
    /// </summary>
    public static class SecureStringHelper
    {
        public static string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
            {
                return string.Empty;
            }

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(plainText);
                byte[] protectedData = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(protectedData);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SecureStringHelper.Protect failed");
                return plainText; // fall back to plain, better than data loss
            }
        }

        public static string Unprotect(string protectedText)
        {
            if (string.IsNullOrEmpty(protectedText))
            {
                return string.Empty;
            }

            try
            {
                byte[] data = Convert.FromBase64String(protectedText);
                byte[] unprotectedData = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(unprotectedData);
            }
            catch (FormatException)
            {
                // Not base64 or not DPAPI-protected yet — treat as plain text for
                // backward compatibility.
                return protectedText;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SecureStringHelper.Unprotect failed");
                return string.Empty;
            }
        }
    }
}
