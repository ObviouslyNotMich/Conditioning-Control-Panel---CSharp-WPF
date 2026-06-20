using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

public class ServiceExtractionTests
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
            Root = Path.Combine(Path.GetTempPath(), $"ccp-core-tests-{Guid.NewGuid():N}");
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

    private class TestSecretStore : ISecretStore
    {
        private readonly Dictionary<string, byte[]> _store = new();
        public void Store(string key, byte[] value) => _store[key] = value.ToArray();
        public byte[]? Retrieve(string key) => _store.TryGetValue(key, out var v) ? v.ToArray() : null;
        public void Delete(string key) => _store.Remove(key);
    }

    private class TestDisposable : IDisposable
    {
        public void Dispose() { }
    }

    private class TestScheduler : IScheduler
    {
        public IDisposable StartPeriodicTimer(TimeSpan interval, Action callback) => new TestDisposable();
        public IDisposable StartOneShotTimer(TimeSpan dueTime, Action callback) => new TestDisposable();
    }

    private class TestProgressionService : IProgressionService
    {
        public void AddXP(int amount, XPSource source) { }
        public double GetSessionXPMultiplier(int playerLevel) => 1.0 + playerLevel * 0.02;
        public double GetXPForLevel(int level) => 100.0;
        public event EventHandler<int>? LevelUp { add { } remove { } }
    }

    private class TestSettingsBackupProvider : ISettingsBackupProvider
    {
        public bool HasCloudIdentity => false;
        public Task BackupSettingsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public void SettingsService_LoadsDefaults_WhenFileMissing()
    {
        var env = new TestAppEnvironment();
        try
        {
            var service = new SettingsService(env, new TestSecretStore(), new TestSettingsBackupProvider());

            Assert.True(service.WasSettingsFileMissing);
            Assert.NotNull(service.Current);
        }
        finally
        {
            env.Cleanup();
        }
    }

    [Fact]
    public void SettingsService_SaveAndRestore_RoundTrips()
    {
        var env = new TestAppEnvironment();
        try
        {
            Directory.CreateDirectory(env.UserDataPath);
            var service = new SettingsService(env, new TestSecretStore(), new TestSettingsBackupProvider());

            service.Current.PlayerLevel = 42;
            service.SaveImmediate();

            var service2 = new SettingsService(env, new TestSecretStore(), new TestSettingsBackupProvider());
            Assert.False(service2.WasSettingsFileMissing);
            Assert.Equal(42, service2.Current.PlayerLevel);
        }
        finally
        {
            env.Cleanup();
        }
    }

    [Fact]
    public async Task SessionService_StartStop_CompletesAndRaisesEvents()
    {
        var env = new TestAppEnvironment();
        try
        {
            var settings = new SettingsService(env, new TestSecretStore(), new TestSettingsBackupProvider());
            var sessionService = new SessionService(
                settings,
                new TestProgressionService(),
                new TestScheduler());

            bool completed = false;
            sessionService.SessionCompleted += (_, _) => completed = true;

            var session = new Session
            {
                Id = "test",
                Name = "Test Session",
                DurationMinutes = 1,
                Settings = new SessionSettings(),
                Phases = new List<SessionPhase> { new() { StartMinute = 0, Name = "Phase 1" } }
            };

            await sessionService.StartSessionAsync(session);
            Assert.Equal(SessionState.Running, sessionService.State);

            sessionService.StopSession(completed: true);
            Assert.Equal(SessionState.Idle, sessionService.State);
            Assert.True(completed);
        }
        finally
        {
            env.Cleanup();
        }
    }
}
