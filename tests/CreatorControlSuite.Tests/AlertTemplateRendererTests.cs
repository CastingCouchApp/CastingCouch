using CreatorControlSuite.Modules.Alerts;

namespace CreatorControlSuite.Tests;

public sealed class AlertTemplateRendererTests
{
    [Fact]
    public void ReplacesUserAndVariables()
    {
        var result = AlertTemplateRenderer.Render(
            "{user} raidet mit {viewers} Zuschauern!",
            "TestRaid",
            new Dictionary<string, string>
            {
                ["viewers"] = "25"
            });

        Assert.Equal(
            "TestRaid raidet mit 25 Zuschauern!",
            result);
    }

    [Fact]
    public void LeavesUnknownVariablesUntouched()
    {
        var result = AlertTemplateRenderer.Render(
            "{user} {unknown}",
            "TestUser",
            new Dictionary<string, string>());

        Assert.Equal(
            "TestUser {unknown}",
            result);
    }
}
