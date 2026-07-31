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

public partial class MainWindow
{
    private async Task LoadSettingsAsync()
    {
        _loadingSettingsIntoUi = true;
        _settings = await _settingsApplicationService.LoadAsync();
        bool migratedSceneAutomation = MigrateLegacyStartToGameAutomation();
        if (migratedSceneAutomation)
        {
            await _settingsStore.SaveAsync(_settings);
        }
        _settings.Product.Version = GetCurrentProductVersion();
        if (string.IsNullOrWhiteSpace(_settings.Updates.Channel))
        {
            _settings.Updates.Channel = _settings.Product.UpdateChannel;
        }
        RefreshObsAudioProfilesUi();
        // Spotify-Laufzeitdaten werden grundsätzlich in die konfigurierte JSON geschrieben.
        _settings.Spotify.OverlayEnabled = true;
        RefreshTimedAutomationRules();
        RefreshRunOfShowSteps();

        _generalSettingsPageViewModel.Load(
            _settings.Branding,
            _settings.General);
        ApplyTitleBarChrome();
        _connectionWatchdogTimer.Interval = TimeSpan.FromSeconds(
            Math.Clamp(_settings.General.ConnectionWatchdogSeconds, 5, 300));
        SettingsPageViewHost.DashboardAutoFocusOnStreamStartBox.IsChecked =
            _settings.Dashboard.AutoFocusModeOnStreamStart;
        SettingsPageViewHost.DashboardAutoExitFocusOnStreamEndBox.IsChecked =
            _settings.Dashboard.AutoExitFocusModeOnStreamEnd;
        SettingsPageViewHost.DashboardShowServiceStatusBox.IsChecked = _settings.Dashboard.ShowServiceStatus;
        SettingsPageViewHost.DashboardShowStreamControlsBox.IsChecked = _settings.Dashboard.ShowStreamControls;
        SettingsPageViewHost.DashboardShowLivePanelsBox.IsChecked = _settings.Dashboard.ShowLivePanels;
        SettingsPageViewHost.DashboardShowQuickServicesBox.IsChecked = _settings.Dashboard.ShowQuickServices;
        SettingsPageViewHost.DashboardShowWorkflowRailBox.IsChecked = _settings.Dashboard.ShowWorkflowRail;
        SettingsPageViewHost.DashboardShowAdvancedToolsBox.IsChecked = _settings.Dashboard.ShowAdvancedTools;
        SettingsPageViewHost.DashboardShowNotificationsBox.IsChecked = _settings.Dashboard.ShowNotifications;
        SettingsPageViewHost.DashboardShowStreamHistoryBox.IsChecked = _settings.Dashboard.ShowStreamHistory;
        LoadDashboardModuleOrderEditor();
        ApplyDashboardModuleOrder();
        ApplyDashboardModuleSizes();
        ApplyDashboardObsScenePreviewSize();
        ApplyDashboardLayout();

        SettingsPageViewHost.ObsHostBox.Text = _settings.Obs.Host;
        SettingsPageViewHost.ObsPortBox.Text = _settings.Obs.Port.ToString();
        SettingsPageViewHost.ObsAutoConnectBox.IsChecked = _settings.Obs.AutoConnect;
        SettingsPageViewHost.ObsConnectOnPrepareBox.IsChecked = _settings.Obs.ConnectOnPrepare;
        SettingsPageViewHost.ObsExecutablePathBox.Text = _settings.Obs.ExecutablePath;
        SettingsPageViewHost.ObsPasswordBox.Password = await _secretStore.LoadAsync("obs.password") ?? "";

        SettingsPageViewHost.TwitchClientIdBox.Text = _settings.Twitch.ClientId;
        SettingsPageViewHost.TwitchChannelBox.Text = _settings.Twitch.ChannelName;
        SettingsPageViewHost.TwitchAutoConnectBox.IsChecked = _settings.Twitch.AutoConnect;
        SettingsPageViewHost.TwitchConnectOnPrepareBox.IsChecked = _settings.Twitch.ConnectOnPrepare;
        SettingsPageViewHost.TwitchCreatorDashboardUrlBox.Text = _settings.Twitch.CreatorDashboardUrl;
        SettingsPageViewHost.TwitchChatEnabledBox.IsChecked = _settings.Twitch.EnableChat;
        SettingsPageViewHost.TwitchChatUiBuiltInRadio.IsChecked = _settings.Twitch.ChatUiMode != TwitchChatUiMode.EmbeddedWeb;
        SettingsPageViewHost.TwitchChatUiEmbeddedWebRadio.IsChecked = _settings.Twitch.ChatUiMode == TwitchChatUiMode.EmbeddedWeb;
        SettingsPageViewHost.TwitchEventSubEnabledBox.IsChecked = _settings.Twitch.EnableEventSub;
        NormalizeTwitchChattersRefreshSettings();
        SettingsPageViewHost.TwitchChattersRefreshLowBox.Text = _settings.Twitch.ChattersRefreshSecondsLow.ToString();
        SettingsPageViewHost.TwitchChattersRefreshHighBox.Text = _settings.Twitch.ChattersRefreshSecondsHigh.ToString();
        SettingsPageViewHost.TwitchChattersRefreshThresholdBox.Text = _settings.Twitch.ChattersRefreshViewerThreshold.ToString();
        ApplyTwitchUsersRefreshInterval();

        SettingsPageViewHost.SpotifyClientIdBox.Text = _settings.Spotify.ClientId;
        SettingsPageViewHost.SpotifyRedirectUriBox.Text = _settings.Spotify.RedirectUri;
        SettingsPageViewHost.SpotifyAutoConnectBox.IsChecked = _settings.Spotify.AutoConnect;
        SettingsPageViewHost.SpotifyConnectOnPrepareBox.IsChecked = _settings.Spotify.ConnectOnPrepare;
        SettingsPageViewHost.SpotifyExecutablePathBox.Text = _settings.Spotify.ExecutablePath;
        SelectMusicPlayerProviderRadio(_settings.MusicPlayer.ProviderId);
        SettingsPageViewHost.YouTubeMusicBridgePortBox.Text = _settings.YouTubeMusic.BridgePort.ToString();
        SettingsPageViewHost.YouTubeMusicAutoConnectBox.IsChecked = _settings.YouTubeMusic.AutoConnect;
        SettingsPageViewHost.YouTubeMusicConnectOnPrepareBox.IsChecked = _settings.YouTubeMusic.ConnectOnPrepare;
        UpdateMusicPlayerSettingsVisibility();
        ApplyMusicProviderUiState();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyAutoTransferPreferredBox.IsChecked = _settings.Spotify.AutoTransferToPreferredDevice;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyUseActiveFallbackBox.IsChecked = _settings.Spotify.UseActiveDeviceWhenPreferredUnavailable;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifySmartAutomationBox.IsChecked = _settings.Spotify.SmartAutomationEnabled;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyHealthMonitorBox.IsChecked = _settings.Spotify.HealthMonitorEnabled;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyAutoRecoverBox.IsChecked = _settings.Spotify.AutoRecoverPlayback;
        SettingsPageViewHost.StreamerBotHostBox.Text = _settings.StreamerBot.Host;
        SettingsPageViewHost.StreamerBotPortBox.Text = _settings.StreamerBot.Port.ToString();
        SettingsPageViewHost.StreamerBotEndpointBox.Text = _settings.StreamerBot.Endpoint;
        SettingsPageViewHost.StreamerBotPasswordBox.Password = _settings.StreamerBot.Password;
        SettingsPageViewHost.StreamerBotAutoConnectBox.IsChecked = _settings.StreamerBot.AutoConnect;
        SettingsPageViewHost.StreamerBotConnectOnPrepareBox.IsChecked = _settings.StreamerBot.ConnectOnPrepare;
        SettingsPageViewHost.StreamerBotExecutablePathBox.Text = _settings.StreamerBot.ExecutablePath;
        _alertRuntimePageViewModel.Load(
            _settings.Alerts,
            _settings.StreamerBot);
        BindStreamerBotActionSelectors();
        SettingsPageViewHost.SpotifyVolumeBox.Text = _settings.Spotify.StartVolumePercent.ToString();
        SettingsPageViewHost.SpotifyFadeOutBox.IsChecked = _settings.Spotify.FadeOutEnabled;
        SettingsPageViewHost.SpotifyPauseAfterFadeBox.IsChecked = _settings.Spotify.PauseAfterFadeOut;
        SettingsPageViewHost.SpotifyFadeOutSecondsBox.Text = _settings.Spotify.FadeOutSeconds.ToString();
        SettingsPageViewHost.SpotifyFadeInSecondsBox.Text = _settings.Spotify.FadeInSeconds.ToString();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyHideMutedBox.IsChecked = _settings.Spotify.OverlayHideWhenMuted;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDetectObsMuteBox.IsChecked = _settings.Spotify.OverlayMuteDetectionObsSource;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDetectVolumeMuteBox.IsChecked = _settings.Spotify.OverlayMuteDetectionSpotifyVolume;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyHidePausedBox.IsChecked = _settings.Spotify.OverlayHideWhenPaused;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyObsAudioSourceBox.Text = string.IsNullOrWhiteSpace(_settings.Spotify.OverlayObsAudioSource) ? "Spotify" : _settings.Spotify.OverlayObsAudioSource;
        // Den vom Benutzer gespeicherten Zustand wiederherstellen. Zuvor wurde
        // der Haken bei jedem Programmstart zwangsweise auf einen festen Wert
        // gesetzt und die Einstellung dadurch praktisch ignoriert.
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlayEnabledBox.IsChecked = true;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlayEnabledBox.IsEnabled = false;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlayEnabledBox.ToolTip = "Spotify-Daten werden immer automatisch in die hinterlegte JSON-Datei geschrieben.";
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlayEnabledBox.Visibility = Visibility.Visible;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDataJsonPathBox.Text = string.IsNullOrWhiteSpace(_settings.Overlay.DataFilePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CreatorControlSuite", "Overlay", "data", _settings.Overlay.DataFileName)
            : Environment.ExpandEnvironmentVariables(_settings.Overlay.DataFilePath);
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlaySourceBox.Text = string.IsNullOrWhiteSpace(_settings.Spotify.OverlayObsSource) ? "ccs_spotify" : _settings.Spotify.OverlayObsSource;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlaySceneBox.Text = _settings.Spotify.OverlayObsScene;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyShufflePlaylistBox.IsChecked = _settings.Spotify.ShuffleSelectedPlaylist;
        _spotifyAutomationPageViewModel.Load(
            _settings.Workflow,
            _settings.Spotify,
            _spotifyModule.GetSnapshot().Playlists);
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidEnabledBox.IsChecked = _settings.Twitch.RaidOnStreamEnd;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidCountdownSecondsBox.Text = Math.Max(1, _settings.Twitch.RaidCountdownSeconds).ToString();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidStartTimeoutSecondsBox.Text = RaidStartPolicy
            .ClampTimeoutSeconds(_settings.Twitch.RaidStartTimeoutSeconds)
            .ToString();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchStopStreamAfterRaidBox.IsChecked = _settings.Twitch.StopStreamAfterRaid;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchStopSpotifyAfterRaidBox.IsChecked = _settings.Twitch.StopSpotifyAfterRaid;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidChannelsBox.Text = string.Join(Environment.NewLine, _settings.Twitch.RaidChannels);
        int endSceneSeconds = Math.Max(0, _settings.Twitch.EndSceneDurationSeconds > 0
            ? _settings.Twitch.EndSceneDurationSeconds
            : _settings.Workflow.EndSceneSeconds);
        _settings.Twitch.EndSceneDurationSeconds = endSceneSeconds;
        _settings.Workflow.EndSceneSeconds = endSceneSeconds;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchEndSceneSecondsBox.Text = endSceneSeconds.ToString();
        DashboardPageViewHost.DashboardRaidEnabledBox.IsChecked = _settings.Twitch.RaidOnStreamEnd;
        if (_settings.Twitch.PlannedStreamEndSeconds < 1)
        {
            _settings.Twitch.PlannedStreamEndSeconds =
                Math.Max(1, _settings.Twitch.PlannedStreamEndMinutes) * 60;
            await _settingsStore.SaveAsync(_settings);
        }
        DashboardPageViewHost.DashboardPlannedStreamEndSecondsBox.Text =
            _settings.Twitch.PlannedStreamEndSeconds.ToString();
        if (EnsureDefaultDashboardSceneButtons())
        {
            await _settingsStore.SaveAsync(_settings);
        }
        RebuildDashboardSceneButtons();
        RefreshRaidChannelSelectors();
        UpdateDashboardRaidControlsVisibility();
        UpdateDashboardStreamEndModuleVisibility();

        _twitchGoalsPageViewModel.Load(
            _settings.Obs,
            _settings.Twitch,
            _currentFollowerCount,
            _currentActiveSubscriptionCount);

        _alertLibraryPageViewModel.Load(_settings);
        AlertTypeBox.SelectedItem =
            _alertLibraryPageViewModel.SelectedType;

        await LoadSelectedAlertDefinitionAsync();

        _overlayConnectionSettingsPageViewModel.Load(_settings.Overlay);
        _overlayCanvasPageViewModel.Load(_settings);
        RefreshOverlayWebServerStatusUi();
        _overlayExtensionPacksPageViewModel.Refresh();

        SettingsPageViewHost.StartSceneBox.Text = _settings.Obs.StartScene;
        SettingsPageViewHost.LiveSceneBox.Text = _settings.Obs.LiveScene;
        SettingsPageViewHost.PauseSceneBox.Text = _settings.Obs.PauseScene;
        SettingsPageViewHost.EndSceneBox.Text = _settings.Obs.EndScene;
        SettingsPageViewHost.EndSceneSecondsBox.Text = Math.Max(
            0,
            _settings.Twitch.EndSceneDurationSeconds > 0
                ? _settings.Twitch.EndSceneDurationSeconds
                : _settings.Workflow.EndSceneSeconds).ToString();
        DashboardCountdownSecondsBox.Text = Math.Max(0, _settings.Workflow.StartCountdownSeconds).ToString();
        DashboardCountdownLabelBox.Text = string.IsNullOrWhiteSpace(_settings.Workflow.CountdownLabel)
            ? "Countdown"
            : _settings.Workflow.CountdownLabel;
        RefreshDashboardCountdownIdleDisplay();
        _liveViewerSampleTimer.Interval = TimeSpan.FromSeconds(
            Math.Clamp(_settings.Workflow.ViewerSampleSeconds, 5, 300));

        SettingsPageViewHost.StreamDeckEnabledBox.IsChecked = _settings.StreamDeck.Enabled;
        SettingsPageViewHost.StreamDeckProfileBox.IsChecked = _settings.StreamDeck.AutoInstallProfile;

        await _updatePageViewModel.LoadAsync(_settings.Updates);

        if (_settings.Obs.AutoConnect)
        {
            await ConnectObsAsync(showErrorDialog: false);
        }

        if (_settings.Twitch.AutoConnect &&
            !string.IsNullOrWhiteSpace(_settings.Twitch.ClientId))
        {
            await ConnectTwitchAsync(showErrorDialog: false);
        }

        if (IsSpotifyMusicProvider() &&
            _settings.Spotify.AutoConnect &&
            !string.IsNullOrWhiteSpace(_settings.Spotify.ClientId))
        {
            await ConnectSpotifyAsync(showErrorDialog: false);
        }
        else if (IsYouTubeMusicProvider() && _settings.YouTubeMusic.AutoConnect)
        {
            try
            {
                await _musicPlayerRouter.ApplyProviderAsync(MusicProviderIds.YouTubeMusic);
                await _musicPlayerRouter.ConnectActiveAsync();
            }
            catch
            {
                // Auto-Connect darf den Start nicht abbrechen.
            }
        }
        else
        {
            await _musicPlayerRouter.RefreshFromSettingsAsync();
        }
        if (_settings.StreamerBot.AutoConnect)
        {
            await ConnectStreamerBotAsync();
        }

        await ApplyTwitchChatUiModeAsync();
        _loadingSettingsIntoUi = false;
    }

    private bool MigrateLegacyStartToGameAutomation()
    {
        bool changed = false;
        foreach (TimedAutomationRuleSettings? rule in _settings.Workflow.TimedAutomations.Where(rule =>
                     (rule.Name.StartsWith("Streamstart – Initialisierung", StringComparison.OrdinalIgnoreCase) ||
                      rule.Name.StartsWith("Streamstart – Intro ausblenden", StringComparison.OrdinalIgnoreCase)) &&
                     !string.IsNullOrWhiteSpace(rule.NextRuleId)))
        {
            // Diese Vorlagen besitzen eigene Zeittrigger. Eine zusätzliche
            // Verkettung führt die Folgeregeln sofort und damit zu früh aus.
            rule.NextRuleId = "";
            changed = true;
        }

        foreach (TimedAutomationRuleSettings? rule in _settings.Workflow.TimedAutomations.Where(rule =>
                     rule.Name.StartsWith("Streamstart – Game wechseln", StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(rule.TriggerType, "StreamElapsed", StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(rule.ActionType, "SwitchScene", StringComparison.OrdinalIgnoreCase) &&
                     rule.DelaySeconds == 600))
        {
            rule.TriggerType = "SceneElapsed";
            rule.TriggerScene = string.IsNullOrWhiteSpace(_settings.Obs.StartScene)
                ? "Start"
                : _settings.Obs.StartScene;
            rule.Name = "Startszene – nach 10 Minuten zu " +
                        (string.IsNullOrWhiteSpace(rule.TargetScene) ? "Zielszene" : rule.TargetScene);
            changed = true;
        }

        return changed;
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            _generalSettingsPageViewModel.ApplyTo(
                _settings.Branding,
                _settings.General);

            _settings.Obs.Host = SettingsPageViewHost.ObsHostBox.Text.Trim();
            _settings.Obs.Port = int.Parse(SettingsPageViewHost.ObsPortBox.Text.Trim());
            _settings.Obs.AutoConnect = SettingsPageViewHost.ObsAutoConnectBox.IsChecked == true;
            _settings.Obs.ConnectOnPrepare = SettingsPageViewHost.ObsConnectOnPrepareBox.IsChecked == true;
            _settings.Obs.ExecutablePath = SettingsPageViewHost.ObsExecutablePathBox.Text.Trim();
            _settings.Obs.StartScene = SettingsPageViewHost.StartSceneBox.Text.Trim();
            _settings.Obs.LiveScene = SettingsPageViewHost.LiveSceneBox.Text.Trim();
            _settings.Obs.PauseScene = SettingsPageViewHost.PauseSceneBox.Text.Trim();
            _settings.Obs.EndScene = SettingsPageViewHost.EndSceneBox.Text.Trim();

            _settings.Twitch.ClientId = SettingsPageViewHost.TwitchClientIdBox.Text.Trim();
            _settings.Twitch.ChannelName = SettingsPageViewHost.TwitchChannelBox.Text.Trim();
            _settings.Twitch.AutoConnect = SettingsPageViewHost.TwitchAutoConnectBox.IsChecked == true;
            _settings.Twitch.ConnectOnPrepare = SettingsPageViewHost.TwitchConnectOnPrepareBox.IsChecked == true;
            _settings.Twitch.CreatorDashboardUrl = SettingsPageViewHost.TwitchCreatorDashboardUrlBox.Text.Trim();
            _settings.Twitch.EnableChat = SettingsPageViewHost.TwitchChatEnabledBox.IsChecked == true;
            _settings.Twitch.ChatUiMode = SettingsPageViewHost.TwitchChatUiEmbeddedWebRadio.IsChecked == true
                ? TwitchChatUiMode.EmbeddedWeb
                : TwitchChatUiMode.BuiltIn;
            _settings.Twitch.EnableEventSub = SettingsPageViewHost.TwitchEventSubEnabledBox.IsChecked == true;
            _settings.Twitch.ChattersRefreshSecondsLow = int.TryParse(
                    SettingsPageViewHost.TwitchChattersRefreshLowBox.Text.Trim(),
                    out int chattersLow)
                ? chattersLow
                : _settings.Twitch.ChattersRefreshSecondsLow;
            _settings.Twitch.ChattersRefreshSecondsHigh = int.TryParse(
                    SettingsPageViewHost.TwitchChattersRefreshHighBox.Text.Trim(),
                    out int chattersHigh)
                ? chattersHigh
                : _settings.Twitch.ChattersRefreshSecondsHigh;
            _settings.Twitch.ChattersRefreshViewerThreshold = int.TryParse(
                    SettingsPageViewHost.TwitchChattersRefreshThresholdBox.Text.Trim(),
                    out int chattersThreshold)
                ? chattersThreshold
                : _settings.Twitch.ChattersRefreshViewerThreshold;
            NormalizeTwitchChattersRefreshSettings();
            SettingsPageViewHost.TwitchChattersRefreshLowBox.Text = _settings.Twitch.ChattersRefreshSecondsLow.ToString();
            SettingsPageViewHost.TwitchChattersRefreshHighBox.Text = _settings.Twitch.ChattersRefreshSecondsHigh.ToString();
            SettingsPageViewHost.TwitchChattersRefreshThresholdBox.Text = _settings.Twitch.ChattersRefreshViewerThreshold.ToString();
            ApplyTwitchUsersRefreshInterval();

            _settings.Spotify.ClientId = SettingsPageViewHost.SpotifyClientIdBox.Text.Trim();
            _settings.Spotify.RedirectUri = SettingsPageViewHost.SpotifyRedirectUriBox.Text.Trim();
            _settings.Spotify.AutoConnect = SettingsPageViewHost.SpotifyAutoConnectBox.IsChecked == true;
            _settings.Spotify.ConnectOnPrepare = SettingsPageViewHost.SpotifyConnectOnPrepareBox.IsChecked == true;
            _settings.Spotify.ExecutablePath = SettingsPageViewHost.SpotifyExecutablePathBox.Text.Trim();
            _settings.MusicPlayer ??= new MusicPlayerSettings();
            _settings.YouTubeMusic ??= new YouTubeMusicSettings();
            _settings.MusicPlayer.ProviderId = GetSelectedMusicPlayerProviderId();
            if (!int.TryParse(SettingsPageViewHost.YouTubeMusicBridgePortBox.Text.Trim(), out int ytPort) || ytPort is <= 0 or > 65535)
            {
                throw new InvalidOperationException("Ungültiger YouTube-Music-Bridge-Port.");
            }

            _settings.YouTubeMusic.BridgePort = ytPort;
            _settings.YouTubeMusic.AutoConnect = SettingsPageViewHost.YouTubeMusicAutoConnectBox.IsChecked == true;
            _settings.YouTubeMusic.ConnectOnPrepare = SettingsPageViewHost.YouTubeMusicConnectOnPrepareBox.IsChecked == true;

            // Der im Spotify-Bereich eingetragene Laufzeit-JSON-Pfad muss auch beim
            // allgemeinen Speichern erhalten bleiben. Sonst schreibt der laufende
            // Spotify-Refresh weiter in die zuvor konfigurierte Standarddatei.
            string? spotifyDataPath = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDataJsonPathBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(spotifyDataPath))
            {
                spotifyDataPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(spotifyDataPath));
                if (!string.Equals(Path.GetExtension(spotifyDataPath), ".json", StringComparison.OrdinalIgnoreCase))
                {
                    spotifyDataPath += ".json";
                }

                _settings.Overlay.DataFilePath = spotifyDataPath;
                _settings.Overlay.DataFileName = Path.GetFileName(spotifyDataPath);
            }

            _settings.StreamerBot.Host = SettingsPageViewHost.StreamerBotHostBox.Text.Trim();
            _settings.StreamerBot.Port = int.Parse(SettingsPageViewHost.StreamerBotPortBox.Text.Trim());
            _settings.StreamerBot.Endpoint = SettingsPageViewHost.StreamerBotEndpointBox.Text.Trim();
            _settings.StreamerBot.Password = SettingsPageViewHost.StreamerBotPasswordBox.Password;
            _settings.StreamerBot.AutoConnect = SettingsPageViewHost.StreamerBotAutoConnectBox.IsChecked == true;
            _settings.StreamerBot.ConnectOnPrepare = SettingsPageViewHost.StreamerBotConnectOnPrepareBox.IsChecked == true;
            _settings.StreamerBot.ExecutablePath = SettingsPageViewHost.StreamerBotExecutablePathBox.Text.Trim();
            if (!_alertRuntimePageViewModel.TryApplyTo(
                    _settings.Alerts,
                    _settings.StreamerBot,
                    out string alertRuntimeSettingsError))
            {
                throw new InvalidOperationException(
                    alertRuntimeSettingsError);
            }
            SyncStreamerBotActionSelectorText();
            _settings.Spotify.StartVolumePercent = int.Parse(SettingsPageViewHost.SpotifyVolumeBox.Text.Trim());
            _settings.Spotify.FadeOutEnabled = SettingsPageViewHost.SpotifyFadeOutBox.IsChecked == true;
            _settings.Spotify.PauseAfterFadeOut = SettingsPageViewHost.SpotifyPauseAfterFadeBox.IsChecked == true;
            _settings.Spotify.FadeOutSeconds = int.Parse(SettingsPageViewHost.SpotifyFadeOutSecondsBox.Text.Trim());
            _settings.Spotify.FadeInSeconds = int.Parse(SettingsPageViewHost.SpotifyFadeInSecondsBox.Text.Trim());
            _settings.Spotify.OverlayShowTitle = true;
            _settings.Spotify.OverlayShowArtist = true;
            _settings.Spotify.OverlayShowAlbumCover = true;
            _settings.Spotify.OverlayShowProgress = true;
            _settings.Spotify.OverlayHideWhenPaused = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyHidePausedBox.IsChecked == true;
            _settings.Spotify.OverlayHideWhenMuted = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyHideMutedBox.IsChecked == true;
            _settings.Spotify.OverlayMuteDetectionObsSource = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDetectObsMuteBox.IsChecked == true;
            _settings.Spotify.OverlayMuteDetectionSpotifyVolume = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDetectVolumeMuteBox.IsChecked == true;
            _settings.Spotify.OverlayObsAudioSource = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyObsAudioSourceBox.Text?.Trim() ?? "Spotify";
            _settings.Spotify.OverlayEnabled = true;
            _spotifyAutomationPageViewModel.ApplyTo(
                _settings.Workflow,
                _settings.Spotify);
            _settings.Spotify.ShuffleSelectedPlaylist =
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyShufflePlaylistBox.IsChecked == true;
            ApplyTwitchEndFieldsToSettings();
            _twitchGoalsPageViewModel.ApplyTo(
                _settings.Obs,
                _settings.Twitch);

            if (SettingsPageViewHost.SpotifyDeviceBox.SelectedItem is SpotifyDevice selectedDevice)
            {
                _settings.Spotify.PreferredDeviceId = selectedDevice.Id;
            }

            if (SettingsPageViewHost.SpotifyPlaylistBox.SelectedItem is SpotifyPlaylist selectedPlaylist)
            {
                _settings.Spotify.StartPlaylistUri = selectedPlaylist.Uri;
            }

            SaveAlertDefinitionToSettings();

            if (!_overlayConnectionSettingsPageViewModel.TryApplyTo(
                    _settings.Overlay,
                    out string overlaySettingsError))
            {
                throw new InvalidOperationException(overlaySettingsError);
            }

            _settings.Overlay.EnsureCanvasesMigrated();
            _overlayCanvasPageViewModel.UpdatePort(
                _settings.Overlay.WebServerPort);
            await _overlayModule.ChatHistory.SyncCapacityToHubAsync();
            await RefreshChatEmoteCatalogFromSettingsAsync();

            _settings.Workflow.EndSceneSeconds = int.Parse(SettingsPageViewHost.EndSceneSecondsBox.Text.Trim());
            _settings.Twitch.EndSceneDurationSeconds = _settings.Workflow.EndSceneSeconds;
            PersistDashboardCountdownSettings();

            _settings.StreamDeck.Enabled = SettingsPageViewHost.StreamDeckEnabledBox.IsChecked == true;
            _settings.StreamDeck.AutoInstallProfile = SettingsPageViewHost.StreamDeckProfileBox.IsChecked == true;

            _updatePageViewModel.ApplyTo(_settings.Updates);
            _settings.Product.UpdateChannel = _settings.Updates.Channel;
            _settings.Product.Version = GetCurrentProductVersion();

            ValidationReport validation =
                _settingsApplicationService.Validate(_settings);

            if (!validation.IsValid)
            {
                ValidationIssue firstError = validation.Issues.First(
                    issue =>
                        issue.Severity ==
                        ValidationSeverity.Error);

                throw new InvalidOperationException(
                    firstError.Section +
                    ": " +
                    firstError.Message +
                    " " +
                    firstError.SuggestedFix);
            }

            RebuildDashboardSceneButtons();
            RefreshRaidChannelSelectors();

            await _settingsApplicationService.SaveAsync(_settings);
            _connectionWatchdogTimer.Interval = TimeSpan.FromSeconds(
                Math.Clamp(
                    _settings.General.ConnectionWatchdogSeconds,
                    5,
                    300));
            await RestartOverlayWebServerFromSettingsAsync();
            await _musicPlayerRouter.ApplyProviderAsync(_settings.MusicPlayer.ProviderId);
            ApplyMusicProviderUiState();
            await RefreshMusicPlayerUiAsync();

            await _secretStore.SaveAsync("obs.password", SettingsPageViewHost.ObsPasswordBox.Password);

            await ApplyTwitchChatUiModeAsync();

            _appLogger.Write(
                AppLogLevel.Information,
                "Settings",
                "Einstellungen wurden gespeichert.");

            SettingsPageViewHost.ApplySaveResult(
                "Einstellungen gespeichert.",
                success: true);
        }
        catch (Exception exception)
        {
            SettingsPageViewHost.ApplySaveResult(
                exception.Message,
                success: false);
        }
    }
}
