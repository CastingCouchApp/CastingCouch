using CreatorControlSuite.Modules.Twitch;
using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.Tests;

public sealed class ChatEmoteEnricherTests
{
    [Fact]
    public void Enrich_ReplacesThirdPartyCodesInTextFragments()
    {
        var catalog = new Dictionary<string, ChatEmoteDefinition>(StringComparer.Ordinal)
        {
            ["OMEGALUL"] = new("OMEGALUL", "https://cdn.example/omegalul", "bttv"),
            ["Clap"] = new("Clap", "https://cdn.example/clap", "7tv")
        };

        IReadOnlyList<TwitchChatFragment> input =
        [
            new(TwitchChatFragmentType.Text, "nice OMEGALUL Clap end"),
            new(TwitchChatFragmentType.Emote, "Kappa", "25")
        ];

        IReadOnlyList<OverlayChatPart> parts = ChatEmoteEnricher.Enrich(input, catalog);

        Assert.Equal(6, parts.Count);
        Assert.Equal("text", parts[0].Type);
        Assert.Equal("nice ", parts[0].Text);
        Assert.Equal("emote", parts[1].Type);
        Assert.Equal("OMEGALUL", parts[1].Text);
        Assert.Equal("https://cdn.example/omegalul", parts[1].Url);
        Assert.Equal("bttv", parts[1].Provider);
        Assert.Equal("text", parts[2].Type);
        Assert.Equal(" ", parts[2].Text);
        Assert.Equal("emote", parts[3].Type);
        Assert.Equal("Clap", parts[3].Text);
        Assert.Equal("7tv", parts[3].Provider);
        Assert.Equal("text", parts[4].Type);
        Assert.Equal(" end", parts[4].Text);
        Assert.Equal("emote", parts[5].Type);
        Assert.Equal("Kappa", parts[5].Text);
        Assert.Equal(
            "https://static-cdn.jtvnw.net/emoticons/v2/25/default/dark/2.0",
            parts[5].Url);
        Assert.Equal("twitch", parts[5].Provider);
    }

    [Fact]
    public void Enrich_PrefersLongerEmoteCodes()
    {
        var catalog = new Dictionary<string, ChatEmoteDefinition>(StringComparer.Ordinal)
        {
            ["LUL"] = new("LUL", "https://cdn.example/lul", "bttv"),
            ["LULW"] = new("LULW", "https://cdn.example/lulw", "bttv")
        };

        IReadOnlyList<OverlayChatPart> parts = ChatEmoteEnricher.Enrich(
            [new TwitchChatFragment(TwitchChatFragmentType.Text, "LULW")],
            catalog);

        Assert.Single(parts);
        Assert.Equal("LULW", parts[0].Text);
        Assert.Equal("https://cdn.example/lulw", parts[0].Url);
    }

    [Fact]
    public void Enrich_LeavesUnknownTokensAsText()
    {
        IReadOnlyList<OverlayChatPart> parts = ChatEmoteEnricher.Enrich(
            [new TwitchChatFragment(TwitchChatFragmentType.Text, "hello world")],
            new Dictionary<string, ChatEmoteDefinition>());

        Assert.Single(parts);
        Assert.Equal("text", parts[0].Type);
        Assert.Equal("hello world", parts[0].Text);
    }

    [Fact]
    public void Enrich_MapsMentionsAndCheermotesAsText()
    {
        IReadOnlyList<OverlayChatPart> parts = ChatEmoteEnricher.Enrich(
            [
                new(TwitchChatFragmentType.Mention, "@alice"),
                new(TwitchChatFragmentType.Text, " "),
                new(TwitchChatFragmentType.Cheermote, "Cheer100")
            ],
            new Dictionary<string, ChatEmoteDefinition>());

        Assert.Single(parts);
        Assert.Equal("text", parts[0].Type);
        Assert.Equal("@alice Cheer100", parts[0].Text);
    }
}
