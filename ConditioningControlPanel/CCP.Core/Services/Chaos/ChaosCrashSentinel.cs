using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// Dirty-shutdown detector for Rabbit Hole runs.
///
/// The signature chaos crash is a NATIVE process vanish (OOM / access violation) mid-run: it
/// bypasses every managed handler, so nothing lands in crash.log and the app log simply stops.
/// On tester machines we have no other evidence it even happened.
///
/// This writes a small flag file the moment a run goes live and refreshes it with the run's
/// context (version, FX flags, difficulty, monitors, elapsed, peak native MB, bubble count). A
/// clean teardown OR a clean app shutdown deletes it. So if the file is still on disk at the NEXT
/// launch, the previous chaos session died abnormally — and <see cref="ConsumeAndReport"/> logs
/// exactly that, with the last-known context, turning an invisible vanish into one diagnostic
/// line in the tester's app log.
/// </summary>
public sealed class ChaosCrashSentinel
{
    private readonly IAppEnvironment _appEnvironment;
    private readonly ILogger<ChaosCrashSentinel>? _logger;

    public ChaosCrashSentinel(IAppEnvironment appEnvironment, ILogger<ChaosCrashSentinel>? logger = null)
    {
        _appEnvironment = appEnvironment ?? throw new ArgumentNullException(nameof(appEnvironment));
        _logger = logger;
    }

    private string FilePath => Path.Combine(_appEnvironment.UserDataPath, "logs", "chaos_session.active");

    /// <summary>Arm/refresh the sentinel with the latest run context (idempotent overwrite).</summary>
    public void Mark(string contextLine)
    {
        try
        {
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contextLine ?? "");
        }
        catch { /* diagnostics must never throw into a run */ }
    }

    /// <summary>Clear the sentinel — a run ended (or the app is shutting down) cleanly.</summary>
    public void Clear()
    {
        try
        {
            var path = FilePath;
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    /// <summary>
    /// Called once at startup. If a sentinel survived from a prior session, the previous chaos
    /// run did not end cleanly (almost certainly a native crash) — log it loudly and consume the
    /// file so it only reports once.
    /// </summary>
    public void ConsumeAndReport()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path)) return;

            string ctx = "";
            try { ctx = File.ReadAllText(path).Trim(); } catch { }

            _logger?.LogWarning(
                "[CHAOSCRASH] DETECTED: previous chaos session ended abnormally (no clean teardown — " +
                "likely a native crash/OOM, which leaves nothing in crash.log). Last context: {Context}",
                string.IsNullOrWhiteSpace(ctx) ? "(unavailable)" : ctx);

            Clear();
        }
        catch { }
    }
}
