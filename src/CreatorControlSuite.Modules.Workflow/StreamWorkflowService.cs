using System.Text.Json;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Modules.Alerts;
using CreatorControlSuite.Modules.OBS;
using CreatorControlSuite.Modules.Overlay;
using CreatorControlSuite.Modules.Spotify;
using CreatorControlSuite.Modules.Workflow.Models;

namespace CreatorControlSuite.Modules.Workflow;

public sealed class StreamWorkflowService : IStreamWorkflowService
{
    private readonly ISettingsStore _settingsStore;
    private readonly IObsWebSocketClient _obsClient;
    private readonly SpotifyModule _spotify;
    private readonly AlertsModule _alerts;
    private readonly IOverlayDataService _overlay;
    private readonly SemaphoreSlim _transitionLock = new(1, 1);

    private CancellationTokenSource? _countdownCancellation;
    private WorkflowState _state =
        new(
            StreamPhase.Idle,
            null,
            null,
            null,
            0,
            "",
            "Bereit");

    public StreamWorkflowService(
        ISettingsStore settingsStore,
        IObsWebSocketClient obsClient,
        SpotifyModule spotify,
        AlertsModule alerts,
        IOverlayDataService overlay)
    {
        _settingsStore = settingsStore;
        _obsClient = obsClient;
        _spotify = spotify;
        _alerts = alerts;
        _overlay = overlay;
    }

    public WorkflowState State => _state;
    public StreamSessionStats SessionStats { get; } = new();

    public event EventHandler<WorkflowState>? StateChanged;

    public async Task PrepareAsync(
        CancellationToken cancellationToken = default)
    {
        await RunTransitionAsync(
            async settings =>
            {
                SetState(
                    StreamPhase.Preparing,
                    "Stream wird vorbereitet.");

                SessionStats.StartedAt = DateTimeOffset.Now;
                SessionStats.EndedAt = null;
                SessionStats.ViewerSamples.Clear();
                SessionStats.ChatMessages = 0;
                SessionStats.AlertsPlayed = 0;
                SessionStats.NewSubscriptions = 0;
                SessionStats.GiftSubscriptions = 0;
                SessionStats.BitsCheered = 0;
                SessionStats.IncomingRaids = 0;

                if (settings.Workflow.AutoSwitchScenes &&
                    _obsClient.IsConnected &&
                    !string.IsNullOrWhiteSpace(settings.Obs.StartScene))
                {
                    await SetCurrentProgramSceneWhenObsReadyAsync(
                        settings.Obs.StartScene,
                        cancellationToken);
                }

                // Spotify-Startplaylist startet ausschließlich beim OBS-Übergang
                // OFFLINE -> LIVE (HandleObservedStreamStartAsync), nicht beim Vorbereiten.

                await _overlay.UpdateAsync(
                    data =>
                    {
                        data.Stream.IsLive = false;
                        data.Stream.Phase = StreamPhase.Preparing.ToString();
                        data.Stream.CurrentScene = settings.Obs.StartScene;
                        // Beim Vorbereiten darf der Countdown noch nicht anlaufen.
                        // Er wird erst nach dem erfolgreichen OBS-Streamstart gesetzt.
                        data.Stream.ElapsedSeconds = 0;
                    },
                    cancellationToken);

                SetState(
                    StreamPhase.Preparing,
                    "Vorbereitung abgeschlossen.",
                    settings.Obs.StartScene);
            },
            cancellationToken);
    }

    private async Task SetCurrentProgramSceneWhenObsReadyAsync(
        string sceneName,
        CancellationToken cancellationToken)
    {
        const int maximumAttempts = 15;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_obsClient.IsConnected)
            {
                return;
            }

            try
            {
                await _obsClient.SetCurrentProgramSceneAsync(
                    sceneName,
                    cancellationToken);
                return;
            }
            catch (Exception exception) when (IsObsStartupNotReadyException(exception))
            {
                lastException = exception;
                await Task.Delay(700, cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"OBS ist verbunden, akzeptiert den Szenenwechsel zu ‘{sceneName}’ aber noch nicht.",
            lastException);
    }

    private static bool IsObsStartupNotReadyException(Exception exception)
    {
        var message = exception.ToString();
        return message.Contains("not ready to perform the request", StringComparison.OrdinalIgnoreCase)
            || message.Contains("fehlgeschlagen (207)", StringComparison.OrdinalIgnoreCase)
            || message.Contains("request code 207", StringComparison.OrdinalIgnoreCase);
    }

    public async Task StartCountdownAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);

        _countdownCancellation?.Cancel();
        _countdownCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        SetState(
            StreamPhase.Countdown,
            "Countdown läuft.",
            settings.Obs.StartScene,
            settings.Workflow.StartCountdownSeconds);

        for (var remaining = settings.Workflow.StartCountdownSeconds;
             remaining >= 0;
             remaining--)
        {
            _countdownCancellation.Token.ThrowIfCancellationRequested();

            _state = _state with
            {
                Phase = StreamPhase.Countdown,
                CountdownRemainingSeconds = remaining,
                Detail = "Countdown läuft."
            };

            StateChanged?.Invoke(this, _state);

            await _overlay.UpdateAsync(
                data =>
                {
                    data.Stream.Phase =
                        StreamPhase.Countdown.ToString();
                    data.Stream.ElapsedSeconds = remaining;
                    data.Stream.CurrentScene =
                        settings.Obs.StartScene;
                },
                _countdownCancellation.Token);

            if (remaining > 0)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(1),
                    _countdownCancellation.Token);
            }
        }

        await GoLiveAsync(cancellationToken);
    }

    public async Task GoLiveAsync(
        CancellationToken cancellationToken = default)
    {
        await RunTransitionAsync(
            async settings =>
            {
                SetState(
                    StreamPhase.Live,
                    "Wechsel auf Live.");

                if (settings.Workflow.AutoStartObsStream &&
                    _obsClient.IsConnected)
                {
                    var stream = await _obsClient.GetStreamStatusAsync(
                        cancellationToken);

                    if (!stream.OutputActive)
                    {
                        await _obsClient.StartStreamAsync(
                            cancellationToken);
                    }
                }

                if (settings.Workflow.AutoSwitchScenes &&
                    _obsClient.IsConnected)
                {
                    await _obsClient.SetCurrentProgramSceneAsync(
                        settings.Obs.LiveScene,
                        cancellationToken);
                }

                if (settings.Workflow.AutoFadeSpotifyOnLive &&
                    settings.Spotify.FadeOutEnabled)
                {
                    try
                    {
                        await _spotify.FadeToAsync(
                            0,
                            TimeSpan.FromSeconds(
                                settings.Spotify.FadeOutSeconds),
                            settings.Spotify.PauseAfterFadeOut,
                            cancellationToken);
                    }
                    catch
                    {
                    }
                }

                var liveStartedAt = DateTimeOffset.Now;
                _state = _state with
                {
                    Phase = StreamPhase.Live,
                    LiveStartedAt = liveStartedAt,
                    CurrentScene = settings.Obs.LiveScene,
                    Detail = "Stream ist live.",
                    CountdownRemainingSeconds = 0
                };

                await _overlay.UpdateAsync(
                    data =>
                    {
                        data.Stream.IsLive = true;
                        data.Stream.Phase =
                            StreamPhase.Live.ToString();
                        data.Stream.StartedAt = liveStartedAt;
                        data.Stream.EndedAt = null;
                        data.Stream.CurrentScene =
                            settings.Obs.LiveScene;
                    },
                    cancellationToken);

                StateChanged?.Invoke(this, _state);
            },
            cancellationToken);
    }

    public async Task PauseAsync(
        CancellationToken cancellationToken = default)
    {
        await RunTransitionAsync(
            async settings =>
            {
                if (settings.Workflow.AutoSwitchScenes &&
                    _obsClient.IsConnected)
                {
                    await _obsClient.SetCurrentProgramSceneAsync(
                        settings.Obs.PauseScene,
                        cancellationToken);
                }

                SetState(
                    StreamPhase.Paused,
                    "Stream pausiert.",
                    settings.Obs.PauseScene);

                await UpdateOverlayAsync(
                    isLive: true,
                    phase: StreamPhase.Paused,
                    currentScene: settings.Obs.PauseScene,
                    cancellationToken);
            },
            cancellationToken);
    }

    public async Task ResumeAsync(
        CancellationToken cancellationToken = default)
    {
        await RunTransitionAsync(
            async settings =>
            {
                if (settings.Workflow.AutoSwitchScenes &&
                    _obsClient.IsConnected)
                {
                    await _obsClient.SetCurrentProgramSceneAsync(
                        settings.Obs.LiveScene,
                        cancellationToken);
                }

                SetState(
                    StreamPhase.Live,
                    "Stream fortgesetzt.",
                    settings.Obs.LiveScene);

                await UpdateOverlayAsync(
                    isLive: true,
                    phase: StreamPhase.Live,
                    currentScene: settings.Obs.LiveScene,
                    cancellationToken);
            },
            cancellationToken);
    }

    public async Task EndAsync(
        CancellationToken cancellationToken = default)
    {
        await RunTransitionAsync(
            async settings =>
            {
                SetState(
                    StreamPhase.Ending,
                    "Stream wird beendet.",
                    settings.Obs.EndScene);

                var endedAt = DateTimeOffset.Now;
                await FinalizeSessionStatsAsync(endedAt, cancellationToken);

                if (settings.Workflow.AutoSwitchScenes &&
                    _obsClient.IsConnected)
                {
                    await _obsClient.SetCurrentProgramSceneAsync(
                        settings.Obs.EndScene,
                        cancellationToken);
                }

                await Task.Delay(
                    TimeSpan.FromSeconds(
                        Math.Max(1, settings.Workflow.EndSceneSeconds)),
                    cancellationToken);

                if (settings.Workflow.AutoStopObsStream &&
                    _obsClient.IsConnected)
                {
                    var stream = await _obsClient.GetStreamStatusAsync(
                        cancellationToken);

                    if (stream.OutputActive)
                    {
                        await _obsClient.StopStreamAsync(
                            cancellationToken);
                    }
                }

                await _alerts.StopCurrentAsync(cancellationToken);
                await _alerts.ClearQueueAsync(cancellationToken);

                await _overlay.UpdateAsync(
                    data =>
                    {
                        data.Stream.IsLive = false;
                        data.Stream.Phase =
                            StreamPhase.Completed.ToString();
                        data.Stream.EndedAt = endedAt;
                        data.Stream.CurrentScene =
                            settings.Obs.EndScene;
                    },
                    cancellationToken);

                SetState(
                    StreamPhase.Completed,
                    "Stream abgeschlossen.",
                    settings.Obs.EndScene);

                if (settings.Workflow.ExportSessionReport)
                {
                    await ExportSessionReportAsync(
                        cancellationToken);
                }
            },
            cancellationToken);
    }

    public async Task ResetAsync(
        CancellationToken cancellationToken = default)
    {
        _countdownCancellation?.Cancel();

        SessionStats.StartedAt = null;
        SessionStats.EndedAt = null;
        SessionStats.FollowersAtStart = 0;
        SessionStats.FollowersAtEnd = 0;
        SessionStats.ChatMessages = 0;
        SessionStats.AlertsPlayed = 0;
        SessionStats.NewSubscriptions = 0;
        SessionStats.GiftSubscriptions = 0;
        SessionStats.BitsCheered = 0;
        SessionStats.IncomingRaids = 0;
        SessionStats.ViewerSamples.Clear();

        await _overlay.UpdateAsync(
            data =>
            {
                data.Stream = new();
                data.Stats = new();
            },
            cancellationToken);

        SetState(
            StreamPhase.Idle,
            "Bereit.");
    }

    public async Task ResetSessionStatsAsync(
        DateTimeOffset? startedAt = null,
        CancellationToken cancellationToken = default)
    {
        SessionStats.StartedAt = startedAt ?? DateTimeOffset.Now;
        SessionStats.EndedAt = null;
        SessionStats.FollowersAtStart = 0;
        SessionStats.FollowersAtEnd = 0;
        SessionStats.ChatMessages = 0;
        SessionStats.AlertsPlayed = 0;
        SessionStats.NewSubscriptions = 0;
        SessionStats.GiftSubscriptions = 0;
        SessionStats.BitsCheered = 0;
        SessionStats.IncomingRaids = 0;
        SessionStats.ViewerSamples.Clear();

        await SyncStatsToOverlayAsync(cancellationToken);
    }

    public async Task FinalizeSessionStatsAsync(
        DateTimeOffset? endedAt = null,
        CancellationToken cancellationToken = default)
    {
        SessionStats.EndedAt = endedAt ?? DateTimeOffset.Now;
        await SyncStatsToOverlayAsync(cancellationToken);
    }

    public async Task AddViewerSampleAsync(
        int viewers,
        CancellationToken cancellationToken = default)
    {
        SessionStats.ViewerSamples.Add(
            new ViewerSample(
                DateTimeOffset.Now,
                Math.Max(0, viewers)));

        await _overlay.UpdateAsync(
            data =>
            {
                data.Stream.ViewerCount = Math.Max(0, viewers);
                data.Stats.PeakViewers =
                    SessionStats.PeakViewers;
                data.Stats.AverageViewers =
                    SessionStats.AverageViewers;
            },
            cancellationToken);
    }

    public async Task SetFollowerCountsAsync(
        int start,
        int current,
        CancellationToken cancellationToken = default)
    {
        SessionStats.FollowersAtStart = Math.Max(0, start);
        SessionStats.FollowersAtEnd = Math.Max(0, current);

        await _overlay.UpdateAsync(
            data =>
            {
                data.Stats.FollowersGained =
                    SessionStats.FollowersGained;
                data.Twitch.Followers = current;
            },
            cancellationToken);
    }

    public async Task RegisterTwitchEventAsync(
        string eventType,
        int count = 1,
        CancellationToken cancellationToken = default)
    {
        count = Math.Max(1, count);

        switch (eventType)
        {
            case "channel.subscribe":
            case "channel.subscription.message":
                SessionStats.NewSubscriptions += count;
                break;
            case "channel.subscription.gift":
                SessionStats.GiftSubscriptions += count;
                break;
            case "channel.cheer":
                SessionStats.BitsCheered += count;
                break;
            case "channel.raid":
                SessionStats.IncomingRaids += 1;
                break;
            default:
                return;
        }

        await _overlay.UpdateAsync(
            data =>
            {
                data.Stats.NewSubscriptions = SessionStats.NewSubscriptions;
                data.Stats.GiftSubscriptions = SessionStats.GiftSubscriptions;
                data.Stats.BitsCheered = SessionStats.BitsCheered;
                data.Stats.IncomingRaids = SessionStats.IncomingRaids;
            },
            cancellationToken);
    }

    public async Task RegisterChatMessageAsync(
        CancellationToken cancellationToken = default)
    {
        SessionStats.ChatMessages++;

        await _overlay.UpdateAsync(
            data => data.Stats.ChatMessages =
                SessionStats.ChatMessages,
            cancellationToken);
    }

    public async Task RegisterAlertPlayedAsync(
        CancellationToken cancellationToken = default)
    {
        SessionStats.AlertsPlayed++;

        await _overlay.UpdateAsync(
            data => data.Stats.AlertsPlayed =
                SessionStats.AlertsPlayed,
            cancellationToken);
    }

    private async Task RunTransitionAsync(
        Func<AppSettings, Task> transition,
        CancellationToken cancellationToken)
    {
        await _transitionLock.WaitAsync(cancellationToken);

        try
        {
            var settings = await _settingsStore.LoadAsync(
                cancellationToken);

            await transition(settings);
        }
        catch (Exception exception)
        {
            SetState(
                StreamPhase.Error,
                exception.Message);

            throw;
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    private async Task UpdateOverlayAsync(
        bool isLive,
        StreamPhase phase,
        string currentScene,
        CancellationToken cancellationToken)
    {
        await _overlay.UpdateAsync(
            data =>
            {
                data.Stream.IsLive = isLive;
                data.Stream.Phase = phase.ToString();
                data.Stream.CurrentScene = currentScene;
            },
            cancellationToken);
    }

    private async Task SyncStatsToOverlayAsync(
        CancellationToken cancellationToken)
    {
        await _overlay.UpdateAsync(
            data =>
            {
                data.Stats.FollowersGained =
                    SessionStats.FollowersGained;
                data.Stats.PeakViewers =
                    SessionStats.PeakViewers;
                data.Stats.AverageViewers =
                    SessionStats.AverageViewers;
                data.Stats.StreamTimeSeconds =
                    SessionStats.StreamTimeSeconds;
                data.Stats.ChatMessages =
                    SessionStats.ChatMessages;
                data.Stats.AlertsPlayed =
                    SessionStats.AlertsPlayed;
                data.Stats.NewSubscriptions =
                    SessionStats.NewSubscriptions;
                data.Stats.GiftSubscriptions =
                    SessionStats.GiftSubscriptions;
                data.Stats.BitsCheered =
                    SessionStats.BitsCheered;
                data.Stats.IncomingRaids =
                    SessionStats.IncomingRaids;
            },
            cancellationToken);
    }

    private async Task ExportSessionReportAsync(
        CancellationToken cancellationToken)
    {
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "CreatorControlSuite",
            "Reports");

        Directory.CreateDirectory(dataRoot);

        var path = Path.Combine(
            dataRoot,
            "session-" +
            DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss") +
            ".json");

        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(
                SessionStats,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }),
            cancellationToken);
    }

    private void SetState(
        StreamPhase phase,
        string detail,
        string currentScene = "",
        int countdown = 0)
    {
        _state = _state with
        {
            Phase = phase,
            Detail = detail,
            CurrentScene = currentScene,
            CountdownRemainingSeconds = countdown,
            SessionStartedAt =
                _state.SessionStartedAt
                ?? SessionStats.StartedAt
        };

        StateChanged?.Invoke(this, _state);
    }
}
