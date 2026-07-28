using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.App.Services;

public sealed record RaidActionState(
    bool ShowStartRaid,
    bool CanCancelRaid,
    bool ShowManualRaidActions);

public static class TwitchDashboardApplicationService
{
    public static IReadOnlyList<string> NormalizeRaidChannels(
        IEnumerable<string> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        return
        [
            .. channels
                .Select(NormalizeChannel)
                .Where(channel => channel.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];
    }

    public static IReadOnlyList<string> RememberRaidChannel(
        IEnumerable<string> channels,
        string channel,
        int maximum = 40)
    {
        ArgumentNullException.ThrowIfNull(channels);
        string normalized = NormalizeChannel(channel);
        if (normalized.Length == 0)
        {
            return NormalizeRaidChannels(channels);
        }

        int limit = Math.Max(1, maximum);
        return
        [
            .. new[] { normalized }
                .Concat(channels.Select(NormalizeChannel))
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(limit)
        ];
    }

    public static string? ResolveChatChannel(
        TwitchConnectionSnapshot snapshot,
        string? configuredChannel)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        string channel = snapshot.ChannelLogin;
        if (string.IsNullOrWhiteSpace(channel))
        {
            channel = !string.IsNullOrWhiteSpace(snapshot.ChannelName)
                ? snapshot.ChannelName
                : configuredChannel ?? "";
        }

        return string.IsNullOrWhiteSpace(channel)
            ? null
            : channel.Trim();
    }

    public static IReadOnlyList<TwitchChannelSuggestion>
        BuildRaidSuggestions(
            IEnumerable<string> recentLogins,
            IEnumerable<TwitchChannelSuggestion> followed,
            IEnumerable<TwitchChannelSuggestion> followedLive,
            IEnumerable<TwitchChannelSuggestion> searched,
            IReadOnlyDictionary<string, TwitchChannelSuggestion> liveByLogin,
            string query,
            int maximum = 25)
    {
        ArgumentNullException.ThrowIfNull(recentLogins);
        ArgumentNullException.ThrowIfNull(followed);
        ArgumentNullException.ThrowIfNull(followedLive);
        ArgumentNullException.ThrowIfNull(searched);
        ArgumentNullException.ThrowIfNull(liveByLogin);

        string normalizedQuery = NormalizeChannel(query);
        int limit = Math.Max(1, maximum);
        var liveLookup = new Dictionary<string, TwitchChannelSuggestion>(
            liveByLogin,
            StringComparer.OrdinalIgnoreCase);
        TwitchChannelSuggestion[] followedItems = [.. followed];
        TwitchChannelSuggestion[] liveItems = [.. followedLive];
        TwitchChannelSuggestion[] searchedItems = [.. searched];
        foreach (TwitchChannelSuggestion item in liveItems.Concat(
                     searchedItems.Where(candidate => candidate.IsLive)))
        {
            liveLookup.TryAdd(
                item.Login,
                item with { IsLive = true, SourceLabel = "Live" });
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<TwitchChannelSuggestion>(limit);
        IEnumerable<TwitchChannelSuggestion> recent =
            NormalizeRaidChannels(recentLogins)
                .Where(login => MatchesRaidQuery(
                    login,
                    login,
                    normalizedQuery))
                .Select(login =>
                {
                    bool isLive = liveLookup.TryGetValue(
                        login,
                        out TwitchChannelSuggestion? live);
                    return new TwitchChannelSuggestion(
                        login,
                        isLive ? live!.DisplayName : login,
                        isLive,
                        "Zuletzt");
                })
                .OrderByDescending(item => item.IsLive);
        AppendUnique(recent, results, seen, limit);

        IEnumerable<TwitchChannelSuggestion> liveSuggestions = liveItems
            .Concat(searchedItems.Where(item => item.IsLive))
            .Concat(liveLookup.Values)
            .Where(item => MatchesRaidQuery(
                item.Login,
                item.DisplayName,
                normalizedQuery))
            .Select(item =>
                item with { IsLive = true, SourceLabel = "Live" });
        AppendUnique(liveSuggestions, results, seen, limit);

        IEnumerable<TwitchChannelSuggestion> offlineSuggestions =
            followedItems
                .Where(item => MatchesRaidQuery(
                    item.Login,
                    item.DisplayName,
                    normalizedQuery))
                .Concat(searchedItems.Where(item => !item.IsLive))
                .Select(item => item with { IsLive = false });
        AppendUnique(offlineSuggestions, results, seen, limit);
        return results;
    }

    public static bool MatchesRaidQuery(
        string login,
        string displayName,
        string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return login.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               displayName.Contains(
                   query,
                   StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> BuildRaidStatusProbeLogins(
        IEnumerable<string> recentLogins,
        IEnumerable<TwitchChannelSuggestion> followed,
        string query,
        int maximum = 80)
    {
        ArgumentNullException.ThrowIfNull(recentLogins);
        ArgumentNullException.ThrowIfNull(followed);

        string normalizedQuery = NormalizeChannel(query);
        int limit = Math.Max(1, maximum);
        return
        [
            .. NormalizeRaidChannels(recentLogins)
                .Where(login => MatchesRaidQuery(
                    login,
                    login,
                    normalizedQuery))
                .Concat(followed
                    .Where(item => MatchesRaidQuery(
                        item.Login,
                        item.DisplayName,
                        normalizedQuery))
                    .Select(item => NormalizeChannel(item.Login)))
                .Where(login => login.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(limit)
        ];
    }

    public static string FormatRaidLiveDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}:{duration.Minutes:00} Std.";
        }

        return $"{Math.Max(0, duration.Minutes)} Min.";
    }

    public static RaidActionState ProjectRaidActions(
        bool hasTarget,
        bool targetOnline,
        bool countdownActive,
        bool awaitingManualRaid,
        bool streamEndFlowActive)
    {
        bool raidReady =
            hasTarget &&
            targetOnline &&
            !countdownActive &&
            (awaitingManualRaid || streamEndFlowActive);
        return new RaidActionState(
            ShowStartRaid: raidReady || countdownActive,
            CanCancelRaid: countdownActive,
            ShowManualRaidActions:
                awaitingManualRaid && !countdownActive);
    }

    private static string NormalizeChannel(string? channel) =>
        channel?.Trim().TrimStart('@') ?? "";

    private static void AppendUnique(
        IEnumerable<TwitchChannelSuggestion> candidates,
        ICollection<TwitchChannelSuggestion> results,
        ISet<string> seen,
        int maximum)
    {
        foreach (TwitchChannelSuggestion item in candidates)
        {
            if (!seen.Add(item.Login))
            {
                continue;
            }

            results.Add(item);
            if (results.Count >= maximum)
            {
                return;
            }
        }
    }
}
