using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Models;

namespace ConditioningControlPanel.Avalonia.ViewModels;

/// <summary>One badge in the card's "Conditioning Used" row.</summary>
public class FeatureBadgeViewModel
{
    public string Label { get; init; } = "";
    public int Count { get; init; }
    /// <summary>Resolved URI of the dashboard feature icon.</summary>
    public string? ImagePath { get; init; }
}

/// <summary>
/// Avalonia-compatible presentation model for the Season Recap card.
/// Adapted from the WPF SeasonRecapViewModel; uses Core models/localization
/// and returns bool visibility flags so bindings work with Avalonia's
/// BoolToVisibilityConverter.
///
/// The three "count-up" figures (season time, all-time time, peak rank) are
/// exposed both as final formatted strings and as numeric targets the control
/// animates toward on reveal.
/// </summary>
public class SeasonRecapViewModel
{
    private readonly SeasonRecapSnapshot _s;

    public SeasonRecapViewModel(SeasonRecapSnapshot snapshot)
    {
        _s = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    /// <summary>Mode-sensitive backdrop art for the card.</summary>
    public string BackgroundImagePath => RecapBackgrounds.ForMod(null); // TODO: mod-aware background once IModService exposes ActiveModId.

    // ---------- header ----------
    public int SeasonNumber => SeasonNumbering.ToSeasonNumber(_s.SeasonKey);
    public int NextSeasonNumber => SeasonNumber + 1;
    public string SeasonLabel => Loc.Get("recap_header_season");
    public string SeasonNumberText => SeasonNumber >= 0 ? SeasonNumber.ToString("00") : "--";
    public string StatusText => Loc.Get("recap_status_complete");

    public string DateRangeText
    {
        get
        {
            var (start, end) = SeasonNumbering.DateRange(_s.SeasonKey);
            if (start == DateTime.MinValue) return "";
            string fmt(DateTime d) => d.ToString("MMM dd", CultureInfo.InvariantCulture).ToLowerInvariant();
            return $"{fmt(start)} - {fmt(end)} · {start.Year}";
        }
    }

    // ---------- identity ----------
    public string Handle => string.IsNullOrWhiteSpace(_s.Handle) ? Loc.Get("recap_default_handle") : _s.Handle;

    public string TitleText => Loc.Get(TitleTiers.Resolve(AllTimeHours, _s.Percentile));

    // Status pills (shown under the hero).
    public string SupporterLabel => Loc.Get("recap_badge_supporter");
    public string OgLabel => Loc.Get("recap_badge_og");
    public bool IsSupporter => _s.IsSupporter;
    public bool IsOg => _s.IsOg;
    public bool ShowStatusRow => _s.IsSupporter || _s.IsOg;

    // ---------- hero (season time + all-time) ----------
    public double SeasonMinutes => _s.SeasonMinutes;
    public double AllTimeMinutes => _s.AllTimeMinutes;
    public double AllTimeHours => _s.AllTimeMinutes / 60.0;

    public string SeasonTimeText => FormatHm(_s.SeasonMinutes);
    public string AllTimeText => FormatHm(_s.AllTimeMinutes);
    public string HeroLabel => Loc.Get("recap_hero_label");
    public string AllTimeLabel => Loc.Get("recap_alltime_label");
    public string SessionsSubline => Loc.GetF("recap_sessions_subline", _s.SessionCount);

    // ---------- stat grid ----------
    public bool HasRank => _s.PeakRank > 0;
    public string PeakRankText => _s.PeakRank > 0 ? $"#{_s.PeakRank}" : "—";
    public int PeakRankTarget => _s.PeakRank;
    public string PeakRankOfText => _s.PeakRank > 0 && _s.PeakRankTotal > 0
        ? Loc.GetF("recap_rank_of", _s.PeakRankTotal.ToString("N0"))
        : "";
    public string PercentileText => _s.Percentile > 0 ? Loc.GetF("recap_top_percent", _s.Percentile) : "—";
    public string DaysActiveText => _s.DaysActive.ToString();
    public string DaysActiveOfText => _s.SeasonLengthDays > 0 ? $"/ {_s.SeasonLengthDays}" : "";
    public string LongestStreakText => _s.LongestStreak.ToString();
    public string StreakUnitText => Loc.Get("recap_streak_days");

    public string StatPeakRankLabel => Loc.Get("recap_stat_peak_rank");
    public string StatPercentileLabel => Loc.Get("recap_stat_percentile");
    public string StatDaysActiveLabel => Loc.Get("recap_stat_days_active");
    public string StatStreakLabel => Loc.Get("recap_stat_longest_streak");

    // ---------- badge row ----------
    public string BadgesTitle => Loc.Get("recap_badges_title");

    public int FeaturesUsedCount => _s.FeatureUse.Count(kv => kv.Value > 0);
    public int FeaturesTotal => _s.FeaturesTotal > 0 ? _s.FeaturesTotal : SeasonFeatureKeys.TotalCount;
    public string FeaturesUsedText => Loc.GetF("recap_features_used", FeaturesUsedCount, FeaturesTotal);

    public IReadOnlyList<FeatureBadgeViewModel> TopBadges
    {
        get
        {
            return _s.FeatureUse
                .Where(kv => kv.Value > 0)
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(6)
                .Select(kv =>
                {
                    var def = SeasonFeatureKeys.Find(kv.Key);
                    return new FeatureBadgeViewModel
                    {
                        Label = def != null ? Loc.Get(def.LabelLocKey) : kv.Key,
                        Count = kv.Value,
                        ImagePath = def != null ? ResolveAssetUri(def.ImagePath) : null,
                    };
                })
                .ToList();
        }
    }

    // ---------- verdict ----------
    public string VerdictEyebrow => Loc.Get("recap_verdict_eyebrow");

    private string VerdictKey
    {
        get
        {
            var pct = _s.Percentile;
            if (pct > 0 && pct <= 2) return "recap_verdict_elite";
            if (pct > 0 && pct <= 10) return "recap_verdict_strong";
            if (AllTimeHours >= 50 || _s.SeasonMinutes >= 600) return "recap_verdict_mid";
            return "recap_verdict_gentle";
        }
    }
    public string VerdictBefore => SplitVerdict().before;
    public string VerdictName => Handle;
    public string VerdictAfter => SplitVerdict().after;

    private (string before, string after) SplitVerdict()
    {
        var t = Loc.Get(VerdictKey);
        const string token = "{0}";
        var idx = t.IndexOf(token, StringComparison.Ordinal);
        if (idx < 0) return (t, "");
        return (t.Substring(0, idx), t.Substring(idx + token.Length));
    }

    // ---------- reset / brand ----------
    public int AllTimeHoursRounded => (int)Math.Round(AllTimeHours, MidpointRounding.AwayFromZero);
    public string ResetBefore => Loc.GetF("recap_reset_before", SeasonNumberText);
    public string ResetBold => Loc.GetF("recap_reset_bold", AllTimeHoursRounded);
    public string ResetAfter => Loc.GetF("recap_reset_after", NextSeasonNumber.ToString("00"));
    public string ResetCta => Loc.Get("recap_reset_cta");
    public string BrandText => Loc.Get("recap_brand");

    // ---------- share prefill ----------
    public string SharePrefillText => Loc.GetF(
        "recap_share_prefill",
        SeasonNumberText,
        FormatHmCompact(_s.SeasonMinutes),
        FormatHmCompact(_s.AllTimeMinutes),
        _s.Percentile > 0 ? _s.Percentile.ToString() : "?");

    public string SuggestedFileName => $"cclabs-season-{_s.SeasonKey}.png";

    // ---------- formatting helpers ----------
    public static string FormatHm(double totalMinutes)
    {
        var t = Math.Max(0, totalMinutes);
        var hours = (int)(t / 60);
        var mins = (int)(t % 60);
        return Loc.GetF("recap_time_hm", hours, mins);
    }

    public static string FormatHmCompact(double totalMinutes)
    {
        var hours = (int)Math.Round(Math.Max(0, totalMinutes) / 60.0, MidpointRounding.AwayFromZero);
        return $"{hours}h";
    }

    /// <summary>
    /// Resolves a relative Resources/ path to a URI Avalonia can load.
    /// Pack URIs are left as-is for a converter to handle; mod overrides are
    /// not yet available cross-platform.
    /// </summary>
    private static string ResolveAssetUri(string resourcePath)
    {
        if (string.IsNullOrEmpty(resourcePath)) return "";
        // TODO: wire mod override resolution once IModService exposes installed paths.
        return $"pack://application:,,,/Resources/{resourcePath.Replace('\\', '/')}";
    }
}
