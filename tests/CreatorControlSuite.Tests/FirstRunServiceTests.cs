using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Setup;

namespace CreatorControlSuite.Tests;

public sealed class FirstRunServiceTests
{
    [Fact]
    public async Task IsRequiredUntilCompleted()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "CreatorControlSuite.FirstRunTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        try
        {
            var settingsStore = new JsonSettingsStore(
                Path.Combine(root, "settings.json"));

            var service = new FirstRunService(
                Path.Combine(root, "first-run.json"),
                settingsStore);

            Assert.True(await service.IsRequiredAsync());

            await service.SaveStateAsync(
                new FirstRunState
                {
                    Completed = true,
                    CompletedVersion = 1,
                    CompletedAt = DateTimeOffset.Now
                });

            Assert.False(await service.IsRequiredAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
