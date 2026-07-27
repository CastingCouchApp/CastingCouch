using CreatorControlSuite.Core.Diagnostics;
namespace CreatorControlSuite.Tests;

public sealed class BetaReadinessModelsTests
{
    [Fact]
    public void BlockingDashboardIsNotReady()
    {
        var d = new BetaReadinessDashboard(DateTimeOffset.Now, 70, false, [new BetaReadinessArea("Release", 3, 1, 1, 70, "Test")], ["Blocker"]);
        Assert.False(d.BetaReady); Assert.Single(d.Blockers);
    }
}
