using System.Text.Json;
using CreatorControlSuite.App.Services;
using CreatorControlSuite.App.ViewModels.Pages;

namespace CreatorControlSuite.Tests;

public sealed class StreamerBotApplicationServiceTests
{
    [Fact]
    public void ParseActions_MapsDefaultsSkipsUnnamedAndSorts()
    {
        using JsonDocument document = JsonDocument.Parse(
            """
            {
              "actions": [
                { "id": "2", "name": "Zulu", "group": "System", "enabled": false },
                { "id": "ignored", "group": "System" },
                { "id": "1", "name": "Alpha" }
              ]
            }
            """);

        IReadOnlyList<StreamerBotActionOption> actions =
            StreamerBotApplicationService.ParseActions(document.RootElement);

        Assert.Equal(["Alpha", "Zulu"], actions.Select(action => action.Name));
        Assert.Equal("Ohne Gruppe", actions[0].Group);
        Assert.True(actions[0].Enabled);
        Assert.False(actions[1].Enabled);
    }

    [Fact]
    public void ParseActions_RejectsMissingActionArray()
    {
        using JsonDocument document = JsonDocument.Parse("""{"status":"ok"}""");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => StreamerBotApplicationService.ParseActions(
                document.RootElement));

        Assert.Equal(
            "Streamer.bot hat keine Aktionsliste zurückgegeben.",
            exception.Message);
    }

    [Fact]
    public void FilterActions_SearchesNameAndGroupAndPrioritizesFavorites()
    {
        StreamerBotActionOption[] actions =
        [
            new("3", "Outro", "Scenes", true),
            new("2", "Intro", "Scenes", true),
            new("1", "Follow", "Alerts", true)
        ];

        IReadOnlyList<StreamerBotActionOption> scenes =
            StreamerBotApplicationService.FilterActions(
                actions,
                ["3"],
                "scene");
        IReadOnlyList<StreamerBotActionOption> all =
            StreamerBotApplicationService.FilterActions(
                actions,
                ["3"],
                "");

        Assert.Equal(["Outro", "Intro"], scenes.Select(action => action.Name));
        Assert.Equal(
            ["Outro", "Follow", "Intro"],
            all.Select(action => action.Name));
        Assert.Equal(
            ["Alerts", "Scenes"],
            StreamerBotApplicationService.SelectGroups(actions));
    }

    [Fact]
    public void ParseArguments_RequiresObjectAndPreservesValues()
    {
        Dictionary<string, object?> empty =
            StreamerBotApplicationService.ParseArguments(" ");
        Dictionary<string, object?> parsed =
            StreamerBotApplicationService.ParseArguments(
                """{"name":"Ada","count":2}""");

        Assert.Empty(empty);
        Assert.Equal("Ada", ((JsonElement)parsed["name"]!).GetString());
        Assert.Equal(2, ((JsonElement)parsed["count"]!).GetInt32());
        Assert.Throws<InvalidOperationException>(
            () => StreamerBotApplicationService.ParseArguments("[1,2]"));
    }

    [Fact]
    public void ParseEvent_ProjectsAliasesAndClassifiesAlerts()
    {
        using JsonDocument alertDocument = JsonDocument.Parse(
            """
            {
              "event": { "source": "Twitch", "type": "GiftSub" },
              "data": {
                "displayName": "Ada",
                "months": "3",
                "message": "Danke"
              }
            }
            """);
        using JsonDocument customDocument = JsonDocument.Parse(
            """
            {
              "event": { "source": "General", "type": "Custom" }
            }
            """);

        StreamerBotEventProjection alert =
            StreamerBotApplicationService.ParseEvent(
                alertDocument.RootElement);
        StreamerBotEventProjection custom =
            StreamerBotApplicationService.ParseEvent(
                customDocument.RootElement);

        Assert.Equal("Twitch", alert.Source);
        Assert.Equal("GiftSub", alert.Type);
        Assert.Equal("Ada · 3 · Danke", alert.Summary);
        Assert.True(alert.IsKnownAlert);
        Assert.Equal("General · Custom", custom.Summary);
        Assert.False(custom.IsKnownAlert);
    }

    [Fact]
    public void ParseEvent_RejectsMessagesWithoutEventEnvelope()
    {
        using JsonDocument document = JsonDocument.Parse("""{"data":{}}""");

        Assert.Null(
            StreamerBotApplicationService.TryParseEvent(
                document.RootElement));
    }
}
