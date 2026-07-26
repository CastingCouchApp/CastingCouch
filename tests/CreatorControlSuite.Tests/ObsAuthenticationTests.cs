using CreatorControlSuite.Modules.OBS.Protocol;

namespace CreatorControlSuite.Tests;

public sealed class ObsAuthenticationTests
{
    [Fact]
    public void AuthenticationResponseIsDeterministic()
    {
        var first = ObsAuthentication.CreateResponse(
            "password",
            "salt",
            "challenge");

        var second = ObsAuthentication.CreateResponse(
            "password",
            "salt",
            "challenge");

        Assert.Equal(first, second);
        Assert.NotEmpty(first);
    }

    [Fact]
    public void DifferentPasswordsProduceDifferentResponses()
    {
        var first = ObsAuthentication.CreateResponse(
            "password-a",
            "salt",
            "challenge");

        var second = ObsAuthentication.CreateResponse(
            "password-b",
            "salt",
            "challenge");

        Assert.NotEqual(first, second);
    }
}
