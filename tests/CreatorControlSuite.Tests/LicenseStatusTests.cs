using CreatorControlSuite.Core.Licensing;
namespace CreatorControlSuite.Tests;

public sealed class LicenseStatusTests
{
    [Fact] public void DevelopmentLicenseIsUsable() => Assert.True(new LicenseStatus(LicenseState.Development, "Dev", null, ["*"]).IsUsable);
    [Fact] public void ExpiredLicenseIsNotUsable() => Assert.False(new LicenseStatus(LicenseState.Expired, "Expired", null, []).IsUsable);
}
