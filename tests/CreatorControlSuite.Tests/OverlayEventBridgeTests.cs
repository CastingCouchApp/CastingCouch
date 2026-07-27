using CreatorControlSuite.Modules.Overlay;
using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.Tests;

public sealed class OverlayEventBridgeTests
{
    [Fact]
    public void FromTwitch_MapsEventSubFields()
    {
        var twitch = new TwitchEvent(
            Type: "channel.follow",
            Summary: "alice folgt jetzt",
            ReceivedAt: DateTimeOffset.Parse("2026-07-27T18:00:00Z"),
            Data: new Dictionary<string, string>
            {
                ["user_name"] = "alice",
                ["user_id"] = "1"
            });

        OverlayRealtimeEvent evt = OverlayEventBridge.FromTwitch(
            twitch.Type,
            twitch.Summary,
            twitch.ReceivedAt,
            twitch.Data);

        Assert.Equal("twitch", evt.Source);
        Assert.Equal("channel.follow", evt.Type);
        Assert.Equal("alice folgt jetzt", evt.Summary);
        Assert.Equal(DateTimeOffset.Parse("2026-07-27T18:00:00Z"), evt.At);
        Assert.Equal("alice", evt.Data["user_name"]);
        Assert.Equal("1", evt.Data["user_id"]);
    }

    [Fact]
    public void AppStreamPhase_BuildsTypedEvent()
    {
        OverlayRealtimeEvent evt = OverlayEventBridge.AppStreamPhase("Live");

        Assert.Equal("app", evt.Source);
        Assert.Equal("app.stream.phase", evt.Type);
        Assert.Equal("Live", evt.Data["phase"]);
    }

    [Fact]
    public void AppStreamLive_BuildsTypedEvent()
    {
        OverlayRealtimeEvent evt = OverlayEventBridge.AppStreamLive(true);

        Assert.Equal("app", evt.Source);
        Assert.Equal("app.stream.live", evt.Type);
        Assert.Equal("true", evt.Data["isLive"]);
    }

    [Fact]
    public void AppObsScene_BuildsTypedEvent()
    {
        OverlayRealtimeEvent evt = OverlayEventBridge.AppObsScene("Game");

        Assert.Equal("app", evt.Source);
        Assert.Equal("app.obs.scene", evt.Type);
        Assert.Equal("Game", evt.Data["scene"]);
    }

    [Fact]
    public void AppSpotifyTrack_BuildsMusicTrackEvent()
    {
        OverlayRealtimeEvent evt = OverlayEventBridge.AppSpotifyTrack(
            "Song",
            "Artist",
            "https://cover");

        Assert.Equal("app", evt.Source);
        Assert.Equal("app.music.track", evt.Type);
        Assert.Equal("spotify", evt.Data["provider"]);
        Assert.Equal("Song", evt.Data["title"]);
        Assert.Equal("Artist", evt.Data["artist"]);
        Assert.Equal("https://cover", evt.Data["coverUrl"]);
    }

    [Fact]
    public void AppMusicTrack_BuildsYouTubeMusicEvent()
    {
        OverlayRealtimeEvent evt = OverlayEventBridge.AppMusicTrack(
            "ytmusic",
            "Song",
            "Artist",
            "https://cover");

        Assert.Equal("app.music.track", evt.Type);
        Assert.Equal("ytmusic", evt.Data["provider"]);
        Assert.Equal("YouTube Music", evt.Data["providerDisplayName"]);
    }

    [Fact]
    public void AppAlert_BuildsTypedEvent()
    {
        OverlayRealtimeEvent evt = OverlayEventBridge.AppAlert("Follow", "alice");

        Assert.Equal("app", evt.Source);
        Assert.Equal("app.alert", evt.Type);
        Assert.Equal("Follow", evt.Data["alertType"]);
        Assert.Equal("alice", evt.Data["user"]);
    }
}
