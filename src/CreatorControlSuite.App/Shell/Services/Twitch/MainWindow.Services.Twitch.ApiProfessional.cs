#nullable enable
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CreatorControlSuite.App.Core.Eventing;
using CreatorControlSuite.App.Helpers;
using CreatorControlSuite.App.Mvvm;
using CreatorControlSuite.App.Services;
using CreatorControlSuite.App.Services.CreatorIntelligence;
using CreatorControlSuite.App.Themes;
using CreatorControlSuite.App.Twitch;
using CreatorControlSuite.App.ViewModels;
using CreatorControlSuite.App.ViewModels.Pages;
using CreatorControlSuite.App.Views.Dialogs;
using CreatorControlSuite.App.Views.Pages.Music;
using CreatorControlSuite.App.Views.Pages.Workflow;
using CreatorControlSuite.Core.Automation;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Diagnostics;
using CreatorControlSuite.Core.Eventing;
using CreatorControlSuite.Core.Ipc;
using CreatorControlSuite.Core.Logging;
using CreatorControlSuite.Core.Music;
using CreatorControlSuite.Core.Profiles;
using CreatorControlSuite.Core.Security;
using CreatorControlSuite.Core.Twitch;
using CreatorControlSuite.Core.Updates;
using CreatorControlSuite.Core.Validation;
using CreatorControlSuite.Modules.Alerts;
using CreatorControlSuite.Modules.Alerts.Models;
using CreatorControlSuite.Modules.OBS;
using CreatorControlSuite.Modules.OBS.Models;
using CreatorControlSuite.Modules.Overlay;
using CreatorControlSuite.Modules.Overlay.Extensions;
using CreatorControlSuite.Modules.Overlay.Models;
using CreatorControlSuite.Modules.Spotify;
using CreatorControlSuite.Modules.Spotify.Models;
using CreatorControlSuite.Modules.StreamDeck;
using CreatorControlSuite.Modules.StreamDeck.Models;
using CreatorControlSuite.Modules.Twitch;
using CreatorControlSuite.Modules.Twitch.Models;
using CreatorControlSuite.Modules.Workflow;
using CreatorControlSuite.Modules.Workflow.Models;
using CreatorControlSuite.Modules.YouTubeMusic;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using MultiPcDeviceRecord = CreatorControlSuite.Core.Security.PairedAgentDevice;

namespace CreatorControlSuite.App.Shell;

public partial class MainWindow : Window
{
    private void SelectDashboardStatisticInUi()
    {
        string metric = string.IsNullOrWhiteSpace(_settings.Dashboard.DashboardStatistic)
            ? "ViewerCount"
            : _settings.Dashboard.DashboardStatistic;
        _statisticsPageViewModel.LoadMetric(metric);
        UpdateDashboardSelectedStatistic();
    }

    private void UpdateDashboardSelectedStatistic()
    {
        StreamSessionStats stats = _workflowModule.Service.SessionStats;
        string metric = _settings.Dashboard.DashboardStatistic ?? "ViewerCount";
        (DashboardPageViewHost.DashboardSelectedStatisticLabel.Text, DashboardPageViewHost.DashboardSelectedStatisticValue.Text) = metric switch
        {
            "FollowerCount" => ("FOLLOWERZAHL", _currentFollowerCount.ToString()),
            "SubscriberCount" => ("SUB-ANZAHL", _currentActiveSubscriptionCount.ToString()),
            "NewFollowers" => ("NEUE FOLLOWER", stats.FollowersGained.ToString()),
            "NewSubscribers" => ("NEUE SUBS", stats.NewSubscriptions.ToString()),
            _ => ("ZUSCHAUERZAHL", _currentLiveViewerCount.ToString())
        };
    }

    private void UpdateStreamLivePulse(bool isLive)
    {
        StreamDashboardStatus.Foreground = isLive
            ? System.Windows.Media.Brushes.LimeGreen
            : System.Windows.Media.Brushes.IndianRed;
        StreamDashboardStatus.BeginAnimation(UIElement.OpacityProperty, null);
        StreamDashboardStatus.Opacity = 1;
        if (!isLive)
        {
            return;
        }

        var pulse = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 1.0,
            To = 0.35,
            Duration = TimeSpan.FromSeconds(1.2),
            AutoReverse = true,
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
        };
        StreamDashboardStatus.BeginAnimation(UIElement.OpacityProperty, pulse);
    }

    private async Task LoadTwitchProfessionalHistoryAsync()
    {
        _twitchProfessionalHistoryItems.Clear();
        TwitchProfessionalHistorySnapshot snapshot =
            await TwitchProfessionalHistoryService.LoadAsync(
                GetStreamHistoryFilePath());
        var view = ServicesPageViewHost.TwitchServiceViewHost;
        view.ServicesTwitchProfessionalTotalStreamsText.Text =
            snapshot.TotalStreams;
        view.ServicesTwitchProfessionalRecordPeakText.Text =
            snapshot.RecordPeak;
        view.ServicesTwitchProfessionalRecordAverageText.Text =
            snapshot.RecordAverage;
        view.ServicesTwitchProfessionalTotalDurationText.Text =
            snapshot.TotalDuration;
        view.ServicesTwitchProfessionalTotalFollowersText.Text =
            snapshot.TotalFollowers;
        view.ServicesTwitchProfessionalViewerTrendText.Text =
            snapshot.ViewerTrend;
        view.ServicesTwitchProfessionalFollowerTrendText.Text =
            snapshot.FollowerTrend;
        view.ServicesTwitchProfessionalCategoryTrendText.Text =
            snapshot.CategoryTrend;
        view.ServicesTwitchProfessionalDurationTrendText.Text =
            snapshot.DurationTrend;
        view.ServicesTwitchProfessionalPeakTrendText.Text =
            snapshot.PeakTrend;
        view.ServicesTwitchProfessionalAverageTrendText.Text =
            snapshot.AverageTrend;
        view.ServicesTwitchProfessionalChatRateText.Text =
            snapshot.ChatRate;
        view.ServicesTwitchProfessionalBestCategoryText.Text =
            snapshot.BestCategory;
        view.ServicesTwitchProfessionalEngagementRateText.Text =
            snapshot.EngagementRate;
        view.ServicesTwitchProfessionalFollowerRateText.Text =
            snapshot.FollowerRate;
        view.ServicesTwitchProfessionalConsistencyText.Text =
            snapshot.Consistency;
        view.ServicesTwitchProfessionalSummaryText.Text =
            snapshot.Summary;
        foreach (string item in snapshot.HistoryItems)
        {
            _twitchProfessionalHistoryItems.Add(item);
        }
    }

    private void RefreshTwitchProfessionalUi(TwitchRaidTargetStatus? liveStatus = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => RefreshTwitchProfessionalUi(liveStatus));
            return;
        }

        TwitchConnectionSnapshot snapshot = _twitchModule.GetSnapshot();
        StreamSessionStats stats = _workflowModule.Service.SessionStats;
        bool live = liveStatus is not null
            ? liveStatus.IsOnline
            : _twitchStreamStartedAt.HasValue || _lastObsStreamActive;
        DateTimeOffset? startedAt = liveStatus?.StartedAt
            ?? ResolveLiveStreamStartedAt();
        TimeSpan duration = startedAt.HasValue
            ? DateTimeOffset.Now - startedAt.Value
            : TimeSpan.Zero;

        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalLiveText.Text = live ? "LIVE" : "OFFLINE";
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalLiveText.Foreground = live
            ? System.Windows.Media.Brushes.LightGreen
            : System.Windows.Media.Brushes.Gray;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalViewerText.Text = _currentLiveViewerCount.ToString();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalPeakText.Text = stats.PeakViewers.ToString();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalAverageText.Text = stats.AverageViewers.ToString("0.0");
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalDurationText.Text = duration.ToString(@"hh\:mm\:ss");
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalChatText.Text = _twitchSessionChatMessages.ToString();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalUniqueChattersText.Text = _twitchSessionUniqueChatters.Count.ToString();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalEventsText.Text = _twitchSessionEvents.ToString();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalFollowersText.Text = stats.FollowersGained.ToString();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalCategoryText.Text = string.IsNullOrWhiteSpace(liveStatus?.GameName)
            ? (string.IsNullOrWhiteSpace(snapshot.CategoryName) ? "-" : snapshot.CategoryName)
            : liveStatus.GameName;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalTitleText.Text = string.IsNullOrWhiteSpace(liveStatus?.StreamTitle)
            ? (string.IsNullOrWhiteSpace(snapshot.ChannelTitle) ? "-" : snapshot.ChannelTitle)
            : liveStatus.StreamTitle;
    }

    private void RefreshTwitchUi()
    {
        TwitchConnectionSnapshot snapshot = _twitchModule.GetSnapshot();

        TwitchDashboardStatus.Text = snapshot.Authenticated
            ? "VERBUNDEN"
            : "NICHT VERBUNDEN";

        TwitchDashboardLamp.Fill = snapshot.Authenticated
            ? System.Windows.Media.Brushes.LimeGreen
            : System.Windows.Media.Brushes.IndianRed;

        SettingsPageViewHost.TwitchConnectionStatusText.Text = snapshot.Authenticated
            ? $"Verbunden als {snapshot.Login} · " +
              $"EventSub: {(snapshot.EventSubConnected ? "aktiv" : "offline")}"
            : "Nicht verbunden";

        SettingsPageViewHost.TwitchConnectionStatusText.Foreground = snapshot.Authenticated
            ? System.Windows.Media.Brushes.LightGreen
            : System.Windows.Media.Brushes.Gray;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchStatusText.Text = SettingsPageViewHost.TwitchConnectionStatusText.Text;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchStatusText.Foreground = SettingsPageViewHost.TwitchConnectionStatusText.Foreground;

        SettingsPageViewHost.TwitchTitleBox.Text = snapshot.ChannelTitle;
        SettingsPageViewHost.TwitchCategorySearchBox.Text = snapshot.CategoryName;
        DashboardPageViewHost.DashboardTwitchTitleBox.Text = snapshot.ChannelTitle;
        DashboardPageViewHost.DashboardTwitchCategorySearchBox.Text = snapshot.CategoryName;
        DashboardPageViewHost.DashboardTwitchChannelTitleText.Text = string.IsNullOrWhiteSpace(snapshot.ChannelTitle)
            ? "Kein Streamtitel gesetzt"
            : snapshot.ChannelTitle;
        string notification = string.IsNullOrWhiteSpace(_settings.Twitch.LiveNotificationText)
            ? "Live-Benachrichtigung nicht gesetzt"
            : _settings.Twitch.LiveNotificationText;
        DashboardPageViewHost.DashboardTwitchChannelDetailsText.Text =
            $"{(string.IsNullOrWhiteSpace(snapshot.CategoryName) ? "Keine Kategorie" : snapshot.CategoryName)} · {notification}";
        ServicesPageViewHost.TwitchServiceViewHost.RefreshChannelEditor(
            snapshot.ChannelTitle,
            snapshot.CategoryName);
        RefreshDashboardServiceActionButtons();
        _ = RefreshTwitchWebChatViewsAsync(forceReload: false);
    }

    private static string GetTwitchRoleLabel(
        TwitchChatMessage message)
    {
        if (string.Equals(
                message.ChatterUserId,
                message.BroadcasterUserId,
                StringComparison.Ordinal))
        {
            return "[STREAMER] ";
        }

        if (message.Badges.Any(
                badge =>
                    string.Equals(
                        badge.SetId,
                        "moderator",
                        StringComparison.OrdinalIgnoreCase)))
        {
            return "[MOD] ";
        }

        if (message.Badges.Any(
                badge =>
                    string.Equals(
                        badge.SetId,
                        "vip",
                        StringComparison.OrdinalIgnoreCase)))
        {
            return "[VIP] ";
        }

        if (message.Badges.Any(
                badge =>
                    string.Equals(
                        badge.SetId,
                        "subscriber",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        badge.SetId,
                        "founder",
                        StringComparison.OrdinalIgnoreCase)))
        {
            return "[SUB] ";
        }

        return "";
    }

    private void UpdateDashboardTwitchUser(
        TwitchChatMessage message,
        string role)
    {
        string userId = string.IsNullOrWhiteSpace(message.ChatterUserId)
            ? message.ChatterLogin
            : message.ChatterUserId;
        string userName = string.IsNullOrWhiteSpace(message.ChatterName)
            ? message.ChatterLogin
            : message.ChatterName;

        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(userName))
        {
            return;
        }

        string display = role + userName;

        if (_twitchUserDisplayById.TryGetValue(userId, out string? previous))
        {
            int index = _twitchUserItems.IndexOf(previous);

            if (index >= 0)
            {
                _twitchUserItems[index] = display;
            }
        }
        else if (!_twitchUserItems.Any(item =>
                     string.Equals(
                         GetTwitchUserNameFromDisplay(item),
                         userName,
                         StringComparison.OrdinalIgnoreCase)))
        {
            _twitchUserItems.Add(display);
        }

        _twitchUserDisplayById[userId] = display;

        while (_twitchUserItems.Count > 1000)
        {
            _twitchUserItems.RemoveAt(0);
        }
    }

    private static string GetTwitchUserNameFromDisplay(string display)
    {
        foreach (string? prefix in new[]
                 {
                     "[STREAMER] ",
                     "[MOD] ",
                     "[VIP] ",
                     "[SUB] "
                 })
        {
            if (display.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return display[prefix.Length..];
            }
        }

        return display;
    }

    private static int GetTwitchEventCount(TwitchEvent twitchEvent)
    {
        static int Parse(
            IReadOnlyDictionary<string, string> data,
            params string[] keys)
        {
            foreach (string key in keys)
            {
                if (data.TryGetValue(key, out string? value) &&
                    int.TryParse(value, out int parsed))
                {
                    return Math.Max(1, parsed);
                }
            }

            return 1;
        }

        return twitchEvent.Type switch
        {
            "channel.subscription.gift" =>
                Parse(twitchEvent.Data, "total", "count", "amount"),
            "channel.cheer" =>
                Parse(twitchEvent.Data, "bits"),
            _ => 1
        };
    }

    private static void AddLimitedItem(
        ObservableCollection<string> collection,
        string value,
        int limit)
    {
        collection.Add(value);

        while (collection.Count > limit)
        {
            collection.RemoveAt(0);
        }
    }
}
