using CreatorControlSuite.Core.Licensing;
namespace CreatorControlSuite.Tests;
public sealed class FeatureCatalogTests
{
    [Fact] public void ProIncludesCommercialUse() => Assert.Contains(FeatureCatalog.CommercialUse, FeatureCatalog.ResolveEdition("Pro"));
    [Fact] public void CoreDoesNotIncludeSpotify() => Assert.DoesNotContain(FeatureCatalog.Spotify, FeatureCatalog.ResolveEdition("Core"));
}
