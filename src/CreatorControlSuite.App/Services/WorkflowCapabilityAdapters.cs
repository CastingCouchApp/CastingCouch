using CreatorControlSuite.Modules.Alerts;
using CreatorControlSuite.Modules.OBS;
using CreatorControlSuite.Modules.Overlay;
using CreatorControlSuite.Modules.Overlay.Models;
using CreatorControlSuite.Modules.Spotify;
using CreatorControlSuite.Modules.Workflow;

namespace CreatorControlSuite.App.Services;

public sealed class WorkflowObsCapability(
    IObsWebSocketClient obs) : IWorkflowObsCapability
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

public sealed class WorkflowMusicCapability(
    SpotifyModule spotify) : IWorkflowMusicCapability
{
    public Task FadeToAsync(
        int targetVolumePercent,
        TimeSpan duration,
        bool pauseAfterFade,
        CancellationToken cancellationToken) =>
        spotify.FadeToAsync(
            targetVolumePercent,
            duration,
            pauseAfterFade,
            cancellationToken);
}

public sealed class WorkflowAlertCapability(
    AlertsModule alerts) : IWorkflowAlertCapability
{
    public async Task StopAndClearAsync(CancellationToken cancellationToken)
    {
        await alerts.StopCurrentAsync(cancellationToken);
        await alerts.ClearQueueAsync(cancellationToken);
    }
}

public sealed class WorkflowOverlayCapability(
    IOverlayDataService overlay) : IWorkflowOverlayCapability
{
    public Task UpdateAsync(
        Action<WorkflowOverlayData> update,
        CancellationToken cancellationToken) =>
        overlay.UpdateAsync(
            data =>
            {
                WorkflowOverlayData workflowData = FromOverlay(data);
                update(workflowData);
                Apply(workflowData, data);
            },
            cancellationToken);

    private static WorkflowOverlayData FromOverlay(OverlayData data) => new()
    {
        Stream = new WorkflowOverlayStream
        {
            IsLive = data.Stream.IsLive,
            Phase = data.Stream.Phase,
            StartedAt = data.Stream.StartedAt,
            EndedAt = data.Stream.EndedAt,
            ElapsedSeconds = data.Stream.ElapsedSeconds,
            ViewerCount = data.Stream.ViewerCount,
            CurrentScene = data.Stream.CurrentScene
        },
        Stats = new WorkflowOverlayStats
        {
            FollowersGained = data.Stats.FollowersGained,
            PeakViewers = data.Stats.PeakViewers,
            AverageViewers = data.Stats.AverageViewers,
            StreamTimeSeconds = data.Stats.StreamTimeSeconds,
            ChatMessages = data.Stats.ChatMessages,
            AlertsPlayed = data.Stats.AlertsPlayed,
            NewSubscriptions = data.Stats.NewSubscriptions,
            GiftSubscriptions = data.Stats.GiftSubscriptions,
            BitsCheered = data.Stats.BitsCheered,
            IncomingRaids = data.Stats.IncomingRaids
        },
        Countdown = new WorkflowOverlayCountdown
        {
            IsRunning = data.Countdown.IsRunning,
            RemainingSeconds = data.Countdown.RemainingSeconds,
            TotalSeconds = data.Countdown.TotalSeconds,
            EndsAt = data.Countdown.EndsAt,
            Label = data.Countdown.Label,
            Mode = data.Countdown.Mode
        },
        Twitch = new WorkflowOverlayTwitch
        {
            Followers = data.Twitch.Followers
        }
    };

    private static void Apply(WorkflowOverlayData source, OverlayData target)
    {
        target.Stream.IsLive = source.Stream.IsLive;
        target.Stream.Phase = source.Stream.Phase;
        target.Stream.StartedAt = source.Stream.StartedAt;
        target.Stream.EndedAt = source.Stream.EndedAt;
        target.Stream.ElapsedSeconds = source.Stream.ElapsedSeconds;
        target.Stream.ViewerCount = source.Stream.ViewerCount;
        target.Stream.CurrentScene = source.Stream.CurrentScene;
        target.Stats.FollowersGained = source.Stats.FollowersGained;
        target.Stats.PeakViewers = source.Stats.PeakViewers;
        target.Stats.AverageViewers = source.Stats.AverageViewers;
        target.Stats.StreamTimeSeconds = source.Stats.StreamTimeSeconds;
        target.Stats.ChatMessages = source.Stats.ChatMessages;
        target.Stats.AlertsPlayed = source.Stats.AlertsPlayed;
        target.Stats.NewSubscriptions = source.Stats.NewSubscriptions;
        target.Stats.GiftSubscriptions = source.Stats.GiftSubscriptions;
        target.Stats.BitsCheered = source.Stats.BitsCheered;
        target.Stats.IncomingRaids = source.Stats.IncomingRaids;
        target.Countdown.IsRunning = source.Countdown.IsRunning;
        target.Countdown.RemainingSeconds = source.Countdown.RemainingSeconds;
        target.Countdown.TotalSeconds = source.Countdown.TotalSeconds;
        target.Countdown.EndsAt = source.Countdown.EndsAt;
        target.Countdown.Label = source.Countdown.Label;
        target.Countdown.Mode = source.Countdown.Mode;
        target.Twitch.Followers = source.Twitch.Followers;
    }
}
