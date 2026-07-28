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
    private void InitializeCoreUiBindings()
    {
        SettingsPageViewHost.DashboardModuleOrderList.ItemsSource = _dashboardModuleOrderItems;
        SettingsPageViewHost.DashboardModuleOrderList.PreviewMouseLeftButtonDown += DashboardModuleOrderList_PreviewMouseLeftButtonDown;
        SettingsPageViewHost.DashboardModuleOrderList.PreviewMouseMove += DashboardModuleOrderList_PreviewMouseMove;
        SettingsPageViewHost.DashboardModuleOrderList.Drop += DashboardModuleOrderList_Drop;

        SettingsPageViewHost.DashboardPresetBox.SelectedIndex = 0;
        DashboardPageViewHost.DashboardQuickPresetBox.SelectedIndex = 0;
        SettingsPageViewHost.DashboardApplyPresetButton.Click += async (_, _) =>
        {
            ApplySelectedDashboardPreset(SettingsPageViewHost.DashboardPresetBox);
            await _settingsStore.SaveAsync(_settings);
        };
        DashboardPageViewHost.DashboardQuickApplyPresetButton.Click += async (_, _) =>
        {
            ApplySelectedDashboardPreset(DashboardPageViewHost.DashboardQuickPresetBox);
            await _settingsStore.SaveAsync(_settings);
        };
        DashboardPageViewHost.DashboardFocusModeButton.Click += (_, _) =>
        {
            if (_dashboardFocusModeActive)
            {
                ExitDashboardFocusMode();
            }
            else
            {
                EnterDashboardFocusMode();
            }
        };

        SettingsPageViewHost.DashboardModuleOrderList.SelectionChanged += (_, _) => RefreshDashboardModuleSizeEditor();
        SettingsPageViewHost.DashboardApplyModuleSizeButton.Click += async (_, _) =>
        {
            ApplySelectedDashboardModuleSizeFromSettingsEditor();
            await _settingsStore.SaveAsync(_settings);
        };
        DashboardPageViewHost.DashboardDirectApplySizeButton.Click += async (_, _) =>
        {
            ApplySelectedDashboardModuleSizeFromDirectEditor();
            await _settingsStore.SaveAsync(_settings);
        };

        DashboardPageViewHost.DashboardEditLayoutButton.Click += (_, _) => ToggleDashboardLayoutEditMode();
        DashboardPageViewHost.DashboardRestoreHiddenModulesButton.Click += (_, _) => RestoreAllDashboardModules();
        DashboardPageViewHost.DashboardContentStack.Drop += DashboardContentStack_Drop;
        DashboardPageViewHost.DashboardContentStack.DragOver += DashboardContentStack_DragOver;
        RegisterDashboardDirectDragHandlers();

        SettingsPageViewHost.IpcStatusText.Text = _ipcServer.IsRunning
            ? "IPC aktiv: " + NamedPipeIpcServer.PipeName
            : "IPC nicht aktiv.";

        _ipcServer.StateChanged += (_, running) =>
        {
            Dispatcher.Invoke(() =>
            {
                SettingsPageViewHost.IpcStatusText.Text = running
                    ? "IPC aktiv: " + NamedPipeIpcServer.PipeName
                    : "IPC nicht aktiv.";
            });
        };

        LogsGrid.ItemsSource = _visibleLogs;
        SpotifyInspectorGrid.ItemsSource = _spotifyInspectorRows;
        LogLevelFilterBox.SelectedIndex = 0;

        SettingsPageViewHost.TwitchChatList.ItemsSource = _twitchChatItems;
        SettingsPageViewHost.TwitchEventsList.ItemsSource = _twitchEventItems;
        DashboardPageViewHost.DashboardTwitchChatList.ItemsSource = _twitchChatItems;
        DashboardPageViewHost.DashboardTwitchEventsList.ItemsSource = _twitchEventItems;
        DashboardPageViewHost.DashboardTwitchUsersList.ItemsSource = _twitchUserItems;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchChatList.ItemsSource = _twitchChatItems;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchEventsList.ItemsSource = _twitchEventItems;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesRedemptionsList.ItemsSource = _twitchRedemptionItems;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchUsersList.ItemsSource = _twitchUserItems;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalHistoryList.ItemsSource = _twitchProfessionalHistoryItems;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchModerationLogList.ItemsSource = _twitchModerationLogItems;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceRecommendationsList.ItemsSource = _creatorIntelligenceRecommendations;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesRefreshTwitchProfessionalButton.Click += async (_, _) =>
        {
            await RefreshLiveViewerSampleAsync();
            await RefreshTwitchGoalsAsync();
            await LoadTwitchProfessionalHistoryAsync();
            RefreshTwitchProfessionalUi();
        };
        ServicesPageViewHost.TwitchServiceViewHost.ServicesOpenTwitchProfessionalHistoryButton.Click += (_, _) => OpenStreamHistoryFolder();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesExportTwitchProfessionalHistoryButton.Click += async (_, _) => await ExportTwitchProfessionalHistoryCsvAsync();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreateTwitchProfessionalReportButton.Click += async (_, _) => await CreateTwitchProfessionalReportAsync();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCopyTwitchProfessionalSummaryButton.Click += async (_, _) => await CopyLatestTwitchProfessionalSummaryAsync();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesModerationPreset1Button.Click += (_, _) => ServicesPageViewHost.TwitchServiceViewHost.ServicesModerationDurationBox.Text = "1";
        ServicesPageViewHost.TwitchServiceViewHost.ServicesModerationPreset10Button.Click += (_, _) => ServicesPageViewHost.TwitchServiceViewHost.ServicesModerationDurationBox.Text = "10";
        ServicesPageViewHost.TwitchServiceViewHost.ServicesModerationPreset60Button.Click += (_, _) => ServicesPageViewHost.TwitchServiceViewHost.ServicesModerationDurationBox.Text = "60";
        ServicesPageViewHost.TwitchServiceViewHost.ServicesModerationPreset1440Button.Click += (_, _) => ServicesPageViewHost.TwitchServiceViewHost.ServicesModerationDurationBox.Text = "1440";
        ServicesPageViewHost.TwitchServiceViewHost.ServicesClearModerationLogButton.Click += (_, _) => _twitchModerationLogItems.Clear();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesExportModerationLogButton.Click += async (_, _) => await ExportTwitchModerationLogAsync();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceRefreshButton.Click += async (_, _) => await RefreshCreatorIntelligenceAsync();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceOpenFolderButton.Click += (_, _) => OpenCreatorIntelligenceFolder();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceAddNoteButton.Click += async (_, _) => await AddCreatorIntelligenceNoteAsync();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceWeeklyReportButton.Click += async (_, _) => await CreateCreatorIntelligenceWeeklyReportAsync();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceCompleteActionButton.Click += async (_, _) => await CompleteSelectedCreatorActionAsync();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesCreatorIntelligenceStartExperimentButton.Click += async (_, _) => await StartSelectedCreatorExperimentAsync();
        SelectDashboardStatisticInUi();
        DashboardPageViewHost.DashboardTwitchUsersList.SelectionChanged += (_, _) => CopySelectedModerationUser(DashboardPageViewHost.DashboardTwitchUsersList, DashboardPageViewHost.DashboardModerationUserBox);
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchUsersList.SelectionChanged += (_, _) => CopySelectedModerationUser(ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchUsersList, ServicesPageViewHost.TwitchServiceViewHost.ServicesModerationUserBox);
        DashboardPageViewHost.DashboardPreflightList.ItemsSource = _dashboardPreflightItems;
        DashboardPageViewHost.DashboardNotificationList.ItemsSource = _dashboardNotificationItems;
        DashboardPageViewHost.DashboardStreamHistoryList.ItemsSource = _streamHistoryItems;
        _twitchUsersRefreshTimer.Tick += async (_, _) => await RefreshTwitchUsersAsync();
        ApplyTwitchUsersRefreshInterval();
        _twitchUsersRefreshTimer.Start();
        _liveViewerSampleTimer.Tick += async (_, _) => await RefreshLiveViewerSampleAsync();
        _liveViewerSampleTimer.Start();

        Loaded += async (_, _) =>
        {
            try
            {
                await RunStartupStepSafelyAsync("Einstellungen laden", LoadSettingsAsync);
                await RunStartupStepSafelyAsync("Dashboard initialisieren", () =>
                {
                    RefreshDashboardAutomationSummary();
                    RefreshDashboardResourceUsage();
                    return Task.CompletedTask;
                });
            }
            finally
            {
                // UI events (Checked/Unchecked/SelectionChanged) fire while the
                // saved settings are copied into the controls. They must not
                // write the settings file during startup.
                _settingsUiLoaded = true;
            }
        };

        Closing += OnMainWindowClosing;

        ObsDashboardStatus.MouseLeftButtonUp += (_, _) =>
            NavigateToServicesTab(2, ServicesObsButton);
        TwitchDashboardStatus.MouseLeftButtonUp += (_, _) =>
            NavigateToServicesTab(1, ServicesTwitchButton);
        SpotifyDashboardStatus.MouseLeftButtonUp += (_, _) =>
            NavigateToServicesTab(0, ServicesSpotifyButton);
        StreamerBotDashboardStatus.MouseLeftButtonUp += (_, _) =>
            NavigateToServicesTab(3, ServicesStreamerBotButton);
        AlertsDashboardStatus.MouseLeftButtonUp += (_, _) =>
        {
            ServicesNavigationPanel.Visibility = Visibility.Collapsed;
            ShowPage(AlertsPage);
        };

        DashboardConnectionSummaryChip.MouseLeftButtonUp += (_, _) =>
            ShowServicesOverview();

        DashboardOpenObsServiceButton.Click += (_, _) =>
            NavigateToServicesTab(2, ServicesObsButton);
        DashboardOpenTwitchServiceButton.Click += (_, _) =>
            NavigateToServicesTab(1, ServicesTwitchButton);
        DashboardOpenSpotifyServiceButton.Click += (_, _) =>
        {
            if (IsSpotifyMusicProvider())
            {
                NavigateToServicesTab(0, ServicesSpotifyButton);
            }
            else
            {
                ServicesNavigationPanel.Visibility = Visibility.Collapsed;
                ShowPage(MusicPlayerPage);
                _ = RefreshMusicPlayerUiAsync();
            }
        };
        DashboardOpenStreamerBotServiceButton.Click += (_, _) =>
            NavigateToServicesTab(3, ServicesStreamerBotButton);
        DashboardOpenAlertsServiceButton.Click += (_, _) =>
        {
            ServicesNavigationPanel.Visibility = Visibility.Collapsed;
            ShowPage(AlertsPage);
        };

        DashboardButton.Click += (_, _) =>
        {
            ServicesNavigationPanel.Visibility = Visibility.Collapsed;
            ShowPage(DashboardPage);
        };
        PlayerButton.Click += (_, _) =>
        {
            ServicesNavigationPanel.Visibility = Visibility.Collapsed;
            ShowPage(MusicPlayerPage);
            _ = RefreshMusicPlayerUiAsync();
        };
        DashboardPageViewHost.DashboardOpenMusicPlayerButton.Click += (_, _) =>
        {
            ServicesNavigationPanel.Visibility = Visibility.Collapsed;
            ShowPage(MusicPlayerPage);
            _ = RefreshMusicPlayerUiAsync();
        };
        DashboardPageViewHost.DashboardQuickOpenMusicPlayerButton.Click += (_, _) =>
        {
            ServicesNavigationPanel.Visibility = Visibility.Collapsed;
            ShowPage(MusicPlayerPage);
            _ = RefreshMusicPlayerUiAsync();
        };
        DashboardTopMusicPreviousButton.Click += async (_, _) =>
            await ExecuteMusicCommandAsync(() => _musicPlayerRouter.PreviousAsync());
        DashboardTopMusicPlayPauseButton.Click += async (_, _) =>
            await ExecuteMusicCommandAsync(() => _musicPlayerRouter.PlayPauseAsync());
        DashboardTopMusicNextButton.Click += async (_, _) =>
            await ExecuteMusicCommandAsync(() => _musicPlayerRouter.NextAsync());
        DashboardTopMusicVolumeSlider.ValueChanged += async (_, _) =>
        {
            int volume = (int)Math.Round(DashboardTopMusicVolumeSlider.Value);
            DashboardTopMusicVolumeText.Text = $"{volume} %";
            if (!_updatingMusicPlayerUi)
            {
                if (IsSpotifyMusicProvider())
                {
                    await QueueSpotifyVolumeUpdateAsync(volume);
                }
                else
                {
                    await ExecuteMusicCommandAsync(() => _musicPlayerRouter.SetVolumeAsync(volume));
                }
            }
        };
        DashboardTopMusicWidget.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is System.Windows.Controls.Primitives.ButtonBase ||
                FindVisualParent<System.Windows.Controls.Slider>(e.OriginalSource as DependencyObject) is not null)
            {
                return;
            }

            ServicesNavigationPanel.Visibility = Visibility.Collapsed;
            ShowPage(MusicPlayerPage);
            _ = RefreshMusicPlayerUiAsync();
        };
        DashboardTopMusicTitleViewport.SizeChanged += (_, _) => UpdateMusicTitleMarquees();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOpenMusicSettingsButton.Click += (_, _) =>
            NavigateToSettingsTab(4, SettingsButton);
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOpenPlayerButton.Click += (_, _) =>
        {
            ServicesNavigationPanel.Visibility = Visibility.Collapsed;
            ShowPage(MusicPlayerPage);
            _ = RefreshMusicPlayerUiAsync();
        };
        SettingsPageViewHost.MusicProviderSpotifyRadio.Checked += (_, _) => UpdateMusicPlayerSettingsVisibility();
        SettingsPageViewHost.MusicProviderYouTubeMusicRadio.Checked += (_, _) => UpdateMusicPlayerSettingsVisibility();
        SettingsPageViewHost.OpenMusicPlayerFromSettingsButton.Click += (_, _) =>
        {
            ServicesNavigationPanel.Visibility = Visibility.Collapsed;
            ShowPage(MusicPlayerPage);
            _ = RefreshMusicPlayerUiAsync();
        };
        _musicPlayerRouter.SnapshotChanged += (_, _) =>
            Dispatcher.InvokeAsync(async () => await RefreshMusicPlayerUiAsync());
        _musicPlayerRouter.ActiveProviderChanged += (_, _) =>
            Dispatcher.InvokeAsync(async () =>
            {
                ApplyMusicProviderUiState();
                await RefreshMusicPlayerUiAsync();
            });
        ServicesButton.Click += (_, _) =>
        {
            ShowServicesOverview();
        };
        ServicesSpotifyButton.Click += (_, _) =>
            NavigateToServicesTab(0, ServicesSpotifyButton);
        ServicesTwitchButton.Click += (_, _) =>
            NavigateToServicesTab(1, ServicesTwitchButton);
        ServicesObsButton.Click += (_, _) =>
            NavigateToServicesTab(2, ServicesObsButton);
        ServicesStreamerBotButton.Click += (_, _) =>
            NavigateToServicesTab(3, ServicesStreamerBotButton);
        ServicesStreamDeckButton.Click += (_, _) =>
            NavigateToServicesTab(4, ServicesStreamDeckButton);
        WorkflowButton.Click += (_, _) =>
        {
            ServicesNavigationPanel.Visibility = Visibility.Collapsed;
            ShowPage(WorkflowPage);
        };
    }
}
