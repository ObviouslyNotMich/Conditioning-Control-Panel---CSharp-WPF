using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using global::Avalonia.Controls;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;
using ConditioningControlPanel;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Chaos;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Chaos;

#region legacy enums / identifiers

public enum ChaosRank { Curious, Tempted, Slipping, Devoted, Entranced, Lost, Claimed }
public enum ChaosBranch { Control, Greed, Depth }
public enum ChaosRarity { Common, Uncommon, Rare }
public enum ChaosSpeaker { Madam, Rabbit, Hatter, Doll, Enemy }

// ---- narrative layer enums (mirror WPF ChaosNarrativeModels) ----
public enum ChaosBand { Ambient = 0, Reactive = 1, Story = 2 }
public enum ChaosLineMode { Once, Pooled }
public enum ChaosRegister { Hub, DescentHigh, DescentLow }
public enum ChaosDepthMatch { Min, Exact, Range }
public enum ChaosConversationMode { Once, Repeatable }

public enum ChaosDifficulty { Easy, Medium, Hard, Extreme }

public static class RevealIds
{
    public const string Dollhouse          = "dollhouse";            // the hub itself (first descent done)
    public const string TabLookingGlass    = "tab_looking_glass";    // Slipping
    public const string SectionToys        = "section_toys";          // first toy pocket owned
    public const string SectionAccessories = "section_accessories";   // first accessory pocket owned
    public const string HerCorner          = "her_corner";            // bench stub in the Toybox (run 2+, until Looking Glass reveals)
    public const string PillTeasing        = "pill_teasing";          // Tempted
    public const string PillRelentless     = "pill_relentless";       // Entranced
    public const string PillInescapable    = "pill_inescapable";      // extreme_tier owned
    public const string StartPicker        = "start_picker";          // bench: the starting mantra
    public const string Diary              = "diary";                 // bench: the Diary
    public const string StatsPanel         = "stats_panel";           // bench: the stats panel
    public const string DraftSkip          = "draft_skip";            // run 3+
    public const string BenchToyPocket2    = "bench_toy_pocket_2";    // Devoted
    public const string BenchAccPocket2    = "bench_acc_pocket_2";    // Devoted
    public const string VariantVideo       = "variant_video";         // Entranced (run whitelist clamp)
    public const string VariantHtlink      = "variant_htlink";        // Entranced (run whitelist clamp)
    public const string Capstones          = "capstones";             // Devoted (final levels purchasable)
    public const string ExtremeTierRow     = "extreme_tier_buyable";  // Devoted (buyability; lesson stacks on top)
}

public static class BenchIds
{
    public const string ToyPocket1 = "toy_pocket_1";
    public const string AccPocket1 = "acc_pocket_1";
    public const string StartMantra = "start_mantra";
    public const string Diary = "diary";
    public const string StatsPanel = "stats_panel";
    public const string ToyPocket2 = "toy_pocket_2";
    public const string AccPocket2 = "acc_pocket_2";
}

public static class ChaosGlyphs
{
    public const string Xp = "🕰";
    public const string Drops = "✦";
    public const string Gold = "🪙";
}

#endregion

#region models

public sealed class ChaosBoon
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public string? Flavor { get; set; }
    public bool IsCurse { get; set; }
    public ChaosRarity Rarity { get; set; } = ChaosRarity.Common;
    public string[]? RequiresAny { get; set; }
    public string[]? RequiresAll { get; set; }

    /// <summary>Run-multiplier bonus added when this boon is taken (WPF parity).</summary>
    public double RunMultBonus { get; set; }

    /// <summary>Effect applied when this boon is drafted or equipped as a start boon.</summary>
    public Action<ChaosRunState>? Apply { get; set; }

    /// <summary>Curse-only: the sin's upside when Surrender's capstone waives the drawback.</summary>
    public Action<ChaosRunState>? ApplyShielded { get; set; }

    /// <summary>One-shot boons: can only be taken once per run.</summary>
    public bool Unique { get; set; }
}

public sealed class ChaosUpgrade
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public string? Flavor { get; set; }
    public ChaosBranch Branch { get; set; } = ChaosBranch.Depth;
    public int Cost { get; set; }
    public string Glyph { get; set; } = "◈";
    public string? IconPath { get; set; }
}

public sealed class ChaosLifetimeBoon
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public string? Flavor { get; set; }
    public string Glyph { get; set; } = "◈";
    public ChaosBoonCategory Category { get; set; } = ChaosBoonCategory.Skill;
    public int UnlockCost { get; set; }
    public int MaxLevel { get; set; } = 1;
    public int[] UpgradeCosts { get; set; } = Array.Empty<int>();
    public double[] LevelValues { get; set; } = Array.Empty<double>();
    public string ValueLabel { get; set; } = "{0}";
    public double ValueAt(int level) =>
        LevelValues.Length == 0 ? 0 : LevelValues[Math.Clamp(level, 1, LevelValues.Length) - 1];

    public bool IsActiveUse { get; set; }
    public double UseCooldownSec { get; set; }
    public ChaosRank RankFloor { get; set; } = ChaosRank.Curious;
    public string? CapstoneDesc { get; set; }

    /// <summary>Effect applied when this lifetime boon is active at run start; the double is the level value.</summary>
    public Action<ChaosRunState, double>? Apply { get; set; }

    /// <summary>Capstone-only alternate apply when Surrender shields a sin drawback.</summary>
    public Action<ChaosRunState, double>? ApplyShielded { get; set; }
}

public sealed class ChaosConversationLine
{
    public string Text { get; set; } = "";
    public string? AudioKey { get; set; }
    public bool Emphasis { get; set; }
}

public sealed class ChaosConversation
{
    public string Id { get; set; } = "";
    public string Trigger { get; set; } = "";
    public ChaosSpeaker Speaker { get; set; } = ChaosSpeaker.Madam;
    public string? Title { get; set; }
    public ChaosRegister Register { get; set; } = ChaosRegister.Hub;
    public ChaosConversationMode Mode { get; set; } = ChaosConversationMode.Once;
    public ChaosLineGate? Gates { get; set; }
    public string PortraitId { get; set; } = "madam";
    public bool PortraitOnLeft { get; set; }
    public List<ChaosConversationLine> Lines { get; set; } = new();
}

public sealed class ChaosLineGate
{
    public int RankMin { get; set; }
    public ChaosDepthMatch? DepthMatch { get; set; }
    public int DepthA { get; set; }
    public int DepthB { get; set; }
    public bool? FirstTime { get; set; }
    public string? ItemOwned { get; set; }
    public string? SinId { get; set; }
    public string? RunStatKey { get; set; }
    public double RunStatMin { get; set; }
}

public sealed class ChaosNarrativeCue
{
    public string Id { get; set; } = "";
    public string Trigger { get; set; } = "";
    public ChaosSpeaker Speaker { get; set; } = ChaosSpeaker.Madam;
    public ChaosBand Band { get; set; } = ChaosBand.Reactive;
    public ChaosLineMode Mode { get; set; } = ChaosLineMode.Pooled;
    public ChaosRegister Register { get; set; } = ChaosRegister.DescentHigh;
    public ChaosLineGate? Gates { get; set; }
    public int CooldownMs { get; set; }
    public int Weight { get; set; } = 1;
    public string Text { get; set; } = "";
    public string? AudioKey { get; set; }
}

public sealed class ChaosToyState : INotifyPropertyChanged
{
    public string Id { get; set; } = "";
    public string Glyph { get; set; } = "◈";
    public string KeyLabel { get; set; } = "Q";
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public string? CapstoneDesc { get; set; }
    public string? Flavor { get; set; }
    public double CooldownSec { get; set; }

    private int _chargesLeft = -1;
    public int ChargesLeft
    {
        get => _chargesLeft;
        set { _chargesLeft = value; Refresh(); }
    }

    private double _cooldownRemainingSec;
    public double CooldownRemainingSec
    {
        get => _cooldownRemainingSec;
        set { _cooldownRemainingSec = Math.Max(0, value); Refresh(); }
    }

    private bool _isEffectActive;
    public bool IsEffectActive
    {
        get => _isEffectActive;
        set { if (_isEffectActive == value) return; _isEffectActive = value; OnChanged(nameof(IsEffectActive)); Refresh(); }
    }

    public bool IsReady => CooldownRemainingSec <= 0 && ChargesLeft != 0;
    public string StatusText => ChargesLeft >= 0
        ? (ChargesLeft == 0 ? "spent" : $"{ChargesLeft} left")
        : CooldownRemainingSec > 0 ? $"{Math.Ceiling(CooldownRemainingSec):0}s" : "ready";
    public string ButtonLabel => $"{Glyph} {Name} · {KeyLabel}";

    private void Refresh()
    {
        OnChanged(nameof(ChargesLeft));
        OnChanged(nameof(CooldownRemainingSec));
        OnChanged(nameof(IsReady));
        OnChanged(nameof(StatusText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public void RaiseChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private void OnChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ChaosRunConfig
{
    public ChaosPlayMode PlayMode { get; set; } = ChaosPlayMode.Story;
    public string Difficulty { get; set; } = "Easy";
    public string MotionMode { get; set; } = "Mixed";
    public int RunDurationSec { get; set; } = 180;
    public int WaveCount { get; set; } = 5;
    public List<string> EnabledVariants { get; set; } = new();
    public bool BoonDraftEnabled { get; set; } = true;
    public bool AllowCurses { get; set; } = true;
    public bool DartersEnabled { get; set; } = true;
    public double DifficultyMult { get; set; } = 1.0;
    public double SparkGainMult { get; set; } = 1.0;
    public double BaseMult { get; set; } = 1.0;
    public int StartingShields { get; set; } = 0;
    public double StartingFocus { get; set; } = 50;

    // ---- happy-path / WPF parity knobs (kept additive; defaults are safe defaults) ----
    public bool ScriptedFirstRun { get; set; }
    public double SpawnRateMult { get; set; } = 1.0;
    public double SinChance { get; set; } = 0.5;
    public double EffectIntensity { get; set; } = 1.0;
    public int DraftAutoResumeSec { get; set; } = 12;
    public bool AmbientMode { get; set; }
    public bool MagnetEnabled { get; set; }
    public double FuseTimeMult { get; set; } = 1.0;
    public bool PopupHeartEnabled { get; set; } = true;
    public bool PendulumSwing { get; set; }
    public double HitboxScale { get; set; } = 1.0;
    public int DraftChoices { get; set; } = 3;
    public bool ScreenShakeEnabled { get; set; } = true;
    public bool ColorFlashesEnabled { get; set; } = true;
    public double ShakeIntensity { get; set; } = 1.0;
    public ChaosMotion? MotionOverride { get; set; }

    public static ChaosRunConfig FromSettings()
    {
        var s = App.Services?.GetService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>()?.Current;
        if (s == null) return new ChaosRunConfig();
        return new ChaosRunConfig
        {
            PlayMode = s.NarrativeModeEnabled ? ChaosPlayMode.Story : ChaosPlayMode.FreeDesktop,
            Difficulty = s.ChaosDifficulty,
            MotionMode = s.ChaosMotionMode,
            RunDurationSec = s.ChaosRunDurationSec,
            WaveCount = s.ChaosWaveCount,
            EnabledVariants = s.ChaosEnabledVariants?.ToList() ?? new List<string>(),
            BoonDraftEnabled = s.ChaosBoonDraftEnabled,
            AllowCurses = s.ChaosAllowCurses,
            DartersEnabled = s.ChaosDartersEnabled,
            DifficultyMult = DifficultyToMult(s.ChaosDifficulty),
            SparkGainMult = 1.0,
            BaseMult = 1.0,
            StartingShields = 0,
            StartingFocus = 50,
            EffectIntensity = s.ChaosEffectIntensity,
            ScreenShakeEnabled = s.ChaosScreenShakeEnabled,
            ColorFlashesEnabled = s.ChaosColorFlashesEnabled,
            ShakeIntensity = s.ChaosShakeIntensity,
        };
    }

    private static double DifficultyToMult(string? diff) => (diff ?? "Easy") switch
    {
        "Extreme" => 2.0,
        "Hard" => 1.5,
        "Medium" => 1.2,
        _ => 1.0,
    };
}

public sealed class ChaosRunState : INotifyPropertyChanged
{
    public int Shields { get; set; }
    public bool FocusLow { get; set; }
    public int Combo { get; set; }
    public double ComboMult { get; set; } = 1.0;
    public double DifficultyMult { get; set; } = 1.0;
    public double HeatMult { get; set; } = 1.0;
    public double BoonMult { get; set; } = 1.0;
    public double Heat { get; set; }
    public bool RippleReady { get; set; }
    public string ClockText { get; set; } = "0:00";
    public string ScoreText { get; set; } = "0";
    public string TotalMultText { get; set; } = "x1.0";
    public string FocusText { get; set; } = "50 / 100";
    public double Focus { get; set; } = 50;
    public double FocusMax { get; set; } = 100;
    public string RippleText { get; set; } = "READY";
    public string ShieldText { get; set; } = "0 ♥";

    // ---- hold-to-defuse channel state ----
    public bool IsChanneling { get; set; }
    public DateTime ChannelStartTime { get; set; }
    public string? ChannelTargetBubbleId { get; set; }
    public double ChannelHeldSec { get; set; }
    public string ChannelText { get; set; } = "";

    public double RunProgress { get; set; }
    /// <summary>Mirrors WPF RunIntensity; currently tracks progress intensity 0..1.</summary>
    public double RunIntensity => RunProgress;
    public string RunTimeText { get; set; } = "0:00";
    public string ActWaveText { get; set; } = "I · 1";
    public double RunDurationSec { get; set; } = 180;
    public int WaveCount { get; set; } = 5;
    public double ElapsedSec { get; set; }
    public int ActIndex { get; set; } = 1;
    public int WaveIndex { get; set; } = 1;
    public int BestCombo { get; set; }
    public int Defused { get; set; }
    public int Detonated { get; set; }
    public int EffectsFired { get; set; }
    public double Score { get; set; }
    public ChaosRunConfig Config { get; set; } = new();

    public List<ChaosSidebarBoon> ActiveSidebarToys { get; set; } = new();
    public List<ChaosSidebarBoon> ActiveSidebarAccessories { get; set; } = new();
    public List<ChaosSidebarBoon> RunPickTiles { get; set; } = new();
    public List<ChaosSidebarBoon> RunModifiers { get; set; } = new();
    public List<ChaosBoon> ActiveBoons { get; set; } = new();
    public List<ChaosBoon> ActiveCurses { get; set; } = new();
    public List<ChaosToyState> ActiveToys { get; set; } = new();
    public List<string> RecentEvents { get; set; } = new();

    #region boon / curse tuning knobs

    public double FuseTimeMult { get; set; } = 1.0;
    public bool MagnetEnabled { get; set; }
    public double DefuseInvulnMs { get; set; }
    public bool AllLiveNextWave { get; set; }
    public double ChainReactionReach { get; set; } = 1.0;
    public double LuckBonus { get; set; }
    public HashSet<string> MaxedBoons { get; set; } = new();
    public Dictionary<string, double> ToyPower { get; set; } = new();
    public int CollarSaves { get; set; }
    public double SlowMoBonusSec { get; set; }
    public double BlindfoldPayMult { get; set; } = 1.0;
    public bool BlindfoldActive { get; set; }
    public double BlindfoldOpacity { get; set; } = 1.0;
    public double SinExtraMult { get; set; }
    public bool SurrenderShieldUsed { get; set; }
    public double RabbitRateMult { get; set; } = 1.0;
    public double LastBreathWindowSec { get; set; }
    public double LastBreathPayMult { get; set; } = 1.0;
    public double ChanceDoubleOdds { get; set; }
    public int RerollsLeft { get; set; }
    public double CursorPullStrength { get; set; }
    public bool SpankerActive { get; set; }
    public double SpankGrowFactor { get; set; } = 1.0;
    public double IntrusiveThoughtsSec { get; set; }
    public double GoldenChance { get; set; } = 0.005;
    public int DropPerPop { get; set; }
    public long TrickleDrops { get; set; }
    public bool ShowPopScores { get; set; }
    public bool ShowWaveTimer { get; set; }
    public int ShieldRegenPops { get; set; }
    public double BenignBaseline { get; set; } = 0.4;
    public double BubbleScale { get; set; } = 1.0;
    public int StartShields { get; set; }
    public double RippleRechargeSec { get; set; } = 15.0;
    public double RippleRadiusPx { get; set; } = 400.0;
    public double RippleLifeMs { get; set; } = 1200.0;

    // ---- run-boon (mantra / sin) flags ----
    public bool GoldDiggerEnabled { get; set; }
    public bool WelcomeShowerEnabled { get; set; }
    public int HeavyDropEvery { get; set; }
    public double GgRabbitChance { get; set; }
    public bool RippleEnabled { get; set; }
    public bool AftermathEnabled { get; set; }
    public int EStimChargeMult { get; set; } = 1;
    public double AfterglowSec { get; set; }
    public int DvdSplitBounces { get; set; }
    public double RabbitTrailSec { get; set; }
    public bool UnleashedEnabled { get; set; }
    public bool ElectrifiedRabbits { get; set; }
    public double EStimShockwaveChance { get; set; }
    public double PendulumPayMult { get; set; } = 1.0;
    public double DetonationDurationMult { get; set; } = 1.0;
    public bool LastSecondGoldEnabled { get; set; }
    public double PrismChance { get; set; }
    public bool PrismTreatOnly { get; set; }
    public double CamGirlFlee { get; set; }
    public double CamGirlTipChance { get; set; }
    public double UrgeMult { get; set; } = 1.0;
    public bool ActivesDisabled { get; set; }
    public bool RelapseLoopArmed { get; set; }
    public bool RelapseLoopActive { get; set; }

    // ---- generic runtime bonuses for the minimal common-effect boons ----
    public double ComboMultBonus { get; set; }

    #endregion

    public event PropertyChangedEventHandler? PropertyChanged;
    public void RaiseChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void PushEvent(string text)
    {
        RecentEvents.Add(text);
        if (RecentEvents.Count > 40) RecentEvents.RemoveAt(0);
    }

    /// <summary>Applies a drafted boon or start-boon effect to this run state (WPF parity).</summary>
    public void ApplyBoon(ChaosBoon boon, bool shieldDrawback = false)
    {
        if (boon == null) return;
        if (shieldDrawback) boon.ApplyShielded?.Invoke(this);
        else boon.Apply?.Invoke(this);
        BoonMult += boon.RunMultBonus;
        (boon.IsCurse ? ActiveCurses : ActiveBoons).Add(boon);
    }
}

public sealed class BubblePreset
{
    public string Name { get; set; } = "";
    public List<string> VariantIds { get; set; } = new();
}

#endregion

#region static service stubs

public static class ChaosMeta
{
    private static string? _filePath;
    private static readonly JsonSerializerOptions _loadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions _saveOptions = new() { WriteIndented = true };

    public static ChaosMetaState State { get; set; } = new();
    public static string Rank => ChaosRanks.Name(CurrentRank);
    public static ChaosRank CurrentRank => ChaosRanks.For(State.RunsCompleted);
    public static int RankIndex => (int)CurrentRank;
    public const int FIRST_FALL_BONUS = 100;

    public static void Init(IAppEnvironment env)
    {
        _filePath = Path.Combine(env.UserDataPath, "chaos_meta.json");
        State = LoadState();
        RefundRetiredBoons();
        SanitizePockets();
    }

    private static ChaosMetaState LoadState()
    {
        try
        {
            var path = _filePath;
            if (string.IsNullOrEmpty(path)) return new ChaosMetaState();
            var tempPath = path + ".tmp";

            if (File.Exists(tempPath) && !File.Exists(path))
            {
                try { File.Move(tempPath, path); } catch { }
            }
            else if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }

            if (!File.Exists(path)) return new ChaosMetaState();

            var json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<ChaosMetaState>(json, _loadOptions);
            if (state == null)
            {
                LogWarning("ChaosMeta: chaos_meta.json parsed to null; using fresh meta state");
                return new ChaosMetaState();
            }

            state.PurchasedUpgrades ??= new();
            state.DisabledUpgrades ??= new();
            state.DiscoveredCodexIds ??= new();
            state.LifetimeBoonLevels ??= new();
            state.ActiveLifetimeBoons ??= new();
            state.BenchPurchases ??= new();
            state.LessonProgress ??= new();
            state.LessonsComplete ??= new();
            state.PendingReveals ??= new();
            state.SeenReveals ??= new();
            state.FirstTimesAwarded ??= new();
            state.BubbleHintsLearned ??= new();
            state.SeenNarrativeLines ??= new();
            state.NarrativeCooldownEnds ??= new();
            return state;
        }
        catch (Exception ex)
        {
            LogWarning("ChaosMeta.Load failed ({Error}); using fresh meta state", ex.Message);
            return new ChaosMetaState();
        }
    }

    public static void Save()
    {
        try
        {
            var path = _filePath;
            if (string.IsNullOrEmpty(path)) return;
            var tempPath = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var json = JsonSerializer.Serialize(State, _saveOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            LogWarning("ChaosMeta.Save failed: {Error}", ex.Message);
        }
    }

    public static bool AtLeast(ChaosRank rank) => CurrentRank >= rank;

    public static void AddGold(int amount)
    {
        if (amount <= 0) return;
        State.Gold += amount;
        Save();
    }

    public static bool TrySpendGold(int amount)
    {
        if (amount < 0 || State.Gold < amount) return false;
        State.Gold -= amount;
        Save();
        return true;
    }

    public static void EquipStartBoon(string? boonId)
    {
        State.EquippedStartBoon = boonId;
        Save();
    }

    /// <summary>Apply every active+unlocked lifetime boon (at its current level) to the run state.</summary>
    public static void ApplyLifetimeBoons(ChaosRunState run)
    {
        if (run == null) return;
        foreach (var id in State.ActiveLifetimeBoons)
        {
            int lvl = BoonLevel(id);
            var b = ChaosLifetimeBoons.ById(id);
            if (b != null && lvl >= 1)
            {
                b.Apply?.Invoke(run, b.ValueAt(lvl));
                if (lvl >= b.MaxLevel) run.MaxedBoons.Add(b.Id);
            }
        }
    }

    public static void MarkDiscovered(string codexId)
    {
        if (string.IsNullOrEmpty(codexId)) return;
        if (State.DiscoveredCodexIds.Add(codexId)) Save();
    }

    public static bool IsDiscovered(string codexId) =>
        !string.IsNullOrEmpty(codexId) && State.DiscoveredCodexIds.Contains(codexId);

    public static bool IsOwned(string id) => State.PurchasedUpgrades.Contains(id);

    public static bool IsUpgradeActive(string id) =>
        IsOwned(id) && !State.DisabledUpgrades.Contains(id);

    public static void SetUpgradeActive(string id, bool active)
    {
        if (!IsOwned(id)) return;
        bool changed = active ? State.DisabledUpgrades.Remove(id) : State.DisabledUpgrades.Add(id);
        if (changed) Save();
    }

    public static bool CanAfford(string id)
    {
        var u = ChaosUpgrades.ById(id);
        return u != null && !IsOwned(id) && State.Sparks >= u.Cost;
    }

    public static bool CanAffordUnlock(string id)
    {
        var c = UnlockCostOf(id);
        return c.HasValue && State.Sparks >= c.Value;
    }

    public static bool CanAffordUpgrade(string id)
    {
        var c = NextUpgradeCostOf(id);
        return c.HasValue && State.Sparks >= c.Value;
    }

    public static bool IsPurchaseRankLocked(string id) =>
        id == "extreme_tier" && !IsOwned(id) && !AtLeast(ChaosRank.Devoted);

    public static bool IsBoonRankLocked(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        return b != null && !IsBoonUnlocked(id) && !AtLeast(b.RankFloor);
    }

    public static bool IsAccessoryScriptLocked(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        return b != null
            && b.Category == ChaosBoonCategory.Accessory
            && b.Id != "the_spanker"
            && !IsBoonUnlocked("the_spanker");
    }

    public static bool IsBoonUnlocked(string id) => BoonLevel(id) >= 1;

    public static bool IsBoonActive(string id) =>
        State.ActiveLifetimeBoons.Contains(id) && IsBoonUnlocked(id);

    public static void SetBoonActive(string id, bool active)
    {
        if (active)
        {
            if (!IsBoonUnlocked(id)) return;
            var cat = ChaosLifetimeBoons.ById(id)?.Category ?? ChaosBoonCategory.Utility;
            if (!IsBoonActive(id) && !HasFreePocket(cat)) return;
        }
        bool changed = active ? State.ActiveLifetimeBoons.Add(id) : State.ActiveLifetimeBoons.Remove(id);
        if (changed) Save();
    }

    public static int BoonLevel(string id) =>
        State.LifetimeBoonLevels.TryGetValue(id, out var l) ? l : 0;

    public static bool TryUnlockBoon(string id)
    {
        if (IsBoonRankLocked(id)) return false;
        if (ChaosLessons.IsLessonBlocked(id)) return false;
        if (IsAccessoryScriptLocked(id)) return false;
        var c = UnlockCostOf(id);
        if (!c.HasValue || State.Sparks < c.Value) return false;
        State.Sparks -= c.Value;
        State.LifetimeBoonLevels[id] = 1;
        var cat = ChaosLifetimeBoons.ById(id)?.Category ?? ChaosBoonCategory.Utility;
        if (HasFreePocket(cat)) State.ActiveLifetimeBoons.Add(id);
        Save();
        return true;
    }

    public static bool TryUpgradeBoon(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        var c = NextUpgradeCostOf(id);
        if (b == null || !c.HasValue || State.Sparks < c.Value) return false;
        if (BoonLevel(id) + 1 >= b.MaxLevel && !AtLeast(ChaosRank.Devoted)) return false;
        State.Sparks -= c.Value;
        State.LifetimeBoonLevels[id] = Math.Min(BoonLevel(id) + 1, b.MaxLevel);
        Save();
        return true;
    }

    public static bool TryPurchase(string id)
    {
        var u = ChaosUpgrades.ById(id);
        if (u == null) return false;
        if (ChaosLessons.IsLessonBlocked(id)) return false;
        if (State.PurchasedUpgrades.Contains(id)) return false;
        if (State.Sparks < u.Cost) return false;
        if (IsPurchaseRankLocked(id)) return false;

        State.Sparks -= u.Cost;
        State.PurchasedUpgrades.Add(id);
        if (id == "extreme_tier") State.ExtremeUnlocked = true;
        Save();
        return true;
    }

    public static bool HasFreePocket(ChaosBoonCategory cat) => EquippedCountIn(cat) < SlotsFor(cat);

    public const int MAX_POCKETS_PER_CATEGORY = 2;

    public static int SlotsFor(ChaosBoonCategory cat) => cat switch
    {
        ChaosBoonCategory.Utility => int.MaxValue,
        ChaosBoonCategory.Skill => Math.Min(State.ToyPockets, MAX_POCKETS_PER_CATEGORY),
        ChaosBoonCategory.Accessory => Math.Min(State.AccessoryPockets, MAX_POCKETS_PER_CATEGORY),
        _ => 0,
    };

    public static int EquippedCountIn(ChaosBoonCategory cat) =>
        State.ActiveLifetimeBoons.Count(id => ChaosLifetimeBoons.ById(id)?.Category == cat && IsBoonUnlocked(id));

    public static (string Name, bool Affordable, string? LessonId, int Cost)? NextGoal()
    {
        ChaosNextGoal? bestAffordable = null, bestAny = null;
        void Consider(string id, string name, int cost, string? lessonId)
        {
            var g = new ChaosNextGoal(id, name, cost, lessonId);
            if (bestAny == null || cost < bestAny.Value.Cost) bestAny = g;
            if (g.Affordable && (bestAffordable == null || cost < bestAffordable.Value.Cost)) bestAffordable = g;
        }

        foreach (var u in ChaosUpgrades.All)
        {
            if (IsOwned(u.Id) || IsPurchaseRankLocked(u.Id)) continue;
            Consider(u.Id, u.Name, u.Cost,
                ChaosLessons.IsLessonBlocked(u.Id) ? u.Id : null);
        }
        foreach (var b in ChaosLifetimeBoons.All)
        {
            int level = BoonLevel(b.Id);
            if (level <= 0)
            {
                if (IsAccessoryScriptLocked(b.Id)) continue;
                Consider(b.Id, b.Name, b.UnlockCost,
                    ChaosLessons.IsLessonBlocked(b.Id) ? b.Id : null);
            }
            else if (level < b.MaxLevel && !IsCapstonePurchaseRankLocked(b.Id))
            {
                int cost = NextUpgradeCostOf(b.Id) ?? 0;
                if (cost > 0) Consider(b.Id, b.Name, cost, null);
            }
        }
        var result = bestAffordable ?? bestAny;
        return result.HasValue ? (result.Value.Name, result.Value.Affordable, result.Value.LessonId, result.Value.Cost) : null;
    }

    private static int? UnlockCostOf(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        return (b == null || IsBoonUnlocked(id)) ? null : b.UnlockCost;
    }

    private static int? NextUpgradeCostOf(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        if (b == null) return null;
        int lvl = BoonLevel(id);
        if (lvl < 1 || lvl >= b.MaxLevel) return null;
        return (lvl - 1) < b.UpgradeCosts.Length ? b.UpgradeCosts[lvl - 1] : null;
    }

    private static bool IsCapstonePurchaseRankLocked(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        if (b == null) return false;
        int lvl = BoonLevel(id);
        return lvl >= 1 && lvl < b.MaxLevel && lvl + 1 >= b.MaxLevel && !AtLeast(ChaosRank.Devoted);
    }

    private static void SanitizePockets()
    {
        try
        {
            bool changed = false;
            if (State.ToyPockets > MAX_POCKETS_PER_CATEGORY) { State.ToyPockets = MAX_POCKETS_PER_CATEGORY; changed = true; }
            if (State.AccessoryPockets > MAX_POCKETS_PER_CATEGORY) { State.AccessoryPockets = MAX_POCKETS_PER_CATEGORY; changed = true; }
            foreach (var cat in new[] { ChaosBoonCategory.Skill, ChaosBoonCategory.Accessory })
            {
                int keep = SlotsFor(cat);
                foreach (var b in ChaosLifetimeBoons.InCategory(cat))
                {
                    if (!IsBoonActive(b.Id)) continue;
                    if (keep > 0) { keep--; continue; }
                    State.ActiveLifetimeBoons.Remove(b.Id);
                    changed = true;
                    LogInformation("Chaos: unequipped {Id} (over the {Cat} pocket cap)", b.Id, cat);
                }
            }
            if (changed) Save();
        }
        catch (Exception ex) { LogWarning("Chaos: pocket sanitize failed ({E})", ex.Message); }
    }

    private static readonly Dictionary<string, int[]> RetiredBoonRefunds = new()
    {
        ["muscle_memory"] = new[] { 200, 400, 700, 1150, 1800 },
        ["magic_wand"] = new[] { 150, 300, 550, 950, 1550 },
    };

    private static void RefundRetiredBoons()
    {
        try
        {
            bool changed = false;
            foreach (var (id, costs) in RetiredBoonRefunds)
            {
                if (State.LifetimeBoonLevels.TryGetValue(id, out int lvl) && lvl >= 1)
                {
                    int refund = costs[Math.Clamp(lvl, 1, costs.Length) - 1];
                    State.Sparks += refund;
                    State.LifetimeBoonLevels.Remove(id);
                    changed = true;
                    LogInformation("Chaos: retired boon {Id} (L{Lvl}) refunded ✦{Refund}", id, lvl, refund);
                }
                if (State.ActiveLifetimeBoons.Remove(id)) changed = true;
            }
            if (State.PurchasedUpgrades.Remove("spark_gain")) changed = true;
            if (State.DisabledUpgrades.Remove("spark_gain")) changed = true;
            var retiredHabitRefunds = new Dictionary<string, int>
            {
                ["bigger_hitboxes"] = 80, ["magnet"] = 150, ["shield_recharge"] = 200,
                ["start_shield"] = 100, ["collar"] = 200, ["pendulum"] = 220,
                ["base_mult"] = 90, ["golden_touch"] = 130, ["take_more"] = 400,
                ["tunnel_vision"] = 140, ["max_bubbles"] = 110,
            };
            foreach (var (id, refund) in retiredHabitRefunds)
            {
                if (State.PurchasedUpgrades.Remove(id))
                {
                    State.Sparks += refund;
                    changed = true;
                    LogInformation("Chaos: retired habit {Id} refunded ✦{Refund}", id, refund);
                }
                if (State.DisabledUpgrades.Remove(id)) changed = true;
            }
            foreach (var id in new[] { "tunnel_vision", "pendulum" })
            {
                if (State.LifetimeBoonLevels.Remove(id)) changed = true;
                if (State.ActiveLifetimeBoons.Remove(id)) changed = true;
            }
            if (changed) Save();
        }
        catch (Exception ex) { LogWarning("Chaos: retired-boon refund failed ({E})", ex.Message); }
    }

    public static void DebugResetState()
    {
        State = new ChaosMetaState();
        Save();
        LogWarning("ChaosMeta: state RESET via debug strip");
    }

    private static void LogWarning(string message, params object?[] args)
    {
        try { global::ConditioningControlPanel.App.Logger?.Warning(message, args); } catch { }
    }

    private static void LogInformation(string message, params object?[] args)
    {
        try { global::ConditioningControlPanel.App.Logger?.Information(message, args); } catch { }
    }

    public readonly record struct ChaosNextGoal(string Id, string Name, int Cost, string? LessonId)
    {
        public bool Affordable => LessonId == null && Cost <= State.Sparks;
    }
}

public static class RevealService
{
    private static readonly Dictionary<string, Func<bool>> _registry = new()
    {
        [RevealIds.Dollhouse]          = () => ChaosMeta.State.RunsCompleted >= 1,
        [RevealIds.TabLookingGlass]    = () => ChaosMeta.RankIndex >= (int)ChaosRank.Slipping,
        [RevealIds.SectionToys]        = () => ChaosMeta.State.ToyPockets >= 1,
        [RevealIds.SectionAccessories] = () => ChaosMeta.State.AccessoryPockets >= 1,
        [RevealIds.HerCorner]          = () => ChaosMeta.State.RunsCompleted >= 2 && ChaosMeta.RankIndex < (int)ChaosRank.Slipping,
        [RevealIds.PillTeasing]        = () => ChaosMeta.RankIndex >= (int)ChaosRank.Tempted,
        [RevealIds.PillRelentless]     = () => ChaosMeta.RankIndex >= (int)ChaosRank.Entranced,
        [RevealIds.PillInescapable]    = () => ChaosMeta.State.ExtremeUnlocked,
        [RevealIds.DraftSkip]          = () => ChaosMeta.State.RunsCompleted >= 2,
        [RevealIds.StartPicker]        = () => ChaosMeta.State.BenchPurchases.Contains(BenchIds.StartMantra),
        [RevealIds.Diary]              = () => ChaosMeta.State.BenchPurchases.Contains(BenchIds.Diary),
        [RevealIds.StatsPanel]         = () => ChaosMeta.State.BenchPurchases.Contains(BenchIds.StatsPanel),
        [RevealIds.BenchToyPocket2]    = () => ChaosMeta.RankIndex >= (int)ChaosRank.Devoted,
        [RevealIds.BenchAccPocket2]    = () => ChaosMeta.RankIndex >= (int)ChaosRank.Devoted,
        [RevealIds.VariantVideo]       = () => ChaosMeta.RankIndex >= (int)ChaosRank.Entranced,
        [RevealIds.VariantHtlink]      = () => ChaosMeta.RankIndex >= (int)ChaosRank.Entranced,
        [RevealIds.Capstones]          = () => ChaosMeta.RankIndex >= (int)ChaosRank.Devoted,
        [RevealIds.ExtremeTierRow]     = () => ChaosMeta.RankIndex >= (int)ChaosRank.Devoted,
    };

    public static event Action<string>? Pending;

    public static bool IsUnlocked(string id) =>
        !_registry.TryGetValue(id, out var pred) || SafePred(pred);

    public static bool IsPending(string id) => ChaosMeta.State.PendingReveals.Contains(id);
    public static bool IsSeen(string id) => ChaosMeta.State.SeenReveals.Contains(id);

    public static bool Clamp(string id, bool userSetting) => userSetting && IsUnlocked(id);

    public static void Sync(string reason)
    {
        try
        {
            bool changed = false;
            foreach (var (id, pred) in _registry)
            {
                if (!SafePred(pred)) continue;
                if (ChaosMeta.State.SeenReveals.Contains(id)) continue;
                if (!ChaosMeta.State.PendingReveals.Add(id)) continue;
                changed = true;
                Log("Chaos reveal pending: {Id} ({Reason})", id, reason);
                try { Pending?.Invoke(id); } catch { }
            }
            if (changed) ChaosMeta.Save();
        }
        catch (Exception ex) { Log("RevealService.Sync failed ({E})", ex.Message); }
    }

    public static IReadOnlyList<string> PendingIds() => ChaosMeta.State.PendingReveals.ToList();

    public static void MarkSeen(string id)
    {
        bool changed = ChaosMeta.State.PendingReveals.Remove(id);
        changed |= ChaosMeta.State.SeenReveals.Add(id);
        if (changed) ChaosMeta.Save();
    }

    private static bool SafePred(Func<bool> pred)
    {
        try { return pred(); } catch { return false; }
    }

    private static void Log(string message, params object?[] args)
    {
        try { global::ConditioningControlPanel.App.Logger?.Information(message, args); } catch { }
    }
}

public static class ChaosRanks
{
    public static int[] Thresholds { get; } = { 0, 3, 10, 25, 50 };
    public static string RankLockedTip => "sink deeper to learn this.";
    public static string CapstoneLockedTip => "the capstone waits for Devoted.";

    public static ChaosRank For(int runsCompleted)
    {
        var r = ChaosRank.Curious;
        for (int i = Thresholds.Length - 1; i >= 0; i--)
            if (runsCompleted >= Thresholds[i]) { r = (ChaosRank)i; break; }
        return r;
    }

    public static string Name(ChaosRank rank) => rank.ToString().ToLowerInvariant();
    public static string NameLower(ChaosRank rank) => Name(rank);
    public static string Line(ChaosRank rank) => "";
    public static string RankSpecifics(ChaosRank rank) => $"reach {Thresholds.ElementAtOrDefault((int)rank)} descents.";
}

public static class ChaosUpgrades
{
    public static List<ChaosUpgrade> All { get; } = new();
    public static ChaosUpgrade? ById(string id) => All.FirstOrDefault(x => x.Id == id);
}

public static class ChaosLifetimeBoons
{
    public static List<ChaosLifetimeBoon> All { get; } = new();
    public static IEnumerable<ChaosLifetimeBoon> InCategory(ChaosBoonCategory cat) => All.Where(b => b.Category == cat);
    public static ChaosLifetimeBoon? ById(string id) => All.FirstOrDefault(x => x.Id == id);
}

public static class ChaosBoonPool
{
    public static List<ChaosBoon> All { get; } = new();
}

public static class ChaosBubbleVariants
{
    public sealed class Variant
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public Color Tint { get; set; }
        public bool IsLive { get; set; }
    }

    public static List<Variant> All { get; } = new();
    public static string DescriptionFor(string id) => "";
    public static List<BubblePreset> Presets { get; } = new();

    /// <summary>Build a variant spec from its catalog definition.</summary>
    public static ChaosBubbleSpec Build(Variant variant, double intensity, double fuseTimeMult = 1.0,
        ChaosMotion? motionOverride = null, double effectIntensity = 1.0, double sizeScale = 1.0,
        double sideDriftChance = 0.0)
    {
        var rng = Random.Shared;
        double size = 80 + rng.NextDouble() * 80;
        bool isLive = variant.IsLive;
        int fuseMs = isLive ? (int)(4000 * fuseTimeMult * (1.1 - intensity * 0.2)) : 0;
        var motion = motionOverride ?? variant.Id switch
        {
            "flash" => ChaosMotion.FloatUp,
            "subliminal" => ChaosMotion.RainDown,
            "braindrain" => ChaosMotion.RoamBounce,
            "pink" => ChaosMotion.RoamBounce,
            "spiral" => ChaosMotion.RoamBounce,
            _ => rng.Next(3) switch { 0 => ChaosMotion.FloatUp, 1 => ChaosMotion.RainDown, _ => ChaosMotion.RoamBounce }
        };
        var tint = variant.Tint;
        return new ChaosBubbleSpec
        {
            VariantId = variant.Id,
            PayloadKind = variant.Id,
            SizePx = size * sizeScale,
            IsLive = isLive,
            FuseMs = Math.Max(500, fuseMs),
            Motion = motion,
            SpeedMult = 1.0 + rng.NextDouble() * 0.5,
            EffectIntensity = effectIntensity,
            SideDriftChance = sideDriftChance,
            TintR = tint.R, TintG = tint.G, TintB = tint.B,
        };
    }

    /// <summary>Build a lucky golden income bubble spec.</summary>
    public static ChaosBubbleSpec BuildGolden()
    {
        var rng = Random.Shared;
        return new ChaosBubbleSpec
        {
            VariantId = "golden",
            PayloadKind = "golden",
            IsGolden = true,
            SizePx = 110 + rng.NextDouble() * 30,
            Motion = rng.NextDouble() < 0.5 ? ChaosMotion.FloatUp : ChaosMotion.RainDown,
            SpeedMult = 2.8,
            TintR = 0xFF, TintG = 0xD7, TintB = 0x00,
        };
    }

    /// <summary>Builds a white-rabbit darter bubble spec for the Rabbit Caller active toy.</summary>
    public static ChaosBubbleSpec BuildDarter(double intensity = 1.0, bool spotlight = false,
        double? atPxX = null, double? atPxY = null)
    {
        var rng = Random.Shared;
        return new ChaosBubbleSpec
        {
            VariantId = "darter",
            PayloadKind = "darter",
            IsDarter = true,
            SizePx = 70 + rng.Next(40),
            Motion = ChaosMotion.RoamBounce,
            SpeedMult = 1.0,
            DarterSpeed = 360 * intensity,
            DarterMaxBounces = 3,
            TelegraphMs = 500,
            LifetimeMs = 6000,
            Spotlight = spotlight,
            SpawnAtPxX = atPxX,
            SpawnAtPxY = atPxY,
            TintR = 0xFF, TintG = 0xFF, TintB = 0xFF,
        };
    }
}

public static class ChaosArt
{
    public static IImage? Resolve(string kind, string id) => AvaloniaChaosArt.Resolve(kind, id);
    public static IImage? ResolveBanner() => null;
    public static IImage? ResolveRecap() => null;
    public static IImage? TryLoad(string? path) => AvaloniaChaosArt.TryLoad(path);
}

public static class ChaosTips
{
    public static void Attach(Control element, string title, string? desc, string? extra = null,
                              Color? accent = null, string? flavor = null)
    {
        // TODO: wire ToolTipService once cross-platform helpers land.
    }
}

public static class ChaosSfx
{
    public static void Play(string name, float scale = 0.5f) { }
    public static void PlayBoonReveal(bool rare) { }
    public static void PlayBoonPicked() { }
}

public static class ChaosNarrator
{
    private const int MIN_DWELL_MS = 2400;
    private const int MAX_DWELL_MS = 7000;

    /// <summary>True while the Madam is approximately still speaking.</summary>
    public static bool IsPlaying { get; private set; }

    /// <summary>
    /// Speak a single cue: show the on-screen subtitle, mark the narrator busy, and
    /// play placeholder audio. Real voice clips are a future TODO.
    /// </summary>
    public static void Speak(ChaosNarrativeCue cue, bool interrupt)
    {
        try
        {
            if (cue == null) return;
            int durMs = EstimateDurationMs(cue.Text);
            IsPlaying = true;
            ChaosAnnouncerOverlay.AnnounceNarrator(cue.Text, (int)cue.Band, interrupt, durMs);
            PlayCardLine(cue.AudioKey, cue.Text); // TODO: real narrator audio clips
            _ = ResetAfterAsync(durMs + 220);
        }
        catch (Exception ex)
        {
            LogDebug("ChaosNarrator.Speak failed: {E}", ex.Message);
        }
    }

    /// <summary>Play one conversation-card line's audio placeholder. Real clips are a future TODO.</summary>
    public static void PlayCardLine(string? audioKey, string text)
    {
        // TODO: resolve assets/Chaos/narrator/{audioKey}.mp3 and play when narrator audio lands.
    }

    /// <summary>Release the card's speaking hold.</summary>
    public static void EndCard() => Reset();

    /// <summary>Force-stop any speaking state (run teardown).</summary>
    public static void Reset() => IsPlaying = false;

    private static async System.Threading.Tasks.Task ResetAfterAsync(int delayMs)
    {
        try
        {
            await System.Threading.Tasks.Task.Delay(delayMs);
            IsPlaying = false;
        }
        catch { }
    }

    private static int EstimateDurationMs(string text)
    {
        int est = 1200 + (text?.Length ?? 0) * 55;
        return Math.Clamp(est, MIN_DWELL_MS, MAX_DWELL_MS);
    }

    private static void LogDebug(string message, params object?[] args)
    {
        try { App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Debug(message, args); } catch { }
    }
}

/// <summary>
/// Static in-run/hub dispatch for the narrative layer. Mirrors the WPF
/// <c>ChaosNarrativeHooks</c> shape but with Avalonia-specific display wiring.
/// </summary>
public static class ChaosNarrativeHooks
{
    private static bool _active;
    private static bool _sawBareDeto;
    private static bool _sawFirstPop;
    private static bool _sawFirstDefuse;
    private static bool _sawFirstDetonation;

    public static void Init() => ChaosNarrativeDirector.Init();

    public static void OnRunStarted(ChaosNarrativeContext ctx)
    {
        _active = true;
        _sawBareDeto = false;
        _sawFirstPop = false;
        _sawFirstDefuse = false;
        _sawFirstDetonation = false;
        ctx.Trigger = "run_start";
        if (ChaosHappyPath.IsScripting) return;
        ChaosNarrativeDirector.Fire(ctx);
    }

    public static void OnRunEnded(ChaosNarrativeContext ctx, double score, bool ranFullCourse)
    {
        _active = false;
        _sawBareDeto = false;
        _sawFirstPop = false;
        _sawFirstDefuse = false;
        _sawFirstDetonation = false;
        ChaosNarrator.Reset();
    }

    /// <summary>Hub-side moment (no live run). Picks and displays a conversation standalone.</summary>
    public static void OnHubMoment(string moment, ChaosNarrativeContext ctx)
    {
        try
        {
            ctx.Trigger = moment;
            ctx.Depth = 0;
            var convo = ChaosNarrativeDirector.Pick(ctx, moment);
            if (convo == null) return;
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => ShowStandaloneConversation(convo));
        }
        catch (Exception ex)
        {
            LogDebug("ChaosNarrativeHooks.OnHubMoment: {E}", ex.Message);
        }
    }

    public static void OnWaveStart(int waveIndex, ChaosNarrativeContext ctx)
    {
        if (!_active || ChaosHappyPath.IsScripting) return;
        ctx.Trigger = "zone_border";
        ctx.Depth = waveIndex;
        ChaosNarrativeDirector.Fire(ctx);
    }

    public static void OnBoonDraft(int waveIndex, ChaosNarrativeContext ctx)
    {
        if (!_active) return;
        // Reserved hook: no default narrator line while the draft UI is open.
    }

    public static void OnFirstPop(ChaosNarrativeContext ctx)
    {
        if (!_active || _sawFirstPop || ChaosHappyPath.IsScripting) return;
        _sawFirstPop = true;
        ctx.Trigger = "first_pop";
        ChaosNarrativeDirector.Fire(ctx);
    }

    public static void OnFirstDefuse(ChaosNarrativeContext ctx)
    {
        if (!_active || _sawFirstDefuse || ChaosHappyPath.IsScripting) return;
        _sawFirstDefuse = true;
        ctx.Trigger = "first_defuse";
        ChaosNarrativeDirector.Fire(ctx);
    }

    public static void OnFirstDetonation(ChaosNarrativeContext ctx)
    {
        if (!_active || _sawFirstDetonation || ChaosHappyPath.IsScripting) return;
        _sawFirstDetonation = true;
        ctx.Trigger = "first_bare_deto";
        ChaosNarrativeDirector.Fire(ctx);
    }

    public static void OnBrinkDefuse(ChaosNarrativeContext ctx)
    {
        if (!_active || ChaosHappyPath.IsScripting) return;
        ctx.Trigger = "brink_defuse";
        ChaosNarrativeDirector.Fire(ctx);
    }

    public static void OnSinAccepted(string sinId, ChaosNarrativeContext ctx)
    {
        if (!_active || ChaosHappyPath.IsScripting) return;
        ctx.Trigger = "sin_accepted";
        ctx.SinId = sinId;
        ChaosNarrativeDirector.Fire(ctx);
    }

    /// <summary>True the first time a bare detonation lands in this run (false thereafter).</summary>
    public static bool TryFirstBareDeto()
    {
        if (_sawBareDeto) return false;
        _sawBareDeto = true;
        return true;
    }

    private static void ShowStandaloneConversation(ChaosConversation convo)
    {
        try
        {
            var win = new ChaosOverlayWindow();
            var bg = ChaosArt.Resolve("hub", "backdrop") ?? ChaosArt.Resolve("backdrops", "depth1");
            win.Show();
            win.ShowConversation(convo, bg, onComplete: () => { try { win.Close(); } catch { } });
        }
        catch (Exception ex)
        {
            LogDebug("ChaosNarrativeHooks.ShowStandaloneConversation: {E}", ex.Message);
        }
    }

    private static void LogDebug(string message, params object?[] args)
    {
        try { App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Debug(message, args); } catch { }
    }
}

public partial class ChaosAnnouncerOverlay
{
    // Real Announce overloads are defined in ChaosAnnouncerOverlay.axaml.cs.
}

#endregion

#region typed app-service facades

public static class AvaloniaChaosApp
{
    public static IChaosService? Chaos => App.Services?.GetService<IChaosService>();
    public static IAvatarWindowService? Avatar => App.Services?.GetService<IAvatarWindowService>();
    public static IBarkService? Bark => App.Services?.GetService<IBarkService>();
    public static IVideoInfo? Video => App.Services?.GetService<IVideoInfo>();
    public static Window? MainWindowRef => App.Services?.GetService<IMainWindowService>()?.MainWindow as Window;
}

#endregion
