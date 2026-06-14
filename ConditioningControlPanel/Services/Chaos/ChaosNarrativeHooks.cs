namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Static in-run dispatch for the narrative layer — the same one-line-call pattern as
/// <see cref="ChaosLessonHooks"/>. <see cref="ChaosModeService"/> calls <see cref="OnMoment"/> at each
/// scripted beat; this gates and forwards to <see cref="ChaosNarrativeDirector"/>. All state is
/// run-scoped and cleared by <see cref="OnRunEnded"/>; nothing here can throw into the run.
///
/// Gating (the scope guardrail): the Madam is silent unless NarrativeModeEnabled, and stays silent
/// while the happy path scripts the early descents (<see cref="ChaosHappyPath.IsScripting"/>) — which
/// is what keeps her off the run-4 rigged-sin announcement, and matches "learn first, the voice deepens
/// later". Moments fired from gameplay paths (detonate/defuse/act-change) are already past the
/// run's pause guards, so a paused/draft field never speaks a gameplay line.
/// </summary>
public static class ChaosNarrativeHooks
{
    private static bool _active;
    private static bool _sawBareDeto;   // first_bare_deto fires once per run

    public static void OnRunStarted()
    {
        _active = true;
        _sawBareDeto = false;
    }

    public static void OnRunEnded()
    {
        _active = false;
        _sawBareDeto = false;
        ChaosNarrator.Reset();   // drop any duck/speaking state — never outlive the run
    }

    /// <summary>Forward a moment to the director if the narrator is allowed to speak right now.</summary>
    public static void OnMoment(string trigger, ChaosNarrativeContext ctx)
    {
        if (!_active) return;
        if (App.Settings?.Current?.NarrativeModeEnabled != true) return;
        if (ChaosHappyPath.IsScripting) return;   // tutorial descents stay quiet (and no run-4 double-announce)
        ctx.Trigger = trigger;
        ChaosNarrativeDirector.Fire(ctx);
    }

    /// <summary>
    /// A hub-side moment (no live run) — e.g. returning to the dollhouse. Routes to the conversation
    /// path only (the Madam doesn't throw reactive overlay lines at the hub). Not gated on the run/scripting
    /// flags, since there is no descent here; still honors NarrativeModeEnabled (checked in the director).
    /// </summary>
    public static void OnHubMoment(string trigger, ChaosNarrativeContext ctx)
    {
        ctx.Trigger = trigger;
        ChaosNarrativeDirector.FireHub(ctx);
    }

    /// <summary>True the first time a bare detonation lands in this run (false thereafter).</summary>
    public static bool TryFirstBareDeto()
    {
        if (_sawBareDeto) return false;
        _sawBareDeto = true;
        return true;
    }
}
