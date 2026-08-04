using System.Text.Json;
using CreatorControlSuite.Modules.Twitch;
using CreatorControlSuite.Modules.Twitch.Models;
using CreatorControlSuite.App.Twitch;

namespace CreatorControlSuite.Tests;

public sealed class TwitchChatMessageParserTests
{
    [Fact]
    public void DisplayItem_PreservesNativeEmotesForInAppChat()
    {
        var message = new TwitchChatMessage(
            "msg-1", "b1", "u1", "alice", "Alice", "hi Kappa", "#FF0000",
            DateTimeOffset.Parse("2026-07-27T18:00:00Z"), [],
            [
                new(TwitchChatFragmentType.Text, "hi "),
                new(TwitchChatFragmentType.Emote, "Kappa", "25")
            ]);

        TwitchChatDisplayItem item = TwitchChatDisplayItem.FromMessage(message, "[MOD] ");

        Assert.Equal("18:00:00 · [MOD] Alice: ", item.Prefix);
        Assert.Equal(2, item.Parts.Count);
        Assert.False(item.Parts[0].IsEmote);
        Assert.True(item.Parts[1].IsEmote);
        Assert.Equal("Kappa", item.Parts[1].Text);
        Assert.Equal(
            "https://static-cdn.jtvnw.net/emoticons/v2/25/default/dark/2.0",
            item.Parts[1].ImageUrl);
        Assert.Contains("[MOD]", item.ToString());
    }

    [Fact]
    public void Parse_ReadsTextAndEmoteFragments()
    {
        using JsonDocument doc = JsonDocument.Parse(
            """
            {
              "message_id": "msg-1",
              "broadcaster_user_id": "b1",
              "chatter_user_id": "u1",
              "chatter_user_login": "alice",
              "chatter_user_name": "Alice",
              "color": "#FF0000",
              "badges": [ { "set_id": "moderator", "id": "1" } ],
              "message": {
                "text": "hi Kappa there",
                "fragments": [
                  { "type": "text", "text": "hi " },
                  {
                    "type": "emote",
                    "text": "Kappa",
                    "emote": { "id": "25", "emote_set_id": "0" }
                  },
                  { "type": "text", "text": " there" }
                ]
              }
            }
            """);

        TwitchChatMessage message = TwitchChatMessageParser.Parse(
            doc.RootElement,
            DateTimeOffset.Parse("2026-07-27T18:00:00Z"));

        Assert.Equal("msg-1", message.MessageId);
        Assert.Equal("Alice", message.ChatterName);
        Assert.Equal("hi Kappa there", message.MessageText);
        Assert.Equal("#FF0000", message.Color);
        Assert.Single(message.Badges);
        Assert.Equal("moderator", message.Badges[0].SetId);
        Assert.Equal("1", message.Badges[0].Id);
        Assert.Equal(3, message.Fragments.Count);
        Assert.Equal(TwitchChatFragmentType.Text, message.Fragments[0].Type);
        Assert.Equal("hi ", message.Fragments[0].Text);
        Assert.Null(message.Fragments[0].EmoteId);
        Assert.Equal(TwitchChatFragmentType.Emote, message.Fragments[1].Type);
        Assert.Equal("Kappa", message.Fragments[1].Text);
        Assert.Equal("25", message.Fragments[1].EmoteId);
        Assert.Equal(
            "https://static-cdn.jtvnw.net/emoticons/v2/25/default/dark/2.0",
            TwitchChatMessageParser.GetTwitchEmoteUrl("25"));
    }

    [Fact]
    public void Parse_WithoutFragments_FallsBackToSingleTextPart()
    {
        using JsonDocument doc = JsonDocument.Parse(
            """
            {
              "message_id": "msg-2",
              "broadcaster_user_id": "b1",
              "chatter_user_id": "u1",
              "chatter_user_login": "bob",
              "chatter_user_name": "Bob",
              "color": "",
              "message": { "text": "plain only" }
            }
            """);

        TwitchChatMessage message = TwitchChatMessageParser.Parse(
            doc.RootElement,
            DateTimeOffset.UtcNow);

        Assert.Equal("plain only", message.MessageText);
        Assert.Single(message.Fragments);
        Assert.Equal(TwitchChatFragmentType.Text, message.Fragments[0].Type);
        Assert.Equal("plain only", message.Fragments[0].Text);
    }

    [Fact]
    public void Parse_MapsMentionAndCheermoteAsText()
    {
        using JsonDocument doc = JsonDocument.Parse(
            """
            {
              "message_id": "msg-3",
              "broadcaster_user_id": "b1",
              "chatter_user_id": "u1",
              "chatter_user_login": "carol",
              "chatter_user_name": "Carol",
              "color": "",
              "message": {
                "text": "@alice Cheer100",
                "fragments": [
                  {
                    "type": "mention",
                    "text": "@alice",
                    "mention": { "user_id": "u2", "user_name": "alice", "user_login": "alice" }
                  },
                  { "type": "text", "text": " " },
                  {
                    "type": "cheermote",
                    "text": "Cheer100",
                    "cheermote": { "prefix": "Cheer", "bits": 100, "tier": 1 }
                  }
                ]
              }
            }
            """);

        TwitchChatMessage message = TwitchChatMessageParser.Parse(
            doc.RootElement,
            DateTimeOffset.UtcNow);

        Assert.Equal(3, message.Fragments.Count);
        Assert.Equal(TwitchChatFragmentType.Mention, message.Fragments[0].Type);
        Assert.Equal("@alice", message.Fragments[0].Text);
        Assert.Equal(TwitchChatFragmentType.Cheermote, message.Fragments[2].Type);
        Assert.Equal("Cheer100", message.Fragments[2].Text);
    }
}
