namespace CreatorControlSuite.Modules.Overlay;

public sealed record OverlayRealtimeEvent(
    string Source,
    string Type,
    DateTimeOffset At,
    string Summary,
    IReadOnlyDictionary<string, string> Data);
