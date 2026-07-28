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
    private void InitializeDashboardBindings()
    {
        DashboardPageViewHost.DashboardQuickStartObsButton.Click += (_, _) => LaunchConfiguredExecutable(_settings.Obs.ExecutablePath, "OBS");
        DashboardPageViewHost.DashboardQuickOpenTwitchButton.Click += (_, _) =>
            NavigateToServicesTab(1);
        DashboardPageViewHost.DashboardQuickStartSpotifyButton.Click += (_, _) => LaunchConfiguredExecutable(_settings.Spotify.ExecutablePath, "Spotify");
        DashboardPageViewHost.DashboardQuickStartStreamerBotButton.Click += (_, _) => LaunchConfiguredExecutable(_settings.StreamerBot.ExecutablePath, "Streamer.bot");
        DashboardPageViewHost.DashboardQuickTestAlertButton.Click += async (_, _) =>
        {
            if (AlertTypeBox.SelectedItem is null && AlertTypeBox.Items.Count > 0)
            {
                AlertTypeBox.SelectedIndex = 0;
            }

            await TestAlertInObsAsync();
        };
        DashboardPageViewHost.DashboardQuickOpenOverlayButton.Click += async (_, _) => await OpenOverlayFolderAsync();
        DashboardPageViewHost.DashboardQuickAccessAlertButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardPageViewHost.DashboardQuickAccessAlertButton,
                "Test-Alert",
                async () =>
                {
                    DashboardPageViewHost.DashboardQuickTestAlertButton.RaiseEvent(
                        new RoutedEventArgs(Button.ClickEvent));
                    await Task.Delay(250);
                },
                refreshDashboard: false);
        DashboardPageViewHost.DashboardQuickAccessOverlayButton.Click += (_, _) =>
            ShowPage(OverlayPage);
        DashboardPageViewHost.DashboardShortStreamTestButton.Click += async (_, _) =>
        {
            ShowPage(WorkflowPage);
            WorkflowPageViewHost.ShowShortStreamTest(
                "Kurztest bereit. Der Stream wird nicht gestartet.");
            await RefreshTimedAutomationObsListsAsync();
        };
        DashboardPageViewHost.DashboardServicesStreamDeckButton.Click += (_, _) =>
            NavigateToServicesTab(4, ServicesStreamDeckButton);
        DashboardTopOpenStreamDeckButton.Click += (_, _) =>
            NavigateToServicesTab(4, ServicesStreamDeckButton);
        DashboardPageViewHost.DashboardServiceConnectObsButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardPageViewHost.DashboardServiceConnectObsButton,
                "OBS-Verbindung",
                ToggleObsFromDashboardAsync);
        DashboardPageViewHost.DashboardServiceConnectTwitchButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardPageViewHost.DashboardServiceConnectTwitchButton,
                "Twitch-Verbindung",
                ToggleTwitchFromDashboardAsync);
        DashboardPageViewHost.DashboardServiceConnectSpotifyButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardPageViewHost.DashboardServiceConnectSpotifyButton,
                "Spotify-Verbindung",
                ToggleSpotifyFromDashboardAsync);
        DashboardPageViewHost.DashboardServiceConnectStreamerBotButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardPageViewHost.DashboardServiceConnectStreamerBotButton,
                "Streamer.bot-Verbindung",
                ToggleStreamerBotFromDashboardAsync);
        DashboardTopConnectObsButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardTopConnectObsButton,
                "OBS-Verbindung",
                ToggleObsFromDashboardAsync);
        DashboardTopConnectTwitchButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardTopConnectTwitchButton,
                "Twitch-Verbindung",
                ToggleTwitchFromDashboardAsync);
        DashboardTopConnectSpotifyButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardTopConnectSpotifyButton,
                "Spotify-Verbindung",
                ToggleSpotifyFromDashboardAsync);
        DashboardTopConnectStreamerBotButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardTopConnectStreamerBotButton,
                "Streamer.bot-Verbindung",
                ToggleStreamerBotFromDashboardAsync);
        DashboardPageViewHost.DashboardOpenTwitchChatButton.Click += (_, _) =>
            OpenDashboardTwitchChat();
        DashboardPageViewHost.DashboardManageAutomationsButton.Click += (_, _) => ShowPage(WorkflowPage);
        DashboardPageViewHost.DashboardOpenEventsButton.Click += async (_, _) =>
        {
            ShowPage(StatisticsPage);
            await _statisticsPageViewModel.LoadAsync(
                GetStreamHistoryFilePath());
        };
        DashboardPageViewHost.DashboardOpenDiagnosticsButton.Click += async (_, _) =>
        {
            ShowPage(DiagnosticsPage);
            await RunDiagnosticsAsync();
        };
        DashboardPageViewHost.DashboardEventCenterList.ItemsSource = _twitchEventItems;
        _dashboardResourceTimer.Tick += (_, _) => RefreshDashboardResourceUsage();
        _dashboardLiveRefreshTimer.Tick += async (_, _) =>
            await RefreshDashboardLiveDataAsync();
        _obsPreviewRefreshTimer.Tick += async (_, _) =>
            await RefreshObsPreviewTickAsync();
        _connectionWatchdogTimer.Tick += async (_, _) => await RunConnectionWatchdogAsync();
        _connectionWatchdogTimer.Start();
        _dashboardResourceTimer.Start();
        _dashboardLiveRefreshTimer.Start();
        _obsPreviewRefreshTimer.Start();
        Loaded += async (_, _) =>
        {
            await RunStartupStepSafelyAsync("Dashboard-Livedaten laden", async () =>
            {
                RefreshDashboardServiceActionButtons();
                await RefreshDashboardLiveDataAsync();
                SetActiveNavigationButton(DashboardButton);
            });
        };

        DashboardPageViewHost.DashboardSpotifyPreviousButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardPageViewHost.DashboardSpotifyPreviousButton,
                "Spotify: vorheriger Titel",
                () => ExecuteSpotifyAsync(() => _spotifyModule.PreviousAsync()));
        DashboardPageViewHost.DashboardSpotifyPlayButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardPageViewHost.DashboardSpotifyPlayButton,
                "Spotify: Wiedergabe",
                () => ExecuteSpotifyAsync(() => _spotifyModule.ResumeAsync()));
        DashboardPageViewHost.DashboardSpotifyPauseButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardPageViewHost.DashboardSpotifyPauseButton,
                "Spotify: Pause",
                () => ExecuteSpotifyAsync(() => _spotifyModule.PauseAsync()));
        DashboardPageViewHost.DashboardSpotifyNextButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardPageViewHost.DashboardSpotifyNextButton,
                "Spotify: nächster Titel",
                () => ExecuteSpotifyAsync(() => _spotifyModule.NextAsync()));
        DashboardPageViewHost.DashboardSpotifyShuffleButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardPageViewHost.DashboardSpotifyShuffleButton,
                "Spotify: Zufallswiedergabe",
                () => ExecuteSpotifyAsync(async () =>
                {
                    bool enabled = !_spotifyModule.GetSnapshot().Playback.ShuffleEnabled;
                    await _spotifyModule.SetShuffleAsync(enabled);
                    await RefreshSpotifyAsync();
                    AddDashboardNotification(
                        $"Spotify-Zufallswiedergabe wurde {(enabled ? "eingeschaltet" : "ausgeschaltet")}.",
                        "Info");
                }));
        DashboardPageViewHost.DashboardSpotifyProgressBar.PreviewMouseLeftButtonUp += async (_, _) =>
        {
            if (_updatingSpotifyUi || !DashboardPageViewHost.DashboardSpotifyProgressBar.IsEnabled)
            {
                return;
            }

            int targetMs = (int)Math.Round(DashboardPageViewHost.DashboardSpotifyProgressBar.Value);
            DashboardPageViewHost.DashboardSpotifyProgressBar.IsEnabled = false;
            try
            {
                await ExecuteSpotifyAsync(() => _spotifyModule.SeekAsync(targetMs));
            }
            finally
            {
                DashboardPageViewHost.DashboardSpotifyProgressBar.IsEnabled = true;
            }
        };
        DashboardPageViewHost.DashboardSpotifyRepeatButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardPageViewHost.DashboardSpotifyRepeatButton,
                "Spotify: Wiederholung",
                () => ExecuteSpotifyAsync(async () =>
                {
                    string current = _spotifyModule.GetSnapshot().Playback.RepeatMode;
                    string next = current?.ToLowerInvariant() switch
                    {
                        "off" => "context",
                        "context" => "track",
                        _ => "off"
                    };
                    await _spotifyModule.SetRepeatAsync(next);
                    await RefreshSpotifyAsync();
                    string label = next switch
                    {
                        "context" => "Playlist",
                        "track" => "Titel",
                        _ => "Aus"
                    };
                    AddDashboardNotification($"Spotify-Wiederholung: {label}.", "Info");
                }));
        DashboardPageViewHost.DashboardObsStartStreamButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardPageViewHost.DashboardObsStartStreamButton,
                "Stream starten",
                StartObsStreamAsync);
        DashboardPageViewHost.DashboardObsStopStreamButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardPageViewHost.DashboardObsStopStreamButton,
                "Stream beenden",
                () => StopObsStreamAsync());
        DashboardHeaderStreamActionButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardHeaderStreamActionButton,
                "Stream umschalten",
                ToggleDashboardHeaderStreamAsync);
        DashboardPageViewHost.DashboardAddSceneButton.Click += async (_, _) => await AddDashboardSceneButtonAsync();
        DashboardPageViewHost.DashboardObsScenePreviewSizeBox.SelectionChanged += async (_, _) =>
            await ApplyDashboardObsScenePreviewSizeFromUiAsync();
        DashboardPageViewHost.DashboardRaidEnabledBox.Checked += async (_, _) =>
        {
            if (!_settingsUiLoaded)
            {
                return;
            }

            _settings.Twitch.RaidOnStreamEnd = true;
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidEnabledBox.IsChecked = true;
            UpdateDashboardRaidControlsVisibility();
            await _settingsStore.SaveAsync(_settings);
        };
        DashboardPageViewHost.DashboardRaidEnabledBox.Unchecked += async (_, _) =>
        {
            if (!_settingsUiLoaded)
            {
                return;
            }

            _settings.Twitch.RaidOnStreamEnd = false;
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidEnabledBox.IsChecked = false;
            UpdateDashboardRaidControlsVisibility();
            await _settingsStore.SaveAsync(_settings);
        };
        DashboardPageViewHost.DashboardRaidChannelBox.SelectionChanged += async (_, _) =>
        {
            if (!_settingsUiLoaded)
            {
                return;
            }

            if (DashboardPageViewHost.DashboardRaidChannelBox.SelectedItem is string channel)
            {
                _settings.Twitch.SelectedRaidChannel = channel;
                ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidTargetBox.SelectedItem = channel;
                await RefreshRaidTargetStatusAsync(channel);
                await _settingsStore.SaveAsync(_settings);
            }
        };
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidTargetBox.SelectionChanged += async (_, _) =>
        {
            if (ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidTargetBox.SelectedItem is string channel)
            {
                _settings.Twitch.SelectedRaidChannel = channel;
                DashboardPageViewHost.DashboardRaidChannelBox.SelectedItem = channel;
                await RefreshRaidTargetStatusAsync(channel);
            }
        };
        DashboardPageViewHost.DashboardOpenRaidChannelButton.Click += (_, _) => OpenSelectedRaidChannel();
        DashboardPageViewHost.DashboardJoinStreamTogetherButton.Click += (_, _) =>
            OpenConfiguredTarget(
                string.IsNullOrWhiteSpace(_settings.Twitch.CreatorDashboardUrl)
                    ? "https://dashboard.twitch.tv/stream-manager"
                    : _settings.Twitch.CreatorDashboardUrl,
                "Twitch Stream Together");
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchOpenRaidChannelButton.Click += (_, _) => OpenSelectedRaidChannel();
        DashboardPageViewHost.DashboardCancelRaidButton.Click += async (_, _) => await CancelActiveRaidAsync();
        DashboardPageViewHost.DashboardStartRaidButton.Click += async (_, _) => await ExecuteRaidFromDashboardAsync();
        DashboardPageViewHost.DashboardPlanStreamEndButton.Click += async (_, _) => await StartPlannedStreamEndAsync();
        DashboardPageViewHost.DashboardCancelPlannedStreamEndButton.Click += (_, _) => CancelPlannedStreamEnd();
        DashboardPageViewHost.DashboardSkipRaidAndStopButton.Click += (_, _) => SkipRaidAndFinishStreamEnd();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchAddRaidChannelButton.Click += async (_, _) => await AddRaidChannelAsync();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRemoveRaidChannelButton.Click += async (_, _) => await RemoveSelectedRaidChannelAsync();
        DashboardPageViewHost.DashboardSpotifyVolumeSlider.ValueChanged += async (_, _) =>
        {
            DashboardPageViewHost.DashboardSpotifyVolumeText.Text = $"{(int)Math.Round(DashboardPageViewHost.DashboardSpotifyVolumeSlider.Value)} %";
            if (!_updatingSpotifyUi)
            {
                await QueueSpotifyVolumeUpdateAsync((int)Math.Round(DashboardPageViewHost.DashboardSpotifyVolumeSlider.Value));
            }
        };
        DashboardPageViewHost.DashboardSendTwitchChatButton.Click += async (_, _) =>
        {
            SettingsPageViewHost.TwitchChatMessageBox.Text =
                DashboardPageViewHost.DashboardTwitchChatMessageBox.Text;

            await SendTwitchChatAsync();

            DashboardPageViewHost.DashboardTwitchChatMessageBox.Clear();
        };

        DashboardPageViewHost.DashboardTimeoutUserButton.Click += async (_, _) => await ModerateTwitchUserAsync(DashboardPageViewHost.DashboardModerationUserBox.Text, false, "10", null);
        DashboardPageViewHost.DashboardBanUserButton.Click += async (_, _) => await ModerateTwitchUserAsync(DashboardPageViewHost.DashboardModerationUserBox.Text, true, null, null);
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTimeoutUserButton.Click += async (_, _) => await ModerateTwitchUserAsync(ServicesPageViewHost.TwitchServiceViewHost.ServicesModerationUserBox.Text, false, ServicesPageViewHost.TwitchServiceViewHost.ServicesModerationDurationBox.Text, ServicesPageViewHost.TwitchServiceViewHost.ServicesModerationReasonBox.Text);
        ServicesPageViewHost.TwitchServiceViewHost.ServicesBanUserButton.Click += async (_, _) => await ModerateTwitchUserAsync(ServicesPageViewHost.TwitchServiceViewHost.ServicesModerationUserBox.Text, true, ServicesPageViewHost.TwitchServiceViewHost.ServicesModerationDurationBox.Text, ServicesPageViewHost.TwitchServiceViewHost.ServicesModerationReasonBox.Text);
        ServicesPageViewHost.TwitchServiceViewHost.ServicesUnbanUserButton.Click += async (_, _) => await UnbanTwitchUserAsync(ServicesPageViewHost.TwitchServiceViewHost.ServicesModerationUserBox.Text);

        DashboardPageViewHost.DashboardCommandPrepareButton.Click += async (_, _) => await PrepareStreamAsync();
        DashboardPageViewHost.DashboardCommandStartButton.Click += async (_, _) => await StartObsStreamAsync();
        DashboardPageViewHost.DashboardCommandStopButton.Click += async (_, _) => await StopObsStreamAsync();
        DashboardPageViewHost.DashboardRunPreflightButton.Click += async (_, _) => await RunDashboardPreflightAsync();
        DashboardPageViewHost.DashboardSceneStartButton.Click += async (_, _) => await SwitchDashboardConfiguredSceneAsync(_settings.Obs.StartScene);
        DashboardPageViewHost.DashboardSceneLiveButton.Click += async (_, _) => await SwitchDashboardConfiguredSceneAsync(_settings.Obs.LiveScene);
        DashboardPageViewHost.DashboardScenePauseButton.Click += async (_, _) => await SwitchDashboardConfiguredSceneAsync(_settings.Obs.PauseScene);
        DashboardPageViewHost.DashboardSceneEndButton.Click += async (_, _) => await SwitchDashboardConfiguredSceneAsync(_settings.Obs.EndScene);
        DashboardPageViewHost.DashboardObsAudioInputBox.SelectionChanged += async (_, _) => await RefreshDashboardObsAudioStateAsync();
        DashboardPageViewHost.DashboardObsAudioMuteButton.Click += async (_, _) => await SetDashboardObsAudioMuteAsync(true);
        DashboardPageViewHost.DashboardObsAudioUnmuteButton.Click += async (_, _) => await SetDashboardObsAudioMuteAsync(false);
        DashboardPageViewHost.DashboardObsAudioSetVolumeButton.Click += async (_, _) => await SetDashboardObsAudioVolumeAsync();
        DashboardPageViewHost.DashboardOpenObsMixerButton.Click += (_, _) => ShowPage(ServicesPage);
        DashboardPageViewHost.DashboardRefreshRaidAssistantButton.Click += async (_, _) =>
        {
            string channel = DashboardPageViewHost.DashboardRaidChannelBox.SelectedItem as string ?? _settings.Twitch.SelectedRaidChannel;
            if (!string.IsNullOrWhiteSpace(channel))
            {
                await RefreshRaidTargetStatusAsync(channel);
                DashboardPageViewHost.DashboardRaidAssistantText.Text = DashboardPageViewHost.DashboardRaidTargetStatusText.Text;
            }
        };
        DashboardPageViewHost.DashboardOpenProfilesButton.Click += async (_, _) => { ShowPage(ProfilesPage); await RefreshProfilesAsync(); };
        DashboardPageViewHost.DashboardApplyProfileButton.Click += async (_, _) => await ApplyDashboardProfileAndPrepareAsync();
        DashboardPageViewHost.DashboardOpenWorkflowButton.Click += (_, _) => ShowPage(WorkflowPage);
        DashboardPageViewHost.DashboardClearNotificationsButton.Click += async (_, _) =>
        {
            _dashboardNotifications.Clear();
            RefreshDashboardNotificationView();
            await SaveDashboardNotificationsAsync();
        };
        DashboardPageViewHost.DashboardMarkNotificationsReadButton.Click += async (_, _) =>
        {
            foreach (DashboardNotificationEntry item in _dashboardNotifications)
            {
                item.IsRead = true;
            }

            RefreshDashboardNotificationView();
            await SaveDashboardNotificationsAsync();
        };
        DashboardPageViewHost.DashboardNotificationFilterBox.SelectionChanged += (_, _) => RefreshDashboardNotificationView();
        DashboardPageViewHost.DashboardRefreshHistoryButton.Click += async (_, _) => await LoadStreamHistoryAsync();
        DashboardPageViewHost.DashboardOpenHistoryFolderButton.Click += (_, _) => OpenStreamHistoryFolder();
        DashboardPageViewHost.DashboardOpenStreamDeckSettingsButton.Click += (_, _) => ShowPage(SettingsPage);
        DashboardPageViewHost.DashboardOpenServicesAdvancedButton.Click += (_, _) => ShowPage(ServicesPage);
        DashboardPageViewHost.DashboardOpenDiagnosticsAdvancedButton.Click += async (_, _) => { ShowPage(DiagnosticsPage); await RunDiagnosticsAsync(); };
        DashboardPageViewHost.DashboardOpenSettingsAdvancedButton.Click += (_, _) => ShowPage(SettingsPage);
        DashboardPageViewHost.DashboardOpenProfilesAdvancedButton.Click += async (_, _) => { ShowPage(ProfilesPage); await RefreshProfilesAsync(); };
    }
}
