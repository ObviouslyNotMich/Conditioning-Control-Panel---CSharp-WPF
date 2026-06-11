using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Persistent meta-progression save model for Chaos Mode — banked between runs and
/// loaded once at startup (<see cref="ChaosMeta.Init"/>). Serialized to
/// <c>chaos_meta.json</c> in the same folder as settings.json. Additive-only: every
/// field has a neutral default, so a fresh state leaves a run byte-for-byte unchanged.
/// </summary>
public sealed class ChaosMetaState
{
    public int SchemaVersion { get; set; } = 1;   // for future migrations

    public int Sparks { get; set; } = 0;
    public HashSet<string> PurchasedUpgrades { get; set; } = new();
    /// <summary>Trained habits the player has switched OFF (absent = on, so old saves stay fully active).</summary>
    public HashSet<string> DisabledUpgrades { get; set; } = new();
    public bool ExtremeUnlocked { get; set; } = false;

    /// <summary>Boon id pre-equipped to apply at run start (Loadout tab). Null = none.</summary>
    public string? EquippedStartBoon { get; set; } = null;

    /// <summary>Codex entries the player has encountered (prefixed: "bubble:{id}" / "boon:{id}").</summary>
    public HashSet<string> DiscoveredCodexIds { get; set; } = new();

    /// <summary>Lifetime-boon levels (Skills/Accessories/Utility): id -> level (>=1 means unlocked). 0/absent = locked.</summary>
    public Dictionary<string, int> LifetimeBoonLevels { get; set; } = new();

    /// <summary>Lifetime-boon ids currently toggled on (applied to a run at start, icon shown in the HUD strip).</summary>
    public HashSet<string> ActiveLifetimeBoons { get; set; } = new();

    // ---- hold-to-defuse onboarding (2026-06-11 verb rework) — all default false so old saves load clean ----
    public bool SeenDefuseTutorial { get; set; } = false;
    public bool SeenBarkDefuseFirst { get; set; } = false;
    public bool SeenBarkDefuseNoFocus { get; set; } = false;
    public bool SeenBarkDefuseRelease { get; set; } = false;
    public bool SeenBarkClickDetonate { get; set; } = false;

    // ---- behavioral-bubble debuts: first encounter spawns alone with an extended trance ----
    public bool SeenEcho { get; set; } = false;
    public bool SeenChaperone { get; set; } = false;
    public bool SeenTease { get; set; } = false;
    public bool SeenBound { get; set; } = false;
    public bool SeenBrittle { get; set; } = false;

    // ---- two-currency split (2026-06-11): Sparks (code name frozen) is the DROPS balance,
    // banked end-of-run; Gold is the instant in-run balance, spent only at her bench ----
    public int Gold { get; set; } = 0;

    // ---- pockets are purchase-driven now: counts start at zero, her bench sews more ----
    public int ToyPockets { get; set; } = 0;
    public int AccessoryPockets { get; set; } = 0;

    /// <summary>Gold purchases at her bench (non-power conveniences): id -> owned.</summary>
    public HashSet<string> BenchPurchases { get; set; } = new();

    /// <summary>One-time auto-cover of a short balance on the first toy pocket attempt.</summary>
    public bool GiftGiven { get; set; } = false;

    // ---- lessons (challenge-gated buyability): id == purchasable id ----
    public Dictionary<string, long> LessonProgress { get; set; } = new();
    public HashSet<string> LessonsComplete { get; set; } = new();

    // ---- reveal framework: element ids pending their dollhouse flash / already flashed ----
    public HashSet<string> PendingReveals { get; set; } = new();
    public HashSet<string> SeenReveals { get; set; } = new();

    // ---- first-times bonuses (drops, one-time each): first_taste/first_snap/first_whisper/first_yes/first_play ----
    public HashSet<string> FirstTimesAwarded { get; set; } = new();

    // ---- happy-path scripted beats ----
    public bool SeenDuoDemo { get; set; } = false;
    public bool SeenSkipDebut { get; set; } = false;
    public bool SeenGoldFirst { get; set; } = false;
    public bool SeenDollhouse { get; set; } = false;
    public bool SeenFirstSin { get; set; } = false;

    /// <summary>Highest rank index the player has been shown a rank card for (0 = curious).</summary>
    public int LastRankSeen { get; set; } = 0;

    // lifetime stats (consumed by the Stats tab in a later session)
    public int RunsCompleted { get; set; } = 0;
    public long BestScore { get; set; } = 0;
    public int BestCombo { get; set; } = 0;
    public long TotalDefused { get; set; } = 0;
    /// <summary>Total time spent down the hole across all completed descents, in seconds.</summary>
    public double TotalRunSeconds { get; set; } = 0;
}
