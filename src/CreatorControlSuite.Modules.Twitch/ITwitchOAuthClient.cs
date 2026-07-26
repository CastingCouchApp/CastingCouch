using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.Modules.Twitch;

public interface ITwitchOAuthClient
{
    Task<TwitchDeviceCode> StartDeviceAuthorizationAsync(
        string clientId,
        IReadOnlyCollection<string> scopes,
        CancellationToken cancellationToken = default);

    Task<TwitchTokenSet> WaitForDeviceAuthorizationAsync(
        string clientId,
        TwitchDeviceCode deviceCode,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    Task<TwitchTokenSet> RefreshAsync(
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken = default);

    Task<TwitchTokenValidation> ValidateAsync(
        string accessToken,
        CancellationToken cancellationToken = default);
}
