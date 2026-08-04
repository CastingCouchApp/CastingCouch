#nullable enable
using System.Text.Json.Nodes;
using CreatorControlSuite.Core.Logging;
using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.App.Shell;

public partial class MainWindow
{
    private async Task RefreshTwitchGoalsAsync()
    {
        if (!_twitchModule.GetSnapshot().Authenticated)
        {
            _currentActiveSubscriptionCount = 0;
            DashboardChatAlertsText.Text = "0";
            return;
        }

        try
        {
            Task<int> followerTask =
                _twitchModule.GetFollowerCountAsync();
            Task<int> subscriptionTask =
                _twitchModule.GetActiveSubscriptionCountAsync();

            await Task.WhenAll(followerTask, subscriptionTask);

            _currentFollowerCount = Math.Max(0, followerTask.Result);
            _currentActiveSubscriptionCount =
                Math.Max(0, subscriptionTask.Result);

            _settings.Twitch.FollowerGoal.Current =
                _currentFollowerCount;
            _settings.Twitch.SubGoal.Current =
                _currentActiveSubscriptionCount;

            _twitchGoalsPageViewModel.UpdateLiveCounts(
                _currentFollowerCount,
                _currentActiveSubscriptionCount);

            DashboardFollowerTotalText.Text =
                $"Gesamt: {_currentFollowerCount}";
            DashboardHeroFollowerText.Text =
                _currentFollowerCount.ToString();
            DashboardChatAlertsText.Text =
                _currentActiveSubscriptionCount.ToString();
            DashboardTwitchGoalsText.Text =
                $"Follower-Ziel: {_currentFollowerCount:0} / " +
                $"{_settings.Twitch.FollowerGoal.Target:0} · " +
                $"Sub-Ziel: {_currentActiveSubscriptionCount:0} / " +
                $"{_settings.Twitch.SubGoal.Target:0}";

            await _overlayModule.Service.UpdateAsync(data =>
            {
                data.Twitch.Followers = _currentFollowerCount;
                data.Twitch.FollowerGoalState.Current =
                    _currentFollowerCount;
                data.Twitch.FollowerGoalState.Target =
                    _settings.Twitch.FollowerGoal.Target;
                data.Twitch.FollowerGoalState.Title =
                    _settings.Twitch.FollowerGoal.Title;
                data.Twitch.FollowerGoalState.FontFace =
                    _settings.Twitch.FollowerGoal.FontFace;
                data.Twitch.FollowerGoalState.FontSize =
                    _settings.Twitch.FollowerGoal.FontSize;

                data.Twitch.SubGoalState.Current =
                    _currentActiveSubscriptionCount;
                data.Twitch.SubGoalState.Target =
                    _settings.Twitch.SubGoal.Target;
                data.Twitch.SubGoalState.Title =
                    _settings.Twitch.SubGoal.Title;
                data.Twitch.SubGoalState.FontFace =
                    _settings.Twitch.SubGoal.FontFace;
                data.Twitch.SubGoalState.FontSize =
                    _settings.Twitch.SubGoal.FontSize;
            });
        }
        catch (Exception exception)
        {
            _appLogger.Write(
                AppLogLevel.Warning,
                "Twitch",
                "Twitch-Ziele konnten nicht automatisch aktualisiert werden.",
                exception);
        }
    }

    private async Task RefreshTwitchFollowerCountAsync(
        bool initializeStreamBaseline = false)
    {
        if (!_twitchModule.GetSnapshot().Authenticated)
        {
            return;
        }

        try
        {
            int followerCount =
                await _twitchModule.GetFollowerCountAsync();

            _currentFollowerCount = Math.Max(0, followerCount);

            if (initializeStreamBaseline)
            {
                _streamFollowerBaseline = _currentFollowerCount;
                _twitchSessionChatMessages = 0;
                _twitchSessionEvents = 0;
                _twitchSessionUniqueChatters.Clear();
                _twitchSessionObservedAt = DateTimeOffset.Now;
                RefreshTwitchProfessionalUi();
            }

            int baseline = _streamFollowerBaseline > 0
                ? _streamFollowerBaseline
                : _currentFollowerCount;

            await _workflowModule.Service.SetFollowerCountsAsync(
                baseline,
                _currentFollowerCount);

            DashboardFollowerTotalText.Text =
                $"Gesamt: {_currentFollowerCount}";
            DashboardFollowersGainedText.Text =
                Math.Max(
                        0,
                        _currentFollowerCount - baseline)
                    .ToString();

            await _overlayModule.Service.UpdateAsync(data =>
            {
                data.Twitch.Followers = _currentFollowerCount;
            });
            await UpdateActiveOverlayJsonAsync(root =>
            {
                JsonObject twitch =
                    root["twitch"] as JsonObject ?? [];
                twitch["followers"] = _currentFollowerCount;
                twitch["followerGoal"] =
                    _settings.Twitch.FollowerGoal.Target;
                JsonObject goal =
                    twitch["followerGoalState"] as JsonObject ?? [];
                goal["title"] =
                    _settings.Twitch.FollowerGoal.Title;
                goal["current"] = _currentFollowerCount;
                goal["target"] =
                    _settings.Twitch.FollowerGoal.Target;
                goal["fontFace"] =
                    _settings.Twitch.FollowerGoal.FontFace;
                goal["fontSize"] =
                    _settings.Twitch.FollowerGoal.FontSize;
                twitch["followerGoalState"] = goal;
                root["twitch"] = twitch;
            });
            DashboardTwitchGoalsText.Text =
                $"Follower-Ziel: {_currentFollowerCount:0} / " +
                $"{_settings.Twitch.FollowerGoal.Target:0} · " +
                $"Sub-Ziel: {_currentActiveSubscriptionCount:0} / " +
                $"{_settings.Twitch.SubGoal.Target:0}";
        }
        catch (Exception exception)
        {
            _appLogger.Write(
                AppLogLevel.Warning,
                "Twitch",
                "Followerzahl konnte nicht aktualisiert werden.",
                exception);
        }
    }

    private async Task RefreshLiveViewerSampleAsync()
    {
        if (_liveViewerSampleRunning)
        {
            return;
        }

        TwitchConnectionSnapshot twitchSnapshot =
            _twitchModule.GetSnapshot();
        if (!twitchSnapshot.Authenticated)
        {
            _twitchStreamStartedAt = null;
            _currentLiveViewerCount = 0;
            DashboardHeroViewerText.Text = "0";
            AddDashboardViewerTrendSample(0);
            RefreshTwitchProfessionalUi();
            ApplyTwitchUsersRefreshInterval();
            return;
        }

        // Helix expects the canonical login, not the display name.
        string channel =
            !string.IsNullOrWhiteSpace(twitchSnapshot.ChannelLogin)
                ? twitchSnapshot.ChannelLogin
                : twitchSnapshot.Login;

        if (string.IsNullOrWhiteSpace(channel))
        {
            return;
        }

        _liveViewerSampleRunning = true;

        try
        {
            TwitchRaidTargetStatus? status =
                await _twitchModule.GetRaidTargetStatusAsync(channel);

            if (status is null || !status.IsOnline)
            {
                _twitchStreamStartedAt = null;
                _currentLiveViewerCount = 0;
                DashboardHeroViewerText.Text = "0";
                AddDashboardViewerTrendSample(0);
                RefreshTwitchProfessionalUi();
                RefreshWorkflowUi(_workflowModule.Service.State);
                ApplyTwitchUsersRefreshInterval();
                return;
            }

            ApplyTwitchLiveStreamStartedAt(status.StartedAt);

            _currentLiveViewerCount =
                Math.Max(0, status.ViewerCount);
            AddDashboardViewerTrendSample(_currentLiveViewerCount);
            ApplyTwitchUsersRefreshInterval();
            await _creatorIntelligence.RecordAsync(
                "twitch.viewer.sample",
                new
                {
                    viewers = _currentLiveViewerCount,
                    scene = _servicesObsCurrentScene,
                    category = status.GameName,
                    title = status.StreamTitle
                });
            RefreshTwitchProfessionalUi(status);

            await _workflowModule.Service.AddViewerSampleAsync(
                _currentLiveViewerCount);
            RefreshWorkflowUi(_workflowModule.Service.State);
            RefreshCommunityUi();

            await _overlayModule.Service.UpdateAsync(data =>
            {
                data.Stream.ViewerCount = _currentLiveViewerCount;
            });
            DateTimeOffset? liveStartedAt =
                ResolveLiveStreamStartedAt();
            await UpdateActiveOverlayJsonAsync(root =>
            {
                JsonObject stream =
                    root["stream"] as JsonObject ?? [];
                stream["viewerCount"] = _currentLiveViewerCount;
                stream["isLive"] = true;
                stream["startedAt"] = liveStartedAt;
                stream["elapsedSeconds"] = liveStartedAt.HasValue
                    ? Math.Max(
                        0,
                        (long)(DateTimeOffset.Now -
                            liveStartedAt.Value).TotalSeconds)
                    : 0;
                root["stream"] = stream;
            });
        }
        catch (Exception exception)
        {
            _appLogger.Write(
                AppLogLevel.Warning,
                "Twitch",
                "Aktuelle Zuschauerzahl konnte nicht aktualisiert werden.",
                exception);
        }
        finally
        {
            _liveViewerSampleRunning = false;
        }
    }
}
