using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Modules;
using CreatorControlSuite.Modules.Spotify.Models;

namespace CreatorControlSuite.Modules.Spotify;

public sealed class SpotifyModule : IConnectableModule
{
    private readonly ISettingsStore _settingsStore;
    private readonly ISpotifyOAuthClient _oauthClient;
    private readonly ISpotifyApiClient _apiClient;
    private readonly SpotifyTokenRepository _tokenRepository;

    private SpotifyTokenSet? _token;
    private string _displayName = "";
    private IReadOnlyList<SpotifyDevice> _devices = [];
    private IReadOnlyList<SpotifyPlaylist> _playlists = [];
    private SpotifyQueue _queue = new(null, []);
    private IReadOnlyList<SpotifyRecentlyPlayedItem> _recentlyPlayed = [];
    private IReadOnlyList<SpotifyTrack> _savedTracks = [];
    private SpotifyPlaybackState _playback =
        new(false, false, false, "off", 0, null, null, "");
    private DateTimeOffset _lastLibraryRefresh = DateTimeOffset.MinValue;
    private readonly Dictionary<string, string> _lastRefreshErrors = new(StringComparer.OrdinalIgnoreCase);
    private int _consecutiveEmptyPlaybackSnapshots;
    private DateTimeOffset _lastValidPlaybackAt = DateTimeOffset.MinValue;
    private static readonly TimeSpan PlaybackEmptyGracePeriod = TimeSpan.FromSeconds(15);
    private const int EmptyPlaybackConfirmationCount = 5;

    public IReadOnlyDictionary<string, string> LastRefreshErrors => _lastRefreshErrors;

    public SpotifyModule(
        ISettingsStore settingsStore,
        ISpotifyOAuthClient oauthClient,
        ISpotifyApiClient apiClient,
        SpotifyTokenRepository tokenRepository)
    {
        _settingsStore = settingsStore;
        _oauthClient = oauthClient;
        _apiClient = apiClient;
        _tokenRepository = tokenRepository;
    }

    public string Id => "spotify";
    public string DisplayName => "Spotify";

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task AuthorizeAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsStore.LoadAsync(
            cancellationToken);

        if (string.IsNullOrWhiteSpace(settings.Spotify.ClientId))
        {
            throw new InvalidOperationException(
                "Bitte zuerst die Spotify Client-ID eintragen.");
        }

        var token = await _oauthClient.AuthorizeAsync(
            settings.Spotify.ClientId,
            settings.Spotify.RedirectUri,
            settings.Spotify.Scopes,
            cancellationToken);

        await _tokenRepository.SaveAsync(
            token,
            cancellationToken);

        await ConnectAsync(cancellationToken);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(
            cancellationToken);

        _token = await GetValidTokenAsync(
            settings.Spotify.ClientId,
            cancellationToken);

        _apiClient.Configure(_token.AccessToken);

        await RefreshAsync(cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        _token = null;
        _displayName = "";
        _devices = [];
        _playlists = [];
        _queue = new SpotifyQueue(null, []);
        _recentlyPlayed = [];
        _savedTracks = [];
        _playback = new(false, false, false, "off", 0, null, null, "");
        _consecutiveEmptyPlaybackSnapshots = 0;
        _lastValidPlaybackAt = DateTimeOffset.MinValue;

        return Task.CompletedTask;
    }

    public async Task RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        // Die Bereiche werden bewusst einzeln geladen. Ein zeitweilig nicht
        // verfügbarer Premium-Endpunkt (z. B. Queue oder Player) darf nicht mehr
        // verhindern, dass Geräte und Playlists angezeigt werden.
        _lastRefreshErrors.Clear();
        _displayName = await LoadSectionAsync(
            "Benutzerkonto",
            () => _apiClient.GetCurrentUserDisplayNameAsync(cancellationToken),
            _displayName);
        _devices = await LoadSectionAsync(
            "Wiedergabegeräte",
            () => _apiClient.GetDevicesAsync(cancellationToken),
            _devices);
        _playback = await LoadSectionAsync(
            "Player",
            () => _apiClient.GetPlaybackStateAsync(cancellationToken),
            _playback);
        _playlists = await LoadSectionAsync(
            "Playlists",
            () => _apiClient.GetCurrentUserPlaylistsAsync(cancellationToken),
            _playlists);
        _queue = await LoadSectionAsync(
            "Warteschlange",
            () => _apiClient.GetQueueAsync(cancellationToken),
            _queue);
        _recentlyPlayed = await LoadSectionAsync(
            "Verlauf",
            () => _apiClient.GetRecentlyPlayedAsync(20, cancellationToken),
            _recentlyPlayed);
        _savedTracks = await LoadSectionAsync(
            "Favoriten",
            () => _apiClient.GetSavedTracksAsync(200, cancellationToken),
            _savedTracks);
        _lastLibraryRefresh = DateTimeOffset.UtcNow;
    }


    private async Task<T> LoadSectionAsync<T>(string section, Func<Task<T>> loader, T fallback)
    {
        try
        {
            return await loader();
        }
        catch (InvalidOperationException exception) when (IsUnauthorized(exception))
        {
            await EnsureApiTokenAsync(forceRefresh: true, cancellationToken: CancellationToken.None);
            try
            {
                return await loader();
            }
            catch (Exception retryException)
            {
                _lastRefreshErrors[section] = NormalizeApiError(retryException.Message);
                return fallback;
            }
        }
        catch (Exception exception)
        {
            _lastRefreshErrors[section] = NormalizeApiError(exception.Message);
            return fallback;
        }
    }


    private static string NormalizeApiError(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Unbekannter Spotify-Fehler.";
        if (message.Contains("403", StringComparison.OrdinalIgnoreCase))
            return "Zugriff verweigert. Spotify-Premium und die erforderlichen Berechtigungen prüfen; anschließend Spotify neu autorisieren.";
        if (message.Contains("401", StringComparison.OrdinalIgnoreCase))
            return "Anmeldung abgelaufen oder Berechtigung fehlt. Spotify neu autorisieren.";
        if (message.Contains("429", StringComparison.OrdinalIgnoreCase))
            return "Spotify begrenzt die Anfragen vorübergehend. Bitte später erneut aktualisieren.";
        return message.Length > 260 ? message[..260] + "…" : message;
    }

    public async Task RefreshPlaybackAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        SpotifyPlaybackState refreshed;
        try
        {
            refreshed = await _apiClient.GetPlaybackStateAsync(cancellationToken);
        }
        catch (InvalidOperationException exception) when (IsUnauthorized(exception))
        {
            // Ein abgelaufenes Access-Token ist kein Spotify-Disconnect. Token erneuern
            // und genau einmal wiederholen, ohne den letzten Playerzustand zu löschen.
            await EnsureApiTokenAsync(forceRefresh: true, cancellationToken: cancellationToken);
            try
            {
                refreshed = await _apiClient.GetPlaybackStateAsync(cancellationToken);
            }
            catch (Exception retryException)
            {
                _lastRefreshErrors["Player"] = NormalizeApiError(retryException.Message);
                return;
            }
        }
        catch (Exception exception)
        {
            // Netzwerkfehler, 429 und kurze Spotify-Aussetzer dürfen den gültigen
            // Snapshot nicht überschreiben. Der nächste Poll versucht es erneut.
            _lastRefreshErrors["Player"] = NormalizeApiError(exception.Message);
            return;
        }

        if (refreshed.Track is not null)
        {
            _playback = refreshed;
            _consecutiveEmptyPlaybackSnapshots = 0;
            _lastValidPlaybackAt = DateTimeOffset.UtcNow;
            _lastRefreshErrors.Remove("Player");
            return;
        }

        _consecutiveEmptyPlaybackSnapshots++;
        var withinGracePeriod =
            _lastValidPlaybackAt != DateTimeOffset.MinValue &&
            DateTimeOffset.UtcNow - _lastValidPlaybackAt < PlaybackEmptyGracePeriod;

        // Spotify liefert bei Gerätewechsel, Token-Erneuerung oder kurzzeitigem
        // API-Leerlauf gelegentlich 204/leer. Erst fünf aufeinanderfolgende leere
        // Antworten UND mindestens 15 Sekunden ohne gültigen Titel bestätigen einen
        // echten leeren Playerzustand. Bis dahin bleibt der letzte Titel sichtbar.
        if (_playback.Track is not null &&
            (withinGracePeriod || _consecutiveEmptyPlaybackSnapshots < EmptyPlaybackConfirmationCount))
        {
            return;
        }

        _playback = refreshed;
    }

    public async Task RefreshQueueAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        _queue = await _apiClient.GetQueueAsync(cancellationToken);
    }

    public async Task RefreshRecentlyPlayedAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        _recentlyPlayed = await _apiClient.GetRecentlyPlayedAsync(20, cancellationToken);
    }

    public async Task RefreshSavedTracksAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        _savedTracks = await _apiClient.GetSavedTracksAsync(200, cancellationToken);
    }

    public async Task<bool> IsTrackSavedAsync(SpotifyTrack track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        EnsureConnected();
        return await _apiClient.IsTrackSavedAsync(track.Id, cancellationToken);
    }

    public async Task SetTrackSavedAsync(SpotifyTrack track, bool saved, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        EnsureConnected();
        if (saved) await _apiClient.SaveTrackAsync(track.Id, cancellationToken);
        else await _apiClient.RemoveSavedTrackAsync(track.Id, cancellationToken);
        await RefreshSavedTracksAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SpotifyTrack>> SearchTracksAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        return await _apiClient.SearchTracksAsync(query, 10, cancellationToken);
    }

    public async Task<IReadOnlyList<SpotifyTrack>> GetPlaylistTracksAsync(
        SpotifyPlaylist playlist,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        EnsureConnected();

        await EnsureApiTokenAsync(forceRefresh: false, cancellationToken: cancellationToken);
        try
        {
            return await _apiClient.GetPlaylistTracksAsync(playlist.Id, 500, cancellationToken);
        }
        catch (InvalidOperationException exception) when (IsUnauthorized(exception))
        {
            await EnsureApiTokenAsync(forceRefresh: true, cancellationToken: cancellationToken);
            return await _apiClient.GetPlaylistTracksAsync(playlist.Id, 500, cancellationToken);
        }
    }

    public async Task AddToQueueAsync(
        SpotifyTrack track,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        EnsureConnected();

        var settings = await _settingsStore.LoadAsync(cancellationToken);
        var deviceId = string.IsNullOrWhiteSpace(settings.Spotify.PreferredDeviceId)
            ? GetPreferredDeviceId()
            : settings.Spotify.PreferredDeviceId;

        await EnsureApiTokenAsync(forceRefresh: false, cancellationToken: cancellationToken);
        try
        {
            await _apiClient.AddToQueueAsync(track.Uri, deviceId, cancellationToken);
        }
        catch (InvalidOperationException exception) when (IsUnauthorized(exception))
        {
            await EnsureApiTokenAsync(forceRefresh: true, cancellationToken: cancellationToken);
            await _apiClient.AddToQueueAsync(track.Uri, deviceId, cancellationToken);
        }

        _queue = await _apiClient.GetQueueAsync(cancellationToken);
    }

    public async Task PlayTrackAsync(
        SpotifyTrack track,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        EnsureConnected();

        var settings = await _settingsStore.LoadAsync(cancellationToken);
        var deviceId = string.IsNullOrWhiteSpace(settings.Spotify.PreferredDeviceId)
            ? GetPreferredDeviceId()
            : settings.Spotify.PreferredDeviceId;

        await EnsureApiTokenAsync(forceRefresh: false, cancellationToken: cancellationToken);
        try
        {
            await _apiClient.PlayTrackAsync(track.Uri, deviceId, cancellationToken);
        }
        catch (InvalidOperationException exception) when (IsUnauthorized(exception))
        {
            await EnsureApiTokenAsync(forceRefresh: true, cancellationToken: cancellationToken);
            await _apiClient.PlayTrackAsync(track.Uri, deviceId, cancellationToken);
        }

        await RefreshPlaybackAsync(cancellationToken);
    }

    public async Task RefreshLibraryIfStaleAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        if (DateTimeOffset.UtcNow - _lastLibraryRefresh < TimeSpan.FromMinutes(5))
        {
            return;
        }

        var devicesTask = _apiClient.GetDevicesAsync(cancellationToken);
        var playlistsTask = _apiClient.GetCurrentUserPlaylistsAsync(cancellationToken);
        await Task.WhenAll(devicesTask, playlistsTask);
        _devices = await devicesTask;
        _playlists = await playlistsTask;
        _lastLibraryRefresh = DateTimeOffset.UtcNow;
    }

    public async Task RefreshDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        try
        {
            _devices = await _apiClient.GetDevicesAsync(cancellationToken);
        }
        catch (InvalidOperationException exception) when (IsUnauthorized(exception))
        {
            await EnsureApiTokenAsync(forceRefresh: true, cancellationToken: cancellationToken);
            _devices = await _apiClient.GetDevicesAsync(cancellationToken);
        }
    }

    public SpotifySnapshot GetSnapshot()
    {
        return new SpotifySnapshot(
            Authenticated: _token is not null,
            UserDisplayName: _displayName,
            Devices: _devices,
            Playback: _playback,
            Playlists: _playlists,
            Queue: _queue,
            RecentlyPlayed: _recentlyPlayed,
            SavedTracks: _savedTracks);
    }

    public async Task TransferPlaybackAsync(
        string deviceId,
        bool play,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        await _apiClient.TransferPlaybackAsync(
            deviceId,
            play,
            cancellationToken);

    }


    public async Task<SpotifyDevice> ActivatePreferredDeviceAsync(
        bool play,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        await RefreshDevicesAsync(cancellationToken);

        SpotifyDevice? device = null;
        if (!string.IsNullOrWhiteSpace(settings.Spotify.PreferredDeviceId))
        {
            device = _devices.FirstOrDefault(item =>
                string.Equals(item.Id, settings.Spotify.PreferredDeviceId, StringComparison.Ordinal));
        }

        if (device is null && settings.Spotify.UseActiveDeviceWhenPreferredUnavailable)
        {
            device = _devices.FirstOrDefault(item => item.IsActive && !item.IsRestricted)
                ?? _devices.FirstOrDefault(item => !item.IsRestricted);
        }

        if (device is null)
        {
            throw new InvalidOperationException(
                "Das gespeicherte Spotify-Standardgerät ist nicht erreichbar. Spotify dort öffnen und kurz einen Titel starten.");
        }

        if (device.IsRestricted)
        {
            throw new InvalidOperationException(
                $"Das Spotify-Gerät '{device.Name}' ist eingeschränkt und kann nicht ferngesteuert werden.");
        }

        if (!device.IsActive || !string.Equals(_playback.Device?.Id, device.Id, StringComparison.Ordinal))
        {
            await _apiClient.TransferPlaybackAsync(device.Id, play, cancellationToken);
            await RefreshPlaybackAsync(cancellationToken);
        }

        return device;
    }

    public async Task StartConfiguredPlaylistAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var settings = await _settingsStore.LoadAsync(
            cancellationToken);

        await StartPlaylistAsync(
            settings.Spotify.StartPlaylistUri,
            startVolumePercent: settings.Spotify.StartVolumePercent,
            cancellationToken);
    }

    public async Task StartPlaylistAsync(
        string playlistUri,
        bool applyConfiguredStartVolume = false,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        var startVolumePercent = applyConfiguredStartVolume
            ? settings.Spotify.StartVolumePercent
            : (int?)null;

        await StartPlaylistAsync(
            playlistUri,
            startVolumePercent,
            cancellationToken);
    }

    public async Task StartPlaylistAsync(
        string playlistUri,
        int? startVolumePercent,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        if (string.IsNullOrWhiteSpace(playlistUri))
        {
            throw new InvalidOperationException(
                "Bitte zuerst eine Spotify-Playlist auswählen.");
        }

        // Access tokens can be invalidated before their saved expiry timestamp.
        // Refresh proactively and retry once on a Spotify 401 response.
        await EnsureApiTokenAsync(forceRefresh: false, cancellationToken: cancellationToken);
        try
        {
            await StartPlaylistCoreAsync(playlistUri, startVolumePercent, cancellationToken);
        }
        catch (InvalidOperationException exception) when (IsUnauthorized(exception))
        {
            await EnsureApiTokenAsync(forceRefresh: true, cancellationToken: cancellationToken);
            await StartPlaylistCoreAsync(playlistUri, startVolumePercent, cancellationToken);
        }
    }

    private async Task StartPlaylistCoreAsync(
        string playlistUri,
        int? startVolumePercent,
        CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        var deviceId = string.IsNullOrWhiteSpace(settings.Spotify.PreferredDeviceId)
            ? GetPreferredDeviceId()
            : settings.Spotify.PreferredDeviceId;

        if (settings.Spotify.AutoTransferToPreferredDevice)
        {
            var activated = await ActivatePreferredDeviceAsync(play: false, cancellationToken);
            deviceId = activated.Id;
        }
        else if (!string.IsNullOrWhiteSpace(deviceId) && _playback.Device?.Id != deviceId)
        {
            await _apiClient.TransferPlaybackAsync(deviceId, play: false, cancellationToken);
        }

        // StartPlayback activates the player reliably. Shuffle used to run first
        // and Spotify rejects that request for an inactive device, so the actual
        // playlist start was never reached.
        await _apiClient.StartPlaybackAsync(deviceId, playlistUri, cancellationToken);

        if (startVolumePercent.HasValue)
        {
            await _apiClient.SetVolumeAsync(
                Math.Clamp(startVolumePercent.Value, 0, 100),
                deviceId,
                cancellationToken);
        }

        await _apiClient.SetShuffleAsync(
            settings.Spotify.ShuffleSelectedPlaylist,
            deviceId,
            cancellationToken);
    }

    private async Task EnsureApiTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        var saved = await _tokenRepository.LoadAsync(cancellationToken)
            ?? throw new InvalidOperationException("Spotify wurde noch nicht autorisiert.");

        if (!forceRefresh && saved.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            _token = saved;
            _apiClient.Configure(saved.AccessToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(saved.RefreshToken))
        {
            throw new InvalidOperationException(
                "Die Spotify-Anmeldung ist abgelaufen. Bitte Spotify unter Einstellungen einmal neu autorisieren.");
        }

        var refreshed = await _oauthClient.RefreshAsync(
            settings.Spotify.ClientId,
            saved.RefreshToken,
            cancellationToken);
        await _tokenRepository.SaveAsync(refreshed, cancellationToken);
        _token = refreshed;
        _apiClient.Configure(refreshed.AccessToken);
    }

    private static bool IsUnauthorized(Exception exception)
        => exception.Message.Contains("Spotify API 401", StringComparison.OrdinalIgnoreCase)
           || exception.Message.Contains("expired access token", StringComparison.OrdinalIgnoreCase)
           || exception.Message.Contains("invalid access token", StringComparison.OrdinalIgnoreCase);

    public async Task PauseAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        await _apiClient.PausePlaybackAsync(
            GetPreferredDeviceId(),
            cancellationToken);

    }

    public async Task ResumeAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        await _apiClient.StartPlaybackAsync(
            GetPreferredDeviceId(),
            contextUri: null,
            cancellationToken);

    }

    public Task NextAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        return _apiClient.SkipNextAsync(
            GetPreferredDeviceId(),
            cancellationToken);
    }

    public Task PreviousAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        return _apiClient.SkipPreviousAsync(
            GetPreferredDeviceId(),
            cancellationToken);
    }

    public Task SetVolumeAsync(
        int volumePercent,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        return _apiClient.SetVolumeAsync(
            volumePercent,
            GetPreferredDeviceId(),
            cancellationToken);
    }

    public async Task SetShuffleAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        await EnsureApiTokenAsync(forceRefresh: false, cancellationToken: cancellationToken);
        try
        {
            await _apiClient.SetShuffleAsync(
                enabled,
                GetPreferredDeviceId(),
                cancellationToken);
        }
        catch (InvalidOperationException exception) when (IsUnauthorized(exception))
        {
            await EnsureApiTokenAsync(forceRefresh: true, cancellationToken: cancellationToken);
            await _apiClient.SetShuffleAsync(
                enabled,
                GetPreferredDeviceId(),
                cancellationToken);
        }
    }

    public async Task SetRepeatAsync(
        string repeatMode,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        await EnsureApiTokenAsync(forceRefresh: false, cancellationToken: cancellationToken);
        try
        {
            await _apiClient.SetRepeatAsync(
                repeatMode,
                GetPreferredDeviceId(),
                cancellationToken);
        }
        catch (InvalidOperationException exception) when (IsUnauthorized(exception))
        {
            await EnsureApiTokenAsync(forceRefresh: true, cancellationToken: cancellationToken);
            await _apiClient.SetRepeatAsync(
                repeatMode,
                GetPreferredDeviceId(),
                cancellationToken);
        }
    }

    public async Task SeekAsync(
        int positionMs,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var durationMs = Math.Max(0, _playback.Track?.DurationMs ?? 0);
        var clampedPosition = durationMs > 0
            ? Math.Clamp(positionMs, 0, durationMs)
            : Math.Max(0, positionMs);

        await EnsureApiTokenAsync(forceRefresh: false, cancellationToken: cancellationToken);
        try
        {
            await _apiClient.SeekPlaybackAsync(
                clampedPosition,
                GetPreferredDeviceId(),
                cancellationToken);
        }
        catch (InvalidOperationException exception) when (IsUnauthorized(exception))
        {
            await EnsureApiTokenAsync(forceRefresh: true, cancellationToken: cancellationToken);
            await _apiClient.SeekPlaybackAsync(
                clampedPosition,
                GetPreferredDeviceId(),
                cancellationToken);
        }
    }

    public async Task FadeToAsync(
        int targetVolumePercent,
        TimeSpan duration,
        bool pauseAtEnd,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var snapshot = _playback;
        var currentVolume =
            snapshot.Device?.VolumePercent ?? 100;

        var target = Math.Clamp(
            targetVolumePercent,
            0,
            100);

        var steps = Math.Max(
            1,
            (int)Math.Ceiling(duration.TotalMilliseconds / 150));

        for (var step = 1; step <= steps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var progress = step / (double)steps;
            var volume = (int)Math.Round(
                currentVolume +
                (target - currentVolume) * progress);

            await _apiClient.SetVolumeAsync(
                volume,
                GetPreferredDeviceId(),
                cancellationToken);

            if (step < steps)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(150),
                    cancellationToken);
            }
        }

        if (pauseAtEnd && target == 0)
        {
            await _apiClient.PausePlaybackAsync(
                GetPreferredDeviceId(),
                cancellationToken);
        }

    }

    public async Task<ModuleStatus> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        if (_token is null)
        {
            return new ModuleStatus(
                Id,
                DisplayName,
                ModuleHealth.Ready,
                "Nicht verbunden",
                DateTimeOffset.Now);
        }

        try
        {
            await RefreshPlaybackAsync(cancellationToken);

            var detail = _playback.Track is null
                ? _displayName + " · Pause"
                : _displayName + " · " +
                  (_playback.IsPlaying ? "Spielt: " : "Pause: ") +
                  _playback.Track.Artist + " – " +
                  _playback.Track.Name;

            return new ModuleStatus(
                Id,
                DisplayName,
                ModuleHealth.Connected,
                detail,
                DateTimeOffset.Now);
        }
        catch (Exception exception)
        {
            return new ModuleStatus(
                Id,
                DisplayName,
                ModuleHealth.Degraded,
                exception.Message,
                DateTimeOffset.Now);
        }
    }

    private async Task<SpotifyTokenSet> GetValidTokenAsync(
        string clientId,
        CancellationToken cancellationToken)
    {
        var token = await _tokenRepository.LoadAsync(
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Spotify wurde noch nicht autorisiert.");

        if (token.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return token;
        }

        if (string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            throw new InvalidOperationException(
                "Der Spotify-Token ist abgelaufen. Bitte Spotify neu autorisieren.");
        }

        var refreshed = await _oauthClient.RefreshAsync(
            clientId,
            token.RefreshToken,
            cancellationToken);

        // Spotify liefert beim Refresh nicht immer erneut die Scope-Liste.
        // In diesem Fall müssen die ursprünglich genehmigten Berechtigungen
        // erhalten bleiben, sonst wirkt die Verbindung zwar erfolgreich,
        // Geräte und Playlists bleiben aber leer.
        if (refreshed.Scopes.Count == 0 && token.Scopes.Count > 0)
        {
            refreshed = refreshed with { Scopes = token.Scopes };
        }

        await _tokenRepository.SaveAsync(
            refreshed,
            cancellationToken);

        return refreshed;
    }

    private string? GetPreferredDeviceId()
    {
        return _playback.Device?.Id
            ?? _devices.FirstOrDefault(device => device.IsActive)?.Id
            ?? _devices.FirstOrDefault()?.Id;
    }

    private void EnsureConnected()
    {
        if (_token is null)
        {
            throw new InvalidOperationException(
                "Spotify ist nicht verbunden.");
        }
    }
}
