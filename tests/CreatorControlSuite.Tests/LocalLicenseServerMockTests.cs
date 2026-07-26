using CreatorControlSuite.Core.Licensing;
namespace CreatorControlSuite.Tests;
public sealed class LocalLicenseServerMockTests
{
    [Fact] public async Task ProTestKeyCanBeActivatedAndChecked()
    {
        var server=new LocalLicenseServerMock();
        var activation=await server.ActivateAsync(new("creator-control-suite","PRO-TEST-001","install-1","2.0.81"));
        Assert.True(activation.Success); Assert.NotNull(activation.ActivationId);
        var status=await server.CheckStatusAsync(activation.ActivationId!,"install-1");
        Assert.True(status.Success); Assert.False(status.Revoked);
    }
}
