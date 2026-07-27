using CreatorControlSuite.Modules.OBS.Protocol;

namespace CreatorControlSuite.Tests;

public sealed class ObsAuthenticationTests
{
    [Fact]
    public void AuthenticationResponseIsDeterministic()
    {
        string first = ObsAuthentication.CreateResponse(
            "password",
            "salt",
            "challenge");

        string second = ObsAuthentication.CreateResponse(
            "password",
            "salt",
            "challenge");

        Assert.Equal(first, second);
        Assert.NotEmpty(first);
    }

    [Fact]
    public void DifferentPasswordsProduceDifferentResponses()
    {
        string first = ObsAuthentication.CreateResponse(
            "password-a",
            "salt",
            "challenge");

        string second = ObsAuthentication.CreateResponse(
            "password-b",
            "salt",
            "challenge");

        Assert.NotEqual(first, second);
    }
}
