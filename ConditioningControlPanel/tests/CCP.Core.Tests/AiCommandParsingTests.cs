using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

public class AiCommandParsingTests
{
    [Fact]
    public void ParseCommand_FlashImage_DeserializesData()
    {
        var json = @"{""command"": ""flash_image"", ""data"": {""Amount"": 5, ""Duration"": 10, ""Size"": 100, ""Opacity"": 80}}";
        var cmd = AiCommandData.ParseCommand(json);

        Assert.NotNull(cmd);
        Assert.Equal(AICommandType.flash_image, cmd!.Command);
        var data = Assert.IsType<FlashImage>(cmd.Data);
        Assert.Equal(5, data.Amount);
        Assert.Equal(10, data.Duration);
        Assert.Equal(100, data.Size);
        Assert.Equal(80, data.Opacity);
    }

    [Fact]
    public void ParseCommand_Bubbles_DeserializesData()
    {
        var json = @"{""command"": ""bubbles"", ""data"": {""On"": true, ""Frequency"": 3}}";
        var cmd = AiCommandData.ParseCommand(json);

        Assert.Equal(AICommandType.bubbles, cmd!.Command);
        var data = Assert.IsType<Bubbles>(cmd.Data);
        Assert.True(data.On);
        Assert.Equal(3, data.Frequency);
    }

    [Fact]
    public void ParseCommand_Video_DeserializesData()
    {
        var json = @"{""command"": ""video"", ""data"": {""Title"": ""Hypno Loop"", ""Path"": ""loop.mp4"", ""Random"": true}}";
        var cmd = AiCommandData.ParseCommand(json);

        Assert.Equal(AICommandType.video, cmd!.Command);
        var data = Assert.IsType<Media>(cmd.Data);
        Assert.Equal("Hypno Loop", data.Title);
        Assert.Equal("loop.mp4", data.Path);
        Assert.True(data.Random);
    }

    [Fact]
    public void ParseCommand_Audio_DeserializesData()
    {
        var json = @"{""command"": ""audio"", ""data"": {""Title"": ""Drone"", ""Path"": ""drone.mp3""}}";
        var cmd = AiCommandData.ParseCommand(json);

        Assert.Equal(AICommandType.audio, cmd!.Command);
        var data = Assert.IsType<Media>(cmd.Data);
        Assert.Equal("drone.mp3", data.Path);
    }

    [Fact]
    public void ParseCommand_Subliminal_DeserializesData()
    {
        var json = @"{""command"": ""subliminal"", ""data"": {""Text"": ""OBEY"", ""Opacity"": 60}}";
        var cmd = AiCommandData.ParseCommand(json);

        Assert.Equal(AICommandType.subliminal, cmd!.Command);
        var data = Assert.IsType<Subliminal>(cmd.Data);
        Assert.Equal("OBEY", data.Text);
        Assert.Equal(60, data.Opacity);
    }

    [Fact]
    public void ParseCommand_Spiral_DeserializesData()
    {
        var json = @"{""command"": ""spiral"", ""data"": {""On"": false, ""Intensity"": 7}}";
        var cmd = AiCommandData.ParseCommand(json);

        Assert.Equal(AICommandType.spiral, cmd!.Command);
        var data = Assert.IsType<SpiralPinkFiler>(cmd.Data);
        Assert.False(data.On);
        Assert.Equal(7, data.Intensity);
    }

    [Fact]
    public void ParseCommand_Pink_DeserializesData()
    {
        var json = @"{""command"": ""pink"", ""data"": {""On"": true, ""Intensity"": 15}}";
        var cmd = AiCommandData.ParseCommand(json);

        Assert.Equal(AICommandType.pink, cmd!.Command);
        Assert.IsType<SpiralPinkFiler>(cmd.Data);
    }

    [Fact]
    public void ParseCommand_Bounce_DeserializesData()
    {
        var json = @"{""command"": ""bounce"", ""data"": {""Words"": [""OBEY"", ""DROP""], ""On"": true}}";
        var cmd = AiCommandData.ParseCommand(json);

        Assert.Equal(AICommandType.bounce, cmd!.Command);
        var data = Assert.IsType<Bounce>(cmd.Data);
        Assert.Equal(new[] { "OBEY", "DROP" }, data.Words);
        Assert.True(data.On);
    }

    [Fact]
    public void ParseCommand_Haptic_DeserializesData()
    {
        var json = @"{""command"": ""haptic"", ""data"": {""Intensity"": 0.75, ""Duration"": 2000}}";
        var cmd = AiCommandData.ParseCommand(json);

        Assert.Equal(AICommandType.haptic, cmd!.Command);
        var data = Assert.IsType<HapticCommandData>(cmd.Data);
        Assert.Equal(0.75, data.Intensity);
        Assert.Equal(2000, data.Duration);
    }

    [Fact]
    public void ParseCommand_MantraLockscreen_DeserializesData()
    {
        var json = @"{""command"": ""mantra_lockscreen"", ""data"": {""mantra"": ""OBEY"", ""amount"": 5}}";
        var cmd = AiCommandData.ParseCommand(json);

        Assert.Equal(AICommandType.mantra_lockscreen, cmd!.Command);
        var data = Assert.IsType<MantraLockscreen>(cmd.Data);
        Assert.Equal("OBEY", data.Mantra);
        Assert.Equal(5, data.Amount);
    }

    [Fact]
    public void ParseCommand_GetBackToMe_DeserializesNestedCommands()
    {
        var json = @"{""command"": ""getbacktome"", ""data"": {""Delay"": 30, ""Token"": ""abc"", ""JsonOnly"": true, ""Commands"": [{""command"": ""flash_image"", ""data"": {""Amount"": 1, ""Duration"": 5, ""Size"": 50, ""Opacity"": 100}}]}}";
        var cmd = AiCommandData.ParseCommand(json);

        Assert.Equal(AICommandType.getbacktome, cmd!.Command);
        var data = Assert.IsType<GetBackToMe>(cmd.Data);
        Assert.Equal(30, data.Delay);
        Assert.Equal("abc", data.Token);
        Assert.True(data.JsonOnly);
        Assert.Single(data.Commands!);
        Assert.Equal(AICommandType.flash_image, data.Commands![0].Command);
    }

    [Fact]
    public void ParseCommand_StringOnly_ReturnsCommandWithoutData()
    {
        var cmd = AiCommandData.ParseCommand("\"audio\"");

        Assert.NotNull(cmd);
        Assert.Equal(AICommandType.audio, cmd!.Command);
        Assert.Null(cmd.Data);
    }

    [Fact]
    public void ParseCommand_UnknownCommand_ReturnsNone()
    {
        var json = @"{""command"": ""unknown_thing""}";
        var cmd = AiCommandData.ParseCommand(json);

        Assert.NotNull(cmd);
        Assert.Equal(AICommandType.none, cmd!.Command);
    }

    [Fact]
    public void ParseCommand_MissingClosingBrace_Recovers()
    {
        // Inner object is complete; only the top-level closing brace is missing.
        var json = @"{""command"": ""bubbles"", ""data"": {""On"": true, ""Frequency"": 2}";
        var cmd = AiCommandData.ParseCommand(json);

        Assert.NotNull(cmd);
        Assert.Equal(AICommandType.bubbles, cmd!.Command);
        var data = Assert.IsType<Bubbles>(cmd.Data);
        Assert.True(data.On);
        Assert.Equal(2, data.Frequency);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ParseCommand_InvalidInput_ReturnsNull(string? json)
    {
        var cmd = AiCommandData.ParseCommand(json!);
        Assert.Null(cmd);
    }

    [Fact]
    public void ParseCommand_AllowsCommentsAndTrailingCommas()
    {
        var json = @"{
            // trigger a flash
            ""command"": ""flash_image"",
            ""data"": {""Amount"": 1, ""Duration"": 2, ""Size"": 50, ""Opacity"": 100,},
        }";
        var cmd = AiCommandData.ParseCommand(json);

        Assert.NotNull(cmd);
        Assert.Equal(AICommandType.flash_image, cmd!.Command);
    }
}
