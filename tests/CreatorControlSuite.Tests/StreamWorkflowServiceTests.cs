using System.Text.Json;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Security;
using CreatorControlSuite.Modules.Alerts;
using CreatorControlSuite.Modules.Alerts.Models;
using CreatorControlSuite.Modules.OBS;
using CreatorControlSuite.Modules.OBS.Models;
using CreatorControlSuite.Modules.Overlay;
using CreatorControlSuite.Modules.Overlay.Models;
using CreatorControlSuite.Modules.Spotify;
using CreatorControlSuite.Modules.Spotify.Models;
using CreatorControlSuite.Modules.Workflow;
using CreatorControlSuite.Modules.Workflow.Models;

namespace CreatorControlSuite.Tests;

public sealed class StreamWorkflowServiceTests
{
    [Fact]
    public async Task PrepareAsync_SetsPreparingPhaseAndOverlay()
    {
        Harness harness = CreateHarness();

        await harness.Service.PrepareAsync();

        Assert.Equal(StreamPhase.Preparing, harness.Service.State.Phase);
        Assert.Equal("Start", harness.Service.State.CurrentScene);
        Assert.Equal(StreamPhase.Preparing.ToString(), harness.Overlay.Current.Stream.Phase);
        Assert.False(harness.Overlay.Current.Stream.IsLive);
        Assert.NotNull(harness.Service.SessionStats.StartedAt);
        Assert.Equal(1, harness.Overlay.ClearChatCalls);
    }

    [Fact]
    public async Task GoLiveAsync_SetsLivePhaseAndSwitchesScene()
    {
        Harness harness = CreateHarness(settings =>
        {
            settings.Workflow.AutoSwitchScenes = true;
        });

        await harness.Service.GoLiveAsync();

        Assert.Equal(StreamPhase.Live, harness.Service.State.Phase);
        Assert.Equal("Game", harness.Service.State.CurrentScene);
        Assert.True(harness.Overlay.Current.Stream.IsLive);
        Assert.Equal(StreamPhase.Live.ToString(), harness.Overlay.Current.Stream.Phase);
        Assert.Contains("Game", harness.Obs.ProgramScenes);
    }

    [Fact]
    public async Task StartCountdownAsync_WritesOverlayCountdownState()
    {
        Harness harness = CreateHarness(settings =>
        {
            settings.Workflow.StartCountdownSeconds = 2;
            settings.Workflow.AutoSwitchScenes = false;
            settings.Workflow.AutoStartObsStream = false;
            settings.Workflow.AutoFadeSpotifyOnLive = false;
        });

        Task countdown = harness.Service.StartCountdownAsync();
        await Task.Delay(50);

        Assert.True(harness.Overlay.Current.Countdown.IsRunning);
        Assert.Equal(2, harness.Overlay.Current.Countdown.TotalSeconds);
        Assert.True(harness.Overlay.Current.Countdown.RemainingSeconds >= 0);
        Assert.NotNull(harness.Overlay.Current.Countdown.EndsAt);
        Assert.Equal("stream-start", harness.Overlay.Current.Countdown.Mode);

        await countdown;

        Assert.Equal(StreamPhase.Live, harness.Service.State.Phase);
        Assert.False(harness.Overlay.Current.Countdown.IsRunning);
        Assert.Equal(0, harness.Overlay.Current.Countdown.RemainingSeconds);
    }

    [Fact]
    public async Task StopCountdownAsync_ClearsOverlayCountdownWithoutGoingLive()
    {
        Harness harness = CreateHarness(settings =>
        {
            settings.Workflow.StartCountdownSeconds = 30;
            settings.Workflow.AutoSwitchScenes = false;
            settings.Workflow.AutoStartObsStream = false;
            settings.Workflow.AutoFadeSpotifyOnLive = false;
        });

        Task countdown = harness.Service.StartCountdownAsync();
        await Task.Delay(80);
        Assert.Equal(StreamPhase.Countdown, harness.Service.State.Phase);

        await harness.Service.StopCountdownAsync();
        try
        {
            await countdown;
        }
        catch (OperationCanceledException)
        {
        }

        Assert.NotEqual(StreamPhase.Live, harness.Service.State.Phase);
        Assert.False(harness.Overlay.Current.Countdown.IsRunning);
        Assert.Equal(0, harness.Overlay.Current.Countdown.RemainingSeconds);
        Assert.Null(harness.Overlay.Current.Countdown.EndsAt);
    }

    [Fact]
    public async Task PauseAsync_Then_ResumeAsync_Transitions()
    {
        Harness harness = CreateHarness(settings =>
        {
            settings.Workflow.AutoSwitchScenes = true;
        });

        await harness.Service.GoLiveAsync();
        await harness.Service.PauseAsync();

        Assert.Equal(StreamPhase.Paused, harness.Service.State.Phase);
        Assert.Equal("Pause", harness.Service.State.CurrentScene);
        Assert.Contains("Pause", harness.Obs.ProgramScenes);

        await harness.Service.ResumeAsync();

        Assert.Equal(StreamPhase.Live, harness.Service.State.Phase);
        Assert.Equal("Game", harness.Service.State.CurrentScene);
        Assert.Equal(StreamPhase.Live.ToString(), harness.Overlay.Current.Stream.Phase);
    }

    [Fact]
    public async Task EndAsync_StopsAlertsAndCompletes()
    {
        Harness harness = CreateHarness(settings =>
        {
            settings.Workflow.AutoSwitchScenes = true;
            settings.Workflow.EndSceneSeconds = 1;
        });

        await harness.Service.GoLiveAsync();
        await harness.Service.EndAsync();

        Assert.Equal(StreamPhase.Completed, harness.Service.State.Phase);
        Assert.Equal("Ende", harness.Service.State.CurrentScene);
        Assert.Equal(StreamPhase.Completed.ToString(), harness.Overlay.Current.Stream.Phase);
        Assert.False(harness.Overlay.Current.Stream.IsLive);
        Assert.True(harness.Alerts.StopCurrentCalls >= 1);
        Assert.True(harness.Alerts.ClearQueueCalls >= 1);
        Assert.NotNull(harness.Service.SessionStats.EndedAt);
    }

    [Theory]
    [InlineData("channel.subscribe", 1, 0)]
    [InlineData("channel.cheer", 0, 42)]
    public async Task RegisterTwitchEventAsync_IncrementsStats(
        string eventType,
        int expectedSubs,
        int expectedBits)
    {
        Harness harness = CreateHarness();
        int count = eventType == "channel.cheer" ? 42 : 1;

        await harness.Service.RegisterTwitchEventAsync(eventType, count);

        Assert.Equal(expectedSubs, harness.Service.SessionStats.NewSubscriptions);
        Assert.Equal(expectedBits, harness.Service.SessionStats.BitsCheered);
        Assert.Equal(expectedSubs, harness.Overlay.Current.Stats.NewSubscriptions);
        Assert.Equal(expectedBits, harness.Overlay.Current.Stats.BitsCheered);
    }

    [Fact]
    public async Task ResetAsync_ClearsStatsAndReturnsIdle()
    {
        Harness harness = CreateHarness();

        await harness.Service.PrepareAsync();
        await harness.Service.RegisterTwitchEventAsync("channel.subscribe");
        await harness.Service.ResetAsync();

        Assert.Equal(StreamPhase.Idle, harness.Service.State.Phase);
        Assert.Null(harness.Service.SessionStats.StartedAt);
        Assert.Equal(0, harness.Service.SessionStats.NewSubscriptions);
        Assert.Equal("Idle", harness.Overlay.Current.Stream.Phase);
    }

    [Fact]
    public async Task ResetSessionStatsAsync_ClearsChatForObservedStreamStart()
    {
        Harness harness = CreateHarness();

        await harness.Service.ResetSessionStatsAsync();

        Assert.Equal(1, harness.Overlay.ClearChatCalls);
    }

    private static Harness CreateHarness(Action<AppSettings>? configure = null)
    {
        var settings = new AppSettings();
        settings.Workflow.ExportSessionReport = false;
        settings.Workflow.AutoFadeSpotifyOnLive = false;
        settings.Workflow.AutoStartObsStream = false;
        settings.Workflow.AutoStopObsStream = false;
        settings.Workflow.EndSceneSeconds = 1;
        settings.Workflow.StartCountdownSeconds = 0;
        configure?.Invoke(settings);

        var store = new InMemorySettingsStore(settings);
        var obs = new FakeObsWebSocketClient();
        var overlay = new FakeWorkflowOverlayCapability();
        var alerts = new FakeAlertEngine();

        var service = new StreamWorkflowService(
            store,
            new FakeWorkflowObsCapability(obs),
            new FakeWorkflowMusicCapability(),
            new FakeWorkflowAlertCapability(alerts),
            overlay);

        return new Harness(service, obs, overlay, alerts);
    }

    private sealed record Harness(
        StreamWorkflowService Service,
        FakeObsWebSocketClient Obs,
        FakeWorkflowOverlayCapability Overlay,
        FakeAlertEngine Alerts);

    private sealed class InMemorySettingsStore(AppSettings settings) : ISettingsStore
    {
        private AppSettings _settings = settings;

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_settings);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class MemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _values = [];

        public Task SaveAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> LoadAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.TryGetValue(key, out string? value);
            return Task.FromResult(value);
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeWorkflowObsCapability(
        FakeObsWebSocketClient obs) : IWorkflowObsCapability
    {
        public bool IsConnected => obs.IsConnected;

        public Task SetSceneAsync(
            string sceneName,
            CancellationToken cancellationToken) =>
            obs.SetCurrentProgramSceneAsync(sceneName, cancellationToken);

        public async Task<bool> IsStreamActiveAsync(
            CancellationToken cancellationToken) =>
            (await obs.GetStreamStatusAsync(cancellationToken)).OutputActive;

        public Task StartStreamAsync(CancellationToken cancellationToken) =>
            obs.StartStreamAsync(cancellationToken);

        public Task StopStreamAsync(CancellationToken cancellationToken) =>
            obs.StopStreamAsync(cancellationToken);
    }

    private sealed class FakeWorkflowMusicCapability : IWorkflowMusicCapability
    {
        public Task FadeToAsync(
            int targetVolumePercent,
            TimeSpan duration,
            bool pauseAfterFade,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeWorkflowAlertCapability(
        FakeAlertEngine alerts) : IWorkflowAlertCapability
    {
        public async Task StopAndClearAsync(CancellationToken cancellationToken)
        {
            await alerts.StopCurrentAsync(cancellationToken);
            await alerts.ClearQueueAsync(cancellationToken);
        }
    }

    private sealed class FakeWorkflowOverlayCapability : IWorkflowOverlayCapability
    {
        public WorkflowOverlayData Current { get; private set; } = new();
        public int ClearChatCalls { get; private set; }

        public Task ClearChatAsync(CancellationToken cancellationToken)
        {
            ClearChatCalls++;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(
            Action<WorkflowOverlayData> update,
            CancellationToken cancellationToken)
        {
            update(Current);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOverlayDataService : IOverlayDataService
    {
        public OverlayData Current { get; private set; } = new();

        public event EventHandler<OverlayData>? DataChanged
        {
            add { }
            remove { }
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UpdateAsync(
            Action<OverlayData> update,
            CancellationToken cancellationToken = default)
        {
            update(Current);
            Current.UpdatedAt = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }

        public Task WriteAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<string> GetDataFilePathAsync(CancellationToken cancellationToken = default)
            => Task.FromResult("overlay-data.json");

        public Task<string> GetOverlayRootAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(".");
    }

    private sealed class FakeAlertEngine : IAlertEngine
    {
        public int StopCurrentCalls { get; private set; }
        public int ClearQueueCalls { get; private set; }

        public event EventHandler<AlertPlaybackState>? StateChanged
        {
            add { }
            remove { }
        }

        public AlertPlaybackState State { get; private set; } =
            new(false, null, 0, null, "Gestoppt");

        public Task StartAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task EnqueueAsync(
            AlertRequest request,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ClearQueueAsync(CancellationToken cancellationToken = default)
        {
            ClearQueueCalls++;
            return Task.CompletedTask;
        }

        public Task StopCurrentAsync(CancellationToken cancellationToken = default)
        {
            StopCurrentCalls++;
            return Task.CompletedTask;
        }

        public Task<AlertPreview> BuildPreviewAsync(
            string type,
            string user,
            IReadOnlyDictionary<string, string>? variables = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                new AlertPreview(type, user, "", "", TimeSpan.FromSeconds(1), "Fade", "Segoe UI", 44, "#FFFFFF"));

        public Task InstallObsSourcesAsync(
            string type,
            string user,
            IReadOnlyDictionary<string, string>? variables = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubSpotifyOAuthClient : ISpotifyOAuthClient
    {
        public Task<SpotifyTokenSet> AuthorizeAsync(
            string clientId,
            string redirectUri,
            IReadOnlyCollection<string> scopes,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<SpotifyTokenSet> RefreshAsync(
            string clientId,
            string refreshToken,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class StubSpotifyApiClient : ISpotifyApiClient
    {
        public void Configure(string accessToken)
        {
        }

        public Task<string> GetCurrentUserDisplayNameAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult("");

        public Task<IReadOnlyList<SpotifyDevice>> GetDevicesAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SpotifyDevice>>([]);

        public Task<SpotifyPlaybackState> GetPlaybackStateAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(new SpotifyPlaybackState(false, false, false, "off", 0, null, null, ""));

        public Task<SpotifyQueue> GetQueueAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(new SpotifyQueue(null, []));

        public Task<IReadOnlyList<SpotifyRecentlyPlayedItem>> GetRecentlyPlayedAsync(
            int limit = 20,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SpotifyRecentlyPlayedItem>>([]);

        public Task<IReadOnlyList<SpotifyTrack>> SearchTracksAsync(
            string query,
            int limit = 20,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SpotifyTrack>>([]);

        public Task<IReadOnlyList<SpotifyTrack>> GetSavedTracksAsync(
            int limit = 50,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SpotifyTrack>>([]);

        public Task<bool> IsTrackSavedAsync(
            string trackId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task SaveTrackAsync(
            string trackId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveSavedTrackAsync(
            string trackId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task AddToQueueAsync(
            string trackUri,
            string? deviceId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<SpotifyPlaylist>> GetCurrentUserPlaylistsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SpotifyPlaylist>>([]);

        public Task<IReadOnlyList<SpotifyTrack>> GetPlaylistTracksAsync(
            string playlistId,
            int limit = 50,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SpotifyTrack>>([]);

        public Task TransferPlaybackAsync(
            string deviceId,
            bool play,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StartPlaybackAsync(
            string? deviceId,
            string? contextUri,
            string? offsetTrackUri = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task PlayTrackAsync(
            string trackUri,
            string? deviceId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task PausePlaybackAsync(
            string? deviceId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetVolumeAsync(
            int volumePercent,
            string? deviceId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetShuffleAsync(
            bool enabled,
            string? deviceId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetRepeatAsync(
            string repeatMode,
            string? deviceId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SeekPlaybackAsync(
            int positionMs,
            string? deviceId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SkipNextAsync(
            string? deviceId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SkipPreviousAsync(
            string? deviceId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeObsWebSocketClient : IObsWebSocketClient
    {
        public bool IsConnected { get; set; } = true;
        public List<string> ProgramScenes { get; } = [];

        public event EventHandler<bool>? ConnectionStateChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<string>? CurrentProgramSceneChanged
        {
            add { }
            remove { }
        }

        public event EventHandler? SceneCollectionChanged
        {
            add { }
            remove { }
        }

        public event EventHandler? SceneItemsChanged
        {
            add { }
            remove { }
        }

        public event EventHandler? InputsChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<IReadOnlyList<ObsInputVolumeMeter>>? InputVolumeMeters
        {
            add { }
            remove { }
        }

        public Task ConnectAsync(
            ObsConnectionOptions options,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ObsServerInfo> GetVersionAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ObsServerInfo("", "", 0, "", ""));

        public Task<IReadOnlyList<ObsSceneInfo>> GetSceneListAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ObsSceneInfo>>([]);

        public Task<IReadOnlyList<ObsInputInfo>> GetInputListAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ObsInputInfo>>([]);

        public Task<IReadOnlyList<ObsTransitionInfo>> GetSceneTransitionListAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ObsTransitionInfo>>([]);

        public Task<IReadOnlyList<ObsSceneItemInfo>> GetSceneItemListAsync(
            string sceneName,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ObsSceneItemInfo>>([]);

        public Task<ObsInputAudioState> GetInputAudioStateAsync(
            string inputName,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ObsInputAudioState(inputName, false, 0));

        public Task SetInputMuteAsync(
            string inputName,
            bool muted,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetInputVolumeDbAsync(
            string inputName,
            double volumeDb,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ObsInputAdvancedAudioState> GetInputAdvancedAudioStateAsync(
            string inputName,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ObsInputAdvancedAudioState(inputName, "OBS_MONITORING_TYPE_NONE", 0));

        public Task SetInputAudioMonitorTypeAsync(
            string inputName,
            string monitorType,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetInputAudioSyncOffsetAsync(
            string inputName,
            int syncOffsetMilliseconds,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<string> GetCurrentProgramSceneAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(ProgramScenes.LastOrDefault() ?? "");

        public Task SetCurrentProgramSceneAsync(
            string sceneName,
            CancellationToken cancellationToken = default)
        {
            ProgramScenes.Add(sceneName);
            return Task.CompletedTask;
        }

        public Task SetCurrentSceneTransitionAsync(
            string transitionName,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetCurrentSceneTransitionDurationAsync(
            int transitionDurationMilliseconds,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ObsStreamStatus> GetStreamStatusAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ObsStreamStatus(false, false, "00:00:00", 0, 0, 0, 0));

        public Task<ObsStats> GetStatsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ObsStats(0, 0, 0, 0, 0, 0, 0, 0, 0));

        public Task StartStreamAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopStreamAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ObsOutputStatus> GetRecordStatusAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ObsOutputStatus(false, false, "00:00:00", 0, 0));

        public Task StartRecordAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopRecordAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task PauseRecordAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ResumeRecordAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ObsOutputStatus> GetReplayBufferStatusAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ObsOutputStatus(false, false, "00:00:00", 0, 0));

        public Task StartReplayBufferAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopReplayBufferAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SaveReplayBufferAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> GetVirtualCamStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task StartVirtualCamAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopVirtualCamAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ObsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(
                new ObsSnapshot(IsConnected, "", "", [], [], null, null));

        public Task<bool> InputExistsAsync(
            string inputName,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<IReadOnlyDictionary<string, JsonElement>> GetInputSettingsAsync(
            string inputName,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, JsonElement>>(
                new Dictionary<string, JsonElement>());

        public Task<bool> SceneItemExistsAsync(
            string sceneName,
            string sourceName,
            CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task EnsureSceneAsync(
            string sceneName,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CreateInputAsync(
            string sceneName,
            string inputName,
            string inputKind,
            object inputSettings,
            bool sceneItemEnabled,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CreateSceneItemAsync(
            string sceneName,
            string sourceName,
            bool enabled,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task EnsureMediaInputAsync(
            string sceneName,
            string inputName,
            string localFile,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task EnsureTextInputAsync(
            string sceneName,
            string inputName,
            string text,
            string fontFace,
            int fontSize,
            string fontColor,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetInputSettingsAsync(
            string inputName,
            object inputSettings,
            bool overlay,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RestartMediaInputAsync(
            string inputName,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopMediaInputAsync(
            string inputName,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task PressInputPropertiesButtonAsync(
            string inputName,
            string propertyName,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetSceneItemEnabledAsync(
            string sceneName,
            string sourceName,
            bool enabled,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetSceneItemLockedAsync(
            string sceneName,
            string sourceName,
            bool locked,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetSceneItemIndexAsync(
            string sceneName,
            string sourceName,
            int sceneItemIndex,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetSceneItemTransformAsync(
            string sceneName,
            string sourceName,
            double x,
            double y,
            double width,
            double height,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ObsSourceFilterInfo>> GetSourceFilterListAsync(
            string sourceName,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ObsSourceFilterInfo>>([]);

        public Task SetSourceFilterEnabledAsync(
            string sourceName,
            string filterName,
            bool enabled,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ObsSceneItemTransformInfo> GetSceneItemTransformAsync(
            string sceneName,
            string sourceName,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ObsSceneItemTransformInfo(0, 0, 0, 0, 0, 0, 0, 0, 0));

        public Task ResetSceneItemTransformAsync(
            string sceneName,
            string sourceName,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SetSceneItemDetailedTransformAsync(
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
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<(string CurrentProfile, IReadOnlyList<string> Profiles)> GetProfileListAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<(string, IReadOnlyList<string>)>(("", []));

        public Task SetCurrentProfileAsync(
            string profileName,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<(string CurrentSceneCollection, IReadOnlyList<string> SceneCollections)> GetSceneCollectionListAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<(string, IReadOnlyList<string>)>(("", []));

        public Task SetCurrentSceneCollectionAsync(
            string sceneCollectionName,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<byte[]> GetSourceScreenshotAsync(
            string sourceName,
            int imageWidth = 640,
            int? imageHeight = 360,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Array.Empty<byte>());

        public Task<ObsVideoSettings> GetVideoSettingsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ObsVideoSettings(1920, 1080, 1280, 720, 30, 1));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
