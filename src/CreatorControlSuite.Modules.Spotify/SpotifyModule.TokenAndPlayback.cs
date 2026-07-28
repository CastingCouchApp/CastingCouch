using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Modules.Spotify.Models;

namespace CreatorControlSuite.Modules.Spotify;

public sealed partial class SpotifyModule
{
    private async Task<SpotifyTokenSet> GetValidTokenAsync(
        string clientId,
        CancellationToken cancellationToken)
    {
        SpotifyTokenSet token = await _tokenRepository.LoadAsync(
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

        SpotifyTokenSet refreshed = await _oauthClient.RefreshAsync(
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

    private async Task ExecutePlayerCommandAsync(
        Func<string?, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        EnsureConnected();
        await EnsureApiTokenAsync(forceRefresh: false, cancellationToken: cancellationToken);
        string? deviceId = await ResolveControlDeviceIdAsync(cancellationToken);
        try
        {
            await action(deviceId, cancellationToken);
        }
        catch (InvalidOperationException exception) when (IsUnauthorized(exception))
        {
            await EnsureApiTokenAsync(forceRefresh: true, cancellationToken: cancellationToken);
            deviceId = await ResolveControlDeviceIdAsync(cancellationToken);
            await action(deviceId, cancellationToken);
        }
    }

    private async Task<string?> ResolveControlDeviceIdAsync(CancellationToken cancellationToken)
    {
        AppSettings settings = await _settingsStore.LoadAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(settings.Spotify.PreferredDeviceId))
        {
            return settings.Spotify.PreferredDeviceId;
        }

        return GetRuntimeDeviceId();
    }

    private string? GetRuntimeDeviceId()
    {
        return _playback.Device?.Id
            ?? _devices.FirstOrDefault(device => device.IsActive)?.Id
            ?? _devices.FirstOrDefault()?.Id;
    }

    private void PatchPlaybackIsPlaying(bool isPlaying)
    {
        _playback = _playback with { IsPlaying = isPlaying, HasPlayback = true };
        if (isPlaying || _playback.Track is not null)
        {
            _lastValidPlaybackAt = DateTimeOffset.UtcNow;
            _consecutiveEmptyPlaybackSnapshots = 0;
        }
    }

    private void PatchPlaybackVolume(int volumePercent)
    {
        int clamped = Math.Clamp(volumePercent, 0, 100);
        if (_playback.Device is null)
        {
            return;
        }

        _playback = _playback with
        {
            Device = _playback.Device with { VolumePercent = clamped }
        };
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
