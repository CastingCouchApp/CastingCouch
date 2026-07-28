namespace CreatorControlSuite.Modules.Overlay.Assets;

/// <summary>
/// Verwaltet importierte Overlay-Bild-Assets unter
/// <c>%LocalAppData%\CreatorControlSuite\Overlay\assets\</c>.
/// </summary>
public interface IOverlayAssetStore
{
    string RootPath { get; }

    IReadOnlyList<OverlayAssetInfo> List();

    bool TryGet(string id, out OverlayAssetInfo asset);

    Task<OverlayAssetInfo> ImportAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
