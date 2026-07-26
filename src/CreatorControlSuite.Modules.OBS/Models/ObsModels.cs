namespace CreatorControlSuite.Modules.OBS.Models;

public sealed record ObsConnectionOptions(
    string Host,
    int Port,
    string Password,
    TimeSpan ConnectTimeout,
    TimeSpan RequestTimeout);

public sealed record ObsServerInfo(
    string ObsVersion,
    string WebSocketVersion,
    int RpcVersion,
    string Platform,
    string PlatformDescription);

public sealed record ObsSceneInfo(
    string Name,
    int Index);

public sealed record ObsInputInfo(
    string Name,
    string Kind,
    string UnversionedKind);

public sealed record ObsTransitionInfo(
    string Name,
    string Kind,
    bool Configurable);

public sealed record ObsInputAudioState(
    string Name,
    bool Muted,
    double VolumeDb);

public sealed record ObsInputAdvancedAudioState(
    string Name,
    string MonitorType,
    int SyncOffsetMilliseconds);

public sealed record ObsInputVolumeMeter(
    string InputName,
    double MagnitudeDb,
    double PeakDb,
    double InputPeakDb);

public sealed record ObsStreamStatus(
    bool OutputActive,
    bool OutputReconnecting,
    string OutputTimecode,
    long OutputDuration,
    long OutputBytes,
    int OutputSkippedFrames,
    int OutputTotalFrames);

public sealed record ObsOutputStatus(
    bool Active,
    bool Paused,
    string Timecode,
    long Duration,
    long Bytes);

public sealed record ObsStats(
    double CpuUsage,
    double MemoryUsage,
    double AvailableDiskSpace,
    double ActiveFps,
    double AverageFrameRenderTime,
    int RenderSkippedFrames,
    int RenderTotalFrames,
    int OutputSkippedFrames,
    int OutputTotalFrames);

public sealed record ObsSnapshot(
    bool Connected,
    string CurrentProgramScene,
    string CurrentPreviewScene,
    IReadOnlyList<ObsSceneInfo> Scenes,
    IReadOnlyList<ObsInputInfo> Inputs,
    ObsServerInfo? Server,
    ObsStreamStatus? Stream);


public sealed record ObsSceneItemInfo(
    int ItemId,
    int Index,
    string SourceName,
    string SourceType,
    bool Enabled,
    bool Locked,
    bool IsGroup);

public sealed record ObsSourceFilterInfo(
    string Name,
    string Kind,
    bool Enabled,
    int Index);

public sealed record ObsSceneItemTransformInfo(
    double PositionX,
    double PositionY,
    double Width,
    double Height,
    double Rotation,
    int CropLeft,
    int CropTop,
    int CropRight,
    int CropBottom);
