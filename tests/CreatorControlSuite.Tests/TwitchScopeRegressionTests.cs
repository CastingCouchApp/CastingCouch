using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.Tests;

public sealed class TwitchScopeRegressionTests
{
    [Fact]
    public void DefaultScopes_RequireGuestStarReadPermission()
    {
        var settings = new TwitchSettings();

        Assert.Contains(
            "channel:read:guest_star",
            settings.Scopes,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExistingTokenWithoutGuestStarScope_IsDetectable()
    {
        var settings = new TwitchSettings();
        var validation = new TwitchTokenValidation(
            "client",
            "creator",
            "42",
            settings.Scopes
                .Where(scope => !string.Equals(
                    scope,
                    "channel:read:guest_star",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray(),
            3600);

        string[] missingScopes = settings.Scopes
            .Except(validation.Scopes, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(["channel:read:guest_star"], missingScopes);
    }
}
