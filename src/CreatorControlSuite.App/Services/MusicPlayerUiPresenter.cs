using CreatorControlSuite.App.Core.Eventing;
using CreatorControlSuite.Core.Eventing;
using CreatorControlSuite.Core.Music;

namespace CreatorControlSuite.App.Services;

public sealed class MusicPlayerUiPresenter : IMusicPlayerUiPresenter
{
    private readonly IMusicPlayerRouter _router;
    private readonly IEventBus _eventBus;

    public MusicPlayerUiPresenter(IMusicPlayerRouter router, IEventBus eventBus)
    {
        _router = router;
        _eventBus = eventBus;
    }

    public async Task<MusicPlayerUiState> GetStateAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _router.GetSnapshotAsync(cancellationToken);
        var trackLabel = string.IsNullOrWhiteSpace(snapshot.Title)
            ? "Kein Titel"
            : string.IsNullOrWhiteSpace(snapshot.Artist)
                ? snapshot.Title
                : $"{snapshot.Artist} – {snapshot.Title}";

        var state = new MusicPlayerUiState(
            Title: snapshot.Title,
            Artist: snapshot.Artist,
            Album: snapshot.Album,
            IsPlaying: snapshot.IsPlaying,
            Connected: snapshot.Connected,
            ProviderId: snapshot.ProviderId,
            PositionMs: snapshot.ProgressMs,
            DurationMs: snapshot.DurationMs,
            VolumePercent: snapshot.VolumePercent,
            StatusText: snapshot.StatusText,
            CoverUrl: string.IsNullOrWhiteSpace(snapshot.CoverUrl) ? null : snapshot.CoverUrl,
            TrackLabel: trackLabel);

        _eventBus.Publish(new MusicSnapshotUpdated(state, DateTimeOffset.Now));
        return state;
    }
}
