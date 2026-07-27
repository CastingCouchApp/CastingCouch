using CreatorControlSuite.Core.Legal;
namespace CreatorControlSuite.Tests;

public sealed class LegalConsentServiceTests
{
    [Fact] public async Task ConsentIsRequiredUntilAccepted() { string root = Path.Combine(Path.GetTempPath(), "CreatorControlSuite.LegalTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); try { var s = new LegalConsentService(Path.Combine(root, "consent.json"), root); Assert.True(await s.IsConsentRequiredAsync()); await s.SaveAcceptedAsync(); Assert.False(await s.IsConsentRequiredAsync()); } finally { Directory.Delete(root, true); } }
}
