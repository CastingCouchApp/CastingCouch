using CreatorControlSuite.App.Services;
using CreatorControlSuite.Modules.Spotify.Models;

namespace CreatorControlSuite.Tests;

public sealed class SpotifyPlaybackLevelResolverTests
{
    [Fact]
    public void Resolve_PrefersCurrentPlaybackLevelOverDeviceCatalogLevel()
    {
        SpotifySnapshot snapshot = Snapshot(
            playbackDevice: Device("device", 10, true),
            devices: [Device("device", 50, true)]);

        Assert.Equal(10, SpotifyPlaybackLevelResolver.Resolve(snapshot, 25));
    }

    [Fact]
    public void Resolve_UsesActiveDeviceWhenPlaybackContainsNoDevice()
    {
        SpotifySnapshot snapshot = Snapshot(
            playbackDevice: null,
            devices: [Device("device", 50, true)]);

        Assert.Equal(50, SpotifyPlaybackLevelResolver.Resolve(snapshot, 25));
    }

    [Fact]
    public void Resolve_UsesFallbackOnlyWhenSpotifyHasNoDeviceLevel()
    {
        Assert.Equal(25, SpotifyPlaybackLevelResolver.Resolve(Snapshot(null, []), 25));
    }

    [Fact]
    public void Resolve_PrefersRecentlyRequestedSceneLevelDuringSpotifyPropagationDelay()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SpotifySnapshot snapshot = Snapshot(
            playbackDevice: Device("device", 30, true),
            devices: [Device("device", 50, true)]);

        int level = SpotifyPlaybackLevelResolver.Resolve(
            snapshot,
            fallbackLevel: 25,
            requestedLevel: 30,
            requestedAt: now.AddSeconds(-1),
            now: now);

        Assert.Equal(30, level);
    }

    private static SpotifySnapshot Snapshot(
        SpotifyDevice? playbackDevice,
        IReadOnlyList<SpotifyDevice> devices) =>
        new(
            true,
            "Test",
            devices,
            new SpotifyPlaybackState(false, false, false, "off", 0, playbackDevice, null, ""),
            [],
            new SpotifyQueue(null, []),
            [],
            []);

    private static SpotifyDevice Device(string id, int level, bool active) =>
        new(id, "Gerät", "Computer", active, false, false, level, true);
}
