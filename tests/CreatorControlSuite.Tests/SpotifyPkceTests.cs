using CreatorControlSuite.Modules.Spotify;

namespace CreatorControlSuite.Tests;

public sealed class SpotifyPkceTests
{
    [Fact]
    public void CodeChallengeIsDeterministic()
    {
        const string verifier =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~";

        var first =
            SpotifyOAuthClient.CreateCodeChallenge(verifier);

        var second =
            SpotifyOAuthClient.CreateCodeChallenge(verifier);

        Assert.Equal(first, second);
        Assert.NotEmpty(first);
        Assert.DoesNotContain("+", first);
        Assert.DoesNotContain("/", first);
        Assert.DoesNotContain("=", first);
    }
}
