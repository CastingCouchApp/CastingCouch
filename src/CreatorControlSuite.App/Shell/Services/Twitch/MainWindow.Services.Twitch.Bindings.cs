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
    private void InitializeTwitchBindings()
    {
        SettingsPageViewHost.AuthorizeTwitchButton.Click += async (_, _) =>
            await AuthorizeTwitchAsync();

        SettingsPageViewHost.ConnectTwitchButton.Click += async (_, _) =>
            await ConnectTwitchAsync();

        SettingsPageViewHost.DisconnectTwitchButton.Click += async (_, _) =>
            await DisconnectTwitchAsync();

        SettingsPageViewHost.TwitchChatUiBuiltInRadio.Checked += async (_, _) => await OnTwitchChatUiModeChangedAsync();
        SettingsPageViewHost.TwitchChatUiEmbeddedWebRadio.Checked += async (_, _) => await OnTwitchChatUiModeChangedAsync();
        SettingsPageViewHost.TwitchWebLoginButton.Click += (_, _) => OpenTwitchWebLoginWindow();

        SettingsPageViewHost.SearchTwitchCategoryButton.Click += async (_, _) =>
            await SearchTwitchCategoriesAsync();

        SettingsPageViewHost.SaveTwitchChannelButton.Click += async (_, _) =>
            await SaveTwitchChannelAsync();
        DashboardPageViewHost.DashboardSearchTwitchCategoryButton.Click += async (_, _) => await SearchTwitchCategoriesAsync(DashboardPageViewHost.DashboardTwitchCategorySearchBox, DashboardPageViewHost.DashboardTwitchCategoryResultsBox);
        DashboardPageViewHost.DashboardSaveTwitchChannelButton.Click += async (_, _) => await SaveTwitchChannelAsync(DashboardPageViewHost.DashboardTwitchTitleBox, DashboardPageViewHost.DashboardTwitchCategoryResultsBox);
        DashboardPageViewHost.DashboardTwitchChannelWidget.MouseLeftButtonUp += async (_, _) => await OpenTwitchChannelEditorAsync();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesSearchTwitchCategoryButton.Click += async (_, _) => await SearchTwitchCategoriesAsync(ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchCategorySearchBox, ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchCategoryResultsBox);
        ServicesPageViewHost.TwitchServiceViewHost.ServicesSaveTwitchChannelButton.Click += async (_, _) => await SaveTwitchChannelAsync(ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchTitleBox, ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchCategoryResultsBox);
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchEndStreamButton.Click += async (_, _) => await ShowStreamEndDialogAndRunAsync();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreateRewardButton.Click += async (_, _) => await CreateTwitchRewardAsync();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesRefreshRewardsButton.Click += async (_, _) => await RefreshTwitchRewardsAsync();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatePollButton.Click += async (_, _) => await CreateTwitchPollAsync();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatePredictionButton.Click += async (_, _) => await CreateTwitchPredictionAsync();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesEndPollButton.Click += async (_, _) => await EndTwitchPollAsync("TERMINATED");
        ServicesPageViewHost.TwitchServiceViewHost.ServicesArchivePollButton.Click += async (_, _) => await EndTwitchPollAsync("ARCHIVED");
        ServicesPageViewHost.TwitchServiceViewHost.ServicesLockPredictionButton.Click += async (_, _) => await EndTwitchPredictionAsync("LOCKED");
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCancelPredictionButton.Click += async (_, _) => await EndTwitchPredictionAsync("CANCELED");
        ServicesPageViewHost.TwitchServiceViewHost.ServicesResolvePredictionButton.Click += async (_, _) => await ResolveTwitchPredictionAsync();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesRefreshRedemptionsButton.Click += async (_, _) => await RefreshTwitchRedemptionsAsync();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesFulfillRedemptionButton.Click += async (_, _) => await UpdateSelectedTwitchRedemptionAsync("FULFILLED");
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCancelRedemptionButton.Click += async (_, _) => await UpdateSelectedTwitchRedemptionAsync("CANCELED");


        SettingsPageViewHost.SendTwitchChatButton.Click += async (_, _) =>
            await SendTwitchChatAsync();

        _twitchModule.ChatMessageReceived += async (_, message) =>
        {
            Dispatcher.Invoke(() =>
            {
                _twitchSessionChatMessages++;
                if (!string.IsNullOrWhiteSpace(message.ChatterUserId))
                {
                    _twitchSessionUniqueChatters.Add(message.ChatterUserId);
                }
                _twitchSessionObservedAt ??= DateTimeOffset.Now;
                RefreshTwitchProfessionalUi();
                _ = _creatorIntelligence.RecordAsync("twitch.chat.message", new { user = message.ChatterName, scene = _servicesObsCurrentScene, viewers = _currentLiveViewerCount });

                string role =
                    GetTwitchRoleLabel(
                        message);

                string chatLine =
                    $"{message.ReceivedAt:HH:mm:ss} · {role}{message.ChatterName}: {message.MessageText}";

                AddLimitedItem(
                    _twitchChatItems,
                    chatLine,
                    500);

                ScrollTwitchChatToLatest();

                UpdateDashboardTwitchUser(
                    message,
                    role);
                RefreshCommunityUi();
            });

            await _workflowModule.Service.RegisterChatMessageAsync();
            await PublishOverlayChatMessageAsync(message);
        };

        _twitchModule.EventReceived += async (_, twitchEvent) =>
        {
            Dispatcher.Invoke(() =>
            {
                _twitchSessionEvents++;
                _twitchSessionObservedAt ??= DateTimeOffset.Now;
                RefreshTwitchProfessionalUi();
                _ = _creatorIntelligence.RecordAsync("twitch.event", new { type = twitchEvent.Type, summary = twitchEvent.Summary, viewers = _currentLiveViewerCount, scene = _servicesObsCurrentScene });
                if (twitchEvent.Type == "channel.follow")
                {
                    _ = _creatorIntelligence.RecordAsync("twitch.follow", new { twitchEvent.Summary });
                }

                AddLimitedItem(
                    _twitchEventItems,
                    $"{twitchEvent.ReceivedAt:HH:mm:ss} · " +
                    twitchEvent.Summary,
                    200);

                if (twitchEvent.Type == "channel.guest_star_guest.update")
                {
                    string state = twitchEvent.Data.TryGetValue("state", out string? guestState)
                        ? guestState
                        : "";
                    DashboardPageViewHost.DashboardJoinStreamTogetherButton.Visibility =
                        state is "invited" or "accepted" or "ready"
                            ? Visibility.Visible
                            : Visibility.Collapsed;
                }
            });

            await HandleOverlayChatModerationEventAsync(twitchEvent);

            await PublishOverlayRealtimeEventAsync(OverlayEventBridge.FromTwitch(
                twitchEvent.Type,
                twitchEvent.Summary,
                twitchEvent.ReceivedAt,
                twitchEvent.Data));

            if (twitchEvent.Type == "channel.follow")
            {
                await RefreshTwitchFollowerCountAsync();
                await RefreshTwitchGoalsAsync();
            }
            else if (twitchEvent.Type is
                "channel.subscribe" or
                "channel.subscription.message" or
                "channel.subscription.gift")
            {
                await RefreshTwitchGoalsAsync();
            }

            string alertType = twitchEvent.Type switch
            {
                "channel.follow" => "Follow",
                "channel.subscribe" => "Sub",
                "channel.subscription.message" => "ReSub",
                "channel.subscription.gift" => "GiftSub",
                "channel.cheer" => "Cheer",
                "channel.raid" => "Raid",
                _ => ""
            };

            int eventCount = GetTwitchEventCount(twitchEvent);
            await _workflowModule.Service.RegisterTwitchEventAsync(
                twitchEvent.Type,
                eventCount);

            if (!string.IsNullOrWhiteSpace(alertType))
            {
                // Streamer.bot spielt üblicherweise auf genau diese Twitch-Ereignisse
                // seine Alerts ab. Dadurch greift das Spotify-Ducking automatisch,
                // auch wenn Streamer.bot keine expliziten Start/Ende-Befehle sendet.
                _ = PulseExternalAlertAsync("streamerbot", $"{alertType}-{Guid.NewGuid():N}", TimeSpan.FromSeconds(10));

                string user = twitchEvent.Data.TryGetValue(
                    "user_name",
                    out string? userName)
                    ? userName
                    : twitchEvent.Data.TryGetValue(
                        "from_broadcaster_user_name",
                        out string? raidUser)
                        ? raidUser
                        : "Twitch";

                await _alertsModule.EnqueueAsync(
                    alertType,
                    user,
                    twitchEvent.Data);
            }

            RefreshWorkflowUi(_workflowModule.Service.State);
            RefreshCommunityUi();
        };
    }
}
