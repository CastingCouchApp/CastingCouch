namespace CreatorControlSuite.Modules.Overlay.Extensions;

/// <summary>
/// Verwaltet installierte Overlay-Extension-Packs unter
/// <c>%LocalAppData%\CreatorControlSuite\Overlay\extensions\{packId}\</c>.
/// </summary>
public interface IOverlayExtensionStore
{
    string RootPath { get; }

    /// <summary>
    /// Extrahiert ein ZIP-Paket (mit Zip-Slip-Schutz und Dateityp-Allowlist), validiert
    /// dessen <c>manifest.json</c> und installiert es unter der Id aus dem Manifest.
    /// Ein bereits installiertes Pack mit derselben Id wird ersetzt.
    /// </summary>
    Task<OverlayExtensionPackSummary> InstallFromZipAsync(Stream zipStream, CancellationToken cancellationToken = default);

    Task UninstallAsync(string packId, CancellationToken cancellationToken = default);

    /// <summary>Listet alle installierten Packs anhand ihrer <c>manifest.json</c>.</summary>
    IReadOnlyList<OverlayExtensionPackSummary> ListCatalog();

    /// <summary>
    /// Löst einen relativen Pfad innerhalb eines installierten Packs sicher auf.
    /// Liefert <c>false</c> bei unbekannter Pack-Id, Pfad-Traversal oder fehlender Datei.
    /// </summary>
    bool TryResolveFile(string packId, string relativePath, out string fullPath);
}
