using CreatorControlSuite.Modules.Spotify.Models;

namespace CreatorControlSuite.Modules.Spotify;

public interface ISpotifyOAuthClient
{
    Task<SpotifyTokenSet> AuthorizeAsync(
        string clientId,
        string redirectUri,
        IReadOnlyCollection<string> scopes,
        CancellationToken cancellationToken = default);

    Task<SpotifyTokenSet> RefreshAsync(
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken = default);
}
