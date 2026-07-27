using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Core.Music;

public sealed class MusicPlayerRouter : IMusicPlayerRouter
{
    private readonly ISettingsStore _settingsStore;
    private readonly Dictionary<string, IMusicPlayer> _players;

    public MusicPlayerRouter(
        ISettingsStore settingsStore,
        IEnumerable<IMusicPlayer> players)
    {
        _settingsStore = settingsStore;
        _players = players.ToDictionary(
            player => MusicProviderIds.Normalize(player.Id),
            player => player,
            StringComparer.OrdinalIgnoreCase);

        if (_players.Count == 0)
        {
            throw new InvalidOperationException("Es ist kein Music-Player registriert.");
        }
    }

    public string ActiveProviderId { get; private set; } = MusicProviderIds.Spotify;

    public string ActiveDisplayName =>
        ActivePlayer.DisplayName;

    public IMusicPlayer ActivePlayer =>
        _players.TryGetValue(ActiveProviderId, out IMusicPlayer? player)
            ? player
            : _players[MusicProviderIds.Spotify];

    public IReadOnlyList<IMusicPlayer> Players =>
        [.. _players.Values.OrderBy(player => player.DisplayName, StringComparer.OrdinalIgnoreCase)];

    public event EventHandler? ActiveProviderChanged;
    public event EventHandler? SnapshotChanged;

    public async Task ApplyProviderAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        string normalized = MusicProviderIds.Normalize(providerId);
        if (!_players.ContainsKey(normalized))
        {
            throw new InvalidOperationException($"Unbekannter Music-Provider: {providerId}");
        }

        if (string.Equals(ActiveProviderId, normalized, StringComparison.OrdinalIgnoreCase))
        {
            await EnsureExclusiveAsync(cancellationToken);
            return;
        }

        IMusicPlayer previous = ActivePlayer;
        ActiveProviderId = normalized;

        try
        {
            await previous.DisconnectAsync(cancellationToken);
        }
        catch
        {
            // Provider-Wechsel soll nicht an Disconnect-Fehlern scheitern.
        }

        await EnsureExclusiveAsync(cancellationToken);
        ActiveProviderChanged?.Invoke(this, EventArgs.Empty);
        NotifySnapshotChanged();
    }

    public async Task RefreshFromSettingsAsync(CancellationToken cancellationToken = default)
    {
        AppSettings settings = await _settingsStore.LoadAsync(cancellationToken);
        string providerId = settings.MusicPlayer?.ProviderId ?? MusicProviderIds.Spotify;
        await ApplyProviderAsync(providerId, cancellationToken);
    }

    public Task<NowPlayingSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        => ActivePlayer.GetSnapshotAsync(cancellationToken);

    public Task PlayAsync(CancellationToken cancellationToken = default)
        => ActivePlayer.PlayAsync(cancellationToken);

    public Task PauseAsync(CancellationToken cancellationToken = default)
        => ActivePlayer.PauseAsync(cancellationToken);

    public Task PlayPauseAsync(CancellationToken cancellationToken = default)
        => ActivePlayer.PlayPauseAsync(cancellationToken);

    public Task NextAsync(CancellationToken cancellationToken = default)
        => ActivePlayer.NextAsync(cancellationToken);

    public Task PreviousAsync(CancellationToken cancellationToken = default)
        => ActivePlayer.PreviousAsync(cancellationToken);

    public Task SeekAsync(int positionMs, CancellationToken cancellationToken = default)
        => ActivePlayer.SeekAsync(positionMs, cancellationToken);

    public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
        => ActivePlayer.SetVolumeAsync(volumePercent, cancellationToken);

    public Task ConnectActiveAsync(CancellationToken cancellationToken = default)
        => ActivePlayer.ConnectAsync(cancellationToken);

    public Task DisconnectActiveAsync(CancellationToken cancellationToken = default)
        => ActivePlayer.DisconnectAsync(cancellationToken);

    public void NotifySnapshotChanged()
        => SnapshotChanged?.Invoke(this, EventArgs.Empty);

    private async Task EnsureExclusiveAsync(CancellationToken cancellationToken)
    {
        foreach (IMusicPlayer player in _players.Values)
        {
            if (string.Equals(player.Id, ActiveProviderId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                await player.DisconnectAsync(cancellationToken);
            }
            catch
            {
                // Inaktive Provider hart trennen; Fehler ignorieren.
            }
        }
    }
}
