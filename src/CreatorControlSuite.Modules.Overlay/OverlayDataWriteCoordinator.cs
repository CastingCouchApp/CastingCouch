namespace CreatorControlSuite.Modules.Overlay;

/// <summary>
/// Prozessweite Schreibsperre für die gemeinsame overlay-data.json.
/// Alle Module und UI-Routinen müssen dieselbe Sperre verwenden, damit
/// parallele Read-Modify-Write-Vorgänge keine Datenbereiche zurücksetzen.
/// </summary>
public static class OverlayDataWriteCoordinator
{
    public static SemaphoreSlim Lock { get; } = new(1, 1);
}
