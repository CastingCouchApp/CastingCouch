using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Music;

namespace CreatorControlSuite.Tests;

public sealed class MusicPlayerRouterTests
{
    [Fact]
    public async Task ApplyProvider_DisconnectsInactivePlayer()
    {
        var store = new InMemorySettingsStore();
        var spotify = new FakeMusicPlayer(MusicProviderIds.Spotify, "Spotify");
        var youtube = new FakeMusicPlayer(MusicProviderIds.YouTubeMusic, "YouTube Music");
        var router = new MusicPlayerRouter(store, [spotify, youtube]);

        await router.ApplyProviderAsync(MusicProviderIds.YouTubeMusic);

        Assert.Equal(MusicProviderIds.YouTubeMusic, router.ActiveProviderId);
        Assert.True(spotify.DisconnectCalls >= 1);
        Assert.Equal(0, youtube.DisconnectCalls);

        await router.ApplyProviderAsync(MusicProviderIds.Spotify);

        Assert.Equal(MusicProviderIds.Spotify, router.ActiveProviderId);
        Assert.True(youtube.DisconnectCalls >= 1);
    }

    [Fact]
    public async Task Commands_TargetOnlyActivePlayer()
    {
        var store = new InMemorySettingsStore();
        var spotify = new FakeMusicPlayer(MusicProviderIds.Spotify, "Spotify");
        var youtube = new FakeMusicPlayer(MusicProviderIds.YouTubeMusic, "YouTube Music");
        var router = new MusicPlayerRouter(store, [spotify, youtube]);

        await router.ApplyProviderAsync(MusicProviderIds.YouTubeMusic);
        await router.PlayPauseAsync();
        await router.NextAsync();

        Assert.Equal(1, youtube.PlayPauseCalls);
        Assert.Equal(1, youtube.NextCalls);
        Assert.Equal(0, spotify.PlayPauseCalls);
        Assert.Equal(0, spotify.NextCalls);
    }

    private sealed class InMemorySettingsStore : ISettingsStore
    {
        private AppSettings _settings = new();

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_settings);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMusicPlayer(string id, string displayName) : IMusicPlayer
    {
        public string Id { get; } = id;
        public string DisplayName { get; } = displayName;
        public bool SupportsSeek => false;
        public bool SupportsVolume => false;
        public int DisconnectCalls { get; private set; }
        public int PlayPauseCalls { get; private set; }
        public int NextCalls { get; private set; }

        public Task<NowPlayingSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(NowPlayingSnapshot.Empty(Id));

        public Task PlayAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PlayPauseAsync(CancellationToken cancellationToken = default)
        {
            PlayPauseCalls++;
            return Task.CompletedTask;
        }

        public Task NextAsync(CancellationToken cancellationToken = default)
        {
            NextCalls++;
            return Task.CompletedTask;
        }

        public Task PreviousAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SeekAsync(int positionMs, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            DisconnectCalls++;
            return Task.CompletedTask;
        }
    }
}
