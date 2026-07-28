using CreatorControlSuite.App.Services;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Modules.OBS.Models;

namespace CreatorControlSuite.Tests;

public sealed class ObsDashboardApplicationServiceTests
{
    [Fact]
    public void SelectSimpleVisibilityRules_FiltersAndSortsCompatibleRules()
    {
        TimedAutomationRuleSettings[] rules =
        [
            Rule("late", "SceneElapsed", "SetSourceVisibility", 20),
            Rule("wrong-action", "SceneElapsed", "SwitchScene", 1),
            Rule("early", "StreamElapsed", "SetSourceVisibility", 2),
            Rule("wrong-trigger", "StreamStarted", "SetSourceVisibility", 0)
        ];

        IReadOnlyList<TimedAutomationRuleSettings> selected =
            ObsDashboardApplicationService.SelectSimpleVisibilityRules(rules);

        Assert.Equal(
            ["early", "late"],
            selected.Select(rule => rule.Id));
    }

    [Fact]
    public void CreateSimpleVisibilityRule_MapsAllEditorValues()
    {
        TimedAutomationRuleSettings rule =
            ObsDashboardApplicationService.CreateSimpleVisibilityRule(
                "Gaming",
                "Webcam",
                delaySeconds: 12,
                visible: false);

        Assert.Equal("Gaming → Webcam: nach 12 Sek. ausblenden", rule.Name);
        Assert.Equal("SceneElapsed", rule.TriggerType);
        Assert.Equal("Gaming", rule.TriggerScene);
        Assert.Equal("SetSourceVisibility", rule.ActionType);
        Assert.Equal("Gaming", rule.ObsScene);
        Assert.Equal("Webcam", rule.ObsSource);
        Assert.False(rule.SourceVisible);
        Assert.True(rule.OncePerStream);
    }

    [Fact]
    public void EvaluateStreamObservation_DebouncesConnectedOfflinePolls()
    {
        ObsStreamObservation transient =
            ObsDashboardApplicationService.EvaluateStreamObservation(
                reportsActive: false,
                snapshotConnected: true,
                wasActive: true,
                hasSessionStart: true,
                consecutiveInactivePolls: 0,
                requiredInactivePolls: 15);
        ObsStreamObservation confirmed =
            ObsDashboardApplicationService.EvaluateStreamObservation(
                reportsActive: false,
                snapshotConnected: true,
                wasActive: true,
                hasSessionStart: true,
                consecutiveInactivePolls: 14,
                requiredInactivePolls: 15);

        Assert.True(transient.IsActive);
        Assert.Equal(1, transient.ConsecutiveInactivePolls);
        Assert.False(transient.Ended);
        Assert.False(confirmed.IsActive);
        Assert.Equal(15, confirmed.ConsecutiveInactivePolls);
        Assert.True(confirmed.Ended);
    }

    [Fact]
    public void EvaluateStreamObservation_DoesNotTreatDisconnectAsStreamEnd()
    {
        ObsStreamObservation observation =
            ObsDashboardApplicationService.EvaluateStreamObservation(
                reportsActive: false,
                snapshotConnected: false,
                wasActive: true,
                hasSessionStart: true,
                consecutiveInactivePolls: 3,
                requiredInactivePolls: 15);

        Assert.True(observation.IsActive);
        Assert.Equal(3, observation.ConsecutiveInactivePolls);
        Assert.False(observation.Ended);
    }

    [Fact]
    public void ResolveObservedStreamStartedAt_UsesTimecodeOrObservationTime()
    {
        var observedAt = new DateTimeOffset(
            2026,
            7,
            28,
            20,
            0,
            0,
            TimeSpan.FromHours(2));

        DateTimeOffset reconstructed =
            ObsDashboardApplicationService.ResolveObservedStreamStartedAt(
                "01:02:03",
                observedAt);
        DateTimeOffset fallback =
            ObsDashboardApplicationService.ResolveObservedStreamStartedAt(
                "invalid",
                observedAt);

        Assert.Equal(observedAt - new TimeSpan(1, 2, 3), reconstructed);
        Assert.Equal(observedAt, fallback);
    }

    [Fact]
    public void SelectTrackedInput_UsesConfiguredThenExactThenPartialName()
    {
        ObsInputInfo[] inputs =
        [
            new("Desktop Audio", "wasapi", "wasapi"),
            new("Studio Mikrofon", "wasapi", "wasapi"),
            new("Configured", "wasapi", "wasapi")
        ];

        Assert.Equal(
            "Configured",
            ObsDashboardApplicationService.SelectTrackedInput(
                inputs,
                "configured",
                ["Desktop Audio"],
                ["mikrofon"])?.Name);
        Assert.Equal(
            "Desktop Audio",
            ObsDashboardApplicationService.SelectTrackedInput(
                inputs,
                "",
                ["Desktop Audio"],
                ["mikrofon"])?.Name);
        Assert.Equal(
            "Studio Mikrofon",
            ObsDashboardApplicationService.SelectTrackedInput(
                inputs,
                "",
                ["Missing"],
                ["mikrofon"])?.Name);
    }

    [Fact]
    public void SelectSceneActivationRules_MatchesTriggerAndSceneIgnoringCase()
    {
        TimedAutomationRuleSettings[] rules =
        [
            Rule("matching", "SceneElapsed", "SwitchScene", 5, "Gaming"),
            Rule("wrong-scene", "SceneElapsed", "SwitchScene", 5, "Pause"),
            Rule("wrong-trigger", "StreamElapsed", "SwitchScene", 5, "Gaming")
        ];

        IReadOnlyList<TimedAutomationRuleSettings> selected =
            ObsDashboardApplicationService.SelectSceneActivationRules(
                rules,
                "gaming");

        Assert.Equal(
            ["matching"],
            selected.Select(rule => rule.Id));
    }

    [Fact]
    public void SelectDistinctSceneNames_RemovesBlanksAndDuplicates()
    {
        ObsSceneInfo[] scenes =
        [
            new("Gaming", 0),
            new("gaming", 1),
            new("", 2),
            new("Pause", 3)
        ];

        IReadOnlyList<string> names =
            ObsDashboardApplicationService.SelectDistinctSceneNames(scenes);

        Assert.Equal(["Gaming", "Pause"], names);
    }

    [Fact]
    public void ResolveLiveStartedAt_UsesTwitchThenObsThenObservation()
    {
        DateTimeOffset twitch =
            DateTimeOffset.Parse("2026-07-28T18:00:00Z");
        DateTimeOffset obs =
            DateTimeOffset.Parse("2026-07-28T18:00:02Z");
        DateTimeOffset observed =
            DateTimeOffset.Parse("2026-07-28T18:00:05Z");

        Assert.Equal(
            twitch,
            ObsDashboardApplicationService.ResolveLiveStartedAt(
                twitch,
                obs,
                observed));
        Assert.Equal(
            obs,
            ObsDashboardApplicationService.ResolveLiveStartedAt(
                null,
                obs,
                observed));
        Assert.Equal(
            observed,
            ObsDashboardApplicationService.ResolveLiveStartedAt(
                null,
                null,
                observed));
    }

    private static TimedAutomationRuleSettings Rule(
        string id,
        string trigger,
        string action,
        int delay,
        string triggerScene = "") =>
        new()
        {
            Id = id,
            TriggerType = trigger,
            TriggerScene = triggerScene,
            ActionType = action,
            DelaySeconds = delay
        };
}
