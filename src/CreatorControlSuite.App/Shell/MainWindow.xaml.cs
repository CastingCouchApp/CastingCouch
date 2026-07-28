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
    private readonly ObservableCollection<string> _multiPcDeviceItems = [];
    private readonly ObservableCollection<string> _multiPcHistoryItems = [];
    private readonly ObservableCollection<string> _multiPcRolloutItems = [];
    private readonly ObservableCollection<string> _runOfShowPlanNames = [];
    private CancellationTokenSource? _multiPcRolloutCts;
    private CancellationTokenSource? _scheduledMultiPcRolloutCts;
    private readonly Dictionary<string, string> _multiPcRolloutGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Threading.DispatcherTimer _multiPcRefreshTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly List<MultiPcDeviceRecord> _multiPcDevices = [];
    private readonly PairedAgentRegistry _multiPcRegistry;
    private string _multiPcPairingCode = "------";
    private readonly System.Net.Http.HttpClient _multiPcHttpClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private string MultiPcRegistryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CreatorControlSuite", "multi-pc-devices.json");
    private string MultiPcRolloutGroupsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CreatorControlSuite", "multi-pc-rollout-groups.json");
    private string MultiPcScheduledRolloutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CreatorControlSuite", "multi-pc-scheduled-rollout.json");
    private string MultiPcRolloutAuditPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CreatorControlSuite", "multi-pc-rollout-audit.jsonl");
    private string MultiPcScheduledPackagesDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CreatorControlSuite", "ScheduledRollouts");

    public MainWindow(
        ISettingsStore settingsStore,
        ISecretStore secretStore,
        IObsWebSocketClient obsClient,
        TwitchModule twitchModule,
        SpotifyModule spotifyModule,
        YouTubeMusicModule youTubeMusicModule,
        IMusicPlayerRouter musicPlayerRouter,
        AlertsModule alertsModule,
        OverlayModule overlayModule,
        IOverlayRealtimeHub overlayRealtimeHub,
        IChatEmoteCatalog chatEmoteCatalog,
        IChatBadgeCatalog chatBadgeCatalog,
        ITwitchApiClient twitchApiClient,
        WorkflowModule workflowModule,
        IProfileService profileService,
        StreamDeckModule streamDeckModule,
        IAppLogger appLogger,
        SettingsApplicationService settingsApplicationService,
        RuntimeHealthService runtimeHealthService,
        ICrashReporter crashReporter,
        ILocalIpcServer ipcServer,
        ISupportPackageService supportPackageService,
        IReleaseReadinessService releaseReadinessService,
        IWorkflowE2eService workflowE2eService,
        IInstallerSelfTestService installerSelfTestService,
        IBetaReadinessService betaReadinessService,
        ExternalAlertActivityService externalAlertActivity,
        IThemeService themeService,
        IMusicPlayerUiPresenter musicPlayerUiPresenter,
        IMultiPcAgentClient multiPcAgentClient,
        IMultiPcPairingClient multiPcPairingClient,
        RemoteUpdateRolloutService remoteUpdateRolloutService,
        IStreamerBotClient streamerBotClient,
        INavigationService navigationService,
        DiagnosticsPageViewModel diagnosticsPageViewModel,
        ProfilesPageViewModel profilesPageViewModel,
        AboutPageViewModel aboutPageViewModel,
        MusicPlayerPageViewModel musicPlayerPageViewModel,
        UpdatePageViewModel updatePageViewModel,
        MigrationPageViewModel migrationPageViewModel,
        LegalPageViewModel legalPageViewModel,
        GeneralSettingsPageViewModel generalSettingsPageViewModel,
        TwitchGoalsPageViewModel twitchGoalsPageViewModel,
        SpotifyAutomationPageViewModel spotifyAutomationPageViewModel,
        WorkflowSessionPageViewModel workflowSessionPageViewModel,
        OverlayConnectionSettingsPageViewModel overlayConnectionSettingsPageViewModel,
        OverlayCanvasPageViewModel overlayCanvasPageViewModel,
        OverlayExtensionPacksPageViewModel overlayExtensionPacksPageViewModel,
        AlertLibraryPageViewModel alertLibraryPageViewModel,
        AlertDefinitionEditorViewModel alertDefinitionEditorViewModel,
        AlertRuntimePageViewModel alertRuntimePageViewModel,
        StatisticsPageViewModel statisticsPageViewModel,
        CreatorIntelligenceService creatorIntelligence,
        IEventBus eventBus)
    {
        InitializeComponent();
        NativeWindowHelper.RestrictMaximizeToWorkArea(this);
        WindowState = WindowState.Maximized;
        StateChanged += (_, _) => UpdateTitleBarMaximizeButton();
        UpdateTitleBarMaximizeButton();

        _settingsStore = settingsStore;
        _externalAlertActivity = externalAlertActivity;
        _themeService = themeService;
        _musicPlayerUiPresenter = musicPlayerUiPresenter;
        _multiPcAgentClient = multiPcAgentClient;
        _multiPcPairingClient = multiPcPairingClient;
        _remoteUpdateRolloutService = remoteUpdateRolloutService;
        _streamerBotClient = streamerBotClient;
        _navigationService = navigationService;
        _diagnosticsPageViewModel = diagnosticsPageViewModel;
        _profilesPageViewModel = profilesPageViewModel;
        _aboutPageViewModel = aboutPageViewModel;
        _musicPlayerPageViewModel = musicPlayerPageViewModel;
        _updatePageViewModel = updatePageViewModel;
        _migrationPageViewModel = migrationPageViewModel;
        _generalSettingsPageViewModel = generalSettingsPageViewModel;
        _twitchGoalsPageViewModel = twitchGoalsPageViewModel;
        _spotifyAutomationPageViewModel = spotifyAutomationPageViewModel;
        _workflowSessionPageViewModel = workflowSessionPageViewModel;
        _overlayConnectionSettingsPageViewModel =
            overlayConnectionSettingsPageViewModel;
        _overlayCanvasPageViewModel = overlayCanvasPageViewModel;
        _overlayExtensionPacksPageViewModel =
            overlayExtensionPacksPageViewModel;
        _alertLibraryPageViewModel = alertLibraryPageViewModel;
        _alertDefinitionEditorViewModel = alertDefinitionEditorViewModel;
        _alertRuntimePageViewModel = alertRuntimePageViewModel;
        _statisticsPageViewModel = statisticsPageViewModel;
        _creatorIntelligence = creatorIntelligence;
        _eventBus = eventBus;
        DiagnosticsModuleView.DataContext = _diagnosticsPageViewModel;
        ProfilesPageViewHost.DataContext = _profilesPageViewModel;
        AboutPageViewHost.DataContext = _aboutPageViewModel;
        MusicPlayerPageViewHost.DataContext = _musicPlayerPageViewModel;
        MusicPlayerPageViewHost.Actions = CreateMusicPlayerPageActions();
        WorkflowPageViewHost.Actions = CreateWorkflowPageActions();
        SettingsPageViewHost.DataContext = _alertRuntimePageViewModel;
        SettingsPageViewHost.SaveRequestedAsync = SaveSettingsAsync;
        ServicesPageViewHost.ServiceRequested = tabIndex =>
            NavigateToServicesTab(
                tabIndex,
                tabIndex switch
                {
                    0 => ServicesSpotifyButton,
                    1 => ServicesTwitchButton,
                    2 => ServicesObsButton,
                    3 => ServicesStreamerBotButton,
                    4 => ServicesStreamDeckButton,
                    _ => ServicesButton
                });
        SettingsPageViewHost.UpdateSettingsViewHost.DataContext = _updatePageViewModel;
        SettingsPageViewHost.MigrationSettingsViewHost.DataContext = _migrationPageViewModel;
        SettingsPageViewHost.LegalSettingsViewHost.DataContext = legalPageViewModel;
        SettingsPageViewHost.GeneralSettingsViewHost.DataContext = _generalSettingsPageViewModel;
        _generalSettingsPageViewModel.PropertyChanged += GeneralSettingsPageViewModelOnPropertyChanged;
        ServicesPageViewHost.TwitchServiceViewHost.TwitchGoalsViewHost.DataContext = _twitchGoalsPageViewModel;
        _twitchGoalsPageViewModel.SaveRequestedAsync = SaveTwitchGoalsAsync;
        ServicesPageViewHost.SpotifyServiceViewHost.SpotifyAutomationViewHost.DataContext = _spotifyAutomationPageViewModel;
        _spotifyAutomationPageViewModel.SaveRequestedAsync =
            SaveSpotifyAutomationSettingsAsync;
        WorkflowPageViewHost.WorkflowSessionViewHost.DataContext = _workflowSessionPageViewModel;
        OverlayConnectionSettingsViewHost.DataContext =
            _overlayConnectionSettingsPageViewModel;
        OverlayCanvasViewHost.DataContext = _overlayCanvasPageViewModel;
        OverlayExtensionPacksViewHost.DataContext =
            _overlayExtensionPacksPageViewModel;
        AlertLibraryViewHost.DataContext = _alertLibraryPageViewModel;
        AlertDesignerPanel.DataContext = _alertDefinitionEditorViewModel;
        AlertsContentPanel.DataContext = _alertRuntimePageViewModel;
        StatisticsPageViewHost.DataContext = _statisticsPageViewModel;
        AlertTypeBox.ItemsSource = _alertLibraryPageViewModel.Types;
        _overlayConnectionSettingsPageViewModel.CopyTextRequested =
            text => Clipboard.SetText(text);
        _overlayConnectionSettingsPageViewModel.BrowseBackgroundRequestedAsync =
            BrowseOverlayChatBackgroundImageAsync;
        _overlayConnectionSettingsPageViewModel.BrowseAssetLibraryRequestedAsync =
            BrowseOverlayAssetLibraryImageAsync;
        _overlayCanvasPageViewModel.CopyTextRequested =
            text => Clipboard.SetText(text);
        _overlayCanvasPageViewModel.PromptNameRequestedAsync =
            PromptOverlayCanvasNameAsync;
        _overlayCanvasPageViewModel.ConfirmDeleteRequestedAsync =
            ConfirmOverlayCanvasDeleteAsync;
        _overlayCanvasPageViewModel.OpenEditorRequestedAsync =
            OpenOverlayEditorAsync;
        _overlayCanvasPageViewModel.ErrorRequested =
            message => MessageBox.Show(
                this,
                message,
                "Overlay Canvas",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        _overlayExtensionPacksPageViewModel.OpenPackRequestedAsync =
            OpenOverlayExtensionPackAsync;
        _overlayExtensionPacksPageViewModel.ConfirmUninstallRequestedAsync =
            ConfirmOverlayExtensionPackUninstallAsync;
        _overlayExtensionPacksPageViewModel.ErrorRequested =
            (message, validationError) => MessageBox.Show(
                this,
                message,
                "Extension Pack importieren",
                MessageBoxButton.OK,
                    validationError
                        ? MessageBoxImage.Warning
                        : MessageBoxImage.Error);
        _alertLibraryPageViewModel.BeforeDuplicateRequestedAsync =
            () =>
            {
                SaveAlertDefinitionToSettings();
                return Task.CompletedTask;
            };
        _alertLibraryPageViewModel.SelectionChangedAsync =
            async type =>
            {
                if (!Equals(AlertTypeBox.SelectedItem, type))
                {
                    AlertTypeBox.SelectedItem = type;
                }

                await LoadSelectedAlertDefinitionAsync();
            };
        _alertLibraryPageViewModel.ConfirmDeleteRequestedAsync =
            type => Task.FromResult(
                MessageBox.Show(
                    this,
                    $"Alert '{type}' wirklich löschen?",
                    "Alert löschen",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) == MessageBoxResult.Yes);
        _alertLibraryPageViewModel.ErrorRequested =
            message =>
            {
                AlertPreviewStatusText.Text = message;
                AlertPreviewStatusText.Foreground = Brushes.IndianRed;
            };
        _alertRuntimePageViewModel.SetActions(_streamerBotActions);
        _alertRuntimePageViewModel.RefreshActionsRequestedAsync =
            () => RefreshStreamerBotActionsAsync(true);
        _alertRuntimePageViewModel.ApplySuppressionRequestedAsync =
            ApplyStreamerBotAlertSuppressionAsync;
        _alertRuntimePageViewModel.SetStreamerBotAlertsRequestedAsync =
            enabled => SetStreamerBotAlertsEnabledAsync(enabled);
        _alertRuntimePageViewModel.StopCurrentAlertRequestedAsync =
            () => alertsModule.StopCurrentAsync();
        _alertRuntimePageViewModel.ClearAlertQueueRequestedAsync =
            () => alertsModule.ClearQueueAsync();
        _alertRuntimePageViewModel.InstallObsSourcesRequestedAsync =
            InstallObsAlertSceneAsync;
        _statisticsPageViewModel.RefreshRequestedAsync =
            () => _statisticsPageViewModel.LoadAsync(
                GetStreamHistoryFilePath());
        _statisticsPageViewModel.OpenFolderRequested =
            OpenStreamHistoryFolder;
        _statisticsPageViewModel.MetricChangedAsync =
            async metric =>
            {
                _settings.Dashboard.DashboardStatistic = metric;
                UpdateDashboardSelectedStatistic();
                await _settingsStore.SaveAsync(_settings);
            };
        _updatePageViewModel.ConfirmRestoreAsync = ConfirmUpdateRestoreAsync;
        _updatePageViewModel.AfterRestoreAsync = LoadSettingsAsync;
        _migrationPageViewModel.AfterImportAsync = LoadSettingsAsync;
        _updatePageViewModel.ShutdownApplication =
            () => Application.Current.Shutdown(0);
        _profilesPageViewModel.AfterProfileAppliedAsync = async () => await LoadSettingsAsync();
        _profilesPageViewModel.ProfilesChanged += (_, _) =>
        {
            DashboardPageViewHost.DashboardProfileBox.ItemsSource = _profilesPageViewModel.Profiles;
            if (DashboardPageViewHost.DashboardProfileBox.SelectedItem is null && _profilesPageViewModel.Profiles.Count > 0)
            {
                DashboardPageViewHost.DashboardProfileBox.SelectedIndex = 0;
            }
        };
        _externalAlertActivity.ActiveCountChanged += async (_, _) => await ApplyCombinedAlertDuckingAsync();
        _secretStore = secretStore;
        _multiPcRegistry = new PairedAgentRegistry(MultiPcRegistryPath, secretStore);
        _obsClient = obsClient;
        _twitchModule = twitchModule;
        _spotifyModule = spotifyModule;
        _youTubeMusicModule = youTubeMusicModule;
        _musicPlayerRouter = musicPlayerRouter;
        _alertsModule = alertsModule;
        _overlayModule = overlayModule;
        _overlayRealtimeHub = overlayRealtimeHub;
        _chatEmoteCatalog = chatEmoteCatalog;
        _chatBadgeCatalog = chatBadgeCatalog;
        _twitchApiClient = twitchApiClient;
        _workflowModule = workflowModule;
        _workflowSessionPageViewModel.ResetRequestedAsync =
            () => ExecuteWorkflowAsync(
                () => _workflowModule.Service.ResetAsync());
        _workflowSessionPageViewModel.AddViewerSampleRequestedAsync =
            async viewers =>
            {
                await _workflowModule.Service.AddViewerSampleAsync(viewers);
                RefreshWorkflowUi(_workflowModule.Service.State);
            };
        _profileService = profileService;
        _streamDeckModule = streamDeckModule;
        _appLogger = appLogger;
        _settingsApplicationService = settingsApplicationService;
        _runtimeHealthService = runtimeHealthService;
        _crashReporter = crashReporter;
        _ipcServer = ipcServer;
        _supportPackageService = supportPackageService;
        _releaseReadinessService = releaseReadinessService;
        _workflowE2eService = workflowE2eService;
        _installerSelfTestService = installerSelfTestService;
        _betaReadinessService = betaReadinessService;

        _themeService.ThemeChanged += (_, _) => Dispatcher.Invoke(OnThemeChanged);

        InitializeCoreUiBindings();
        MultiPcButton.Click += async (_, _) =>
        {
            ServicesNavigationPanel.Visibility = Visibility.Collapsed;
            ShowPage(MultiPcPage);
            await RefreshMultiPcPageAsync();
        };
        OverlaysButton.Click += (_, _) =>
        {
            ServicesNavigationPanel.Visibility = Visibility.Collapsed;
            ShowPage(OverlayPage);
        };
        AlertsButton.Click += (_, _) =>
        {
            ServicesNavigationPanel.Visibility = Visibility.Collapsed;
            ShowPage(AlertsPage);
        };
        SettingsButton.Click += (_, _) =>
        {
            ServicesNavigationPanel.Visibility = Visibility.Collapsed;
            NavigateToSettingsTab(0, SettingsButton);
        };
        DiagnosticsButton.Click += async (_, _) =>
        {
            ShowPage(DiagnosticsPage);
            await RefreshDiagnosticsPageSafelyAsync();
        };
        StatisticsButton.Click += async (_, _) =>
        {
            ShowPage(StatisticsPage);
            await _statisticsPageViewModel.LoadAsync(
                GetStreamHistoryFilePath());
        };
        ProfilesButton.Click += async (_, _) =>
        {
            ShowPage(ProfilesPage);
            await RefreshProfilesAsync();
        };
        AboutButton.Click += (_, _) => ShowPage(AboutPage);
        MultiPcDevicesList.ItemsSource = _multiPcDeviceItems;
        MultiPcHistoryList.ItemsSource = _multiPcHistoryItems;
        MultiPcRolloutStatusList.ItemsSource = _multiPcRolloutItems;
        LoadMultiPcRolloutGroups();
        RefreshMultiPcRolloutGroupChoices();
        MultiPcRefreshButton.Click += async (_, _) => await RefreshMultiPcPageAsync();
        MultiPcDiscoverButton.Click += async (_, _) => await DiscoverMultiPcAgentsAsync();
        MultiPcGeneratePairingCodeButton.Click += (_, _) => GenerateMultiPcPairingCode();
        MultiPcAddDeviceButton.Click += async (_, _) => await AddMultiPcDeviceAsync();
        MultiPcRemoveDeviceButton.Click += async (_, _) => await RemoveSelectedMultiPcDeviceAsync();
        MultiPcDevicesList.SelectionChanged += (_, _) => UpdateSelectedMultiPcDeviceText();
        MultiPcObsStartButton.Click += async (_, _) => await SendMultiPcCommandAsync("obs.start");
        MultiPcObsStopButton.Click += async (_, _) => await SendMultiPcCommandAsync("obs.stop");
        MultiPcSpotifyPlayPauseButton.Click += async (_, _) => await SendMultiPcCommandAsync("spotify.playpause");
        MultiPcStreamerBotStartButton.Click += async (_, _) => await SendMultiPcCommandAsync("streamerbot.start");
        MultiPcSystemRestartButton.Click += async (_, _) => await SendMultiPcCommandAsync("system.restart");
        MultiPcSystemShutdownButton.Click += async (_, _) => await SendMultiPcCommandAsync("system.shutdown");
        MultiPcWakeButton.Click += async (_, _) => await WakeSelectedMultiPcDeviceAsync();
        MultiPcAgentDiagnosticsButton.Click += async (_, _) => await FetchMultiPcDiagnosticsAsync();
        MultiPcObsRefreshStateButton.Click += async (_, _) => await RefreshRemoteObsStateAsync();
        MultiPcObsSwitchSceneButton.Click += async (_, _) => await SwitchRemoteObsSceneAsync();
        MultiPcObsMuteButton.Click += async (_, _) => await SetRemoteObsMuteAsync(true);
        MultiPcObsUnmuteButton.Click += async (_, _) => await SetRemoteObsMuteAsync(false);
        MultiPcObsSetVolumeButton.Click += async (_, _) => await SetRemoteObsVolumeAsync();
        MultiPcObsFadeVolumeButton.Click += async (_, _) => await FadeRemoteObsVolumeAsync();
        MultiPcObsSourceShowButton.Click += async (_, _) => await SetRemoteObsSceneItemVisibilityAsync(true);
        MultiPcObsSourceHideButton.Click += async (_, _) => await SetRemoteObsSceneItemVisibilityAsync(false);
        MultiPcObsApplyTransformButton.Click += async (_, _) => await ApplyRemoteObsTransformAsync(false);
        MultiPcObsResetTransformButton.Click += async (_, _) => await ApplyRemoteObsTransformAsync(true);
        MultiPcObsFilterEnableButton.Click += async (_, _) => await SetRemoteObsFilterAsync(true);
        MultiPcObsFilterDisableButton.Click += async (_, _) => await SetRemoteObsFilterAsync(false);
        MultiPcObsAudioInputsBox.SelectionChanged += async (_, _) => await RefreshRemoteObsFiltersAsync();
        MultiPcObsStreamStartButton.Click += async (_, _) => await SendRemoteObsOutputActionAsync("stream.start");
        MultiPcObsStreamStopButton.Click += async (_, _) => await SendRemoteObsOutputActionAsync("stream.stop");
        MultiPcObsRecordStartButton.Click += async (_, _) => await SendRemoteObsOutputActionAsync("record.start");
        MultiPcObsRecordStopButton.Click += async (_, _) => await SendRemoteObsOutputActionAsync("record.stop");
        MultiPcObsRecordPauseButton.Click += async (_, _) => await ToggleRemoteObsRecordPauseAsync();
        MultiPcObsApplyTransitionButton.Click += async (_, _) => await ApplyRemoteObsTransitionAsync();
        MultiPcObsPreviewRefreshButton.Click += async (_, _) => await RefreshRemoteObsPreviewAsync();
        MultiPcObsLoadConfigurationButton.Click += async (_, _) => await LoadRemoteObsConfigurationAsync();
        MultiPcObsApplyProfileButton.Click += async (_, _) => await ApplyRemoteObsConfigurationAsync(true);
        MultiPcObsApplySceneCollectionButton.Click += async (_, _) => await ApplyRemoteObsConfigurationAsync(false);
        MultiPcObsSavePresetButton.Click += async (_, _) => await SaveRemoteObsPresetAsync();
        MultiPcObsLoadPresetsButton.Click += async (_, _) => await LoadRemoteObsPresetsAsync();
        MultiPcObsApplyPresetButton.Click += async (_, _) => await ApplyRemoteObsPresetAsync();
        MultiPcObsDeletePresetButton.Click += async (_, _) => await DeleteRemoteObsPresetAsync();
        MultiPcLoadAgentLogsButton.Click += async (_, _) => await LoadRemoteAgentLogsAsync();
        MultiPcStageUpdateButton.Click += async (_, _) => await DeployRemotePackageAsync("update/stage", "Update-ZIP auswählen", "Update-Paket wurde bereitgestellt");
        MultiPcLoadUpdateStatusButton.Click += async (_, _) => await LoadRemoteUpdateStatusAsync();
        MultiPcLoadUpdateHistoryButton.Click += async (_, _) => await LoadRemoteUpdateHistoryAsync();
        MultiPcValidateUpdateButton.Click += async (_, _) => await ExecuteRemoteUpdateActionAsync("validate");
        MultiPcApplyUpdateButton.Click += async (_, _) => await ExecuteRemoteUpdateActionAsync("apply");
        MultiPcRollbackUpdateButton.Click += async (_, _) => await ExecuteRemoteUpdateActionAsync("rollback");
        MultiPcStartRolloutButton.Click += async (_, _) => await StartRemoteUpdateRolloutAsync();
        MultiPcCancelRolloutButton.Click += (_, _) => CancelRemoteUpdateRollout();
        MultiPcScheduleRolloutButton.Click += async (_, _) => await ScheduleRemoteUpdateRolloutAsync();
        MultiPcCancelScheduledRolloutButton.Click += (_, _) => CancelScheduledRemoteUpdateRollout();
        MultiPcLoadAuditButton.Click += (_, _) => LoadMultiPcRolloutAudit();
        MultiPcAssignRolloutGroupButton.Click += (_, _) => AssignSelectedDeviceToRolloutGroup();
        Loaded += async (_, _) =>
            await RunStartupStepSafelyAsync("Geplanten Remote-Rollout wiederherstellen", RestoreScheduledRemoteUpdateRolloutAsync);
        MultiPcSaveAgentSettingsButton.Click += async (_, _) => await SaveRemoteAgentSettingsAsync();
        _multiPcRefreshTimer.Tick += async (_, _) =>
        {
            if (MultiPcPage.Visibility == Visibility.Visible)
            {
                await RefreshMultiPcPageAsync();
                if (MultiPcObsAutoSyncCheckBox.IsChecked == true && GetSelectedRemoteDevice() is not null)
                {
                    await RefreshRemoteObsStateAsync();
                    await RefreshRemoteObsOutputStateAsync();
                }
            }
        };
        _multiPcRefreshTimer.Start();
        LoadMultiPcRegistry();
        GenerateMultiPcPairingCode();

        InitializeDashboardBindings();
        InitializeDashboardLayoutBindings();
        InitializeDiagnosticsBindings();
        InitializeObsBindings();
        InitializeTwitchBindings();
        InitializeSpotifyBindings();
        InitializeServiceBindings();
        InitializeRunOfShowBindings();
        _spotifySavedStateHistoryView = InitializeTimedAutomationBindings();
        InitializeTimedAutomationPostBindings();
        InitializeStreamDeckBindings();
    }

    private sealed record TwitchRewardRedemptionItem(TwitchRewardRedemption Redemption)
    {
        public string DisplayText => $"{Redemption.UserDisplayName} · {Redemption.RewardTitle}" + (string.IsNullOrWhiteSpace(Redemption.UserInput) ? "" : $" · {Redemption.UserInput}");
    }
}
