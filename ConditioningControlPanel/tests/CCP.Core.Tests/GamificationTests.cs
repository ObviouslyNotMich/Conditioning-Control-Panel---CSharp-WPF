using System;
using System.Linq;
using ConditioningControlPanel.Core.Models;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

public class GamificationTests
{
    [Theory]
    [InlineData(SessionDifficulty.Easy, 400)]
    [InlineData(SessionDifficulty.Medium, 800)]
    [InlineData(SessionDifficulty.Hard, 1200)]
    [InlineData(SessionDifficulty.Extreme, 2000)]
    public void SessionDifficultyXP_MapsCorrectly(SessionDifficulty difficulty, int expected)
    {
        Assert.Equal(expected, Session.GetDifficultyXP(difficulty));
    }

    [Fact]
    public void AchievementProgress_Unlock_And_IsUnlocked()
    {
        var progress = new AchievementProgress();

        Assert.False(progress.IsUnlocked("test_achievement"));

        progress.Unlock("test_achievement");

        Assert.True(progress.IsUnlocked("test_achievement"));
        Assert.Single(progress.UnlockedAchievements);
    }

    [Fact]
    public void AchievementProgress_Unlock_Deduplicates()
    {
        var progress = new AchievementProgress();
        progress.Unlock("daily_check");
        progress.Unlock("daily_check");

        Assert.Single(progress.UnlockedAchievements);
    }

    [Fact]
    public void AchievementProgress_TrackAvatarClick_ReachesThreshold()
    {
        var progress = new AchievementProgress();

        bool triggered = false;
        for (int i = 0; i < 20; i++)
        {
            if (progress.TrackAvatarClick())
                triggered = true;
        }

        Assert.True(triggered);
        Assert.InRange(progress.AvatarClickCount, 1, 20);
    }

    [Fact]
    public void AchievementProgress_TrackAvatarClick_WindowResets()
    {
        var progress = new AchievementProgress
        {
            AvatarClickStartTime = DateTime.Now.AddSeconds(-20),
            AvatarClickCount = 5
        };

        progress.TrackAvatarClick();

        Assert.Equal(1, progress.AvatarClickCount);
    }

    [Fact]
    public void AchievementProgress_TrackNeedyDoll_ReachesThreshold()
    {
        var progress = new AchievementProgress();

        bool triggered = false;
        for (int i = 0; i < 150; i++)
        {
            if (progress.TrackNeedyDollClick())
                triggered = true;
        }

        Assert.True(triggered);
    }

    [Fact]
    public void SkillDefinition_All_ContainsFoundationSkills()
    {
        var all = SkillDefinition.All;

        Assert.Contains(all, s => s.Id == "pink_hours");
        Assert.Contains(all, s => s.Id == "sparkle_boost_1" && s.EffectType == SkillEffectType.XpMultiplier);
        Assert.Contains(all, s => s.Id == "eternal_doll" && s.IsSecret);
    }

    [Fact]
    public void SkillDefinition_XpMultipliers_StackCorrectly()
    {
        var boost1 = SkillDefinition.All.Single(s => s.Id == "sparkle_boost_1");
        var boost2 = SkillDefinition.All.Single(s => s.Id == "sparkle_boost_2");
        var boost3 = SkillDefinition.All.Single(s => s.Id == "sparkle_boost_3");

        Assert.Equal(0.10, boost1.EffectValue);
        Assert.Equal(0.15, boost2.EffectValue);
        Assert.Equal(0.20, boost3.EffectValue);
    }

    [Fact]
    public void AppSettings_CurrentStreak_ClampsNegative()
    {
        var settings = new AppSettings { HighestStreak = 5 };

        settings.CurrentStreak = -3;

        Assert.Equal(0, settings.CurrentStreak);
        Assert.Equal(5, settings.HighestStreak);
    }

    [Fact]
    public void AssetFileItem_FullPath_DerivesProperties()
    {
        var item = new AssetFileItem
        {
            FullPath = @"C:\Content\ hypnosis\video.mp4"
        };

        Assert.Equal("video.mp4", item.Name);
        Assert.Equal(".mp4", item.Extension);
        Assert.True(item.IsVideo);
        Assert.False(item.IsGif);
    }

    [Theory]
    [InlineData(".PNG", true)]
    [InlineData(".jpg", true)]
    [InlineData(".mp4", true)]
    [InlineData(".txt", false)]
    [InlineData(".exe", false)]
    public void AssetFileItem_IsSupportedExtension(string extension, bool expected)
    {
        Assert.Equal(expected, AssetFileItem.IsSupportedExtension(extension));
    }

    [Theory]
    [InlineData(512L, "512 B")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(2097152L, "2.0 MB")]
    public void AssetFileItem_SizeDisplay_FormatsCorrectly(long bytes, string expected)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            var item = new AssetFileItem { SizeBytes = bytes };
            Assert.Equal(expected, item.SizeDisplay);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Achievement_All_ContainsProgressionAchievements()
    {
        Assert.Contains(Achievement.All, a => a.Key == "plastic_initiation");
        Assert.Contains(Achievement.All, a => a.Key == "perfect_plastic_puppet");
        Assert.All(Achievement.All, a => Assert.False(string.IsNullOrEmpty(a.Value.Id)));
    }
}
