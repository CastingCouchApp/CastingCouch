using System.Reflection;
using System.Text.Json;

namespace CreatorControlSuite.Tests;

public sealed class SpotifyScopeJsonTests
{
    [Fact]
    public void ScopeStringCanBeSplitIntoMultipleScopes()
    {
        const string json =
            """
            {
              "access_token": "token",
              "refresh_token": "refresh",
              "expires_in": 3600,
              "token_type": "Bearer",
              "scope": "user-read-email user-read-playback-state"
            }
            """;

        var oauthType =
            Type.GetType(
                "CreatorControlSuite.Modules.Spotify.SpotifyOAuthClient, CreatorControlSuite.Modules.Spotify");

        Assert.NotNull(oauthType);

        Type? tokenType =
            oauthType!
                .GetNestedType(
                    "TokenResponse",
                    BindingFlags.NonPublic);

        Assert.NotNull(tokenType);

        object? token =
            JsonSerializer.Deserialize(
                json,
                tokenType!,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

        Assert.NotNull(token);

        string[]? scope =
            (string[]?)tokenType!
                .GetProperty("Scope")!
                .GetValue(token);

        Assert.Equal(
            new[]
            {
                "user-read-email",
                "user-read-playback-state"
            },
            scope);
    }
}
