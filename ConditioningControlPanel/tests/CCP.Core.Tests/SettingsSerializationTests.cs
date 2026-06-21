using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

public class SettingsSerializationTests
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        ObjectCreationHandling = ObjectCreationHandling.Replace
    };

    [Fact]
    public void RoundTrip_Defaults_ArePreserved()
    {
        var original = new AppSettings
        {
            Language = "fr",
            PlayerLevel = 12,
            PlayerXP = 1234.5,
            FlashFrequency = 25,
        };

        var json = JsonConvert.SerializeObject(original, SerializerSettings);
        var restored = JsonConvert.DeserializeObject<AppSettings>(json, SerializerSettings)!;

        Assert.Equal("fr", restored.Language);
        Assert.Equal(12, restored.PlayerLevel);
        Assert.Equal(1234.5, restored.PlayerXP);
        Assert.Equal(25, restored.FlashFrequency);
    }

    [Fact]
    public void EmotePresets_PaddedToFive_WhenShort()
    {
        var json = new JObject
        {
            ["RemoteEmotePresets"] = new JArray(
                new JObject { ["Icon"] = "🌟", ["Text"] = "star" },
                new JObject { ["Icon"] = "❤️", ["Text"] = "love" }
            )
        }.ToString();

        var settings = JsonConvert.DeserializeObject<AppSettings>(json, SerializerSettings)!;

        Assert.Equal(5, settings.RemoteEmotePresets.Count);
        Assert.Equal("star", settings.RemoteEmotePresets[0].Text);
        Assert.Equal("love", settings.RemoteEmotePresets[1].Text);
        Assert.Equal("drifting", settings.RemoteEmotePresets[2].Text);
        Assert.Equal("too much", settings.RemoteEmotePresets[4].Text);
    }

    [Fact]
    public void EmotePresets_TruncatedToFive_WhenLong()
    {
        var presets = Enumerable.Range(0, 8)
            .Select(i => new EmotePreset { Icon = $"icon{i}", Text = $"text{i}" })
            .ToList();
        var original = new AppSettings { RemoteEmotePresets = presets };

        var json = JsonConvert.SerializeObject(original, SerializerSettings);
        var restored = JsonConvert.DeserializeObject<AppSettings>(json, SerializerSettings)!;

        Assert.Equal(5, restored.RemoteEmotePresets.Count);
        Assert.Equal("text0", restored.RemoteEmotePresets[0].Text);
        Assert.Equal("text4", restored.RemoteEmotePresets[4].Text);
    }

    [Fact]
    public void EmotePresets_MojibakeIcons_AreReplacedWithDefaults()
    {
        // Latin-1 supplement characters are the mojibake signature the model looks for.
        var json = new JObject
        {
            ["RemoteEmotePresets"] = new JArray(
                new JObject { ["Icon"] = "df Y\u00a0", ["Text"] = "yes" },
                new JObject { ["Icon"] = "pleading\u00ff", ["Text"] = "more" },
                new JObject { ["Icon"] = "melting", ["Text"] = "drifting" },
                new JObject { ["Icon"] = "heart", ["Text"] = "thank you" },
                new JObject { ["Icon"] = "warning", ["Text"] = "too much" }
            )
        }.ToString();

        var settings = JsonConvert.DeserializeObject<AppSettings>(json, SerializerSettings)!;

        var defaults = AppSettings.DefaultRemoteEmotePresets();
        Assert.Equal(defaults[0].Icon, settings.RemoteEmotePresets[0].Icon);
        Assert.Equal(defaults[1].Icon, settings.RemoteEmotePresets[1].Icon);
        Assert.Equal("melting", settings.RemoteEmotePresets[2].Icon);
    }

    [Theory]
    [InlineData(nameof(AppSettings.SelectedAvatarSet), 10, 7)]
    [InlineData(nameof(AppSettings.SelectedAvatarSet), -1, 0)]
    [InlineData(nameof(AppSettings.FlashFrequency), 500, 180)]
    [InlineData(nameof(AppSettings.FlashFrequency), 0, 1)]
    [InlineData(nameof(AppSettings.MasterVolume), 150, 100)]
    [InlineData(nameof(AppSettings.MasterVolume), -10, 0)]
    [InlineData(nameof(AppSettings.HydraLimit), 100, 20)]
    [InlineData(nameof(AppSettings.HydraLimit), 0, 1)]
    [InlineData(nameof(AppSettings.SkillPoints), -5, 0)]
    public void NumericProperties_AreClamped(string propertyName, int input, int expected)
    {
        var settings = new AppSettings();
        var property = typeof(AppSettings).GetProperty(propertyName)!;
        property.SetValue(settings, input);

        var actual = (int)property.GetValue(settings)!;
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DisabledAssetPaths_ConvertBackslashesToForwardSlashes()
    {
        var settings = new AppSettings
        {
            DisabledAssetPaths = new HashSet<string> { @"folder\sub\image.png", @"other/file.jpg" }
        };

        Assert.Contains("folder/sub/image.png", settings.DisabledAssetPaths);
        Assert.Contains("other/file.jpg", settings.DisabledAssetPaths);
        Assert.DoesNotContain(@"folder\sub\image.png", settings.DisabledAssetPaths);
    }

    [Fact]
    public void DisabledAssetPaths_AreCaseInsensitive()
    {
        var settings = new AppSettings
        {
            DisabledAssetPaths = new HashSet<string> { "Folder/Image.PNG" }
        };

        Assert.Contains("folder/image.png", settings.DisabledAssetPaths);
    }

    [Fact]
    public void MarkBarkFired_Deduplicates()
    {
        var settings = new AppSettings();

        Assert.True(settings.MarkBarkFired("greeting"));
        Assert.False(settings.MarkBarkFired("greeting"));
        Assert.True(settings.IsBarkFired("greeting"));
    }

    [Fact]
    public void CurrentStreak_UpdatesHighestStreak()
    {
        var settings = new AppSettings { HighestStreak = 3 };

        settings.CurrentStreak = 2;
        Assert.Equal(3, settings.HighestStreak);

        settings.CurrentStreak = 5;
        Assert.Equal(5, settings.HighestStreak);
    }

    [Fact]
    public void SchemaVersion_IsBumped_OnSerialize()
    {
        var settings = new AppSettings { SettingsSchemaVersion = 0 };

        var json = JsonConvert.SerializeObject(settings, SerializerSettings);
        var restored = JsonConvert.DeserializeObject<AppSettings>(json, SerializerSettings)!;

        Assert.True(restored.SettingsSchemaVersion >= 6);
    }

    [Fact]
#pragma warning disable CS0618 // ContentMode is obsolete but required for migration compatibility tests.
    public void ContentMode_Migration_MapsSissyHypno()
    {
        // Build raw JSON so the pre-v6 schema version is preserved (SerializeObject would bump it).
        var json = new JObject
        {
            ["SettingsSchemaVersion"] = 0,
            ["ContentMode"] = "SissyHypno",
            ["ContentModeChosen"] = true,
            ["ActiveModId"] = BuiltInMods.CCPDefaultId
        }.ToString();

        var restored = JsonConvert.DeserializeObject<AppSettings>(json, SerializerSettings)!;
        restored.MigrateFromContentModeToMod();

        Assert.Equal(BuiltInMods.SissyHypnoId, restored.ActiveModId);
        Assert.Equal(6, restored.SettingsSchemaVersion);
    }
#pragma warning restore CS0618

    [Fact]
#pragma warning disable CS0618
    public void ContentMode_Migration_LeavesExplicitActiveModId_Alone()
    {
        var json = new JObject
        {
            ["SettingsSchemaVersion"] = 0,
            ["ContentMode"] = "BambiSleep",
            ["ContentModeChosen"] = true,
            ["ActiveModId"] = BuiltInMods.SissyHypnoId
        }.ToString();

        var restored = JsonConvert.DeserializeObject<AppSettings>(json, SerializerSettings)!;
        restored.MigrateFromContentModeToMod();

        Assert.Equal(BuiltInMods.SissyHypnoId, restored.ActiveModId);
        Assert.Equal(6, restored.SettingsSchemaVersion);
    }
#pragma warning restore CS0618

    [Fact]
    public void AssetPreset_DisplayText_IncludesCounts()
    {
        var preset = new AssetPreset
        {
            Name = "Test Preset",
            EnabledImageCount = 12,
            EnabledVideoCount = 3
        };

        Assert.Equal("Test Preset (12 img, 3 vid)", preset.DisplayText);
    }

    [Fact]
    public void AssetPreset_IsDefault_OnlyForDefaultId()
    {
        Assert.True(AssetPreset.CreateDefault().IsDefault);
        Assert.False(new AssetPreset { Id = "custom" }.IsDefault);
    }
}
