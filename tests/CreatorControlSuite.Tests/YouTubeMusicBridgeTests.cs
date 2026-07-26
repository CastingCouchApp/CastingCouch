using System.Net.Http;
using System.Text;
using System.Text.Json;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Music;
using CreatorControlSuite.Modules.YouTubeMusic;

namespace CreatorControlSuite.Tests;

public sealed class YouTubeMusicBridgeTests
{
    [Fact]
    public async Task StateEndpoint_UpdatesSnapshot_AndCommandsAreDequeued()
    {
        var store = new InMemorySettingsStore
        {
            Settings = new AppSettings
            {
                YouTubeMusic = new YouTubeMusicSettings { BridgePort = 43899, StateTimeoutSeconds = 30 }
            }
        };

        await using var bridge = new YouTubeMusicBridge(store);
        await bridge.StartAsync();

        try
        {
            using var client = new HttpClient();
            var payload = JsonSerializer.Serialize(new
            {
                title = "Test Track",
                artist = "Test Artist",
                album = "Test Album",
                coverUrl = "https://example.com/cover.jpg",
                isPlaying = true,
                progressMs = 1200,
                durationMs = 240000
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var post = await client.PostAsync("http://127.0.0.1:43899/ytmusic/state", content);
            post.EnsureSuccessStatusCode();

            var snapshot = bridge.GetSnapshot(30);
            Assert.Equal(MusicProviderIds.YouTubeMusic, snapshot.ProviderId);
            Assert.True(snapshot.Connected);
            Assert.True(snapshot.IsPlaying);
            Assert.Equal("Test Track", snapshot.Title);
            Assert.Equal("Test Artist", snapshot.Artist);

            bridge.EnqueueCommand("next");
            bridge.EnqueueCommand("playpause");

            var response = await client.GetAsync("http://127.0.0.1:43899/ytmusic/commands");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var commands = doc.RootElement.GetProperty("commands").EnumerateArray()
                .Select(x => x.GetString() ?? "")
                .ToArray();
            Assert.Equal(new[] { "next", "playpause" }, commands);
        }
        finally
        {
            await bridge.StopAsync();
        }
    }

    private sealed class InMemorySettingsStore : ISettingsStore
    {
        public AppSettings Settings { get; set; } = new();

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Settings);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Settings = settings;
            return Task.CompletedTask;
        }
    }
}
