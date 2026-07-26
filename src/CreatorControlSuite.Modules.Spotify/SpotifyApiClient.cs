using System.Diagnostics;
using System.Net;
using CreatorControlSuite.Core.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CreatorControlSuite.Modules.Spotify.Models;

namespace CreatorControlSuite.Modules.Spotify;

public sealed class SpotifyApiClient : ISpotifyApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IAppLogger _logger;
    private string _accessToken = "";
    private long _requestSequence;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private DateTimeOffset _rateLimitUntil = DateTimeOffset.MinValue;

    public SpotifyApiClient(HttpClient httpClient, IAppLogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public void Configure(string accessToken)
    {
        _accessToken = accessToken;
    }

    public async Task<string> GetCurrentUserDisplayNameAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            "me",
            body: null,
            cancellationToken);

        var user = await response.Content.ReadFromJsonAsync<UserResponse>(
            JsonOptions,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Spotify-Benutzerantwort war leer.");

        return string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.Id
            : user.DisplayName;
    }

    public async Task<IReadOnlyList<SpotifyDevice>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            "me/player/devices",
            body: null,
            cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<DevicesResponse>(
            JsonOptions,
            cancellationToken)
            ?? new DevicesResponse();

        return result.Devices
            .Select(ToDevice)
            .ToList();
    }

    public async Task<SpotifyPlaybackState> GetPlaybackStateAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            "me/player",
            body: null,
            cancellationToken,
            allowNoContent: true);

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return new SpotifyPlaybackState(
                HasPlayback: false,
                IsPlaying: false,
                ShuffleEnabled: false,
                RepeatMode: "off",
                ProgressMs: 0,
                Device: null,
                Track: null,
                ContextUri: "");
        }

        var state = await response.Content.ReadFromJsonAsync<PlaybackResponse>(
            JsonOptions,
            cancellationToken);

        if (state is null)
        {
            return new SpotifyPlaybackState(
                HasPlayback: false,
                IsPlaying: false,
                ShuffleEnabled: false,
                RepeatMode: "off",
                ProgressMs: 0,
                Device: null,
                Track: null,
                ContextUri: "");
        }

        return new SpotifyPlaybackState(
            HasPlayback: state.Item is not null,
            IsPlaying: state.IsPlaying,
            ShuffleEnabled: state.ShuffleState,
            RepeatMode: string.IsNullOrWhiteSpace(state.RepeatState) ? "off" : state.RepeatState,
            ProgressMs: state.ProgressMs,
            Device: state.Device is null
                ? null
                : ToDevice(state.Device),
            Track: state.Item is null
                ? null
                : ToTrack(state.Item),
            ContextUri: state.Context?.Uri ?? "");
    }

    public async Task<SpotifyQueue> GetQueueAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            "me/player/queue",
            body: null,
            cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<QueueResponse>(
            JsonOptions,
            cancellationToken) ?? new QueueResponse();

        return new SpotifyQueue(
            result.CurrentlyPlaying is null ? null : ToTrack(result.CurrentlyPlaying),
            result.Queue.Select(ToTrack).ToList());
    }


    public async Task<IReadOnlyList<SpotifyRecentlyPlayedItem>> GetRecentlyPlayedAsync(
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var safeLimit = Math.Clamp(limit, 1, 50);
        using var response = await SendAsync(
            HttpMethod.Get,
            $"me/player/recently-played?limit={safeLimit}",
            body: null,
            cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<RecentlyPlayedResponse>(
            JsonOptions,
            cancellationToken) ?? new RecentlyPlayedResponse();

        return result.Items
            .Where(item => item.Track is not null)
            .Select(item => new SpotifyRecentlyPlayedItem(
                ToTrack(item.Track!),
                item.PlayedAt))
            .ToList();
    }

    public async Task<IReadOnlyList<SpotifyTrack>> SearchTracksAsync(
        string query,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        // Spotify reduced the Search endpoint maximum to 10 for Development
        // Mode apps in February 2026. Higher values return HTTP 400 "Invalid limit".
        var safeLimit = Math.Clamp(limit, 1, 10);
        var url = "search?type=track&limit=" + safeLimit +
                  "&q=" + Uri.EscapeDataString(query.Trim());

        using var response = await SendAsync(
            HttpMethod.Get,
            url,
            body: null,
            cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<SearchResponse>(
            JsonOptions,
            cancellationToken) ?? new SearchResponse();

        return result.Tracks.Items.Select(ToTrack).ToList();
    }

    public async Task<IReadOnlyList<SpotifyTrack>> GetSavedTracksAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var requestedLimit = Math.Clamp(limit, 1, 500);
        var tracks = new List<SpotifyTrack>();
        var offset = 0;
        while (tracks.Count < requestedLimit)
        {
            var pageSize = Math.Min(50, requestedLimit - tracks.Count);
            using var response = await SendAsync(HttpMethod.Get,
                $"me/tracks?limit={pageSize}&offset={offset}", null, cancellationToken);
            var result = await response.Content.ReadFromJsonAsync<SavedTracksResponse>(JsonOptions, cancellationToken)
                         ?? new SavedTracksResponse();
            tracks.AddRange(result.Items.Where(item => item.Track is not null).Select(item => ToTrack(item.Track!)));
            if (string.IsNullOrWhiteSpace(result.Next) || result.Items.Length == 0) break;
            offset += result.Items.Length;
        }
        return tracks;
    }

    public async Task<bool> IsTrackSavedAsync(string trackId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get,
            "me/tracks/contains?ids=" + Uri.EscapeDataString(trackId), null, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<bool[]>(JsonOptions, cancellationToken) ?? [];
        return result.FirstOrDefault();
    }

    public async Task SaveTrackAsync(string trackId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Put,
            "me/tracks?ids=" + Uri.EscapeDataString(trackId), null, cancellationToken, allowNoContent: true);
    }

    public async Task RemoveSavedTrackAsync(string trackId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Delete,
            "me/tracks?ids=" + Uri.EscapeDataString(trackId), null, cancellationToken, allowNoContent: true);
    }

    public async Task AddToQueueAsync(
        string trackUri,
        string? deviceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackUri))
        {
            throw new ArgumentException("Spotify-Titel-URI fehlt.", nameof(trackUri));
        }

        var url = "me/player/queue?uri=" + Uri.EscapeDataString(trackUri.Trim());
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            url += "&device_id=" + Uri.EscapeDataString(deviceId);
        }

        using var response = await SendAsync(
            HttpMethod.Post,
            url,
            body: null,
            cancellationToken,
            allowNoContent: true);
    }

    public async Task<IReadOnlyList<SpotifyPlaylist>> GetCurrentUserPlaylistsAsync(
        CancellationToken cancellationToken = default)
    {
        var playlists = new List<SpotifyPlaylist>();
        var offset = 0;

        while (true)
        {
            using var response = await SendAsync(
                HttpMethod.Get,
                $"me/playlists?limit=50&offset={offset}",
                body: null,
                cancellationToken);

            var result = await response.Content.ReadFromJsonAsync<PlaylistPageResponse>(
                JsonOptions, cancellationToken) ?? new PlaylistPageResponse();

            playlists.AddRange(result.Items.Select(playlist => new SpotifyPlaylist(
                playlist.Id, playlist.Uri, playlist.Name,
                playlist.Owner?.DisplayName ?? playlist.Owner?.Id ?? "",
                playlist.Images.FirstOrDefault()?.Url ?? "",
                playlist.Tracks?.Total ?? 0)));

            if (string.IsNullOrWhiteSpace(result.Next) || result.Items.Length == 0) break;
            offset += result.Items.Length;
        }

        return playlists
            .GroupBy(playlist => playlist.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(playlist => playlist.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<SpotifyTrack>> GetPlaylistTracksAsync(
        string playlistId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playlistId))
        {
            throw new ArgumentException("Spotify-Playlist-ID fehlt.", nameof(playlistId));
        }

        var requestedLimit = Math.Clamp(limit, 1, 500);
        var tracks = new List<SpotifyTrack>();
        var offset = 0;

        while (tracks.Count < requestedLimit)
        {
            var pageSize = Math.Min(50, requestedLimit - tracks.Count);
            using var response = await SendAsync(
                HttpMethod.Get,
                $"playlists/{Uri.EscapeDataString(playlistId.Trim())}/tracks?limit={pageSize}&offset={offset}",
                body: null,
                cancellationToken);

            var result = await response.Content.ReadFromJsonAsync<PlaylistTracksResponse>(
                JsonOptions, cancellationToken) ?? new PlaylistTracksResponse();
            tracks.AddRange(result.Items
                .Where(item => item.Track is not null)
                .Select(item => ToTrack(item.Track!)));

            if (string.IsNullOrWhiteSpace(result.Next) || result.Items.Length == 0) break;
            offset += result.Items.Length;
        }

        return tracks;
    }

    public async Task TransferPlaybackAsync(
        string deviceId,
        bool play,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Put,
            "me/player",
            new
            {
                device_ids = new[] { deviceId },
                play
            },
            cancellationToken,
            allowNoContent: true);
    }

    public async Task StartPlaybackAsync(
        string? deviceId,
        string? contextUri,
        CancellationToken cancellationToken = default)
    {
        var url = "me/player/play";

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            url += "?device_id=" +
                Uri.EscapeDataString(deviceId);
        }

        object? body = string.IsNullOrWhiteSpace(contextUri)
            ? null
            : new { context_uri = contextUri };

        using var response = await SendAsync(
            HttpMethod.Put,
            url,
            body,
            cancellationToken,
            allowNoContent: true);
    }


    public async Task PlayTrackAsync(
        string trackUri,
        string? deviceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trackUri))
        {
            throw new ArgumentException("Spotify-Titel-URI fehlt.", nameof(trackUri));
        }

        var url = "me/player/play";
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            url += "?device_id=" + Uri.EscapeDataString(deviceId);
        }

        using var response = await SendAsync(
            HttpMethod.Put,
            url,
            new { uris = new[] { trackUri.Trim() } },
            cancellationToken,
            allowNoContent: true);
    }

    public async Task PausePlaybackAsync(
        string? deviceId,
        CancellationToken cancellationToken = default)
    {
        var url = "me/player/pause";

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            url += "?device_id=" +
                Uri.EscapeDataString(deviceId);
        }

        using var response = await SendAsync(
            HttpMethod.Put,
            url,
            body: null,
            cancellationToken,
            allowNoContent: true);
    }

    public async Task SetVolumeAsync(
        int volumePercent,
        string? deviceId,
        CancellationToken cancellationToken = default)
    {
        var clamped = Math.Clamp(volumePercent, 0, 100);

        var url =
            "me/player/volume?volume_percent=" +
            clamped;

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            url += "&device_id=" +
                Uri.EscapeDataString(deviceId);
        }

        using var response = await SendAsync(
            HttpMethod.Put,
            url,
            body: null,
            cancellationToken,
            allowNoContent: true);
    }

    public async Task SetShuffleAsync(
        bool enabled,
        string? deviceId,
        CancellationToken cancellationToken = default)
    {
        var url = "me/player/shuffle?state=" +
            enabled.ToString().ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            url += "&device_id=" +
                Uri.EscapeDataString(deviceId);
        }

        using var response = await SendAsync(
            HttpMethod.Put,
            url,
            body: null,
            cancellationToken,
            allowNoContent: true);
    }

    public async Task SetRepeatAsync(
        string repeatMode,
        string? deviceId,
        CancellationToken cancellationToken = default)
    {
        var normalized = repeatMode?.Trim().ToLowerInvariant() switch
        {
            "track" => "track",
            "context" => "context",
            _ => "off"
        };

        var url = "me/player/repeat?state=" + Uri.EscapeDataString(normalized);
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            url += "&device_id=" + Uri.EscapeDataString(deviceId);
        }

        using var response = await SendAsync(
            HttpMethod.Put,
            url,
            body: null,
            cancellationToken,
            allowNoContent: true);
    }

    public async Task SeekPlaybackAsync(
        int positionMs,
        string? deviceId,
        CancellationToken cancellationToken = default)
    {
        var url = "me/player/seek?position_ms=" + Math.Max(0, positionMs);

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            url += "&device_id=" + Uri.EscapeDataString(deviceId);
        }

        using var response = await SendAsync(
            HttpMethod.Put,
            url,
            body: null,
            cancellationToken,
            allowNoContent: true);
    }

    public async Task SkipNextAsync(
        string? deviceId,
        CancellationToken cancellationToken = default)
    {
        var url = "me/player/next";

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            url += "?device_id=" +
                Uri.EscapeDataString(deviceId);
        }

        using var response = await SendAsync(
            HttpMethod.Post,
            url,
            body: null,
            cancellationToken,
            allowNoContent: true);
    }

    public async Task SkipPreviousAsync(
        string? deviceId,
        CancellationToken cancellationToken = default)
    {
        var url = "me/player/previous";

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            url += "?device_id=" +
                Uri.EscapeDataString(deviceId);
        }

        using var response = await SendAsync(
            HttpMethod.Post,
            url,
            body: null,
            cancellationToken,
            allowNoContent: true);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string relativeUrl,
        object? body,
        CancellationToken cancellationToken,
        bool allowNoContent = false)
    {
        if (string.IsNullOrWhiteSpace(_accessToken))
        {
            throw new InvalidOperationException(
                "Spotify API ist nicht konfiguriert.");
        }

        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (now < _rateLimitUntil)
            {
                throw new SpotifyRateLimitException(_rateLimitUntil - now);
            }

            using var request = new HttpRequestMessage(
                method,
                SpotifyConstants.ApiBaseUrl + relativeUrl);

            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer",
                    _accessToken);

            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            var requestNumber = Interlocked.Increment(ref _requestSequence);
            var stopwatch = Stopwatch.StartNew();
            HttpResponseMessage response;

            try
            {
                response = await _httpClient.SendAsync(
                    request,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                stopwatch.Stop();
                _logger.Write(
                    AppLogLevel.Error,
                    "Spotify.Api",
                    $"Spotify-Anfrage #{requestNumber} ist vor einer HTTP-Antwort fehlgeschlagen.",
                    exception,
                    CreateDiagnosticProperties(method, relativeUrl, requestNumber, stopwatch.Elapsed, null, null));
                throw;
            }

            stopwatch.Stop();
            var retryAfter = GetRetryAfter(response);
            var logLevel = response.StatusCode == HttpStatusCode.TooManyRequests
                ? AppLogLevel.Warning
                : response.IsSuccessStatusCode
                    ? AppLogLevel.Debug
                    : AppLogLevel.Error;

            _logger.Write(
                logLevel,
                "Spotify.Api",
                $"Spotify-Anfrage #{requestNumber}: {method.Method} /v1/{relativeUrl} -> {(int)response.StatusCode} {response.ReasonPhrase} in {stopwatch.ElapsedMilliseconds} ms.",
                properties: CreateDiagnosticProperties(method, relativeUrl, requestNumber, stopwatch.Elapsed, response.StatusCode, retryAfter));

            if (allowNoContent && response.StatusCode == HttpStatusCode.NoContent)
            {
                return response;
            }

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var effectiveRetryAfter = retryAfter ?? TimeSpan.FromSeconds(5);
                    if (effectiveRetryAfter <= TimeSpan.Zero)
                    {
                        effectiveRetryAfter = TimeSpan.FromSeconds(5);
                    }

                    _rateLimitUntil = DateTimeOffset.UtcNow.Add(effectiveRetryAfter);
                    response.Dispose();
                    throw new SpotifyRateLimitException(effectiveRetryAfter, error);
                }

                response.Dispose();
                throw new InvalidOperationException(
                    $"Spotify API {(int)response.StatusCode}: {error}");
            }

            return response;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        return response.Headers.RetryAfter?.Delta
            ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
    }

    private static IReadOnlyDictionary<string, string> CreateDiagnosticProperties(
        HttpMethod method,
        string relativeUrl,
        long requestNumber,
        TimeSpan duration,
        HttpStatusCode? statusCode,
        TimeSpan? retryAfter)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["requestNumber"] = requestNumber.ToString(),
            ["method"] = method.Method,
            ["endpoint"] = "/v1/" + relativeUrl,
            ["statusCode"] = statusCode is null ? "none" : ((int)statusCode.Value).ToString(),
            ["durationMs"] = ((long)duration.TotalMilliseconds).ToString(),
            ["retryAfterSeconds"] = retryAfter is null
                ? "none"
                : Math.Max(0, (int)Math.Ceiling(retryAfter.Value.TotalSeconds)).ToString()
        };
    }

    private static SpotifyDevice ToDevice(DeviceResponse device)
    {
        return new SpotifyDevice(
            device.Id ?? "",
            device.Name,
            device.Type,
            device.IsActive,
            device.IsPrivateSession,
            device.IsRestricted,
            device.VolumePercent ?? 0,
            device.SupportsVolume);
    }

    private static SpotifyTrack ToTrack(TrackResponse track)
    {
        return new SpotifyTrack(
            track.Id,
            track.Uri,
            track.Name,
            string.Join(
                ", ",
                track.Artists.Select(artist => artist.Name)),
            track.Album?.Name ?? "",
            track.Album?.Images.FirstOrDefault()?.Url ?? "",
            track.DurationMs);
    }

    private sealed class UserResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = "";
    }

    private sealed class DevicesResponse
    {
        [JsonPropertyName("devices")]
        public DeviceResponse[] Devices { get; set; } = [];
    }

    private sealed class DeviceResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("is_active")]
        public bool IsActive { get; set; }

        [JsonPropertyName("is_private_session")]
        public bool IsPrivateSession { get; set; }

        [JsonPropertyName("is_restricted")]
        public bool IsRestricted { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("volume_percent")]
        public int? VolumePercent { get; set; }

        [JsonPropertyName("supports_volume")]
        public bool SupportsVolume { get; set; }
    }

    private sealed class RecentlyPlayedResponse
    {
        [JsonPropertyName("items")]
        public RecentlyPlayedItemResponse[] Items { get; set; } = [];
    }

    private sealed class RecentlyPlayedItemResponse
    {
        [JsonPropertyName("track")]
        public TrackResponse? Track { get; set; }

        [JsonPropertyName("played_at")]
        public DateTimeOffset PlayedAt { get; set; }
    }

    private sealed class SavedTracksResponse
    {
        [JsonPropertyName("next")]
        public string? Next { get; set; }

        [JsonPropertyName("items")]
        public SavedTrackItemResponse[] Items { get; set; } = [];
    }

    private sealed class SavedTrackItemResponse
    {
        [JsonPropertyName("track")]
        public TrackResponse? Track { get; set; }
    }

    private sealed class PlaylistTracksResponse
    {
        [JsonPropertyName("next")]
        public string? Next { get; set; }

        [JsonPropertyName("items")]
        public PlaylistTrackItemResponse[] Items { get; set; } = [];
    }

    private sealed class PlaylistTrackItemResponse
    {
        [JsonPropertyName("track")]
        public TrackResponse? Track { get; set; }
    }

    private sealed class SearchResponse
    {
        [JsonPropertyName("tracks")]
        public TrackPageResponse Tracks { get; set; } = new();
    }

    private sealed class TrackPageResponse
    {
        [JsonPropertyName("items")]
        public TrackResponse[] Items { get; set; } = [];
    }

    private sealed class QueueResponse
    {
        [JsonPropertyName("currently_playing")]
        public TrackResponse? CurrentlyPlaying { get; set; }

        [JsonPropertyName("queue")]
        public TrackResponse[] Queue { get; set; } = [];
    }

    private sealed class PlaybackResponse
    {
        [JsonPropertyName("device")]
        public DeviceResponse? Device { get; set; }

        [JsonPropertyName("progress_ms")]
        public int ProgressMs { get; set; }

        [JsonPropertyName("is_playing")]
        public bool IsPlaying { get; set; }

        [JsonPropertyName("shuffle_state")]
        public bool ShuffleState { get; set; }

        [JsonPropertyName("repeat_state")]
        public string RepeatState { get; set; } = "off";

        [JsonPropertyName("item")]
        public TrackResponse? Item { get; set; }

        [JsonPropertyName("context")]
        public ContextResponse? Context { get; set; }
    }

    private sealed class ContextResponse
    {
        [JsonPropertyName("uri")]
        public string Uri { get; set; } = "";
    }

    private sealed class TrackResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("uri")]
        public string Uri { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("duration_ms")]
        public int DurationMs { get; set; }

        [JsonPropertyName("artists")]
        public ArtistResponse[] Artists { get; set; } = [];

        [JsonPropertyName("album")]
        public AlbumResponse? Album { get; set; }
    }

    private sealed class ArtistResponse
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }

    private sealed class AlbumResponse
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("images")]
        public ImageResponse[] Images { get; set; } = [];
    }

    private sealed class ImageResponse
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "";
    }

    private sealed class PlaylistPageResponse
    {
        [JsonPropertyName("next")]
        public string? Next { get; set; }

        [JsonPropertyName("items")]
        public PlaylistResponse[] Items { get; set; } = [];
    }

    private sealed class PlaylistResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("uri")]
        public string Uri { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("owner")]
        public OwnerResponse? Owner { get; set; }

        [JsonPropertyName("images")]
        public ImageResponse[] Images { get; set; } = [];

        [JsonPropertyName("tracks")]
        public TracksResponse? Tracks { get; set; }
    }

    private sealed class OwnerResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = "";
    }

    private sealed class TracksResponse
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }
    }
}
