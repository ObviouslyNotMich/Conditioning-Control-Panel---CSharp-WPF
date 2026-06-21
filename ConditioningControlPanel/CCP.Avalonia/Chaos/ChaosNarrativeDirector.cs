using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ChaosNarrativeContext = ConditioningControlPanel.Core.Services.Chaos.ChaosNarrativeContext;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>
/// Avalonia port of the Madam's narrative director. Picks eligible narrative lines and
/// conversations based on run/hub context, gating, cooldowns, and seen-once state.
/// Conversations are returned to callers (so the run overlay or a standalone hub window
/// can display them); single reactive/ambient lines are spoken through <see cref="ChaosNarrator"/>.
///
/// Persistence lives on <see cref="ChaosMeta.State.SeenNarrativeLines"/> and
/// <see cref="ChaosMeta.State.NarrativeCooldownEnds"/>; rank awareness uses
/// <see cref="ChaosMeta.State.LastRankSeen"/> indirectly through <see cref="ChaosNarrativeContext.RankIndex"/>.
/// </summary>
public static class ChaosNarrativeDirector
{
    private const int NARRATOR_MIN_GAP_MS = 3500;   // REACTIVE/AMBIENT wait this long between lines
    private static DateTime _lastSpokeUtc = DateTime.MinValue;

    public static void Init()
    {
        _lastSpokeUtc = DateTime.MinValue;
    }

    /// <summary>
    /// Select an eligible conversation for <paramref name="category"/> (the trigger key).
    /// Returns null when no conversation matches or narrative mode is off.
    /// The caller is responsible for displaying the returned conversation.
    /// </summary>
    public static ChaosConversation? Pick(ChaosNarrativeContext ctx, string category)
    {
        try
        {
            if (!AvaloniaChaosMode.NarrativeActive) return null;

            ctx.Trigger = category;
            bool hub = ctx.Depth <= 0 || category.StartsWith("hub_", StringComparison.OrdinalIgnoreCase);
            var convo = SelectConversation(ctx, hub);
            if (convo == null) return null;

            if (convo.Mode == ChaosConversationMode.Once && ChaosMeta.State.SeenNarrativeLines.Add(convo.Id))
                ChaosMeta.Save();

            return convo;
        }
        catch (Exception ex)
        {
            LogDebug("ChaosNarrativeDirector.Pick: {E}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// In-run narrative dispatch. Tries a STORY conversation first, then falls back to a
    /// single reactive/ambient line. Conversation display is owned by the caller via <see cref="Pick"/>;
    /// this method only fires single lines.
    /// </summary>
    internal static void Fire(ChaosNarrativeContext ctx)
    {
        try
        {
            if (!AvaloniaChaosMode.NarrativeActive) return;

            // STORY conversations are handled by Pick/ShowConversation. If one is eligible,
            // do not also speak a reactive line at the same beat.
            if (SelectConversation(ctx, hub: false) != null) return;

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var register = RegisterForDepth(ctx.Depth);

            var eligible = Catalog
                .Where(c => c.Trigger == ctx.Trigger)
                .Where(c => RegisterMatches(c.Register, register, ctx.Depth))
                .Where(c => GatesPass(c, ctx))
                .Where(c => !(c.Mode == ChaosLineMode.Once && IsSeen(c.Id)))
                .Where(c => !OnCooldown(c, nowMs))
                .ToList();
            if (eligible.Count == 0) return;

            var band = eligible.Max(c => c.Band);
            var bucket = eligible.Where(c => c.Band == band).ToList();

            bool interrupt = band == ChaosBand.Story;
            if (!interrupt && (DateTime.UtcNow - _lastSpokeUtc).TotalMilliseconds < NARRATOR_MIN_GAP_MS)
                return;

            var pick = Select(bucket);
            if (pick == null) return;

            _lastSpokeUtc = DateTime.UtcNow;
            Commit(pick, nowMs);
            ChaosNarrator.Speak(pick, interrupt);
        }
        catch (Exception ex)
        {
            LogDebug("ChaosNarrativeDirector.Fire: {E}", ex.Message);
        }
    }

    /// <summary>
    /// Hub-side dispatch (no live run). Conversations only — the Madam never throws a reactive
    /// overlay line at the hub. Display is owned by the caller via <see cref="Pick"/>.
    /// </summary>
    internal static void FireHub(ChaosNarrativeContext ctx)
    {
        try
        {
            if (!AvaloniaChaosMode.NarrativeActive) return;
            SelectConversation(ctx, hub: true);
        }
        catch (Exception ex)
        {
            LogDebug("ChaosNarrativeDirector.FireHub: {E}", ex.Message);
        }
    }

    // ---- conversation selection ----
    private static ChaosConversation? SelectConversation(ChaosNarrativeContext ctx, bool hub)
    {
        var convo = Conversations
            .Where(c => c.Trigger == ctx.Trigger)
            .Where(c => hub ? c.Register == ChaosRegister.Hub : c.Register != ChaosRegister.Hub)
            .Where(c => GatesPass(c.Gates, ctx, c.Id))
            .Where(c => !(c.Mode == ChaosConversationMode.Once && IsSeen(c.Id)))
            .FirstOrDefault();
        return convo;
    }

    // ---- selection: unseen first, then highest weight, then random ----
    private static ChaosNarrativeCue? Select(List<ChaosNarrativeCue> bucket)
    {
        if (bucket.Count == 0) return null;
        var unseen = bucket.Where(c => !IsSeen(c.Id)).ToList();
        var pool = unseen.Count > 0 ? unseen : bucket;
        int maxW = pool.Max(c => Math.Max(1, c.Weight));
        var top = pool.Where(c => Math.Max(1, c.Weight) == maxW).ToList();
        return top[Random.Shared.Next(top.Count)];
    }

    // ---- register ----
    private static ChaosRegister RegisterForDepth(int depth) =>
        depth <= 2 ? ChaosRegister.DescentHigh : ChaosRegister.DescentLow;

    private static bool RegisterMatches(ChaosRegister lineReg, ChaosRegister ctxReg, int depth)
    {
        if (lineReg == ChaosRegister.Hub) return false;          // hub lines never fire in a run
        if (depth == 3) return lineReg != ChaosRegister.Hub;     // depth III: either descent register
        return lineReg == ctxReg;
    }

    // ---- gates ----
    private static bool GatesPass(ChaosNarrativeCue c, ChaosNarrativeContext ctx) => GatesPass(c.Gates, ctx, c.Id);

    private static bool GatesPass(ChaosLineGate? g, ChaosNarrativeContext ctx, string id)
    {
        if (g == null) return true;
        if (g.RankMin > 0 && ctx.RankIndex < g.RankMin) return false;
        if (g.DepthMatch is { } dm)
        {
            bool ok = dm switch
            {
                ChaosDepthMatch.Min => ctx.Depth >= g.DepthA,
                ChaosDepthMatch.Exact => ctx.Depth == g.DepthA,
                ChaosDepthMatch.Range => ctx.Depth >= g.DepthA && ctx.Depth <= g.DepthB,
                _ => true,
            };
            if (!ok) return false;
        }
        if (g.FirstTime is { } ft && ft && IsSeen(id)) return false;
        if (!string.IsNullOrEmpty(g.ItemOwned) &&
            (ctx.OwnedItemIds == null || !ctx.OwnedItemIds.Contains(g.ItemOwned))) return false;
        if (!string.IsNullOrEmpty(g.SinId) && !string.Equals(g.SinId, ctx.SinId, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(g.RunStatKey))
        {
            double v = ctx.RunStats != null && ctx.RunStats.TryGetValue(g.RunStatKey!, out var sv) ? sv : 0;
            if (v < g.RunStatMin) return false;
        }
        return true;
    }

    // ---- persistence ----
    private static bool IsSeen(string id) => ChaosMeta.State.SeenNarrativeLines.Contains(id);

    private static bool OnCooldown(ChaosNarrativeCue c, long nowMs)
    {
        if (c.CooldownMs <= 0) return false;
        return ChaosMeta.State.NarrativeCooldownEnds.TryGetValue(c.Id, out var end) && nowMs < end;
    }

    private static void Commit(ChaosNarrativeCue c, long nowMs)
    {
        bool changed = false;
        if (c.Mode == ChaosLineMode.Once) changed |= ChaosMeta.State.SeenNarrativeLines.Add(c.Id);
        if (c.CooldownMs > 0) { ChaosMeta.State.NarrativeCooldownEnds[c.Id] = nowMs + c.CooldownMs; changed = true; }
        if (changed) ChaosMeta.Save();
    }

    private static void LogDebug(string message, params object?[] args)
    {
        try { App.Services?.GetService<global::ConditioningControlPanel.IAppLogger>()?.Debug(message, args); } catch { }
    }

    // ========================================================================
    // PLACEHOLDER catalog — mirrors the WPF narrative slice. AudioKey points at
    // assets/Chaos/narrator/{key}.mp3 (absent for now → text-only, still ducks).
    // ========================================================================
    private static readonly List<ChaosNarrativeCue> Catalog = new()
    {
        // ---- run_start ----
        new() { Id="madam.start.story_first", Trigger="run_start", Band=ChaosBand.Story, Mode=ChaosLineMode.Once,
                Register=ChaosRegister.DescentHigh, AudioKey="madam_start_first",
                Text="Back so soon? Down you go." },
        new() { Id="madam.start.pool_a", Trigger="run_start", Band=ChaosBand.Reactive, Mode=ChaosLineMode.Pooled,
                Register=ChaosRegister.DescentHigh, CooldownMs=20000, Weight=2, AudioKey="madam_start_a",
                Text="The hole opens. Fall in." },
        new() { Id="madam.start.pool_b", Trigger="run_start", Band=ChaosBand.Reactive, Mode=ChaosLineMode.Pooled,
                Register=ChaosRegister.DescentHigh, CooldownMs=20000, Weight=1, AudioKey="madam_start_b",
                Text="Mind the bubbles, pet." },

        // ---- zone_border (depth advance) ----
        new() { Id="madam.zone.story_ii", Trigger="zone_border", Band=ChaosBand.Story, Mode=ChaosLineMode.Once,
                Register=ChaosRegister.DescentHigh, Gates=new(){ DepthMatch=ChaosDepthMatch.Exact, DepthA=2 },
                AudioKey="madam_zone_ii", Text="Deeper now. It gets warmer here." },
        new() { Id="madam.zone.pool_a", Trigger="zone_border", Band=ChaosBand.Reactive, Mode=ChaosLineMode.Pooled,
                Register=ChaosRegister.DescentHigh, CooldownMs=8000, Weight=2, AudioKey="madam_zone_a",
                Text="Another floor falls away." },
        new() { Id="madam.zone.pool_low", Trigger="zone_border", Band=ChaosBand.Reactive, Mode=ChaosLineMode.Pooled,
                Register=ChaosRegister.DescentLow, CooldownMs=8000, Weight=2, AudioKey="madam_zone_low",
                Text="No floor left to stand on." },

        // ---- first_bare_deto ----
        new() { Id="madam.deto.story_first", Trigger="first_bare_deto", Band=ChaosBand.Story, Mode=ChaosLineMode.Once,
                Register=ChaosRegister.DescentHigh, AudioKey="madam_deto_first",
                Text="There it is. It got in." },
        new() { Id="madam.deto.pool_a", Trigger="first_bare_deto", Band=ChaosBand.Reactive, Mode=ChaosLineMode.Pooled,
                Register=ChaosRegister.DescentHigh, CooldownMs=12000, Weight=2, AudioKey="madam_deto_a",
                Text="You let one bloom." },
        new() { Id="madam.deto.pool_low", Trigger="first_bare_deto", Band=ChaosBand.Reactive, Mode=ChaosLineMode.Pooled,
                Register=ChaosRegister.DescentLow, CooldownMs=12000, Weight=1, AudioKey="madam_deto_low",
                Text="Good. Stop fighting it." },

        // ---- brink_defuse ----
        new() { Id="madam.brink.pool_a", Trigger="brink_defuse", Band=ChaosBand.Reactive, Mode=ChaosLineMode.Pooled,
                Register=ChaosRegister.DescentHigh, CooldownMs=9000, Weight=2, AudioKey="madam_brink_a",
                Text="Cutting it close, aren't you." },
        new() { Id="madam.brink.pool_b", Trigger="brink_defuse", Band=ChaosBand.Reactive, Mode=ChaosLineMode.Pooled,
                Register=ChaosRegister.DescentHigh, CooldownMs=9000, Weight=1, AudioKey="madam_brink_b",
                Text="So steady. I'm impressed." },
        new() { Id="madam.brink.pool_low", Trigger="brink_defuse", Band=ChaosBand.Reactive, Mode=ChaosLineMode.Pooled,
                Register=ChaosRegister.DescentLow, CooldownMs=9000, Weight=2, AudioKey="madam_brink_low",
                Text="Holding on changes nothing." },

        // ---- sin_accepted ----
        new() { Id="madam.sin.story_first", Trigger="sin_accepted", Band=ChaosBand.Story, Mode=ChaosLineMode.Once,
                Register=ChaosRegister.DescentHigh, AudioKey="madam_sin_first",
                Text="You said yes. I'll remember that." },
        new() { Id="madam.sin.pool_a", Trigger="sin_accepted", Band=ChaosBand.Reactive, Mode=ChaosLineMode.Pooled,
                Register=ChaosRegister.DescentHigh, CooldownMs=10000, Weight=2, AudioKey="madam_sin_a",
                Text="Greedy. I like that." },
        new() { Id="madam.sin.pool_low", Trigger="sin_accepted", Band=ChaosBand.Reactive, Mode=ChaosLineMode.Pooled,
                Register=ChaosRegister.DescentLow, CooldownMs=10000, Weight=2, AudioKey="madam_sin_low",
                Text="Yes. Take it. Take all of it." },
    };

    // ========================================================================
    // PLACEHOLDER conversations — hub greeting + depth-V beat.
    // ========================================================================
    private static readonly List<ChaosConversation> Conversations = new()
    {
        // ---- hub_return: the Madam greets you back at the dollhouse ----
        new()
        {
            Id = "madam.convo.hub_return", Trigger = "hub_return", Speaker = ChaosSpeaker.Madam,
            Title = "of the Rabbit Hole", Register = ChaosRegister.Hub,
            Mode = ChaosConversationMode.Repeatable, PortraitId = "madam", PortraitOnLeft = false,
            Lines =
            {
                new() { Text = "There you are. I wondered if you'd come back." },
                new() { Text = "You always do.", Emphasis = true },
                new() { Text = "Well? The hole won't fall into itself. Down you go." },
            },
        },

        // ---- depthV_enter: the floor runs out, mid-descent ----
        new()
        {
            Id = "madam.convo.depthV", Trigger = "depthV_enter", Speaker = ChaosSpeaker.Madam,
            Title = "of the Rabbit Hole", Register = ChaosRegister.DescentLow,
            Mode = ChaosConversationMode.Repeatable, PortraitId = "madam", PortraitOnLeft = false,
            Lines =
            {
                new() { Text = "There's no floor here. There never was." },
                new() { Text = "Stop reaching for one.", Emphasis = true },
            },
        },
    };
}
