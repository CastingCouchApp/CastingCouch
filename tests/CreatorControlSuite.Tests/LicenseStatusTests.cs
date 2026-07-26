using CreatorControlSuite.Core.Licensing;
namespace CreatorControlSuite.Tests;
public sealed class LicenseStatusTests
{
    [Fact] public void DevelopmentLicenseIsUsable()=>Assert.True(new LicenseStatus(LicenseState.Development,"Dev",null,new[]{"*"}).IsUsable);
    [Fact] public void ExpiredLicenseIsNotUsable()=>Assert.False(new LicenseStatus(LicenseState.Expired,"Expired",null,Array.Empty<string>()).IsUsable);
}
