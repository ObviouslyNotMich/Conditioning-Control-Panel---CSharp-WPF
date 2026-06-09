using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ConditioningControlPanel.Services.Chaos;

public enum ChaosDifficulty { Easy, Medium, Hard, Extreme }

public enum ChaosRarity { Common, Uncommon, Rare }

/// <summary>Knobs that drive a single Chaos run. Built from the Lab card + AppSettings.</summary>
public sealed class ChaosRunConfig
{
    public ChaosDifficulty Difficulty { get; set; } = ChaosDifficulty.Easy;
    public int DurationSec { get; set; } = 180;
    public int WaveCount { get; set; } = 5;
    public List<PayloadConfig> Payloads { get; set; } = EffectPayloadFactory.DefaultConfig();

    // ---- setup-window config ----
    public int StartingShields { get; set; } = 3;
    /// <summary>Null = Mixed (per-variant default motion); otherwise force this motion.</summary>
    public ChaosMotion? MotionOverride { get; set; }
    /// <summary>Enabled variant ids. Null = all enabled.</summary>
    public List<string>? EnabledVariants { get; set; }
    public bool ScreenShakeEnabled { get; set; } = true;
    public bool ColorFlashesEnabled { get; set; } = true;
    public double ShakeIntensity { get; set; } = 0.8;
    public double EffectIntensity { get; set; } = 0.85;
    public bool BoonDraftEnabled { get; set; } = true;
    public bool AllowCurses { get; set; } = true;
    /// <summary>Darters (bouncing-flash catch targets) spawn during the run when true.</summary>
    public bool DartersEnabled { get; set; } = true;
    /// <summary>If a boon draft is left untouched this many seconds, auto-take the SKIP (+1 shield) and
    /// resume so an unattended run never freezes forever. 0 disables (wait indefinitely).</summary>
    public int DraftAutoResumeSec { get; set; } = ChaosModeService.DraftAutoResumeSecDefault;
    /// <summary>Opt-in ambient mode (OFF by default): remap intrusive detonations (video / HT link) to a
    /// lighter payload (bouncing text / gif cascade) so a background run is never yanked fullscreen.</summary>
    public bool AmbientMode { get; set; } = false;

    // ---- meta-progression knobs (set by ChaosMeta.ApplyTo from owned upgrades) ----
    // Every field has a neutral default so an unmodified run is byte-for-byte unchanged.
    /// <summary>Multiplies live-bubble fuse time; seeds <see cref="ChaosRunState.FuseTimeMult"/>.</summary>
    public double FuseTimeMult { get; set; } = 1.0;
    /// <summary>Magnet (near-click defuse); seeds <see cref="ChaosRunState.MagnetEnabled"/>.</summary>
    public bool MagnetEnabled { get; set; } = false;
    /// <summary>Base of the multiplier stack; seeds <see cref="ChaosRunState.BaseMult"/>.</summary>
    public double BaseMult { get; set; } = 1.0;
    /// <summary>Extra benign-pop scoring when true (golden_touch).</summary>
    public bool GoldenTouchBaseline { get; set; } = false;
    /// <summary>Scales banked Sparks at run end (spark_gain).</summary>
    public double SparkGainMult { get; set; } = 1.0;
    /// <summary>Boon-draft option count (draft4 → 4). Default 3 = current behavior.</summary>
    public int DraftChoices { get; set; } = 3;
    /// <summary>Added to the run's computed concurrent-bubble cap (max_bubbles).</summary>
    public int MaxBubblesBonus { get; set; } = 0;
    /// <summary>Hit-test scale — runtime deferred (bigger_hitboxes).</summary>
    public double HitboxScale { get; set; } = 1.0;
    /// <summary>Seconds between passive shield regen — runtime deferred (shield_recharge); 0 = off.</summary>
    public double ShieldRechargeSeconds { get; set; } = 0;
    /// <summary>Scales detonation cost — runtime deferred (take_more).</summary>
    public double DetonationPenaltyMult { get; set; } = 1.0;

    public static ChaosRunConfig FromSettings()
    {
        var s = App.Settings?.Current;
        var cfg = new ChaosRunConfig();
        if (s == null) { ChaosMeta.ApplyTo(cfg); return cfg; }
        cfg.Difficulty = Enum.TryParse<ChaosDifficulty>(s.ChaosDifficulty, out var d) ? d : ChaosDifficulty.Easy;
        cfg.DurationSec = Math.Clamp(s.ChaosRunDurationSec, 60, 900);
        cfg.WaveCount = Math.Clamp(s.ChaosWaveCount, 1, 12);
        cfg.StartingShields = Math.Clamp(s.ChaosStartingShields, 0, 5);
        cfg.MotionOverride = Enum.TryParse<ChaosMotion>(s.ChaosMotionMode, out var m) ? m : (ChaosMotion?)null;
        cfg.EnabledVariants = s.ChaosEnabledVariants;   // null = all
        cfg.ScreenShakeEnabled = s.ChaosScreenShakeEnabled;
        cfg.ColorFlashesEnabled = s.ChaosColorFlashesEnabled;
        cfg.ShakeIntensity = Math.Clamp(s.ChaosShakeIntensity, 0.0, 1.0);
        cfg.EffectIntensity = Math.Clamp(s.ChaosEffectIntensity, 0.2, 1.5);
        cfg.BoonDraftEnabled = s.ChaosBoonDraftEnabled;
        cfg.AllowCurses = s.ChaosAllowCurses;
        cfg.DartersEnabled = s.ChaosDartersEnabled;
        ChaosMeta.ApplyTo(cfg);   // owned permanent upgrades shape every run
        return cfg;
    }

    /// <summary>Per-difficulty payout/intensity scalar baked into the multiplier stack.</summary>
    public double DifficultyMult => Difficulty switch
    {
        ChaosDifficulty.Easy => 1.0,
        ChaosDifficulty.Medium => 1.3,
        ChaosDifficulty.Hard => 1.7,
        ChaosDifficulty.Extreme => 2.2,
        _ => 1.0
    };
}

/// <summary>
/// A drafted boon (or curse). Pure data + an <see cref="Apply"/> mutator that
/// flips knobs on the live <see cref="ChaosRunState"/>. Data-driven so the pool
/// grows by adding records, not code paths.
/// </summary>
public sealed record ChaosBoon(
    string Id,
    string Name,
    string Desc,
    ChaosRarity Rarity,
    bool IsCurse,
    double RunMultBonus,
    Action<ChaosRunState> Apply);

/// <summary>The v1 boon/curse pool.</summary>
public static class ChaosBoonPool
{
    public static readonly List<ChaosBoon> All = new()
    {
        new("slow_fuses", "Slow Fuses", "+30% fuse time on live bubbles.",
            ChaosRarity.Common, false, 0.0, s => s.FuseTimeMult *= 1.30),
        new("defuse_chain", "Defuse Chain", "Each defuse grants a brief invulnerability.",
            ChaosRarity.Uncommon, false, 0.10, s => s.DefuseInvulnMs = 900),
        new("golden_touch", "Golden Touch", "+15% run multiplier outright.",
            ChaosRarity.Uncommon, false, 0.15, s => { /* RunMultBonus folds into BoonMult */ }),
        new("magnet", "Magnet", "Near-clicks still defuse a live bubble.",
            ChaosRarity.Uncommon, false, 0.10, s => s.MagnetEnabled = true),
        new("extra_shield", "Extra Shield", "Gain +2 shields.",
            ChaosRarity.Common, false, 0.0, s => s.Shields += 2),

        // Curses — risk/reward: bigger run-mult bonus, nastier knob.
        new("hair_trigger", "Hair Trigger", "−25% fuse time, but +0.4x run multiplier.",
            ChaosRarity.Rare, true, 0.40, s => s.FuseTimeMult *= 0.75),
        new("live_wire", "Live Wire", "Next wave: every bubble is live. +0.5x run multiplier.",
            ChaosRarity.Rare, true, 0.50, s => s.AllLiveNextWave = true),
        new("double_or_nothing", "Double or Nothing", "Next wave pays x2 — detonations cost a shield extra.",
            ChaosRarity.Rare, true, 0.0, s => { s.NextWavePayoutMult = 2.0; s.DoubleOrNothingArmed = true; }),
    };

    private static readonly Random _rng = new();

    /// <summary>
    /// Draft <paramref name="choices"/> options (default 3): mostly boons + occasionally a
    /// curse (unless curses are disabled). <paramref name="choices"/> comes from
    /// <c>ChaosRunConfig.DraftChoices</c> (the draft4 upgrade raises it to 4).
    /// </summary>
    public static List<ChaosBoon> Draft(bool allowCurses = true, int choices = 3)
    {
        choices = Math.Clamp(choices, 2, 4);
        var boons = All.Where(b => !b.IsCurse).OrderBy(_ => _rng.Next()).ToList();
        var curses = All.Where(b => b.IsCurse).OrderBy(_ => _rng.Next()).ToList();

        var draft = new List<ChaosBoon>();
        bool includeCurse = allowCurses && _rng.NextDouble() < 0.5 && curses.Count > 0;
        int boonCount = includeCurse ? choices - 1 : choices;

        draft.AddRange(boons.Take(Math.Min(boonCount, boons.Count)));
        if (includeCurse) draft.Add(curses[0]);

        // Top up from boons if the pool ran short.
        foreach (var b in boons.Skip(boonCount))
        {
            if (draft.Count >= choices) break;
            draft.Add(b);
        }
        return draft.Take(choices).ToList();
    }
}

/// <summary>Live, bindable state for one Chaos run. The HUD binds directly to this.</summary>
public sealed class ChaosRunState : INotifyPropertyChanged
{
    public ChaosRunConfig Config { get; }

    public ChaosRunState(ChaosRunConfig config)
    {
        Config = config;
        WaveCount = config.WaveCount;
        RunDurationSec = config.DurationSec;
        Shields = config.StartingShields;
        FuseTimeMult = config.FuseTimeMult;     // seed from owned upgrades; boons multiply at runtime
        MagnetEnabled = config.MagnetEnabled;
    }

    // ---- timing / waves ----
    private double _elapsedSec;
    public double ElapsedSec { get => _elapsedSec; set { _elapsedSec = value; OnChanged(); OnChanged(nameof(RunTimeText)); OnChanged(nameof(ClockText)); OnChanged(nameof(RunProgress)); OnChanged(nameof(RunIntensity)); } }
    public int RunDurationSec { get; }
    public double RunProgress => RunDurationSec <= 0 ? 0 : Math.Clamp(ElapsedSec / RunDurationSec, 0, 1);
    /// <summary>0..1 escalation curve used to scale spawn rate, fuse, strength, live-share.</summary>
    public double RunIntensity => Math.Clamp(RunProgress, 0, 1);
    public string RunTimeText => $"{(int)ElapsedSec / 60:00}:{(int)ElapsedSec % 60:00} / {RunDurationSec / 60:00}:{RunDurationSec % 60:00}";
    /// <summary>Compact remaining/elapsed clock for the collapsed HUD strip.</summary>
    public string ClockText => $"{(int)ElapsedSec / 60:00}:{(int)ElapsedSec % 60:00}";

    private int _actIndex = 1;
    public int ActIndex { get => _actIndex; set { _actIndex = value; OnChanged(); OnChanged(nameof(ActWaveText)); } }
    private int _waveIndex = 1;
    public int WaveIndex { get => _waveIndex; set { _waveIndex = value; OnChanged(); OnChanged(nameof(ActWaveText)); } }
    public int WaveCount { get; }
    public string ActWaveText => $"ACT {ToRoman(ActIndex)} · WAVE {WaveIndex}/{WaveCount}";

    private double _waveProgress;
    public double WaveProgress { get => _waveProgress; set { _waveProgress = Math.Clamp(value, 0, 1); OnChanged(); } }

    // ---- score / combo / heat ----
    private double _score;
    public double Score { get => _score; set { _score = value; OnChanged(); OnChanged(nameof(ScoreText)); } }
    public string ScoreText => $"{(int)Score:N0}";

    private int _combo;
    public int Combo { get => _combo; set { _combo = Math.Max(0, value); if (_combo > BestCombo) BestCombo = _combo; OnChanged(); OnChanged(nameof(ComboMult)); OnChanged(nameof(TotalMult)); OnChanged(nameof(TotalMultText)); } }
    public int BestCombo { get; private set; }

    private double _heat;
    /// <summary>0..1 chaos-local heat; ramps while clean, resets on detonation.</summary>
    public double Heat { get => _heat; set { _heat = Math.Clamp(value, 0, 1); OnChanged(); OnChanged(nameof(HeatMult)); OnChanged(nameof(TotalMult)); OnChanged(nameof(TotalMultText)); } }

    private int _shields;
    public int Shields { get => _shields; set { _shields = Math.Max(0, value); OnChanged(); OnChanged(nameof(ShieldText)); } }
    public string ShieldText => string.Concat(Enumerable.Repeat("♥", Shields)) + string.Concat(Enumerable.Repeat("♡", Math.Max(0, Config.StartingShields - Shields)));

    // ---- multiplier stack (chaos-local; skill/pink-rush applied once at payout) ----
    public double BaseMult => Config.BaseMult;
    public double ComboMult => Math.Min(1.0 + Combo * 0.08, 6.0);
    public double DifficultyMult => Config.DifficultyMult;
    public double HeatMult => 1.0 + Heat * 1.0; // up to x2 at full heat
    private double _boonMult = 1.0;
    public double BoonMult { get => _boonMult; set { _boonMult = value; OnChanged(); OnChanged(nameof(TotalMult)); OnChanged(nameof(TotalMultText)); } }
    public double TotalMult => BaseMult * ComboMult * DifficultyMult * HeatMult * BoonMult * (DoubleOrNothingActive ? NextWavePayoutMult : 1.0);
    public string TotalMultText => $"x{TotalMult:0.0}";

    /// <summary>Skill-tree multiplier (incl. Pink Rush) — informational; applied once at payout.</summary>
    public double SkillMult => App.SkillTree?.GetTotalXpMultiplier() ?? 1.0;
    public string SkillMultText => $"x{SkillMult:0.0}";

    // ---- counters ----
    private int _spawned; public int Spawned { get => _spawned; set { _spawned = value; OnChanged(); } }
    private int _defused; public int Defused { get => _defused; set { _defused = value; OnChanged(); } }
    private int _detonated; public int Detonated { get => _detonated; set { _detonated = value; OnChanged(); } }
    private int _effectsFired; public int EffectsFired { get => _effectsFired; set { _effectsFired = value; OnChanged(); } }

    // ---- boons / curses / feed ----
    public ObservableCollection<ChaosBoon> ActiveBoons { get; } = new();
    public ObservableCollection<ChaosBoon> ActiveCurses { get; } = new();
    public ObservableCollection<string> RecentEvents { get; } = new();

    public void PushEvent(string text)
    {
        RecentEvents.Insert(0, text);
        while (RecentEvents.Count > 6) RecentEvents.RemoveAt(RecentEvents.Count - 1);
    }

    public void ApplyBoon(ChaosBoon boon)
    {
        boon.Apply(this);
        BoonMult += boon.RunMultBonus;
        (boon.IsCurse ? ActiveCurses : ActiveBoons).Add(boon);
        PushEvent($"{(boon.IsCurse ? "☠" : "◈")} {boon.Name}");
    }

    // ---- run-tuning knobs flipped by boons/curses; spawner/fuse logic reads these ----
    public double FuseTimeMult = 1.0;
    public bool MagnetEnabled;
    public double DefuseInvulnMs;
    public double NextWavePayoutMult = 1.0;
    public bool DoubleOrNothingArmed;
    public bool DoubleOrNothingActive;
    public bool AllLiveNextWave;
    public double LuckBonus;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static string ToRoman(int n) => n switch
    {
        <= 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V",
        6 => "VI", 7 => "VII", 8 => "VIII", 9 => "IX", _ => n.ToString()
    };
}
