using System;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Services.Companion;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Services.Companion;

/// <summary>
/// Avalonia/Core implementation of <see cref="ICompanionService"/>.
/// Owns active-companion state, per-companion progress, and XP application.
/// Does not depend on WPF-specific dispatch or service locators.
/// </summary>
public sealed class AvaloniaCompanionService : ICompanionService, IDisposable
{
    private const double DrainXpPerTick = 3.0;
    private const double DrainIntervalSeconds = 2.0;

    private readonly ISettingsService _settings;
    private readonly ICommunityPromptService? _prompts;
    private readonly ILogger<AvaloniaCompanionService>? _logger;

    private DispatcherTimer? _drainTimer;
    private bool _disposed;

    public AvaloniaCompanionService(
        ISettingsService settings,
        ICommunityPromptService? prompts = null,
        ILogger<AvaloniaCompanionService>? logger = null)
    {
        _settings = settings;
        _prompts = prompts;
        _logger = logger;

        UpdateDrainTimer();

        _logger?.LogInformation("AvaloniaCompanionService initialized. Active companion: {Companion}",
            ActiveCompanionDef.Name);
    }

    public CompanionId ActiveCompanion =>
        (CompanionId)(_settings.Current?.ActiveCompanionId ?? 0);

    public CompanionDefinition ActiveCompanionDef =>
        CompanionDefinition.GetById(ActiveCompanion);

    public CompanionProgress ActiveProgress =>
        GetProgress(ActiveCompanion);

    public event EventHandler<CompanionId>? CompanionSwitched;
    public event EventHandler<(CompanionId Companion, double Amount, double Modifier)>? XPAwarded;
    public event EventHandler<(CompanionId Companion, int NewLevel)>? LevelUp;
    public event EventHandler<double>? XPDrained;

    public CompanionProgress GetProgress(CompanionId id)
    {
        var settings = _settings.Current;
        if (settings == null)
            return CompanionProgress.CreateNew(id);

        if (!settings.CompanionProgressData.TryGetValue((int)id, out var progress))
        {
            progress = CompanionProgress.CreateNew(id);
            settings.CompanionProgressData[(int)id] = progress;
        }
        return progress;
    }

    public bool SwitchCompanion(CompanionId newCompanion)
    {
        var def = CompanionDefinition.GetById(newCompanion);
        var oldCompanion = ActiveCompanion;
        if (oldCompanion == newCompanion)
            return true;

        var settings = _settings.Current;
        if (settings == null)
            return false;

        settings.ActiveCompanionId = (int)newCompanion;

        var progress = GetProgress(newCompanion);
        if (progress.FirstActivated == DateTime.MinValue)
            progress.FirstActivated = DateTime.Now;

        ApplyCompanionPrompt(newCompanion);
        _settings.Save();

        UpdateDrainTimer();
        CompanionSwitched?.Invoke(this, newCompanion);
        _logger?.LogInformation("Switched companion: {Old} -> {New}",
            CompanionDefinition.GetById(oldCompanion).Name, def.Name);
        return true;
    }

    public void AddCompanionXP(double baseAmount, XPSource source)
    {
        var settings = _settings.Current;
        if (settings == null) return;

        var companionId = ActiveCompanion;
        var progress = GetProgress(companionId);
        if (progress.IsMaxLevel)
        {
            _logger?.LogDebug("Companion {Companion} is max level, XP not awarded", companionId);
            return;
        }

        double modifier = 1.0;
        var companion = ActiveCompanionDef;
        switch (companion.BonusType)
        {
            case CompanionBonusType.PinkFilterBonus:
                if (settings.PinkFilterEnabled && settings.PinkFilterOpacity > 0)
                    modifier = 1.0 + (settings.PinkFilterOpacity / 100.0);
                break;
            case CompanionBonusType.AutonomyBonus:
                // Autonomy flag is not available here; keep base modifier.
                break;
            case CompanionBonusType.StrictModeBonus:
                if (!settings.StrictLockEnabled)
                    modifier = 0.5;
                else if (!settings.PanicKeyEnabled && settings.AttentionChecksEnabled)
                    modifier = 2.0;
                break;
            case CompanionBonusType.SessionCompletionBonus:
                if (source == XPSource.Session)
                    modifier = 1.25;
                break;
        }

        var finalAmount = baseAmount * modifier;
        progress.CurrentXP += finalAmount;
        progress.TotalXPEarned += finalAmount;

        int levelsGained = 0;
        while (progress.CurrentXP >= progress.XPForNextLevel && !progress.IsMaxLevel)
        {
            progress.CurrentXP -= progress.XPForNextLevel;
            progress.Level++;
            levelsGained++;
            _logger?.LogInformation("Companion {Companion} leveled up to {Level}!",
                ActiveCompanionDef.Name, progress.Level);
            LevelUp?.Invoke(this, (companionId, progress.Level));
        }

        _settings.Save();
        XPAwarded?.Invoke(this, (companionId, finalAmount, modifier));
    }

    public void OnAttentionCheckFailed()
    {
        if (ActiveCompanionDef.BonusType != CompanionBonusType.StrictModeBonus)
            return;

        var progress = ActiveProgress;
        const double penalty = 25.0;
        progress.CurrentXP = Math.Max(0, progress.CurrentXP - penalty);
        _settings.Save();

        _logger?.LogInformation("Trainer penalty: -{Penalty} XP for attention check fail. Current XP: {XP:F1}",
            penalty, progress.CurrentXP);
    }

    public string GetCompanionStatusText()
    {
        var progress = ActiveProgress;
        if (progress.IsMaxLevel)
            return $"{ActiveCompanionDef.Name} · MAX LEVEL · Complete!";
        return $"{ActiveCompanionDef.Name} · Level {progress.Level} · {progress.CurrentXP:F0} / {progress.XPForNextLevel:F0} XP";
    }

    private static DispatcherTimer StartPeriodicTimer(TimeSpan interval, Action callback)
    {
        var timer = new DispatcherTimer { Interval = interval };
        timer.Tick += (_, _) => callback();
        timer.Start();
        return timer;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _drainTimer?.Stop();
        _drainTimer = null;
    }

    private void UpdateDrainTimer()
    {
        _drainTimer?.Stop();
        _drainTimer = null;

        if (ActiveCompanionDef.BonusType != CompanionBonusType.XPDrain)
            return;

        _drainTimer = StartPeriodicTimer(TimeSpan.FromSeconds(DrainIntervalSeconds), OnDrainTick);

        _logger?.LogInformation("Brain Parasite drain timer started ({DrainRate} XP/sec)",
            DrainXpPerTick / DrainIntervalSeconds);
    }

    private void OnDrainTick()
    {
        var settings = _settings.Current;
        if (settings == null) return;

        // Can't drain below 0 — don't decrease level
        if (settings.PlayerXP <= 0)
            return;

        settings.PlayerXP = Math.Max(0, settings.PlayerXP - DrainXpPerTick);
        _settings.Save();

        XPDrained?.Invoke(this, DrainXpPerTick);

        _logger?.LogDebug("Brain Parasite drained {Amount} player XP. Current: {XP:F1}",
            DrainXpPerTick, settings.PlayerXP);
    }

    private void ApplyCompanionPrompt(CompanionId companion)
    {
        try
        {
            var promptId = _settings.Current?.GetCompanionPromptId((int)companion);
            if (string.IsNullOrEmpty(promptId) || _prompts == null)
                return;

            _prompts.ActivatePrompt(promptId);
            _logger?.LogInformation("Activated prompt '{PromptId}' for companion {Companion}",
                promptId, CompanionDefinition.GetById(companion).Name);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to apply companion prompt");
        }
    }
}
