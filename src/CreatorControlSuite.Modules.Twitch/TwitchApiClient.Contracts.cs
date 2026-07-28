using System.Text.Json.Serialization;

namespace CreatorControlSuite.Modules.Twitch;

public sealed partial class TwitchApiClient
{
    private sealed class RewardListResponse { [JsonPropertyName("data")] public RewardData[] Data { get; set; } = []; }
    private sealed class RewardData
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("cost")] public int Cost { get; set; }
        [JsonPropertyName("prompt")] public string Prompt { get; set; } = "";
        [JsonPropertyName("is_enabled")] public bool IsEnabled { get; set; }
        [JsonPropertyName("background_color")] public string BackgroundColor { get; set; } = "";
    }
    private sealed class PollListResponse { [JsonPropertyName("data")] public PollData[] Data { get; set; } = []; }
    private sealed class PollData
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("ends_at")] public DateTimeOffset? EndedAt { get; set; }
    }
    private sealed class PredictionListResponse { [JsonPropertyName("data")] public PredictionData[] Data { get; set; } = []; }
    private sealed class PredictionData
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("locks_at")] public DateTimeOffset? LocksAt { get; set; }
        [JsonPropertyName("outcomes")] public PredictionOutcomeData[] Outcomes { get; set; } = [];
    }
    private sealed class PredictionOutcomeData
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("channel_points")] public int ChannelPoints { get; set; }
    }
    private sealed class RedemptionListResponse { [JsonPropertyName("data")] public RedemptionData[] Data { get; set; } = []; }
    private sealed class RedemptionData
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("user_login")] public string UserLogin { get; set; } = "";
        [JsonPropertyName("user_name")] public string UserName { get; set; } = "";
        [JsonPropertyName("user_input")] public string UserInput { get; set; } = "";
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("redeemed_at")] public DateTimeOffset RedeemedAt { get; set; }
        [JsonPropertyName("reward")] public RedemptionRewardData Reward { get; set; } = new();
    }
    private sealed class RedemptionRewardData { [JsonPropertyName("title")] public string Title { get; set; } = ""; }

    private sealed class StreamListResponse
    {
        [JsonPropertyName("data")]
        public StreamData[] Data { get; set; } = [];

        [JsonPropertyName("pagination")]
        public PaginationData? Pagination { get; set; }
    }

    private sealed class StreamData
    {
        [JsonPropertyName("user_login")]
        public string UserLogin { get; set; } = "";

        [JsonPropertyName("user_name")]
        public string UserName { get; set; } = "";

        [JsonPropertyName("game_name")]
        public string GameName { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("viewer_count")]
        public int ViewerCount { get; set; }

        [JsonPropertyName("started_at")]
        public DateTimeOffset? StartedAt { get; set; }
    }

    private sealed class SubscriptionsResponse
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }
    }

    private sealed class FollowersResponse
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }
    }

    private sealed class ChattersResponse
    {
        [JsonPropertyName("data")]
        public ChatterData[] Data { get; set; } = [];
    }

    private sealed class ChatterData
    {
        [JsonPropertyName("user_login")]
        public string UserLogin { get; set; } = "";

        [JsonPropertyName("user_name")]
        public string UserName { get; set; } = "";
    }

    private sealed class UserListResponse
    {
        [JsonPropertyName("data")]
        public UserData[] Data { get; set; } = [];
    }

    private sealed class UserData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("login")]
        public string Login { get; set; } = "";

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = "";

        [JsonPropertyName("profile_image_url")]
        public string ProfileImageUrl { get; set; } = "";
    }

    private sealed class ChannelListResponse
    {
        [JsonPropertyName("data")]
        public ChannelData[] Data { get; set; } = [];
    }

    private sealed class ChannelData
    {
        [JsonPropertyName("broadcaster_id")]
        public string BroadcasterId { get; set; } = "";

        [JsonPropertyName("broadcaster_login")]
        public string BroadcasterLogin { get; set; } = "";

        [JsonPropertyName("broadcaster_name")]
        public string BroadcasterName { get; set; } = "";

        [JsonPropertyName("game_id")]
        public string GameId { get; set; } = "";

        [JsonPropertyName("game_name")]
        public string GameName { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("broadcaster_language")]
        public string BroadcasterLanguage { get; set; } = "";
    }

    private sealed class CategoryListResponse
    {
        [JsonPropertyName("data")]
        public CategoryData[] Data { get; set; } = [];
    }

    private sealed class CategoryData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("box_art_url")]
        public string BoxArtUrl { get; set; } = "";
    }

    private sealed class ChannelSearchListResponse
    {
        [JsonPropertyName("data")]
        public ChannelSearchData[] Data { get; set; } = [];
    }

    private sealed class ChannelSearchData
    {
        [JsonPropertyName("broadcaster_login")]
        public string BroadcasterLogin { get; set; } = "";

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = "";

        [JsonPropertyName("is_live")]
        public bool IsLive { get; set; }
    }

    private sealed class FollowedChannelsResponse
    {
        [JsonPropertyName("data")]
        public FollowedChannelData[] Data { get; set; } = [];

        [JsonPropertyName("pagination")]
        public PaginationData? Pagination { get; set; }
    }

    private sealed class FollowedChannelData
    {
        [JsonPropertyName("broadcaster_login")]
        public string BroadcasterLogin { get; set; } = "";

        [JsonPropertyName("broadcaster_name")]
        public string BroadcasterName { get; set; } = "";
    }

    private sealed class PaginationData
    {
        [JsonPropertyName("cursor")]
        public string? Cursor { get; set; }
    }

    private sealed class SendChatMessageResponse
    {
        [JsonPropertyName("data")]
        public SendChatMessageData[] Data { get; set; } = [];
    }

    private sealed class SendChatMessageData
    {
        [JsonPropertyName("is_sent")]
        public bool IsSent { get; set; }

        [JsonPropertyName("drop_reason")]
        public DropReason? DropReason { get; set; }
    }

    private sealed class DropReason
    {
        [JsonPropertyName("message")]
        public string Message { get; set; } = "";
    }

    private sealed class ChatBadgeListResponse
    {
        [JsonPropertyName("data")]
        public ChatBadgeSetData[] Data { get; set; } = [];
    }

    private sealed class ChatBadgeSetData
    {
        [JsonPropertyName("set_id")]
        public string SetId { get; set; } = "";

        [JsonPropertyName("versions")]
        public ChatBadgeVersionData[] Versions { get; set; } = [];
    }

    private sealed class ChatBadgeVersionData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("image_url_1x")]
        public string ImageUrl1x { get; set; } = "";

        [JsonPropertyName("image_url_2x")]
        public string ImageUrl2x { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";
    }
}
