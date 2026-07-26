using CreatorControlSuite.Modules.Overlay.Models;

namespace CreatorControlSuite.Modules.Overlay;

public interface IOverlayDataService
{
    OverlayData Current { get; }

    event EventHandler<OverlayData>? DataChanged;

    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task UpdateAsync(
        Action<OverlayData> update,
        CancellationToken cancellationToken = default);

    Task WriteAsync(CancellationToken cancellationToken = default);
    Task<string> GetDataFilePathAsync(CancellationToken cancellationToken = default);
    Task<string> GetOverlayRootAsync(CancellationToken cancellationToken = default);
    Task InstallBundledOverlayAsync(CancellationToken cancellationToken = default);
}
