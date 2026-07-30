using CreatorControlSuite.App.Services;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Tests;

public sealed class SpotifySceneMusicRuleServiceTests
{
    [Fact]
    public void CreateRows_AddsEveryObsSceneAndMapsExistingRule()
    {
        SpotifyAutomationRuleSettings existing = new()
        {
            TriggerValue = "Gaming",
            ActionType = "StartPlaylist",
            PlaylistUri = "spotify:playlist:gaming",
            Shuffle = true,
            VolumePercent = 42,
            FadeEnabled = true,
            FadeMilliseconds = 1750
        };

        IReadOnlyList<SpotifySceneMusicRow> rows =
            SpotifySceneMusicRuleService.CreateRows(["Starting", "Gaming"], [existing]);

        Assert.Equal(2, rows.Count);
        Assert.False(rows[0].Enabled);
        Assert.True(rows[1].Enabled);
        Assert.Equal("spotify:playlist:gaming", rows[1].PlaylistUri);
        Assert.True(rows[1].Shuffle);
        Assert.Equal(42, rows[1].VolumePercent);
        Assert.True(rows[1].FadeEnabled);
        Assert.Equal(1750, rows[1].FadeMilliseconds);
    }

    [Fact]
    public void ApplyRows_ReplacesOnlySceneRulesAndClampsValues()
    {
        SpotifyAutomationRuleSettings unrelated = new()
        {
            TriggerType = "StreamStarted",
            TriggerValue = "any",
            ActionType = "Resume"
        };
        SpotifySceneMusicRow row = new("Gaming")
        {
            Enabled = true,
            PlaylistUri = "spotify:playlist:gaming",
            Shuffle = true,
            VolumePercent = 120,
            FadeEnabled = true,
            FadeMilliseconds = -5
        };

        IReadOnlyList<SpotifyAutomationRuleSettings> rules =
            SpotifySceneMusicRuleService.ApplyRows([unrelated], [row]);

        Assert.Equal(2, rules.Count);
        Assert.Same(unrelated, rules[0]);
        SpotifyAutomationRuleSettings sceneRule = rules[1];
        Assert.Equal("ObsSceneChanged", sceneRule.TriggerType);
        Assert.Equal("Gaming", sceneRule.TriggerValue);
        Assert.Equal(100, sceneRule.VolumePercent);
        Assert.Equal(0, sceneRule.FadeMilliseconds);
    }
}
