using System.Globalization;
using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Core.Services.BugReport;

/// <summary>
/// Pure static scrubber for bug-report payloads. Unit-testable in isolation.
/// Applies a fixed set of regex rules to remove PII and normalize timestamps.
/// </summary>
public static class LogScrubber
{
    // C:\Users\<name>\...  or  C:/Users/<name>/...
    private static readonly Regex UserPathRegex = new(
        @"(?i)([A-Z]:[\\/])Users[\\/]([^\\/\r\n""']+)",
        RegexOptions.Compiled);

    private static readonly Regex EmailRegex = new(
        @"\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex JsonTokenRegex = new(
        @"(?i)(""?(?:access_token|refresh_token|auth_token|api_key|apikey|authorization|bearer_token|x-auth-token|x-admin-token|client_secret|patreon_token|discord_token)""?\s*[:=]\s*)""?[A-Z0-9._~+/=\-]{8,}""?",
        RegexOptions.Compiled);

    private static readonly Regex BearerRegex = new(
        @"(?i)\bBearer\s+[A-Z0-9._~+/=\-]{16,}\b",
        RegexOptions.Compiled);

    private static readonly Regex DiscordBotTokenRegex = new(
        @"\b[MNO][A-Za-z0-9_\-]{23,25}\.[A-Za-z0-9_\-]{6}\.[A-Za-z0-9_\-]{27,}\b",
        RegexOptions.Compiled);

    private static readonly Regex AppDataLiteralRegex = new(
        @"(?i)%(?:LOCALAPPDATA|APPDATA|USERPROFILE)%",
        RegexOptions.Compiled);

    private static readonly Regex TimestampRegex = new(
        @"\b(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2}):(\d{2})(?:\.\d+)?(Z|[+\-]\d{2}:?\d{2})?\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Scrub a string, returning the redacted text plus per-category counts.
    /// </summary>
    public static (string Scrubbed, ScrubberCounts Counts) Scrub(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return (string.Empty, ScrubberCounts.Empty);

        int paths = 0, emails = 0, tokens = 0, appdata = 0;

        var step1 = UserPathRegex.Replace(input, m =>
        {
            paths++;
            return $"{m.Groups[1].Value}Users\\<redacted>";
        });

        var step2 = EmailRegex.Replace(step1, _ =>
        {
            emails++;
            return "[email redacted]";
        });

        var step3 = JsonTokenRegex.Replace(step2, m =>
        {
            tokens++;
            return $"{m.Groups[1].Value}[token redacted]";
        });

        var step4 = BearerRegex.Replace(step3, _ =>
        {
            tokens++;
            return "Bearer [token redacted]";
        });

        var step5 = DiscordBotTokenRegex.Replace(step4, _ =>
        {
            tokens++;
            return "[discord token redacted]";
        });

        var step6 = AppDataLiteralRegex.Replace(step5, _ =>
        {
            appdata++;
            return "%APPDATA%";
        });

        var step7 = TimestampRegex.Replace(step6, m =>
        {
            if (TryNormalizeTimestamp(m, out var normalized))
                return normalized;
            return m.Value;
        });

        return (step7, new ScrubberCounts(paths, emails, tokens, appdata));
    }

    private static bool TryNormalizeTimestamp(Match m, out string normalized)
    {
        normalized = string.Empty;
        try
        {
            int year = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            int month = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            int day = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            int hour = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            int minute = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);

            DateTimeOffset dto;
            var zone = m.Groups[7].Value;
            if (!string.IsNullOrEmpty(zone))
            {
                if (!DateTimeOffset.TryParse(m.Value, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out dto))
                    return false;
            }
            else
            {
                var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local);
                dto = new DateTimeOffset(local).ToUniversalTime();
            }

            var utc = dto.ToUniversalTime();
            var rounded = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
            normalized = rounded.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
