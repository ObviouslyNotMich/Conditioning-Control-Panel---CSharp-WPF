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
    public bool ExtremeUnlocked { get; set; } = false;

    /// <summary>Boon id pre-equipped to apply at run start (Loadout tab). Null = none.</summary>
    public string? EquippedStartBoon { get; set; } = null;

    /// <summary>Codex entries the player has encountered (prefixed: "bubble:{id}" / "boon:{id}").</summary>
    public HashSet<string> DiscoveredCodexIds { get; set; } = new();

    /// <summary>Lifetime-boon levels (Skills/Accessories/Utility): id -> level (>=1 means unlocked). 0/absent = locked.</summary>
    public Dictionary<string, int> LifetimeBoonLevels { get; set; } = new();

    /// <summary>Lifetime-boon ids currently toggled on (applied to a run at start, icon shown in the HUD strip).</summary>
    public HashSet<string> ActiveLifetimeBoons { get; set; } = new();

    // lifetime stats (consumed by the Stats tab in a later session)
    public int RunsCompleted { get; set; } = 0;
    public long BestScore { get; set; } = 0;
    public int BestCombo { get; set; } = 0;
    public long TotalDefused { get; set; } = 0;
}
