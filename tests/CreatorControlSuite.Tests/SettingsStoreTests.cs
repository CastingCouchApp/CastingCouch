using System.Text.Json;
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

    [Fact]
    public async Task Save_PreservesTopLevelJsonContractAfterDomainSplit()
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

            await store.SaveAsync(new AppSettings());

            using JsonDocument document =
                JsonDocument.Parse(await File.ReadAllTextAsync(path));
            string[] properties =
            [
                .. document.RootElement.EnumerateObject()
                    .Select(property => property.Name)
            ];
            string[] expected =
            [
                "SchemaVersion",
                "AdditionalScenes",
                "Product",
                "General",
                "Branding",
                "Obs",
                "Twitch",
                "Spotify",
                "MusicPlayer",
                "YouTubeMusic",
                "StreamerBot",
                "Alerts",
                "Overlay",
                "Workflow",
                "StreamDeck",
                "Dashboard",
                "Updates"
            ];

            Assert.Equal(
                expected.OrderBy(value => value),
                properties.OrderBy(value => value));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Load_MigratesLegacySettingsSequentially_AndPersistsSchema()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "CreatorControlSuite.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(
                path,
                """{"Product":{"UpdateChannel":"Beta"},"MusicPlayer":{"ProviderId":"YTMUSIC"}}""");
            var store = new JsonSettingsStore(path);

            AppSettings loaded = await store.LoadAsync();

            Assert.Equal(AppSettings.CurrentSchemaVersion, loaded.SchemaVersion);
            Assert.Equal("Beta", loaded.Updates.Channel);
            Assert.Equal("ytmusic", loaded.MusicPlayer.ProviderId);
            string persisted = await File.ReadAllTextAsync(path);
            Assert.Contains(
                $"\"SchemaVersion\": {AppSettings.CurrentSchemaVersion}",
                persisted,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Load_RejectsUnknownFutureSchema()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "CreatorControlSuite.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(path, """{"SchemaVersion":999}""");
            var store = new JsonSettingsStore(path);

            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
