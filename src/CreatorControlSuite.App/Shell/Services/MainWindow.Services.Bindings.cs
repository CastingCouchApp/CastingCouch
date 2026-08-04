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
    private void InitializeServiceBindings()
    {
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchDashboardButton.Click += (_, _) => OpenConfiguredTarget(_settings.Twitch.CreatorDashboardUrl, "Twitch Creator Dashboard");
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchConnectButton.Click += async (_, _) => await ConnectTwitchAsync();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchDisconnectButton.Click += async (_, _) => await DisconnectTwitchAsync();

        ServicesPageViewHost.ObsServiceViewHost.ServicesObsLaunchButton.Click += (_, _) => LaunchConfiguredExecutable(_settings.Obs.ExecutablePath, "OBS");
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsConnectButton.Click += async (_, _) => await ConnectObsAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsDisconnectButton.Click += async (_, _) => await DisconnectObsAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsRefreshButton.Click += async (_, _) => await RefreshObsAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsApplyTransitionButton.Click += async (_, _) => await ApplySelectedObsTransitionAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsStartStreamButton.Click += async (_, _) => await StartObsStreamAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsStopStreamButton.Click += async (_, _) => await StopObsStreamAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsControlStartStreamButton.Click += async (_, _) => await ExecuteObsControlAsync("Stream starten", () => _obsClient.StartStreamAsync());
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsControlStopStreamButton.Click += async (_, _) => await ExecuteObsControlAsync("Stream stoppen", () => _obsClient.StopStreamAsync());
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsStartRecordButton.Click += async (_, _) => await ExecuteObsControlAsync("Aufnahme starten", () => _obsClient.StartRecordAsync());
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsPauseRecordButton.Click += async (_, _) => await ToggleObsRecordPauseAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsStopRecordButton.Click += async (_, _) => await ExecuteObsControlAsync("Aufnahme stoppen", () => _obsClient.StopRecordAsync());
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsStartReplayButton.Click += async (_, _) => await ExecuteObsControlAsync("Replay Buffer starten", () => _obsClient.StartReplayBufferAsync());
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSaveReplayButton.Click += async (_, _) => await ExecuteObsControlAsync("Replay speichern", () => _obsClient.SaveReplayBufferAsync());
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsStopReplayButton.Click += async (_, _) => await ExecuteObsControlAsync("Replay Buffer stoppen", () => _obsClient.StopReplayBufferAsync());
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsStartVirtualCamButton.Click += async (_, _) => await ExecuteObsControlAsync("Virtuelle Kamera starten", () => _obsClient.StartVirtualCamAsync());
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsStopVirtualCamButton.Click += async (_, _) => await ExecuteObsControlAsync("Virtuelle Kamera stoppen", () => _obsClient.StopVirtualCamAsync());
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSwitchSceneButton.Click += async (_, _) => await SwitchServicesObsSceneAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsScenesList.MouseDoubleClick += async (_, _) => await SwitchServicesObsSceneAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneSearchBox.TextChanged += (_, _) => ApplyServicesObsSceneFilter();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSourceSearchBox.TextChanged += (_, _) => ApplyServicesObsSourceFilter();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsInputSearchBox.TextChanged += (_, _) => ApplyServicesObsInputFilter();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsInputFilterBox.SelectionChanged += (_, _) => ApplyServicesObsInputFilter();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsScenesList.SelectionChanged += async (_, _) => await RefreshServicesObsSceneItemsAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemsList.SelectionChanged += async (_, _) => await RefreshSelectedObsSceneItemStateAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsShowSceneItemButton.Click += async (_, _) => await SetSelectedObsSceneItemVisibilityAsync(true);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsHideSceneItemButton.Click += async (_, _) => await SetSelectedObsSceneItemVisibilityAsync(false);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsLockSceneItemButton.Click += async (_, _) => await SetSelectedObsSceneItemLockAsync(true);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsUnlockSceneItemButton.Click += async (_, _) => await SetSelectedObsSceneItemLockAsync(false);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsMoveSceneItemUpButton.Click += async (_, _) => await MoveSelectedObsSceneItemAsync(1);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsMoveSceneItemDownButton.Click += async (_, _) => await MoveSelectedObsSceneItemAsync(-1);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsRefreshSceneItemsButton.Click += async (_, _) => await RefreshServicesObsSceneItemsAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsRestartMediaButton.Click += async (_, _) => await RestartSelectedObsMediaInputAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsStopMediaButton.Click += async (_, _) => await StopSelectedObsMediaInputAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsRefreshBrowserButton.Click += async (_, _) => await RefreshSelectedObsBrowserInputAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsApplySceneItemTransformButton.Click += async (_, _) => await ApplySelectedObsSceneItemTransformAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsReloadSceneItemTransformButton.Click += async (_, _) => await LoadSelectedObsSceneItemTransformAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsResetSceneItemTransformButton.Click += async (_, _) => await ResetSelectedObsSceneItemTransformAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemFullscreenButton.Click += async (_, _) => await ApplyObsSceneItemTransformPresetAsync(0, 0, 1920, 1080);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSceneItemCentered720Button.Click += async (_, _) => await ApplyObsSceneItemTransformPresetAsync(320, 180, 1280, 720);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSourceFiltersList.SelectionChanged += (_, _) => RefreshSelectedObsSourceFilterState();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsEnableSourceFilterButton.Click += async (_, _) => await SetSelectedObsSourceFilterEnabledAsync(true);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsDisableSourceFilterButton.Click += async (_, _) => await SetSelectedObsSourceFilterEnabledAsync(false);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsRefreshSourceFiltersButton.Click += async (_, _) => await RefreshSelectedObsSourceFiltersAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsMuteInputButton.Click += async (_, _) => await SetSelectedObsInputMuteAsync(true);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsUnmuteInputButton.Click += async (_, _) => await SetSelectedObsInputMuteAsync(false);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSetVolumeButton.Click += async (_, _) => await SetSelectedObsInputVolumeAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsInputsList.SelectionChanged += async (_, _) => await RefreshSelectedObsInputStateAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsRefreshInputStateButton.Click += async (_, _) => await RefreshSelectedObsInputStateAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsVolumeSlider.ValueChanged += (_, _) =>
        {
            if (!_updatingObsMixerVolumeUi)
            {
                ServicesPageViewHost.ObsServiceViewHost.ServicesObsVolumeDbBox.Text = ServicesPageViewHost.ObsServiceViewHost.ServicesObsVolumeSlider.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                ServicesPageViewHost.ObsServiceViewHost.ServicesObsVolumePercentText.Text = $"{DbToPercent(ServicesPageViewHost.ObsServiceViewHost.ServicesObsVolumeSlider.Value):0} % · -60 dB = sehr leise · 0 dB = Standard · +10 dB = Verstärkung";
            }
        };
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsVolumeMinus20Button.Click += async (_, _) => await ApplyObsMixerPresetAsync(-20);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsVolumeMinus10Button.Click += async (_, _) => await ApplyObsMixerPresetAsync(-10);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsVolumeZeroButton.Click += async (_, _) => await ApplyObsMixerPresetAsync(0);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsMuteAllButton.Click += async (_, _) => await SetObsInputsMuteAsync(_servicesObsInputs, true, "Alle Audioquellen");
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsUnmuteAllButton.Click += async (_, _) => await SetObsInputsMuteAsync(_servicesObsInputs, false, "Alle Audioquellen");
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsOnlyMicButton.Click += async (_, _) => await SoloObsAudioCategoryAsync("microphone");
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsOnlyGameButton.Click += async (_, _) => await SoloObsAudioCategoryAsync("game");
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsApplyGroupVolumeButton.Click += async (_, _) => await ApplyObsAudioGroupVolumeAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsMuteGroupButton.Click += async (_, _) => await SetSelectedObsAudioGroupMuteAsync(true);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsUnmuteGroupButton.Click += async (_, _) => await SetSelectedObsAudioGroupMuteAsync(false);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsApplyAdvancedAudioButton.Click += async (_, _) => await ApplySelectedObsAdvancedAudioAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsSaveAudioProfileButton.Click += async (_, _) => await SaveObsAudioProfileAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsApplyAudioProfileButton.Click += async (_, _) => await ApplySelectedObsAudioProfileAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsDeleteAudioProfileButton.Click += async (_, _) => await DeleteSelectedObsAudioProfileAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsAudioProfileBox.SelectionChanged += (_, _) =>
        {
            if (ServicesPageViewHost.ObsServiceViewHost.ServicesObsAudioProfileBox.SelectedItem is ObsAudioProfileSettings profile)
            {
                ServicesPageViewHost.ObsServiceViewHost.ServicesObsAudioProfileNameBox.Text = profile.Name;
                ServicesPageViewHost.ObsServiceViewHost.ServicesObsAudioProfileStateText.Text = $"{profile.Inputs.Count} Audioquellen gespeichert.";
            }
        };
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationSceneBox.SelectionChanged += async (_, _) => await RefreshSimpleObsAutomationSourcesAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationAddButton.Click += async (_, _) => await AddSimpleObsAutomationRuleAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationDeleteButton.Click += async (_, _) => await DeleteSimpleObsAutomationRuleAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationTestButton.Click += async (_, _) => await TestSimpleObsAutomationRuleAsync();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyBrowseDataJsonButton.Click += (_, _) => BrowseSpotifyDataJsonPath();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlaySceneBox.SelectionChanged += async (_, _) => await RefreshSpotifyOverlayBrowserSourcesAsync();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifySyncOverlayButton.Click += async (_, _) => await WriteSpotifyDataJsonNowAsync();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyReloadOverlayButton.Click += (_, _) => OpenSpotifyDataJsonFolder();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPreviewOverlayButton.Click += (_, _) => OpenSpotifyDataJsonFile();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyVolumeSlider.ValueChanged += async (_, _) =>
        {
            int volume = (int)Math.Round(ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyVolumeSlider.Value);
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyVolumeText.Text = $"Level {volume}";

            if (!_updatingSpotifyUi)
            {
                // ValueChanged fires continuously while the thumb is dragged.
                // A short debounce prevents API flooding while keeping the response live.
                await QueueSpotifyVolumeUpdateAsync(volume);
            }
        };
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchSaveEndSettingsButton.Click += async (_, _) => await SaveTwitchEndSettingsAsync();
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotLaunchButton.Click += (_, _) => LaunchConfiguredExecutable(_settings.StreamerBot.ExecutablePath, "Streamer.bot");
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotConnectButton.Click += async (_, _) => await ConnectStreamerBotAsync();
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotDisconnectButton.Click += async (_, _) => await DisconnectStreamerBotAsync();
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotDiagnoseButton.Click += async (_, _) => await DiagnoseStreamerBotAsync();
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotReconnectButton.Click += async (_, _) => await ReconnectStreamerBotAsync();
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotRefreshActionsButton.Click += async (_, _) => await RefreshStreamerBotActionsAsync(true);
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionSearchBox.TextChanged += (_, _) => ApplyStreamerBotActionFilter();
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionsList.SelectionChanged += (_, _) => UpdateSelectedStreamerBotAction();
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotFormatArgumentsButton.Click += (_, _) => FormatStreamerBotArgumentsJson();
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotHistoryList.ItemsSource = _streamerBotExecutionHistory;
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotLiveEventsList.ItemsSource = _streamerBotLiveEvents;
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotClearLiveEventsButton.Click += (_, _) =>
        {
            _streamerBotLiveEvents.Clear();
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotLiveEventStatusText.Text = "Live-Ereignisse wurden geleert.";
        };
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotRunActionButton.Click += async (_, _) => await RunSelectedStreamerBotActionAsync();
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotFavoriteActionButton.Click += (_, _) => ToggleSelectedStreamerBotFavorite();
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotTemplateBox.ItemsSource = _streamerBotActionTemplates;
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotSaveTemplateButton.Click += (_, _) => SaveSelectedStreamerBotTemplate();
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotLoadTemplateButton.Click += (_, _) => LoadSelectedStreamerBotTemplate();
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotDeleteTemplateButton.Click += (_, _) => DeleteSelectedStreamerBotTemplate();
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotScheduleActionButton.Click += async (_, _) => await ScheduleSelectedStreamerBotActionAsync();
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotCancelScheduleButton.Click += (_, _) => CancelScheduledStreamerBotAction();
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotExportHistoryButton.Click += (_, _) => ExportStreamerBotHistoryCsv();
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotClearHistoryButton.Click += (_, _) =>
        {
            _streamerBotExecutionHistory.Clear();
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionResultText.Text = "Ausführungshistorie wurde geleert.";
        };
        SettingsPageViewHost.BrowseObsExecutableButton.Click += (_, _) => BrowseExecutable(SettingsPageViewHost.ObsExecutablePathBox, "OBS|obs64.exe;obs32.exe|Programme|*.exe");
        SettingsPageViewHost.BrowseSpotifyExecutableButton.Click += (_, _) => BrowseExecutable(SettingsPageViewHost.SpotifyExecutablePathBox, "Spotify|Spotify.exe|Programme|*.exe");
        SettingsPageViewHost.OpenSpotifyFromSettingsButton.Click += (_, _) =>
            LaunchConfiguredExecutable(SettingsPageViewHost.SpotifyExecutablePathBox.Text.Trim(), "Spotify");
        SettingsPageViewHost.BrowseStreamerBotExecutableButton.Click += (_, _) => BrowseExecutable(SettingsPageViewHost.StreamerBotExecutablePathBox, "Streamer.bot|Streamer.bot.exe|Programme|*.exe");
        BrowseAlertMediaButton.Click += (_, _) => BrowseAlertFile(AlertMediaPathBox, "Videodateien|*.mp4;*.webm;*.mov;*.mkv|Alle Dateien|*.*");
        BrowseAlertSoundButton.Click += (_, _) =>
        {
            BrowseAlertFile(AlertSoundPathBox, "Audiodateien|*.mp3;*.wav;*.ogg;*.m4a;*.flac|Alle Dateien|*.*");
            LoadAlertAudioPreviewSource();
        };
        RefreshAlertAudioDevicesButton.Click += (_, _) => LoadAlertAudioOutputDevices();
        PlayAlertAudioSelectionButton.Click += (_, _) => PlaySelectedAlertAudioRange();
        PauseAlertAudioSelectionButton.Click += (_, _) => AlertAudioPreviewMedia.Pause();
        StopAlertAudioSelectionButton.Click += (_, _) => StopAlertAudioPreview();
        _alertAudioPreviewTimer.Tick += (_, _) =>
        {
            if (AlertAudioPreviewMedia.Position.TotalSeconds >= AlertAudioEndSlider.Value)
            {
                StopAlertAudioPreview();
            }
        };
        SettingsPageViewHost.SpotifyVolumeSlider.ValueChanged += async (_, _) =>
            await QueueSpotifyVolumeUpdateAsync();

        SettingsPageViewHost.TestSpotifyFadeButton.Click += async (_, _) =>
            await TestSpotifyFadeAsync();

        SettingsPageViewHost.SpotifyDeviceBox.SelectionChanged += (_, _) =>
        {
            if (_updatingSpotifyUi || SettingsPageViewHost.SpotifyDeviceBox.SelectedItem is not SpotifyDevice device)
            {
                return;
            }

            _settings.Spotify.PreferredDeviceId = device.Id;
        };

        SettingsPageViewHost.SpotifyPlaylistBox.SelectionChanged += (_, _) =>
        {
            if (SettingsPageViewHost.SpotifyPlaylistBox.SelectedItem is SpotifyPlaylist playlist)
            {
                _settings.Spotify.StartPlaylistUri = playlist.Uri;
            }
        };

        AlertTypeBox.SelectionChanged += async (_, _) =>
        {
            if (AlertTypeBox.SelectedItem is string type)
            {
                _alertLibraryPageViewModel.Select(type);
            }
        };

        SaveAlertDefinitionButton.Click += async (_, _) =>
            await SaveSelectedAlertDefinitionAsync();

        PreviewAlertButton.Click += async (_, _) =>
            await PreviewAlertAsync();

        TestAlertInObsButton.Click += async (_, _) =>
            await TestAlertInObsAsync();

        _alertsModule.StateChanged += async (_, state) =>
        {
            Dispatcher.Invoke(() =>
            {
                _alertRuntimePageViewModel.UpdateQueueState(state);

                AlertsDashboardStatus.Text = state.IsRunning
                    ? "AKTIV"
                    : "BEREIT";
            });

            bool alertJustStarted = state.IsRunning && !_suiteAlertRunning;
            _suiteAlertRunning = state.IsRunning;
            _suiteAlertQueueLength = state.QueueLength;
            await ApplyCombinedAlertDuckingAsync();

            await _overlayModule.Service.UpdateAsync(
                data =>
                {
                    data.Alerts.IsRunning = state.IsRunning;
                    data.Alerts.CurrentType = state.Current?.Type ?? "";
                    data.Alerts.QueueLength = state.QueueLength;
                });

            if (alertJustStarted)
            {
                await _workflowModule.Service.RegisterAlertPlayedAsync();
                string alertType = state.Current?.Type ?? "";
                string user = state.Current?.User ?? "";
                await PublishOverlayRealtimeEventAsync(OverlayEventBridge.AppAlert(alertType, user));
            }
        };

        DashboardPageViewHost.DashboardPrepareStreamButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardPageViewHost.DashboardPrepareStreamButton,
                "Stream vorbereiten",
                PrepareStreamWithConfiguredServicesAsync);
        DashboardCountdownStartButton.Click += async (_, _) =>
            await StartDashboardOverlayCountdownAsync();

        DashboardCountdownStopButton.Click += async (_, _) =>
            await ExecuteWorkflowAsync(() => _workflowModule.Service.StopCountdownAsync());

        DashboardCountdownResetButton.Click += async (_, _) =>
            await ResetDashboardOverlayCountdownAsync();

        DashboardCountdownSettingsButton.Click += (_, _) => OpenDashboardCountdownSettingsPopup();
        DashboardCountdownSettingsCancelButton.Click += (_, _) => DashboardCountdownSettingsPopup.IsOpen = false;
        DashboardCountdownSettingsSaveButton.Click += async (_, _) => await SaveDashboardCountdownSettingsFromPopupAsync();
        DashboardCountdownPreset5Button.Click += (_, _) => ApplyDashboardCountdownPreset(300);
        DashboardCountdownPreset10Button.Click += (_, _) => ApplyDashboardCountdownPreset(600);
        DashboardCountdownPreset30Button.Click += (_, _) => ApplyDashboardCountdownPreset(1800);
    }
}
