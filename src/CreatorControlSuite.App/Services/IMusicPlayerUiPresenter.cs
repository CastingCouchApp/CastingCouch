namespace CreatorControlSuite.App.Services;

public sealed record MusicPlayerUiState(
    string Title,
    string Artist,
    string Album,
    bool IsPlaying,
    bool Connected,
    string ProviderId,
    int PositionMs,
    int DurationMs,
    int? VolumePercent,
    string StatusText,
    string? CoverUrl,
    string TrackLabel);

public interface IMusicPlayerUiPresenter
{
    Task<MusicPlayerUiState> GetStateAsync(CancellationToken cancellationToken = default);
}
