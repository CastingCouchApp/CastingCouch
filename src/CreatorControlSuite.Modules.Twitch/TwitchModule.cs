using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Modules;
using CreatorControlSuite.Core.Twitch;
using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.Modules.Twitch;

public sealed class TwitchModule(
    ISettingsStore settingsStore,
    ITwitchOAuthClient oauthClient,
    ITwitchApiClient apiClient,
    ITwitchEventSubClient eventSubClient,
    TwitchTokenRepository tokenRepository) : IConnectableModule
{
    private readonly ISettingsStore _settingsStore = settingsStore;
    private readonly ITwitchOAuthClient _oauthClient = oauthClient;
    private readonly ITwitchApiClient _apiClient = apiClient;
    private readonly ITwitchEventSubClient _eventSubClient = eventSubClient;
    private readonly TwitchTokenRepository _tokenRepository = tokenRepository;

    private TwitchTokenValidation? _validation;
    private TwitchUser? _currentUser;
    private TwitchChannelInformation? _channel;

    public string Id => "twitch";
    public string DisplayName => "Twitch";

    public event EventHandler<TwitchChatMessage>? ChatMessageReceived
    {
        add => _eventSubClient.ChatMessageReceived += value;
        remove => _eventSubClient.ChatMessageReceived -= value;
    }

    public event EventHandler<TwitchEvent>? EventReceived
    {
        add => _eventSubClient.EventReceived += value;
        remove => _eventSubClient.EventReceived -= value;
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task<TwitchDeviceCode> StartAuthorizationAsync(
        CancellationToken cancellationToken = default)
    {
        AppSettings settings = await _settingsStore.LoadAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(settings.Twitch.ClientId))
        {
            throw new InvalidOperationException(
                "Bitte zuerst die Twitch Client-ID eintragen.");
        }

        return await _oauthClient.StartDeviceAuthorizationAsync(
            settings.Twitch.ClientId,
            settings.Twitch.Scopes,
            cancellationToken);
    }

    public async Task CompleteAuthorizationAsync(
        TwitchDeviceCode deviceCode,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        AppSettings settings = await _settingsStore.LoadAsync(cancellationToken);

        TwitchTokenSet tokenSet =
            await _oauthClient.WaitForDeviceAuthorizationAsync(
                settings.Twitch.ClientId,
                deviceCode,
                progress,
                cancellationToken);

        await _tokenRepository.SaveAsync(
            tokenSet,
            cancellationToken);

        await ConnectAsync(cancellationToken);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        AppSettings settings = await _settingsStore.LoadAsync(cancellationToken);
        TwitchTokenSet tokenSet = await GetValidTokenAsync(
            settings.Twitch.ClientId,
            cancellationToken);

        _validation = await _oauthClient.ValidateAsync(
            tokenSet.AccessToken,
            cancellationToken);

        string[] missingScopes = settings.Twitch.Scopes
            .Except(_validation.Scopes, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingScopes.Length > 0)
        {
            throw new InvalidOperationException(
                "Der gespeicherte Twitch-Token hat nicht alle benötigten Berechtigungen. " +
                "Bitte Twitch erneut autorisieren. Fehlend: " +
                string.Join(", ", missingScopes));
        }

        _apiClient.Configure(
            settings.Twitch.ClientId,
            tokenSet.AccessToken);

        _currentUser = await _apiClient.GetCurrentUserAsync(
            cancellationToken);

        TwitchUser broadcaster = string.IsNullOrWhiteSpace(
            settings.Twitch.ChannelName)
            ? _currentUser
            : await _apiClient.GetUserByLoginAsync(
                settings.Twitch.ChannelName,
                cancellationToken)
              ?? throw new InvalidOperationException(
                  "Der konfigurierte Twitch-Kanal wurde nicht gefunden.");

        _channel = await _apiClient.GetChannelInformationAsync(
            broadcaster.Id,
            cancellationToken);

        await _eventSubClient.ConnectAsync(
            _apiClient,
            broadcaster.Id,
            _currentUser.Id,
            settings.Twitch.EnableChat,
            settings.Twitch.EnableEventSub,
            cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _eventSubClient.DisconnectAsync(cancellationToken);

        _validation = null;
        _currentUser = null;
        _channel = null;
    }

    public Task<TwitchRaidTargetStatus?> GetRaidTargetStatusAsync(
        string login,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser is null || _channel is null)
        {
            throw new InvalidOperationException("Twitch ist nicht verbunden.");
        }

        return _apiClient.GetRaidTargetStatusAsync(login, cancellationToken);
    }

    public async Task StartRaidAsync(
        string targetLogin,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser is null || _channel is null)
        {
            throw new InvalidOperationException("Twitch ist nicht verbunden.");
        }

        string login = RaidChatCommand.NormalizeLogin(targetLogin);
        TwitchUser target = await _apiClient.GetUserByLoginAsync(
            login,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Das ausgewählte Raid-Ziel wurde auf Twitch nicht gefunden.");

        if (string.Equals(
                target.Id,
                _channel.BroadcasterId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Der eigene Kanal kann nicht als Raid-Ziel verwendet werden.");
        }

        // Helix StartRaid is the supported equivalent of chat "/raid".
        await _apiClient.StartRaidAsync(
            _channel.BroadcasterId,
            target.Id,
            cancellationToken);

        // Best-effort: also emit "/raid <login>" into chat so the command is visible
        // (Helix may strip slash-commands; raid already started above).
        try
        {
            await _apiClient.SendChatMessageAsync(
                _channel.BroadcasterId,
                _currentUser.Id,
                RaidChatCommand.Format(target.Login),
                cancellationToken);
        }
        catch
        {
            // Raid already started via Helix – chat echo is optional.
        }
    }

    public Task CancelRaidAsync(CancellationToken cancellationToken = default)
    {
        if (_currentUser is null || _channel is null)
        {
            throw new InvalidOperationException("Twitch ist nicht verbunden.");
        }

        return _apiClient.CancelRaidAsync(_channel.BroadcasterId, cancellationToken);
    }

    public Task<int> GetFollowerCountAsync(
        CancellationToken cancellationToken = default)
    {
        if (_currentUser is null || _channel is null)
        {
            throw new InvalidOperationException(
                "Twitch ist nicht verbunden.");
        }

        return _apiClient.GetFollowerCountAsync(
            _channel.BroadcasterId,
            cancellationToken);
    }

    public Task<int> GetActiveSubscriptionCountAsync(
        CancellationToken cancellationToken = default)
    {
        if (_channel is null)
        {
            throw new InvalidOperationException(
                "Twitch ist nicht verbunden.");
        }

        return _apiClient.GetActiveSubscriptionCountAsync(
            _channel.BroadcasterId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetChattersAsync(
        CancellationToken cancellationToken = default)
    {
        if (_currentUser is null || _channel is null)
        {
            return [];
        }

        return await _apiClient.GetChattersAsync(
            _channel.BroadcasterId,
            _currentUser.Id,
            cancellationToken);
    }

    public async Task SendChatMessageAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser is null ||
            _channel is null)
        {
            throw new InvalidOperationException(
                "Twitch ist nicht verbunden.");
        }

        await _apiClient.SendChatMessageAsync(
            _channel.BroadcasterId,
            _currentUser.Id,
            message,
            cancellationToken);
    }

    public async Task ModerateUserAsync(
        string userLogin,
        int? durationSeconds,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser is null || _channel is null)
        {
            throw new InvalidOperationException("Twitch ist nicht verbunden.");
        }

        TwitchUser target = await _apiClient.GetUserByLoginAsync(
            userLogin.Trim().TrimStart('@'),
            cancellationToken)
            ?? throw new InvalidOperationException("Der Twitch-Benutzer wurde nicht gefunden.");

        if (string.Equals(target.Id, _channel.BroadcasterId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Der eigene Kanal kann nicht moderiert werden.");
        }

        await _apiClient.BanUserAsync(
            _channel.BroadcasterId,
            _currentUser.Id,
            target.Id,
            durationSeconds,
            reason,
            cancellationToken);
    }

    public async Task UnbanUserAsync(
        string userLogin,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser is null || _channel is null)
        {
            throw new InvalidOperationException("Twitch ist nicht verbunden.");
        }

        TwitchUser target = await _apiClient.GetUserByLoginAsync(
            userLogin.Trim().TrimStart('@'),
            cancellationToken)
            ?? throw new InvalidOperationException("Der Twitch-Benutzer wurde nicht gefunden.");

        await _apiClient.UnbanUserAsync(
            _channel.BroadcasterId,
            _currentUser.Id,
            target.Id,
            cancellationToken);
    }


    public Task<IReadOnlyList<TwitchChannelPointReward>> GetCustomRewardsAsync(CancellationToken cancellationToken = default)
    {
        if (_channel is null)
        {
            throw new InvalidOperationException("Twitch ist nicht verbunden.");
        }

        return _apiClient.GetCustomRewardsAsync(_channel.BroadcasterId, cancellationToken);
    }

    public Task<TwitchChannelPointReward> CreateCustomRewardAsync(string title, int cost, string? prompt, CancellationToken cancellationToken = default)
    {
        if (_channel is null)
        {
            throw new InvalidOperationException("Twitch ist nicht verbunden.");
        }

        return _apiClient.CreateCustomRewardAsync(_channel.BroadcasterId, title, cost, prompt, cancellationToken);
    }

    public Task<TwitchPoll> CreatePollAsync(string title, IReadOnlyList<string> choices, int durationSeconds, CancellationToken cancellationToken = default)
    {
        if (_channel is null)
        {
            throw new InvalidOperationException("Twitch ist nicht verbunden.");
        }

        return _apiClient.CreatePollAsync(_channel.BroadcasterId, title, choices, durationSeconds, cancellationToken);
    }

    public Task<TwitchPrediction> CreatePredictionAsync(string title, IReadOnlyList<string> outcomes, int windowSeconds, CancellationToken cancellationToken = default)
    {
        if (_channel is null)
        {
            throw new InvalidOperationException("Twitch ist nicht verbunden.");
        }

        return _apiClient.CreatePredictionAsync(_channel.BroadcasterId, title, outcomes, windowSeconds, cancellationToken);
    }

    public Task<TwitchPoll> EndPollAsync(string pollId, string status, CancellationToken cancellationToken = default)
    {
        if (_channel is null)
        {
            throw new InvalidOperationException("Twitch ist nicht verbunden.");
        }

        return _apiClient.EndPollAsync(_channel.BroadcasterId, pollId, status, cancellationToken);
    }

    public Task<TwitchPrediction> EndPredictionAsync(string predictionId, string status, string? winningOutcomeId, CancellationToken cancellationToken = default)
    {
        if (_channel is null)
        {
            throw new InvalidOperationException("Twitch ist nicht verbunden.");
        }

        return _apiClient.EndPredictionAsync(_channel.BroadcasterId, predictionId, status, winningOutcomeId, cancellationToken);
    }

    public Task<IReadOnlyList<TwitchRewardRedemption>> GetRewardRedemptionsAsync(string rewardId, string status = "UNFULFILLED", CancellationToken cancellationToken = default)
    {
        if (_channel is null)
        {
            throw new InvalidOperationException("Twitch ist nicht verbunden.");
        }

        return _apiClient.GetRewardRedemptionsAsync(_channel.BroadcasterId, rewardId, status, cancellationToken);
    }

    public Task UpdateRewardRedemptionStatusAsync(string rewardId, string redemptionId, string status, CancellationToken cancellationToken = default)
    {
        if (_channel is null)
        {
            throw new InvalidOperationException("Twitch ist nicht verbunden.");
        }

        return _apiClient.UpdateRewardRedemptionStatusAsync(_channel.BroadcasterId, rewardId, redemptionId, status, cancellationToken);
    }

    public async Task UpdateChannelAsync(
        string? title,
        string? categoryId,
        CancellationToken cancellationToken = default)
    {
        if (_channel is null)
        {
            throw new InvalidOperationException(
                "Twitch ist nicht verbunden.");
        }

        await _apiClient.UpdateChannelInformationAsync(
            _channel.BroadcasterId,
            title,
            categoryId,
            cancellationToken);

        _channel = await _apiClient.GetChannelInformationAsync(
            _channel.BroadcasterId,
            cancellationToken);
    }

    public Task<IReadOnlyList<TwitchCategory>> SearchCategoriesAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        return _apiClient.SearchCategoriesAsync(
            query,
            cancellationToken);
    }

    public Task<IReadOnlyList<TwitchChannelSuggestion>> SearchChannelsAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser is null)
        {
            throw new InvalidOperationException("Twitch ist nicht verbunden.");
        }

        return _apiClient.SearchChannelsAsync(query, cancellationToken);
    }

    public Task<IReadOnlyList<TwitchChannelSuggestion>> GetFollowedChannelsAsync(
        CancellationToken cancellationToken = default)
    {
        if (_currentUser is null)
        {
            throw new InvalidOperationException("Twitch ist nicht verbunden.");
        }

        return _apiClient.GetFollowedChannelsAsync(_currentUser.Id, cancellationToken);
    }

    public Task<IReadOnlyList<TwitchChannelSuggestion>> GetFollowedLiveStreamsAsync(
        CancellationToken cancellationToken = default)
    {
        if (_currentUser is null)
        {
            throw new InvalidOperationException("Twitch ist nicht verbunden.");
        }

        return _apiClient.GetFollowedLiveStreamsAsync(_currentUser.Id, cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, TwitchChannelSuggestion>> GetLiveChannelsByLoginsAsync(
        IEnumerable<string> logins,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser is null)
        {
            throw new InvalidOperationException("Twitch ist nicht verbunden.");
        }

        return _apiClient.GetLiveChannelsByLoginsAsync(logins, cancellationToken);
    }

    public TwitchConnectionSnapshot GetSnapshot()
    {
        return new TwitchConnectionSnapshot(
            Authenticated: _validation is not null,
            EventSubConnected: _eventSubClient.IsConnected,
            Login: _validation?.Login ?? "",
            UserId: _validation?.UserId ?? "",
            ChannelLogin: _channel?.BroadcasterLogin ?? "",
            ChannelName: _channel?.BroadcasterName ?? "",
            ChannelTitle: _channel?.Title ?? "",
            CategoryName: _channel?.GameName ?? "",
            Scopes: _validation?.Scopes ?? []);
    }

    public Task<ModuleStatus> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        TwitchConnectionSnapshot snapshot = GetSnapshot();

        return Task.FromResult(
            new ModuleStatus(
                Id,
                DisplayName,
                snapshot.Authenticated
                    ? ModuleHealth.Connected
                    : ModuleHealth.Ready,
                snapshot.Authenticated
                    ? $"{snapshot.Login} · {snapshot.ChannelName} · " +
                      $"{snapshot.CategoryName}"
                    : "Nicht verbunden",
                DateTimeOffset.Now));
    }

    private async Task<TwitchTokenSet> GetValidTokenAsync(
        string clientId,
        CancellationToken cancellationToken)
    {
        TwitchTokenSet tokenSet =
            await _tokenRepository.LoadAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "Twitch wurde noch nicht autorisiert.");

        try
        {
            TwitchTokenValidation validation = await _oauthClient.ValidateAsync(
                tokenSet.AccessToken,
                cancellationToken);

            if (validation.ExpiresInSeconds > 300)
            {
                return tokenSet;
            }
        }
        catch
        {
        }

        if (string.IsNullOrWhiteSpace(tokenSet.RefreshToken))
        {
            throw new InvalidOperationException(
                "Der Twitch-Token ist abgelaufen. Bitte Twitch neu autorisieren.");
        }

        TwitchTokenSet refreshed = await _oauthClient.RefreshAsync(
            clientId,
            tokenSet.RefreshToken,
            cancellationToken);

        await _tokenRepository.SaveAsync(
            refreshed,
            cancellationToken);

        return refreshed;
    }
}
