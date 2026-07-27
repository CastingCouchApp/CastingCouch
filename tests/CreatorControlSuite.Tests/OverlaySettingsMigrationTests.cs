using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Tests;

public sealed class OverlaySettingsMigrationTests
{
    [Fact]
    public void EnsureInstancesMigrated_SeedsDefaultFromLegacyRootPath()
    {
        var settings = new OverlaySettings
        {
            RootPath = @"D:\Overlays\Main",
            Instances = []
        };

        settings.EnsureInstancesMigrated();

        Assert.Single(settings.Instances);
        OverlayInstanceSettings instance = settings.Instances[0];
        Assert.Equal("Default", instance.Name);
        Assert.Equal(@"D:\Overlays\Main", instance.RootPath);
        Assert.True(instance.Enabled);
        Assert.False(string.IsNullOrWhiteSpace(instance.Id));
    }

    [Fact]
    public void EnsureInstancesMigrated_DoesNotDuplicateWhenInstancesExist()
    {
        var settings = new OverlaySettings
        {
            RootPath = @"D:\Overlays\Legacy",
            Instances =
            [
                new OverlayInstanceSettings
                {
                    Id = "abc",
                    Name = "Alerts",
                    RootPath = @"D:\Overlays\Alerts"
                }
            ]
        };

        settings.EnsureInstancesMigrated();

        Assert.Single(settings.Instances);
        Assert.Equal("Alerts", settings.Instances[0].Name);
    }

    [Fact]
    public void EnsureInstancesMigrated_NoOpWhenNoRootAndNoInstances()
    {
        var settings = new OverlaySettings();
        settings.EnsureInstancesMigrated();
        Assert.Empty(settings.Instances);
    }

    [Fact]
    public void GetInstanceUrl_BuildsMountPath()
    {
        var settings = new OverlaySettings { WebServerPort = 8765 };
        Assert.Equal(
            "http://127.0.0.1:8765/o/abc123/",
            settings.GetInstanceUrl("abc123"));
    }
}
