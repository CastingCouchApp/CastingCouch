using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.Modules.Twitch;

public interface ITwitchApiClient
{
    void Configure(
        string clientId,
        string accessToken);

    Task<TwitchUser> GetCurrentUserAsync(
        CancellationToken cancellationToken = default);

    Task<TwitchUser?> GetUserByLoginAsync(
        string login,
        CancellationToken cancellationToken = default);

    Task<TwitchChannelInformation> GetChannelInformationAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default);

    Task UpdateChannelInformationAsync(
        string broadcasterId,
        string? title,
        string? categoryId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TwitchCategory>> SearchCategoriesAsync(
        string query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TwitchChannelSuggestion>> SearchChannelsAsync(
        string query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TwitchChannelSuggestion>> GetFollowedChannelsAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TwitchChannelSuggestion>> GetFollowedLiveStreamsAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, TwitchChannelSuggestion>> GetLiveChannelsByLoginsAsync(
        IEnumerable<string> logins,
        CancellationToken cancellationToken = default);

    Task<TwitchRaidTargetStatus?> GetRaidTargetStatusAsync(
        string login,
        CancellationToken cancellationToken = default);

    Task StartRaidAsync(
        string fromBroadcasterId,
        string toBroadcasterId,
        CancellationToken cancellationToken = default);

    Task CancelRaidAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default);

    Task<int> GetFollowerCountAsync(
        string broadcasterId,
        string moderatorId,
        CancellationToken cancellationToken = default);

    Task<int> GetActiveSubscriptionCountAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetChattersAsync(
        string broadcasterId,
        string moderatorId,
        CancellationToken cancellationToken = default);

    Task SendChatMessageAsync(
        string broadcasterId,
        string senderId,
        string message,
        CancellationToken cancellationToken = default);

    Task BanUserAsync(
        string broadcasterId,
        string moderatorId,
        string userId,
        int? durationSeconds,
        string? reason,
        CancellationToken cancellationToken = default);

    Task UnbanUserAsync(
        string broadcasterId,
        string moderatorId,
        string userId,
        CancellationToken cancellationToken = default);


    Task<IReadOnlyList<TwitchChannelPointReward>> GetCustomRewardsAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default);

    Task<TwitchChannelPointReward> CreateCustomRewardAsync(
        string broadcasterId,
        string title,
        int cost,
        string? prompt,
        CancellationToken cancellationToken = default);

    Task<TwitchPoll> CreatePollAsync(
        string broadcasterId,
        string title,
        IReadOnlyList<string> choices,
        int durationSeconds,
        CancellationToken cancellationToken = default);

    Task<TwitchPrediction> CreatePredictionAsync(
        string broadcasterId,
        string title,
        IReadOnlyList<string> outcomes,
        int predictionWindowSeconds,
        CancellationToken cancellationToken = default);

    Task<TwitchPoll> EndPollAsync(string broadcasterId, string pollId, string status, CancellationToken cancellationToken = default);

    Task<TwitchPrediction> EndPredictionAsync(string broadcasterId, string predictionId, string status, string? winningOutcomeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TwitchRewardRedemption>> GetRewardRedemptionsAsync(string broadcasterId, string rewardId, string status, CancellationToken cancellationToken = default);

    Task UpdateRewardRedemptionStatusAsync(string broadcasterId, string rewardId, string redemptionId, string status, CancellationToken cancellationToken = default);

    Task CreateEventSubSubscriptionAsync(
        string type,
        string version,
        object condition,
        string sessionId,
        CancellationToken cancellationToken = default);
}
