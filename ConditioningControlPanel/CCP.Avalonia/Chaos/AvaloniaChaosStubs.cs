using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using global::Avalonia.Controls;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;
using ConditioningControlPanel;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Chaos;

#region legacy enums / identifiers

public enum ChaosRank { Curious, Slipping, Devoted, Lost, Claimed }
public enum ChaosBranch { Control, Greed, Depth }
public enum ChaosRarity { Common, Uncommon, Rare }
public enum ChaosSpeaker { Madam, Rabbit, Hatter, Doll, Enemy }

public static class RevealIds
{
    public const string TabLookingGlass = "tab_looking_glass";
    public const string SectionToys = "section_toys";
    public const string SectionAccessories = "section_accessories";
    public const string HerCorner = "her_corner";
    public const string PillTeasing = "pill_teasing";
    public const string PillRelentless = "pill_relentless";
    public const string PillInescapable = "pill_inescapable";
    public const string StartPicker = "start_picker";
    public const string Diary = "diary";
    public const string StatsPanel = "stats_panel";
    public const string DraftSkip = "draft_skip";
    public const string BenchToyPocket2 = "bench_toy_pocket_2";
    public const string BenchAccPocket2 = "bench_acc_pocket_2";
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

public sealed class ChaosMetaState
{
    public int RunsCompleted { get; set; }
    public int Sparks { get; set; }
    public int Gold { get; set; }
    public int ToyPockets { get; set; }
    public int AccessoryPockets { get; set; }
    public HashSet<string> BenchPurchases { get; set; } = new();
    public HashSet<string> SeenReveals { get; set; } = new();
    public HashSet<string> PendingReveals { get; set; } = new();
    public bool GiftGiven { get; set; }
    public bool SeenIntroGuide { get; set; }
    public bool SeenDollhouse { get; set; }
    public bool SeenSkipDebut { get; set; }
    public bool ExtremeUnlocked { get; set; }
    public int LastRankSeen { get; set; }
    public double BestScore { get; set; }
    public int BestCombo { get; set; }
    public double TotalRunSeconds { get; set; }
    public double TotalChannelSeconds { get; set; }
    public int TotalDefused { get; set; }
    public string? EquippedStartBoon { get; set; }
}

public sealed class ChaosBoon
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public string? Flavor { get; set; }
    public bool IsCurse { get; set; }
    public ChaosRarity Rarity { get; set; } = ChaosRarity.Common;
    public string? RequiresAny { get; set; }
    public string? RequiresAll { get; set; }
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
    public string ValueLabel { get; set; } = "{0}";
    public double ValueAt(int level) => level;
    public bool IsActiveUse { get; set; }
    public double UseCooldownSec { get; set; }
    public ChaosRank RankFloor { get; set; } = ChaosRank.Curious;
    public string? CapstoneDesc { get; set; }
}

public sealed class ChaosConversationLine
{
    public string Text { get; set; } = "";
    public bool Emphasis { get; set; }
    public string? AudioKey { get; set; }
}

public sealed class ChaosConversation
{
    public List<ChaosConversationLine> Lines { get; set; } = new();
    public string PortraitId { get; set; } = "";
    public bool PortraitOnLeft { get; set; } = true;
    public ChaosSpeaker Speaker { get; set; } = ChaosSpeaker.Madam;
    public string? Title { get; set; }
}

public sealed class ChaosNarrativeContext
{
    public int RankIndex { get; set; }
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
    public string StatusText { get; set; } = "ready";
    public bool IsEffectActive { get; set; }
    public bool IsReady { get; set; } = true;

    public event PropertyChangedEventHandler? PropertyChanged;
    public void RaiseChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ChaosRunConfig
{
    public ChaosPlayMode PlayMode { get; set; } = ChaosPlayMode.Story;
    public string Difficulty { get; set; } = "Easy";
    public static ChaosRunConfig FromSettings() => new();
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
    public double RunProgress { get; set; }
    public string RunTimeText { get; set; } = "0:00";
    public string ActWaveText { get; set; } = "I · 1";
    public double RunDurationSec { get; set; } = 180;
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
    public List<string> RecentEvents { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public void RaiseChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
    public static ChaosMetaState State { get; set; } = new();
    public static string Rank => ChaosRanks.Name(CurrentRank);
    public static ChaosRank CurrentRank => ChaosRank.Curious;
    public static int RankIndex => (int)CurrentRank;
    public const int FIRST_FALL_BONUS = 100;

    public static void Save() { }
    public static bool AtLeast(ChaosRank rank) => CurrentRank >= rank;
    public static bool IsOwned(string id) => false;
    public static bool CanAfford(string id) => false;
    public static bool CanAffordUnlock(string id) => false;
    public static bool CanAffordUpgrade(string id) => false;
    public static bool IsPurchaseRankLocked(string id) => false;
    public static bool IsBoonRankLocked(string id) => false;
    public static bool IsAccessoryScriptLocked(string id) => false;
    public static bool IsBoonUnlocked(string id) => false;
    public static bool IsBoonActive(string id) => false;
    public static void SetBoonActive(string id, bool active) { }
    public static int BoonLevel(string id) => 0;
    public static bool TryUnlockBoon(string id) => false;
    public static bool TryUpgradeBoon(string id) => false;
    public static bool TryPurchase(string id) => false;
    public static bool TrySpendGold(int amount) => false;
    public static bool HasFreePocket(ChaosBoonCategory cat) => false;
    public static int SlotsFor(ChaosBoonCategory cat) => 0;
    public static int EquippedCountIn(ChaosBoonCategory cat) => 0;
    public static bool IsUpgradeActive(string id) => false;
    public static void SetUpgradeActive(string id, bool active) { }
    public static void AddGold(int amount) => State.Gold += amount;
    public static bool IsDiscovered(string key) => false;
    public static void EquipStartBoon(string? id) => State.EquippedStartBoon = id;
    public static (string Name, bool Affordable, string? LessonId, int Cost)? NextGoal() => null;
}

public static class ChaosLessons
{
    public const string T_EXTREME_TIER = "extreme_tier";

    public sealed class Def
    {
        public string Id { get; set; } = "";
        public string Text { get; set; } = "";
        public string? Detail { get; set; }
        public long Target { get; set; } = 1;
        public bool HighWater { get; set; }
    }

    public static Def? ById(string id) => null;
    public static long Progress(string id) => 0;
    public static bool IsLessonBlocked(string id) => false;
    public static void RaiseTo(string id, long target) { }
    public static void Tick(string id, long amount) { }
}

public static class RevealService
{
    public static bool IsUnlocked(string id) => false;
    public static List<string> PendingIds() => new();
    public static void MarkSeen(string id) { }
    public static void Sync(string reason) { }
}

public static class ChaosRanks
{
    public static int[] Thresholds { get; } = { 0, 3, 10, 25, 50 };
    public static string RankLockedTip => "sink deeper to learn this.";
    public static string CapstoneLockedTip => "the capstone waits for Devoted.";

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
    }

    public static List<Variant> All { get; } = new();
    public static string DescriptionFor(string id) => "";
    public static List<BubblePreset> Presets { get; } = new();
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
    public static void PlayCardLine(string? audioKey, string text) { }
    public static void EndCard() { }
}

public static class ChaosNarrativeHooks
{
    public static void OnHubMoment(string moment, ChaosNarrativeContext ctx) { }
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
