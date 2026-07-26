namespace CreatorControlSuite.Modules.Alerts.Models;

public sealed record AlertRequest(
    Guid Id,
    string Type,
    string User,
    IReadOnlyDictionary<string, string> Variables,
    DateTimeOffset CreatedAt,
    int Priority);

public sealed record AlertDefinition(
    string Type,
    bool Enabled,
    string TextTemplate,
    string MediaPath,
    string SoundPath,
    TimeSpan Duration,
    int Priority,
    string FontFace,
    int FontSize,
    string FontColor,
    string Animation,
    int X,
    int Y,
    int Width,
    int Height,
    int VolumePercent);

public sealed record AlertPlaybackState(
    bool IsRunning,
    AlertRequest? Current,
    int QueueLength,
    DateTimeOffset? StartedAt,
    string Detail);

public sealed record AlertPreview(
    string Type,
    string Text,
    string MediaPath,
    string SoundPath,
    TimeSpan Duration,
    string Animation,
    string FontFace,
    int FontSize,
    string FontColor);
