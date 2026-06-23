using System;
using System.IO;
using System.Threading;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Moderation;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

public class ModerationCounterTests : IDisposable
{
    private readonly string _root;
    private readonly IAppEnvironment _environment;

    public ModerationCounterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"ccp-moderation-tests-{Guid.NewGuid():N}");
        _environment = new TestAppEnvironment(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public void FreshCounter_HasNoHits()
    {
        var counter = new ModerationCounter(_environment);
        var state = counter.GetState();
        Assert.Equal(0, state.HitsInLastTenMinutes);
        Assert.False(state.WarningTriggered);
        Assert.False(state.CooldownActive);
        Assert.Null(state.CooldownEndsAt);
    }

    [Fact]
    public void TwoHits_DoNotTriggerWarning()
    {
        var counter = new ModerationCounter(_environment);
        bool warned = false;
        counter.WarningTriggered += _ => warned = true;

        counter.RecordHit(ProhibitedCategory.Illegal, "test");
        counter.RecordHit(ProhibitedCategory.Illegal, "test");

        Assert.False(warned);
        Assert.Equal(2, counter.GetState().HitsInLastTenMinutes);
    }

    [Fact]
    public void ThreeHits_TriggersWarningOnce()
    {
        var counter = new ModerationCounter(_environment);
        int warnings = 0;
        counter.WarningTriggered += _ => Interlocked.Increment(ref warnings);

        counter.RecordHit(ProhibitedCategory.Illegal, "test");
        counter.RecordHit(ProhibitedCategory.Illegal, "test");
        counter.RecordHit(ProhibitedCategory.Illegal, "test");

        Assert.Equal(1, warnings);
        Assert.True(counter.GetState().WarningTriggered);
    }

    [Fact]
    public void FiveHits_StartsCooldown()
    {
        var counter = new ModerationCounter(_environment);
        DateTime? cooldownEnd = null;
        counter.CooldownStarted += end => cooldownEnd = end;

        for (int i = 0; i < 5; i++)
            counter.RecordHit(ProhibitedCategory.Illegal, "test");

        Assert.NotNull(cooldownEnd);
        var state = counter.GetState();
        Assert.True(state.CooldownActive);
        Assert.True(state.CooldownEndsAt > DateTime.UtcNow.AddMinutes(4));
    }

    [Fact]
    public void CooldownBlocksAdditionalHitsFromExtending()
    {
        var counter = new ModerationCounter(_environment);
        for (int i = 0; i < 5; i++)
            counter.RecordHit(ProhibitedCategory.Illegal, "test");

        var stateAfterCooldown = counter.GetState();
        Assert.True(stateAfterCooldown.CooldownActive);

        // Additional hits while on cooldown should not increase the count.
        counter.RecordHit(ProhibitedCategory.Illegal, "test");
        counter.RecordHit(ProhibitedCategory.Illegal, "test");

        Assert.True(counter.GetState().CooldownActive);
    }

    [Fact]
    public void LoadFromDisk_RestoresHits()
    {
        var counter1 = new ModerationCounter(_environment);
        counter1.RecordHit(ProhibitedCategory.Illegal, "test");
        counter1.RecordHit(ProhibitedCategory.Illegal, "test");
        counter1.RecordHit(ProhibitedCategory.Illegal, "test");

        var path = Path.Combine(_environment.ApplicationDataPath, "moderation-counter.json");
        WaitForPersistedHitCount(path, expectedHits: 3, timeout: TimeSpan.FromSeconds(5));

        var counter2 = new ModerationCounter(_environment);
        counter2.LoadFromDisk();

        Assert.Equal(3, counter2.GetState().HitsInLastTenMinutes);
        Assert.True(counter2.GetState().WarningTriggered);
    }

    private static void WaitForPersistedHitCount(string path, int expectedHits, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    if (json.Contains($"\"hits\""))
                    {
                        var count = System.Text.RegularExpressions.Regex.Matches(json, "\"\\d{4}-").Count;
                        if (count >= expectedHits)
                            return;
                    }
                }
            }
            catch { /* file may be locked */ }
            Thread.Sleep(50);
        }
    }

    private sealed class TestAppEnvironment : IAppEnvironment
    {
        public TestAppEnvironment(string root)
        {
            BaseDirectory = AppContext.BaseDirectory;
            UserDataPath = Path.Combine(root, "local");
            ApplicationDataPath = Path.Combine(root, "roaming");
            EffectiveAssetsPath = Path.Combine(root, "assets");
        }

        public string BaseDirectory { get; }
        public string UserDataPath { get; }
        public string ApplicationDataPath { get; }
        public string EffectiveAssetsPath { get; }
    }
}
