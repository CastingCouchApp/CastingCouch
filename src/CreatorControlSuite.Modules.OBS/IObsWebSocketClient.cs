using CreatorControlSuite.Modules.OBS.Models;

namespace CreatorControlSuite.Modules.OBS;

public interface IObsWebSocketClient : IAsyncDisposable
{
    bool IsConnected { get; }

    event EventHandler<bool>? ConnectionStateChanged;
    event EventHandler<string>? CurrentProgramSceneChanged;
    event EventHandler? SceneCollectionChanged;
    event EventHandler? SceneItemsChanged;
    event EventHandler? InputsChanged;
    event EventHandler<IReadOnlyList<ObsInputVolumeMeter>>? InputVolumeMeters;

    Task ConnectAsync(
        ObsConnectionOptions options,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<ObsServerInfo> GetVersionAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ObsSceneInfo>> GetSceneListAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ObsInputInfo>> GetInputListAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ObsTransitionInfo>> GetSceneTransitionListAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ObsSceneItemInfo>> GetSceneItemListAsync(
        string sceneName,
        CancellationToken cancellationToken = default);


    Task<ObsInputAudioState> GetInputAudioStateAsync(
        string inputName,
        CancellationToken cancellationToken = default);

    Task SetInputMuteAsync(
        string inputName,
        bool muted,
        CancellationToken cancellationToken = default);

    Task SetInputVolumeDbAsync(
        string inputName,
        double volumeDb,
        CancellationToken cancellationToken = default);

    Task<ObsInputAdvancedAudioState> GetInputAdvancedAudioStateAsync(
        string inputName,
        CancellationToken cancellationToken = default);

    Task SetInputAudioMonitorTypeAsync(
        string inputName,
        string monitorType,
        CancellationToken cancellationToken = default);

    Task SetInputAudioSyncOffsetAsync(
        string inputName,
        int syncOffsetMilliseconds,
        CancellationToken cancellationToken = default);

    Task<string> GetCurrentProgramSceneAsync(
        CancellationToken cancellationToken = default);

    Task SetCurrentProgramSceneAsync(
        string sceneName,
        CancellationToken cancellationToken = default);

    Task SetCurrentSceneTransitionAsync(
        string transitionName,
        CancellationToken cancellationToken = default);

    Task SetCurrentSceneTransitionDurationAsync(
        int transitionDurationMilliseconds,
        CancellationToken cancellationToken = default);

    Task<ObsStreamStatus> GetStreamStatusAsync(
        CancellationToken cancellationToken = default);

    Task<ObsStats> GetStatsAsync(
        CancellationToken cancellationToken = default);

    Task StartStreamAsync(CancellationToken cancellationToken = default);
    Task StopStreamAsync(CancellationToken cancellationToken = default);

    Task<ObsOutputStatus> GetRecordStatusAsync(CancellationToken cancellationToken = default);
    Task StartRecordAsync(CancellationToken cancellationToken = default);
    Task StopRecordAsync(CancellationToken cancellationToken = default);
    Task PauseRecordAsync(CancellationToken cancellationToken = default);
    Task ResumeRecordAsync(CancellationToken cancellationToken = default);

    Task<ObsOutputStatus> GetReplayBufferStatusAsync(CancellationToken cancellationToken = default);
    Task StartReplayBufferAsync(CancellationToken cancellationToken = default);
    Task StopReplayBufferAsync(CancellationToken cancellationToken = default);
    Task SaveReplayBufferAsync(CancellationToken cancellationToken = default);

    Task<bool> GetVirtualCamStatusAsync(CancellationToken cancellationToken = default);
    Task StartVirtualCamAsync(CancellationToken cancellationToken = default);
    Task StopVirtualCamAsync(CancellationToken cancellationToken = default);

    Task<ObsSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default);

    Task<bool> InputExistsAsync(
        string inputName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, System.Text.Json.JsonElement>> GetInputSettingsAsync(
        string inputName,
        CancellationToken cancellationToken = default);

    Task<bool> SceneItemExistsAsync(
        string sceneName,
        string sourceName,
        CancellationToken cancellationToken = default);

    Task EnsureSceneAsync(
        string sceneName,
        CancellationToken cancellationToken = default);

    Task CreateInputAsync(
        string sceneName,
        string inputName,
        string inputKind,
        object inputSettings,
        bool sceneItemEnabled,
        CancellationToken cancellationToken = default);

    Task CreateSceneItemAsync(
        string sceneName,
        string sourceName,
        bool enabled,
        CancellationToken cancellationToken = default);

    Task EnsureMediaInputAsync(
        string sceneName,
        string inputName,
        string localFile,
        CancellationToken cancellationToken = default);

    Task EnsureTextInputAsync(
        string sceneName,
        string inputName,
        string text,
        string fontFace,
        int fontSize,
        string fontColor,
        CancellationToken cancellationToken = default);

    Task SetInputSettingsAsync(
        string inputName,
        object inputSettings,
        bool overlay,
        CancellationToken cancellationToken = default);

    Task RestartMediaInputAsync(
        string inputName,
        CancellationToken cancellationToken = default);

    Task StopMediaInputAsync(
        string inputName,
        CancellationToken cancellationToken = default);

    Task PressInputPropertiesButtonAsync(
        string inputName,
        string propertyName,
        CancellationToken cancellationToken = default);

    Task SetSceneItemEnabledAsync(
        string sceneName,
        string sourceName,
        bool enabled,
        CancellationToken cancellationToken = default);

    Task SetSceneItemLockedAsync(
        string sceneName,
        string sourceName,
        bool locked,
        CancellationToken cancellationToken = default);

    Task SetSceneItemIndexAsync(
        string sceneName,
        string sourceName,
        int sceneItemIndex,
        CancellationToken cancellationToken = default);

    Task SetSceneItemTransformAsync(
        string sceneName,
        string sourceName,
        double x,
        double y,
        double width,
        double height,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ObsSourceFilterInfo>> GetSourceFilterListAsync(
        string sourceName,
        CancellationToken cancellationToken = default);

    Task SetSourceFilterEnabledAsync(
        string sourceName,
        string filterName,
        bool enabled,
        CancellationToken cancellationToken = default);

    Task<ObsSceneItemTransformInfo> GetSceneItemTransformAsync(
        string sceneName,
        string sourceName,
        CancellationToken cancellationToken = default);

    Task ResetSceneItemTransformAsync(
        string sceneName,
        string sourceName,
        CancellationToken cancellationToken = default);

    Task SetSceneItemDetailedTransformAsync(
        string sceneName,
        string sourceName,
        double x,
        double y,
        double width,
        double height,
        double rotation,
        int cropLeft,
        int cropTop,
        int cropRight,
        int cropBottom,
        CancellationToken cancellationToken = default);
    Task<(string CurrentProfile, IReadOnlyList<string> Profiles)> GetProfileListAsync(CancellationToken cancellationToken = default);
    Task SetCurrentProfileAsync(string profileName, CancellationToken cancellationToken = default);
    Task<(string CurrentSceneCollection, IReadOnlyList<string> SceneCollections)> GetSceneCollectionListAsync(CancellationToken cancellationToken = default);
    Task SetCurrentSceneCollectionAsync(string sceneCollectionName, CancellationToken cancellationToken = default);

    Task<byte[]> GetSourceScreenshotAsync(
        string sourceName,
        int imageWidth = 640,
        int? imageHeight = 360,
        CancellationToken cancellationToken = default);

}
