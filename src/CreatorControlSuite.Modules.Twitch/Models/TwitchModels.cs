namespace CreatorControlSuite.Modules.Twitch.Models;

public sealed record TwitchDeviceCode(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int ExpiresInSeconds,
    int PollIntervalSeconds);

public sealed record TwitchTokenSet(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    IReadOnlyList<string> Scopes,
    DateTimeOffset ObtainedAt)
{
    public DateTimeOffset ExpiresAt =>
        ObtainedAt.AddSeconds(Math.Max(0, ExpiresInSeconds - 60));
}

public sealed record TwitchTokenValidation(
    string ClientId,
    string Login,
    string UserId,
    IReadOnlyList<string> Scopes,
    int ExpiresInSeconds);

public sealed record TwitchUser(
    string Id,
    string Login,
    string DisplayName,
    string ProfileImageUrl);

public sealed record TwitchChannelInformation(
    string BroadcasterId,
    string BroadcasterLogin,
    string BroadcasterName,
    string GameId,
    string GameName,
    string Title,
    string Language);

public sealed record TwitchCategory(
    string Id,
    string Name,
    string BoxArtUrl);
public sealed record TwitchRaidTargetStatus(
    string Login,
    string DisplayName,
    string ProfileImageUrl,
    bool IsOnline,
    string GameName,
    string StreamTitle,
    int ViewerCount,
    DateTimeOffset? StartedAt,
    string ChannelUrl);


public sealed record TwitchChatMessage(
    string MessageId,
    string BroadcasterUserId,
    string ChatterUserId,
    string ChatterLogin,
    string ChatterName,
    string MessageText,
    string Color,
    DateTimeOffset ReceivedAt,
    IReadOnlyList<string> Badges);

public sealed record TwitchEvent(
    string Type,
    string Summary,
    DateTimeOffset ReceivedAt,
    IReadOnlyDictionary<string, string> Data);

public sealed record TwitchConnectionSnapshot(
    bool Authenticated,
    bool EventSubConnected,
    string Login,
    string UserId,
    string ChannelLogin,
    string ChannelName,
    string ChannelTitle,
    string CategoryName,
    IReadOnlyList<string> Scopes);

public sealed record TwitchChannelPointReward(
    string Id,
    string Title,
    int Cost,
    string Prompt,
    bool IsEnabled,
    string BackgroundColor);

public sealed record TwitchPoll(
    string Id,
    string Title,
    string Status,
    DateTimeOffset? EndsAt);

public sealed record TwitchPredictionOutcome(string Id, string Title, int ChannelPoints);

public sealed record TwitchPrediction(
    string Id,
    string Title,
    string Status,
    DateTimeOffset? LocksAt,
    IReadOnlyList<TwitchPredictionOutcome> Outcomes);

public sealed record TwitchRewardRedemption(
    string Id,
    string RewardId,
    string RewardTitle,
    string UserLogin,
    string UserDisplayName,
    string UserInput,
    string Status,
    DateTimeOffset RedeemedAt);
