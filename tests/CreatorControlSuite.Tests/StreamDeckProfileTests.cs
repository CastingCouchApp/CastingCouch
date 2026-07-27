using CreatorControlSuite.Modules.StreamDeck;
using CreatorControlSuite.Modules.StreamDeck.Models;

namespace CreatorControlSuite.Tests;

public sealed class StreamDeckProfileTests
{
    [Fact]
    public async Task BuildsProfilePackage()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "CreatorControlSuite.StreamDeckTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        try
        {
            var service =
                new StreamDeckProfileService(root);

            StreamDeckProfilePackage package =
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
