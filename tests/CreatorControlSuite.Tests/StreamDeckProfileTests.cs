using CreatorControlSuite.Modules.StreamDeck;

namespace CreatorControlSuite.Tests;

public sealed class StreamDeckProfileTests
{
    [Fact]
    public async Task BuildsProfilePackage()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "CreatorControlSuite.StreamDeckTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        try
        {
            var service =
                new StreamDeckProfileService(root);

            var package =
                await service.BuildDefaultProfileAsync();

            Assert.True(File.Exists(package.Path));
            Assert.Contains(
                package.Actions,
                action => action.Command == "workflow.live");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
