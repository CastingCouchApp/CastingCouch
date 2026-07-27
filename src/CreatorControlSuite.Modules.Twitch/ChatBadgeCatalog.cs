using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.Modules.Twitch;

public sealed record ChatBadgeDefinition(
    string SetId,
    string VersionId,
    string ImageUrl,
    string Title);

public sealed record ResolvedChatBadge(
    string SetId,
    string Id,
    string? Url,
    string Title);

public interface IChatBadgeCatalog
{
    void Replace(IEnumerable<ChatBadgeDefinition> badges);

    string? ResolveUrl(string setId, string versionId);

    IReadOnlyList<ResolvedChatBadge> ResolveBadges(IReadOnlyList<TwitchChatBadge> badges);

    Task RefreshAsync(
        ITwitchApiClient apiClient,
        string broadcasterUserId,
        CancellationToken cancellationToken = default);
}

public sealed class ChatBadgeCatalog : IChatBadgeCatalog
{
    // Stabile Twitch-CDN-Fallbacks für globale Badges (wenn Helix noch nicht geladen ist).
    private static readonly IReadOnlyDictionary<string, ChatBadgeDefinition> KnownGlobalFallbacks =
        new Dictionary<string, ChatBadgeDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["broadcaster"] = new(
                "broadcaster",
                "1",
                "https://static-cdn.jtvnw.net/badges/v1/5527c58c-fb7d-422d-b71b-f309dcb85b62/2",
                "Broadcaster"),
            ["moderator"] = new(
                "moderator",
                "1",
                "https://static-cdn.jtvnw.net/badges/v1/3267646d-33f0-4b17-b3df-f923a41db1d0/2",
                "Moderator"),
            ["vip"] = new(
                "vip",
                "1",
                "https://static-cdn.jtvnw.net/badges/v1/b817aba4-fad8-49e2-b88a-7cc724473d84/2",
                "VIP"),
            ["subscriber"] = new(
                "subscriber",
                "0",
                "https://static-cdn.jtvnw.net/badges/v1/5d9f2208-5dd8-11e7-8513-2ff4adfae661/2",
                "Subscriber"),
            ["founder"] = new(
                "founder",
                "0",
                "https://static-cdn.jtvnw.net/badges/v1/511b78a9-ab37-472f-9561-314f1bd5d137/2",
                "Founder"),
            ["premium"] = new(
                "premium",
                "1",
                "https://static-cdn.jtvnw.net/badges/v1/a1dd5073-19c3-4911-8cb4-c464a7bc1510/2",
                "Prime Gaming"),
            ["partner"] = new(
                "partner",
                "1",
                "https://static-cdn.jtvnw.net/badges/v1/d12a2e27-16f6-41d0-ab77-b780518f00a3/2",
                "Verified"),
            ["staff"] = new(
                "staff",
                "1",
                "https://static-cdn.jtvnw.net/badges/v1/d97c37bd-a6f5-4c38-8f57-4e4bef88af34/2",
                "Staff"),
            ["admin"] = new(
                "admin",
                "1",
                "https://static-cdn.jtvnw.net/badges/v1/9ef7e029-4ccd-4e57-8b9a-7b4f57b40c07/2",
                "Admin"),
            ["global_mod"] = new(
                "global_mod",
                "1",
                "https://static-cdn.jtvnw.net/badges/v1/9384cfc3-b2d1-412f-8bdc-a8bc1538c7d0/2",
                "Global Mod"),
            ["artist-badge"] = new(
                "artist-badge",
                "1",
                "https://static-cdn.jtvnw.net/badges/v1/4300a3ff-7b9f-39d9-a8b7-85a9c0d0d4a9/2",
                "Artist"),
            ["predictions"] = new(
                "predictions",
                "blue-1",
                "https://static-cdn.jtvnw.net/badges/v1/e33d8b46-f63b-4e67-996d-4a7dce66ad0d/2",
                "Predictions")
        };

    private readonly object _gate = new();
    private Dictionary<string, ChatBadgeDefinition> _badges = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ChatBadgeDefinition> _bySet =
        new(StringComparer.OrdinalIgnoreCase);

    public void Replace(IEnumerable<ChatBadgeDefinition> badges)
    {
        var map = new Dictionary<string, ChatBadgeDefinition>(StringComparer.OrdinalIgnoreCase);
        var bySet = new Dictionary<string, ChatBadgeDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (ChatBadgeDefinition badge in badges)
        {
            if (string.IsNullOrWhiteSpace(badge.SetId) ||
                string.IsNullOrWhiteSpace(badge.VersionId) ||
                string.IsNullOrWhiteSpace(badge.ImageUrl))
            {
                continue;
            }

            map[Key(badge.SetId, badge.VersionId)] = badge;
            bySet[badge.SetId] = badge;
        }

        lock (_gate)
        {
            _badges = map;
            _bySet = bySet;
        }
    }

    public string? ResolveUrl(string setId, string versionId) =>
        Find(setId, versionId)?.ImageUrl;

    public IReadOnlyList<ResolvedChatBadge> ResolveBadges(IReadOnlyList<TwitchChatBadge> badges)
    {
        if (badges is null || badges.Count == 0)
        {
            return [];
        }

        var resolved = new List<ResolvedChatBadge>(badges.Count);
        foreach (TwitchChatBadge badge in badges)
        {
            ChatBadgeDefinition? def = Find(badge.SetId, badge.Id);
            if (def is null || string.IsNullOrWhiteSpace(def.ImageUrl))
            {
                continue;
            }

            resolved.Add(new ResolvedChatBadge(
                badge.SetId,
                badge.Id,
                def.ImageUrl,
                string.IsNullOrWhiteSpace(def.Title) ? badge.SetId : def.Title));
        }

        return resolved;
    }

    public async Task RefreshAsync(
        ITwitchApiClient apiClient,
        string broadcasterUserId,
        CancellationToken cancellationToken = default)
    {
        var all = new List<ChatBadgeDefinition>();
        try
        {
            all.AddRange(await apiClient.GetGlobalChatBadgesAsync(cancellationToken));
            if (!string.IsNullOrWhiteSpace(broadcasterUserId))
            {
                all.AddRange(await apiClient.GetChannelChatBadgesAsync(
                    broadcasterUserId,
                    cancellationToken));
            }
        }
        catch
        {
            // best-effort – Fallbacks greifen weiterhin
        }

        Replace(all);
    }

    private ChatBadgeDefinition? Find(string setId, string versionId)
    {
        if (string.IsNullOrWhiteSpace(setId))
        {
            return null;
        }

        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(versionId) &&
                _badges.TryGetValue(Key(setId, versionId), out ChatBadgeDefinition? exact))
            {
                return exact;
            }

            if (_bySet.TryGetValue(setId, out ChatBadgeDefinition? anyVersion))
            {
                return anyVersion;
            }
        }

        return KnownGlobalFallbacks.TryGetValue(setId, out ChatBadgeDefinition? fallback)
            ? fallback
            : null;
    }

    private static string Key(string setId, string versionId) =>
        (setId ?? "").Trim() + "/" + (versionId ?? "").Trim();
}
