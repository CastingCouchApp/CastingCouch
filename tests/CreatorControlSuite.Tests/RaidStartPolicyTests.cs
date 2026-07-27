using System.Net;
using CreatorControlSuite.Core.Twitch;
using CreatorControlSuite.Modules.Twitch;

namespace CreatorControlSuite.Tests;

public sealed class RaidChatCommandTests
{
    [Theory]
    [InlineData("CoolStreamer", "/raid CoolStreamer")]
    [InlineData("@CoolStreamer", "/raid CoolStreamer")]
    [InlineData("  CoolStreamer  ", "/raid CoolStreamer")]
    public void Format_BuildsSlashRaidCommand(string login, string expected)
    {
        Assert.Equal(expected, RaidChatCommand.Format(login));
    }

    [Fact]
    public void Format_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => RaidChatCommand.Format("  "));
    }
}

public sealed class RaidCountdownPolicyTests
{
    [Fact]
    public void DecideAfterCancellation_Skip_IsSuccessful()
    {
        RaidCountdownOutcome outcome = RaidCountdownPolicy.DecideAfterCancellation(skipRequested: true);
        Assert.Equal(RaidCountdownOutcome.Skipped, outcome);
        Assert.True(RaidCountdownPolicy.IsSuccessful(outcome));
    }

    [Fact]
    public void DecideAfterCancellation_Cancel_IsNotSuccessful()
    {
        RaidCountdownOutcome outcome = RaidCountdownPolicy.DecideAfterCancellation(skipRequested: false);
        Assert.Equal(RaidCountdownOutcome.Cancelled, outcome);
        Assert.False(RaidCountdownPolicy.IsSuccessful(outcome));
    }

    [Fact]
    public void Completed_IsSuccessful()
    {
        Assert.True(RaidCountdownPolicy.IsSuccessful(RaidCountdownOutcome.Completed));
    }
}

public sealed class RaidStartPolicyTests
{
    [Theory]
    [InlineData(0, 120)]
    [InlineData(-5, 120)]
    [InlineData(14, 15)]
    [InlineData(15, 15)]
    [InlineData(120, 120)]
    [InlineData(600, 600)]
    [InlineData(9999, 600)]
    public void ClampTimeoutSeconds_ClampsToRange(int input, int expected)
    {
        Assert.Equal(expected, RaidStartPolicy.ClampTimeoutSeconds(input));
    }

    [Fact]
    public void DecideAfterStatus_Offline_KeepsPolling()
    {
        Assert.Equal(
            RaidStartDecision.KeepPolling,
            RaidStartPolicy.DecideAfterStatus(targetFound: true, isOnline: false));
    }

    [Fact]
    public void DecideAfterStatus_NotFound_KeepsPolling()
    {
        Assert.Equal(
            RaidStartDecision.KeepPolling,
            RaidStartPolicy.DecideAfterStatus(targetFound: false, isOnline: false));
    }

    [Fact]
    public void DecideAfterStatus_Online_AttemptsStart()
    {
        Assert.Equal(
            RaidStartDecision.AttemptStart,
            RaidStartPolicy.DecideAfterStatus(targetFound: true, isOnline: true));
    }

    [Fact]
    public void DecideAfterStartError_OwnChannel_GivesUp()
    {
        var ex = new InvalidOperationException(
            "Der eigene Kanal kann nicht als Raid-Ziel verwendet werden.");
        Assert.Equal(RaidStartDecision.GiveUp, RaidStartPolicy.DecideAfterStartError(ex));
        Assert.True(RaidStartPolicy.IsPermanentRaidError(ex));
    }

    [Fact]
    public void DecideAfterStartError_RateLimit_Retries()
    {
        var ex = new InvalidOperationException("Twitch Rate-Limit erreicht. Raid wird erneut versucht.");
        Assert.Equal(RaidStartDecision.RetryTransient, RaidStartPolicy.DecideAfterStartError(ex));
        Assert.True(RaidStartPolicy.IsTransientRaidError(ex));
    }

    [Fact]
    public void DecideAfterStartError_NotConnected_GivesUp()
    {
        var ex = new InvalidOperationException("Twitch ist nicht verbunden.");
        Assert.Equal(RaidStartDecision.GiveUp, RaidStartPolicy.DecideAfterStartError(ex));
    }

    [Fact]
    public void GetRetryDelay_IncreasesThenCaps()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), RaidStartPolicy.GetRetryDelay(0));
        Assert.Equal(TimeSpan.FromSeconds(5), RaidStartPolicy.GetRetryDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(8), RaidStartPolicy.GetRetryDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(12), RaidStartPolicy.GetRetryDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(15), RaidStartPolicy.GetRetryDelay(10));
    }
}

public sealed class TwitchRaidErrorMapperTests
{
    [Fact]
    public void FormatStartRaidError_Maps409WithMessage()
    {
        string message = TwitchRaidErrorMapper.FormatStartRaidError(
            HttpStatusCode.Conflict,
            """{"error":"Conflict","status":409,"message":"The channel is offline"}""");

        Assert.Contains("Raid derzeit nicht möglich", message);
        Assert.Contains("offline", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatStartRaidError_Maps429()
    {
        string message = TwitchRaidErrorMapper.FormatStartRaidError(
            (HttpStatusCode)429,
            "{}");

        Assert.Contains("Rate-Limit", message);
    }

    [Fact]
    public void FormatStartRaidError_Maps503()
    {
        string message = TwitchRaidErrorMapper.FormatStartRaidError(
            HttpStatusCode.ServiceUnavailable,
            "");

        Assert.Contains("vorübergehend", message);
    }

    [Fact]
    public void FormatCancelRaidError_Maps404()
    {
        string message = TwitchRaidErrorMapper.FormatCancelRaidError(
            HttpStatusCode.NotFound,
            "");

        Assert.Contains("Kein aktiver Raid", message);
    }
}
