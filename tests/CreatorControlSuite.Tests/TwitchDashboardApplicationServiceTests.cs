using CreatorControlSuite.App.Services;
using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.Tests;

public sealed class TwitchDashboardApplicationServiceTests
{
    [Fact]
    public void NormalizeRaidChannels_TrimsHandlesAndDeduplicates()
    {
        IReadOnlyList<string> channels =
            TwitchDashboardApplicationService.NormalizeRaidChannels(
                [" @Alpha ", "alpha", "", "@Beta"]);

        Assert.Equal(["Alpha", "Beta"], channels);
    }

    [Fact]
    public void RememberRaidChannel_MovesTargetToFrontAndCapsHistory()
    {
        string[] existing =
        [
            .. Enumerable.Range(1, 45).Select(index => $"channel{index}"),
            "TARGET"
        ];

        IReadOnlyList<string> channels =
            TwitchDashboardApplicationService.RememberRaidChannel(
                existing,
                "@target",
                maximum: 40);

        Assert.Equal("target", channels[0]);
        Assert.Equal(40, channels.Count);
        Assert.Single(
            channels,
            channel => string.Equals(
                channel,
                "target",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildRaidSuggestions_PrioritizesRecentThenLiveThenOffline()
    {
        TwitchChannelSuggestion recentLive =
            Suggestion("recent-live", isLive: true, "Live lookup");
        IReadOnlyList<TwitchChannelSuggestion> suggestions =
            TwitchDashboardApplicationService.BuildRaidSuggestions(
                recentLogins: ["recent-offline", "recent-live"],
                followed:
                [
                    Suggestion("followed-offline", false, "Gefolgt"),
                    Suggestion("duplicate-live", false, "Gefolgt")
                ],
                followedLive:
                [
                    Suggestion("live-follow", true, "Gefolgt live"),
                    Suggestion("duplicate-live", true, "Gefolgt live")
                ],
                searched:
                [
                    Suggestion("search-live", true, "Suche"),
                    Suggestion("search-offline", false, "Suche")
                ],
                liveByLogin: new Dictionary<string, TwitchChannelSuggestion>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["recent-live"] = recentLive
                },
                query: "",
                maximum: 25);

        Assert.Equal(
            [
                "recent-live",
                "recent-offline",
                "live-follow",
                "duplicate-live",
                "search-live",
                "followed-offline",
                "search-offline"
            ],
            suggestions.Select(item => item.Login));
        Assert.Equal(
            suggestions.Count,
            suggestions
                .Select(item => item.Login)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
    }

    [Fact]
    public void BuildRaidStatusProbeLogins_FiltersAndDeduplicatesCandidates()
    {
        IReadOnlyList<string> logins =
            TwitchDashboardApplicationService.BuildRaidStatusProbeLogins(
                recentLogins: [" @Alpha ", "beta", "alpha"],
                followed:
                [
                    Suggestion("alphabet", false, "Gefolgt"),
                    Suggestion("BETA", false, "Gefolgt"),
                    Suggestion("gamma", false, "Gefolgt")
                ],
                query: "alp",
                maximum: 2);

        Assert.Equal(["Alpha", "alphabet"], logins);
    }

    [Fact]
    public void ResolveChatChannel_PrefersCanonicalChannelLogin()
    {
        TwitchConnectionSnapshot snapshot = Snapshot(
            channelLogin: "canonical",
            channelName: "Display Name",
            login: "account");

        Assert.Equal(
            "canonical",
            TwitchDashboardApplicationService.ResolveChatChannel(
                snapshot,
                "configured"));
        Assert.Equal(
            "configured",
            TwitchDashboardApplicationService.ResolveChatChannel(
                Snapshot("", "", ""),
                " configured "));
    }

    [Theory]
    [InlineData(0, "0 Min.")]
    [InlineData(59, "59 Min.")]
    [InlineData(62, "1:02 Std.")]
    public void FormatRaidLiveDuration_UsesCompactGermanText(
        int minutes,
        string expected)
    {
        Assert.Equal(
            expected,
            TwitchDashboardApplicationService.FormatRaidLiveDuration(
                TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public void ProjectRaidActions_ReflectsCountdownAndManualFlow()
    {
        RaidActionState ready =
            TwitchDashboardApplicationService.ProjectRaidActions(
                hasTarget: true,
                targetOnline: true,
                countdownActive: false,
                awaitingManualRaid: true,
                streamEndFlowActive: false);
        RaidActionState countdown =
            TwitchDashboardApplicationService.ProjectRaidActions(
                hasTarget: false,
                targetOnline: false,
                countdownActive: true,
                awaitingManualRaid: false,
                streamEndFlowActive: false);

        Assert.True(ready.ShowStartRaid);
        Assert.False(ready.CanCancelRaid);
        Assert.True(ready.ShowManualRaidActions);
        Assert.True(countdown.ShowStartRaid);
        Assert.True(countdown.CanCancelRaid);
        Assert.False(countdown.ShowManualRaidActions);
    }

    private static TwitchChannelSuggestion Suggestion(
        string login,
        bool isLive,
        string source) =>
        new(login, login, isLive, source);

    private static TwitchConnectionSnapshot Snapshot(
        string channelLogin,
        string channelName,
        string login) =>
        new(
            Authenticated: true,
            EventSubConnected: true,
            Login: login,
            UserId: "id",
            ChannelLogin: channelLogin,
            ChannelName: channelName,
            ChannelTitle: "",
            CategoryName: "",
            Scopes: []);
}
