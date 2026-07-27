using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public async Task SettingsCanBeSavedAndLoaded()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "CreatorControlSuite.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        try
        {
            string path = Path.Combine(directory, "settings.json");
            var store = new JsonSettingsStore(path);

            var settings = new AppSettings();
            settings.Branding.DisplayName = "Denver John";
            settings.Workflow.EndSceneSeconds = 60;

            await store.SaveAsync(settings);

            AppSettings loaded = await store.LoadAsync();

            Assert.Equal("Denver John", loaded.Branding.DisplayName);
            Assert.Equal(60, loaded.Workflow.EndSceneSeconds);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
