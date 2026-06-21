using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Core.Services;

/// <summary>
/// Stub for the season-recap service referenced by AchievementProgress and SessionService.
/// Real implementation should track active days and streak peaks per season.
/// </summary>
public static class SeasonRecapService
{
    public static void MarkActiveToday()
    {
    }

    public static void TrackStreakPeak(int consecutiveDays)
    {
    }

    public static void IncrementSessionStarted()
    {
        var s = App.Settings?.Current;
        if (s == null) return;
        EnsureBucket(s);
        s.SeasonSessionsStarted += 1;
    }

    public static void TrackFeature(string featureKey)
    {
        var s = App.Settings?.Current;
        if (s == null) return;
        EnsureBucket(s);
        s.TrackSeasonFeature(featureKey);
    }

    private static void EnsureBucket(AppSettings s)
    {
        if (string.IsNullOrEmpty(s.SeasonStatsSeason))
            s.SeasonStatsSeason = DateTime.UtcNow.ToString("yyyy-MM");
    }

    /// <summary>
    /// Loads the most recent persisted season-recap snapshot, or null if none exists.
    /// Real implementation should read from disk/cloud.
    /// </summary>
    public static SeasonRecapSnapshot? LoadLatest()
    {
        // TODO: implement persisted snapshot storage for Avalonia/WPF.
        return null;
    }

    /// <summary>
    /// Returns whether any persisted season-recap snapshot exists.
    /// </summary>
    public static bool HasAnySnapshot()
    {
        // TODO: implement persisted snapshot storage for Avalonia/WPF.
        return false;
    }

    /// <summary>
    /// Snapshots the just-ended season and rolls the live season counters.
    /// Returns the snapshot, or null when there is no meaningful season data.
    /// </summary>
    public static SeasonRecapSnapshot? CaptureAndRollover(string currentSeason)
    {
        var s = App.Settings?.Current;
        if (s == null) return null;

        var snapshot = new SeasonRecapSnapshot
        {
            SeasonKey = s.SeasonStatsSeason ?? currentSeason,
            CapturedAtUtc = DateTime.UtcNow,
            HighestLevelEver = s.HighestLevelEver,
            SessionCount = s.SeasonSessionsStarted,
            SeasonMinutes = s.SeasonConditioningMinutes,
            AllTimeMinutes = s.TotalConditioningMinutes,
            LongestStreak = s.HighestStreak,
            DaysActive = 0, // TODO: track active days per season once the service is fully ported.
            SeasonLengthDays = SeasonNumbering.LengthDays(s.SeasonStatsSeason ?? currentSeason),
            FeatureUse = new Dictionary<string, int>(s.SeasonFeatureUse),
            FeaturesTotal = SeasonFeatureKeys.TotalCount
        };

        // Roll the live counters for the new season.
        s.SeasonStatsSeason = currentSeason;
        s.SeasonSessionsStarted = 0;
        s.SeasonConditioningMinutes = 0;
        s.SeasonFeatureUse = new Dictionary<string, int>();
        s.SeasonPeakRank = 0;
        s.SeasonPeakRankTotal = 0;

        App.Settings?.Save();

        // Return null when there is no meaningful data to show.
        if (snapshot.SeasonMinutes <= 0 && snapshot.SessionCount <= 0 && snapshot.DaysActive <= 0)
            return null;

        return snapshot;
    }
}
