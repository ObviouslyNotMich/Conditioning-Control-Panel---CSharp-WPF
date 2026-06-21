using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

public class ModManifestTests
{
    [Fact]
    public void Deserialize_MinimalManifest_PopulatesRequiredFields()
    {
        var json = @"{""id"": ""custom.mod"", ""name"": ""Custom Mod"", ""author"": ""Tester""}";
        var manifest = JsonConvert.DeserializeObject<ModManifest>(json);

        Assert.NotNull(manifest);
        Assert.Equal("custom.mod", manifest!.Id);
        Assert.Equal("Custom Mod", manifest.Name);
        Assert.Equal("Tester", manifest.Author);
        Assert.Equal("1.0.0", manifest.Version);
    }

    [Fact]
    public void Deserialize_VersionDefaultsTo100_WhenMissing()
    {
        var json = @"{""id"": ""x"", ""name"": ""X"", ""author"": ""A""}";
        var manifest = JsonConvert.DeserializeObject<ModManifest>(json);

        Assert.Equal("1.0.0", manifest!.Version);
    }

    [Fact]
    public void Deserialize_FullManifest_PopulatesOptionalSections()
    {
        var json = @"{
            ""id"": ""test.mod"",
            ""name"": ""Test Mod"",
            ""version"": ""2.0.0"",
            ""author"": ""Dev"",
            ""description"": ""A test mod"",
            ""tags"": [""pink"", ""hypno""],
            ""theme"": { ""accentColor"": ""#FF69B4"" },
            ""identity"": { ""companionName"": ""Tester"" },
            ""subliminalPool"": { ""OBEY"": true },
            ""triggers"": { ""freeze"": ""Freeze"" },
            ""browser"": { ""defaultUrl"": ""https://example.com/"" },
            ""customAvatarSets"": [{ ""setNumber"": 7, ""label"": ""Extra"", ""unlockLevel"": 10 }]
        }";
        var manifest = JsonConvert.DeserializeObject<ModManifest>(json);

        Assert.NotNull(manifest);
        Assert.Equal("2.0.0", manifest!.Version);
        Assert.Equal("A test mod", manifest.Description);
        Assert.Equal(new[] { "pink", "hypno" }, manifest.Tags);
        Assert.NotNull(manifest.Theme);
        Assert.Equal("#FF69B4", manifest.Theme!.AccentColor);
        Assert.NotNull(manifest.Identity);
        Assert.Equal("Tester", manifest.Identity!.CompanionName);
        Assert.Contains("OBEY", manifest.SubliminalPool!.Keys);
        Assert.NotNull(manifest.Triggers);
        Assert.Equal("Freeze", manifest.Triggers!.Freeze);
        Assert.NotNull(manifest.Browser);
        Assert.Equal("https://example.com/", manifest.Browser!.DefaultUrl);
        Assert.Single(manifest.CustomAvatarSets!);
        Assert.Equal(7, manifest.CustomAvatarSets![0].SetNumber);
    }

    [Fact]
    public void Deserialize_Personality_PopulatesPromptSettings()
    {
        var json = @"{
            ""id"": ""p.mod"",
            ""name"": ""P"",
            ""author"": ""A"",
            ""personalities"": [
                { ""id"": ""brat"", ""name"": ""Brat"", ""promptSettings"": { ""tone"": ""sassy"" } }
            ]
        }";
        var manifest = JsonConvert.DeserializeObject<ModManifest>(json);

        Assert.Single(manifest!.Personalities!);
        Assert.Equal("brat", manifest.Personalities![0].Id);
        Assert.Equal("sassy", manifest.Personalities![0].PromptSettings!["tone"]);
    }

    [Fact]
    public void ModPackage_ShortcutsExposeManifestValues()
    {
        var manifest = new ModManifest { Id = "pkg.id", Name = "Package" };
        var package = new ModPackage(manifest, @"C:\Mods\pkg", false);

        Assert.Equal("pkg.id", package.Id);
        Assert.Equal("Package", package.Name);
        Assert.False(package.IsBuiltIn);
        Assert.Equal(@"C:\Mods\pkg", package.InstalledPath);
    }

    [Fact]
    public void BuiltInMods_ProvidesExpectedStockMods()
    {
        Assert.Equal("builtin-ccp-default", BuiltInMods.CCPDefault.Id);
        Assert.Equal("builtin-bambisleep", BuiltInMods.BambiSleep.Id);
        Assert.Equal("builtin-sissyhypno", BuiltInMods.SissyHypno.Id);
        Assert.Equal("drone-mode", BuiltInMods.Dronification.Id);
    }

    [Fact]
    public void BuiltInMods_BambiSleep_HasIdentityAndPools()
    {
        var mod = BuiltInMods.BambiSleep;

        Assert.NotNull(mod.Identity);
        Assert.Equal("BambiSprite", mod.Identity!.CompanionName);
        Assert.NotEmpty(mod.SubliminalPool!);
        Assert.NotEmpty(mod.CustomTriggers!);
    }

    [Fact]
    public void BuiltInMods_SissyHypno_HasTextReplacements()
    {
        var mod = BuiltInMods.SissyHypno;

        Assert.NotNull(mod.TextReplacements);
        Assert.Contains("Bambi", mod.TextReplacements!.Keys);
    }

    [Fact]
    public void BuiltInMods_CCPDefault_HasBouncingTextPool()
    {
        var mod = BuiltInMods.CCPDefault;

        Assert.NotNull(mod.BouncingTextPool);
        Assert.Contains("GOOD GIRL", mod.BouncingTextPool!.Keys);
    }
}
