using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using CreatorControlSuite.Core.Logging;
using CreatorControlSuite.Modules.Spotify;
using CreatorControlSuite.Modules.Spotify.Models;

namespace CreatorControlSuite.Tests;

public sealed class SpotifyApiContractTests
{
    [Fact]
    [Trait("Category", "Contract")]
    public async Task Playback_MapsCurrentSchemaAndAuthorization()
    {
        var handler = new FixtureHandler("playback-track.json");
        var client = CreateClient(handler);

        SpotifyPlaybackState playback =
            await client.GetPlaybackStateAsync();

        RequestSnapshot request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://api.spotify.com/v1/me/player",
            request.Uri);
        Assert.Equal("Bearer contract-token", request.Authorization);
        Assert.True(playback.HasPlayback);
        Assert.True(playback.IsPlaying);
        Assert.True(playback.ShuffleEnabled);
        Assert.Equal("context", playback.RepeatMode);
        Assert.Equal(12345, playback.ProgressMs);
        Assert.Equal("Studio PC", playback.Device?.Name);
        Assert.Equal(64, playback.Device?.VolumePercent);
        Assert.Equal("Contract Song", playback.Track?.Name);
        Assert.Equal(
            "Contract Artist, Guest Artist",
            playback.Track?.Artist);
        Assert.Equal(
            "spotify:playlist:stream",
            playback.ContextUri);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task Playback_AcceptsDocumentedNullableFields()
    {
        var handler = new FixtureHandler("playback-nullable.json");
        var client = CreateClient(handler);

        SpotifyPlaybackState playback =
            await client.GetPlaybackStateAsync();

        Assert.False(playback.HasPlayback);
        Assert.Equal(0, playback.ProgressMs);
        Assert.Null(playback.Track);
        Assert.Equal("", playback.ContextUri);
        Assert.Equal("", playback.Device?.Id);
        Assert.Equal(0, playback.Device?.VolumePercent);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task Playlists_MapRenamedItemsFieldAndPaginate()
    {
        var handler = new FixtureHandler(
            "playlists-page-1.json",
            "playlists-page-2.json");
        var client = CreateClient(handler);

        IReadOnlyList<SpotifyPlaylist> playlists =
            await client.GetCurrentUserPlaylistsAsync();

        Assert.Equal(2, handler.Requests.Count);
        Assert.EndsWith(
            "me/playlists?limit=50&offset=0",
            handler.Requests[0].Uri);
        Assert.EndsWith(
            "me/playlists?limit=50&offset=2",
            handler.Requests[1].Uri);
        Assert.Equal(["Alpha", "Beta", "Zeta"], playlists.Select(p => p.Name));
        Assert.Equal(12, playlists[0].TrackCount);
        Assert.Equal(0, playlists[1].TrackCount);
        Assert.Equal(7, playlists[2].TrackCount);
        Assert.Equal("owner-z", playlists[2].OwnerName);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task PlaylistItems_UseCurrentRouteAndItemField()
    {
        var handler = new FixtureHandler("playlist-items.json");
        var client = CreateClient(handler);

        IReadOnlyList<SpotifyTrack> tracks =
            await client.GetPlaylistTracksAsync("playlist / id");

        RequestSnapshot request = Assert.Single(handler.Requests);
        Assert.EndsWith(
            "playlists/playlist%20%2F%20id/items?limit=50&offset=0",
            request.Uri);
        SpotifyTrack track = Assert.Single(tracks);
        Assert.Equal("track-new", track.Id);
        Assert.Equal("New Contract Track", track.Name);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task LibraryOperations_UseGenericUriEndpoints()
    {
        var handler = new FixtureHandler(
            new FixtureResponse("[true]"),
            new FixtureResponse(""),
            new FixtureResponse(""));
        var client = CreateClient(handler);

        bool saved = await client.IsTrackSavedAsync("track / id");
        await client.SaveTrackAsync("track / id");
        await client.RemoveSavedTrackAsync("spotify:track:track / id");

        Assert.True(saved);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.EndsWith(
            "me/library/contains?uris=spotify%3Atrack%3Atrack%20%2F%20id",
            handler.Requests[0].Uri);
        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
        Assert.EndsWith(
            "me/library?uris=spotify%3Atrack%3Atrack%20%2F%20id",
            handler.Requests[1].Uri);
        Assert.Equal(HttpMethod.Delete, handler.Requests[2].Method);
        Assert.EndsWith(
            "me/library?uris=spotify%3Atrack%3Atrack%20%2F%20id",
            handler.Requests[2].Uri);
    }

    private static SpotifyApiClient CreateClient(FixtureHandler handler)
    {
        var client = new SpotifyApiClient(
            new HttpClient(handler),
            new NullLogger());
        client.Configure("contract-token");
        return client;
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        private readonly Queue<FixtureResponse> _responses;

        public FixtureHandler(params string[] fixtureNames)
            : this(fixtureNames.Select(name =>
                new FixtureResponse(ReadFixture(name))).ToArray())
        {
        }

        public FixtureHandler(params FixtureResponse[] responses)
        {
            _responses = new Queue<FixtureResponse>(responses);
        }

        public List<RequestSnapshot> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RequestSnapshot(
                request.Method,
                request.RequestUri?.AbsoluteUri ?? "",
                request.Headers.Authorization?.ToString() ?? ""));
            FixtureResponse fixture = _responses.Dequeue();
            return Task.FromResult(new HttpResponseMessage(fixture.StatusCode)
            {
                Content = new StringContent(
                    fixture.Content,
                    System.Text.Encoding.UTF8,
                    "application/json")
            });
        }
    }

    private sealed record FixtureResponse(
        string Content,
        HttpStatusCode StatusCode = HttpStatusCode.OK);

    private sealed record RequestSnapshot(
        HttpMethod Method,
        string Uri,
        string Authorization);

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "spotify",
            name));

    private sealed class NullLogger : IAppLogger
    {
        public event EventHandler<AppLogEntry>? EntryWritten
        {
            add { }
            remove { }
        }

        public void Write(
            AppLogLevel level,
            string category,
            string message,
            Exception? exception = null,
            IReadOnlyDictionary<string, string>? properties = null)
        {
        }

        public Task<IReadOnlyList<AppLogEntry>> ReadRecentAsync(
            int maxEntries = 500,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AppLogEntry>>([]);

        public Task<string> ExportAsync(
            string targetPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(targetPath);
    }
}
