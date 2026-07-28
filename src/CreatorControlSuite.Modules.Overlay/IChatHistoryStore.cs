namespace CreatorControlSuite.Modules.Overlay;

public interface IChatHistoryStore
{
    string FilePath { get; }

    Task<IReadOnlyList<OverlayRealtimeEvent>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        IReadOnlyList<OverlayRealtimeEvent> events,
        CancellationToken cancellationToken = default);
}
