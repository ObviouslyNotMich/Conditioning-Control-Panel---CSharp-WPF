using System;
using System.IO;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Progression;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

public class AchievementServiceTests
{
    private class TestAppEnvironment : IAppEnvironment
    {
        public string Root { get; }
        public string BaseDirectory { get; } = AppContext.BaseDirectory;
        public string UserDataPath { get; }
        public string ApplicationDataPath { get; }
        public string EffectiveAssetsPath { get; }

        public TestAppEnvironment()
        {
            Root = Path.Combine(Path.GetTempPath(), $"ccp-achievement-tests-{Guid.NewGuid():N}");
            UserDataPath = Path.Combine(Root, "local");
            ApplicationDataPath = Path.Combine(Root, "roaming");
            EffectiveAssetsPath = Path.Combine(Root, "assets");
        }

        public void Cleanup()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    private class TestScheduler : IScheduler
    {
        public IDisposable StartPeriodicTimer(TimeSpan interval, Action callback) => new TestDisposable();
        public IDisposable StartOneShotTimer(TimeSpan dueTime, Action callback) => new TestDisposable();
    }

    private class TestDisposable : IDisposable
    {
        public void Dispose() { }
    }

    private class TestUiDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;
        public void Post(Action action) => action();
        public void Invoke(Action action) => action();
        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public T Invoke<T>(Func<T> func) => func();
    }

    [Fact]
    public void TryUnlock_KnownAchievement_AddsToProgressAndRaisesEvent()
    {
        var env = new TestAppEnvironment();
        try
        {
            var service = new AchievementService(env, new DebugLogger(), new TestScheduler(), new TestUiDispatcher());
            Achievement? raised = null;
            service.AchievementUnlocked += (_, a) => raised = a;

            var result = service.TryUnlock("plastic_initiation");

            Assert.True(result);
            Assert.True(service.Progress.IsUnlocked("plastic_initiation"));
            Assert.NotNull(raised);
            Assert.Equal("plastic_initiation", raised!.Id);
        }
        finally
        {
            env.Cleanup();
        }
    }

    [Fact]
    public void TryUnlock_AlreadyUnlocked_ReturnsFalse()
    {
        var env = new TestAppEnvironment();
        try
        {
            var service = new AchievementService(env, new DebugLogger(), new TestScheduler(), new TestUiDispatcher());
            service.TryUnlock("plastic_initiation");

            var result = service.TryUnlock("plastic_initiation");

            Assert.False(result);
        }
        finally
        {
            env.Cleanup();
        }
    }

    [Fact]
    public void TryUnlock_UnknownAchievement_ReturnsFalse()
    {
        var env = new TestAppEnvironment();
        try
        {
            var service = new AchievementService(env, new DebugLogger(), new TestScheduler(), new TestUiDispatcher());

            var result = service.TryUnlock("not_a_real_achievement");

            Assert.False(result);
        }
        finally
        {
            env.Cleanup();
        }
    }

    [Fact]
    public void SuppressPopups_SilentUnlock_DoesNotRaiseEvent()
    {
        var env = new TestAppEnvironment();
        try
        {
            var service = new AchievementService(env, new DebugLogger(), new TestScheduler(), new TestUiDispatcher());
            service.SuppressPopups = true;
            var raised = false;
            service.AchievementUnlocked += (_, _) => raised = true;

            service.TryUnlock("plastic_initiation");

            Assert.True(service.Progress.IsUnlocked("plastic_initiation"));
            Assert.False(raised);
        }
        finally
        {
            env.Cleanup();
        }
    }

    [Fact]
    public void TrackBubblePopped_MilestoneAwardsSkillPoint()
    {
        var env = new TestAppEnvironment();
        try
        {
            App.Settings = new TestSettingsService();
            var service = new AchievementService(env, new DebugLogger(), new TestScheduler(), new TestUiDispatcher());
            var startingPoints = App.Settings.Current.SkillPoints;

            for (int i = 0; i < 100; i++)
            {
                service.TrackBubblePopped();
            }

            Assert.Equal(startingPoints + 1, App.Settings.Current.SkillPoints);
        }
        finally
        {
            App.Settings = null;
            env.Cleanup();
        }
    }

    [Fact]
    public void TrackSessionComplete_DeepSleep_Unlocks()
    {
        var env = new TestAppEnvironment();
        try
        {
            var service = new AchievementService(env, new DebugLogger(), new TestScheduler(), new TestUiDispatcher());

            service.TrackSessionComplete("Test", 200, false, false);

            Assert.True(service.Progress.IsUnlocked("deep_sleep"));
        }
        finally
        {
            env.Cleanup();
        }
    }

    [Fact]
    public void CheckLevelAchievements_LevelsUnlockAppropriately()
    {
        var env = new TestAppEnvironment();
        try
        {
            var service = new AchievementService(env, new DebugLogger(), new TestScheduler(), new TestUiDispatcher());

            service.CheckLevelAchievements(20);

            Assert.True(service.Progress.IsUnlocked("plastic_initiation"));
            Assert.True(service.Progress.IsUnlocked("dumb_bimbo"));
            Assert.False(service.Progress.IsUnlocked("fully_synthetic"));
        }
        finally
        {
            env.Cleanup();
        }
    }

    [Fact]
    public void Save_PersistsProgress()
    {
        var env = new TestAppEnvironment();
        try
        {
            var service = new AchievementService(env, new DebugLogger(), new TestScheduler(), new TestUiDispatcher());
            service.TryUnlock("plastic_initiation");

            service.Save();

            var json = File.ReadAllText(Path.Combine(env.UserDataPath, "achievements.json"));
            Assert.Contains("plastic_initiation", json);
        }
        finally
        {
            env.Cleanup();
        }
    }

    [Fact]
    public async Task DisposeAsync_SavesAndDisposes()
    {
        var env = new TestAppEnvironment();
        try
        {
            var service = new AchievementService(env, new DebugLogger(), new TestScheduler(), new TestUiDispatcher());
            service.TryUnlock("plastic_initiation");

            await service.DisposeAsync();

            var json = File.ReadAllText(Path.Combine(env.UserDataPath, "achievements.json"));
            Assert.Contains("plastic_initiation", json);
        }
        finally
        {
            env.Cleanup();
        }
    }

    private class TestSettingsService : IAppSettingsService
    {
        public AppSettings Current { get; } = new();
        public void Save() { }
    }
}
