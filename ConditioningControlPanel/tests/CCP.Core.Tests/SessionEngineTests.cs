using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

public class SessionEngineTests
{
    private class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public bool WasSettingsFileMissing => true;
        public List<string> PendingPresetReinstalls { get; } = new();
        public void Save() { }
        public void Save(bool suppressCloudBackup = false) { }
        public void SaveImmediate(bool suppressCloudBackup = false) { }
        public void RestoreFrom(AppSettings settings) { }
        public void Reset() { }
    }

    private class FakeProgressionService : IProgressionService
    {
        public void AddXP(int amount, XPSource source) { }
        public double GetSessionXPMultiplier(int playerLevel) => 1.0 + playerLevel * 0.02;
        public double GetXPForLevel(int level) => 100.0;
        public double GetTotalXP(int level, double currentXP) => (level - 1) * 100.0 + currentXP;
        public double GetCurrentLevelXP(int level, double totalXP) => totalXP - (level - 1) * 100.0;
        public event EventHandler<int>? LevelUp { add { } remove { } }
    }

    private static Session CreateSession(int durationMinutes = 10, int bonusXP = 400)
    {
        return new Session
        {
            Id = "test",
            Name = "Test Session",
            DurationMinutes = durationMinutes,
            BonusXP = bonusXP,
            Settings = new SessionSettings(),
            Phases = new List<SessionPhase>
            {
                new() { StartMinute = 0, Name = "Start" },
                new() { StartMinute = 5, Name = "Middle" },
                new() { StartMinute = 10, Name = "End" }
            }
        };
    }

    private static void Tick(SessionService service)
    {
        var method = typeof(SessionService).GetMethod("OnTick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(service, null);
    }

    [AvaloniaFact]
    public async Task StartSession_SetsRunningAndRaisesStarted()
    {
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService());

        bool started = false;
        service.SessionStarted += (_, _) => started = true;

        await service.StartSessionAsync(CreateSession());

        Assert.Equal(SessionState.Running, service.State);
        Assert.NotNull(service.CurrentSession);
        Assert.True(started);
        Assert.Equal(0, service.CurrentPhaseIndex);
    }

    [AvaloniaFact]
    public async Task StartSession_RaisesPhaseChanged_WhenPhasesExist()
    {
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService());

        SessionPhase? phase = null;
        service.PhaseChanged += (_, e) => phase = e.Phase;

        await service.StartSessionAsync(CreateSession());

        Assert.NotNull(phase);
        Assert.Equal("Start", phase!.Name);
    }

    [AvaloniaFact]
    public async Task StartSession_AlreadyRunning_Throws()
    {
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService());
        await service.StartSessionAsync(CreateSession());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartSessionAsync(CreateSession()));
    }

    [AvaloniaFact]
    public async Task StopSession_NotCompleted_DoesNotRaiseCompleted()
    {
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService());
        await service.StartSessionAsync(CreateSession());

        bool completed = false;
        service.SessionCompleted += (_, _) => completed = true;

        service.StopSession(completed: false);

        Assert.Equal(SessionState.Idle, service.State);
        Assert.False(completed);
    }

    [AvaloniaFact]
    public async Task StopSession_Completed_RaisesCompletedWithExpectedXP()
    {
        var settings = new FakeSettingsService();
        settings.Current.PlayerLevel = 1;
        var service = new SessionService(settings, new FakeProgressionService());

        var session = CreateSession(bonusXP: 400);
        await service.StartSessionAsync(session);

        SessionCompletedEventArgs? args = null;
        service.SessionCompleted += (_, e) => args = e;

        service.StopSession(completed: true);

        Assert.NotNull(args);
        Assert.Equal(session, args!.Session);
        // base 400 * multiplier 1.02 = 408, no duration bonus.
        Assert.Equal(408, args.XPEarned);
        Assert.Equal(0, args.PauseCount);
    }

    [AvaloniaFact]
    public async Task PauseSession_IncrementsPauseCountAndAppliesPenalty()
    {
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService());
        await service.StartSessionAsync(CreateSession());

        service.PauseSession();

        Assert.Equal(SessionState.Paused, service.State);
        Assert.Equal(1, service.PauseCount);
        Assert.Equal(100, service.XPPenalty);
    }

    [AvaloniaFact]
    public async Task ResumeSession_ReturnsToRunning()
    {
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService());
        await service.StartSessionAsync(CreateSession());
        service.PauseSession();

        service.ResumeSession();

        Assert.Equal(SessionState.Running, service.State);

        // Ensure the timer tick path is wired up again.
        Tick(service);
        Assert.Equal(SessionState.Running, service.State);
    }

    [AvaloniaFact]
    public async Task StopSession_AfterPause_AppliesXPPenalty()
    {
        var settings = new FakeSettingsService();
        settings.Current.PlayerLevel = 1;
        var service = new SessionService(settings, new FakeProgressionService());

        var session = CreateSession(bonusXP: 400);
        await service.StartSessionAsync(session);
        service.PauseSession();

        SessionCompletedEventArgs? args = null;
        service.SessionCompleted += (_, e) => args = e;

        service.StopSession(completed: true);

        // base 400 - 100 penalty = 300; * 1.02 = 306
        Assert.Equal(306, args!.XPEarned);
        Assert.Equal(1, args.PauseCount);
    }

    [AvaloniaFact]
    public async Task CompletedXP_IncludesDurationBonus()
    {
        var settings = new FakeSettingsService();
        settings.Current.PlayerLevel = 1;
        var service = new SessionService(settings, new FakeProgressionService());

        var session = CreateSession(durationMinutes: 10, bonusXP: 400);
        await service.StartSessionAsync(session);
        service.PauseSession();

        // Fake 5 minutes of elapsed time so the duration bonus applies.
        var elapsedField = typeof(SessionService).GetField("_pausedElapsedTime", BindingFlags.NonPublic | BindingFlags.Instance)!;
        elapsedField.SetValue(service, TimeSpan.FromMinutes(5));

        SessionCompletedEventArgs? args = null;
        service.SessionCompleted += (_, e) => args = e;

        service.StopSession(completed: true);

        // base (400 - 100 pause penalty) * 1.02 = 306; duration minutes = 5 - 2 = 3; bonus = round(3 * (8 + 0.15)) = 24
        Assert.Equal(330, args!.XPEarned);
        Assert.Equal(1, args.PauseCount);
    }

    [AvaloniaFact]
    public async Task PhaseTransition_AdvancesCurrentPhase()
    {
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService());
        await service.StartSessionAsync(CreateSession());

        var phaseChanges = new List<string>();
        service.PhaseChanged += (_, e) => phaseChanges.Add(e.Phase.Name);

        var checkPhase = typeof(SessionService).GetMethod("CheckPhaseTransition", BindingFlags.NonPublic | BindingFlags.Instance)!;
        checkPhase.Invoke(service, new object[] { 5.0 });

        Assert.Equal(1, service.CurrentPhaseIndex);
        Assert.Equal("Middle", phaseChanges[^1]);
    }

    [AvaloniaFact]
    public async Task ProgressPercent_ComputesCorrectly()
    {
        var service = new SessionService(new FakeSettingsService(), new FakeProgressionService());
        await service.StartSessionAsync(CreateSession(durationMinutes: 10));
        service.PauseSession();

        var elapsedField = typeof(SessionService).GetField("_pausedElapsedTime", BindingFlags.NonPublic | BindingFlags.Instance)!;
        elapsedField.SetValue(service, TimeSpan.FromMinutes(2.5));

        Assert.Equal(25.0, service.ProgressPercent, precision: 1);
    }
}
