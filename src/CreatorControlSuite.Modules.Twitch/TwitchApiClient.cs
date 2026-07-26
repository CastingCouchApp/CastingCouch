using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.Modules.Twitch;

public sealed class TwitchApiClient : ITwitchApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private string _clientId = "";
    private string _accessToken = "";

    public TwitchApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public void Configure(
        string clientId,
        string accessToken)
    {
        _clientId = clientId;
        _accessToken = accessToken;
    }

    public async Task<TwitchUser> GetCurrentUserAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<UserListResponse>(
            HttpMethod.Get,
            "users",
            body: null,
            cancellationToken);

        var user = response.Data.FirstOrDefault()
                   ?? throw new InvalidOperationException(
                       "Twitch-Benutzer konnte nicht ermittelt werden.");

        return ToUser(user);
    }

    public async Task<TwitchUser?> GetUserByLoginAsync(
        string login,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<UserListResponse>(
            HttpMethod.Get,
            "users?login=" + Uri.EscapeDataString(login),
            body: null,
            cancellationToken);

        var user = response.Data.FirstOrDefault();

        return user is null
            ? null
            : ToUser(user);
    }

    public async Task<TwitchChannelInformation> GetChannelInformationAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<ChannelListResponse>(
            HttpMethod.Get,
            "channels?broadcaster_id=" +
            Uri.EscapeDataString(broadcasterId),
            body: null,
            cancellationToken);

        var channel = response.Data.FirstOrDefault()
                      ?? throw new InvalidOperationException(
                          "Twitch-Kanalinformationen fehlen.");

        return new TwitchChannelInformation(
            channel.BroadcasterId,
            channel.BroadcasterLogin,
            channel.BroadcasterName,
            channel.GameId,
            channel.GameName,
            channel.Title,
            channel.BroadcasterLanguage);
    }

    public async Task UpdateChannelInformationAsync(
        string broadcasterId,
        string? title,
        string? categoryId,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object>();

        if (!string.IsNullOrWhiteSpace(title))
        {
            body["title"] = title;
        }

        if (!string.IsNullOrWhiteSpace(categoryId))
        {
            body["game_id"] = categoryId;
        }

        _ = await SendRawAsync(
            HttpMethod.Patch,
            "channels?broadcaster_id=" +
            Uri.EscapeDataString(broadcasterId),
            body,
            cancellationToken);
    }

    public async Task<IReadOnlyList<TwitchCategory>> SearchCategoriesAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<CategoryListResponse>(
            HttpMethod.Get,
            "search/categories?query=" +
            Uri.EscapeDataString(query) +
            "&first=20",
            body: null,
            cancellationToken);

        return response.Data
            .Select(category => new TwitchCategory(
                category.Id,
                category.Name,
                category.BoxArtUrl))
            .ToList();
    }

    public async Task<IReadOnlyList<TwitchChannelSuggestion>> SearchChannelsAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<ChannelSearchListResponse>(
            HttpMethod.Get,
            "search/channels?query=" +
            Uri.EscapeDataString(query) +
            "&first=20",
            body: null,
            cancellationToken);

        return response.Data
            .OrderByDescending(channel => channel.IsLive)
            .Select(channel => new TwitchChannelSuggestion(
                channel.BroadcasterLogin,
                string.IsNullOrWhiteSpace(channel.DisplayName)
                    ? channel.BroadcasterLogin
                    : channel.DisplayName,
                channel.IsLive,
                "Suche"))
            .ToList();
    }

    public async Task<IReadOnlyList<TwitchChannelSuggestion>> GetFollowedChannelsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var results = new List<TwitchChannelSuggestion>();
        string? cursor = null;

        do
        {
            var url = "channels/followed?user_id=" +
                      Uri.EscapeDataString(userId) +
                      "&first=100";
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                url += "&after=" + Uri.EscapeDataString(cursor);
            }

            var response = await SendAsync<FollowedChannelsResponse>(
                HttpMethod.Get,
                url,
                body: null,
                cancellationToken);

            results.AddRange(response.Data.Select(channel => new TwitchChannelSuggestion(
                channel.BroadcasterLogin,
                string.IsNullOrWhiteSpace(channel.BroadcasterName)
                    ? channel.BroadcasterLogin
                    : channel.BroadcasterName,
                IsLive: false,
                "Gefolgt")));

            cursor = response.Pagination?.Cursor;
            if (results.Count >= 300)
            {
                break;
            }
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        return results;
    }

    public async Task<IReadOnlyList<TwitchChannelSuggestion>> GetFollowedLiveStreamsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var results = new List<TwitchChannelSuggestion>();
        string? cursor = null;

        do
        {
            var url = "streams/followed?user_id=" +
                      Uri.EscapeDataString(userId) +
                      "&first=100";
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                url += "&after=" + Uri.EscapeDataString(cursor);
            }

            var response = await SendAsync<StreamListResponse>(
                HttpMethod.Get,
                url,
                body: null,
                cancellationToken);

            results.AddRange(response.Data
                .Where(stream => !string.IsNullOrWhiteSpace(stream.UserLogin))
                .Select(stream => new TwitchChannelSuggestion(
                    stream.UserLogin,
                    string.IsNullOrWhiteSpace(stream.UserName)
                        ? stream.UserLogin
                        : stream.UserName,
                    IsLive: true,
                    "Live")));

            cursor = response.Pagination?.Cursor;
            if (results.Count >= 100)
            {
                break;
            }
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        return results;
    }

    public async Task<IReadOnlyDictionary<string, TwitchChannelSuggestion>> GetLiveChannelsByLoginsAsync(
        IEnumerable<string> logins,
        CancellationToken cancellationToken = default)
    {
        var unique = logins
            .Select(login => login.Trim().TrimStart('@'))
            .Where(login => !string.IsNullOrWhiteSpace(login))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToList();

        if (unique.Count == 0)
        {
            return new Dictionary<string, TwitchChannelSuggestion>(StringComparer.OrdinalIgnoreCase);
        }

        var query = string.Join(
            "&",
            unique.Select(login => "user_login=" + Uri.EscapeDataString(login)));
        var response = await SendAsync<StreamListResponse>(
            HttpMethod.Get,
            "streams?" + query,
            body: null,
            cancellationToken);

        return response.Data
            .Where(stream => !string.IsNullOrWhiteSpace(stream.UserLogin))
            .GroupBy(stream => stream.UserLogin, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var stream = group.First();
                    return new TwitchChannelSuggestion(
                        stream.UserLogin,
                        string.IsNullOrWhiteSpace(stream.UserName)
                            ? stream.UserLogin
                            : stream.UserName,
                        IsLive: true,
                        "Live");
                },
                StringComparer.OrdinalIgnoreCase);
    }

    public async Task<TwitchRaidTargetStatus?> GetRaidTargetStatusAsync(
        string login,
        CancellationToken cancellationToken = default)
    {
        var user = await GetUserByLoginAsync(login, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var response = await SendAsync<StreamListResponse>(
            HttpMethod.Get,
            "streams?user_id=" + Uri.EscapeDataString(user.Id) + "&first=1",
            body: null,
            cancellationToken);

        var stream = response.Data.FirstOrDefault();
        return new TwitchRaidTargetStatus(
            user.Login,
            user.DisplayName,
            user.ProfileImageUrl,
            stream is not null,
            stream?.GameName ?? "Offline",
            stream?.Title ?? "",
            stream?.ViewerCount ?? 0,
            stream?.StartedAt,
            "https://www.twitch.tv/" + Uri.EscapeDataString(user.Login));
    }

    public async Task StartRaidAsync(
        string fromBroadcasterId,
        string toBroadcasterId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendRawAsync(
            HttpMethod.Post,
            "raids?from_broadcaster_id=" + Uri.EscapeDataString(fromBroadcasterId) +
            "&to_broadcaster_id=" + Uri.EscapeDataString(toBroadcasterId),
            body: null,
            cancellationToken);
    }

    public async Task CancelRaidAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendRawAsync(
            HttpMethod.Delete,
            "raids?broadcaster_id=" + Uri.EscapeDataString(broadcasterId),
            body: null,
            cancellationToken);
    }

    public async Task<int> GetFollowerCountAsync(
        string broadcasterId,
        string moderatorId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<FollowersResponse>(
            HttpMethod.Get,
            "channels/followers?broadcaster_id=" +
            Uri.EscapeDataString(broadcasterId) +
            "&moderator_id=" +
            Uri.EscapeDataString(moderatorId) +
            "&first=1",
            body: null,
            cancellationToken);

        return Math.Max(0, response.Total);
    }

    public async Task<int> GetActiveSubscriptionCountAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<SubscriptionsResponse>(
            HttpMethod.Get,
            "subscriptions?broadcaster_id=" +
            Uri.EscapeDataString(broadcasterId) +
            "&first=1",
            body: null,
            cancellationToken);

        return Math.Max(0, response.Total);
    }

    public async Task<IReadOnlyList<string>> GetChattersAsync(
        string broadcasterId,
        string moderatorId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<ChattersResponse>(
            HttpMethod.Get,
            "chat/chatters?broadcaster_id=" + Uri.EscapeDataString(broadcasterId) +
            "&moderator_id=" + Uri.EscapeDataString(moderatorId) +
            "&first=1000",
            body: null,
            cancellationToken);

        return response.Data
            .Select(item => string.IsNullOrWhiteSpace(item.UserName) ? item.UserLogin : item.UserName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task SendChatMessageAsync(
        string broadcasterId,
        string senderId,
        string message,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<SendChatMessageResponse>(
            HttpMethod.Post,
            "chat/messages",
            new
            {
                broadcaster_id = broadcasterId,
                sender_id = senderId,
                message
            },
            cancellationToken);

        var result = response.Data.FirstOrDefault();

        if (result is not null &&
            !result.IsSent)
        {
            throw new InvalidOperationException(
                result.DropReason?.Message
                ?? "Twitch hat die Chatnachricht nicht gesendet.");
        }
    }

    public async Task BanUserAsync(
        string broadcasterId,
        string moderatorId,
        string userId,
        int? durationSeconds,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["user_id"] = userId
        };

        if (durationSeconds.HasValue)
        {
            data["duration"] = Math.Clamp(durationSeconds.Value, 1, 1_209_600);
        }

        if (!string.IsNullOrWhiteSpace(reason))
        {
            data["reason"] = reason.Trim()[..Math.Min(500, reason.Trim().Length)];
        }

        using var response = await SendRawAsync(
            HttpMethod.Post,
            "moderation/bans?broadcaster_id=" + Uri.EscapeDataString(broadcasterId) +
            "&moderator_id=" + Uri.EscapeDataString(moderatorId),
            new { data },
            cancellationToken);
    }

    public async Task UnbanUserAsync(
        string broadcasterId,
        string moderatorId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendRawAsync(
            HttpMethod.Delete,
            "moderation/bans?broadcaster_id=" + Uri.EscapeDataString(broadcasterId) +
            "&moderator_id=" + Uri.EscapeDataString(moderatorId) +
            "&user_id=" + Uri.EscapeDataString(userId),
            body: null,
            cancellationToken);
    }


    public async Task<IReadOnlyList<TwitchChannelPointReward>> GetCustomRewardsAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<RewardListResponse>(
            HttpMethod.Get,
            "channel_points/custom_rewards?broadcaster_id=" + Uri.EscapeDataString(broadcasterId),
            body: null,
            cancellationToken);

        return response.Data.Select(ToReward).ToList();
    }

    public async Task<TwitchChannelPointReward> CreateCustomRewardAsync(
        string broadcasterId,
        string title,
        int cost,
        string? prompt,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<RewardListResponse>(
            HttpMethod.Post,
            "channel_points/custom_rewards?broadcaster_id=" + Uri.EscapeDataString(broadcasterId),
            new { title = title.Trim(), cost = Math.Max(1, cost), prompt = prompt?.Trim() ?? "", is_enabled = true },
            cancellationToken);
        return ToReward(response.Data.FirstOrDefault() ?? throw new InvalidOperationException("Twitch hat keine Belohnung zurückgegeben."));
    }

    public async Task<TwitchPoll> CreatePollAsync(
        string broadcasterId,
        string title,
        IReadOnlyList<string> choices,
        int durationSeconds,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<PollListResponse>(HttpMethod.Post, "polls", new
        {
            broadcaster_id = broadcasterId,
            title = title.Trim(),
            choices = choices.Select(value => new { title = value.Trim() }).ToArray(),
            duration = Math.Clamp(durationSeconds, 15, 1800)
        }, cancellationToken);
        var poll = response.Data.FirstOrDefault() ?? throw new InvalidOperationException("Twitch hat keine Umfrage zurückgegeben.");
        return new TwitchPoll(poll.Id, poll.Title, poll.Status, poll.EndedAt);
    }

    public async Task<TwitchPrediction> CreatePredictionAsync(
        string broadcasterId,
        string title,
        IReadOnlyList<string> outcomes,
        int predictionWindowSeconds,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<PredictionListResponse>(HttpMethod.Post, "predictions", new
        {
            broadcaster_id = broadcasterId,
            title = title.Trim(),
            outcomes = outcomes.Select(value => new { title = value.Trim() }).ToArray(),
            prediction_window = Math.Clamp(predictionWindowSeconds, 30, 1800)
        }, cancellationToken);
        var prediction = response.Data.FirstOrDefault() ?? throw new InvalidOperationException("Twitch hat keine Vorhersage zurückgegeben.");
        return ToPrediction(prediction);
    }

    public async Task<TwitchPoll> EndPollAsync(string broadcasterId, string pollId, string status, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<PollListResponse>(HttpMethod.Patch, "polls", new
        {
            broadcaster_id = broadcasterId, id = pollId, status
        }, cancellationToken);
        var poll = response.Data.FirstOrDefault() ?? throw new InvalidOperationException("Twitch hat keine Umfrage zurückgegeben.");
        return new TwitchPoll(poll.Id, poll.Title, poll.Status, poll.EndedAt);
    }

    public async Task<TwitchPrediction> EndPredictionAsync(string broadcasterId, string predictionId, string status, string? winningOutcomeId, CancellationToken cancellationToken = default)
    {
        var body = status.Equals("RESOLVED", StringComparison.OrdinalIgnoreCase)
            ? new { broadcaster_id = broadcasterId, id = predictionId, status, winning_outcome_id = winningOutcomeId }
            : (object)new { broadcaster_id = broadcasterId, id = predictionId, status };
        var response = await SendAsync<PredictionListResponse>(HttpMethod.Patch, "predictions", body, cancellationToken);
        return ToPrediction(response.Data.FirstOrDefault() ?? throw new InvalidOperationException("Twitch hat keine Vorhersage zurückgegeben."));
    }

    public async Task<IReadOnlyList<TwitchRewardRedemption>> GetRewardRedemptionsAsync(string broadcasterId, string rewardId, string status, CancellationToken cancellationToken = default)
    {
        var url = "channel_points/custom_rewards/redemptions?broadcaster_id=" + Uri.EscapeDataString(broadcasterId) +
                  "&reward_id=" + Uri.EscapeDataString(rewardId) + "&status=" + Uri.EscapeDataString(status) + "&sort=OLDEST";
        var response = await SendAsync<RedemptionListResponse>(HttpMethod.Get, url, null, cancellationToken);
        return response.Data.Select(r => new TwitchRewardRedemption(r.Id, rewardId, r.Reward.Title, r.UserLogin, r.UserName, r.UserInput, r.Status, r.RedeemedAt)).ToList();
    }

    public async Task UpdateRewardRedemptionStatusAsync(string broadcasterId, string rewardId, string redemptionId, string status, CancellationToken cancellationToken = default)
    {
        var url = "channel_points/custom_rewards/redemptions?broadcaster_id=" + Uri.EscapeDataString(broadcasterId) +
                  "&reward_id=" + Uri.EscapeDataString(rewardId) + "&id=" + Uri.EscapeDataString(redemptionId);
        _ = await SendAsync<RedemptionListResponse>(HttpMethod.Patch, url, new { status }, cancellationToken);
    }

    private static TwitchPrediction ToPrediction(PredictionData prediction) => new(
        prediction.Id, prediction.Title, prediction.Status, prediction.LocksAt,
        prediction.Outcomes.Select(o => new TwitchPredictionOutcome(o.Id, o.Title, o.ChannelPoints)).ToList());

    public async Task CreateEventSubSubscriptionAsync(
        string type,
        string version,
        object condition,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        _ = await SendRawAsync(
            HttpMethod.Post,
            "eventsub/subscriptions",
            new
            {
                type,
                version,
                condition,
                transport = new
                {
                    method = "websocket",
                    session_id = sessionId
                }
            },
            cancellationToken);
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string relativeUrl,
        object? body,
        CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(
            method,
            relativeUrl,
            body,
            cancellationToken);

        return await response.Content.ReadFromJsonAsync<T>(
                   JsonOptions,
                   cancellationToken)
               ?? throw new InvalidOperationException(
                   "Twitch API-Antwort war leer.");
    }

    private async Task<HttpResponseMessage> SendRawAsync(
        HttpMethod method,
        string relativeUrl,
        object? body,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var request = new HttpRequestMessage(
            method,
            TwitchConstants.HelixBaseUrl + relativeUrl);

        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                _accessToken);

        request.Headers.Add(
            "Client-Id",
            _clientId);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(
                cancellationToken);

            response.Dispose();

            throw new InvalidOperationException(
                $"Twitch API {(int)response.StatusCode}: {text}");
        }

        return response;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_clientId) ||
            string.IsNullOrWhiteSpace(_accessToken))
        {
            throw new InvalidOperationException(
                "Twitch API ist nicht konfiguriert.");
        }
    }

    private static TwitchUser ToUser(UserData user)
    {
        return new TwitchUser(
            user.Id,
            user.Login,
            user.DisplayName,
            user.ProfileImageUrl);
    }


    private static TwitchChannelPointReward ToReward(RewardData reward) => new(
        reward.Id, reward.Title, reward.Cost, reward.Prompt, reward.IsEnabled, reward.BackgroundColor);

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
}
