using CreatorControlSuite.Modules.Overlay.Models;

namespace CreatorControlSuite.Modules.Overlay;

public interface IOverlayLayoutStore
{
    Task<OverlayLayout> LoadAsync(string instanceId, CancellationToken cancellationToken = default);
    Task SaveAsync(string instanceId, OverlayLayout layout, CancellationToken cancellationToken = default);
    string GetLayoutFilePath(string instanceId);
    bool Exists(string instanceId);
    IReadOnlyList<string> ListInstanceIds();
    Task DeleteAsync(string instanceId, CancellationToken cancellationToken = default);
    Task DuplicateAsync(string sourceId, string targetId, CancellationToken cancellationToken = default);
}
