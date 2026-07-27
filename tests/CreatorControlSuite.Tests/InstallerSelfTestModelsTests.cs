using CreatorControlSuite.Core.Diagnostics;
namespace CreatorControlSuite.Tests;

public sealed class InstallerSelfTestModelsTests
{
    [Fact]
    public void FailedItemBlocksReport()
    {
        InstallerSelfTestItem[] i = [new InstallerSelfTestItem("Updater", InstallerSelfTestStatus.Failed, "Fehlt", "Build")];
        var r = new InstallerSelfTestReport(DateTimeOffset.Now, DateTimeOffset.Now, !i.Any(x => x.Status == InstallerSelfTestStatus.Failed), i);
        Assert.False(r.Passed);
    }
}
