using System;
using System.Diagnostics;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Quests;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Core.Services.Mantra;

/// <summary>
/// Cross-platform implementation of the mantra typing lab engine.
/// Ported from the legacy WPF <c>Services.MantraService</c> with injected seams
/// so it can run in both WPF and Avalonia heads.
/// </summary>
public sealed class MantraService : IMantraService, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IProgressionService? _progressionService;
    private readonly IQuestService? _questService;
    private readonly Random _random = new();

    private string? _lastMantra;
    private Stopwatch? _mantraTimer;
    private int _completionsThisMinute;
    private DateTime _minuteWindowStart;

    public string? CurrentMantra { get; private set; }
    public int Streak { get; private set; }
    public int BestStreak { get; private set; }
    public int Completions { get; private set; }
    public int TargetCount { get; private set; }
    public bool IsActive { get; private set; }

    public event Action<int>? StreakChanged;
    public event Action? StreakBroken;
    public event Action? MantraCompleted;
    public event Action<int, int>? SessionComplete;

    public MantraService(
        ISettingsService settingsService,
        IProgressionService? progressionService = null,
        IQuestService? questService = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _progressionService = progressionService;
        _questService = questService;
    }

    public void StartSession(int targetReps)
    {
        TargetCount = Math.Clamp(targetReps, 1, 100);
        Completions = 0;
        Streak = 0;
        BestStreak = 0;
        IsActive = true;
        _completionsThisMinute = 0;
        _minuteWindowStart = DateTime.UtcNow;
        _mantraTimer = Stopwatch.StartNew();
        NextMantra();
    }

    public bool TryCompleteMantra()
    {
        if (!IsActive || CurrentMantra == null) return false;

        // Anti-cheat: minimum 1.5s per mantra
        if (_mantraTimer != null && _mantraTimer.Elapsed.TotalSeconds < 1.5)
            return false;

        // Anti-cheat: max 20 completions per minute
        if ((DateTime.UtcNow - _minuteWindowStart).TotalSeconds >= 60)
        {
            _completionsThisMinute = 0;
            _minuteWindowStart = DateTime.UtcNow;
        }
        if (_completionsThisMinute >= 20)
            return false;

        _completionsThisMinute++;
        Completions++;
        Streak++;
        if (Streak > BestStreak) BestStreak = Streak;

        // XP: 30 base + min(streak*5, 50)
        var bonusXP = Math.Min(Streak * 5, 50);
        _progressionService?.AddXP(30 + bonusXP, XPSource.Mantra);
        _questService?.TrackMantraCompleted();

        if (Completions >= TargetCount)
        {
            IsActive = false;
            MantraCompleted?.Invoke();
            StreakChanged?.Invoke(Streak);
            SessionComplete?.Invoke(Completions, BestStreak);
            return true;
        }

        _mantraTimer?.Restart();
        NextMantra();

        MantraCompleted?.Invoke();
        StreakChanged?.Invoke(Streak);
        return true;
    }

    public void BreakStreak()
    {
        if (!IsActive || Streak == 0) return;
        Streak = 0;
        StreakBroken?.Invoke();
        StreakChanged?.Invoke(0);
    }

    public void EndSession()
    {
        if (!IsActive) return;
        IsActive = false;
        CurrentMantra = null;
        _mantraTimer?.Stop();
    }

    private void NextMantra()
    {
        var pool = _settingsService.Current?.MantraPool;
        if (pool == null || pool.Count == 0)
        {
            CurrentMantra = "I am deeply relaxed";
            return;
        }

        if (pool.Count == 1)
        {
            CurrentMantra = pool[0];
            return;
        }

        // No immediate repeats
        string next;
        do
        {
            next = pool[_random.Next(pool.Count)];
        } while (next == _lastMantra && pool.Count > 1);

        _lastMantra = next;
        CurrentMantra = next;
    }

    public void Dispose()
    {
        IsActive = false;
        _mantraTimer?.Stop();
    }
}
