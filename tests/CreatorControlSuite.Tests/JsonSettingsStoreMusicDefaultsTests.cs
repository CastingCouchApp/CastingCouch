using System.Text;
using System.Text.Json;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Music;

namespace CreatorControlSuite.Tests;

public sealed class JsonSettingsStoreMusicDefaultsTests
{
    [Fact]
    public async Task LoadAsync_MissingMusicSections_UsesDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ccs-settings-{Guid.NewGuid():N}.json");
        try
        {
            // Legacy settings ohne MusicPlayer / YouTubeMusic.
            await File.WriteAllTextAsync(path, """
                {
                  "Product": { "ProductName": "Creator Control Suite", "Version": "2.0.81" },
                  "Spotify": { "ClientId": "test" }
                }
                """, Encoding.UTF8);

            var store = new JsonSettingsStore(path);
            var settings = await store.LoadAsync();

            Assert.NotNull(settings.MusicPlayer);
            Assert.Equal(MusicProviderIds.Spotify, settings.MusicPlayer.ProviderId);
            Assert.NotNull(settings.YouTubeMusic);
            Assert.Equal(43831, settings.YouTubeMusic.BridgePort);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            if (File.Exists(path + ".bak"))
                File.Delete(path + ".bak");
        }
    }

    [Fact]
    public async Task LoadAsync_NullMusicPlayer_IsRepaired()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ccs-settings-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, """
                {
                  "MusicPlayer": null,
                  "YouTubeMusic": null
                }
                """, Encoding.UTF8);

            var store = new JsonSettingsStore(path);
            var settings = await store.LoadAsync();

            Assert.NotNull(settings.MusicPlayer);
            Assert.Equal(MusicProviderIds.Spotify, settings.MusicPlayer.ProviderId);
            Assert.NotNull(settings.YouTubeMusic);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
            if (File.Exists(path + ".bak"))
                File.Delete(path + ".bak");
        }
    }
}
