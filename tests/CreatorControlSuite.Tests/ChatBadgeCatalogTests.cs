using CreatorControlSuite.Modules.Twitch;
using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.Tests;

public sealed class ChatBadgeCatalogTests
{
    [Fact]
    public void Resolve_MapsSetAndVersionToImageUrl()
    {
        var catalog = new ChatBadgeCatalog();
        catalog.Replace(
            [
                new ChatBadgeDefinition("moderator", "1", "https://cdn/mod", "Moderator"),
                new ChatBadgeDefinition("subscriber", "12", "https://cdn/sub12", "12-Month Subscriber")
            ]);

        Assert.Equal(
            "https://cdn/mod",
            catalog.ResolveUrl("moderator", "1"));
        Assert.Equal(
            "https://cdn/sub12",
            catalog.ResolveUrl("subscriber", "12"));
        Assert.Null(catalog.ResolveUrl("totally-unknown", "1"));
    }

    [Fact]
    public void Resolve_FallsBackToKnownGlobalBadgeIcon()
    {
        var catalog = new ChatBadgeCatalog();

        string? url = catalog.ResolveUrl("moderator", "1");

        Assert.False(string.IsNullOrWhiteSpace(url));
        Assert.StartsWith("https://static-cdn.jtvnw.net/badges/", url);
    }

    [Fact]
    public void ResolveBadges_OnlyReturnsBadgesWithIcons()
    {
        var catalog = new ChatBadgeCatalog();
        catalog.Replace(
            [new ChatBadgeDefinition("vip", "1", "https://cdn/vip", "VIP")]);

        IReadOnlyList<ResolvedChatBadge> parts = catalog.ResolveBadges(
        [
            new TwitchChatBadge("vip", "1"),
            new TwitchChatBadge("unknown-custom", "9")
        ]);

        Assert.Single(parts);
        Assert.Equal("vip", parts[0].SetId);
        Assert.Equal("https://cdn/vip", parts[0].Url);
        Assert.Equal("VIP", parts[0].Title);
    }

    [Fact]
    public void Resolve_FallsBackToAnyVersionInSet()
    {
        var catalog = new ChatBadgeCatalog();
        catalog.Replace(
            [new ChatBadgeDefinition("subscriber", "12", "https://cdn/sub12", "Sub")]);

        Assert.Equal("https://cdn/sub12", catalog.ResolveUrl("subscriber", "99"));
    }
}
