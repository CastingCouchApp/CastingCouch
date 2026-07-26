using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Diagnostics;
using CreatorControlSuite.Core.Logging;
using CreatorControlSuite.Core.Validation;
using CreatorControlSuite.Core.Security;
using CreatorControlSuite.Core.Profiles;
using CreatorControlSuite.Core.Updates;
using CreatorControlSuite.Core.Migration;
using CreatorControlSuite.Modules.OBS;
using CreatorControlSuite.Modules.OBS.Models;
using CreatorControlSuite.Modules.Twitch;
using CreatorControlSuite.Modules.Twitch.Models;
using CreatorControlSuite.Modules.Spotify;
using CreatorControlSuite.Modules.Spotify.Models;
using CreatorControlSuite.Modules.Alerts;
using CreatorControlSuite.Modules.Alerts.Models;
using CreatorControlSuite.Modules.Overlay;
using CreatorControlSuite.Modules.Workflow;
using CreatorControlSuite.Modules.Workflow.Models;
using CreatorControlSuite.Modules.StreamDeck;
using CreatorControlSuite.App.Services;
using CreatorControlSuite.Core.Ipc;
using CreatorControlSuite.Core.Licensing;
using CreatorControlSuite.Core.Legal;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using System.Windows.Data;

namespace CreatorControlSuite.App;

public partial class MainWindow : Window
{
    private readonly SemaphoreSlim _spotifyOverlayWriteLock = new(1, 1);
    private readonly SpotifyListeningStatisticsService _spotifyListeningStatistics = new();
    private readonly SpotifyAutomationLogService _spotifyAutomationLog = new();
    private readonly SemaphoreSlim _spotifyAutomationLock = new(1, 1);
    private DateTimeOffset _lastSpotifyHealthRecoveryAt = DateTimeOffset.MinValue;
    private string? _lastObservedObsProgramScene;
    private bool? _lastSpotifyOverlayMuted;
    private bool _spotifyOverlayConnectionLatched;
    private bool _spotifyExplicitDisconnectInProgress;
    private bool _legacyOverlayWriterChecked;
    private SpotifyPlaybackState? _lastStableSpotifyPlayback;
    private DateTimeOffset _lastSpotifyPlayingAt = DateTimeOffset.MinValue;
    private bool? _lastKnownSpotifyObsMute;
    private readonly SemaphoreSlim _spotifyOverlayVisibilityLock = new(1, 1);
    private int? _lastRequestedSpotifyVolumePercent;
    private DateTimeOffset? _lastRequestedSpotifyVolumeAt;
    private bool _spotifyStartToGameVolumeChangeRunning;
    private readonly ISettingsStore _settingsStore;
    private readonly ISecretStore _secretStore;
    private readonly DiagnosticService _diagnostics;
    private readonly IObsWebSocketClient _obsClient;
    private readonly TwitchModule _twitchModule;
    private readonly SpotifyModule _spotifyModule;
    private readonly AlertsModule _alertsModule;
    private readonly OverlayModule _overlayModule;
    private readonly WorkflowModule _workflowModule;
    private readonly IProfileService _profileService;
    private readonly IUpdateService _updateService;
    private readonly ILegacyMigrationService _migrationService;
    private readonly StreamDeckModule _streamDeckModule;
    private readonly IAppLogger _appLogger;
    private readonly ISettingsValidator _settingsValidator;
    private readonly RuntimeHealthService _runtimeHealthService;
    private readonly ICrashReporter _crashReporter;
    private readonly ObsBrowserSourceInstaller _obsBrowserSourceInstaller;
    private readonly OverlayProjectService _overlayProjectService;
    private readonly ObservableCollection<OverlayProjectDefinition> _overlayProjects = [];
    private readonly ObservableCollection<OverlayProjectItem> _overlayProjectItems = [];
    private readonly ILocalIpcServer _ipcServer;
    private readonly ILicenseService _licenseService;
    private readonly ILegalConsentService _legalConsentService;
    private readonly IFeatureGate _featureGate;
    private readonly ISupportPackageService _supportPackageService;
    private readonly IReleaseReadinessService _releaseReadinessService;
    private readonly IWorkflowE2eService _workflowE2eService;
    private readonly IInstallerSelfTestService _installerSelfTestService;
    private readonly IBetaReadinessService _betaReadinessService;
    private readonly ObservableCollection<AppLogEntry> _visibleLogs = [];
    private readonly ObservableCollection<SpotifyApiInspectorRow> _spotifyInspectorRows = [];
    private bool _logsPaused;
    private readonly System.Windows.Threading.DispatcherTimer _alertAudioPreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private bool _updatingAlertAudioTrimUi;
    private readonly ObservableCollection<string> _twitchChatItems = [];
    private readonly ObservableCollection<TwitchRewardRedemptionItem> _twitchRedemptionItems = [];
    private readonly ObservableCollection<string> _twitchModerationLogItems = [];
    private string? _activeTwitchPollId;
    private TwitchPrediction? _activeTwitchPrediction;
    private readonly ObservableCollection<string> _twitchEventItems = [];
    private readonly ObservableCollection<string> _twitchUserItems = [];
    private readonly ObservableCollection<string> _dashboardPreflightItems = [];
    private readonly ObservableCollection<string> _dashboardNotificationItems = [];
    private readonly List<DashboardNotificationEntry> _dashboardNotifications = [];
    private readonly ObservableCollection<string> _streamHistoryItems = [];
    private readonly ObservableCollection<string> _twitchProfessionalHistoryItems = [];
    private readonly ObservableCollection<string> _creatorIntelligenceRecommendations = [];
    private readonly CreatorIntelligenceService _creatorIntelligence = new();
    private DateTimeOffset? _streamSessionStartedAt;
    private int _consecutiveObsStreamInactivePolls;
    private const int ConfirmedObsOfflinePollsRequired = 15;
    private bool _spotifyStartPlaylistTriggeredForCurrentStream;
    private bool _loadingSettingsIntoUi;
    private readonly Dictionary<string, string> _twitchUserDisplayById =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<string> _dashboardModuleOrderItems = [];
    private readonly ObservableCollection<TimedAutomationRuleSettings> _timedAutomationRules = [];
    private readonly ObservableCollection<string> _timedAutomationDiagnostics = [];
    private readonly ObservableCollection<RunOfShowStepSettings> _runOfShowSteps = [];
    private bool _updatingRunOfShowPlanUi;
    private int _runOfShowCurrentIndex = -1;
    private CancellationTokenSource? _runOfShowAutoCts;
    private readonly HashSet<string> _executedTimedAutomationRuleIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Threading.DispatcherTimer _timedAutomationTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly System.Windows.Threading.DispatcherTimer _spotifySavedStateCleanupTimer = new() { Interval = TimeSpan.FromMinutes(15) };
    private DateTimeOffset? _automationSceneActivatedAt;
    private string _automationCurrentScene = "";
    private CancellationTokenSource? _timedAutomationTestCts;
    private bool _timedAutomationEvaluationRunning;
    private DateTimeOffset _lastTimedAutomationObsRefresh = DateTimeOffset.MinValue;
    private bool _timedAutomationObsRefreshRunning;
    private readonly Dictionary<string, CancellationTokenSource> _activeTimedAutomationRuns = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _timedAutomationRunSync = new();

    private System.Windows.Point _dashboardModuleDragStart;
    private string? _dashboardDraggedModuleName;
    private bool _dashboardLayoutEditMode;
    private bool _dashboardFocusModeActive;
    private List<string>? _dashboardPreFocusOrder;
    private Dictionary<string, string>? _dashboardPreFocusSizes;
    private Dictionary<string, bool>? _dashboardPreFocusVisibility;
    private FrameworkElement? _dashboardDraggedSection;
    private System.Windows.Point _dashboardDirectDragStart;
    private FrameworkElement? _dashboardSelectedSection;
    private AppSettings _settings = new();
    private UpdatePackage? _pendingUpdatePackage;
    private bool _settingsUiLoaded;
    private bool _updatingSpotifyUi;
    private string? _lastSpotifyAlbumCoverUrl;
    private string? _lastCreatorIntelligenceTrackId;
    private CancellationTokenSource? _spotifyVolumeChangeCts;
    private CancellationTokenSource? _spotifyAutomationCts;
    private readonly object _spotifyAutomationSync = new();
    private int _activeSpotifyAutomationPriority = int.MinValue;
    private string _activeSpotifyAutomationGroup = "";
    private bool _activeSpotifyAutomationExclusive;
    private readonly Dictionary<string, SpotifyAutomationSavedState> _spotifyAutomationSavedStates = new(StringComparer.OrdinalIgnoreCase);

    private sealed record SpotifyAutomationSavedState(
        string ContextUri,
        CreatorControlSuite.Modules.Spotify.Models.SpotifyTrack? Track,
        int ProgressMs,
        int VolumePercent,
        bool ShuffleEnabled,
        string RepeatMode,
        bool WasPlaying,
        DateTimeOffset SavedAtUtc);

    private sealed record SpotifySavedStateOverviewItem(string Group, string Summary, bool IsExpired);
    private sealed record SpotifySavedStateHistoryBackupItem(string FullPath, string DisplayName, DateTime LastWriteTime, long SizeBytes);
    private sealed record SpotifyHistoryRestoreProfile(string Name, bool Entries, bool Favorites, bool Notes, bool Counters, bool Filters, bool MergeEntries, bool IsBuiltIn = false);
    private sealed class SpotifyHistoryRestoreProfileImportItem
    {
        public required SpotifyHistoryRestoreProfile Profile { get; init; }
        public required string Status { get; init; }
        public required string Description { get; init; }
        public required List<string> ActionOptions { get; init; }
        public required string SelectedAction { get; set; }
        public bool CanSelect { get; init; }
    }
    private sealed record SpotifySavedStateHistoryExport(
        int FormatVersion,
        DateTimeOffset ExportedAtUtc,
        int SavedCount,
        int RestoredCount,
        int DiscardedCount,
        int CleanupCount,
        List<string> Entries,
        List<string>? FavoriteEntries = null,
        Dictionary<string, string>? Notes = null);
    private sealed record SpotifySavedStateHistoryPersistence(
        int FormatVersion,
        int SavedCount,
        int RestoredCount,
        int DiscardedCount,
        int CleanupCount,
        List<string> Entries,
        List<string>? FavoriteEntries,
        Dictionary<string, string>? Notes,
        string SearchText,
        int ActionFilterIndex,
        int SortIndex,
        bool FavoritesOnly);
    private sealed class SpotifySavedStateHistoryComparer(string mode) : System.Collections.IComparer
    {
        public int Compare(object? x, object? y)
        {
            var left = x as string ?? "";
            var right = y as string ?? "";
            return mode switch
            {
                "oldest" => string.CompareOrdinal(ExtractTime(left), ExtractTime(right)),
                "action" => string.Compare(ExtractMessage(left), ExtractMessage(right), StringComparison.OrdinalIgnoreCase),
                "group" => string.Compare(ExtractGroup(left), ExtractGroup(right), StringComparison.OrdinalIgnoreCase),
                _ => string.CompareOrdinal(ExtractTime(right), ExtractTime(left))
            };
        }

        private static string ExtractTime(string entry)
        {
            var separator = entry.IndexOf(" · ", StringComparison.Ordinal);
            return separator >= 0 ? entry[..separator] : "";
        }

        private static string ExtractMessage(string entry)
        {
            var separator = entry.IndexOf(" · ", StringComparison.Ordinal);
            return separator >= 0 ? entry[(separator + 3)..] : entry;
        }

        private static string ExtractGroup(string entry)
        {
            var message = ExtractMessage(entry);
            var separator = message.IndexOf(':');
            return separator > 0 ? message[..separator].Trim() : message;
        }
    }
    private readonly ObservableCollection<string> _spotifySavedStateHistory = [];
    private readonly ObservableCollection<SpotifySavedStateHistoryBackupItem> _spotifySavedStateHistoryBackups = [];
    private readonly ObservableCollection<string> _spotifySavedStateHistoryBackupDifferences = [];
    private readonly ObservableCollection<SpotifyHistoryRestoreProfile> _spotifyHistoryRestoreProfiles = [];
    private readonly ObservableCollection<SpotifyHistoryRestoreProfileImportItem> _spotifyHistoryRestoreProfileImportPreview = [];
    private List<SpotifyHistoryRestoreProfile> _pendingSpotifyHistoryRestoreProfileImport = [];
    private string _pendingSpotifyHistoryRestoreProfileImportPath = "";
    private readonly HashSet<string> _spotifySavedStateHistoryFavorites = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _spotifySavedStateHistoryNotes = new(StringComparer.Ordinal);
    private ICollectionView? _spotifySavedStateHistoryView;
    private int _spotifySavedStateSaveCount;
    private int _spotifySavedStateRestoreCount;
    private int _spotifySavedStateDiscardCount;
    private int _spotifySavedStateCleanupCount;
    private bool _loadingSpotifySavedStateHistoryPersistence;
    private DateTimeOffset _lastSpotifySavedStateHistoryBackupUtc = DateTimeOffset.MinValue;
    private string SpotifySavedStateHistoryPersistencePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CreatorControlSuite",
        "spotify-saved-state-history.json");
    private string SpotifySavedStateHistoryBackupDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CreatorControlSuite",
        "Backups",
        "SpotifyHistory");
    private string SpotifyHistoryRestoreProfilesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CreatorControlSuite",
        "spotify-history-restore-profiles.json");
    private readonly SemaphoreSlim _spotifyAlertMuteGate = new(1, 1);
    private readonly ExternalAlertActivityService _externalAlertActivity;
    private bool _suiteAlertRunning;
    private int _suiteAlertQueueLength;
    private int? _spotifyVolumeBeforeAlert;
    private bool _spotifyWasPlayingBeforeAlert;
    private bool _spotifyAlertMuteActive;
    private bool _lastObsStreamActive;
    private CancellationTokenSource? _streamStartAutomationCts;
    private CancellationTokenSource? _raidCountdownCts;
    private bool _raidCountdownActive;
    private System.Net.WebSockets.ClientWebSocket? _streamerBotSocket;
    private System.Net.WebSockets.ClientWebSocket? _streamerBotEventSocket;
    private CancellationTokenSource? _streamerBotEventCts;
    private readonly SemaphoreSlim _streamerBotRequestGate = new(1, 1);
    private readonly ObservableCollection<StreamerBotActionOption> _streamerBotActions = [];
    private readonly ObservableCollection<StreamerBotExecutionHistoryItem> _streamerBotExecutionHistory = [];
    private readonly ObservableCollection<StreamerBotActionTemplate> _streamerBotActionTemplates = [];
    private readonly ObservableCollection<StreamerBotLiveEventItem> _streamerBotLiveEvents = [];
    private CancellationTokenSource? _streamerBotScheduledActionCts;
    private readonly HashSet<string> _streamerBotFavoriteActionIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Threading.DispatcherTimer _twitchUsersRefreshTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly System.Windows.Threading.DispatcherTimer _liveViewerSampleTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool _liveViewerSampleRunning;
    private int _currentLiveViewerCount;
    private readonly Queue<int> _dashboardViewerTrendSamples = new();
    private int _currentFollowerCount;
    private int _streamFollowerBaseline;
    private int _currentActiveSubscriptionCount;
    private int _twitchSessionChatMessages;
    private int _twitchSessionEvents;
    private readonly HashSet<string> _twitchSessionUniqueChatters = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _twitchSessionObservedAt;
    private DateTimeOffset _lastSpotifyRateLimitNotice = DateTimeOffset.MinValue;
    private DateTimeOffset _spotifyRateLimitUntil = DateTimeOffset.MinValue;
    private CancellationTokenSource? _spotifyRateLimitResetCts;
    private static readonly System.Net.Http.HttpClient AlbumCoverHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly System.Net.Http.HttpClient RaidProfileHttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly System.Windows.Threading.DispatcherTimer _dashboardResourceTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly System.Windows.Threading.DispatcherTimer _dashboardLiveRefreshTimer =
        new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly System.Windows.Threading.DispatcherTimer _obsPreviewRefreshTimer =
        new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _obsPreviewRefreshRunning;
    private bool _dashboardLiveRefreshRunning;
    private readonly System.Windows.Threading.DispatcherTimer _connectionWatchdogTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly System.Windows.Threading.DispatcherTimer _streamDeckStateSyncTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly System.Windows.Threading.DispatcherTimer _streamDeckRuleTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Dictionary<string, DateTimeOffset> _streamDeckRuleFirstMatch = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _streamDeckRuleHistory = new();
    private bool _connectionWatchdogRunning;
    private readonly Dictionary<string, DateTimeOffset> _lastReconnectAttempt =
        new(StringComparer.OrdinalIgnoreCase);
    private TimeSpan _lastDashboardCpuTime = Process.GetCurrentProcess().TotalProcessorTime;
    private DateTimeOffset _lastDashboardResourceSample = DateTimeOffset.Now;
    private long _lastObsOutputBytes;
    private DateTimeOffset? _lastObsBitrateSampleAt;
    private double _currentObsBitrateKbps;
    private IReadOnlyList<ObsSceneInfo> _servicesObsScenes = Array.Empty<ObsSceneInfo>();
    private IReadOnlyList<ObsSceneItemInfo> _servicesObsSceneItems = Array.Empty<ObsSceneItemInfo>();
    private IReadOnlyList<ObsInputInfo> _servicesObsInputs = Array.Empty<ObsInputInfo>();
    private readonly Dictionary<string, ObsInputVolumeMeter> _obsLiveMeters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (double PeakDb, DateTimeOffset At)> _obsPeakHold = new(StringComparer.OrdinalIgnoreCase);
    private string _servicesObsCurrentScene = string.Empty;
    private readonly ObservableCollection<string> _multiPcDeviceItems = [];
    private readonly ObservableCollection<string> _multiPcHistoryItems = [];
    private readonly ObservableCollection<string> _multiPcRolloutItems = [];
    private CancellationTokenSource? _multiPcRolloutCts;
    private CancellationTokenSource? _scheduledMultiPcRolloutCts;
    private readonly Dictionary<string, string> _multiPcRolloutGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Threading.DispatcherTimer _multiPcRefreshTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly List<MultiPcDeviceRecord> _multiPcDevices = [];
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
        DiagnosticService diagnostics,
        IObsWebSocketClient obsClient,
        TwitchModule twitchModule,
        SpotifyModule spotifyModule,
        AlertsModule alertsModule,
        OverlayModule overlayModule,
        WorkflowModule workflowModule,
        IProfileService profileService,
        IUpdateService updateService,
        ILegacyMigrationService migrationService,
        StreamDeckModule streamDeckModule,
        IAppLogger appLogger,
        ISettingsValidator settingsValidator,
        RuntimeHealthService runtimeHealthService,
        ICrashReporter crashReporter,
        ObsBrowserSourceInstaller obsBrowserSourceInstaller,
        ILocalIpcServer ipcServer,
        ILicenseService licenseService,
        ILegalConsentService legalConsentService,
        IFeatureGate featureGate,
        ISupportPackageService supportPackageService,
        IReleaseReadinessService releaseReadinessService,
        IWorkflowE2eService workflowE2eService,
        IInstallerSelfTestService installerSelfTestService,
        IBetaReadinessService betaReadinessService,
        ExternalAlertActivityService externalAlertActivity)
    {
        InitializeComponent();

        _settingsStore = settingsStore;
        _externalAlertActivity = externalAlertActivity;
        _externalAlertActivity.ActiveCountChanged += async (_, _) => await ApplyCombinedAlertDuckingAsync();
        _secretStore = secretStore;
        _diagnostics = diagnostics;
        _obsClient = obsClient;
        _twitchModule = twitchModule;
        _spotifyModule = spotifyModule;
        _alertsModule = alertsModule;
        _overlayModule = overlayModule;
        _workflowModule = workflowModule;
        _profileService = profileService;
        _updateService = updateService;
        _migrationService = migrationService;
        _streamDeckModule = streamDeckModule;
        _appLogger = appLogger;
        _settingsValidator = settingsValidator;
        _runtimeHealthService = runtimeHealthService;
        _crashReporter = crashReporter;
        _obsBrowserSourceInstaller = obsBrowserSourceInstaller;
        _overlayProjectService = new OverlayProjectService(_obsClient, _appLogger, _overlayModule.Service);
        _ipcServer = ipcServer;
        _licenseService = licenseService;
        _legalConsentService = legalConsentService;
        _featureGate = featureGate;
        _supportPackageService = supportPackageService;
        _releaseReadinessService = releaseReadinessService;
        _workflowE2eService = workflowE2eService;
        _installerSelfTestService = installerSelfTestService;
        _betaReadinessService = betaReadinessService;

        DashboardModuleOrderList.ItemsSource = _dashboardModuleOrderItems;
        DashboardModuleOrderList.PreviewMouseLeftButtonDown += DashboardModuleOrderList_PreviewMouseLeftButtonDown;
        DashboardModuleOrderList.PreviewMouseMove += DashboardModuleOrderList_PreviewMouseMove;
        DashboardModuleOrderList.Drop += DashboardModuleOrderList_Drop;

        DashboardPresetBox.SelectedIndex = 0;
        DashboardQuickPresetBox.SelectedIndex = 0;
        DashboardApplyPresetButton.Click += async (_, _) =>
        {
            ApplySelectedDashboardPreset(DashboardPresetBox);
            await _settingsStore.SaveAsync(_settings);
        };
        DashboardQuickApplyPresetButton.Click += async (_, _) =>
        {
            ApplySelectedDashboardPreset(DashboardQuickPresetBox);
            await _settingsStore.SaveAsync(_settings);
        };
        DashboardFocusModeButton.Click += (_, _) =>
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

        DashboardModuleOrderList.SelectionChanged += (_, _) => RefreshDashboardModuleSizeEditor();
        DashboardApplyModuleSizeButton.Click += async (_, _) =>
        {
            ApplySelectedDashboardModuleSizeFromSettingsEditor();
            await _settingsStore.SaveAsync(_settings);
        };
        DashboardDirectApplySizeButton.Click += async (_, _) =>
        {
            ApplySelectedDashboardModuleSizeFromDirectEditor();
            await _settingsStore.SaveAsync(_settings);
        };

        DashboardEditLayoutButton.Click += (_, _) => ToggleDashboardLayoutEditMode();
        DashboardRestoreHiddenModulesButton.Click += (_, _) => RestoreAllDashboardModules();
        DashboardContentStack.Drop += DashboardContentStack_Drop;
        DashboardContentStack.DragOver += DashboardContentStack_DragOver;
        RegisterDashboardDirectDragHandlers();

        IpcStatusText.Text = _ipcServer.IsRunning
            ? "IPC aktiv: " + NamedPipeIpcServer.PipeName
            : "IPC nicht aktiv.";

        _ipcServer.StateChanged += (_, running) =>
        {
            Dispatcher.Invoke(() =>
            {
                IpcStatusText.Text = running
                    ? "IPC aktiv: " + NamedPipeIpcServer.PipeName
                    : "IPC nicht aktiv.";
            });
        };

        LogsGrid.ItemsSource = _visibleLogs;
        SpotifyInspectorGrid.ItemsSource = _spotifyInspectorRows;
        LogLevelFilterBox.SelectedIndex = 0;

        TwitchChatList.ItemsSource = _twitchChatItems;
        TwitchEventsList.ItemsSource = _twitchEventItems;
        DashboardTwitchChatList.ItemsSource = _twitchChatItems;
        DashboardTwitchEventsList.ItemsSource = _twitchEventItems;
        DashboardTwitchUsersList.ItemsSource = _twitchUserItems;
        ServicesTwitchChatList.ItemsSource = _twitchChatItems;
        ServicesTwitchEventsList.ItemsSource = _twitchEventItems;
        ServicesRedemptionsList.ItemsSource = _twitchRedemptionItems;
        ServicesTwitchUsersList.ItemsSource = _twitchUserItems;
        ServicesTwitchProfessionalHistoryList.ItemsSource = _twitchProfessionalHistoryItems;
        ServicesTwitchModerationLogList.ItemsSource = _twitchModerationLogItems;
        ServicesCreatorIntelligenceRecommendationsList.ItemsSource = _creatorIntelligenceRecommendations;
        ServicesRefreshTwitchProfessionalButton.Click += async (_, _) =>
        {
            await RefreshLiveViewerSampleAsync();
            await RefreshTwitchGoalsAsync();
            await LoadTwitchProfessionalHistoryAsync();
            RefreshTwitchProfessionalUi();
        };
        ServicesOpenTwitchProfessionalHistoryButton.Click += (_, _) => OpenStreamHistoryFolder();
        ServicesExportTwitchProfessionalHistoryButton.Click += async (_, _) => await ExportTwitchProfessionalHistoryCsvAsync();
        ServicesCreateTwitchProfessionalReportButton.Click += async (_, _) => await CreateTwitchProfessionalReportAsync();
        ServicesCopyTwitchProfessionalSummaryButton.Click += async (_, _) => await CopyLatestTwitchProfessionalSummaryAsync();
        ServicesModerationPreset1Button.Click += (_, _) => ServicesModerationDurationBox.Text = "1";
        ServicesModerationPreset10Button.Click += (_, _) => ServicesModerationDurationBox.Text = "10";
        ServicesModerationPreset60Button.Click += (_, _) => ServicesModerationDurationBox.Text = "60";
        ServicesModerationPreset1440Button.Click += (_, _) => ServicesModerationDurationBox.Text = "1440";
        ServicesClearModerationLogButton.Click += (_, _) => _twitchModerationLogItems.Clear();
        ServicesExportModerationLogButton.Click += async (_, _) => await ExportTwitchModerationLogAsync();
        ServicesCreatorIntelligenceRefreshButton.Click += async (_, _) => await RefreshCreatorIntelligenceAsync();
        ServicesCreatorIntelligenceOpenFolderButton.Click += (_, _) => OpenCreatorIntelligenceFolder();
        ServicesCreatorIntelligenceAddNoteButton.Click += async (_, _) => await AddCreatorIntelligenceNoteAsync();
        ServicesCreatorIntelligenceWeeklyReportButton.Click += async (_, _) => await CreateCreatorIntelligenceWeeklyReportAsync();
        ServicesCreatorIntelligenceCompleteActionButton.Click += async (_, _) => await CompleteSelectedCreatorActionAsync();
        ServicesCreatorIntelligenceStartExperimentButton.Click += async (_, _) => await StartSelectedCreatorExperimentAsync();
        SelectDashboardStatisticInUi();
        StatisticsDashboardMetricBox.SelectionChanged += (_, _) =>
        {
            if (StatisticsDashboardMetricBox.SelectedItem is ComboBoxItem item && item.Tag is string metric)
            {
                _settings.Dashboard.DashboardStatistic = metric;
                UpdateDashboardSelectedStatistic();
                _ = _settingsStore.SaveAsync(_settings);
            }
        };
        DashboardTwitchUsersList.SelectionChanged += (_, _) => CopySelectedModerationUser(DashboardTwitchUsersList, DashboardModerationUserBox);
        ServicesTwitchUsersList.SelectionChanged += (_, _) => CopySelectedModerationUser(ServicesTwitchUsersList, ServicesModerationUserBox);
        DashboardPreflightList.ItemsSource = _dashboardPreflightItems;
        DashboardNotificationList.ItemsSource = _dashboardNotificationItems;
        DashboardStreamHistoryList.ItemsSource = _streamHistoryItems;
        _twitchUsersRefreshTimer.Tick += async (_, _) => await RefreshTwitchUsersAsync();
        _twitchUsersRefreshTimer.Start();
        _liveViewerSampleTimer.Tick += async (_, _) => await RefreshLiveViewerSampleAsync();
        _liveViewerSampleTimer.Start();

        Loaded += async (_, _) =>
        {
            try
            {
                await RunStartupStepSafelyAsync("Einstellungen laden", LoadSettingsAsync);
                await RunStartupStepSafelyAsync("Overlay-Projekte laden", LoadOverlayProjectsAsync);
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

        DashboardOpenObsServiceButton.Click += (_, _) =>
            NavigateToServicesTab(2, ServicesObsButton);
        DashboardOpenTwitchServiceButton.Click += (_, _) =>
            NavigateToServicesTab(1, ServicesTwitchButton);
        DashboardOpenSpotifyServiceButton.Click += (_, _) =>
            NavigateToServicesTab(0, ServicesSpotifyButton);
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
        ServicesOverviewSpotifyButton.Click += (_, _) =>
            NavigateToServicesTab(0, ServicesSpotifyButton);
        ServicesOverviewTwitchButton.Click += (_, _) =>
            NavigateToServicesTab(1, ServicesTwitchButton);
        ServicesOverviewObsButton.Click += (_, _) =>
            NavigateToServicesTab(2, ServicesObsButton);
        ServicesOverviewStreamerBotButton.Click += (_, _) =>
            NavigateToServicesTab(3, ServicesStreamerBotButton);
        ServicesOverviewStreamDeckButton.Click += (_, _) =>
            NavigateToServicesTab(4, ServicesStreamDeckButton);
        WorkflowButton.Click += (_, _) =>
        {
            ServicesNavigationPanel.Visibility = Visibility.Collapsed;
            ShowPage(WorkflowPage);
        };
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
            await RefreshStatisticsAsync();
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
        MultiPcDeployOverlayButton.Click += async (_, _) => await DeployRemotePackageAsync("overlay/deploy", "Overlay-ZIP auswählen", "Overlay-Paket wurde verteilt");
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

        DashboardQuickStartObsButton.Click += (_, _) => LaunchConfiguredExecutable(_settings.Obs.ExecutablePath, "OBS");
        DashboardQuickOpenTwitchButton.Click += (_, _) =>
            NavigateToServicesTab(1);
        DashboardQuickStartSpotifyButton.Click += (_, _) => LaunchConfiguredExecutable(_settings.Spotify.ExecutablePath, "Spotify");
        DashboardQuickStartStreamerBotButton.Click += (_, _) => LaunchConfiguredExecutable(_settings.StreamerBot.ExecutablePath, "Streamer.bot");
        DashboardQuickTestAlertButton.Click += async (_, _) =>
        {
            if (AlertTypeBox.SelectedItem is null && AlertTypeBox.Items.Count > 0) AlertTypeBox.SelectedIndex = 0;
            await TestAlertInObsAsync();
        };
        DashboardQuickOpenOverlayButton.Click += async (_, _) => await OpenOverlayFolderAsync();
        DashboardQuickAccessAlertButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardQuickAccessAlertButton,
                "Test-Alert",
                async () =>
                {
                    DashboardQuickTestAlertButton.RaiseEvent(
                        new RoutedEventArgs(Button.ClickEvent));
                    await Task.Delay(250);
                },
                refreshDashboard: false);
        DashboardQuickAccessOverlayButton.Click += (_, _) =>
            ShowPage(OverlayPage);
        DashboardShortStreamTestButton.Click += async (_, _) =>
        {
            ShowPage(WorkflowPage);
            WorkflowTabControl.SelectedIndex = 2;
            await RefreshTimedAutomationObsListsAsync();
            ShortStreamTestStatusText.Text = "Kurztest bereit. Der Stream wird nicht gestartet.";
        };
        DashboardServicesStreamDeckButton.Click += (_, _) =>
            NavigateToServicesTab(4, ServicesStreamDeckButton);
        DashboardTopOpenStreamDeckButton.Click += (_, _) =>
            NavigateToServicesTab(4, ServicesStreamDeckButton);
        DashboardServiceConnectObsButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardServiceConnectObsButton,
                "OBS-Verbindung",
                ToggleObsFromDashboardAsync);
        DashboardServiceConnectTwitchButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardServiceConnectTwitchButton,
                "Twitch-Verbindung",
                ToggleTwitchFromDashboardAsync);
        DashboardServiceConnectSpotifyButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardServiceConnectSpotifyButton,
                "Spotify-Verbindung",
                ToggleSpotifyFromDashboardAsync);
        DashboardServiceConnectStreamerBotButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardServiceConnectStreamerBotButton,
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
        DashboardOpenTwitchChatButton.Click += (_, _) =>
            OpenDashboardTwitchChat();
        DashboardManageAutomationsButton.Click += (_, _) => ShowPage(WorkflowPage);
        DashboardOpenEventsButton.Click += async (_, _) =>
        {
            ShowPage(StatisticsPage);
            await RefreshStatisticsAsync();
        };
        DashboardOpenDiagnosticsButton.Click += async (_, _) =>
        {
            ShowPage(DiagnosticsPage);
            await RunDiagnosticsAsync();
        };
        DashboardEventCenterList.ItemsSource = _twitchEventItems;
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

        DashboardSpotifyPreviousButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardSpotifyPreviousButton,
                "Spotify: vorheriger Titel",
                () => ExecuteSpotifyAsync(() => _spotifyModule.PreviousAsync()));
        DashboardSpotifyPlayButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardSpotifyPlayButton,
                "Spotify: Wiedergabe",
                () => ExecuteSpotifyAsync(() => _spotifyModule.ResumeAsync()));
        DashboardSpotifyPauseButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardSpotifyPauseButton,
                "Spotify: Pause",
                () => ExecuteSpotifyAsync(() => _spotifyModule.PauseAsync()));
        DashboardSpotifyNextButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardSpotifyNextButton,
                "Spotify: nächster Titel",
                () => ExecuteSpotifyAsync(() => _spotifyModule.NextAsync()));
        DashboardSpotifyShuffleButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardSpotifyShuffleButton,
                "Spotify: Zufallswiedergabe",
                () => ExecuteSpotifyAsync(async () =>
                {
                    var enabled = !_spotifyModule.GetSnapshot().Playback.ShuffleEnabled;
                    await _spotifyModule.SetShuffleAsync(enabled);
                    await RefreshSpotifyAsync();
                    AddDashboardNotification(
                        $"Spotify-Zufallswiedergabe wurde {(enabled ? "eingeschaltet" : "ausgeschaltet")}.",
                        "Info");
                }));
        DashboardSpotifyProgressBar.PreviewMouseLeftButtonUp += async (_, _) =>
        {
            if (_updatingSpotifyUi || !DashboardSpotifyProgressBar.IsEnabled) return;

            var targetMs = (int)Math.Round(DashboardSpotifyProgressBar.Value);
            DashboardSpotifyProgressBar.IsEnabled = false;
            try
            {
                await ExecuteSpotifyAsync(() => _spotifyModule.SeekAsync(targetMs));
            }
            finally
            {
                DashboardSpotifyProgressBar.IsEnabled = true;
            }
        };
        DashboardSpotifyRepeatButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardSpotifyRepeatButton,
                "Spotify: Wiederholung",
                () => ExecuteSpotifyAsync(async () =>
                {
                    var current = _spotifyModule.GetSnapshot().Playback.RepeatMode;
                    var next = current?.ToLowerInvariant() switch
                    {
                        "off" => "context",
                        "context" => "track",
                        _ => "off"
                    };
                    await _spotifyModule.SetRepeatAsync(next);
                    await RefreshSpotifyAsync();
                    var label = next switch
                    {
                        "context" => "Playlist",
                        "track" => "Titel",
                        _ => "Aus"
                    };
                    AddDashboardNotification($"Spotify-Wiederholung: {label}.", "Info");
                }));
        DashboardObsStartStreamButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardObsStartStreamButton,
                "Stream starten",
                StartObsStreamAsync);
        DashboardObsStopStreamButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardObsStopStreamButton,
                "Stream beenden",
                StopObsStreamAsync);
        DashboardHeaderStreamActionButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardHeaderStreamActionButton,
                "Stream umschalten",
                ToggleDashboardHeaderStreamAsync);
        DashboardSwitchSceneButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardSwitchSceneButton,
                "OBS-Szene wechseln",
                SwitchDashboardSceneAsync);
        DashboardSwitchNextSceneButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardSwitchNextSceneButton,
                "Nächste OBS-Szene wechseln",
                SwitchDashboardNextSceneAsync);
        DashboardRefreshScenesButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardRefreshScenesButton,
                "OBS-Szenen aktualisieren",
                RefreshObsAsync);
        DashboardRaidEnabledBox.Checked += async (_, _) =>
        {
            if (!_settingsUiLoaded)
            {
                return;
            }

            _settings.Twitch.RaidOnStreamEnd = true;
            ServicesTwitchRaidEnabledBox.IsChecked = true;
            UpdateDashboardRaidControlsVisibility();
            await _settingsStore.SaveAsync(_settings);
        };
        DashboardRaidEnabledBox.Unchecked += async (_, _) =>
        {
            if (!_settingsUiLoaded)
            {
                return;
            }

            _settings.Twitch.RaidOnStreamEnd = false;
            ServicesTwitchRaidEnabledBox.IsChecked = false;
            UpdateDashboardRaidControlsVisibility();
            await _settingsStore.SaveAsync(_settings);
        };
        DashboardRaidChannelBox.SelectionChanged += async (_, _) =>
        {
            if (!_settingsUiLoaded)
            {
                return;
            }

            if (DashboardRaidChannelBox.SelectedItem is string channel)
            {
                _settings.Twitch.SelectedRaidChannel = channel;
                ServicesTwitchRaidTargetBox.SelectedItem = channel;
                await RefreshRaidTargetStatusAsync(channel);
                await _settingsStore.SaveAsync(_settings);
            }
        };
        ServicesTwitchRaidTargetBox.SelectionChanged += async (_, _) =>
        {
            if (ServicesTwitchRaidTargetBox.SelectedItem is string channel)
            {
                _settings.Twitch.SelectedRaidChannel = channel;
                DashboardRaidChannelBox.SelectedItem = channel;
                await RefreshRaidTargetStatusAsync(channel);
            }
        };
        DashboardOpenRaidChannelButton.Click += (_, _) => OpenSelectedRaidChannel();
        ServicesTwitchOpenRaidChannelButton.Click += (_, _) => OpenSelectedRaidChannel();
        DashboardCancelRaidButton.Click += async (_, _) => await CancelActiveRaidAsync();
        ServicesTwitchAddRaidChannelButton.Click += async (_, _) => await AddRaidChannelAsync();
        ServicesTwitchRemoveRaidChannelButton.Click += async (_, _) => await RemoveSelectedRaidChannelAsync();
        DashboardSpotifyVolumeSlider.ValueChanged += async (_, _) =>
        {
            DashboardSpotifyVolumeText.Text = $"{(int)Math.Round(DashboardSpotifyVolumeSlider.Value)} %";
            if (!_updatingSpotifyUi)
            {
                await QueueSpotifyVolumeUpdateAsync(75, (int)Math.Round(DashboardSpotifyVolumeSlider.Value));
            }
        };
        DashboardSendTwitchChatButton.Click += async (_, _) =>
        {
            TwitchChatMessageBox.Text =
                DashboardTwitchChatMessageBox.Text;

            await SendTwitchChatAsync();

            DashboardTwitchChatMessageBox.Clear();
        };

        DashboardTimeoutUserButton.Click += async (_, _) => await ModerateTwitchUserAsync(DashboardModerationUserBox.Text, false, "10", null);
        DashboardBanUserButton.Click += async (_, _) => await ModerateTwitchUserAsync(DashboardModerationUserBox.Text, true, null, null);
        ServicesTimeoutUserButton.Click += async (_, _) => await ModerateTwitchUserAsync(ServicesModerationUserBox.Text, false, ServicesModerationDurationBox.Text, ServicesModerationReasonBox.Text);
        ServicesBanUserButton.Click += async (_, _) => await ModerateTwitchUserAsync(ServicesModerationUserBox.Text, true, ServicesModerationDurationBox.Text, ServicesModerationReasonBox.Text);
        ServicesUnbanUserButton.Click += async (_, _) => await UnbanTwitchUserAsync(ServicesModerationUserBox.Text);

        DashboardCommandPrepareButton.Click += async (_, _) => await PrepareStreamAsync();
        DashboardCommandStartButton.Click += async (_, _) => await StartObsStreamAsync();
        DashboardCommandStopButton.Click += async (_, _) => await StopObsStreamAsync();
        DashboardRunPreflightButton.Click += async (_, _) => await RunDashboardPreflightAsync();
        DashboardSceneStartButton.Click += async (_, _) => await SwitchDashboardConfiguredSceneAsync(_settings.Obs.StartScene);
        DashboardSceneLiveButton.Click += async (_, _) => await SwitchDashboardConfiguredSceneAsync(_settings.Obs.LiveScene);
        DashboardScenePauseButton.Click += async (_, _) => await SwitchDashboardConfiguredSceneAsync(_settings.Obs.PauseScene);
        DashboardSceneEndButton.Click += async (_, _) => await SwitchDashboardConfiguredSceneAsync(_settings.Obs.EndScene);
        DashboardObsAudioInputBox.SelectionChanged += async (_, _) => await RefreshDashboardObsAudioStateAsync();
        DashboardObsAudioMuteButton.Click += async (_, _) => await SetDashboardObsAudioMuteAsync(true);
        DashboardObsAudioUnmuteButton.Click += async (_, _) => await SetDashboardObsAudioMuteAsync(false);
        DashboardObsAudioSetVolumeButton.Click += async (_, _) => await SetDashboardObsAudioVolumeAsync();
        DashboardOpenObsMixerButton.Click += (_, _) => ShowPage(ServicesPage);
        DashboardRefreshRaidAssistantButton.Click += async (_, _) =>
        {
            var channel = DashboardRaidChannelBox.SelectedItem as string ?? _settings.Twitch.SelectedRaidChannel;
            if (!string.IsNullOrWhiteSpace(channel))
            {
                await RefreshRaidTargetStatusAsync(channel);
                DashboardRaidAssistantText.Text = DashboardRaidTargetStatusText.Text;
            }
        };
        DashboardOpenProfilesButton.Click += async (_, _) => { ShowPage(ProfilesPage); await RefreshProfilesAsync(); };
        DashboardApplyProfileButton.Click += async (_, _) => await ApplyDashboardProfileAndPrepareAsync();
        DashboardOpenWorkflowButton.Click += (_, _) => ShowPage(WorkflowPage);
        DashboardClearNotificationsButton.Click += async (_, _) =>
        {
            _dashboardNotifications.Clear();
            RefreshDashboardNotificationView();
            await SaveDashboardNotificationsAsync();
        };
        DashboardMarkNotificationsReadButton.Click += async (_, _) =>
        {
            foreach (var item in _dashboardNotifications)
            {
                item.IsRead = true;
            }

            RefreshDashboardNotificationView();
            await SaveDashboardNotificationsAsync();
        };
        DashboardNotificationFilterBox.SelectionChanged += (_, _) => RefreshDashboardNotificationView();
        DashboardRefreshHistoryButton.Click += async (_, _) => await LoadStreamHistoryAsync();
        DashboardOpenHistoryFolderButton.Click += (_, _) => OpenStreamHistoryFolder();
        DashboardOpenStreamDeckSettingsButton.Click += (_, _) => ShowPage(SettingsPage);
        DashboardOpenServicesAdvancedButton.Click += (_, _) => ShowPage(ServicesPage);
        DashboardOpenDiagnosticsAdvancedButton.Click += async (_, _) => { ShowPage(DiagnosticsPage); await RunDiagnosticsAsync(); };
        DashboardOpenSettingsAdvancedButton.Click += (_, _) => ShowPage(SettingsPage);
        DashboardOpenProfilesAdvancedButton.Click += async (_, _) => { ShowPage(ProfilesPage); await RefreshProfilesAsync(); };

        StatisticsRefreshButton.Click += async (_, _) => await RefreshStatisticsAsync();
        StatisticsOpenFolderButton.Click += (_, _) => OpenStreamHistoryFolder();

        DashboardModuleMoveUpButton.Click += (_, _) => MoveDashboardModuleEditorItem(-1);
        DashboardModuleMoveDownButton.Click += (_, _) => MoveDashboardModuleEditorItem(1);
        DashboardModuleOrderResetButton.Click += (_, _) =>
        {
            _settings.Dashboard.ModuleOrder = GetDefaultDashboardModuleOrder().ToList();
            LoadDashboardModuleOrderEditor();
            ApplyDashboardModuleOrder();
        };

        DashboardApplyLayoutButton.Click += (_, _) =>
        {
            ApplyDashboardCheckboxesToSettings();
            SaveDashboardModuleOrderFromEditor();
            ApplyDashboardModuleOrder();
            ApplyDashboardModuleSizes();
            ApplyDashboardLayout();
        };
        DashboardResetLayoutButton.Click += (_, _) =>
        {
            DashboardShowServiceStatusBox.IsChecked = true;
            DashboardShowStreamControlsBox.IsChecked = true;
            DashboardShowLivePanelsBox.IsChecked = true;
            DashboardShowQuickServicesBox.IsChecked = true;
            DashboardShowWorkflowRailBox.IsChecked = true;
            DashboardShowAdvancedToolsBox.IsChecked = true;
            DashboardShowNotificationsBox.IsChecked = true;
            DashboardShowStreamHistoryBox.IsChecked = true;
            _settings.Dashboard.ModuleOrder = GetDefaultDashboardModuleOrder().ToList();
            LoadDashboardModuleOrderEditor();
            ApplyDashboardCheckboxesToSettings();
            ApplyDashboardModuleOrder();
            ApplyDashboardModuleSizes();
            ApplyDashboardLayout();
        };

        BrowseOverlayManifestButton.Click += (_, _) => BrowseOverlayManifest();
        CreateOverlayManifestButton.Click += async (_, _) => await CreateOverlayManifestAsync();
        OpenOverlayManifestButton.Click += (_, _) => OpenOverlayManifestFolder();
        SaveOverlayPageButton.Click += async (_, _) => await SaveSettingsAsync();
        SaveSettingsButton.Click += async (_, _) => await SaveSettingsAsync();
        SaveAlertsPageButton.Click += async (_, _) => await SaveSettingsAsync();
        RunDiagnosticsButton.Click += async (_, _) => await RunDiagnosticsAsync();

        ValidateSettingsButton.Click += async (_, _) =>
            await ValidateSettingsAsync();

        RefreshLogsButton.Click += async (_, _) =>
            await RefreshLogsAsync();

        RefreshSpotifyInspectorButton.Click += async (_, _) =>
            await RefreshSpotifyInspectorAsync();
        SpotifyInspectorFilterBox.SelectionChanged += async (_, _) =>
            await RefreshSpotifyInspectorAsync();

        CopySpotifyInspectorButton.Click += (_, _) =>
            CopySelectedSpotifyInspectorEntry();

        PauseLogsButton.Click += (_, _) =>
        {
            _logsPaused = !_logsPaused;
            PauseLogsButton.Content = _logsPaused
                ? "Log-Aktualisierung fortsetzen"
                : "Log-Aktualisierung pausieren";
        };

        CopySelectedLogButton.Click += (_, _) =>
            CopySelectedLog();

        ExportLogsButton.Click += async (_, _) =>
            await ExportLogsAsync();

        OpenCrashReportsButton.Click += (_, _) =>
            OpenLocalDataFolder("CrashReports");

        CreateSupportPackageButton.Click += async (_, _) => await CreateSupportPackageAsync();
        RunReleaseCheckButton.Click += async (_, _) => await RunReleaseCheckAsync();
        RunWorkflowE2eButton.Click += async (_, _) => await RunWorkflowE2eAsync();
        RunInstallerSelfTestButton.Click += async (_, _) => await RunInstallerSelfTestAsync();
        RefreshBetaReadinessButton.Click += async (_, _) => await RefreshBetaReadinessAsync();

        LogSearchBox.TextChanged += async (_, _) =>
            await RefreshLogsAsync();

        LogLevelFilterBox.SelectionChanged += async (_, _) =>
            await RefreshLogsAsync();

        _appLogger.EntryWritten += (_, entry) =>
        {
            if (_logsPaused)
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                if (LogMatchesFilter(entry))
                {
                    _visibleLogs.Insert(0, entry);

                    while (_visibleLogs.Count > 1000)
                    {
                        _visibleLogs.RemoveAt(
                            _visibleLogs.Count - 1);
                    }

                    if (entry.Category.StartsWith("Spotify.", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = RefreshSpotifyInspectorAsync();
                    }
                }
            });
        };

        ConnectObsButton.Click += async (_, _) => await ConnectObsAsync();
        DisconnectObsButton.Click += async (_, _) => await DisconnectObsAsync();
        RefreshObsButton.Click += async (_, _) => await RefreshObsAsync();
        RefreshWorkflowScenesButton.Click += async (_, _) => await RefreshObsAsync();
        StartSceneBox.DropDownOpened += async (_, _) => await RefreshObsAsync();
        LiveSceneBox.DropDownOpened += async (_, _) => await RefreshObsAsync();
        PauseSceneBox.DropDownOpened += async (_, _) => await RefreshObsAsync();
        EndSceneBox.DropDownOpened += async (_, _) => await RefreshObsAsync();
        SwitchObsSceneButton.Click += async (_, _) => await SwitchObsSceneAsync();
        StartObsStreamButton.Click += async (_, _) => await StartObsStreamAsync();
        StopObsStreamButton.Click += async (_, _) => await StopObsStreamAsync();

        _obsClient.ConnectionStateChanged += (_, connected) =>
        {
            Dispatcher.Invoke(() =>
            {
                ObsDashboardStatus.Text = connected ? "VERBUNDEN" : "NICHT VERBUNDEN";
                ObsDashboardLamp.Fill = connected
                    ? System.Windows.Media.Brushes.LimeGreen
                    : System.Windows.Media.Brushes.IndianRed;
                ObsConnectionStatusText.Text = connected
                    ? "Verbunden"
                    : "Nicht verbunden";
                ObsConnectionStatusText.Foreground = connected
                    ? System.Windows.Media.Brushes.LightGreen
                    : System.Windows.Media.Brushes.Gray;
                ServicesObsStatusText.Text = ObsConnectionStatusText.Text;
                ServicesObsStatusText.Foreground = ObsConnectionStatusText.Foreground;
            });
        };

        _obsClient.SceneCollectionChanged += (_, _) => _ = Dispatcher.InvokeAsync(RefreshObsAsync);
        _obsClient.SceneItemsChanged += (_, _) => _ = Dispatcher.InvokeAsync(RefreshServicesObsSceneItemsAsync);
        _obsClient.InputsChanged += (_, _) => _ = Dispatcher.InvokeAsync(async () =>
        {
            await RefreshObsAsync();
            await RefreshSelectedObsInputStateAsync();
        });
        _obsClient.InputVolumeMeters += (_, meters) => _ = Dispatcher.InvokeAsync(() => UpdateObsLiveMeters(meters));

        _obsClient.CurrentProgramSceneChanged += (_, sceneName) =>
        {
            Dispatcher.Invoke(() =>
            {
                ObsConnectionStatusText.Text =
                    "Verbunden · Szene: " + sceneName;
                DashboardCurrentSceneText.Text = sceneName;
                _servicesObsCurrentScene = sceneName;
                ServicesObsCurrentSceneText.Text = "Aktuelle Szene: " + sceneName;
                _automationCurrentScene = sceneName;
                _automationSceneActivatedAt = DateTimeOffset.UtcNow;
                foreach (var sceneRule in _settings.Workflow.TimedAutomations
                             .Where(rule => string.Equals(rule.TriggerType, "SceneElapsed", StringComparison.OrdinalIgnoreCase)
                                            && string.Equals(rule.TriggerScene, sceneName, StringComparison.OrdinalIgnoreCase)))
                {
                    _executedTimedAutomationRuleIds.Remove(sceneRule.Id);
                }
                CancelPendingSceneAutomationExecutions();
            });
            _ = _creatorIntelligence.RecordAsync("obs.scene.changed", new { scene = sceneName, viewers = _currentLiveViewerCount });
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await HandleStartToGameSpotifyVolumeAsync(sceneName);
                await ExecuteSpotifySceneAutomationAsync(sceneName);
                await RefreshDashboardObsScenePreviewAsync(sceneName);
            });
        };

        AuthorizeTwitchButton.Click += async (_, _) =>
            await AuthorizeTwitchAsync();

        ConnectTwitchButton.Click += async (_, _) =>
            await ConnectTwitchAsync();

        DisconnectTwitchButton.Click += async (_, _) =>
            await DisconnectTwitchAsync();

        SearchTwitchCategoryButton.Click += async (_, _) =>
            await SearchTwitchCategoriesAsync();

        SaveTwitchChannelButton.Click += async (_, _) =>
            await SaveTwitchChannelAsync();
        DashboardSearchTwitchCategoryButton.Click += async (_, _) => await SearchTwitchCategoriesAsync(DashboardTwitchCategorySearchBox, DashboardTwitchCategoryResultsBox);
        DashboardSaveTwitchChannelButton.Click += async (_, _) => await SaveTwitchChannelAsync(DashboardTwitchTitleBox, DashboardTwitchCategoryResultsBox);
        ServicesSearchTwitchCategoryButton.Click += async (_, _) => await SearchTwitchCategoriesAsync(ServicesTwitchCategorySearchBox, ServicesTwitchCategoryResultsBox);
        ServicesSaveTwitchChannelButton.Click += async (_, _) => await SaveTwitchChannelAsync(ServicesTwitchTitleBox, ServicesTwitchCategoryResultsBox);
        ServicesCreateRewardButton.Click += async (_, _) => await CreateTwitchRewardAsync();
        ServicesRefreshRewardsButton.Click += async (_, _) => await RefreshTwitchRewardsAsync();
        ServicesCreatePollButton.Click += async (_, _) => await CreateTwitchPollAsync();
        ServicesCreatePredictionButton.Click += async (_, _) => await CreateTwitchPredictionAsync();
        ServicesEndPollButton.Click += async (_, _) => await EndTwitchPollAsync("TERMINATED");
        ServicesArchivePollButton.Click += async (_, _) => await EndTwitchPollAsync("ARCHIVED");
        ServicesLockPredictionButton.Click += async (_, _) => await EndTwitchPredictionAsync("LOCKED");
        ServicesCancelPredictionButton.Click += async (_, _) => await EndTwitchPredictionAsync("CANCELED");
        ServicesResolvePredictionButton.Click += async (_, _) => await ResolveTwitchPredictionAsync();
        ServicesRefreshRedemptionsButton.Click += async (_, _) => await RefreshTwitchRedemptionsAsync();
        ServicesFulfillRedemptionButton.Click += async (_, _) => await UpdateSelectedTwitchRedemptionAsync("FULFILLED");
        ServicesCancelRedemptionButton.Click += async (_, _) => await UpdateSelectedTwitchRedemptionAsync("CANCELED");


        SendTwitchChatButton.Click += async (_, _) =>
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

                var role =
                    GetTwitchRoleLabel(
                        message);

                var chatLine =
                    $"{message.ReceivedAt:HH:mm:ss} · {role}{message.ChatterName}: {message.MessageText}";

                AddLimitedItem(
                    _twitchChatItems,
                    chatLine,
                    500);

                ScrollTwitchChatToLatest();

                UpdateDashboardTwitchUser(
                    message,
                    role);
            });

            await _workflowModule.Service.RegisterChatMessageAsync();
        };

        _twitchModule.EventReceived += async (_, twitchEvent) =>
        {
            Dispatcher.Invoke(() =>
            {
                _twitchSessionEvents++;
                _twitchSessionObservedAt ??= DateTimeOffset.Now;
                RefreshTwitchProfessionalUi();
                _ = _creatorIntelligence.RecordAsync("twitch.event", new { type = twitchEvent.Type, summary = twitchEvent.Summary, viewers = _currentLiveViewerCount, scene = _servicesObsCurrentScene });
                if (twitchEvent.Type == "channel.follow") _ = _creatorIntelligence.RecordAsync("twitch.follow", new { twitchEvent.Summary });
                AddLimitedItem(
                    _twitchEventItems,
                    $"{twitchEvent.ReceivedAt:HH:mm:ss} · " +
                    twitchEvent.Summary,
                    200);
            });

            if (twitchEvent.Type == "channel.follow")
            {
                await RefreshTwitchFollowerCountAsync();
            await RefreshTwitchGoalsAsync();
            }

            var alertType = twitchEvent.Type switch
            {
                "channel.follow" => "Follow",
                "channel.subscribe" => "Sub",
                "channel.subscription.message" => "ReSub",
                "channel.subscription.gift" => "GiftSub",
                "channel.cheer" => "Cheer",
                "channel.raid" => "Raid",
                _ => ""
            };

            var eventCount = GetTwitchEventCount(twitchEvent);
            await _workflowModule.Service.RegisterTwitchEventAsync(
                twitchEvent.Type,
                eventCount);

            if (!string.IsNullOrWhiteSpace(alertType))
            {
                // Streamer.bot spielt üblicherweise auf genau diese Twitch-Ereignisse
                // seine Alerts ab. Dadurch greift das Spotify-Ducking automatisch,
                // auch wenn Streamer.bot keine expliziten Start/Ende-Befehle sendet.
                _ = PulseExternalAlertAsync("streamerbot", $"{alertType}-{Guid.NewGuid():N}", TimeSpan.FromSeconds(10));

                var user = twitchEvent.Data.TryGetValue(
                    "user_name",
                    out var userName)
                    ? userName
                    : twitchEvent.Data.TryGetValue(
                        "from_broadcaster_user_name",
                        out var raidUser)
                        ? raidUser
                        : "Twitch";

                await _alertsModule.EnqueueAsync(
                    alertType,
                    user,
                    twitchEvent.Data);
            }

            RefreshWorkflowUi(_workflowModule.Service.State);
        };

        AuthorizeSpotifyButton.Click += async (_, _) =>
            await AuthorizeSpotifyAsync();

        ConnectSpotifyButton.Click += async (_, _) =>
            await ConnectSpotifyAsync();

        DisconnectSpotifyButton.Click += async (_, _) =>
            await DisconnectSpotifyAsync();

        RefreshSpotifyButton.Click += async (_, _) =>
            await RefreshSpotifyAsync();

        StartSpotifyPlaylistButton.Click += async (_, _) =>
            await StartSpotifyPlaylistAsync();

        SpotifyPlayButton.Click += async (_, _) =>
            await ExecuteSpotifyAsync(
                () => _spotifyModule.ResumeAsync());

        SpotifyPauseButton.Click += async (_, _) =>
            await ExecuteSpotifyAsync(
                () => _spotifyModule.PauseAsync());

        SpotifyPreviousButton.Click += async (_, _) =>
            await ExecuteSpotifyAsync(
                () => _spotifyModule.PreviousAsync());

        SpotifyNextButton.Click += async (_, _) =>
            await ExecuteSpotifyAsync(
                () => _spotifyModule.NextAsync());

        ServicesSpotifyLaunchButton.Click += (_, _) => LaunchConfiguredExecutable(_settings.Spotify.ExecutablePath, "Spotify");
        ServicesSpotifyPreviousButton.Click += async (_, _) =>
            await ExecuteSpotifyAsync(() => _spotifyModule.PreviousAsync());
        ServicesSpotifyPlayButton.Click += async (_, _) =>
            await ExecuteSpotifyAsync(() => _spotifyModule.ResumeAsync());
        ServicesSpotifyPauseButton.Click += async (_, _) =>
            await ExecuteSpotifyAsync(() => _spotifyModule.PauseAsync());
        ServicesSpotifyNextButton.Click += async (_, _) =>
            await ExecuteSpotifyAsync(() => _spotifyModule.NextAsync());
        ServicesSpotifyShuffleButton.Click += async (_, _) =>
            await ExecuteSpotifyAsync(async () =>
            {
                var enabled = !_spotifyModule.GetSnapshot().Playback.ShuffleEnabled;
                await _spotifyModule.SetShuffleAsync(enabled);
                await RefreshSpotifyAsync();
            });
        ServicesSpotifyRepeatButton.Click += async (_, _) =>
            await ExecuteSpotifyAsync(async () =>
            {
                var current = _spotifyModule.GetSnapshot().Playback.RepeatMode;
                var next = current?.ToLowerInvariant() switch
                {
                    "off" => "context",
                    "context" => "track",
                    _ => "off"
                };
                await _spotifyModule.SetRepeatAsync(next);
                await RefreshSpotifyAsync();
            });
        ServicesSpotifyRefreshButton.Click += async (_, _) =>
            await RefreshSpotifyAsync();
        ServicesSpotifyProgressBar.PreviewMouseLeftButtonUp += async (_, _) =>
        {
            if (_updatingSpotifyUi || !ServicesSpotifyProgressBar.IsEnabled) return;

            var targetMs = (int)Math.Round(ServicesSpotifyProgressBar.Value);
            ServicesSpotifyProgressBar.IsEnabled = false;
            try
            {
                await ExecuteSpotifyAsync(() => _spotifyModule.SeekAsync(targetMs));
                await RefreshSpotifyAsync();
            }
            finally
            {
                ServicesSpotifyProgressBar.IsEnabled = true;
            }
        };
        ServicesSpotifyRefreshQueueButton.Click += async (_, _) =>
            await ExecuteUiActionAsync(
                ServicesSpotifyRefreshQueueButton,
                "Spotify-Warteschlange aktualisieren",
                async () =>
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.RefreshQueueAsync());
                    RefreshSpotifyUi();
                });
        ServicesSpotifyPlayQueueItemButton.Click += async (_, _) =>
        {
            if (ServicesSpotifyQueueList.SelectedItem is not SpotifyQueueItem selected)
            {
                ServicesSpotifyQueueStatusText.Text = "Bitte zuerst einen Titel aus der Warteschlange auswählen.";
                return;
            }

            await ExecuteUiActionAsync(
                ServicesSpotifyPlayQueueItemButton,
                "Spotify-Warteschlangentitel abspielen",
                async () =>
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.PlayTrackAsync(selected.Track));
                    await _spotifyModule.RefreshQueueAsync();
                    await _spotifyModule.RefreshRecentlyPlayedAsync();
                    ServicesSpotifyQueueStatusText.Text =
                        $"Wiedergabe gestartet: {selected.Track.Artist} – {selected.Track.Name}";
                    RefreshSpotifyUi();
                });
        };
        ServicesSpotifySkipCurrentButton.Click += async (_, _) =>
            await ExecuteUiActionAsync(
                ServicesSpotifySkipCurrentButton,
                "Spotify-Titel überspringen",
                async () =>
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.NextAsync());
                    await Task.Delay(350);
                    await _spotifyModule.RefreshQueueAsync();
                    await _spotifyModule.RefreshRecentlyPlayedAsync();
                    ServicesSpotifyQueueStatusText.Text = "Der aktuelle Titel wurde übersprungen.";
                    RefreshSpotifyUi();
                });
        ServicesSpotifyTrackSearchButton.Click += async (_, _) =>
            await SearchSpotifyTracksAsync();
        ServicesSpotifyTrackSearchBox.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                await SearchSpotifyTracksAsync();
            }
        };
        ServicesSpotifyPlaylistFilterBox.TextChanged += (_, _) =>
            ApplySpotifyPlaylistFilter();
        ServicesSpotifyLoadPlaylistTracksButton.Click += async (_, _) =>
            await LoadSelectedSpotifyPlaylistTracksAsync();
        ServicesSpotifyToggleFavoritePlaylistButton.Click += async (_, _) =>
            await ToggleSelectedSpotifyPlaylistFavoriteAsync();
        ServicesSpotifyStartQuickPlaylistButton.Click += async (_, _) =>
            await StartSpotifyQuickPlaylistAsync(ServicesSpotifyQuickPlaylistBox.SelectedItem as SpotifyPlaylist);
        DashboardSpotifyStartQuickPlaylistButton.Click += async (_, _) =>
            await StartSpotifyQuickPlaylistAsync(DashboardSpotifyQuickPlaylistBox.SelectedItem as SpotifyPlaylist);
        ServicesSpotifyPlaylistBox.SelectionChanged += (_, _) =>
            UpdateSpotifyFavoriteButton();
        ServicesSpotifyPlayPlaylistTrackButton.Click += async (_, _) =>
            await ExecuteSelectedSpotifyPlaylistTrackAsync(playImmediately: true);
        ServicesSpotifyQueuePlaylistTrackButton.Click += async (_, _) =>
            await ExecuteSelectedSpotifyPlaylistTrackAsync(playImmediately: false);

        ServicesSpotifyPlaySelectedSearchResultButton.Click += async (_, _) =>
        {
            if (ServicesSpotifyTrackSearchResultsList.SelectedItem is not SpotifyTrackSearchItem selected)
            {
                ServicesSpotifyTrackSearchStatusText.Text = "Bitte zuerst einen Titel auswählen.";
                return;
            }

            await ExecuteUiActionAsync(
                ServicesSpotifyPlaySelectedSearchResultButton,
                "Spotify-Titel abspielen",
                async () =>
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.PlayTrackAsync(selected.Track));
                    ServicesSpotifyTrackSearchStatusText.Text =
                        $"Wiedergabe gestartet: {selected.Track.Artist} – {selected.Track.Name}";
                    await RefreshSpotifyAsync();
                });
        };

        ServicesSpotifyAddSelectedToQueueButton.Click += async (_, _) =>
        {
            if (ServicesSpotifyTrackSearchResultsList.SelectedItem is not SpotifyTrackSearchItem selected)
            {
                ServicesSpotifyTrackSearchStatusText.Text = "Bitte zuerst einen Titel auswählen.";
                return;
            }

            await ExecuteUiActionAsync(
                ServicesSpotifyAddSelectedToQueueButton,
                "Spotify-Titel zur Warteschlange hinzufügen",
                async () =>
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.AddToQueueAsync(selected.Track));
                    await _spotifyModule.RefreshQueueAsync();
                    ServicesSpotifyTrackSearchStatusText.Text =
                        $"Hinzugefügt: {selected.Track.Artist} – {selected.Track.Name}";
                    RefreshSpotifyUi();
                });
        };
        ServicesSpotifyRefreshHistoryButton.Click += async (_, _) =>
            await ExecuteUiActionAsync(
                ServicesSpotifyRefreshHistoryButton,
                "Spotify-Verlauf aktualisieren",
                async () =>
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.RefreshRecentlyPlayedAsync());
                    RefreshSpotifyUi();
                    ServicesSpotifyHistoryStatusText.Text = "Verlauf aktualisiert.";
                });
        ServicesSpotifyPlayHistoryButton.Click += async (_, _) =>
        {
            if (ServicesSpotifyHistoryList.SelectedItem is not SpotifyHistoryItem selected)
            {
                ServicesSpotifyHistoryStatusText.Text = "Bitte zuerst einen Titel auswählen.";
                return;
            }

            await ExecuteUiActionAsync(
                ServicesSpotifyPlayHistoryButton,
                "Spotify-Titel abspielen",
                async () =>
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.PlayTrackAsync(selected.Item.Track));
                    ServicesSpotifyHistoryStatusText.Text =
                        $"Wird abgespielt: {selected.Item.Track.Artist} – {selected.Item.Track.Name}";
                    RefreshSpotifyUi();
                });
        };
        ServicesSpotifyQueueHistoryButton.Click += async (_, _) =>
        {
            if (ServicesSpotifyHistoryList.SelectedItem is not SpotifyHistoryItem selected)
            {
                ServicesSpotifyHistoryStatusText.Text = "Bitte zuerst einen Titel auswählen.";
                return;
            }

            await ExecuteUiActionAsync(
                ServicesSpotifyQueueHistoryButton,
                "Spotify-Titel zur Warteschlange hinzufügen",
                async () =>
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.AddToQueueAsync(selected.Item.Track));
                    await _spotifyModule.RefreshQueueAsync();
                    ServicesSpotifyHistoryStatusText.Text =
                        $"Hinzugefügt: {selected.Item.Track.Artist} – {selected.Item.Track.Name}";
                    RefreshSpotifyUi();
                });
        };
        ServicesSpotifyStartPlaylistBox.SelectionChanged += async (_, _) =>
        {
            if (ServicesSpotifyStartPlaylistBox.SelectedItem is SpotifyPlaylist playlist)
            {
                _settings.Spotify.StartPlaylistUri = playlist.Uri;
                await _settingsStore.SaveAsync(_settings);
            }
        };
        ServicesSpotifyResetStatisticsButton.Click += (_, _) =>
        {
            if (MessageBox.Show("Spotify-Statistik wirklich zurücksetzen?", "Spotify-Statistik", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _spotifyListeningStatistics.Reset();
            RefreshSpotifyStatisticsUi();
        };

        ServicesSpotifyRefreshSavedTracksButton.Click += async (_, _) =>
            await ExecuteUiActionAsync(
                ServicesSpotifyRefreshSavedTracksButton,
                "Spotify-Favoriten aktualisieren",
                async () =>
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.RefreshSavedTracksAsync());
                    RefreshSpotifyUi();
                    ServicesSpotifySavedTracksStatusText.Text = "Gespeicherte Titel aktualisiert.";
                });
        ServicesSpotifyPlaySavedTrackButton.Click += async (_, _) =>
        {
            if (ServicesSpotifySavedTracksList.SelectedItem is not SpotifySavedTrackItem selected)
            {
                ServicesSpotifySavedTracksStatusText.Text = "Bitte zuerst einen gespeicherten Titel auswählen.";
                return;
            }

            await ExecuteUiActionAsync(
                ServicesSpotifyPlaySavedTrackButton,
                "Gespeicherten Spotify-Titel abspielen",
                async () =>
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.PlayTrackAsync(selected.Track));
                    ServicesSpotifySavedTracksStatusText.Text =
                        $"Wird abgespielt: {selected.Track.Artist} – {selected.Track.Name}";
                    await RefreshSpotifyAsync();
                });
        };
        ServicesSpotifyRemoveSavedTrackButton.Click += async (_, _) =>
        {
            if (ServicesSpotifySavedTracksList.SelectedItem is not SpotifySavedTrackItem selected)
            {
                ServicesSpotifySavedTracksStatusText.Text = "Bitte zuerst einen gespeicherten Titel auswählen.";
                return;
            }

            await ExecuteUiActionAsync(
                ServicesSpotifyRemoveSavedTrackButton,
                "Spotify-Titel aus Favoriten entfernen",
                async () =>
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.SetTrackSavedAsync(selected.Track, false));
                    ServicesSpotifySavedTracksStatusText.Text =
                        $"Aus Favoriten entfernt: {selected.Track.Artist} – {selected.Track.Name}";
                    RefreshSpotifyUi();
                });
        };
        ServicesSpotifyToggleCurrentSavedButton.Click += async (_, _) =>
        {
            var track = _spotifyModule.GetSnapshot().Playback.Track;
            if (track is null)
            {
                ServicesSpotifySavedTracksStatusText.Text = "Aktuell läuft kein Spotify-Titel.";
                return;
            }

            await ExecuteUiActionAsync(
                ServicesSpotifyToggleCurrentSavedButton,
                "Spotify-Gefällt-mir-Status ändern",
                async () =>
                {
                    var isSaved = await _spotifyModule.IsTrackSavedAsync(track);
                    await _spotifyModule.SetTrackSavedAsync(track, !isSaved);
                    ServicesSpotifySavedTracksStatusText.Text = !isSaved
                        ? $"Zu Favoriten hinzugefügt: {track.Artist} – {track.Name}"
                        : $"Aus Favoriten entfernt: {track.Artist} – {track.Name}";
                    RefreshSpotifyUi();
                });
        };

        ServicesSpotifyStartPlaylistButton.Click += async (_, _) =>
        {
            if (ServicesSpotifyPlaylistBox.SelectedItem is SpotifyPlaylist playlist)
            {
                await StartSpotifyPlaylistAndRememberAsync(playlist);
            }
        };
        DashboardSpotifyStartPlaylistButton.Click += async (_, _) =>
        {
            if (DashboardSpotifyPlaylistBox.SelectedItem is SpotifyPlaylist playlist)
            {
                await StartSpotifyPlaylistAndRememberAsync(playlist);
            }
        };
        ServicesSpotifyDeviceBox.SelectionChanged += (_, _) =>
            UpdateSpotifyDeviceSelectionUi();
        ServicesSpotifyRefreshDevicesButton.Click += async (_, _) =>
            await ExecuteUiActionAsync(
                ServicesSpotifyRefreshDevicesButton,
                "Spotify-Geräte aktualisieren",
                async () =>
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.RefreshDevicesAsync());
                    RefreshSpotifyUi();
                });
        ServicesSpotifyTransferDeviceButton.Click += async (_, _) =>
            await TransferSelectedSpotifyDeviceAsync(play: false);
        ServicesSpotifyTransferAndPlayDeviceButton.Click += async (_, _) =>
            await TransferSelectedSpotifyDeviceAsync(play: true);
        ServicesSpotifySetPreferredDeviceButton.Click += async (_, _) =>
            await SaveSelectedSpotifyDeviceAsync();
        ServicesSpotifyActivatePreferredDeviceButton.Click += async (_, _) =>
            await ActivatePreferredSpotifyDeviceAsync();
        ServicesSpotifyAutoTransferPreferredBox.Checked += async (_, _) => await SaveSpotifyDeviceBehaviorAsync();
        ServicesSpotifyAutoTransferPreferredBox.Unchecked += async (_, _) => await SaveSpotifyDeviceBehaviorAsync();
        ServicesSpotifyUseActiveFallbackBox.Checked += async (_, _) => await SaveSpotifyDeviceBehaviorAsync();
        ServicesSpotifyUseActiveFallbackBox.Unchecked += async (_, _) => await SaveSpotifyDeviceBehaviorAsync();
        ServicesSpotifySmartAutomationBox.Checked += async (_, _) => await SaveSpotifySmartAutomationSettingsAsync();
        ServicesSpotifySmartAutomationBox.Unchecked += async (_, _) => await SaveSpotifySmartAutomationSettingsAsync();
        ServicesSpotifyHealthMonitorBox.Checked += async (_, _) => await SaveSpotifySmartAutomationSettingsAsync();
        ServicesSpotifyHealthMonitorBox.Unchecked += async (_, _) => await SaveSpotifySmartAutomationSettingsAsync();
        ServicesSpotifyAutoRecoverBox.Checked += async (_, _) => await SaveSpotifySmartAutomationSettingsAsync();
        ServicesSpotifyAutoRecoverBox.Unchecked += async (_, _) => await SaveSpotifySmartAutomationSettingsAsync();
        ServicesSpotifyCreateDefaultRulesButton.Click += async (_, _) => await CreateDefaultSpotifyAutomationRulesAsync();
        ServicesSpotifyTestAutomationButton.Click += async (_, _) => await ExecuteSpotifySceneAutomationAsync(_automationCurrentScene, force: true);
        ServicesSpotifyClearAutomationLogButton.Click += (_, _) => { _spotifyAutomationLog.Clear(); RefreshSpotifyAutomationLogUi(); };

        ServicesTwitchDashboardButton.Click += (_, _) => OpenConfiguredTarget(_settings.Twitch.CreatorDashboardUrl, "Twitch Creator Dashboard");
        ServicesTwitchConnectButton.Click += async (_, _) => await ConnectTwitchAsync();
        ServicesTwitchDisconnectButton.Click += async (_, _) => await DisconnectTwitchAsync();

        ServicesObsLaunchButton.Click += (_, _) => LaunchConfiguredExecutable(_settings.Obs.ExecutablePath, "OBS");
        ServicesObsConnectButton.Click += async (_, _) => await ConnectObsAsync();
        ServicesObsDisconnectButton.Click += async (_, _) => await DisconnectObsAsync();
        ServicesObsRefreshButton.Click += async (_, _) => await RefreshObsAsync();
        ServicesObsApplyTransitionButton.Click += async (_, _) => await ApplySelectedObsTransitionAsync();
        ServicesObsStartStreamButton.Click += async (_, _) => await StartObsStreamAsync();
        ServicesObsStopStreamButton.Click += async (_, _) => await StopObsStreamAsync();
        ServicesObsControlStartStreamButton.Click += async (_, _) => await ExecuteObsControlAsync("Stream starten", () => _obsClient.StartStreamAsync());
        ServicesObsControlStopStreamButton.Click += async (_, _) => await ExecuteObsControlAsync("Stream stoppen", () => _obsClient.StopStreamAsync());
        ServicesObsStartRecordButton.Click += async (_, _) => await ExecuteObsControlAsync("Aufnahme starten", () => _obsClient.StartRecordAsync());
        ServicesObsPauseRecordButton.Click += async (_, _) => await ToggleObsRecordPauseAsync();
        ServicesObsStopRecordButton.Click += async (_, _) => await ExecuteObsControlAsync("Aufnahme stoppen", () => _obsClient.StopRecordAsync());
        ServicesObsStartReplayButton.Click += async (_, _) => await ExecuteObsControlAsync("Replay Buffer starten", () => _obsClient.StartReplayBufferAsync());
        ServicesObsSaveReplayButton.Click += async (_, _) => await ExecuteObsControlAsync("Replay speichern", () => _obsClient.SaveReplayBufferAsync());
        ServicesObsStopReplayButton.Click += async (_, _) => await ExecuteObsControlAsync("Replay Buffer stoppen", () => _obsClient.StopReplayBufferAsync());
        ServicesObsStartVirtualCamButton.Click += async (_, _) => await ExecuteObsControlAsync("Virtuelle Kamera starten", () => _obsClient.StartVirtualCamAsync());
        ServicesObsStopVirtualCamButton.Click += async (_, _) => await ExecuteObsControlAsync("Virtuelle Kamera stoppen", () => _obsClient.StopVirtualCamAsync());
        ServicesObsSwitchSceneButton.Click += async (_, _) => await SwitchServicesObsSceneAsync();
        ServicesObsScenesList.MouseDoubleClick += async (_, _) => await SwitchServicesObsSceneAsync();
        ServicesObsSceneSearchBox.TextChanged += (_, _) => ApplyServicesObsSceneFilter();
        ServicesObsSourceSearchBox.TextChanged += (_, _) => ApplyServicesObsSourceFilter();
        ServicesObsInputSearchBox.TextChanged += (_, _) => ApplyServicesObsInputFilter();
        ServicesObsInputFilterBox.SelectionChanged += (_, _) => ApplyServicesObsInputFilter();
        ServicesObsScenesList.SelectionChanged += async (_, _) => await RefreshServicesObsSceneItemsAsync();
        ServicesObsSceneItemsList.SelectionChanged += async (_, _) => await RefreshSelectedObsSceneItemStateAsync();
        ServicesObsShowSceneItemButton.Click += async (_, _) => await SetSelectedObsSceneItemVisibilityAsync(true);
        ServicesObsHideSceneItemButton.Click += async (_, _) => await SetSelectedObsSceneItemVisibilityAsync(false);
        ServicesObsLockSceneItemButton.Click += async (_, _) => await SetSelectedObsSceneItemLockAsync(true);
        ServicesObsUnlockSceneItemButton.Click += async (_, _) => await SetSelectedObsSceneItemLockAsync(false);
        ServicesObsMoveSceneItemUpButton.Click += async (_, _) => await MoveSelectedObsSceneItemAsync(1);
        ServicesObsMoveSceneItemDownButton.Click += async (_, _) => await MoveSelectedObsSceneItemAsync(-1);
        ServicesObsRefreshSceneItemsButton.Click += async (_, _) => await RefreshServicesObsSceneItemsAsync();
        ServicesObsRestartMediaButton.Click += async (_, _) => await RestartSelectedObsMediaInputAsync();
        ServicesObsStopMediaButton.Click += async (_, _) => await StopSelectedObsMediaInputAsync();
        ServicesObsRefreshBrowserButton.Click += async (_, _) => await RefreshSelectedObsBrowserInputAsync();
        ServicesObsApplySceneItemTransformButton.Click += async (_, _) => await ApplySelectedObsSceneItemTransformAsync();
        ServicesObsReloadSceneItemTransformButton.Click += async (_, _) => await LoadSelectedObsSceneItemTransformAsync();
        ServicesObsResetSceneItemTransformButton.Click += async (_, _) => await ResetSelectedObsSceneItemTransformAsync();
        ServicesObsSceneItemFullscreenButton.Click += async (_, _) => await ApplyObsSceneItemTransformPresetAsync(0, 0, 1920, 1080);
        ServicesObsSceneItemCentered720Button.Click += async (_, _) => await ApplyObsSceneItemTransformPresetAsync(320, 180, 1280, 720);
        ServicesObsSourceFiltersList.SelectionChanged += (_, _) => RefreshSelectedObsSourceFilterState();
        ServicesObsEnableSourceFilterButton.Click += async (_, _) => await SetSelectedObsSourceFilterEnabledAsync(true);
        ServicesObsDisableSourceFilterButton.Click += async (_, _) => await SetSelectedObsSourceFilterEnabledAsync(false);
        ServicesObsRefreshSourceFiltersButton.Click += async (_, _) => await RefreshSelectedObsSourceFiltersAsync();
        ServicesObsMuteInputButton.Click += async (_, _) => await SetSelectedObsInputMuteAsync(true);
        ServicesObsUnmuteInputButton.Click += async (_, _) => await SetSelectedObsInputMuteAsync(false);
        ServicesObsSetVolumeButton.Click += async (_, _) => await SetSelectedObsInputVolumeAsync();
        ServicesObsInputsList.SelectionChanged += async (_, _) => await RefreshSelectedObsInputStateAsync();
        ServicesObsRefreshInputStateButton.Click += async (_, _) => await RefreshSelectedObsInputStateAsync();
        ServicesObsVolumeSlider.ValueChanged += (_, _) =>
        {
            if (!_updatingObsMixerVolumeUi)
            {
                ServicesObsVolumeDbBox.Text = ServicesObsVolumeSlider.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                ServicesObsVolumePercentText.Text = $"{DbToPercent(ServicesObsVolumeSlider.Value):0} % · -60 dB = sehr leise · 0 dB = Standard · +10 dB = Verstärkung";
            }
        };
        ServicesObsVolumeMinus20Button.Click += async (_, _) => await ApplyObsMixerPresetAsync(-20);
        ServicesObsVolumeMinus10Button.Click += async (_, _) => await ApplyObsMixerPresetAsync(-10);
        ServicesObsVolumeZeroButton.Click += async (_, _) => await ApplyObsMixerPresetAsync(0);
        ServicesObsMuteAllButton.Click += async (_, _) => await SetObsInputsMuteAsync(_servicesObsInputs, true, "Alle Audioquellen");
        ServicesObsUnmuteAllButton.Click += async (_, _) => await SetObsInputsMuteAsync(_servicesObsInputs, false, "Alle Audioquellen");
        ServicesObsOnlyMicButton.Click += async (_, _) => await SoloObsAudioCategoryAsync("microphone");
        ServicesObsOnlyGameButton.Click += async (_, _) => await SoloObsAudioCategoryAsync("game");
        ServicesObsApplyGroupVolumeButton.Click += async (_, _) => await ApplyObsAudioGroupVolumeAsync();
        ServicesObsMuteGroupButton.Click += async (_, _) => await SetSelectedObsAudioGroupMuteAsync(true);
        ServicesObsUnmuteGroupButton.Click += async (_, _) => await SetSelectedObsAudioGroupMuteAsync(false);
        ServicesObsApplyAdvancedAudioButton.Click += async (_, _) => await ApplySelectedObsAdvancedAudioAsync();
        ServicesObsSaveAudioProfileButton.Click += async (_, _) => await SaveObsAudioProfileAsync();
        ServicesObsApplyAudioProfileButton.Click += async (_, _) => await ApplySelectedObsAudioProfileAsync();
        ServicesObsDeleteAudioProfileButton.Click += async (_, _) => await DeleteSelectedObsAudioProfileAsync();
        ServicesObsAudioProfileBox.SelectionChanged += (_, _) =>
        {
            if (ServicesObsAudioProfileBox.SelectedItem is ObsAudioProfileSettings profile)
            {
                ServicesObsAudioProfileNameBox.Text = profile.Name;
                ServicesObsAudioProfileStateText.Text = $"{profile.Inputs.Count} Audioquellen gespeichert.";
            }
        };
        ServicesObsAutomationSceneBox.SelectionChanged += async (_, _) => await RefreshSimpleObsAutomationSourcesAsync();
        ServicesObsAutomationAddButton.Click += async (_, _) => await AddSimpleObsAutomationRuleAsync();
        ServicesObsAutomationDeleteButton.Click += async (_, _) => await DeleteSimpleObsAutomationRuleAsync();
        ServicesObsAutomationTestButton.Click += async (_, _) => await TestSimpleObsAutomationRuleAsync();
        ServicesSpotifySaveOverlayButton.Click += async (_, _) => await SaveSpotifyOverlaySettingsAsync();
        ServicesSpotifyHideMutedBox.Checked += async (_, _) =>
        {
            await SaveSpotifyDisplayOptionsImmediatelyAsync();
            _lastSpotifyOverlayMuted = null;
            await SynchronizeSpotifyOverlayVisibilityAsync(_spotifyModule.GetSnapshot().Playback);
        };
        ServicesSpotifyHideMutedBox.Unchecked += async (_, _) =>
        {
            await SaveSpotifyDisplayOptionsImmediatelyAsync();
            _lastSpotifyOverlayMuted = null;
            await ApplySpotifyOverlayMuteStateAsync(false);
        };
        ServicesSpotifyDetectObsMuteBox.Checked += async (_, _) => await SaveSpotifyDisplayOptionsImmediatelyAsync();
        ServicesSpotifyDetectObsMuteBox.Unchecked += async (_, _) => await SaveSpotifyDisplayOptionsImmediatelyAsync();
        ServicesSpotifyDetectVolumeMuteBox.Checked += async (_, _) => await SaveSpotifyDisplayOptionsImmediatelyAsync();
        ServicesSpotifyDetectVolumeMuteBox.Unchecked += async (_, _) => await SaveSpotifyDisplayOptionsImmediatelyAsync();
        ServicesSpotifyHidePausedBox.Checked += async (_, _) => await SaveSpotifyDisplayOptionsImmediatelyAsync();
        ServicesSpotifyHidePausedBox.Unchecked += async (_, _) => await SaveSpotifyDisplayOptionsImmediatelyAsync();
        ServicesSpotifyObsAudioSourceBox.LostFocus += async (_, _) => await SaveSpotifyDisplayOptionsImmediatelyAsync();
        ServicesSpotifyBrowseDataJsonButton.Click += (_, _) => BrowseSpotifyDataJsonPath();
        ServicesSpotifyOverlayProjectBox.SelectionChanged += (_, _) => RefreshSpotifyOverlayProjectItems();
        ServicesSpotifyOverlaySceneBox.SelectionChanged += async (_, _) => await RefreshSpotifyOverlayBrowserSourcesAsync();
        ServicesSpotifyOverlayItemBox.SelectionChanged += (_, _) => RefreshSpotifyOverlaySelectionDetails();
        ServicesSpotifySyncOverlayButton.Click += async (_, _) => await WriteSpotifyDataJsonNowAsync();
        ServicesSpotifyReloadOverlayButton.Click += (_, _) => OpenSpotifyDataJsonFolder();
        ServicesSpotifyPreviewOverlayButton.Click += (_, _) => OpenSpotifyDataJsonFile();
        ServicesSpotifySaveAutomationButton.Click += async (_, _) => await SaveSpotifyAutomationSettingsAsync();
        ServicesSpotifyAutoStartOnStreamBox.Checked += async (_, _) => await SaveSpotifyAutomationSettingsAsync();
        ServicesSpotifyAutoStartOnStreamBox.Unchecked += async (_, _) => await SaveSpotifyAutomationSettingsAsync();
        ServicesSpotifyEndMusicBox.Checked += async (_, _) => await SaveSpotifyAutomationSettingsAsync();
        ServicesSpotifyEndMusicBox.Unchecked += async (_, _) => await SaveSpotifyAutomationSettingsAsync();
        ServicesSpotifyPauseOnStreamEndBox.Checked += async (_, _) => await SaveSpotifyAutomationSettingsAsync();
        ServicesSpotifyPauseOnStreamEndBox.Unchecked += async (_, _) => await SaveSpotifyAutomationSettingsAsync();
        ServicesSpotifySetLiveVolumeBox.Checked += async (_, _) => await SaveSpotifyAutomationSettingsAsync();
        ServicesSpotifySetLiveVolumeBox.Unchecked += async (_, _) => await SaveSpotifyAutomationSettingsAsync();
        ServicesSpotifyLiveVolumeBox.LostFocus += async (_, _) => await SaveSpotifyAutomationSettingsAsync();
        ServicesSpotifyMuteDuringAlertsBox.Checked += async (_, _) => await SaveSpotifyAutomationSettingsAsync();
        ServicesSpotifyMuteDuringAlertsBox.Unchecked += async (_, _) => await SaveSpotifyAutomationSettingsAsync();
        ServicesSpotifyAlertVolumeBox.LostFocus += async (_, _) => await SaveSpotifyAutomationSettingsAsync();
        ServicesSpotifyAlertFadeOutMsBox.LostFocus += async (_, _) => await SaveSpotifyAutomationSettingsAsync();
        ServicesSpotifyAlertFadeInMsBox.LostFocus += async (_, _) => await SaveSpotifyAutomationSettingsAsync();
        ServicesSpotifyVolumeSlider.ValueChanged += async (_, _) =>
        {
            var volume = (int)Math.Round(ServicesSpotifyVolumeSlider.Value);
            ServicesSpotifyVolumeText.Text = $"{volume} %";

            if (!_updatingSpotifyUi)
            {
                // ValueChanged fires continuously while the thumb is dragged.
                // A short debounce prevents API flooding while keeping the response live.
                await QueueSpotifyVolumeUpdateAsync(60, volume);
            }
        };
        ServicesTwitchSaveEndSettingsButton.Click += async (_, _) => await SaveTwitchEndSettingsAsync();
        SaveTwitchGoalsButton.Click += async (_, _) => await SaveTwitchGoalsAsync();
        AddFollowerGoalToObsButton.Click += async (_, _) => await InstallGoalInObsAsync("follower");
        InstallAllGoalsSceneButton.Click += async (_, _) => await InstallAllGoalsSceneInObsAsync();
        AddSubGoalToObsButton.Click += async (_, _) => await InstallGoalInObsAsync("sub");
        AddDonationGoalToObsButton.Click += async (_, _) => await InstallGoalInObsAsync("donation");
        ServicesStreamerBotLaunchButton.Click += (_, _) => LaunchConfiguredExecutable(_settings.StreamerBot.ExecutablePath, "Streamer.bot");
        ServicesStreamerBotConnectButton.Click += async (_, _) => await ConnectStreamerBotAsync();
        ServicesStreamerBotDisconnectButton.Click += async (_, _) => await DisconnectStreamerBotAsync();
        ServicesStreamerBotDiagnoseButton.Click += async (_, _) => await DiagnoseStreamerBotAsync();
        ServicesStreamerBotReconnectButton.Click += async (_, _) => await ReconnectStreamerBotAsync();
        ServicesStreamerBotRefreshActionsButton.Click += async (_, _) => await RefreshStreamerBotActionsAsync(true);
        ServicesStreamerBotActionSearchBox.TextChanged += (_, _) => ApplyStreamerBotActionFilter();
        ServicesStreamerBotActionsList.SelectionChanged += (_, _) => UpdateSelectedStreamerBotAction();
        ServicesStreamerBotFormatArgumentsButton.Click += (_, _) => FormatStreamerBotArgumentsJson();
        ServicesStreamerBotHistoryList.ItemsSource = _streamerBotExecutionHistory;
        ServicesStreamerBotLiveEventsList.ItemsSource = _streamerBotLiveEvents;
        ServicesStreamerBotClearLiveEventsButton.Click += (_, _) =>
        {
            _streamerBotLiveEvents.Clear();
            ServicesStreamerBotLiveEventStatusText.Text = "Live-Ereignisse wurden geleert.";
        };
        ServicesStreamerBotRunActionButton.Click += async (_, _) => await RunSelectedStreamerBotActionAsync();
        ServicesStreamerBotFavoriteActionButton.Click += (_, _) => ToggleSelectedStreamerBotFavorite();
        ServicesStreamerBotTemplateBox.ItemsSource = _streamerBotActionTemplates;
        ServicesStreamerBotSaveTemplateButton.Click += (_, _) => SaveSelectedStreamerBotTemplate();
        ServicesStreamerBotLoadTemplateButton.Click += (_, _) => LoadSelectedStreamerBotTemplate();
        ServicesStreamerBotDeleteTemplateButton.Click += (_, _) => DeleteSelectedStreamerBotTemplate();
        ServicesStreamerBotScheduleActionButton.Click += async (_, _) => await ScheduleSelectedStreamerBotActionAsync();
        ServicesStreamerBotCancelScheduleButton.Click += (_, _) => CancelScheduledStreamerBotAction();
        ServicesStreamerBotExportHistoryButton.Click += (_, _) => ExportStreamerBotHistoryCsv();
        ServicesStreamerBotClearHistoryButton.Click += (_, _) =>
        {
            _streamerBotExecutionHistory.Clear();
            ServicesStreamerBotActionResultText.Text = "Ausführungshistorie wurde geleert.";
        };
        RefreshStreamerBotAlertActionsButton.Click += async (_, _) => await RefreshStreamerBotActionsAsync(true);
        DisableStreamerBotAlertsNowButton.Click += async (_, _) => await SetStreamerBotAlertsEnabledAsync(false);
        EnableStreamerBotAlertsNowButton.Click += async (_, _) => await SetStreamerBotAlertsEnabledAsync(true);
        SuppressStreamerBotAlertsBox.Checked += async (_, _) => await ApplyStreamerBotAlertSuppressionAsync();
        SuppressStreamerBotAlertsBox.Unchecked += async (_, _) => await ApplyStreamerBotAlertSuppressionAsync();
        BrowseObsExecutableButton.Click += (_, _) => BrowseExecutable(ObsExecutablePathBox, "OBS|obs64.exe;obs32.exe|Programme|*.exe");
        BrowseSpotifyExecutableButton.Click += (_, _) => BrowseExecutable(SpotifyExecutablePathBox, "Spotify|Spotify.exe|Programme|*.exe");
        OpenSpotifyFromSettingsButton.Click += (_, _) =>
            LaunchConfiguredExecutable(SpotifyExecutablePathBox.Text.Trim(), "Spotify");
        BrowseStreamerBotExecutableButton.Click += (_, _) => BrowseExecutable(StreamerBotExecutablePathBox, "Streamer.bot|Streamer.bot.exe|Programme|*.exe");
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
                StopAlertAudioPreview();
        };
        SpotifyVolumeSlider.ValueChanged += async (_, _) =>
            await QueueSpotifyVolumeUpdateAsync(40);

        SpotifyVolumeSlider.PreviewMouseMove += async (_, eventArgs) =>
        {
            if (eventArgs.LeftButton ==
                System.Windows.Input.MouseButtonState.Pressed)
            {
                await QueueSpotifyVolumeUpdateAsync(20);
            }
        };

        TestSpotifyFadeButton.Click += async (_, _) =>
            await TestSpotifyFadeAsync();

        SpotifyDeviceBox.SelectionChanged += (_, _) =>
        {
            if (_updatingSpotifyUi || SpotifyDeviceBox.SelectedItem is not SpotifyDevice device)
            {
                return;
            }

            _settings.Spotify.PreferredDeviceId = device.Id;
        };

        SpotifyPlaylistBox.SelectionChanged += (_, _) =>
        {
            if (SpotifyPlaylistBox.SelectedItem is SpotifyPlaylist playlist)
            {
                _settings.Spotify.StartPlaylistUri = playlist.Uri;
            }
        };

        AlertTypeBox.SelectionChanged += async (_, _) =>
        {
            SyncAlertLibrarySelection();
            await LoadSelectedAlertDefinitionAsync();
        };

        AlertLibraryList.SelectionChanged += async (_, _) =>
        {
            if (AlertLibraryList.SelectedItem is AlertLibraryItem item &&
                !Equals(AlertTypeBox.SelectedItem, item.Type))
            {
                AlertTypeBox.SelectedItem = item.Type;
                await LoadSelectedAlertDefinitionAsync();
            }
        };

        NewAlertDefinitionButton.Click += async (_, _) => await CreateAlertDefinitionAsync();
        DuplicateAlertDefinitionButton.Click += async (_, _) => await DuplicateAlertDefinitionAsync();
        ToggleAlertDefinitionButton.Click += async (_, _) => await ToggleAlertDefinitionAsync();
        DeleteAlertDefinitionButton.Click += async (_, _) => await DeleteAlertDefinitionAsync();

        SaveAlertDefinitionButton.Click += async (_, _) =>
            await SaveSelectedAlertDefinitionAsync();

        PreviewAlertButton.Click += async (_, _) =>
            await PreviewAlertAsync();

        TestAlertInObsButton.Click += async (_, _) =>
            await TestAlertInObsAsync();

        StopCurrentAlertButton.Click += async (_, _) =>
            await _alertsModule.StopCurrentAsync();

        ClearAlertQueueButton.Click += async (_, _) =>
            await _alertsModule.ClearQueueAsync();

        _alertsModule.StateChanged += async (_, state) =>
        {
            Dispatcher.Invoke(() =>
            {
                AlertQueueStatusText.Text = state.IsRunning
                    ? $"{state.Current?.Type} läuft · Queue: {state.QueueLength}"
                    : $"Bereit · Queue: {state.QueueLength}";

                AlertsDashboardStatus.Text = state.IsRunning
                    ? "AKTIV"
                    : "BEREIT";
            });

            var alertJustStarted = state.IsRunning && !_suiteAlertRunning;
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
            }
        };

        InstallOverlayButton.Click += async (_, _) =>
            await InstallOverlayAsync();

        BrowseOverlayFolderButton.Click += (_, _) => BrowseOverlayFolder();

        OverlayFrameColorPaletteBox.SelectionChanged += (_, _) =>
        {
            if (OverlayFrameColorPaletteBox.SelectedItem is ComboBoxItem selected && selected.Tag is string color)
            {
                OverlayFrameColorBox.Text = color;
                UpdateOverlayFrameColorPreview(color);
            }
        };
        OverlayFrameColorBox.TextChanged += (_, _) => UpdateOverlayFrameColorPreview(OverlayFrameColorBox.Text);

        OpenOverlayFolderButton.Click += async (_, _) =>
            await OpenOverlayFolderAsync();

        ValidateOverlayButton.Click += async (_, _) =>
            await ValidateOverlayAsync();

        InstallObsBrowserSourcesButton.Click += async (_, _) =>
            await InstallObsBrowserSourcesAsync();
        InstallSelectedOverlayContentButton.Click += async (_, _) =>
            await InstallSelectedOverlayContentAsync();

        OverlayProjectList.ItemsSource = _overlayProjects;
        OverlayProjectItemsList.ItemsSource = _overlayProjectItems;
        OverlayProjectList.SelectionChanged += (_, _) => RefreshSelectedOverlayProject();
        OverlayProjectItemsList.SelectionChanged += (_, _) => RefreshSelectedOverlayProjectItem();
        ImportOverlayProjectButton.Click += async (_, _) => await ImportOverlayProjectAsync();
        ImportManagedOverlayButton.Click += async (_, _) => await ImportManagedOverlayAsync();
        ImportOverlayFromObsButton.Click += async (_, _) => await ImportOverlayFromObsAsync();
        AddOverlaySceneButton.Click += async (_, _) => await AddOverlaySceneAsync();
        DeleteOverlayProjectButton.Click += async (_, _) => await DeleteOverlayProjectAsync();
        SaveOverlayMappingButton.Click += async (_, _) => await SaveOverlayProjectMappingAsync();
        SyncOverlayProjectButton.Click += async (_, _) => await SynchronizeOverlayProjectAsync();
        OpenOverlayProjectFolderButton.Click += (_, _) => OpenSelectedOverlayProjectFolder();

        DashboardPrepareStreamButton.Click += async (_, _) =>
            await ExecuteDashboardActionAsync(
                DashboardPrepareStreamButton,
                "Stream vorbereiten",
                PrepareStreamWithConfiguredServicesAsync);
        PrepareStreamButton.Click += async (_, _) => await PrepareStreamWithConfiguredServicesAsync();

        StartCountdownButton.Click += async (_, _) =>
            await ExecuteWorkflowAsync(() => _workflowModule.Service.StartCountdownAsync());

        GoLiveButton.Click += async (_, _) =>
            await ExecuteWorkflowAsync(() => _workflowModule.Service.GoLiveAsync());

        PauseStreamButton.Click += async (_, _) =>
            await ExecuteWorkflowAsync(() => _workflowModule.Service.PauseAsync());

        ResumeStreamButton.Click += async (_, _) =>
            await ExecuteWorkflowAsync(() => _workflowModule.Service.ResumeAsync());

        EndStreamButton.Click += async (_, _) =>
        {
            await ExecuteWorkflowAsync(() => _workflowModule.Service.EndAsync());
            await ResetTimedAutomationsAtStreamEndAsync();
        };

        ResetWorkflowButton.Click += async (_, _) =>
            await ExecuteWorkflowAsync(() => _workflowModule.Service.ResetAsync());

        AddViewerSampleButton.Click += async (_, _) =>
            await AddViewerSampleAsync();

        RunOfShowStepsList.ItemsSource = _runOfShowSteps;
        RunOfShowPlanBox.SelectionChanged += async (_, _) => await SwitchRunOfShowPlanAsync();
        NewRunOfShowPlanButton.Click += async (_, _) => await CreateRunOfShowPlanAsync();
        RenameRunOfShowPlanButton.Click += async (_, _) => await RenameRunOfShowPlanAsync();
        DeleteRunOfShowPlanButton.Click += async (_, _) => await DeleteRunOfShowPlanAsync();
        RunOfShowStepsList.SelectionChanged += (_, _) => LoadSelectedRunOfShowStep();
        NewRunOfShowStepButton.Click += (_, _) => CreateNewRunOfShowStep();
        DuplicateRunOfShowStepButton.Click += async (_, _) => await DuplicateSelectedRunOfShowStepAsync();
        MoveRunOfShowStepUpButton.Click += async (_, _) => await MoveSelectedRunOfShowStepAsync(-1);
        MoveRunOfShowStepDownButton.Click += async (_, _) => await MoveSelectedRunOfShowStepAsync(1);
        DeleteRunOfShowStepButton.Click += async (_, _) => await DeleteSelectedRunOfShowStepAsync();
        ImportRunOfShowButton.Click += async (_, _) => await ImportRunOfShowAsync();
        ExportRunOfShowButton.Click += async (_, _) => await ExportRunOfShowAsync();
        ValidateRunOfShowButton.Click += async (_, _) => await ValidateRunOfShowAsync();
        SaveRunOfShowStepButton.Click += async (_, _) => await SaveSelectedRunOfShowStepAsync();
        RefreshRunOfShowObsButton.Click += async (_, _) => await RefreshRunOfShowObsListsAsync();
        RunOfShowSceneBox.DropDownOpened += async (_, _) => await RefreshRunOfShowObsListsAsync();
        RunOfShowTransitionBox.DropDownOpened += async (_, _) => await RefreshRunOfShowObsListsAsync();
        RunOfShowStreamerBotActionBox.DropDownOpened += async (_, _) => await RefreshRunOfShowStreamerBotActionsAsync(false);
        RefreshRunOfShowStreamerBotActionsButton.Click += async (_, _) => await RefreshRunOfShowStreamerBotActionsAsync(true);
        SearchRunOfShowTwitchCategoryButton.Click += async (_, _) => await SearchRunOfShowTwitchCategoriesAsync();
        RunOfShowTwitchCategorySearchBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) await SearchRunOfShowTwitchCategoriesAsync();
        };
        ExecuteRunOfShowStepButton.Click += async (_, _) => await ExecuteSelectedRunOfShowStepAsync();
        ExecuteNextRunOfShowStepButton.Click += async (_, _) => await ExecuteNextRunOfShowStepAsync();
        ResetRunOfShowButton.Click += (_, _) => ResetRunOfShow();
        StartAutomaticRunOfShowButton.Click += async (_, _) => await StartAutomaticRunOfShowAsync();
        StopAutomaticRunOfShowButton.Click += (_, _) => StopAutomaticRunOfShow();

        TimedAutomationRulesList.ItemsSource = _timedAutomationRules;
        TimedAutomationDiagnosticsList.ItemsSource = _timedAutomationDiagnostics;
        TimedAutomationRulesList.SelectionChanged += (_, _) => LoadSelectedTimedAutomationRule();
        NewTimedAutomationButton.Click += (_, _) => CreateNewTimedAutomationRule();
        ImportTimedAutomationsButton.Click += async (_, _) => await ImportTimedAutomationsAsync();
        ExportTimedAutomationsButton.Click += async (_, _) => await ExportTimedAutomationsAsync();
        AddTimedAutomationTemplateButton.Click += async (_, _) => await AddTimedAutomationTemplateAsync();
        DeleteTimedAutomationButton.Click += async (_, _) => await DeleteSelectedTimedAutomationRuleAsync();
        SaveTimedAutomationButton.Click += async (_, _) => await SaveTimedAutomationRuleAsync();
        RefreshTimedAutomationObsButton.Click += async (_, _) => await RefreshTimedAutomationObsListsAsync(true);
        TestTimedAutomationButton.Click += async (_, _) => await TestSelectedTimedAutomationRuleAsync();
        CancelTimedAutomationTestButton.Click += (_, _) => _timedAutomationTestCts?.Cancel();
        ValidateTimedAutomationsButton.Click += (_, _) => ValidateTimedAutomationRules();
        ClearTimedAutomationDiagnosticsButton.Click += (_, _) => _timedAutomationDiagnostics.Clear();
        StopAllTimedAutomationsButton.Click += (_, _) => StopAllTimedAutomations();
        RefreshSpotifySavedStateButton.Click += (_, _) => RefreshSpotifySavedStateStatus();
        RestoreSpotifySavedStateNowButton.Click += async (_, _) => await RestoreSpotifySavedStateNowAsync();
        DiscardSpotifySavedStateButton.Click += (_, _) => DiscardSpotifySavedState();
        RefreshSpotifySavedStatesOverviewButton.Click += (_, _) => RefreshSpotifySavedStatesOverview();
        RestoreSelectedSpotifySavedStateButton.Click += async (_, _) => await RestoreSelectedSpotifySavedStateAsync();
        DiscardSelectedSpotifySavedStateButton.Click += (_, _) => DiscardSelectedSpotifySavedState();
        DiscardAllSpotifySavedStatesButton.Click += (_, _) => DiscardAllSpotifySavedStates();
        DiscardExpiredSpotifySavedStatesButton.Click += (_, _) => DiscardExpiredSpotifySavedStates("manuell");
        SpotifySavedStateMaxAgeBox.TextChanged += (_, _) => RefreshSpotifySavedStatesOverview();
        SpotifySavedStateCleanupIntervalBox.Checked += (_, _) => UpdateSpotifySavedStateCleanupTimer();
        SpotifySavedStateCleanupIntervalBox.Unchecked += (_, _) => UpdateSpotifySavedStateCleanupTimer();
        SpotifySavedStateCleanupIntervalMinutesBox.TextChanged += (_, _) => UpdateSpotifySavedStateCleanupTimer();
        _spotifySavedStateCleanupTimer.Tick += (_, _) => DiscardExpiredSpotifySavedStates("Intervall");
        SpotifySavedStatesOverviewList.SelectionChanged += (_, _) => UpdateSpotifySavedStatesOverviewSelection();
        ExportSpotifySavedStateHistoryButton.Click += (_, _) => ExportSpotifySavedStateHistory();
        ExportSpotifySavedStateHistoryCsvButton.Click += (_, _) => ExportSpotifySavedStateHistoryCsv();
        ImportSpotifySavedStateHistoryButton.Click += (_, _) => ImportSpotifySavedStateHistory();
        SelectVisibleSpotifySavedStateHistoryButton.Click += (_, _) => SelectVisibleSpotifySavedStateHistory();
        ExportSelectedSpotifySavedStateHistoryButton.Click += (_, _) => ExportSelectedSpotifySavedStateHistory();
        ExportSelectedSpotifySavedStateHistoryCsvButton.Click += (_, _) => ExportSelectedSpotifySavedStateHistoryCsv();
        RemoveSelectedSpotifySavedStateHistoryButton.Click += (_, _) => RemoveSelectedSpotifySavedStateHistory();
        ClearSpotifySavedStateHistoryButton.Click += (_, _) => ClearSpotifySavedStateHistory();
        CreateSpotifySavedStateHistoryBackupButton.Click += (_, _) => CreateSpotifySavedStateHistoryBackup(manual: true);
        RefreshSpotifySavedStateHistoryBackupsButton.Click += (_, _) => RefreshSpotifySavedStateHistoryBackups();
        PreviewSelectedSpotifySavedStateHistoryBackupButton.Click += (_, _) => UpdateSpotifySavedStateHistoryBackupPreview(showStatus: true);
        RestoreSelectedSpotifySavedStateHistoryBackupButton.Click += (_, _) => RestoreSelectedSpotifySavedStateHistoryBackup();
        RestoreSelectedSpotifySavedStateHistoryPartsButton.Click += (_, _) => RestoreSelectedSpotifySavedStateHistoryParts();
        ApplySpotifyHistoryRestoreProfileButton.Click += (_, _) => ApplySelectedSpotifyHistoryRestoreProfile();
        SaveSpotifyHistoryRestoreProfileButton.Click += (_, _) => SaveSpotifyHistoryRestoreProfile();
        DeleteSpotifyHistoryRestoreProfileButton.Click += (_, _) => DeleteSpotifyHistoryRestoreProfile();
        ExportSpotifyHistoryRestoreProfilesButton.Click += (_, _) => ExportSpotifyHistoryRestoreProfiles();
        ImportSpotifyHistoryRestoreProfilesButton.Click += (_, _) => ImportSpotifyHistoryRestoreProfiles();
        ConfirmSpotifyHistoryRestoreProfilesImportButton.Click += (_, _) => ConfirmSpotifyHistoryRestoreProfilesImport();
        DeleteSelectedSpotifySavedStateHistoryBackupButton.Click += (_, _) => DeleteSelectedSpotifySavedStateHistoryBackup();
        OpenSpotifySavedStateHistoryBackupFolderButton.Click += (_, _) => OpenSpotifySavedStateHistoryBackupFolder();
        SpotifySavedStateHistoryBackupsList.SelectionChanged += (_, _) =>
        {
            UpdateSpotifySavedStateHistoryBackupDetail();
            UpdateSpotifySavedStateHistoryBackupPreview(showStatus: false);
        };
        SpotifySavedStateHistoryBackupsList.ItemsSource = _spotifySavedStateHistoryBackups;
        SpotifySavedStateHistoryBackupDifferencesList.ItemsSource = _spotifySavedStateHistoryBackupDifferences;
        SpotifyHistoryRestoreProfileImportPreviewList.ItemsSource = _spotifyHistoryRestoreProfileImportPreview;
        SpotifyHistoryRestoreProfileBox.ItemsSource = _spotifyHistoryRestoreProfiles;
        LoadSpotifyHistoryRestoreProfiles();
        ResetSpotifySavedStateHistoryFilterButton.Click += (_, _) => ResetSpotifySavedStateHistoryFilter();
        SpotifySavedStateHistorySearchBox.TextChanged += (_, _) => { RefreshSpotifySavedStateHistoryFilter(); SaveSpotifySavedStateHistoryPersistence(); };
        SpotifySavedStateHistoryActionFilterBox.SelectionChanged += (_, _) => { RefreshSpotifySavedStateHistoryFilter(); SaveSpotifySavedStateHistoryPersistence(); };
        SpotifySavedStateHistoryFavoritesOnlyBox.Checked += (_, _) => { RefreshSpotifySavedStateHistoryFilter(); SaveSpotifySavedStateHistoryPersistence(); };
        SpotifySavedStateHistoryFavoritesOnlyBox.Unchecked += (_, _) => { RefreshSpotifySavedStateHistoryFilter(); SaveSpotifySavedStateHistoryPersistence(); };
        SpotifySavedStateHistorySortBox.SelectionChanged += (_, _) => { ApplySpotifySavedStateHistorySort(); SaveSpotifySavedStateHistoryPersistence(); };
        SpotifySavedStateHistoryList.SelectionChanged += (_, _) => UpdateSpotifySavedStateHistoryDetail();
        ToggleSpotifySavedStateHistoryFavoriteButton.Click += (_, _) => ToggleSpotifySavedStateHistoryFavorite();
        SaveSpotifySavedStateHistoryNoteButton.Click += (_, _) => SaveSpotifySavedStateHistoryNote();
        SpotifySavedStateHistoryList.ItemsSource = _spotifySavedStateHistory;
        _spotifySavedStateHistoryView = CollectionViewSource.GetDefaultView(_spotifySavedStateHistory);
        _spotifySavedStateHistoryView.Filter = SpotifySavedStateHistoryMatchesFilter;
        LoadSpotifySavedStateHistoryPersistence();
        RefreshSpotifySavedStateHistoryBackups();
        ApplySpotifySavedStateHistorySort();
        RefreshSpotifySavedStatesOverview();
        RefreshSpotifySavedStateStatistics();
        Loaded += async (_, _) =>
        {
            await RunStartupStepSafelyAsync("Spotify-Zustände bereinigen", () =>
            {
                if (SpotifySavedStateCleanupOnStartupBox.IsChecked == true)
                    DiscardExpiredSpotifySavedStates("Programmstart", onlyLogWhenRemoved: true);
                UpdateSpotifySavedStateCleanupTimer();
                return Task.CompletedTask;
            });
        };
        RefreshWorkflowDesignerButton.Click += (_, _) => RefreshWorkflowDesigner();
        AutoLayoutWorkflowDesignerButton.Click += async (_, _) => await AutoLayoutWorkflowDesignerAsync();
        ValidateWorkflowDesignerButton.Click += (_, _) => ValidateWorkflowDesigner();
        ZoomInWorkflowDesignerButton.Click += (_, _) => SetWorkflowDesignerZoom(WorkflowDesignerScale.ScaleX + 0.1);
        ZoomOutWorkflowDesignerButton.Click += (_, _) => SetWorkflowDesignerZoom(WorkflowDesignerScale.ScaleX - 0.1);
        ResetZoomWorkflowDesignerButton.Click += (_, _) => SetWorkflowDesignerZoom(1.0);
        WorkflowDesignerGroupBox.SelectionChanged += (_, _) => RefreshWorkflowDesigner();
        TimedAutomationSourceSceneBox.SelectionChanged += async (_, _) => await RefreshTimedAutomationSourceListAsync();
        TimedAutomationSourceSceneBox.DropDownClosed += async (_, _) => await RefreshTimedAutomationSourceListAsync();
        TimedAutomationTriggerSceneBox.DropDownOpened += async (_, _) => await RefreshTimedAutomationObsListsAsync(false);
        TimedAutomationTargetSceneBox.DropDownOpened += async (_, _) => await RefreshTimedAutomationObsListsAsync(false);
        TimedAutomationSourceSceneBox.DropDownOpened += async (_, _) => await RefreshTimedAutomationObsListsAsync(false);
        TimedAutomationTransitionBox.DropDownOpened += async (_, _) => await RefreshTimedAutomationObsListsAsync(false);
        StartShortStreamTestButton.Click += async (_, _) => await RunShortStreamTestAsync();
        CancelShortStreamTestButton.Click += (_, _) => _timedAutomationTestCts?.Cancel();
        _timedAutomationTimer.Tick += async (_, _) => await EvaluateTimedAutomationRulesAsync();
        _timedAutomationTimer.Start();

        _workflowModule.Service.StateChanged += (_, state) =>
        {
            Dispatcher.Invoke(() => RefreshWorkflowUi(state));
        };

        CreateStreamDeckActionButton.Click += async (_, _) => await CreateStreamDeckActionAsync();
        OpenStreamDeckActionsFolderButton.Click += (_, _) => OpenStreamDeckActionsFolder();
        RefreshStreamDeckActionsButton.Click += (_, _) => RefreshStreamDeckActionsList();
        DeleteStreamDeckActionButton.Click += (_, _) => DeleteSelectedStreamDeckAction();
        TestStreamDeckActionButton.Click += async (_, _) => await TestSelectedStreamDeckActionAsync();
        DuplicateStreamDeckActionButton.Click += async (_, _) => await DuplicateSelectedStreamDeckActionAsync();
        DuplicateStreamDeckProfileButton.Click += async (_, _) => await DuplicateSelectedStreamDeckProfileAsync();
        ResolveStreamDeckConflictsButton.Click += async (_, _) => await ResolveStreamDeckConflictsAsync();
        ActivateStreamDeckViewButton.Click += (_, _) => ActivateSelectedStreamDeckView();
        LockStreamDeckActionButton.Click += async (_, _) => await ToggleSelectedStreamDeckActionLockAsync();
        BackupStreamDeckConfigurationButton.Click += (_, _) => BackupStreamDeckConfiguration();
        RestoreStreamDeckConfigurationButton.Click += (_, _) => RestoreStreamDeckConfiguration();
        ExportStreamDeckActionsButton.Click += (_, _) => ExportStreamDeckActionCatalog();
        ImportStreamDeckActionsButton.Click += (_, _) => ImportStreamDeckActionCatalog();
        ExportSingleStreamDeckActionButton.Click += (_, _) => ExportSelectedStreamDeckAction();
        ImportSingleStreamDeckActionButton.Click += (_, _) => ImportSingleStreamDeckAction();
        QuickAssignStreamDeckActionButton.Click += async (_, _) => await QuickAssignSelectedStreamDeckActionAsync();
        CompareStreamDeckProfilesButton.Click += (_, _) => CompareStreamDeckProfiles();
        SaveStreamDeckTemplateButton.Click += async (_, _) => await SaveStreamDeckTemplateAsync();
        LoadStreamDeckTemplateButton.Click += async (_, _) => await LoadSelectedStreamDeckTemplateAsync();
        DeleteStreamDeckTemplateButton.Click += (_, _) => DeleteSelectedStreamDeckTemplate();
        StreamDeckCreatedActionsList.SelectionChanged += (_, _) => RefreshSelectedStreamDeckActionDetails();
        ApplyStreamDeckFilterButton.Click += (_, _) => RefreshStreamDeckActionsList();
        SyncStreamDeckStateButton.Click += async (_, _) => await SyncStreamDeckRuntimeStateAsync(true);
        DiagnoseStreamDeckActionsButton.Click += (_, _) => DiagnoseStreamDeckActions();
        SimulateStreamDeckActionButton.Click += async (_, _) => await SimulateSelectedStreamDeckActionAsync();
        ClearStreamDeckExecutionLogButton.Click += (_, _) => ClearStreamDeckExecutionLog();
        AddStreamDeckRuleButton.Click += async (_, _) => await AddStreamDeckAutomationRuleAsync();
        DeleteStreamDeckRuleButton.Click += (_, _) => DeleteSelectedStreamDeckAutomationRule();
        EvaluateStreamDeckRulesButton.Click += async (_, _) => await EvaluateStreamDeckAutomationRulesAsync(true);
        PreviewStreamDeckRulesButton.Click += async (_, _) => await EvaluateStreamDeckAutomationRulesAsync(true, true);
        TestStreamDeckRulesButton.Click += (_, _) => TestStreamDeckAutomationRules();
        ClearStreamDeckRuleHistoryButton.Click += (_, _) => { _streamDeckRuleHistory.Clear(); StreamDeckRuleHistoryBox.Clear(); StreamDeckRuleStatusText.Text = "Entscheidungsverlauf geleert."; };
        SaveStreamDeckRuleTemplateButton.Click += async (_, _) => await SaveSelectedStreamDeckRuleTemplateAsync();
        LoadStreamDeckRuleTemplateButton.Click += async (_, _) => await LoadStreamDeckRuleTemplateAsync();
        ExportStreamDeckRuleSetButton.Click += (_, _) => ExportStreamDeckRuleSet();
        ImportStreamDeckRuleSetButton.Click += async (_, _) => await ImportStreamDeckRuleSetAsync();
        AnalyzeStreamDeckRuleConflictsButton.Click += (_, _) => AnalyzeStreamDeckRuleConflicts();
        RestoreStableStreamDeckStateButton.Click += (_, _) => RestoreStableStreamDeckState();
        ShowStreamDeckRuleStatisticsButton.Click += (_, _) => ShowStreamDeckRuleStatistics();
        ExportStreamDeckRuleDiagnosticsButton.Click += (_, _) => ExportStreamDeckRuleDiagnostics();
        ResetStreamDeckRuleStatisticsButton.Click += async (_, _) => await ResetStreamDeckRuleStatisticsAsync();
        _streamDeckStateSyncTimer.Tick += async (_, _) =>
        {
            if (AutoSyncStreamDeckStateBox.IsChecked == true)
            {
                await SyncStreamDeckRuntimeStateAsync(false);
            }
        };
        _streamDeckStateSyncTimer.Start();
        _streamDeckRuleTimer.Tick += async (_, _) => await EvaluateStreamDeckAutomationRulesAsync(false);
        _streamDeckRuleTimer.Start();
        RefreshStreamDeckActionsList();
        RefreshStreamDeckTemplates();
        RefreshStreamDeckExecutionLog();
        RefreshStreamDeckAutomationRules();
        InstallStreamDeckButton.Click += async (_, _) =>
            await ExportStreamDeckProfileAsync();

        OpenStreamDeckFolderButton.Click += (_, _) =>
            OpenLocalDataFolder("StreamDeck");

        CreateProfileButton.Click += async (_, _) =>
            await CreateProfileAsync();

        ApplyProfileButton.Click += async (_, _) =>
            await ApplySelectedProfileAsync();

        ExportProfileButton.Click += async (_, _) =>
            await ExportSelectedProfileAsync();

        ImportProfileButton.Click += async (_, _) =>
            await ImportProfileAsync();

        DeleteProfileButton.Click += async (_, _) =>
            await DeleteSelectedProfileAsync();

        ProfilesList.SelectionChanged += async (_, _) =>
            await ShowSelectedProfileAsync();

        CheckUpdatesButton.Click += async (_, _) =>
            await CheckUpdatesAsync();

        InstallUpdateButton.Click += async (_, _) =>
            await InstallUpdateAsync();

        CreateBackupButton.Click += async (_, _) =>
            await CreateBackupAsync();

        RestoreBackupButton.Click += async (_, _) =>
            await RestoreSelectedBackupAsync();

        DetectLegacyButton.Click += async (_, _) =>
            await DetectLegacyAsync();

        ImportLegacyButton.Click += async (_, _) =>
            await ImportSelectedLegacyAsync();

        ActivateLicenseButton.Click += async (_, _) => await ActivateLicenseAsync();
        DeactivateLicenseButton.Click += async (_, _) => await DeactivateLicenseAsync();
        RefreshLicenseButton.Click += async (_, _) => await RefreshLicenseAsync();
        OpenEulaButton.Click += (_, _) => OpenLegalDocument("eula");
        OpenPrivacyButton.Click += (_, _) => OpenLegalDocument("privacy");
    }

    public void OpenSettingsPage()
    {
        ShowPage(SettingsPage);
    }

    private void ShowServicesOverview()
    {
        ShowPage(ServicesPage);
        ServicesNavigationPanel.Visibility = Visibility.Visible;
        ServicesOverviewPanel.Visibility = Visibility.Visible;
        ServicesTabControl.Visibility = Visibility.Collapsed;
        SetActiveNavigationButton(ServicesButton);
    }

    private void NavigateToServicesTab(
        int tabIndex,
        Button? navigationButton = null)
    {
        ShowPage(ServicesPage);
        ServicesOverviewPanel.Visibility = Visibility.Collapsed;
        ServicesTabControl.Visibility = Visibility.Visible;

        if (tabIndex >= 0 &&
            tabIndex < ServicesTabControl.Items.Count)
        {
            ServicesTabControl.SelectedIndex = tabIndex;
        }

        ServicesNavigationPanel.Visibility = Visibility.Visible;
        SetActiveNavigationButton(
            navigationButton ?? ServicesButton);
    }

    private void NavigateToSettingsTab(
        int tabIndex,
        Button? navigationButton = null)
    {
        ShowPage(SettingsPage);

        if (tabIndex >= 0 &&
            tabIndex < SettingsTabControl.Items.Count)
        {
            SettingsTabControl.SelectedIndex = tabIndex;
        }

        ServicesNavigationPanel.Visibility =
            ReferenceEquals(navigationButton, ServicesStreamDeckButton)
                ? Visibility.Visible
                : Visibility.Collapsed;

        SetActiveNavigationButton(
            navigationButton ?? SettingsButton);
    }

    private void SetActiveNavigationButton(Button? activeButton)
    {
        var navigationButtons = new Button[]
        {
            DashboardButton,
            ServicesButton,
            ServicesSpotifyButton,
            ServicesTwitchButton,
            ServicesObsButton,
            ServicesStreamerBotButton,
            ServicesStreamDeckButton,
            WorkflowButton,
            StatisticsButton,
            OverlaysButton,
            AlertsButton,
            SettingsButton,
            DiagnosticsButton
        };

        foreach (var button in navigationButtons)
        {
            button.ClearValue(Control.BackgroundProperty);
            button.ClearValue(Control.ForegroundProperty);
            button.FontWeight = FontWeights.Normal;
        }

        if (activeButton is null)
        {
            return;
        }

        activeButton.Background =
            new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(42, 23, 10));
        activeButton.Foreground =
            new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(255, 122, 26));
        activeButton.FontWeight = FontWeights.SemiBold;
    }

    private void ShowPage(UIElement page)
    {
        var pages = new UIElement[]
        {
            DashboardPage,
            ServicesPage,
            WorkflowPage,
            OverlayPage,
            AlertsPage,
            SettingsPage,
            DiagnosticsPage,
            StatisticsPage,
            ProfilesPage,
            MultiPcPage,
            AboutPage
        };

        foreach (var candidate in pages)
        {
            candidate.Visibility = Visibility.Collapsed;
            Panel.SetZIndex(candidate, 0);
        }

        page.Visibility = Visibility.Visible;
        Panel.SetZIndex(page, 1);

        if (ReferenceEquals(page, DashboardPage))
        {
            SetActiveNavigationButton(DashboardButton);
        }
        else if (ReferenceEquals(page, ServicesPage))
        {
            SetActiveNavigationButton(ServicesButton);
        }
        else if (ReferenceEquals(page, WorkflowPage))
        {
            SetActiveNavigationButton(WorkflowButton);
        }
        else if (ReferenceEquals(page, StatisticsPage))
        {
            SetActiveNavigationButton(StatisticsButton);
        }
        else if (ReferenceEquals(page, MultiPcPage))
        {
            SetActiveNavigationButton(MultiPcButton);
        }
        else if (ReferenceEquals(page, OverlayPage))
        {
            SetActiveNavigationButton(OverlaysButton);
        }
        else if (ReferenceEquals(page, AlertsPage))
        {
            SetActiveNavigationButton(AlertsButton);
        }
        else if (ReferenceEquals(page, SettingsPage))
        {
            SetActiveNavigationButton(SettingsButton);
        }
        else if (ReferenceEquals(page, DiagnosticsPage))
        {
            SetActiveNavigationButton(DiagnosticsButton);
        }
        else
        {
            SetActiveNavigationButton(null);
        }
    }

    private void LoadMultiPcRegistry()
    {
        try
        {
            if (!File.Exists(MultiPcRegistryPath)) return;
            var json = File.ReadAllText(MultiPcRegistryPath);
            var devices = System.Text.Json.JsonSerializer.Deserialize<List<MultiPcDeviceRecord>>(json) ?? [];
            _multiPcDevices.Clear();
            _multiPcDevices.AddRange(devices);
        }
        catch (Exception ex)
        {
            MultiPcStatusText.Text = $"Geräteliste konnte nicht geladen werden: {ex.Message}";
        }
    }

    private async Task SaveMultiPcRegistryAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MultiPcRegistryPath)!);
        var json = System.Text.Json.JsonSerializer.Serialize(_multiPcDevices, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(MultiPcRegistryPath, json);
    }

    private void GenerateMultiPcPairingCode()
    {
        _multiPcPairingCode = Random.Shared.Next(100000, 999999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        MultiPcPairingCodeText.Text = _multiPcPairingCode;
        MultiPcPairingInputBox.Text = _multiPcPairingCode;
        MultiPcStatusText.Text = "Neuer Pairing-Code erzeugt. Er gilt für diese lokale Sitzung.";
    }

    private async Task AddMultiPcDeviceAsync()
    {
        var name = MultiPcDeviceNameBox.Text.Trim();
        var host = MultiPcHostBox.Text.Trim();
        var code = MultiPcPairingInputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(host))
        {
            MultiPcStatusText.Text = "Gerätename und Host dürfen nicht leer sein.";
            return;
        }
        if (_multiPcDevices.Any(device => string.Equals(device.Host, host, StringComparison.OrdinalIgnoreCase)))
        {
            MultiPcStatusText.Text = "Dieses Gerät ist bereits registriert.";
            return;
        }
        string agentKey;
        try
        {
            var pairUri = $"https://{host}:{GetMultiPcAgentPort()}/api/pair?code={Uri.EscapeDataString(code)}";
            string? observedFingerprint = null;
            using var pairHandler = new System.Net.Http.HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                {
                    observedFingerprint = certificate is null ? null : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(certificate.GetRawCertData()));
                    return certificate is not null;
                }
            };
            using var pairClient = new System.Net.Http.HttpClient(pairHandler) { Timeout = TimeSpan.FromSeconds(5) };
            var pairing = await pairClient.GetFromJsonAsync<MultiPcPairingResponse>(pairUri);
            if (pairing is null || string.IsNullOrWhiteSpace(pairing.AgentKey))
            {
                MultiPcStatusText.Text = "Der Remote-Agent hat keine gültigen Kopplungsdaten geliefert.";
                return;
            }
            if (string.IsNullOrWhiteSpace(observedFingerprint) || !string.Equals(observedFingerprint, pairing.CertificateFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                MultiPcStatusText.Text = "TLS-Fingerabdruck des Agenten stimmt nicht mit der Pairing-Antwort überein.";
                return;
            }
            agentKey = pairing.AgentKey;
            _multiPcDevices.Add(new MultiPcDeviceRecord(Guid.NewGuid().ToString("N"), name, host, DateTimeOffset.Now, agentKey, pairing.CertificateFingerprint, pairing.AllowedCommands ?? [], MultiPcMacAddressBox.Text.Trim(), pairing.Port));
        }
        catch (Exception ex)
        {
            MultiPcStatusText.Text = $"Kopplung mit dem Remote-Agent fehlgeschlagen: {ex.Message}";
            return;
        }
        await SaveMultiPcRegistryAsync();
        GenerateMultiPcPairingCode();
        await RefreshMultiPcPageAsync();
        MultiPcStatusText.Text = $"{name} wurde TLS-verschlüsselt gekoppelt und als vertrauenswürdig gespeichert.";
    }

    private async Task RemoveSelectedMultiPcDeviceAsync()
    {
        var index = MultiPcDevicesList.SelectedIndex - 1;
        if (index < 0 || index >= _multiPcDevices.Count)
        {
            MultiPcStatusText.Text = "Bitte zuerst ein Gerät auswählen.";
            return;
        }
        var removed = _multiPcDevices[index];
        _multiPcDevices.RemoveAt(index);
        await SaveMultiPcRegistryAsync();
        await RefreshMultiPcPageAsync();
        MultiPcStatusText.Text = $"{removed.Name} wurde entfernt.";
    }

    private async Task RefreshMultiPcPageAsync()
    {
        MultiPcLocalAgentStatusText.Text = $"AKTIV · {Environment.MachineName}";
        MultiPcDeviceCountText.Text = (_multiPcDevices.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var online = 1;
        _multiPcDeviceItems.Clear();
        _multiPcDeviceItems.Add($"●  {Environment.MachineName} · Lokaler Hauptrechner · Online · {Environment.OSVersion.VersionString}");
        foreach (var device in _multiPcDevices)
        {
            var reachable = false;
            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync(device.Host, 650);
                reachable = reply.Status == System.Net.NetworkInformation.IPStatus.Success;
            }
            catch
            {
                reachable = false;
            }
            var agent = await TryGetMultiPcAgentStatusAsync(device);
            if (agent is not null) reachable = true;
            if (reachable) online++;
            var agentInfo = agent is null ? (reachable ? "Ping erreichbar · TLS-Agent antwortet nicht" : "Offline/Agent fehlt") : $"TLS-Agent online · CPU {agent.CpuPercent:0}% · RAM {agent.MemoryMb:0} MB · {agent.MachineName}";
            _multiPcDeviceItems.Add($"{(reachable ? "●" : "○")}  {device.Name} · {device.Host} · {agentInfo} · gekoppelt {device.PairedAt.LocalDateTime:g}");
        }
        MultiPcOnlineCountText.Text = online.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void UpdateSelectedMultiPcDeviceText()
    {
        var index = MultiPcDevicesList.SelectedIndex - 1;
        MultiPcSelectedDeviceText.Text = index >= 0 && index < _multiPcDevices.Count
            ? $"Ausgewählt: {_multiPcDevices[index].Name} · {_multiPcDevices[index].Host}"
            : "Kein Remote-Gerät ausgewählt.";
        MultiPcTrustText.Text = index >= 0 && index < _multiPcDevices.Count
            ? $"TLS-Vertrauen: SHA-256 {_multiPcDevices[index].CertificateFingerprint} · Befehle: {string.Join(", ", _multiPcDevices[index].AllowedCommands ?? [])}"
            : "TLS-Vertrauen: kein Gerät ausgewählt";
    }

    private int GetMultiPcAgentPort() => int.TryParse(MultiPcAgentPortBox.Text, out var port) && port is > 0 and <= 65535 ? port : 47631;

    private int GetMultiPcAgentPort(MultiPcDeviceRecord device) => device.AgentPort is > 0 and <= 65535 ? device.AgentPort : GetMultiPcAgentPort();

    private MultiPcDeviceRecord? GetSelectedRemoteDevice()
    {
        var index = MultiPcDevicesList.SelectedIndex - 1;
        return index >= 0 && index < _multiPcDevices.Count ? _multiPcDevices[index] : null;
    }

    private async Task<MultiPcAgentStatus?> TryGetMultiPcAgentStatusAsync(MultiPcDeviceRecord device)
    {
        try
        {
            using var client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/status");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<MultiPcAgentStatus>();
        }
        catch { return null; }
    }

    private async Task SendMultiPcCommandAsync(string command)
    {
        var device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        try
        {
            if (!(device.AllowedCommands ?? []).Contains(command, StringComparer.OrdinalIgnoreCase))
            {
                MultiPcStatusText.Text = $"{device.Name}: Der Agent hat den Befehl ‘{command}’ nicht freigegeben. Berechtigungen werden in agent-permissions.json auf dem Ziel-PC verwaltet.";
                return;
            }
            using var client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/command");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            request.Content = System.Net.Http.Json.JsonContent.Create(new { command });
            using var response = await client.SendAsync(request);
            var result = await response.Content.ReadAsStringAsync();
            MultiPcStatusText.Text = response.IsSuccessStatusCode ? $"{device.Name}: {command} wurde angenommen." : $"Agentfehler: {result}";
            AddMultiPcHistory(device.Name, command, response.IsSuccessStatusCode ? "angenommen" : "Fehler");
        }
        catch (Exception ex) { MultiPcStatusText.Text = $"Remote-Befehl fehlgeschlagen: {ex.Message}"; }
    }


    private async Task RefreshRemoteObsStateAsync()
    {
        var device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        try
        {
            using var client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/obs/state");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            using var response = await client.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) { MultiPcStatusText.Text = "Remote-OBS konnte nicht geladen werden: " + json; return; }
            var state = System.Text.Json.JsonSerializer.Deserialize<RemoteObsState>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            MultiPcObsScenesBox.ItemsSource = state?.Scenes ?? [];
            MultiPcObsAudioInputsBox.ItemsSource = state?.AudioInputs?.Select(x => x.Name + (x.Muted ? " · gemutet" : $" · {x.VolumeDb:0.0} dB")).ToArray() ?? [];
            MultiPcObsSceneItemsBox.ItemsSource = state?.SceneItems?.Select(x => x.SourceName + (x.Enabled ? " · sichtbar" : " · ausgeblendet")).ToArray() ?? [];
            MultiPcObsScenesBox.SelectedItem = state?.CurrentScene;
            if (MultiPcObsAudioInputsBox.SelectedIndex < 0 && MultiPcObsAudioInputsBox.Items.Count > 0) MultiPcObsAudioInputsBox.SelectedIndex = 0;
            if (MultiPcObsSceneItemsBox.SelectedIndex < 0 && MultiPcObsSceneItemsBox.Items.Count > 0) MultiPcObsSceneItemsBox.SelectedIndex = 0;
            MultiPcStatusText.Text = $"Remote-OBS verbunden · aktuelle Szene: {state?.CurrentScene ?? "unbekannt"}.";
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Remote-OBS-Fehler: " + ex.Message; }
    }

    private async Task SwitchRemoteObsSceneAsync()
    {
        var scene = MultiPcObsScenesBox.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(scene)) { MultiPcStatusText.Text = "Bitte eine Remote-Szene auswählen."; return; }
        await PostRemoteObsAsync("scene", new { sceneName = scene }, $"Szene {scene} aktiviert");
        await RefreshRemoteObsStateAsync();
    }

    private async Task SetRemoteObsMuteAsync(bool muted)
    {
        var raw = MultiPcObsAudioInputsBox.SelectedItem?.ToString();
        var input = raw?.Split(" · ", StringSplitOptions.None)[0];
        if (string.IsNullOrWhiteSpace(input)) { MultiPcStatusText.Text = "Bitte eine Remote-Audioquelle auswählen."; return; }
        await PostRemoteObsAsync("mute", new { inputName = input, muted }, $"{input} {(muted ? "gemutet" : "entmutet")}");
        await RefreshRemoteObsStateAsync();
    }

    private async Task SetRemoteObsVolumeAsync()
    {
        var raw = MultiPcObsAudioInputsBox.SelectedItem?.ToString();
        var input = raw?.Split(" · ", StringSplitOptions.None)[0];
        if (string.IsNullOrWhiteSpace(input)) { MultiPcStatusText.Text = "Bitte eine Remote-Audioquelle auswählen."; return; }
        if (!double.TryParse(MultiPcObsVolumeBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var volumeDb))
        {
            MultiPcStatusText.Text = "Lautstärke bitte als dB-Wert eingeben, zum Beispiel -10."; return;
        }
        volumeDb = Math.Clamp(volumeDb, -100, 26);
        await PostRemoteObsAsync("volume", new { inputName = input, volumeDb }, $"Lautstärke von {input} auf {volumeDb:0.0} dB gesetzt");
        await RefreshRemoteObsStateAsync();
    }

    private async Task FadeRemoteObsVolumeAsync()
    {
        var input = MultiPcObsAudioInputsBox.SelectedItem?.ToString()?.Split(" · ", StringSplitOptions.None)[0];
        if (string.IsNullOrWhiteSpace(input)) { MultiPcStatusText.Text = "Bitte eine Remote-Audioquelle auswählen."; return; }
        if (!double.TryParse(MultiPcObsVolumeBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var targetDb)) { MultiPcStatusText.Text = "Ungültiger dB-Wert."; return; }
        var duration = int.TryParse(MultiPcObsFadeDurationBox.Text, out var ms) ? Math.Clamp(ms, 100, 30000) : 1000;
        await PostRemoteObsAsync("volume-fade", new { inputName = input, targetVolumeDb = Math.Clamp(targetDb, -100, 26), durationMilliseconds = duration }, $"Lautstärke von {input} wird gefadet");
    }

    private async Task RefreshRemoteObsFiltersAsync()
    {
        var device = GetSelectedRemoteDevice();
        var source = MultiPcObsAudioInputsBox.SelectedItem?.ToString()?.Split(" · ", StringSplitOptions.None)[0];
        if (device is null || string.IsNullOrWhiteSpace(source)) return;
        try { using var client = CreateTrustedMultiPcClient(device); using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/obs/filters?sourceName={Uri.EscapeDataString(source)}"); request.Headers.Add("X-CCS-Agent-Key", device.AgentKey); using var response = await client.SendAsync(request); if (!response.IsSuccessStatusCode) return; var filters = await response.Content.ReadFromJsonAsync<RemoteObsFilter[]>(); MultiPcObsFiltersBox.ItemsSource = filters?.Select(x => x.Name + (x.Enabled ? " · aktiv" : " · aus")).ToArray() ?? []; if (MultiPcObsFiltersBox.Items.Count > 0) MultiPcObsFiltersBox.SelectedIndex = 0; } catch { }
    }

    private async Task SetRemoteObsFilterAsync(bool enabled)
    {
        var source = MultiPcObsAudioInputsBox.SelectedItem?.ToString()?.Split(" · ", StringSplitOptions.None)[0]; var filter = MultiPcObsFiltersBox.SelectedItem?.ToString()?.Split(" · ", StringSplitOptions.None)[0];
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(filter)) { MultiPcStatusText.Text = "Bitte Quelle und Filter auswählen."; return; }
        await PostRemoteObsAsync("filter", new { sourceName = source, filterName = filter, enabled }, $"Filter {filter} {(enabled ? "aktiviert" : "deaktiviert")}"); await RefreshRemoteObsFiltersAsync();
    }

    private async Task ApplyRemoteObsTransformAsync(bool reset)
    {
        var scene = MultiPcObsScenesBox.SelectedItem?.ToString(); var source = MultiPcObsSceneItemsBox.SelectedItem?.ToString()?.Split(" · ", StringSplitOptions.None)[0];
        if (string.IsNullOrWhiteSpace(scene) || string.IsNullOrWhiteSpace(source)) { MultiPcStatusText.Text = "Bitte Szene und Quelle auswählen."; return; }
        double Parse(string text, double fallback) => double.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
        await PostRemoteObsAsync("transform", new { sceneName = scene, sourceName = source, reset, x = Parse(MultiPcObsPosXBox.Text, 0), y = Parse(MultiPcObsPosYBox.Text, 0), width = Math.Max(1, Parse(MultiPcObsWidthBox.Text, 640)), height = Math.Max(1, Parse(MultiPcObsHeightBox.Text, 360)), rotation = Parse(MultiPcObsRotationBox.Text, 0) }, reset ? $"Transform von {source} zurückgesetzt" : $"Transform von {source} gesetzt");
    }

    private async Task SetRemoteObsSceneItemVisibilityAsync(bool enabled)
    {
        var scene = MultiPcObsScenesBox.SelectedItem?.ToString();
        var raw = MultiPcObsSceneItemsBox.SelectedItem?.ToString();
        var source = raw?.Split(" · ", StringSplitOptions.None)[0];
        if (string.IsNullOrWhiteSpace(scene) || string.IsNullOrWhiteSpace(source))
        {
            MultiPcStatusText.Text = "Bitte eine Szene und eine Szenen-Quelle auswählen."; return;
        }
        await PostRemoteObsAsync("scene-item", new { sceneName = scene, sourceName = source, enabled }, $"{source} wurde {(enabled ? "eingeblendet" : "ausgeblendet")}");
        await RefreshRemoteObsStateAsync();
    }

    private async Task PostRemoteObsAsync(string endpoint, object payload, string successText)
    {
        var device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        try
        {
            using var client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/obs/{endpoint}");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            request.Content = System.Net.Http.Json.JsonContent.Create(payload);
            using var response = await client.SendAsync(request);
            var result = await response.Content.ReadAsStringAsync();
            MultiPcStatusText.Text = response.IsSuccessStatusCode ? successText : "Remote-OBS-Fehler: " + result;
            AddMultiPcHistory(device.Name, "obs." + endpoint, response.IsSuccessStatusCode ? "angenommen" : "Fehler");
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Remote-OBS-Befehl fehlgeschlagen: " + ex.Message; }
    }

    private RemoteObsOutputState? _remoteObsOutputState;

    private async Task RefreshRemoteObsOutputStateAsync()
    {
        var device = GetSelectedRemoteDevice();
        if (device is null) return;
        try
        {
            using var client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/obs/output");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            _remoteObsOutputState = await response.Content.ReadFromJsonAsync<RemoteObsOutputState>();
            if (_remoteObsOutputState is null) return;
            MultiPcObsOutputStatusText.Text = $"Stream: {(_remoteObsOutputState.StreamActive ? "LIVE" : "offline")} · Aufnahme: {(_remoteObsOutputState.RecordActive ? (_remoteObsOutputState.RecordPaused ? "pausiert" : "läuft") : "aus")}";
            MultiPcObsTransitionsBox.ItemsSource = _remoteObsOutputState.Transitions;
            if (MultiPcObsTransitionsBox.SelectedIndex < 0 && _remoteObsOutputState.Transitions.Length > 0) MultiPcObsTransitionsBox.SelectedIndex = 0;
        }
        catch (Exception ex) { MultiPcObsOutputStatusText.Text = "OBS-Ausgabestatus nicht verfügbar: " + ex.Message; }
    }

    private async Task SendRemoteObsOutputActionAsync(string action)
    {
        var device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        try
        {
            using var client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/obs/output");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            request.Content = System.Net.Http.Json.JsonContent.Create(new { action });
            using var response = await client.SendAsync(request);
            var ok = response.IsSuccessStatusCode;
            MultiPcStatusText.Text = ok ? $"OBS-Aktion {action} wurde ausgeführt." : $"OBS-Aktion {action} wurde abgelehnt.";
            AddMultiPcHistory(device.Name, action, ok ? "ausgeführt" : "fehlgeschlagen");
            await RefreshRemoteObsOutputStateAsync();
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Remote-OBS-Aktion fehlgeschlagen: " + ex.Message; }
    }

    private async Task ToggleRemoteObsRecordPauseAsync()
    {
        await RefreshRemoteObsOutputStateAsync();
        if (_remoteObsOutputState is null || !_remoteObsOutputState.RecordActive) { MultiPcStatusText.Text = "Auf dem Remote-PC läuft keine Aufnahme."; return; }
        await SendRemoteObsOutputActionAsync(_remoteObsOutputState.RecordPaused ? "record.resume" : "record.pause");
    }

    private async Task ApplyRemoteObsTransitionAsync()
    {
        var device = GetSelectedRemoteDevice();
        var transition = MultiPcObsTransitionsBox.SelectedItem?.ToString();
        if (device is null || string.IsNullOrWhiteSpace(transition)) { MultiPcStatusText.Text = "Bitte Gerät und Übergang auswählen."; return; }
        var duration = int.TryParse(MultiPcObsTransitionDurationBox.Text, out var value) ? Math.Clamp(value, 50, 20000) : 300;
        try
        {
            using var client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/obs/transition");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            request.Content = System.Net.Http.Json.JsonContent.Create(new { transitionName = transition, durationMilliseconds = duration });
            using var response = await client.SendAsync(request);
            MultiPcStatusText.Text = response.IsSuccessStatusCode ? $"Übergang {transition} ({duration} ms) gesetzt." : "Übergang konnte nicht gesetzt werden.";
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Remote-Übergang fehlgeschlagen: " + ex.Message; }
    }

    private async Task RefreshRemoteObsPreviewAsync()
    {
        var device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        try
        {
            using var client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/obs/preview");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync();
            var image = new System.Windows.Media.Imaging.BitmapImage();
            using var stream = new MemoryStream(bytes);
            image.BeginInit(); image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
            MultiPcObsPreviewImage.Source = image;
            MultiPcStatusText.Text = "Remote-Programmvorschau wurde aktualisiert.";
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Remote-Vorschau fehlgeschlagen: " + ex.Message; }
    }

    private async Task SaveRemoteAgentSettingsAsync()
    {
        var device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        if (!int.TryParse(MultiPcRemoteObsPortBox.Text, out var obsPort) || obsPort is <= 0 or > 65535) { MultiPcStatusText.Text = "Ungültiger OBS-WebSocket-Port."; return; }
        try
        {
            using var client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/settings");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            request.Content = System.Net.Http.Json.JsonContent.Create(new { obsPath = "", streamerBotPath = "", obsWebSocketHost = MultiPcRemoteObsHostBox.Text.Trim(), obsWebSocketPort = obsPort, obsWebSocketPassword = MultiPcRemoteObsPasswordBox.Password });
            using var response = await client.SendAsync(request);
            MultiPcStatusText.Text = response.IsSuccessStatusCode ? "Agent-Einstellungen gespeichert." : "Agent-Einstellungen konnten nicht gespeichert werden.";
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Agent-Einstellungen fehlgeschlagen: " + ex.Message; }
    }

    private async Task FetchMultiPcDiagnosticsAsync()
    {
        var device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        var status = await TryGetMultiPcAgentStatusAsync(device);
        MultiPcStatusText.Text = status is null ? "Der Agent antwortet nicht oder der Schlüssel stimmt nicht." : $"{status.MachineName}: CPU {status.CpuPercent:0}% · RAM {status.MemoryMb:0} MB · Uptime {status.UptimeMinutes:0} Min. · OBS {(status.ObsRunning ? "läuft" : "aus")} · Spotify {(status.SpotifyRunning ? "läuft" : "aus")}.";
    }

    private System.Net.Http.HttpClient CreateTrustedMultiPcClient(MultiPcDeviceRecord device)
    {
        var handler = new System.Net.Http.HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate is not null &&
                string.Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(certificate.GetRawCertData())), device.CertificateFingerprint, StringComparison.OrdinalIgnoreCase)
        };
        return new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
    }

    private System.Net.Http.HttpClient CreateTrustedAgentClient(MultiPcDeviceRecord device)
        => CreateTrustedMultiPcClient(device);


    private void AddMultiPcHistory(string device, string action, string result)
    {
        var timestamp = DateTimeOffset.Now;
        _multiPcHistoryItems.Insert(0, $"{timestamp:HH:mm:ss} · {device} · {action} · {result}");
        while (_multiPcHistoryItems.Count > 50) _multiPcHistoryItems.RemoveAt(_multiPcHistoryItems.Count - 1);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MultiPcRolloutAuditPath)!);
            var entry = new MultiPcRolloutAuditEntry(timestamp, device, action, result);
            File.AppendAllText(MultiPcRolloutAuditPath, System.Text.Json.JsonSerializer.Serialize(entry) + Environment.NewLine);
        }
        catch { }
    }

    private void LoadMultiPcRolloutAudit()
    {
        try
        {
            _multiPcHistoryItems.Clear();
            if (!File.Exists(MultiPcRolloutAuditPath))
            {
                MultiPcStatusText.Text = "Es ist noch kein dauerhaftes Rollout-Auditprotokoll vorhanden.";
                return;
            }
            foreach (var line in File.ReadLines(MultiPcRolloutAuditPath).Where(line => !string.IsNullOrWhiteSpace(line)).TakeLast(200).Reverse())
            {
                var entry = System.Text.Json.JsonSerializer.Deserialize<MultiPcRolloutAuditEntry>(line);
                if (entry is not null) _multiPcHistoryItems.Add($"{entry.Timestamp.LocalDateTime:g} · {entry.Device} · {entry.Action} · {entry.Result}");
            }
            MultiPcStatusText.Text = $"Auditprotokoll geladen: {_multiPcHistoryItems.Count} Einträge.";
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Auditprotokoll konnte nicht geladen werden: " + ex.Message; }
    }

    private async Task WakeSelectedMultiPcDeviceAsync()
    {
        var device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        var raw = (device.MacAddress ?? "").Replace(":", "").Replace("-", "").Replace(".", "");
        if (raw.Length != 12 || !raw.All(Uri.IsHexDigit)) { MultiPcStatusText.Text = "Für dieses Gerät ist keine gültige MAC-Adresse gespeichert."; return; }
        var mac = Convert.FromHexString(raw);
        var packet = new byte[6 + 16 * 6];
        Array.Fill(packet, (byte)0xFF, 0, 6);
        for (var i = 0; i < 16; i++) Buffer.BlockCopy(mac, 0, packet, 6 + i * 6, 6);
        using var udp = new System.Net.Sockets.UdpClient();
        udp.EnableBroadcast = true;
        await udp.SendAsync(packet, packet.Length, new System.Net.IPEndPoint(System.Net.IPAddress.Broadcast, 9));
        MultiPcStatusText.Text = $"Wake-on-LAN-Paket wurde an {device.Name} gesendet.";
        AddMultiPcHistory(device.Name, "wake-on-lan", "gesendet");
    }

    private async Task DiscoverMultiPcAgentsAsync()
    {
        MultiPcStatusText.Text = "Suche Creator Control Agents im lokalen Netzwerk…";
        var found = new List<MultiPcDiscoveryResponse>();
        try
        {
            using var udp = new System.Net.Sockets.UdpClient(0);
            udp.EnableBroadcast = true;
            var request = System.Text.Encoding.UTF8.GetBytes("CCS_DISCOVER_V1");
            await udp.SendAsync(request, request.Length, new System.Net.IPEndPoint(System.Net.IPAddress.Broadcast, 47632));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var response = await udp.ReceiveAsync(cts.Token);
                    var json = System.Text.Encoding.UTF8.GetString(response.Buffer);
                    var item = System.Text.Json.JsonSerializer.Deserialize<MultiPcDiscoveryResponse>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (item is not null && found.All(x => !string.Equals(x.Host, item.Host, StringComparison.OrdinalIgnoreCase))) found.Add(item with { Host = response.RemoteEndPoint.Address.ToString() });
                }
                catch (OperationCanceledException) { break; }
            }
        }
        catch (Exception ex) { MultiPcStatusText.Text = $"LAN-Suche fehlgeschlagen: {ex.Message}"; return; }
        if (found.Count == 0) { MultiPcStatusText.Text = "Keine Agents gefunden. Prüfe Windows-Firewall und ob der Agent läuft."; return; }
        var first = found[0];
        MultiPcDeviceNameBox.Text = first.MachineName;
        MultiPcHostBox.Text = first.Host;
        MultiPcAgentPortBox.Text = first.Port.ToString();
        MultiPcMacAddressBox.Text = first.MacAddress ?? "";
        MultiPcStatusText.Text = found.Count == 1 ? $"Agent {first.MachineName} gefunden und in das Kopplungsformular übernommen." : $"{found.Count} Agents gefunden. {first.MachineName} wurde übernommen.";
    }

    private sealed record MultiPcDiscoveryResponse(string MachineName, string Host, int Port, string Version, string? MacAddress);

    private sealed record MultiPcDeviceRecord(string Id, string Name, string Host, DateTimeOffset PairedAt, string AgentKey, string CertificateFingerprint = "", string[]? AllowedCommands = null, string MacAddress = "", int AgentPort = 47631);
    private sealed record MultiPcPairingResponse(string MachineName, string AgentKey, int Port, string CertificateFingerprint, string Transport, string[]? AllowedCommands);
    private sealed record RemoteObsAudioInput(string Name, bool Muted, double VolumeDb);
    private sealed record RemoteObsSceneItem(string SourceName, bool Enabled);
    private sealed record RemoteObsFilter(string Name, string Kind, bool Enabled, int Index);
    private sealed record RemoteObsState(bool Connected, string CurrentScene, string[] Scenes, RemoteObsAudioInput[] AudioInputs, RemoteObsSceneItem[] SceneItems);
    private sealed record RemoteObsOutputState(bool StreamActive, bool StreamReconnecting, bool RecordActive, bool RecordPaused, string[] Transitions);
    private sealed record MultiPcAgentStatus(string MachineName, double CpuPercent, double MemoryMb, double UptimeMinutes, bool ObsRunning, bool SpotifyRunning, bool StreamerBotRunning, string Version, string Transport, string CertificateFingerprint, string[] AllowedCommands);

    private async Task LoadSettingsAsync()
    {
        _loadingSettingsIntoUi = true;
        _settings = await _settingsStore.LoadAsync();
        _settings.Workflow ??= new WorkflowSettings();
        _settings.Workflow.TimedAutomations ??= [];
        _settings.Workflow.RunOfShowSteps ??= [];
        _settings.Workflow.RunOfShowPlans ??= [];
        _settings.Obs.AudioProfiles ??= [];
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

        DisplayNameBox.Text = _settings.Branding.DisplayName;
        ChannelNameBox.Text = _settings.Branding.ChannelName;
        StartWithWindowsBox.IsChecked = _settings.General.StartWithWindows;
        MinimizeToTrayBox.IsChecked = _settings.General.MinimizeToTray;
        OverlayManifestPathBox.Text = _settings.General.OverlayManifestPath;
        UpdateOverlayManifestStatus();
        ConnectionWatchdogEnabledBox.IsChecked = _settings.General.ConnectionWatchdogEnabled;
        ConnectionWatchdogSecondsBox.Text = _settings.General.ConnectionWatchdogSeconds.ToString();
        ReconnectObsBox.IsChecked = _settings.General.ReconnectObs;
        ReconnectTwitchBox.IsChecked = _settings.General.ReconnectTwitch;
        ReconnectSpotifyBox.IsChecked = _settings.General.ReconnectSpotify;
        ReconnectStreamerBotBox.IsChecked = _settings.General.ReconnectStreamerBot;
        _connectionWatchdogTimer.Interval = TimeSpan.FromSeconds(
            Math.Clamp(_settings.General.ConnectionWatchdogSeconds, 5, 300));
        DashboardAutoFocusOnStreamStartBox.IsChecked =
            _settings.Dashboard.AutoFocusModeOnStreamStart;
        DashboardAutoExitFocusOnStreamEndBox.IsChecked =
            _settings.Dashboard.AutoExitFocusModeOnStreamEnd;
        DashboardShowServiceStatusBox.IsChecked = _settings.Dashboard.ShowServiceStatus;
        DashboardShowStreamControlsBox.IsChecked = _settings.Dashboard.ShowStreamControls;
        DashboardShowLivePanelsBox.IsChecked = _settings.Dashboard.ShowLivePanels;
        DashboardShowQuickServicesBox.IsChecked = _settings.Dashboard.ShowQuickServices;
        DashboardShowWorkflowRailBox.IsChecked = _settings.Dashboard.ShowWorkflowRail;
        DashboardShowAdvancedToolsBox.IsChecked = _settings.Dashboard.ShowAdvancedTools;
        DashboardShowNotificationsBox.IsChecked = _settings.Dashboard.ShowNotifications;
        DashboardShowStreamHistoryBox.IsChecked = _settings.Dashboard.ShowStreamHistory;
        LoadDashboardModuleOrderEditor();
        ApplyDashboardModuleOrder();
        ApplyDashboardModuleSizes();
        ApplyDashboardLayout();

        ObsHostBox.Text = _settings.Obs.Host;
        ObsPortBox.Text = _settings.Obs.Port.ToString();
        ObsAutoConnectBox.IsChecked = _settings.Obs.AutoConnect;
        ObsConnectOnPrepareBox.IsChecked = _settings.Obs.ConnectOnPrepare;
        ObsExecutablePathBox.Text = _settings.Obs.ExecutablePath;
        ObsPasswordBox.Password = await _secretStore.LoadAsync("obs.password") ?? "";

        TwitchClientIdBox.Text = _settings.Twitch.ClientId;
        TwitchChannelBox.Text = _settings.Twitch.ChannelName;
        TwitchAutoConnectBox.IsChecked = _settings.Twitch.AutoConnect;
        TwitchConnectOnPrepareBox.IsChecked = _settings.Twitch.ConnectOnPrepare;
        TwitchCreatorDashboardUrlBox.Text = _settings.Twitch.CreatorDashboardUrl;
        TwitchChatEnabledBox.IsChecked = _settings.Twitch.EnableChat;
        TwitchEventSubEnabledBox.IsChecked = _settings.Twitch.EnableEventSub;

        SpotifyClientIdBox.Text = _settings.Spotify.ClientId;
        SpotifyRedirectUriBox.Text = _settings.Spotify.RedirectUri;
        SpotifyAutoConnectBox.IsChecked = _settings.Spotify.AutoConnect;
        SpotifyConnectOnPrepareBox.IsChecked = _settings.Spotify.ConnectOnPrepare;
        SpotifyExecutablePathBox.Text = _settings.Spotify.ExecutablePath;
        ServicesSpotifyAutoTransferPreferredBox.IsChecked = _settings.Spotify.AutoTransferToPreferredDevice;
        ServicesSpotifyUseActiveFallbackBox.IsChecked = _settings.Spotify.UseActiveDeviceWhenPreferredUnavailable;
        ServicesSpotifySmartAutomationBox.IsChecked = _settings.Spotify.SmartAutomationEnabled;
        ServicesSpotifyHealthMonitorBox.IsChecked = _settings.Spotify.HealthMonitorEnabled;
        ServicesSpotifyAutoRecoverBox.IsChecked = _settings.Spotify.AutoRecoverPlayback;
        StreamerBotHostBox.Text = _settings.StreamerBot.Host;
        StreamerBotPortBox.Text = _settings.StreamerBot.Port.ToString();
        StreamerBotEndpointBox.Text = _settings.StreamerBot.Endpoint;
        StreamerBotPasswordBox.Password = _settings.StreamerBot.Password;
        StreamerBotAutoConnectBox.IsChecked = _settings.StreamerBot.AutoConnect;
        StreamerBotConnectOnPrepareBox.IsChecked = _settings.StreamerBot.ConnectOnPrepare;
        StreamerBotExecutablePathBox.Text = _settings.StreamerBot.ExecutablePath;
        SuiteAlertsEnabledBox.IsChecked = _settings.Alerts.Enabled;
        SuppressStreamerBotAlertsBox.IsChecked = _settings.StreamerBot.SuppressAlertActionsWhenSuiteAlertsEnabled;
        StreamerBotDisableAlertsActionBox.Text = _settings.StreamerBot.DisableAlertsActionName;
        StreamerBotEnableAlertsActionBox.Text = _settings.StreamerBot.EnableAlertsActionName;
        SettingsStreamerBotDisableAlertsActionBox.Text = _settings.StreamerBot.DisableAlertsActionName;
        SettingsStreamerBotEnableAlertsActionBox.Text = _settings.StreamerBot.EnableAlertsActionName;
        BindStreamerBotActionSelectors();
        SpotifyVolumeBox.Text = _settings.Spotify.StartVolumePercent.ToString();
        SpotifyFadeOutBox.IsChecked = _settings.Spotify.FadeOutEnabled;
        SpotifyPauseAfterFadeBox.IsChecked = _settings.Spotify.PauseAfterFadeOut;
        SpotifyFadeOutSecondsBox.Text = _settings.Spotify.FadeOutSeconds.ToString();
        SpotifyFadeInSecondsBox.Text = _settings.Spotify.FadeInSeconds.ToString();
        ServicesSpotifyHideMutedBox.IsChecked = _settings.Spotify.OverlayHideWhenMuted;
        ServicesSpotifyDetectObsMuteBox.IsChecked = _settings.Spotify.OverlayMuteDetectionObsSource;
        ServicesSpotifyDetectVolumeMuteBox.IsChecked = _settings.Spotify.OverlayMuteDetectionSpotifyVolume;
        ServicesSpotifyHidePausedBox.IsChecked = _settings.Spotify.OverlayHideWhenPaused;
        ServicesSpotifyObsAudioSourceBox.Text = string.IsNullOrWhiteSpace(_settings.Spotify.OverlayObsAudioSource) ? "Spotify" : _settings.Spotify.OverlayObsAudioSource;
        // Den vom Benutzer gespeicherten Zustand wiederherstellen. Zuvor wurde
        // der Haken bei jedem Programmstart zwangsweise auf einen festen Wert
        // gesetzt und die Einstellung dadurch praktisch ignoriert.
        ServicesSpotifyOverlayEnabledBox.IsChecked = true;
        ServicesSpotifyOverlayEnabledBox.IsEnabled = false;
        ServicesSpotifyOverlayEnabledBox.ToolTip = "Spotify-Daten werden immer automatisch in die hinterlegte JSON-Datei geschrieben.";
        ServicesSpotifyOverlayEnabledBox.Visibility = Visibility.Visible;
        ServicesSpotifyDataJsonPathBox.Text = string.IsNullOrWhiteSpace(_settings.Overlay.DataFilePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CreatorControlSuite", "Overlay", "data", _settings.Overlay.DataFileName)
            : Environment.ExpandEnvironmentVariables(_settings.Overlay.DataFilePath);
        ServicesSpotifyOverlaySourceBox.Text = string.IsNullOrWhiteSpace(_settings.Spotify.OverlayObsSource) ? "ccs_spotify" : _settings.Spotify.OverlayObsSource;
        ServicesSpotifyOverlaySceneBox.Text = _settings.Spotify.OverlayObsScene;
        ServicesSpotifyShufflePlaylistBox.IsChecked = _settings.Spotify.ShuffleSelectedPlaylist;
        ServicesSpotifyAutoStartOnStreamBox.IsChecked = _settings.Workflow.AutoStartSpotifyPlaylist;
        ServicesSpotifyEndMusicBox.IsChecked = _settings.Workflow.AutoPlayEndMusic;
        ServicesSpotifyPauseOnStreamEndBox.IsChecked = _settings.Workflow.PauseSpotifyOnStreamEnd;
        ServicesSpotifySetLiveVolumeBox.IsChecked = _settings.Spotify.SetVolumeOnLiveTransition;
        ServicesSpotifyLiveVolumeBox.Text = _settings.Spotify.LiveVolumePercent.ToString();
        ServicesSpotifyMuteDuringAlertsBox.IsChecked = _settings.Spotify.MuteDuringAlerts;
        ServicesSpotifyAlertVolumeBox.Text = _settings.Spotify.AlertMuteVolumePercent.ToString();
        ServicesSpotifyAlertFadeOutMsBox.Text = _settings.Spotify.AlertFadeOutMilliseconds.ToString();
        ServicesSpotifyAlertFadeInMsBox.Text = _settings.Spotify.AlertFadeInMilliseconds.ToString();
        ServicesTwitchRaidEnabledBox.IsChecked = _settings.Twitch.RaidOnStreamEnd;
        ServicesTwitchRaidCountdownSecondsBox.Text = Math.Max(1, _settings.Twitch.RaidCountdownSeconds).ToString();
        ServicesTwitchStopStreamAfterRaidBox.IsChecked = _settings.Twitch.StopStreamAfterRaid;
        ServicesTwitchStopSpotifyAfterRaidBox.IsChecked = _settings.Twitch.StopSpotifyAfterRaid;
        ServicesTwitchRaidChannelsBox.Text = string.Join(Environment.NewLine, _settings.Twitch.RaidChannels);
        ServicesTwitchEndSceneSecondsBox.Text = _settings.Workflow.EndSceneSeconds.ToString();
        ServicesTwitchEndFollowerGoalTargetBox.Text = _settings.Twitch.FollowerGoal.Target.ToString("0");
        DashboardRaidEnabledBox.IsChecked = _settings.Twitch.RaidOnStreamEnd;
        RefreshConfiguredDashboardScenes();
        RefreshRaidChannelSelectors();
        UpdateDashboardRaidControlsVisibility();

        GoalOverlaySceneBox.Text = _settings.Obs.GoalOverlayScene;
        FollowerGoalTitleBox.Text = _settings.Twitch.FollowerGoal.Title;
        FollowerGoalCurrentBox.Text = _settings.Twitch.FollowerGoal.Current.ToString("0");
        FollowerGoalTargetBox.Text = _settings.Twitch.FollowerGoal.Target.ToString("0");
        FollowerGoalFontBox.Text = _settings.Twitch.FollowerGoal.FontFace;
        FollowerGoalFontSizeBox.Text = _settings.Twitch.FollowerGoal.FontSize.ToString();
        SubGoalTitleBox.Text = _settings.Twitch.SubGoal.Title;
        SubGoalCurrentBox.Text = _settings.Twitch.SubGoal.Current.ToString("0");
        SubGoalTargetBox.Text = _settings.Twitch.SubGoal.Target.ToString("0");
        SubGoalFontBox.Text = _settings.Twitch.SubGoal.FontFace;
        SubGoalFontSizeBox.Text = _settings.Twitch.SubGoal.FontSize.ToString();
        DonationGoalTitleBox.Text = _settings.Twitch.DonationGoal.Title;
        DonationGoalCurrentBox.Text = _settings.Twitch.DonationGoal.Current.ToString("0.##");
        DonationGoalTargetBox.Text = _settings.Twitch.DonationGoal.Target.ToString("0.##");
        DonationGoalCurrencyBox.Text = _settings.Twitch.DonationGoal.Currency;
        DonationGoalFontBox.Text = _settings.Twitch.DonationGoal.FontFace;
        DonationGoalFontSizeBox.Text = _settings.Twitch.DonationGoal.FontSize.ToString();

        RefreshAlertLibrary();

        AlertTypeBox.SelectedItem =
            _settings.Alerts.Definitions.ContainsKey("Follow")
                ? "Follow"
                : _settings.Alerts.Definitions.Keys.FirstOrDefault();

        AlertObsSceneBox.Text =
            _settings.Alerts.ObsSceneName;

        AlertObsMediaSourceBox.Text =
            _settings.Alerts.ObsMediaSourceName;

        AlertObsTextSourceBox.Text =
            _settings.Alerts.ObsTextSourceName;

        AlertInterDelayBox.Text =
            _settings.Alerts.InterAlertDelayMilliseconds.ToString();

        await LoadSelectedAlertDefinitionAsync();

        UseBundledOverlayBox.IsChecked = _settings.Overlay.UseBundledOverlay;
        OverlayRootBox.Text = _settings.Overlay.RootPath;
        OverlayWidthBox.Text = _settings.Overlay.Width.ToString();
        OverlayHeightBox.Text = _settings.Overlay.Height.ToString();
        EnableLiveStatusWidgetBox.IsChecked = _settings.Overlay.EnableLiveStatusWidget;
        EnableFollowerGoalWidgetBox.IsChecked = _settings.Overlay.EnableFollowerGoal;
        EnableSpotifyWidgetBox.IsChecked = _settings.Overlay.EnableSpotifyWidget;
        EnableEndStatsWidgetBox.IsChecked = _settings.Overlay.EnableEndStatsWidget;
        OverlayStartTextBox.Text = _settings.Overlay.StartText;
        OverlayPauseTextBox.Text = _settings.Overlay.PauseText;
        OverlayEndTextBox.Text = _settings.Overlay.EndText;
        OverlaySharedTextBox.Text = _settings.Overlay.SharedSceneText;
        OverlayFontFamilyBox.Text = _settings.Overlay.FontFamily;
        OverlayFontSizeBox.Text = _settings.Overlay.FontSize.ToString();
        OverlayFontColorBox.Text = _settings.Overlay.FontColor;
        OverlayTimerSecondsBox.Text = _settings.Overlay.StartTimerSeconds.ToString();
        OverlayTimerXBox.Text = _settings.Overlay.TimerX.ToString();
        OverlayTimerYBox.Text = _settings.Overlay.TimerY.ToString();
        SelectComboBoxTag(OverlayFrameStyleBox, _settings.Overlay.FrameStyle);
        OverlayFrameColorBox.Text = _settings.Overlay.FrameColor;
        SelectComboBoxTag(OverlayFrameEffectBox, _settings.Overlay.FrameEffect);
        OverlayObsSceneTargetBox.ItemsSource = new[] { _settings.Obs.StartScene, _settings.Obs.LiveScene, _settings.Obs.PauseScene, "Metaschutz", _settings.Obs.EndScene }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        OverlayObsSceneTargetBox.SelectedIndex = 0;
        OverlayContentTypeBox.SelectedIndex = 0;

        StartSceneBox.Text = _settings.Obs.StartScene;
        LiveSceneBox.Text = _settings.Obs.LiveScene;
        PauseSceneBox.Text = _settings.Obs.PauseScene;
        EndSceneBox.Text = _settings.Obs.EndScene;
        EndSceneSecondsBox.Text = _settings.Workflow.EndSceneSeconds.ToString();
        _liveViewerSampleTimer.Interval = TimeSpan.FromSeconds(
            Math.Clamp(_settings.Workflow.ViewerSampleSeconds, 5, 300));

        StreamDeckEnabledBox.IsChecked = _settings.StreamDeck.Enabled;
        StreamDeckProfileBox.IsChecked = _settings.StreamDeck.AutoInstallProfile;

        AutoUpdateBox.IsChecked = _settings.Updates.AutoCheck;
        BackupBeforeUpdateBox.IsChecked = _settings.Updates.BackupBeforeUpdate;
        SelectUpdateChannelBox(_settings.Updates.Channel);
        InstallUpdateButton.IsEnabled = false;
        _pendingUpdatePackage = null;

        BackupsList.ItemsSource = await _updateService.ListBackupsAsync();
        await RefreshLicenseAsync();

        if (_settings.Updates.AutoCheck)
        {
            await CheckUpdatesAsync(silent: true);
        }

        if (_settings.Obs.AutoConnect)
        {
            await ConnectObsAsync(showErrorDialog: false);
        }

        if (_settings.Twitch.AutoConnect &&
            !string.IsNullOrWhiteSpace(_settings.Twitch.ClientId))
        {
            await ConnectTwitchAsync(showErrorDialog: false);
        }

        if (_settings.Spotify.AutoConnect &&
            !string.IsNullOrWhiteSpace(_settings.Spotify.ClientId))
        {
            await ConnectSpotifyAsync(showErrorDialog: false);
        }
        if (_settings.StreamerBot.AutoConnect)
        {
            await ConnectStreamerBotAsync();
        }

        _loadingSettingsIntoUi = false;
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            _settings.Branding.DisplayName = DisplayNameBox.Text.Trim();
            _settings.Branding.ChannelName = ChannelNameBox.Text.Trim();
            _settings.General.StartWithWindows = StartWithWindowsBox.IsChecked == true;
            _settings.General.MinimizeToTray = MinimizeToTrayBox.IsChecked == true;
            _settings.General.OverlayManifestPath = OverlayManifestPathBox.Text.Trim();

            _settings.Obs.Host = ObsHostBox.Text.Trim();
            _settings.Obs.Port = int.Parse(ObsPortBox.Text.Trim());
            _settings.Obs.AutoConnect = ObsAutoConnectBox.IsChecked == true;
            _settings.Obs.ConnectOnPrepare = ObsConnectOnPrepareBox.IsChecked == true;
            _settings.Obs.ExecutablePath = ObsExecutablePathBox.Text.Trim();
            _settings.Obs.StartScene = StartSceneBox.Text.Trim();
            _settings.Obs.LiveScene = LiveSceneBox.Text.Trim();
            _settings.Obs.PauseScene = PauseSceneBox.Text.Trim();
            _settings.Obs.EndScene = EndSceneBox.Text.Trim();

            _settings.Twitch.ClientId = TwitchClientIdBox.Text.Trim();
            _settings.Twitch.ChannelName = TwitchChannelBox.Text.Trim();
            _settings.Twitch.AutoConnect = TwitchAutoConnectBox.IsChecked == true;
            _settings.Twitch.ConnectOnPrepare = TwitchConnectOnPrepareBox.IsChecked == true;
            _settings.Twitch.CreatorDashboardUrl = TwitchCreatorDashboardUrlBox.Text.Trim();
            _settings.Twitch.EnableChat = TwitchChatEnabledBox.IsChecked == true;
            _settings.Twitch.EnableEventSub = TwitchEventSubEnabledBox.IsChecked == true;

            _settings.Spotify.ClientId = SpotifyClientIdBox.Text.Trim();
            _settings.Spotify.RedirectUri = SpotifyRedirectUriBox.Text.Trim();
            _settings.Spotify.AutoConnect = SpotifyAutoConnectBox.IsChecked == true;
            _settings.Spotify.ConnectOnPrepare = SpotifyConnectOnPrepareBox.IsChecked == true;
            _settings.Spotify.ExecutablePath = SpotifyExecutablePathBox.Text.Trim();

            // Der im Spotify-Bereich eingetragene Laufzeit-JSON-Pfad muss auch beim
            // allgemeinen Speichern erhalten bleiben. Sonst schreibt der laufende
            // Spotify-Refresh weiter in die zuvor konfigurierte Standarddatei.
            var spotifyDataPath = ServicesSpotifyDataJsonPathBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(spotifyDataPath))
            {
                spotifyDataPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(spotifyDataPath));
                if (!string.Equals(Path.GetExtension(spotifyDataPath), ".json", StringComparison.OrdinalIgnoreCase))
                    spotifyDataPath += ".json";
                _settings.Overlay.DataFilePath = spotifyDataPath;
                _settings.Overlay.DataFileName = Path.GetFileName(spotifyDataPath);
            }

            _settings.StreamerBot.Host = StreamerBotHostBox.Text.Trim();
            _settings.StreamerBot.Port = int.Parse(StreamerBotPortBox.Text.Trim());
            _settings.StreamerBot.Endpoint = StreamerBotEndpointBox.Text.Trim();
            _settings.StreamerBot.Password = StreamerBotPasswordBox.Password;
            _settings.StreamerBot.AutoConnect = StreamerBotAutoConnectBox.IsChecked == true;
            _settings.StreamerBot.ConnectOnPrepare = StreamerBotConnectOnPrepareBox.IsChecked == true;
            _settings.StreamerBot.ExecutablePath = StreamerBotExecutablePathBox.Text.Trim();
            _settings.Alerts.Enabled = SuiteAlertsEnabledBox.IsChecked == true;
            _settings.StreamerBot.SuppressAlertActionsWhenSuiteAlertsEnabled = SuppressStreamerBotAlertsBox.IsChecked == true;
            _settings.StreamerBot.DisableAlertsActionName = GetStreamerBotActionName(StreamerBotDisableAlertsActionBox, SettingsStreamerBotDisableAlertsActionBox, "CCS Alerts deaktivieren");
            _settings.StreamerBot.EnableAlertsActionName = GetStreamerBotActionName(StreamerBotEnableAlertsActionBox, SettingsStreamerBotEnableAlertsActionBox, "CCS Alerts aktivieren");
            _settings.StreamerBot.DisableAlertsActionId = GetStreamerBotActionId(StreamerBotDisableAlertsActionBox, SettingsStreamerBotDisableAlertsActionBox);
            _settings.StreamerBot.EnableAlertsActionId = GetStreamerBotActionId(StreamerBotEnableAlertsActionBox, SettingsStreamerBotEnableAlertsActionBox);
            SyncStreamerBotActionSelectorText();
            _settings.Spotify.StartVolumePercent = int.Parse(SpotifyVolumeBox.Text.Trim());
            _settings.Spotify.FadeOutEnabled = SpotifyFadeOutBox.IsChecked == true;
            _settings.Spotify.PauseAfterFadeOut = SpotifyPauseAfterFadeBox.IsChecked == true;
            _settings.Spotify.FadeOutSeconds = int.Parse(SpotifyFadeOutSecondsBox.Text.Trim());
            _settings.Spotify.FadeInSeconds = int.Parse(SpotifyFadeInSecondsBox.Text.Trim());
            _settings.Spotify.OverlayShowTitle = true;
            _settings.Spotify.OverlayShowArtist = true;
            _settings.Spotify.OverlayShowAlbumCover = true;
            _settings.Spotify.OverlayShowProgress = true;
            _settings.Spotify.OverlayHideWhenPaused = ServicesSpotifyHidePausedBox.IsChecked == true;
            _settings.Spotify.OverlayHideWhenMuted = ServicesSpotifyHideMutedBox.IsChecked == true;
            _settings.Spotify.OverlayMuteDetectionObsSource = ServicesSpotifyDetectObsMuteBox.IsChecked == true;
            _settings.Spotify.OverlayMuteDetectionSpotifyVolume = ServicesSpotifyDetectVolumeMuteBox.IsChecked == true;
            _settings.Spotify.OverlayObsAudioSource = ServicesSpotifyObsAudioSourceBox.Text?.Trim() ?? "Spotify";
            _settings.Spotify.OverlayEnabled = true;
            ApplySpotifyAutomationFieldsToSettings();
            ApplyTwitchEndFieldsToSettings();
            ApplyTwitchGoalFieldsToSettings();

            if (SpotifyDeviceBox.SelectedItem is SpotifyDevice selectedDevice)
            {
                _settings.Spotify.PreferredDeviceId = selectedDevice.Id;
            }

            if (SpotifyPlaylistBox.SelectedItem is SpotifyPlaylist selectedPlaylist)
            {
                _settings.Spotify.StartPlaylistUri = selectedPlaylist.Uri;
            }

            _settings.Alerts.ObsSceneName =
                AlertObsSceneBox.Text.Trim();

            _settings.Alerts.ObsMediaSourceName =
                AlertObsMediaSourceBox.Text.Trim();

            _settings.Alerts.ObsTextSourceName =
                AlertObsTextSourceBox.Text.Trim();

            _settings.Alerts.InterAlertDelayMilliseconds =
                int.Parse(AlertInterDelayBox.Text.Trim());

            SaveAlertDefinitionToSettings();

            _settings.Overlay.UseBundledOverlay = UseBundledOverlayBox.IsChecked == true;
            _settings.Overlay.RootPath = OverlayRootBox.Text.Trim();
            _settings.Overlay.Width = int.Parse(OverlayWidthBox.Text.Trim());
            _settings.Overlay.Height = int.Parse(OverlayHeightBox.Text.Trim());
            _settings.Overlay.EnableLiveStatusWidget = EnableLiveStatusWidgetBox.IsChecked == true;
            _settings.Overlay.EnableFollowerGoal = EnableFollowerGoalWidgetBox.IsChecked == true;
            _settings.Overlay.EnableSpotifyWidget = EnableSpotifyWidgetBox.IsChecked == true;
            _settings.Overlay.EnableEndStatsWidget = EnableEndStatsWidgetBox.IsChecked == true;
            _settings.Overlay.StartText = OverlayStartTextBox.Text.Trim();
            _settings.Overlay.PauseText = OverlayPauseTextBox.Text.Trim();
            _settings.Overlay.EndText = OverlayEndTextBox.Text.Trim();
            _settings.Overlay.SharedSceneText = OverlaySharedTextBox.Text.Trim();
            _settings.Overlay.FontFamily = OverlayFontFamilyBox.Text.Trim();
            _settings.Overlay.FontSize = int.Parse(OverlayFontSizeBox.Text.Trim());
            _settings.Overlay.FontColor = OverlayFontColorBox.Text.Trim();
            _settings.Overlay.StartTimerSeconds = int.Parse(OverlayTimerSecondsBox.Text.Trim());
            _settings.Overlay.TimerX = int.Parse(OverlayTimerXBox.Text.Trim());
            _settings.Overlay.TimerY = int.Parse(OverlayTimerYBox.Text.Trim());
            _settings.Overlay.FrameStyle = (OverlayFrameStyleBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Solid";
            _settings.Overlay.FrameColor = OverlayFrameColorBox.Text.Trim();
            _settings.Overlay.FrameEffect = (OverlayFrameEffectBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Glow";
            await WriteOverlayConfigurationAsync();

            _settings.Workflow.EndSceneSeconds = int.Parse(EndSceneSecondsBox.Text.Trim());

            _settings.StreamDeck.Enabled = StreamDeckEnabledBox.IsChecked == true;
            _settings.StreamDeck.AutoInstallProfile = StreamDeckProfileBox.IsChecked == true;

            _settings.Updates.AutoCheck = AutoUpdateBox.IsChecked == true;
            _settings.Updates.BackupBeforeUpdate = BackupBeforeUpdateBox.IsChecked == true;
            _settings.Updates.Channel = GetSelectedUpdateChannel();
            _settings.Product.UpdateChannel = _settings.Updates.Channel;
            _settings.Product.Version = GetCurrentProductVersion();

            var validation = _settingsValidator.Validate(_settings);

            if (!validation.IsValid)
            {
                var firstError = validation.Issues.First(
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

            RefreshConfiguredDashboardScenes();
            RefreshRaidChannelSelectors();

            await _settingsStore.SaveAsync(_settings);

            await _secretStore.SaveAsync("obs.password", ObsPasswordBox.Password);

            _appLogger.Write(
                AppLogLevel.Information,
                "Settings",
                "Einstellungen wurden gespeichert.");

            SettingsStatusText.Text = "Einstellungen gespeichert.";
            SettingsStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            SettingsStatusText.Text = exception.Message;
            SettingsStatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
        }
    }

    private static IReadOnlyList<string> GetDefaultDashboardModuleOrder() =>
    [
        "ConnectionStatus",
        "StreamStatistics",
        "ObsSceneControl",
        "StreamControl",
        "QuickServices",
        "SpotifyPlayer",
        "TwitchChat",
        "Workflow",
        "Preflight",
        "Scenes",
        "RaidControl",
        "RaidAssistant",
        "Notifications",
        "TwitchEvents",
        "Automation",
        "LiveEvents",
        "SystemResources",
        "StreamHistory",
        "AudioMixer",
        "TwitchUsers",
        "StreamDeckRemote",
        "AdvancedShortcuts",
        "WorkflowStatus",
    ];

    private static string GetDashboardModuleDisplayName(string key) => key switch
    {
        "ConnectionStatus" => "Verbindungsstatus",
        "StreamControl" => "Streamsteuerung",
        "WorkflowStatus" => "Workflow-Status",
        "ObsSceneControl" => "OBS · Szene",
        "Notifications" => "Notification Center",
        "QuickServices" => "Dienste",
        "RaidControl" => "Raid beim Streamende",
        "Workflow" => "Workflow",
        "Preflight" => "Preflight",
        "Scenes" => "Szenen-Schnellwahl",
        "AudioMixer" => "OBS Audiomixer",
        "RaidAssistant" => "Raid-Assistent",
        "TwitchChat" => "Twitch Chat",
        "TwitchUsers" => "Twitch User",
        "TwitchEvents" => "Letzte Twitch-Events",
        "SpotifyPlayer" => "Spotify Player",
        "StreamStatistics" => "Stream-Statistik",
        "SystemResources" => "Systemressourcen",
        "StreamDeckRemote" => "Stream Deck & Remote",
        "AdvancedShortcuts" => "Dashboard-Schnellzugriffe",
        "Automation" => "Nächste Automatisierungen",
        "LiveEvents" => "Letzte Events",
        "StreamHistory" => "Stream-Historie",
        _ => key
    };

    private string? GetDashboardModuleKeyFromDisplayName(string displayName)
    {
        return GetDefaultDashboardModuleOrder()
            .FirstOrDefault(key =>
                string.Equals(
                    GetDashboardModuleDisplayName(key),
                    displayName,
                    StringComparison.Ordinal));
    }

    private void NormalizeDashboardModuleOrder()
    {
        var validKeys = GetDefaultDashboardModuleOrder();
        var normalized = (_settings.Dashboard.ModuleOrder ?? [])
            .Where(key => validKeys.Contains(key, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var key in validKeys)
        {
            if (!normalized.Contains(key, StringComparer.Ordinal))
            {
                normalized.Add(key);
            }
        }

        _settings.Dashboard.ModuleOrder = normalized;
        _settings.Dashboard.HiddenModules ??= [];
        _settings.Dashboard.ModuleWidths ??=
            new Dictionary<string, double>(StringComparer.Ordinal);
        _settings.Dashboard.ModuleHeights ??=
            new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var key in validKeys)
        {
            if (!_settings.Dashboard.ModuleWidths.ContainsKey(key))
            {
                _settings.Dashboard.ModuleWidths[key] = 320;
            }

            if (!_settings.Dashboard.ModuleHeights.ContainsKey(key))
            {
                _settings.Dashboard.ModuleHeights[key] = 180;
            }
        }
    }

    private void LoadDashboardModuleOrderEditor()
    {
        NormalizeDashboardModuleOrder();
        _dashboardModuleOrderItems.Clear();

        foreach (var key in _settings.Dashboard.ModuleOrder)
        {
            _dashboardModuleOrderItems.Add(GetDashboardModuleDisplayName(key));
        }
    }

    private void SaveDashboardModuleOrderFromEditor()
    {
        var keys = _dashboardModuleOrderItems
            .Select(GetDashboardModuleKeyFromDisplayName)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Cast<string>()
            .ToList();

        _settings.Dashboard.ModuleOrder = keys;
        NormalizeDashboardModuleOrder();
    }

    private string? GetDashboardModuleKey(FrameworkElement element)
    {
        foreach (var key in GetDefaultDashboardModuleOrder())
        {
            if (ReferenceEquals(GetDashboardModuleElement(key), element))
            {
                return key;
            }
        }

        return null;
    }

    private FrameworkElement? GetDashboardModuleElement(string key) => key switch
    {
        "ConnectionStatus" => DashboardServiceStatusSection,
        "StreamControl" => DashboardStreamControlModule,
        "WorkflowStatus" => DashboardWorkflowStatusModule,
        "ObsSceneControl" => DashboardObsSceneControlModule,
        "Notifications" => DashboardNotificationCenterModule,
        "QuickServices" => DashboardQuickServicesSection,
        "RaidControl" => DashboardRaidControlModule,
        "Workflow" => DashboardWorkflowRailSection,
        "Preflight" => DashboardPreflightModule,
        "Scenes" => DashboardScenesModule,
        "AudioMixer" => DashboardAudioMixerModule,
        "RaidAssistant" => DashboardRaidAssistantModule,
        "TwitchChat" => DashboardTwitchChatModule,
        "TwitchUsers" => DashboardTwitchUsersModule,
        "TwitchEvents" => DashboardTwitchEventsModule,
        "SpotifyPlayer" => DashboardSpotifyPlayerModule,
        "StreamStatistics" => DashboardStreamStatisticsModule,
        "SystemResources" => DashboardSystemResourcesModule,
        "StreamDeckRemote" => DashboardStreamDeckRemoteModule,
        "AdvancedShortcuts" => DashboardAdvancedShortcutsModule,
        "Automation" => DashboardAutomationModule,
        "LiveEvents" => DashboardLiveEventsModule,
        "StreamHistory" => DashboardStreamHistorySection,
        _ => null
    };

    private void ApplyDashboardModuleOrder()
    {
        // 2.0.141: the approved dashboard reference uses a fixed static layout.
        // Existing controls stay in their XAML positions and are not reparented.
    }

    private void RemoveDashboardElementFromCurrentParent(
        FrameworkElement element)
    {
        if (element.Parent is Panel panel)
        {
            panel.Children.Remove(element);
        }
    }

    private void MoveDashboardModuleEditorItem(int direction)
    {
        var index = DashboardModuleOrderList.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        var targetIndex = index + direction;
        if (targetIndex < 0 || targetIndex >= _dashboardModuleOrderItems.Count)
        {
            return;
        }

        var item = _dashboardModuleOrderItems[index];
        _dashboardModuleOrderItems.RemoveAt(index);
        _dashboardModuleOrderItems.Insert(targetIndex, item);
        DashboardModuleOrderList.SelectedIndex = targetIndex;

        SaveDashboardModuleOrderFromEditor();
        ApplyDashboardModuleOrder();
    }

    private void DashboardModuleOrderList_PreviewMouseLeftButtonDown(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        _dashboardModuleDragStart = e.GetPosition(DashboardModuleOrderList);
        _dashboardDraggedModuleName =
            FindListBoxItemTextFromPoint(DashboardModuleOrderList, _dashboardModuleDragStart);
    }

    private void DashboardModuleOrderList_PreviewMouseMove(
        object sender,
        System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed ||
            string.IsNullOrWhiteSpace(_dashboardDraggedModuleName))
        {
            return;
        }

        var current = e.GetPosition(DashboardModuleOrderList);
        if (Math.Abs(current.X - _dashboardModuleDragStart.X) <
                SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dashboardModuleDragStart.Y) <
                SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(
            DashboardModuleOrderList,
            _dashboardDraggedModuleName,
            DragDropEffects.Move);
    }

    private void DashboardModuleOrderList_Drop(
        object sender,
        DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.StringFormat))
        {
            return;
        }

        var dragged = e.Data.GetData(DataFormats.StringFormat) as string;
        if (string.IsNullOrWhiteSpace(dragged))
        {
            return;
        }

        var target =
            FindListBoxItemTextFromPoint(
                DashboardModuleOrderList,
                e.GetPosition(DashboardModuleOrderList));

        var oldIndex = _dashboardModuleOrderItems.IndexOf(dragged);
        var targetIndex = string.IsNullOrWhiteSpace(target)
            ? _dashboardModuleOrderItems.Count - 1
            : _dashboardModuleOrderItems.IndexOf(target);

        if (oldIndex < 0 || targetIndex < 0 || oldIndex == targetIndex)
        {
            return;
        }

        _dashboardModuleOrderItems.RemoveAt(oldIndex);
        if (oldIndex < targetIndex)
        {
            targetIndex--;
        }

        targetIndex = Math.Clamp(targetIndex, 0, _dashboardModuleOrderItems.Count);
        _dashboardModuleOrderItems.Insert(targetIndex, dragged);
        DashboardModuleOrderList.SelectedItem = dragged;

        SaveDashboardModuleOrderFromEditor();
        ApplyDashboardModuleOrder();
    }

    private static string? FindListBoxItemTextFromPoint(
        System.Windows.Controls.ListBox listBox,
        Point point)
    {
        var element = listBox.InputHitTest(point) as DependencyObject;

        while (element is not null &&
               element is not System.Windows.Controls.ListBoxItem)
        {
            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }

        return (element as System.Windows.Controls.ListBoxItem)?.Content?.ToString();
    }

    private void RegisterDashboardDirectDragHandlers()
    {
        // Fixed reference dashboard: module dragging is disabled.
    }

    private ContextMenu BuildDashboardModuleContextMenu(string key)
    {
        var menu = new ContextMenu();

        var hideItem = new MenuItem
        {
            Header = "Modul ausblenden"
        };
        hideItem.Click += (_, _) =>
            SetDashboardModuleHidden(key, true);
        menu.Items.Add(hideItem);

        return menu;
    }

    private void SetDashboardModuleHidden(string key, bool hidden)
    {
        _settings.Dashboard.HiddenModules ??= [];

        _settings.Dashboard.HiddenModules.RemoveAll(
            item => string.Equals(item, key, StringComparison.Ordinal));

        if (hidden)
        {
            _settings.Dashboard.HiddenModules.Add(key);
        }

        ApplyDashboardModuleOrder();
        LoadDashboardModuleOrderEditor();
        _ = _settingsStore.SaveAsync(_settings);
    }

    private void RestoreAllDashboardModules()
    {
        _settings.Dashboard.HiddenModules = [];
        ApplyDashboardModuleOrder();
        LoadDashboardModuleOrderEditor();
        _ = _settingsStore.SaveAsync(_settings);
    }

    private void ToggleDashboardLayoutEditMode()
    {
        _dashboardLayoutEditMode = false;
        AddDashboardNotification(
            "Das Dashboard verwendet die feste Referenz-Anordnung.",
            "Info");
    }

    private void DashboardSection_PreviewMouseLeftButtonDown(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_dashboardLayoutEditMode || sender is not FrameworkElement element)
        {
            return;
        }

        _dashboardDraggedSection = element;
        _dashboardDirectDragStart = e.GetPosition(DashboardContentStack);
        SelectDashboardSectionForSizing(element);

        element.CaptureMouse();
        e.Handled = true;
    }

    private void DashboardSection_PreviewMouseMove(
        object sender,
        System.Windows.Input.MouseEventArgs e)
    {
        if (!_dashboardLayoutEditMode ||
            e.LeftButton != System.Windows.Input.MouseButtonState.Pressed ||
            _dashboardDraggedSection is null)
        {
            return;
        }

        var current = e.GetPosition(DashboardContentStack);

        if (Math.Abs(current.X - _dashboardDirectDragStart.X) <
                SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dashboardDirectDragStart.Y) <
                SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        MoveDashboardSectionToPointer(_dashboardDraggedSection, current);
        _dashboardDirectDragStart = current;
        e.Handled = true;
    }

    private void MoveDashboardSectionToPointer(
        FrameworkElement dragged,
        Point pointer)
    {
        if (!DashboardContentStack.Children.Contains(dragged))
        {
            return;
        }

        var currentIndex =
            DashboardContentStack.Children.IndexOf(dragged);

        var pointerPosition =
            System.Windows.Input.Mouse.GetPosition(
                DashboardContentStack);

        var targetIndex = currentIndex;
        var bestDistance = double.MaxValue;

        for (var index = 0;
             index < DashboardContentStack.Children.Count;
             index++)
        {
            if (DashboardContentStack.Children[index]
                    is not FrameworkElement candidate ||
                ReferenceEquals(candidate, dragged) ||
                candidate.Visibility != Visibility.Visible)
            {
                continue;
            }

            var topLeft =
                candidate.TranslatePoint(
                    new Point(0, 0),
                    DashboardContentStack);

            var centerX =
                topLeft.X + candidate.ActualWidth / 2;
            var centerY =
                topLeft.Y + candidate.ActualHeight / 2;

            var deltaX = pointerPosition.X - centerX;
            var deltaY = pointerPosition.Y - centerY;
            var distance =
                deltaX * deltaX + deltaY * deltaY;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                targetIndex = index;
            }
        }

        if (targetIndex == currentIndex)
        {
            return;
        }

        DashboardContentStack.Children.RemoveAt(currentIndex);

        if (currentIndex < targetIndex)
        {
            targetIndex--;
        }

        targetIndex = Math.Clamp(
            targetIndex,
            0,
            DashboardContentStack.Children.Count);

        DashboardContentStack.Children.Insert(
            targetIndex,
            dragged);
    }

    private void DashboardSection_PreviewMouseLeftButtonUp(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        FinishDashboardDirectDrag(sender as FrameworkElement);
        e.Handled = true;
    }

    private void DashboardSection_LostMouseCapture(
        object sender,
        System.Windows.Input.MouseEventArgs e)
    {
        if (_dashboardDraggedSection is not null)
        {
            FinishDashboardDirectDrag(sender as FrameworkElement);
        }
    }

    private void FinishDashboardDirectDrag(FrameworkElement? element)
    {
        if (_dashboardDraggedSection is null)
        {
            return;
        }

        _dashboardDraggedSection = null;

        if (element?.IsMouseCaptured == true)
        {
            element.ReleaseMouseCapture();
        }

        SaveDashboardModuleOrderFromVisualTree();
        LoadDashboardModuleOrderEditor();
        _ = _settingsStore.SaveAsync(_settings);
    }

    private void DashboardContentStack_DragOver(
        object sender,
        DragEventArgs e)
    {
        if (!_dashboardLayoutEditMode ||
            !e.Data.GetDataPresent(typeof(FrameworkElement)))
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void DashboardContentStack_Drop(
        object sender,
        DragEventArgs e)
    {
        if (!_dashboardLayoutEditMode)
        {
            return;
        }

        var dragged =
            e.Data.GetData(typeof(FrameworkElement))
                as FrameworkElement;

        if (dragged is null ||
            !DashboardContentStack.Children.Contains(dragged))
        {
            return;
        }

        MoveDashboardSectionToPointer(
            dragged,
            e.GetPosition(DashboardContentStack));

        SaveDashboardModuleOrderFromVisualTree();
        LoadDashboardModuleOrderEditor();
        _ = _settingsStore.SaveAsync(_settings);
        e.Handled = true;
    }

    private void SaveDashboardModuleOrderFromVisualTree()
    {
        var order = new List<string>();

        foreach (var child in DashboardContentStack.Children)
        {
            if (child is not FrameworkElement element)
            {
                continue;
            }

            var key = GetDashboardModuleKey(element);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            order.Add(key);
        }

        foreach (var key in GetDefaultDashboardModuleOrder())
        {
            if (!order.Contains(key, StringComparer.Ordinal))
            {
                order.Add(key);
            }
        }

        _settings.Dashboard.ModuleOrder = order;
    }

    private void EnterDashboardFocusMode()
    {
        if (_dashboardFocusModeActive)
        {
            return;
        }

        NormalizeDashboardModuleOrder();
        NormalizeDashboardModuleSizes();

        _dashboardPreFocusOrder =
            _settings.Dashboard.ModuleOrder.ToList();
        _dashboardPreFocusSizes =
            new Dictionary<string, string>(
                _settings.Dashboard.ModuleSizes,
                StringComparer.Ordinal);
        _dashboardPreFocusVisibility =
            new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["ServiceStatus"] = _settings.Dashboard.ShowServiceStatus,
                ["StreamControls"] = _settings.Dashboard.ShowStreamControls,
                ["LivePanels"] = _settings.Dashboard.ShowLivePanels,
                ["QuickServices"] = _settings.Dashboard.ShowQuickServices,
                ["WorkflowRail"] = _settings.Dashboard.ShowWorkflowRail,
                ["AdvancedTools"] = _settings.Dashboard.ShowAdvancedTools,
                ["Notifications"] = _settings.Dashboard.ShowNotifications,
                ["StreamHistory"] = _settings.Dashboard.ShowStreamHistory
            };

        _settings.Dashboard.ModuleOrder =
        [
            "ServiceStatus",
            "StreamControls",
            "LivePanels",
            "AdvancedTools",
            "WorkflowRail",
            "Notifications",
            "QuickServices",
            "StreamHistory"
        ];

        _settings.Dashboard.ShowServiceStatus = true;
        _settings.Dashboard.ShowStreamControls = true;
        _settings.Dashboard.ShowLivePanels = true;
        _settings.Dashboard.ShowAdvancedTools = true;
        _settings.Dashboard.ShowWorkflowRail = true;
        _settings.Dashboard.ShowNotifications = false;
        _settings.Dashboard.ShowQuickServices = false;
        _settings.Dashboard.ShowStreamHistory = false;

        _settings.Dashboard.ModuleSizes["ServiceStatus"] = "Standard";
        _settings.Dashboard.ModuleSizes["StreamControls"] = "Groß";
        _settings.Dashboard.ModuleSizes["LivePanels"] = "Groß";
        _settings.Dashboard.ModuleSizes["AdvancedTools"] = "Groß";
        _settings.Dashboard.ModuleSizes["WorkflowRail"] = "Standard";

        _dashboardFocusModeActive = true;
        DashboardFocusModeButton.Content = "FOKUS BEENDEN";

        ApplyDashboardModuleOrder();
        ApplyDashboardModuleSizes();
        ApplyDashboardLayout();

        AddDashboardNotification(
            "Stream-Fokusmodus aktiviert. Das Dashboard zeigt jetzt nur die wichtigsten Live-Bereiche.",
            "Info");
    }

    private void ExitDashboardFocusMode()
    {
        if (!_dashboardFocusModeActive)
        {
            return;
        }

        if (_dashboardPreFocusOrder is not null)
        {
            _settings.Dashboard.ModuleOrder =
                _dashboardPreFocusOrder.ToList();
        }

        if (_dashboardPreFocusSizes is not null)
        {
            _settings.Dashboard.ModuleSizes =
                new Dictionary<string, string>(
                    _dashboardPreFocusSizes,
                    StringComparer.Ordinal);
        }

        if (_dashboardPreFocusVisibility is not null)
        {
            _settings.Dashboard.ShowServiceStatus =
                _dashboardPreFocusVisibility["ServiceStatus"];
            _settings.Dashboard.ShowStreamControls =
                _dashboardPreFocusVisibility["StreamControls"];
            _settings.Dashboard.ShowLivePanels =
                _dashboardPreFocusVisibility["LivePanels"];
            _settings.Dashboard.ShowQuickServices =
                _dashboardPreFocusVisibility["QuickServices"];
            _settings.Dashboard.ShowWorkflowRail =
                _dashboardPreFocusVisibility["WorkflowRail"];
            _settings.Dashboard.ShowAdvancedTools =
                _dashboardPreFocusVisibility["AdvancedTools"];
            _settings.Dashboard.ShowNotifications =
                _dashboardPreFocusVisibility["Notifications"];
            _settings.Dashboard.ShowStreamHistory =
                _dashboardPreFocusVisibility["StreamHistory"];
        }

        _dashboardFocusModeActive = false;
        DashboardFocusModeButton.Content = "FOKUSMODUS";

        ApplyDashboardModuleOrder();
        ApplyDashboardModuleSizes();
        ApplyDashboardLayout();

        AddDashboardNotification(
            "Stream-Fokusmodus beendet. Das vorherige Dashboard-Layout wurde wiederhergestellt.",
            "Info");
    }

    private void ApplySelectedDashboardPreset(
        System.Windows.Controls.ComboBox source)
    {
        if (source.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
        {
            return;
        }

        var preset = item.Content?.ToString() ?? "Command Center";
        ApplyDashboardPreset(preset);

        DashboardPresetBox.SelectedIndex = source.SelectedIndex;
        DashboardQuickPresetBox.SelectedIndex = source.SelectedIndex;

        LoadDashboardModuleOrderEditor();
        ApplyDashboardModuleOrder();
        ApplyDashboardModuleSizes();
        ApplyDashboardLayout();

        AddDashboardNotification(
            $"Dashboard-Preset „{preset}“ wurde angewendet.",
            "Info");
    }

    private void ApplyDashboardPreset(string preset)
    {
        _settings.Dashboard.ModuleOrder =
            GetDefaultDashboardModuleOrder().ToList();

        _settings.Dashboard.ShowServiceStatus = true;
        _settings.Dashboard.ShowStreamControls = true;
        _settings.Dashboard.ShowLivePanels = true;
        _settings.Dashboard.ShowQuickServices = true;
        _settings.Dashboard.ShowWorkflowRail = true;
        _settings.Dashboard.ShowAdvancedTools = true;
        _settings.Dashboard.ShowNotifications = true;
        _settings.Dashboard.ShowStreamHistory = true;

        NormalizeDashboardModuleSizes();

        switch (preset)
        {
            case "Kompakt":
                foreach (var key in GetDefaultDashboardModuleOrder())
                {
                    _settings.Dashboard.ModuleSizes[key] = "Kompakt";
                }

                _settings.Dashboard.ModuleSizes["LivePanels"] = "Standard";
                _settings.Dashboard.ModuleSizes["AdvancedTools"] = "Standard";
                break;

            case "Twitch Fokus":
                _settings.Dashboard.ModuleOrder =
                [
                    "ServiceStatus",
                    "StreamControls",
                    "LivePanels",
                    "WorkflowRail",
                    "Notifications",
                    "AdvancedTools",
                    "QuickServices",
                    "StreamHistory"
                ];
                _settings.Dashboard.ModuleSizes["LivePanels"] = "Groß";
                _settings.Dashboard.ModuleSizes["StreamControls"] = "Groß";
                _settings.Dashboard.ModuleSizes["Notifications"] = "Standard";
                _settings.Dashboard.ShowQuickServices = false;
                break;

            case "OBS Fokus":
                _settings.Dashboard.ModuleOrder =
                [
                    "ServiceStatus",
                    "StreamControls",
                    "AdvancedTools",
                    "WorkflowRail",
                    "LivePanels",
                    "QuickServices",
                    "Notifications",
                    "StreamHistory"
                ];
                _settings.Dashboard.ModuleSizes["AdvancedTools"] = "Groß";
                _settings.Dashboard.ModuleSizes["StreamControls"] = "Groß";
                _settings.Dashboard.ShowStreamHistory = false;
                break;

            case "Minimal":
                _settings.Dashboard.ShowLivePanels = false;
                _settings.Dashboard.ShowQuickServices = false;
                _settings.Dashboard.ShowWorkflowRail = false;
                _settings.Dashboard.ShowNotifications = false;
                _settings.Dashboard.ShowStreamHistory = false;
                _settings.Dashboard.ModuleSizes["ServiceStatus"] = "Standard";
                _settings.Dashboard.ModuleSizes["StreamControls"] = "Groß";
                _settings.Dashboard.ModuleSizes["AdvancedTools"] = "Standard";
                break;

            default:
                _settings.Dashboard.ModuleSizes["ServiceStatus"] = "Standard";
                _settings.Dashboard.ModuleSizes["StreamControls"] = "Standard";
                _settings.Dashboard.ModuleSizes["LivePanels"] = "Groß";
                _settings.Dashboard.ModuleSizes["QuickServices"] = "Standard";
                _settings.Dashboard.ModuleSizes["WorkflowRail"] = "Groß";
                _settings.Dashboard.ModuleSizes["AdvancedTools"] = "Groß";
                _settings.Dashboard.ModuleSizes["Notifications"] = "Standard";
                _settings.Dashboard.ModuleSizes["StreamHistory"] = "Groß";
                break;
        }

        DashboardShowServiceStatusBox.IsChecked =
            _settings.Dashboard.ShowServiceStatus;
        DashboardShowStreamControlsBox.IsChecked =
            _settings.Dashboard.ShowStreamControls;
        DashboardShowLivePanelsBox.IsChecked =
            _settings.Dashboard.ShowLivePanels;
        DashboardShowQuickServicesBox.IsChecked =
            _settings.Dashboard.ShowQuickServices;
        DashboardShowWorkflowRailBox.IsChecked =
            _settings.Dashboard.ShowWorkflowRail;
        DashboardShowAdvancedToolsBox.IsChecked =
            _settings.Dashboard.ShowAdvancedTools;
        DashboardShowNotificationsBox.IsChecked =
            _settings.Dashboard.ShowNotifications;
        DashboardShowStreamHistoryBox.IsChecked =
            _settings.Dashboard.ShowStreamHistory;
    }

    private void NormalizeDashboardModuleSizes()
    {
        _settings.Dashboard.ModuleSizes ??=
            new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var key in GetDefaultDashboardModuleOrder())
        {
            if (!_settings.Dashboard.ModuleSizes.TryGetValue(key, out var size) ||
                size is not ("Kompakt" or "Standard" or "Groß"))
            {
                _settings.Dashboard.ModuleSizes[key] =
                    key is "LivePanels" or "WorkflowRail" or "AdvancedTools" or "StreamHistory"
                        ? "Groß"
                        : "Standard";
            }
        }
    }

    private void ApplyDashboardModuleSizes()
    {
        NormalizeDashboardModuleSizes();

        foreach (var key in GetDefaultDashboardModuleOrder())
        {
            var element = GetDashboardModuleElement(key);
            if (element is null)
            {
                continue;
            }

            // Das Dashboard verwendet ein festes, links verankertes Referenzlayout.
            // Die frühere Größenlogik zentrierte einzelne Module bei schmalen Fenstern
            // und erzeugte dadurch einen großen leeren Bereich neben der Navigation.
            element.Width = double.NaN;
            element.MinWidth = 0;
            element.MaxWidth = double.PositiveInfinity;
            element.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
    }

    private void RefreshDashboardModuleSizeEditor()
    {
        if (DashboardModuleOrderList.SelectedItem is not string displayName)
        {
            DashboardModuleSizeBox.SelectedIndex = -1;
            return;
        }

        var key = GetDashboardModuleKeyFromDisplayName(displayName);
        if (string.IsNullOrWhiteSpace(key))
        {
            DashboardModuleSizeBox.SelectedIndex = -1;
            return;
        }

        NormalizeDashboardModuleSizes();
        var size = _settings.Dashboard.ModuleSizes[key];

        foreach (var item in DashboardModuleSizeBox.Items
                     .OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (string.Equals(
                    item.Content?.ToString(),
                    size,
                    StringComparison.Ordinal))
            {
                DashboardModuleSizeBox.SelectedItem = item;
                break;
            }
        }
    }

    private void ApplySelectedDashboardModuleSizeFromSettingsEditor()
    {
        if (DashboardModuleOrderList.SelectedItem is not string displayName ||
            DashboardModuleSizeBox.SelectedItem is not System.Windows.Controls.ComboBoxItem sizeItem)
        {
            return;
        }

        var key = GetDashboardModuleKeyFromDisplayName(displayName);
        var size = sizeItem.Content?.ToString();

        if (string.IsNullOrWhiteSpace(key) ||
            size is not ("Kompakt" or "Standard" or "Groß"))
        {
            return;
        }

        NormalizeDashboardModuleSizes();
        _settings.Dashboard.ModuleSizes[key] = size;
        ApplyDashboardModuleSizes();

        AddDashboardNotification(
            $"{displayName}: Größe auf {size} gesetzt.",
            "Info");
    }

    private void SelectDashboardSectionForSizing(FrameworkElement element)
    {
        _dashboardSelectedSection = element;

        var key = GetDefaultDashboardModuleOrder()
            .FirstOrDefault(candidate =>
                ReferenceEquals(
                    GetDashboardModuleElement(candidate),
                    element));

        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        NormalizeDashboardModuleSizes();
        var size = _settings.Dashboard.ModuleSizes[key];

        foreach (var item in DashboardDirectSizeBox.Items
                     .OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (string.Equals(
                    item.Content?.ToString(),
                    size,
                    StringComparison.Ordinal))
            {
                DashboardDirectSizeBox.SelectedItem = item;
                break;
            }
        }

        DashboardCommandCenterSummaryText.Text =
            $"Ausgewählt: {GetDashboardModuleDisplayName(key)} · Größe {size}";
    }

    private void ApplySelectedDashboardModuleSizeFromDirectEditor()
    {
        if (!_dashboardLayoutEditMode ||
            _dashboardSelectedSection is null ||
            DashboardDirectSizeBox.SelectedItem is not System.Windows.Controls.ComboBoxItem sizeItem)
        {
            return;
        }

        var key = GetDefaultDashboardModuleOrder()
            .FirstOrDefault(candidate =>
                ReferenceEquals(
                    GetDashboardModuleElement(candidate),
                    _dashboardSelectedSection));

        var size = sizeItem.Content?.ToString();

        if (string.IsNullOrWhiteSpace(key) ||
            size is not ("Kompakt" or "Standard" or "Groß"))
        {
            return;
        }

        NormalizeDashboardModuleSizes();
        _settings.Dashboard.ModuleSizes[key] = size;
        ApplyDashboardModuleSizes();

        AddDashboardNotification(
            $"{GetDashboardModuleDisplayName(key)}: Größe auf {size} gesetzt.",
            "Info");
    }

    private void ApplyDashboardCheckboxesToSettings()
    {
        _settings.Dashboard.ShowServiceStatus = DashboardShowServiceStatusBox.IsChecked == true;
        _settings.Dashboard.ShowStreamControls = DashboardShowStreamControlsBox.IsChecked == true;
        _settings.Dashboard.ShowLivePanels = DashboardShowLivePanelsBox.IsChecked == true;
        _settings.Dashboard.ShowQuickServices = DashboardShowQuickServicesBox.IsChecked == true;
        _settings.Dashboard.ShowWorkflowRail = DashboardShowWorkflowRailBox.IsChecked == true;
        _settings.Dashboard.ShowAdvancedTools = DashboardShowAdvancedToolsBox.IsChecked == true;
        _settings.Dashboard.ShowNotifications = DashboardShowNotificationsBox.IsChecked == true;
        _settings.Dashboard.ShowStreamHistory = DashboardShowStreamHistoryBox.IsChecked == true;
    }

    private void ApplyDashboardLayout()
    {
        DashboardServiceStatusSection.Visibility = _settings.Dashboard.ShowServiceStatus
            ? Visibility.Visible
            : Visibility.Collapsed;
        DashboardStreamControlsSection.Visibility = _settings.Dashboard.ShowStreamControls
            ? Visibility.Visible
            : Visibility.Collapsed;
        DashboardLivePanelsSection.Visibility = _settings.Dashboard.ShowLivePanels
            ? Visibility.Visible
            : Visibility.Collapsed;
        DashboardQuickServicesSection.Visibility = _settings.Dashboard.ShowQuickServices
            ? Visibility.Visible
            : Visibility.Collapsed;
        DashboardWorkflowRailSection.Visibility = _settings.Dashboard.ShowWorkflowRail
            ? Visibility.Visible
            : Visibility.Collapsed;
        DashboardAdvancedToolsSection.Visibility = _settings.Dashboard.ShowAdvancedTools
            ? Visibility.Visible
            : Visibility.Collapsed;
        DashboardNotificationsSection.Visibility = _settings.Dashboard.ShowNotifications
            ? Visibility.Visible
            : Visibility.Collapsed;
        DashboardStreamHistorySection.Visibility = _settings.Dashboard.ShowStreamHistory
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async Task RefreshLicenseAsync()
    {
        var status = await _licenseService.GetStatusAsync();
        LicenseStatusText.Text = $"Status: {status.State}\n" + status.Detail + (status.License is null ? "" : "\nEdition: " + status.License.Edition + "\nLizenznehmer: " + status.License.CustomerName + "\nLizenz-ID: " + status.License.LicenseId);
        LicenseStatusText.Foreground = status.IsUsable ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.IndianRed;

        var features = await _featureGate.SnapshotAsync();
        FeatureGateGrid.ItemsSource = features.OrderBy(x => x.Key).Select(x => new { Feature = x.Key, Aktiv = x.Value }).ToList();
    }

    private async Task ActivateLicenseAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Creator Control Suite Lizenz (*.ccslicense)|*.ccslicense|JSON (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;
        var status = await _licenseService.ActivateAsync(dialog.FileName);
        await RefreshLicenseAsync();
        if (!status.IsUsable) MessageBox.Show(status.Detail,"Lizenz konnte nicht aktiviert werden",MessageBoxButton.OK,MessageBoxImage.Error);
    }

    private async Task DeactivateLicenseAsync()
    {
        if (MessageBox.Show("Lokale Lizenz wirklich deaktivieren?","Lizenz",MessageBoxButton.YesNo,MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await _licenseService.DeactivateAsync(); await RefreshLicenseAsync();
    }

    private void OpenLegalDocument(string id)
    {
        var document = _legalConsentService.GetDocuments().FirstOrDefault(x => string.Equals(x.Id,id,StringComparison.OrdinalIgnoreCase));
        var documentPath = document?.FilePath;
        if (string.IsNullOrWhiteSpace(documentPath) || !File.Exists(documentPath)) { MessageBox.Show("Dokument wurde nicht gefunden.","Creator Control Suite",MessageBoxButton.OK,MessageBoxImage.Warning); return; }
        Process.Start(new ProcessStartInfo { FileName = documentPath, UseShellExecute = true });
    }

    private async Task CreateSupportPackageAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Creator Control Suite Supportpaket (*.ccssupport)|*.ccssupport",
            FileName = "CreatorControlSuite-Support-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".ccssupport"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var result = await _supportPackageService.CreateAsync(dialog.FileName, new SupportPackageOptions(true, true, true, true, true, true));
            MessageBox.Show("Supportpaket erstellt:\n\n" + result.PackagePath + (result.Warnings.Count == 0 ? "" : "\n\nHinweise:\n" + string.Join("\n", result.Warnings.Select(x => "• " + x))), "Creator Control Suite", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            _appLogger.Write(AppLogLevel.Error, "Support", "Supportpaket konnte nicht erstellt werden.", exception);
            MessageBox.Show(exception.Message, "Supportpaket", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RunReleaseCheckAsync()
    {
        var report = await _releaseReadinessService.CheckAsync();
        ReleaseReadinessGrid.ItemsSource = report.Items;
        MessageBox.Show(report.Ready ? "Der technische Release-Check ist bestanden." : "Der Release-Check enthält blockierende Punkte.", "Release-Check", MessageBoxButton.OK, report.Ready ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async Task RunInstallerSelfTestAsync()
    {
        try
        {
            var report=await _installerSelfTestService.RunAsync();
            InstallerSelfTestGrid.ItemsSource=report.Items;
            MessageBox.Show(report.Passed?"Installer-Selbsttest bestanden.":"Installer-Selbsttest enthält Fehler.",
                "Installer-Selbsttest",MessageBoxButton.OK,report.Passed?MessageBoxImage.Information:MessageBoxImage.Warning);
        }
        catch(Exception ex)
        {
            _appLogger.Write(AppLogLevel.Error,"InstallerSelfTest","Installer-Selbsttest ist fehlgeschlagen.",ex);
            MessageBox.Show(ex.Message,"Installer-Selbsttest",MessageBoxButton.OK,MessageBoxImage.Error);
        }
    }

    private async Task RefreshBetaReadinessAsync()
    {
        try
        {
            var d=await _betaReadinessService.BuildAsync();
            BetaReadinessGrid.ItemsSource=d.Areas;BetaReadinessScoreText.Text=d.OverallScorePercent+" %";
            BetaReadinessStatusText.Text=d.BetaReady?"Beta technisch bereit":"Noch nicht Beta-bereit";
            BetaReadinessStatusText.Foreground=d.BetaReady?System.Windows.Media.Brushes.LightGreen:System.Windows.Media.Brushes.IndianRed;
            BetaBlockersTextBox.Text=d.Blockers.Count==0?"Keine technischen Blocker erkannt.":
                string.Join(Environment.NewLine,d.Blockers.Select(x=>"• "+x));
        }
        catch(Exception ex)
        {
            _appLogger.Write(AppLogLevel.Error,"BetaReadiness","Beta-Readiness konnte nicht ermittelt werden.",ex);
            MessageBox.Show(ex.Message,"Beta-Readiness",MessageBoxButton.OK,MessageBoxImage.Error);
        }
    }

    private async Task RunWorkflowE2eAsync()
    {
        if (MessageBox.Show("Der Test führt den echten Workflow Vorbereiten → Live → Pause → Fortsetzen → Ende aus. OBS und konfigurierte Dienste können gesteuert werden. Jetzt starten?", "Workflow E2E-Test", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            var report = await _workflowE2eService.RunAsync();
            WorkflowE2eGrid.ItemsSource = report.Steps;
            MessageBox.Show(report.Success ? "Workflow E2E-Test erfolgreich." : "Workflow E2E-Test enthält Fehler.", "Workflow E2E-Test");
        }
        catch (Exception exception)
        {
            _appLogger.Write(AppLogLevel.Error, "E2E", "Workflow E2E-Test fehlgeschlagen.", exception);
            MessageBox.Show(exception.Message, "Workflow E2E-Test", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RefreshProfilesAsync()
    {
        var profiles = await _profileService.ListAsync();
        ProfilesList.ItemsSource = profiles;
        DashboardProfileBox.ItemsSource = profiles;

        if (DashboardProfileBox.SelectedItem is null && profiles.Count > 0)
        {
            DashboardProfileBox.SelectedIndex = 0;
        }
    }

    private async Task ShowSelectedProfileAsync()
    {
        if (ProfilesList.SelectedItem is not ProfileSummary summary)
        {
            return;
        }

        var profile = await _profileService.LoadAsync(summary.Id);
        ProfileNameBox.Text = profile.Name;
        ProfileDescriptionBox.Text = profile.Description;
        ProfileStatusText.Text =
            $"Zuletzt geändert: {profile.UpdatedAt:dd.MM.yyyy HH:mm}";
    }

    private async Task CreateProfileAsync()
    {
        try
        {
            var name = ProfileNameBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Profil " + DateTime.Now.ToString("dd.MM.yyyy HH:mm");
            }

            var profile =
                await _profileService.CreateFromCurrentSettingsAsync(
                    name,
                    ProfileDescriptionBox.Text.Trim());

            await RefreshProfilesAsync();

            ProfilesList.SelectedItem =
                (ProfilesList.ItemsSource as IEnumerable<ProfileSummary>)
                ?.FirstOrDefault(item => item.Id == profile.Id);

            ProfileStatusText.Text = "Profil gespeichert.";
            ProfileStatusText.Foreground =
                System.Windows.Media.Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            ProfileStatusText.Text = exception.Message;
            ProfileStatusText.Foreground =
                System.Windows.Media.Brushes.IndianRed;
        }
    }

    private async Task ApplySelectedProfileAsync()
    {
        if (ProfilesList.SelectedItem is not ProfileSummary summary)
        {
            return;
        }

        var result = MessageBox.Show(
            $"Profil „{summary.Name}“ anwenden?\n\n" +
            "Die aktuellen Einstellungen werden ersetzt.",
            "Profil anwenden",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await _profileService.ApplyAsync(summary.Id);
        await LoadSettingsAsync();

        ProfileStatusText.Text =
            "Profil wurde angewendet.";
        ProfileStatusText.Foreground =
            System.Windows.Media.Brushes.LightGreen;
    }

    private async Task ExportSelectedProfileAsync()
    {
        if (ProfilesList.SelectedItem is not ProfileSummary summary)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Creator Control Suite Profil (*.ccsprofile)|*.ccsprofile",
            FileName = summary.Name + ".ccsprofile"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await _profileService.ExportAsync(
            summary.Id,
            dialog.FileName);

        ProfileStatusText.Text =
            "Profil exportiert: " + dialog.FileName;
    }

    private async Task ImportProfileAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Creator Control Suite Profil (*.ccsprofile;*.json)|*.ccsprofile;*.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await _profileService.ImportAsync(dialog.FileName);
        await RefreshProfilesAsync();

        ProfileStatusText.Text = "Profil importiert.";
        ProfileStatusText.Foreground =
            System.Windows.Media.Brushes.LightGreen;
    }

    private async Task DeleteSelectedProfileAsync()
    {
        if (ProfilesList.SelectedItem is not ProfileSummary summary)
        {
            return;
        }

        var result = MessageBox.Show(
            $"Profil „{summary.Name}“ löschen?",
            "Profil löschen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await _profileService.DeleteAsync(summary.Id);
        await RefreshProfilesAsync();

        ProfileStatusText.Text = "Profil gelöscht.";
    }


    private string StreamDeckActionsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CreatorControlSuite", "StreamDeck", "Actions");

    private void RefreshStreamDeckActionsList()
    {
        Directory.CreateDirectory(StreamDeckActionsDirectory);
        StreamDeckActionsFolderText.Text = StreamDeckActionsDirectory;

        var entries = Directory.EnumerateFiles(StreamDeckActionsDirectory, "*.cmd")
            .Select(file => ReadStreamDeckMetadata(file))
            .OrderBy(entry => entry.Profile)
            .ThenBy(entry => entry.Page)
            .ThenBy(entry => entry.Slot)
            .ThenBy(entry => entry.Title)
            .ToList();

        var selectedProfile = (StreamDeckProfileFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
        var selectedPage = (StreamDeckPageFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;

        RebuildStreamDeckFilter(StreamDeckProfileFilterBox, entries.Select(entry => entry.Profile), "Alle Profile", selectedProfile);
        RebuildStreamDeckFilter(StreamDeckPageFilterBox, entries
            .Where(entry => string.IsNullOrWhiteSpace(selectedProfile) || entry.Profile == selectedProfile)
            .Select(entry => entry.Page), "Alle Seiten", selectedPage);

        selectedProfile = (StreamDeckProfileFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
        selectedPage = (StreamDeckPageFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;

        StreamDeckCreatedActionsList.Items.Clear();
        foreach (var entry in entries.Where(entry =>
                     (string.IsNullOrWhiteSpace(selectedProfile) || entry.Profile == selectedProfile) &&
                     (string.IsNullOrWhiteSpace(selectedPage) || entry.Page == selectedPage)))
        {
            var displayTitle = ResolveStreamDeckDisplayTitle(entry);
            StreamDeckCreatedActionsList.Items.Add(new ListBoxItem
            {
                Content = $"{(entry.Locked ? "🔒 " : string.Empty)}[{entry.Profile} / {entry.Page} / {entry.Slot}] {displayTitle}",
                Tag = entry.File
            });
        }

        var occupied = entries.Select(entry => $"{entry.Profile}|{entry.Page}|{entry.Slot}").Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var conflicts = entries.GroupBy(entry => $"{entry.Profile}|{entry.Page}|{entry.Slot}", StringComparer.OrdinalIgnoreCase).Count(group => group.Count() > 1);
        StreamDeckOccupancyText.Text = conflicts == 0 ? $"{occupied} Positionen belegt" : $"{occupied} belegt · {conflicts} Konflikte";
        StreamDeckOccupancyText.Foreground = conflicts == 0 ? Brushes.LightGreen : Brushes.OrangeRed;
        RebuildStreamDeckSlotGrid(entries, selectedProfile, selectedPage);
        RefreshSelectedStreamDeckActionDetails();
    }


    private void RebuildStreamDeckSlotGrid(IEnumerable<(string File, string Title, string Command, string Parameter, string Profile, string Page, int Slot, int Steps, bool Locked, string Condition, string TrueLabel, string FalseLabel)> entries, string selectedProfile, string selectedPage)
    {
        StreamDeckSlotGrid.Children.Clear();
        var profile = string.IsNullOrWhiteSpace(selectedProfile) ? "Standard" : selectedProfile;
        var page = string.IsNullOrWhiteSpace(selectedPage) ? "Hauptseite" : selectedPage;
        var lookup = entries.Where(e => string.Equals(e.Profile, profile, StringComparison.OrdinalIgnoreCase) && string.Equals(e.Page, page, StringComparison.OrdinalIgnoreCase) && e.Slot is >= 1 and <= 32)
            .GroupBy(e => e.Slot).ToDictionary(g => g.Key, g => g.ToList());
        for (var slot = 1; slot <= 32; slot++)
        {
            var currentSlot = slot;
            lookup.TryGetValue(slot, out var assigned);
            var button = new Button { Margin = new Thickness(2), MinHeight = 44, Tag = currentSlot, Content = assigned is null ? slot.ToString() : $"{slot}\n{ResolveStreamDeckDisplayTitle(assigned[0])}", ToolTip = assigned is null ? "Frei" : string.Join("\n", assigned.Select(e => e.Title)) };
            if (assigned is { Count: > 1 }) button.Background = Brushes.OrangeRed;
            else if (assigned is { Count: 1 }) button.Background = Brushes.DarkSlateGray;
            button.Click += async (_, _) => await MoveSelectedStreamDeckActionToSlotAsync(currentSlot, profile, page);
            StreamDeckSlotGrid.Children.Add(button);
        }
    }

    private async Task MoveSelectedStreamDeckActionToSlotAsync(int slot, string profile, string page)
    {
        if (StreamDeckCreatedActionsList.SelectedItem is not ListBoxItem item || item.Tag is not string file)
        {
            StreamDeckActionCreateStatusText.Text = "Bitte zuerst eine Taste auswählen.";
            return;
        }
        var metadataPath = Path.ChangeExtension(file, ".json");
        if (!File.Exists(metadataPath)) return;
        if (ReadStreamDeckMetadata(file).Locked) { StreamDeckActionCreateStatusText.Text = "Die Taste ist gesperrt. Bitte zuerst entsperren."; return; }
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath));
        var values = document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        var output = new Dictionary<string, object?>();
        foreach (var pair in values) output[pair.Key] = pair.Value;
        output["profile"] = profile; output["page"] = page; output["slot"] = slot;
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
        StreamDeckActionCreateStatusText.Text = $"Taste auf {profile} / {page} / Position {slot} verschoben.";
        StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
        RefreshStreamDeckActionsList();
    }

    private async Task DuplicateSelectedStreamDeckProfileAsync()
    {
        var selectedProfile = (StreamDeckProfileFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(selectedProfile)) { StreamDeckActionCreateStatusText.Text = "Bitte zuerst ein Profil filtern."; return; }
        var targetProfile = selectedProfile + " - Kopie";
        var files = Directory.EnumerateFiles(StreamDeckActionsDirectory, "*.cmd").Where(f => string.Equals(ReadStreamDeckMetadata(f).Profile, selectedProfile, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var file in files)
        {
            var entry = ReadStreamDeckMetadata(file);
            var target = Path.Combine(StreamDeckActionsDirectory, Path.GetFileNameWithoutExtension(file) + " - " + targetProfile + ".cmd");
            File.Copy(file, target, true);
            var metaPath = Path.ChangeExtension(file, ".json");
            if (!File.Exists(metaPath)) continue;
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(metaPath));
            var output = document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
            output["profile"] = targetProfile;
            await File.WriteAllTextAsync(Path.ChangeExtension(target, ".json"), JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
        }
        StreamDeckActionCreateStatusText.Text = $"Profil kopiert: {selectedProfile} → {targetProfile} ({files.Count} Tasten).";
        StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
        RefreshStreamDeckActionsList();
    }

    private async Task ResolveStreamDeckConflictsAsync()
    {
        var entries = Directory.EnumerateFiles(StreamDeckActionsDirectory, "*.cmd").Select(ReadStreamDeckMetadata).OrderBy(e => e.Profile).ThenBy(e => e.Page).ThenBy(e => e.Slot).ToList();
        var changed = 0;
        foreach (var group in entries.GroupBy(e => (e.Profile.ToLowerInvariant(), e.Page.ToLowerInvariant())))
        {
            var used = new HashSet<int>();
            foreach (var entry in group)
            {
                var slot = entry.Slot;
                if (slot is < 1 or > 32 || !used.Add(slot))
                {
                    slot = Enumerable.Range(1, 32).FirstOrDefault(candidate => !used.Contains(candidate));
                    if (slot == 0) continue;
                    used.Add(slot);
                    var metaPath = Path.ChangeExtension(entry.File, ".json");
                    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(metaPath));
                    var output = document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
                    output["slot"] = slot;
                    await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
                    changed++;
                }
            }
        }
        StreamDeckActionCreateStatusText.Text = changed == 0 ? "Keine Positionskonflikte gefunden." : $"{changed} Positionskonflikte automatisch gelöst.";
        StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
        RefreshStreamDeckActionsList();
    }

    private static void RebuildStreamDeckFilter(ComboBox box, IEnumerable<string> values, string allText, string selected)
    {
        var distinct = values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList();
        box.Items.Clear();
        box.Items.Add(new ComboBoxItem { Content = allText, Tag = string.Empty });
        foreach (var value in distinct) box.Items.Add(new ComboBoxItem { Content = value, Tag = value });
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag?.ToString(), selected, StringComparison.OrdinalIgnoreCase)) ?? box.Items[0];
    }

    private static (string File, string Title, string Command, string Parameter, string Profile, string Page, int Slot, int Steps, bool Locked, string Condition, string TrueLabel, string FalseLabel) ReadStreamDeckMetadata(string file)
    {
        try
        {
            var metadataPath = Path.ChangeExtension(file, ".json");
            if (!File.Exists(metadataPath)) return (file, Path.GetFileNameWithoutExtension(file), "–", "", "Standard", "Hauptseite", 0, 1, false, "", "", "");
            using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
            var root = document.RootElement;
            string GetString(string name, string fallback) => root.TryGetProperty(name, out var node) ? node.GetString() ?? fallback : fallback;
            var slot = root.TryGetProperty("slot", out var slotNode) && slotNode.TryGetInt32(out var slotValue) ? slotValue : 0;
            var steps = root.TryGetProperty("steps", out var stepsNode) && stepsNode.ValueKind == JsonValueKind.Array ? stepsNode.GetArrayLength() : 1;
            var locked = root.TryGetProperty("locked", out var lockedNode) && lockedNode.ValueKind == JsonValueKind.True;
            return (file, GetString("title", Path.GetFileNameWithoutExtension(file)), GetString("command", "–"), GetString("parameter", ""), GetString("profile", "Standard"), GetString("page", "Hauptseite"), slot, Math.Max(1, steps), locked, GetString("condition", ""), GetString("trueLabel", ""), GetString("falseLabel", ""));
        }
        catch
        {
            return (file, Path.GetFileNameWithoutExtension(file), "–", "", "Standard", "Hauptseite", 0, 1, false, "", "", "");
        }
    }

    private static (bool ToggleMode, string AlternateCommand, string AlternateParameter) ReadStreamDeckToggleMetadata(string file)
    {
        try
        {
            var metadataPath = Path.ChangeExtension(file, ".json");
            if (!File.Exists(metadataPath)) return (false, "", "");
            using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
            var root = document.RootElement;
            var toggle = root.TryGetProperty("toggleMode", out var toggleNode) && toggleNode.ValueKind == JsonValueKind.True;
            var command = root.TryGetProperty("alternateCommand", out var commandNode) ? commandNode.GetString() ?? "" : "";
            var parameter = root.TryGetProperty("alternateParameter", out var parameterNode) ? parameterNode.GetString() ?? "" : "";
            return (toggle, command, parameter);
        }
        catch { return (false, "", ""); }
    }

    private void DiagnoseStreamDeckActions()
    {
        Directory.CreateDirectory(StreamDeckActionsDirectory);
        var issues = new List<string>();
        var cmdFiles = Directory.EnumerateFiles(StreamDeckActionsDirectory, "*.cmd").ToList();
        var clientPath = Path.Combine(AppContext.BaseDirectory, "CreatorControlSuite.CommandClient.exe");
        if (!File.Exists(clientPath)) issues.Add("• CommandClient.exe wurde im Programmordner nicht gefunden.");
        foreach (var file in cmdFiles)
        {
            var metadataPath = Path.ChangeExtension(file, ".json");
            if (!File.Exists(metadataPath)) { issues.Add($"• {Path.GetFileName(file)}: Metadatendatei fehlt."); continue; }
            try
            {
                var entry = ReadStreamDeckMetadata(file);
                var toggle = ReadStreamDeckToggleMetadata(file);
                if (entry.Slot is < 1 or > 32) issues.Add($"• {entry.Title}: ungültige Position {entry.Slot}.");
                if (string.IsNullOrWhiteSpace(entry.Command) || entry.Command == "–") issues.Add($"• {entry.Title}: Hauptbefehl fehlt.");
                if (toggle.ToggleMode && string.IsNullOrWhiteSpace(entry.Condition)) issues.Add($"• {entry.Title}: Toggle aktiv, aber keine Zustandsbindung gesetzt.");
                if (toggle.ToggleMode && string.IsNullOrWhiteSpace(toggle.AlternateCommand)) issues.Add($"• {entry.Title}: zweiter Toggle-Befehl fehlt.");
            }
            catch (Exception ex) { issues.Add($"• {Path.GetFileName(metadataPath)}: {ex.Message}"); }
        }
        var duplicates = cmdFiles.Select(ReadStreamDeckMetadata).GroupBy(e => $"{e.Profile}|{e.Page}|{e.Slot}", StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1);
        foreach (var group in duplicates) issues.Add($"• Doppelbelegung {group.Key}: {string.Join(", ", group.Select(e => e.Title))}");
        StreamDeckDiagnosticsBox.Text = issues.Count == 0 ? $"OK – {cmdFiles.Count} Aktion(en) geprüft. Keine Fehler gefunden." : $"{issues.Count} Problem(e) gefunden:\n" + string.Join("\n", issues);
        StreamDeckDiagnosticsBox.Foreground = issues.Count == 0 ? Brushes.LightGreen : Brushes.OrangeRed;
        StreamDeckActionCreateStatusText.Text = issues.Count == 0 ? "Stream-Deck-Diagnose erfolgreich." : "Stream-Deck-Diagnose hat Probleme gefunden.";
    }

    private void RefreshSelectedStreamDeckActionDetails()
    {
        if (StreamDeckCreatedActionsList.SelectedItem is not ListBoxItem item || item.Tag is not string file)
        {
            StreamDeckSelectedActionDetailsText.Text = "Keine Taste ausgewählt.";
            return;
        }

        var entry = ReadStreamDeckMetadata(file);
        var alternate = ReadStreamDeckToggleMetadata(file);
        var policy = ReadStreamDeckExecutionPolicy(file);
        StreamDeckSelectedActionDetailsText.Text = $"{entry.Title}\nProfil: {entry.Profile} · Seite: {entry.Page} · Position: {entry.Slot}\nStatus: {(entry.Locked ? "Gesperrt" : "Bearbeitbar")}\nBefehl AUS: {entry.Command}\nParameter AUS: {(string.IsNullOrWhiteSpace(entry.Parameter) ? "–" : entry.Parameter)}\nBefehl AN: {(alternate.ToggleMode ? alternate.AlternateCommand : "–")}\nParameter AN: {(string.IsNullOrWhiteSpace(alternate.AlternateParameter) ? "–" : alternate.AlternateParameter)}\nSchritte: {entry.Steps} · Verzögerung: {policy.DelayMs} ms · Wiederholungen: {policy.RetryCount} · Cooldown: {policy.CooldownMs} ms\nZustandsbindung: {(string.IsNullOrWhiteSpace(entry.Condition) ? "–" : entry.Condition)}\nAktuelle Beschriftung: {ResolveStreamDeckDisplayTitle(entry)}";
        LockStreamDeckActionButton.Content = entry.Locked ? "TASTE ENTSPERREN" : "TASTE SPERREN";
    }

    private string StreamDeckRuntimeStateFile => Path.Combine(StreamDeckActionsDirectory, "streamdeck-runtime-state.json");

    private static bool IsStatusLampActive(System.Windows.Shapes.Ellipse lamp)
    {
        var value = lamp.Fill?.ToString() ?? string.Empty;
        return value.Contains("LightGreen", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("#FF90EE90", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("#FF5CB85C", StringComparison.OrdinalIgnoreCase);
    }

    private Dictionary<string, bool> GetStreamDeckRuntimeStates() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["stream.live"] = IsStatusLampActive(StreamDashboardLamp),
        ["obs.connected"] = IsStatusLampActive(ObsDashboardLamp),
        ["spotify.playing"] = IsStatusLampActive(SpotifyDashboardLamp)
    };

    private string ResolveStreamDeckDisplayTitle((string File, string Title, string Command, string Parameter, string Profile, string Page, int Slot, int Steps, bool Locked, string Condition, string TrueLabel, string FalseLabel) entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Condition)) return entry.Title;
        var states = GetStreamDeckRuntimeStates();
        if (!states.TryGetValue(entry.Condition, out var active)) return entry.Title;
        var label = active ? entry.TrueLabel : entry.FalseLabel;
        return string.IsNullOrWhiteSpace(label) ? entry.Title : label;
    }

    private async Task SyncStreamDeckRuntimeStateAsync(bool showConfirmation)
    {
        try
        {
            Directory.CreateDirectory(StreamDeckActionsDirectory);
            var states = GetStreamDeckRuntimeStates();
            var payload = new
            {
                updatedAt = DateTimeOffset.Now,
                stream = new { isLive = states["stream.live"] },
                obs = new { connected = states["obs.connected"] },
                spotify = new { isPlaying = states["spotify.playing"] }
            };
            await File.WriteAllTextAsync(StreamDeckRuntimeStateFile, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            StreamDeckLiveSyncStatusText.Text = $"Live-Sync: {DateTime.Now:HH:mm:ss}";
            StreamDeckLiveSyncStatusText.Foreground = Brushes.LightGreen;
            RefreshStreamDeckActionsList();
            if (showConfirmation)
            {
                StreamDeckActionCreateStatusText.Text = "Stream-Deck-Zustände wurden synchronisiert.";
                StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
            }
        }
        catch (Exception ex)
        {
            StreamDeckLiveSyncStatusText.Text = "Live-Sync fehlgeschlagen: " + ex.Message;
            StreamDeckLiveSyncStatusText.Foreground = Brushes.IndianRed;
        }
    }

    private string StreamDeckExecutionLogFile => Path.Combine(StreamDeckActionsDirectory, "streamdeck-execution-log.jsonl");

    private static (int DelayMs, int RetryCount, int CooldownMs) ReadStreamDeckExecutionPolicy(string file)
    {
        try
        {
            var metadataPath = Path.ChangeExtension(file, ".json");
            if (!File.Exists(metadataPath)) return (250, 1, 1000);
            using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
            var root = document.RootElement;
            int ReadInt(string name, int fallback) => root.TryGetProperty(name, out var node) && node.TryGetInt32(out var value) ? value : fallback;
            return (Math.Clamp(ReadInt("stepDelayMs", 250), 0, 10000), Math.Clamp(ReadInt("retryCount", 1), 0, 5), Math.Clamp(ReadInt("cooldownMs", 1000), 0, 60000));
        }
        catch { return (250, 1, 1000); }
    }

    private async Task AppendStreamDeckExecutionLogAsync(string action, string mode, bool success, long durationMs, string message)
    {
        Directory.CreateDirectory(StreamDeckActionsDirectory);
        var line = JsonSerializer.Serialize(new { timestamp = DateTimeOffset.Now, action, mode, success, durationMs, message });
        await File.AppendAllTextAsync(StreamDeckExecutionLogFile, line + Environment.NewLine);
        RefreshStreamDeckExecutionLog();
    }

    private void RefreshStreamDeckExecutionLog()
    {
        if (!File.Exists(StreamDeckExecutionLogFile)) { StreamDeckExecutionLogBox.Text = "Noch keine Aktion ausgeführt."; return; }
        var lines = File.ReadLines(StreamDeckExecutionLogFile).TakeLast(25).Reverse().Select(line =>
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var r = doc.RootElement;
                var time = r.GetProperty("timestamp").GetDateTimeOffset().ToLocalTime().ToString("HH:mm:ss");
                return $"{time} · {(r.GetProperty("success").GetBoolean() ? "OK" : "FEHLER")} · {r.GetProperty("action").GetString()} · {r.GetProperty("mode").GetString()} · {r.GetProperty("durationMs").GetInt64()} ms · {r.GetProperty("message").GetString()}";
            }
            catch { return line; }
        });
        StreamDeckExecutionLogBox.Text = string.Join(Environment.NewLine, lines);
    }

    private void ClearStreamDeckExecutionLog()
    {
        if (File.Exists(StreamDeckExecutionLogFile)) File.Delete(StreamDeckExecutionLogFile);
        StreamDeckExecutionLogBox.Text = "Protokoll wurde geleert.";
        StreamDeckActionCreateStatusText.Text = "Stream-Deck-Ausführungsprotokoll geleert.";
    }

    private async Task SimulateSelectedStreamDeckActionAsync()
    {
        if (StreamDeckCreatedActionsList.SelectedItem is not ListBoxItem item || item.Tag is not string file || !File.Exists(file))
        {
            StreamDeckActionCreateStatusText.Text = "Bitte zuerst eine erstellte Taste auswählen.";
            return;
        }
        var entry = ReadStreamDeckMetadata(file);
        var policy = ReadStreamDeckExecutionPolicy(file);
        var simulatedDuration = Math.Max(1, entry.Steps) * policy.DelayMs;
        await AppendStreamDeckExecutionLogAsync(entry.Title, "Simulation", true, simulatedDuration, $"{entry.Steps} Schritt(e), {policy.RetryCount} Wiederholung(en), Cooldown {policy.CooldownMs} ms");
        StreamDeckActionCreateStatusText.Text = $"Simulation erfolgreich: {entry.Title}";
        StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
    }

    private async Task TestSelectedStreamDeckActionAsync()
    {
        if (StreamDeckCreatedActionsList.SelectedItem is not ListBoxItem item || item.Tag is not string file || !File.Exists(file))
        {
            StreamDeckActionCreateStatusText.Text = "Bitte zuerst eine erstellte Taste auswählen.";
            return;
        }
        var entry = ReadStreamDeckMetadata(file);
        var policy = ReadStreamDeckExecutionPolicy(file);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var simulation = StreamDeckSimulationModeBox.IsChecked == true;
            var success = false;
            string message;
            if (simulation)
            {
                await Task.Delay(Math.Min(1000, Math.Max(20, entry.Steps * policy.DelayMs)));
                success = true;
                message = "Testsimulation – keine externen Befehle ausgeführt.";
            }
            else
            {
                for (var attempt = 0; attempt <= policy.RetryCount && !success; attempt++)
                {
                    success = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden }) is not null;
                    if (!success && attempt < policy.RetryCount) await Task.Delay(250);
                }
                message = success ? "Befehl gestartet; Rückmeldung gespeichert." : "Prozess konnte nicht gestartet werden.";
            }
            stopwatch.Stop();
            var feedbackPath = Path.Combine(StreamDeckActionsDirectory, "streamdeck-execution-feedback.json");
            await File.WriteAllTextAsync(feedbackPath, JsonSerializer.Serialize(new { action = entry.Title, success, durationMs = stopwatch.ElapsedMilliseconds, executedAt = DateTimeOffset.Now, message }, new JsonSerializerOptions { WriteIndented = true }));
            await AppendStreamDeckExecutionLogAsync(entry.Title, simulation ? "Simulation" : "Test", success, stopwatch.ElapsedMilliseconds, message);
            StreamDeckActionCreateStatusText.Text = success ? $"Test abgeschlossen: {entry.Title} · {stopwatch.ElapsedMilliseconds} ms" : "Test konnte nicht gestartet werden.";
            StreamDeckActionCreateStatusText.Foreground = success ? Brushes.LightGreen : Brushes.IndianRed;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await AppendStreamDeckExecutionLogAsync(entry.Title, "Test", false, stopwatch.ElapsedMilliseconds, ex.Message);
            StreamDeckActionCreateStatusText.Text = "Test fehlgeschlagen: " + ex.Message;
            StreamDeckActionCreateStatusText.Foreground = Brushes.IndianRed;
        }
    }

    private async Task DuplicateSelectedStreamDeckActionAsync()
    {
        if (StreamDeckCreatedActionsList.SelectedItem is not ListBoxItem item || item.Tag is not string file || !File.Exists(file)) return;
        var baseName = Path.GetFileNameWithoutExtension(file) + " - Kopie";
        var target = Path.Combine(StreamDeckActionsDirectory, baseName + ".cmd");
        var counter = 2;
        while (File.Exists(target)) target = Path.Combine(StreamDeckActionsDirectory, $"{baseName} {counter++}.cmd");
        File.Copy(file, target);
        var metadata = Path.ChangeExtension(file, ".json");
        if (File.Exists(metadata))
        {
            var json = await File.ReadAllTextAsync(metadata);
            await File.WriteAllTextAsync(Path.ChangeExtension(target, ".json"), json);
        }
        RefreshStreamDeckActionsList();
        StreamDeckActionCreateStatusText.Text = "Taste dupliziert: " + Path.GetFileNameWithoutExtension(target);
        StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
    }

    private string StreamDeckStateFile => Path.Combine(StreamDeckActionsDirectory, "streamdeck-state.json");

    private void ActivateSelectedStreamDeckView()
    {
        var profile = (StreamDeckProfileFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var page = (StreamDeckPageFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(profile) || string.IsNullOrWhiteSpace(page))
        {
            StreamDeckActionCreateStatusText.Text = "Bitte zuerst ein Profil und eine Seite auswählen.";
            return;
        }
        Directory.CreateDirectory(StreamDeckActionsDirectory);
        File.WriteAllText(StreamDeckStateFile, JsonSerializer.Serialize(new { activeProfile = profile, activePage = page, changedAt = DateTimeOffset.Now }, new JsonSerializerOptions { WriteIndented = true }));
        StreamDeckActionCreateStatusText.Text = $"Aktiv: {profile} / {page}";
        StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
    }

    private async Task ToggleSelectedStreamDeckActionLockAsync()
    {
        if (StreamDeckCreatedActionsList.SelectedItem is not ListBoxItem item || item.Tag is not string file) return;
        var metadataPath = Path.ChangeExtension(file, ".json");
        if (!File.Exists(metadataPath)) return;
        var entry = ReadStreamDeckMetadata(file);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath));
        var output = document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
        output["locked"] = !entry.Locked;
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
        StreamDeckActionCreateStatusText.Text = !entry.Locked ? "Taste gesperrt." : "Taste entsperrt.";
        StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
        RefreshStreamDeckActionsList();
    }

    private void BackupStreamDeckConfiguration()
    {
        Directory.CreateDirectory(StreamDeckActionsDirectory);
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Stream-Deck-Komplettbackup (*.zip)|*.zip", FileName = $"CreatorControlSuite-StreamDeck-Backup-{DateTime.Now:yyyyMMdd-HHmm}.zip" };
        if (dialog.ShowDialog(this) != true) return;
        if (File.Exists(dialog.FileName)) File.Delete(dialog.FileName);
        System.IO.Compression.ZipFile.CreateFromDirectory(StreamDeckActionsDirectory, dialog.FileName);
        StreamDeckActionCreateStatusText.Text = "Komplettbackup erstellt: " + dialog.FileName;
        StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
    }

    private void RestoreStreamDeckConfiguration()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Stream-Deck-Komplettbackup (*.zip)|*.zip" };
        if (dialog.ShowDialog(this) != true) return;
        Directory.CreateDirectory(StreamDeckActionsDirectory);
        foreach (var file in Directory.EnumerateFiles(StreamDeckActionsDirectory)) File.Delete(file);
        System.IO.Compression.ZipFile.ExtractToDirectory(dialog.FileName, StreamDeckActionsDirectory, true);
        RefreshStreamDeckActionsList();
        StreamDeckActionCreateStatusText.Text = "Stream-Deck-Konfiguration wiederhergestellt.";
        StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
    }

    private void ExportStreamDeckActionCatalog()
    {
        Directory.CreateDirectory(StreamDeckActionsDirectory);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Stream-Deck-Aktionskatalog (*.zip)|*.zip",
            FileName = $"CreatorControlSuite-StreamDeck-Actions-{DateTime.Now:yyyyMMdd-HHmm}.zip"
        };
        if (dialog.ShowDialog(this) != true) return;
        if (File.Exists(dialog.FileName)) File.Delete(dialog.FileName);
        System.IO.Compression.ZipFile.CreateFromDirectory(StreamDeckActionsDirectory, dialog.FileName);
        StreamDeckActionCreateStatusText.Text = "Aktionskatalog exportiert: " + dialog.FileName;
        StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
    }

    private void ImportStreamDeckActionCatalog()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Stream-Deck-Aktionskatalog (*.zip)|*.zip" };
        if (dialog.ShowDialog(this) != true) return;
        Directory.CreateDirectory(StreamDeckActionsDirectory);
        System.IO.Compression.ZipFile.ExtractToDirectory(dialog.FileName, StreamDeckActionsDirectory, overwriteFiles: true);
        RefreshStreamDeckActionsList();
        StreamDeckActionCreateStatusText.Text = "Aktionskatalog importiert.";
        StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
    }


    private string StreamDeckTemplatesDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CreatorControlSuite", "StreamDeck", "Templates");

    private sealed record StreamDeckTemplateItem(string Name, string Path);

    private void RefreshStreamDeckTemplates()
    {
        Directory.CreateDirectory(StreamDeckTemplatesDirectory);
        StreamDeckTemplateBox.ItemsSource = Directory.EnumerateFiles(StreamDeckTemplatesDirectory, "*.json")
            .OrderBy(Path.GetFileNameWithoutExtension)
            .Select(path => new StreamDeckTemplateItem(Path.GetFileNameWithoutExtension(path), path))
            .ToList();
    }

    private async Task SaveStreamDeckTemplateAsync()
    {
        var name = string.IsNullOrWhiteSpace(StreamDeckTemplateNameBox.Text) ? StreamDeckActionTitleBox.Text.Trim() : StreamDeckTemplateNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) { StreamDeckActionCreateStatusText.Text = "Bitte einen Vorlagennamen eingeben."; return; }
        var safe = string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        Directory.CreateDirectory(StreamDeckTemplatesDirectory);
        var data = new
        {
            name,
            title = StreamDeckActionTitleBox.Text,
            command = (StreamDeckActionCommandBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "workflow.prepare",
            parameter = StreamDeckActionParameterBox.Text,
            multiAction = StreamDeckMultiActionBox.Text,
            condition = (StreamDeckStateConditionBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty,
            trueLabel = StreamDeckTrueLabelBox.Text,
            falseLabel = StreamDeckFalseLabelBox.Text,
            toggleMode = StreamDeckToggleModeBox.IsChecked == true,
            alternateCommand = (StreamDeckAlternateCommandBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty,
            alternateParameter = StreamDeckAlternateParameterBox.Text,
            stepDelayMs = StreamDeckStepDelayBox.Text,
            retryCount = StreamDeckRetryCountBox.Text,
            cooldownMs = StreamDeckCooldownBox.Text
        };
        await File.WriteAllTextAsync(Path.Combine(StreamDeckTemplatesDirectory, safe + ".json"), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        RefreshStreamDeckTemplates();
        StreamDeckActionCreateStatusText.Text = $"Vorlage gespeichert: {name}";
        StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
    }

    private async Task LoadSelectedStreamDeckTemplateAsync()
    {
        if (StreamDeckTemplateBox.SelectedItem is not StreamDeckTemplateItem item || !File.Exists(item.Path)) { StreamDeckActionCreateStatusText.Text = "Bitte eine Vorlage auswählen."; return; }
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(item.Path));
        var r = doc.RootElement;
        StreamDeckActionTitleBox.Text = r.TryGetProperty("title", out var v) ? v.GetString() ?? item.Name : item.Name;
        SelectComboBoxByTag(StreamDeckActionCommandBox, r.TryGetProperty("command", out v) ? v.GetString() : null);
        StreamDeckActionParameterBox.Text = r.TryGetProperty("parameter", out v) ? v.GetString() ?? "" : "";
        StreamDeckMultiActionBox.Text = r.TryGetProperty("multiAction", out v) ? v.GetString() ?? "" : "";
        SelectComboBoxByTag(StreamDeckStateConditionBox, r.TryGetProperty("condition", out v) ? v.GetString() : null);
        StreamDeckTrueLabelBox.Text = r.TryGetProperty("trueLabel", out v) ? v.GetString() ?? "" : "";
        StreamDeckFalseLabelBox.Text = r.TryGetProperty("falseLabel", out v) ? v.GetString() ?? "" : "";
        StreamDeckToggleModeBox.IsChecked = r.TryGetProperty("toggleMode", out v) && v.ValueKind == JsonValueKind.True;
        SelectComboBoxByTag(StreamDeckAlternateCommandBox, r.TryGetProperty("alternateCommand", out v) ? v.GetString() : null);
        StreamDeckAlternateParameterBox.Text = r.TryGetProperty("alternateParameter", out v) ? v.GetString() ?? "" : "";
        StreamDeckStepDelayBox.Text = r.TryGetProperty("stepDelayMs", out v) ? v.ToString() : "250";
        StreamDeckRetryCountBox.Text = r.TryGetProperty("retryCount", out v) ? v.ToString() : "1";
        StreamDeckCooldownBox.Text = r.TryGetProperty("cooldownMs", out v) ? v.ToString() : "1000";
        StreamDeckActionCreateStatusText.Text = $"Vorlage geladen: {item.Name}";
        StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
    }

    private static void SelectComboBoxByTag(ComboBox box, string? tag)
    {
        foreach (var entry in box.Items.OfType<ComboBoxItem>())
            if (string.Equals(entry.Tag?.ToString(), tag ?? string.Empty, StringComparison.OrdinalIgnoreCase)) { box.SelectedItem = entry; return; }
    }

    private void DeleteSelectedStreamDeckTemplate()
    {
        if (StreamDeckTemplateBox.SelectedItem is not StreamDeckTemplateItem item) return;
        if (File.Exists(item.Path)) File.Delete(item.Path);
        RefreshStreamDeckTemplates();
        StreamDeckActionCreateStatusText.Text = $"Vorlage gelöscht: {item.Name}";
    }

    private void ExportSelectedStreamDeckAction()
    {
        if (StreamDeckCreatedActionsList.SelectedItem is not ListBoxItem item || item.Tag is not string file) { StreamDeckActionCreateStatusText.Text = "Bitte zuerst eine Taste auswählen."; return; }
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Stream-Deck-Taste (*.sdaction)|*.sdaction", FileName = Path.GetFileNameWithoutExtension(file) + ".sdaction" };
        if (dialog.ShowDialog(this) != true) return;
        using var archive = System.IO.Compression.ZipFile.Open(dialog.FileName, System.IO.Compression.ZipArchiveMode.Create);
        archive.CreateEntryFromFile(file, Path.GetFileName(file));
        var meta = Path.ChangeExtension(file, ".json"); if (File.Exists(meta)) archive.CreateEntryFromFile(meta, Path.GetFileName(meta));
        StreamDeckActionCreateStatusText.Text = "Taste exportiert: " + dialog.FileName;
        StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
    }

    private void ImportSingleStreamDeckAction()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Stream-Deck-Taste (*.sdaction)|*.sdaction" };
        if (dialog.ShowDialog(this) != true) return;
        Directory.CreateDirectory(StreamDeckActionsDirectory);
        System.IO.Compression.ZipFile.ExtractToDirectory(dialog.FileName, StreamDeckActionsDirectory, true);
        RefreshStreamDeckActionsList();
        StreamDeckActionCreateStatusText.Text = "Einzelne Taste importiert.";
        StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
    }

    private async Task QuickAssignSelectedStreamDeckActionAsync()
    {
        if (StreamDeckCreatedActionsList.SelectedItem is not ListBoxItem item || item.Tag is not string file) { StreamDeckActionCreateStatusText.Text = "Bitte zuerst eine Taste auswählen."; return; }
        var selected = ReadStreamDeckMetadata(file);
        if (selected.Locked) { StreamDeckActionCreateStatusText.Text = "Die Taste ist gesperrt."; return; }
        var used = Directory.EnumerateFiles(StreamDeckActionsDirectory, "*.cmd").Select(ReadStreamDeckMetadata)
            .Where(e => e.File != file && string.Equals(e.Profile, selected.Profile, StringComparison.OrdinalIgnoreCase) && string.Equals(e.Page, selected.Page, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Slot).ToHashSet();
        var free = Enumerable.Range(1, 32).FirstOrDefault(slot => !used.Contains(slot));
        if (free == 0) { StreamDeckActionCreateStatusText.Text = "Auf dieser Seite ist kein freier Platz vorhanden."; return; }
        await MoveSelectedStreamDeckActionToSlotAsync(free, selected.Profile, selected.Page);
    }

    private void CompareStreamDeckProfiles()
    {
        var entries = Directory.EnumerateFiles(StreamDeckActionsDirectory, "*.cmd").Select(ReadStreamDeckMetadata).ToList();
        var profiles = entries.Select(e => e.Profile).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        if (profiles.Count < 2) { StreamDeckDiagnosticsBox.Text = "Für einen Vergleich werden mindestens zwei Profile benötigt."; return; }
        var baseline = profiles[0];
        var baseKeys = entries.Where(e => e.Profile == baseline).Select(e => $"{e.Page}|{e.Slot}|{e.Command}|{e.Parameter}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lines = new List<string> { $"Vergleichsbasis: {baseline}" };
        foreach (var profile in profiles.Skip(1))
        {
            var keys = entries.Where(e => e.Profile == profile).Select(e => $"{e.Page}|{e.Slot}|{e.Command}|{e.Parameter}").ToHashSet(StringComparer.OrdinalIgnoreCase);
            lines.Add($"{profile}: {keys.Count} Tasten · +{keys.Except(baseKeys).Count()} hinzugefügt · -{baseKeys.Except(keys).Count()} fehlend");
        }
        StreamDeckDiagnosticsBox.Text = string.Join(Environment.NewLine, lines);
    }

    private string StreamDeckAutomationRulesFile => Path.Combine(StreamDeckActionsDirectory, "streamdeck-automation-rules.json");
    private string StreamDeckRuleTemplatesFile => Path.Combine(StreamDeckActionsDirectory, "streamdeck-rule-templates.json");
    private string StreamDeckStableStateFile => Path.Combine(StreamDeckActionsDirectory, "streamdeck-stable-state.json");

    private sealed class StreamDeckAutomationRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Condition { get; set; } = "stream.live";
        public string Condition2 { get; set; } = string.Empty;
        public string LogicalOperator { get; set; } = "and";
        public string Profile { get; set; } = "Standard";
        public string Page { get; set; } = "Hauptseite";
        public int Priority { get; set; } = 100;
        public int DelaySeconds { get; set; }
        public int HoldSeconds { get; set; } = 10;
        public string Time { get; set; } = "20:00";
        public bool IsFallback { get; set; }
        public bool Enabled { get; set; } = true;
        public string Group { get; set; } = "Standard";
        public string ActiveDays { get; set; } = "Mo,Di,Mi,Do,Fr,Sa,So";
        public string ActiveWindow { get; set; } = "00:00-23:59";
        public DateTimeOffset? LastAppliedAt { get; set; }
        public DateTimeOffset? LastEvaluatedAt { get; set; }
        public int MatchCount { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public int ConsecutiveFailures { get; set; }
        public string LastError { get; set; } = string.Empty;
        public string DisabledReason { get; set; } = string.Empty;
    }

    private List<StreamDeckAutomationRule> LoadStreamDeckAutomationRules()
    {
        try
        {
            if (!File.Exists(StreamDeckAutomationRulesFile)) return new List<StreamDeckAutomationRule>();
            return JsonSerializer.Deserialize<List<StreamDeckAutomationRule>>(File.ReadAllText(StreamDeckAutomationRulesFile)) ?? new List<StreamDeckAutomationRule>();
        }
        catch { return new List<StreamDeckAutomationRule>(); }
    }

    private async Task SaveStreamDeckAutomationRulesAsync(List<StreamDeckAutomationRule> rules)
    {
        Directory.CreateDirectory(StreamDeckActionsDirectory);
        await File.WriteAllTextAsync(StreamDeckAutomationRulesFile, JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void RefreshStreamDeckAutomationRules()
    {
        StreamDeckRulesList.Items.Clear();
        foreach (var rule in LoadStreamDeckAutomationRules().OrderByDescending(r => r.Priority))
        {
            var delay = rule.DelaySeconds > 0 ? $" · +{rule.DelaySeconds}s" : string.Empty;
            var fallback = rule.IsFallback ? " · Fallback" : string.Empty;
            var health = rule.Enabled ? $" · OK {rule.SuccessCount}/F {rule.FailureCount}" : $" · DEAKTIVIERT{(string.IsNullOrWhiteSpace(rule.DisabledReason) ? string.Empty : $": {rule.DisabledReason}")}";
            var group = string.IsNullOrWhiteSpace(rule.Group) ? "Standard" : rule.Group;
            var second = string.IsNullOrWhiteSpace(rule.Condition2) ? string.Empty : $" {rule.LogicalOperator.ToUpperInvariant()} {rule.Condition2}";
            var hold = rule.HoldSeconds > 0 ? $" · Sperre {rule.HoldSeconds}s" : string.Empty;
            var time = rule.Condition == "time.reached" ? $" · {rule.Time}" : string.Empty;
            StreamDeckRulesList.Items.Add(new ListBoxItem
            {
                Tag = rule.Id,
                Content = $"[{group}] P{rule.Priority} · {rule.Condition}{second}{time} → {rule.Profile} / {rule.Page}{delay}{hold}{fallback}{health}"
            });
        }
    }

    private async Task AddStreamDeckAutomationRuleAsync()
    {
        try
        {
            var condition = (StreamDeckRuleConditionBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "stream.live";
            var condition2 = (StreamDeckRuleCondition2Box.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
            var logicalOperator = (StreamDeckRuleOperatorBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "and";
            var profile = string.IsNullOrWhiteSpace(StreamDeckRuleProfileBox.Text) ? "Standard" : StreamDeckRuleProfileBox.Text.Trim();
            var page = string.IsNullOrWhiteSpace(StreamDeckRulePageBox.Text) ? "Hauptseite" : StreamDeckRulePageBox.Text.Trim();
            if (!int.TryParse(StreamDeckRulePriorityBox.Text, out var priority) || priority is < 0 or > 1000) throw new InvalidOperationException("Die Regelpriorität muss zwischen 0 und 1000 liegen.");
            if (!int.TryParse(StreamDeckRuleDelayBox.Text, out var delay) || delay is < 0 or > 3600) throw new InvalidOperationException("Die Verzögerung muss zwischen 0 und 3600 Sekunden liegen.");
            if (!int.TryParse(StreamDeckRuleHoldBox.Text, out var hold) || hold is < 0 or > 3600) throw new InvalidOperationException("Die Sperrzeit muss zwischen 0 und 3600 Sekunden liegen.");
            if (condition == "time.reached" && !TimeOnly.TryParse(StreamDeckRuleTimeBox.Text.Trim(), out _)) throw new InvalidOperationException("Die Uhrzeit muss im Format HH:mm eingetragen werden.");
            var rules = LoadStreamDeckAutomationRules();
            var group = string.IsNullOrWhiteSpace(StreamDeckRuleGroupBox.Text) ? "Standard" : StreamDeckRuleGroupBox.Text.Trim();
            var days = string.IsNullOrWhiteSpace(StreamDeckRuleDaysBox.Text) ? "Mo,Di,Mi,Do,Fr,Sa,So" : StreamDeckRuleDaysBox.Text.Trim();
            var window = string.IsNullOrWhiteSpace(StreamDeckRuleWindowBox.Text) ? "00:00-23:59" : StreamDeckRuleWindowBox.Text.Trim();
            if (!IsValidStreamDeckRuleWindow(window)) throw new InvalidOperationException("Der Aktivitätszeitraum muss im Format HH:mm-HH:mm eingetragen werden.");
            rules.Add(new StreamDeckAutomationRule { Condition = condition, Condition2 = condition2, LogicalOperator = logicalOperator, Profile = profile, Page = page, Priority = priority, DelaySeconds = delay, HoldSeconds = hold, Time = StreamDeckRuleTimeBox.Text.Trim(), IsFallback = StreamDeckRuleFallbackBox.IsChecked == true, Group = group, ActiveDays = days, ActiveWindow = window });
            await SaveStreamDeckAutomationRulesAsync(rules);
            RefreshStreamDeckAutomationRules();
            StreamDeckRuleStatusText.Text = $"Regel gespeichert: {condition} → {profile} / {page}";
            StreamDeckRuleStatusText.Foreground = Brushes.LightGreen;
        }
        catch (Exception ex)
        {
            StreamDeckRuleStatusText.Text = ex.Message;
            StreamDeckRuleStatusText.Foreground = Brushes.IndianRed;
        }
    }

    private void DeleteSelectedStreamDeckAutomationRule()
    {
        if (StreamDeckRulesList.SelectedItem is not ListBoxItem item || item.Tag is not string id) { StreamDeckRuleStatusText.Text = "Bitte zuerst eine Regel auswählen."; return; }
        var rules = LoadStreamDeckAutomationRules();
        rules.RemoveAll(rule => string.Equals(rule.Id, id, StringComparison.OrdinalIgnoreCase));
        SaveStreamDeckAutomationRulesAsync(rules).GetAwaiter().GetResult();
        _streamDeckRuleFirstMatch.Remove(id);
        RefreshStreamDeckAutomationRules();
        StreamDeckRuleStatusText.Text = "Regel gelöscht.";
    }

    private bool IsStreamDeckConditionMatch(string condition, StreamDeckAutomationRule rule, Dictionary<string, bool> states)
    {
        return condition switch
        {
            "stream.live" => states.GetValueOrDefault("stream.live"),
            "stream.offline" => !states.GetValueOrDefault("stream.live"),
            "obs.connected" => states.GetValueOrDefault("obs.connected"),
            "obs.disconnected" => !states.GetValueOrDefault("obs.connected"),
            "spotify.playing" => states.GetValueOrDefault("spotify.playing"),
            "spotify.paused" => !states.GetValueOrDefault("spotify.playing"),
            "time.reached" => TimeOnly.TryParse(rule.Time, out var target) && TimeOnly.FromDateTime(DateTime.Now).Hour == target.Hour && TimeOnly.FromDateTime(DateTime.Now).Minute == target.Minute,
            _ => false
        };
    }

    private bool IsStreamDeckRuleMatch(StreamDeckAutomationRule rule, Dictionary<string, bool> states)
    {
        var first = IsStreamDeckConditionMatch(rule.Condition, rule, states);
        if (string.IsNullOrWhiteSpace(rule.Condition2)) return first;
        var second = IsStreamDeckConditionMatch(rule.Condition2, rule, states);
        return string.Equals(rule.LogicalOperator, "or", StringComparison.OrdinalIgnoreCase) ? first || second : first && second;
    }

    private static bool IsValidStreamDeckRuleWindow(string value)
    {
        var parts = value.Split('-', StringSplitOptions.TrimEntries);
        return parts.Length == 2 && TimeOnly.TryParse(parts[0], out _) && TimeOnly.TryParse(parts[1], out _);
    }

    private static bool IsStreamDeckRuleScheduleActive(StreamDeckAutomationRule rule, DateTime now)
    {
        var day = now.DayOfWeek switch { DayOfWeek.Monday => "Mo", DayOfWeek.Tuesday => "Di", DayOfWeek.Wednesday => "Mi", DayOfWeek.Thursday => "Do", DayOfWeek.Friday => "Fr", DayOfWeek.Saturday => "Sa", _ => "So" };
        var days = (rule.ActiveDays ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (days.Length > 0 && !days.Contains(day, StringComparer.OrdinalIgnoreCase)) return false;
        var parts = (rule.ActiveWindow ?? "00:00-23:59").Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !TimeOnly.TryParse(parts[0], out var start) || !TimeOnly.TryParse(parts[1], out var end)) return true;
        var current = TimeOnly.FromDateTime(now);
        return start <= end ? current >= start && current <= end : current >= start || current <= end;
    }

    private void AddStreamDeckRuleHistory(string message)
    {
        _streamDeckRuleHistory.Insert(0, $"{DateTime.Now:HH:mm:ss} · {message}");
        if (_streamDeckRuleHistory.Count > 30) _streamDeckRuleHistory.RemoveRange(30, _streamDeckRuleHistory.Count - 30);
        if (StreamDeckRuleHistoryBox is not null) StreamDeckRuleHistoryBox.Text = string.Join(Environment.NewLine, _streamDeckRuleHistory);
    }

    private void TestStreamDeckAutomationRules()
    {
        var rules = LoadStreamDeckAutomationRules();
        var issues = new List<string>();
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Profile) || string.IsNullOrWhiteSpace(rule.Page)) issues.Add($"{rule.Id}: Zielprofil oder Zielseite fehlt.");
            if (rule.Priority is < 0 or > 1000) issues.Add($"{rule.Id}: Priorität außerhalb 0–1000.");
            if (rule.DelaySeconds is < 0 or > 3600 || rule.HoldSeconds is < 0 or > 3600) issues.Add($"{rule.Id}: Verzögerung oder Sperrzeit ungültig.");
            if (rule.Condition == "time.reached" && !TimeOnly.TryParse(rule.Time, out _)) issues.Add($"{rule.Id}: Uhrzeit ungültig.");
            if (!IsValidStreamDeckRuleWindow(rule.ActiveWindow)) issues.Add($"{rule.Id}: Aktivitätszeitraum ungültig.");
            if (string.IsNullOrWhiteSpace(rule.Group)) issues.Add($"{rule.Id}: Regelgruppe fehlt.");
        }
        StreamDeckRuleStatusText.Text = issues.Count == 0 ? $"Regeltest erfolgreich: {rules.Count} Regel(n) sind formal gültig." : string.Join(Environment.NewLine, issues);
        StreamDeckRuleStatusText.Foreground = issues.Count == 0 ? Brushes.LightGreen : Brushes.IndianRed;
    }

    private async Task EvaluateStreamDeckAutomationRulesAsync(bool showConfirmation, bool previewOnly = false)
    {
        if (StreamDeckAutomationManualLockBox?.IsChecked == true)
        {
            if (showConfirmation) StreamDeckRuleStatusText.Text = "Automatische Umschaltung ist manuell gesperrt.";
            AddStreamDeckRuleHistory("Auswertung übersprungen: manuelle Sperre aktiv");
            return;
        }

        var allRules = LoadStreamDeckAutomationRules();
        var rules = allRules.Where(r => r.Enabled && IsStreamDeckRuleScheduleActive(r, DateTime.Now)).OrderByDescending(r => r.Priority).ToList();
        if (rules.Count == 0) { if (showConfirmation) StreamDeckRuleStatusText.Text = "Es sind keine aktuell aktiven Automatikregeln vorhanden."; return; }
        var states = GetStreamDeckRuntimeStates();
        var now = DateTimeOffset.Now;
        StreamDeckAutomationRule? winner = null;
        foreach (var rule in rules)
        {
            rule.LastEvaluatedAt = now;
            var matched = IsStreamDeckRuleMatch(rule, states);
            if (!matched) { _streamDeckRuleFirstMatch.Remove(rule.Id); continue; }
            rule.MatchCount++;
            if (!_streamDeckRuleFirstMatch.TryGetValue(rule.Id, out var firstMatch)) { _streamDeckRuleFirstMatch[rule.Id] = now; firstMatch = now; }
            if ((now - firstMatch).TotalSeconds < rule.DelaySeconds) continue;
            winner = rule;
            break;
        }
        winner ??= rules.FirstOrDefault(r => r.IsFallback);
        if (winner is null)
        {
            await SaveStreamDeckAutomationRulesAsync(allRules);
            if (showConfirmation) StreamDeckRuleStatusText.Text = "Keine Regel trifft aktuell zu.";
            AddStreamDeckRuleHistory("Keine passende Regel");
            return;
        }
        var lastApplied = allRules.Where(r => r.LastAppliedAt.HasValue).MaxBy(r => r.LastAppliedAt);
        if (lastApplied?.LastAppliedAt is DateTimeOffset last && (now - last).TotalSeconds < lastApplied.HoldSeconds && !string.Equals(lastApplied.Id, winner.Id, StringComparison.OrdinalIgnoreCase))
        {
            await SaveStreamDeckAutomationRulesAsync(allRules);
            if (showConfirmation) StreamDeckRuleStatusText.Text = $"Regelwechsel gesperrt: {lastApplied.Profile} / {lastApplied.Page} bleibt noch {Math.Ceiling(lastApplied.HoldSeconds - (now - last).TotalSeconds)} Sekunden aktiv.";
            return;
        }
        if (previewOnly)
        {
            await SaveStreamDeckAutomationRulesAsync(allRules);
            StreamDeckRuleStatusText.Text = $"Vorschau: {winner.Profile} / {winner.Page} würde durch {winner.Condition}{(string.IsNullOrWhiteSpace(winner.Condition2) ? string.Empty : $" {winner.LogicalOperator.ToUpperInvariant()} {winner.Condition2}")} aktiviert.";
            StreamDeckRuleStatusText.Foreground = Brushes.LightSkyBlue;
            AddStreamDeckRuleHistory($"Vorschau: [{winner.Group}] {winner.Profile} / {winner.Page}");
            return;
        }
        try
        {
            var stateFile = StreamDeckStateFile;
            var current = File.Exists(stateFile) ? File.ReadAllText(stateFile) : string.Empty;
            if (current.Contains($"\"activeProfile\": \"{winner.Profile}\"", StringComparison.OrdinalIgnoreCase) && current.Contains($"\"activePage\": \"{winner.Page}\"", StringComparison.OrdinalIgnoreCase))
            {
                winner.ConsecutiveFailures = 0;
                winner.LastError = string.Empty;
                await SaveStreamDeckAutomationRulesAsync(allRules);
                if (showConfirmation) StreamDeckRuleStatusText.Text = $"Bereits aktiv: {winner.Profile} / {winner.Page}";
                return;
            }
            if (File.Exists(stateFile)) File.Copy(stateFile, StreamDeckStableStateFile, true);
            Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
            File.WriteAllText(stateFile, JsonSerializer.Serialize(new { activeProfile = winner.Profile, activePage = winner.Page, changedAt = now, changedBy = "automation", ruleId = winner.Id }, new JsonSerializerOptions { WriteIndented = true }));
            winner.LastAppliedAt = now;
            winner.SuccessCount++;
            winner.ConsecutiveFailures = 0;
            winner.LastError = string.Empty;
            await SaveStreamDeckAutomationRulesAsync(allRules);
            var message = $"Automatisch aktiviert: {winner.Profile} / {winner.Page} ({winner.Condition}, Priorität {winner.Priority})";
            StreamDeckRuleStatusText.Text = message;
            StreamDeckRuleStatusText.Foreground = Brushes.LightGreen;
            AddStreamDeckRuleHistory($"Aktiviert: [{winner.Group}] {winner.Profile} / {winner.Page}");
            if (showConfirmation || StreamDeckRuleNotifyOnSwitchBox?.IsChecked == true) StreamDeckActionCreateStatusText.Text = message;
        }
        catch (Exception ex)
        {
            winner.FailureCount++;
            winner.ConsecutiveFailures++;
            winner.LastError = ex.Message;
            var threshold = int.TryParse(StreamDeckRuleFailureThresholdBox?.Text, out var parsed) ? Math.Clamp(parsed, 1, 100) : 3;
            if (StreamDeckRuleAutoDisableBox?.IsChecked == true && winner.ConsecutiveFailures >= threshold)
            {
                winner.Enabled = false;
                winner.DisabledReason = $"Automatisch nach {winner.ConsecutiveFailures} Fehlern deaktiviert";
            }
            await SaveStreamDeckAutomationRulesAsync(allRules);
            StreamDeckRuleStatusText.Text = $"Regelfehler [{winner.Group}]: {ex.Message}";
            StreamDeckRuleStatusText.Foreground = Brushes.IndianRed;
            AddStreamDeckRuleHistory($"FEHLER: [{winner.Group}] {ex.Message}");
            RefreshStreamDeckAutomationRules();
        }
    }

    private async Task SaveSelectedStreamDeckRuleTemplateAsync()
    {
        if (StreamDeckRulesList.SelectedItem is not ListBoxItem item || item.Tag is not string id) { StreamDeckRuleStatusText.Text = "Bitte zuerst eine Regel auswählen."; return; }
        var rule = LoadStreamDeckAutomationRules().FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
        if (rule is null) return;
        var templates = LoadStreamDeckRuleTemplates();
        var clone = JsonSerializer.Deserialize<StreamDeckAutomationRule>(JsonSerializer.Serialize(rule)) ?? new StreamDeckAutomationRule();
        clone.Id = Guid.NewGuid().ToString("N");
        clone.LastAppliedAt = null;
        templates.Add(clone);
        await File.WriteAllTextAsync(StreamDeckRuleTemplatesFile, JsonSerializer.Serialize(templates, new JsonSerializerOptions { WriteIndented = true }));
        StreamDeckRuleStatusText.Text = $"Regelvorlage gespeichert: [{clone.Group}] {clone.Condition} → {clone.Profile} / {clone.Page}";
    }

    private List<StreamDeckAutomationRule> LoadStreamDeckRuleTemplates()
    {
        try { return File.Exists(StreamDeckRuleTemplatesFile) ? JsonSerializer.Deserialize<List<StreamDeckAutomationRule>>(File.ReadAllText(StreamDeckRuleTemplatesFile)) ?? [] : []; }
        catch { return []; }
    }

    private async Task LoadStreamDeckRuleTemplateAsync()
    {
        var template = LoadStreamDeckRuleTemplates().LastOrDefault();
        if (template is null) { StreamDeckRuleStatusText.Text = "Es ist noch keine Regelvorlage gespeichert."; return; }
        var rules = LoadStreamDeckAutomationRules();
        template.Id = Guid.NewGuid().ToString("N"); template.LastAppliedAt = null;
        rules.Add(template); await SaveStreamDeckAutomationRulesAsync(rules); RefreshStreamDeckAutomationRules();
        StreamDeckRuleStatusText.Text = $"Letzte Regelvorlage geladen: {template.Profile} / {template.Page}";
    }

    private void ExportStreamDeckRuleSet()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Stream-Deck-Regelset (*.sdrules)|*.sdrules", FileName = "streamdeck-regelset.sdrules" };
        if (dialog.ShowDialog() != true) return;
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(LoadStreamDeckAutomationRules(), new JsonSerializerOptions { WriteIndented = true }));
        StreamDeckRuleStatusText.Text = $"Regelset exportiert: {dialog.FileName}";
    }

    private async Task ImportStreamDeckRuleSetAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Stream-Deck-Regelset (*.sdrules)|*.sdrules|JSON (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var imported = JsonSerializer.Deserialize<List<StreamDeckAutomationRule>>(File.ReadAllText(dialog.FileName)) ?? [];
            foreach (var rule in imported) { rule.Id = Guid.NewGuid().ToString("N"); rule.LastAppliedAt = null; }
            var rules = LoadStreamDeckAutomationRules(); rules.AddRange(imported); await SaveStreamDeckAutomationRulesAsync(rules); RefreshStreamDeckAutomationRules();
            StreamDeckRuleStatusText.Text = $"{imported.Count} Regel(n) importiert.";
        }
        catch (Exception ex) { StreamDeckRuleStatusText.Text = $"Import fehlgeschlagen: {ex.Message}"; StreamDeckRuleStatusText.Foreground = Brushes.IndianRed; }
    }

    private void AnalyzeStreamDeckRuleConflicts()
    {
        var rules = LoadStreamDeckAutomationRules().Where(r => r.Enabled).ToList();
        var conflicts = new List<string>();
        for (var i = 0; i < rules.Count; i++) for (var j = i + 1; j < rules.Count; j++)
        {
            var a = rules[i]; var b = rules[j];
            if (a.Priority != b.Priority || a.IsFallback || b.IsFallback) continue;
            var sameCondition = string.Equals(a.Condition, b.Condition, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Condition2, b.Condition2, StringComparison.OrdinalIgnoreCase) && string.Equals(a.LogicalOperator, b.LogicalOperator, StringComparison.OrdinalIgnoreCase);
            if (sameCondition && (!string.Equals(a.Profile, b.Profile, StringComparison.OrdinalIgnoreCase) || !string.Equals(a.Page, b.Page, StringComparison.OrdinalIgnoreCase)))
                conflicts.Add($"P{a.Priority}: [{a.Group}] {a.Profile}/{a.Page} kollidiert mit [{b.Group}] {b.Profile}/{b.Page}.");
        }
        StreamDeckRuleStatusText.Text = conflicts.Count == 0 ? "Konfliktanalyse abgeschlossen: keine direkten Prioritätskonflikte gefunden." : string.Join(Environment.NewLine, conflicts);
        StreamDeckRuleStatusText.Foreground = conflicts.Count == 0 ? Brushes.LightGreen : Brushes.Orange;
    }

    private void RestoreStableStreamDeckState()
    {
        if (!File.Exists(StreamDeckStableStateFile)) { StreamDeckRuleStatusText.Text = "Es wurde noch kein stabiler Stream-Deck-Zustand gespeichert."; return; }
        File.Copy(StreamDeckStableStateFile, StreamDeckStateFile, true);
        StreamDeckRuleStatusText.Text = "Letztes stabiles Profil und letzte stabile Seite wurden wiederhergestellt.";
        StreamDeckRuleStatusText.Foreground = Brushes.LightGreen;
        AddStreamDeckRuleHistory("Stabiler Zustand manuell wiederhergestellt");
    }


    private void ShowStreamDeckRuleStatistics()
    {
        var rules = LoadStreamDeckAutomationRules();
        if (rules.Count == 0) { StreamDeckRuleStatusText.Text = "Keine Regeln für eine Statistik vorhanden."; return; }
        var enabled = rules.Count(r => r.Enabled);
        var matches = rules.Sum(r => r.MatchCount);
        var successes = rules.Sum(r => r.SuccessCount);
        var failures = rules.Sum(r => r.FailureCount);
        var mostUsed = rules.OrderByDescending(r => r.SuccessCount).FirstOrDefault();
        StreamDeckRuleStatusText.Text = $"Regelstatistik: {enabled}/{rules.Count} aktiv · Treffer {matches} · Umschaltungen {successes} · Fehler {failures}" +
            (mostUsed is null ? string.Empty : $" · Häufigste Regel: [{mostUsed.Group}] {mostUsed.Profile}/{mostUsed.Page} ({mostUsed.SuccessCount})");
        StreamDeckRuleStatusText.Foreground = failures == 0 ? Brushes.LightGreen : Brushes.Orange;
    }

    private async Task ResetStreamDeckRuleStatisticsAsync()
    {
        var rules = LoadStreamDeckAutomationRules();
        foreach (var rule in rules)
        {
            rule.MatchCount = 0; rule.SuccessCount = 0; rule.FailureCount = 0; rule.ConsecutiveFailures = 0;
            rule.LastError = string.Empty; rule.LastEvaluatedAt = null;
        }
        await SaveStreamDeckAutomationRulesAsync(rules);
        RefreshStreamDeckAutomationRules();
        StreamDeckRuleStatusText.Text = "Ausführungsstatistik und Fehlerzähler wurden zurückgesetzt.";
    }

    private void ExportStreamDeckRuleDiagnostics()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Diagnosebericht (*.json)|*.json", FileName = $"streamdeck-regeldiagnose-{DateTime.Now:yyyyMMdd-HHmmss}.json" };
        if (dialog.ShowDialog() != true) return;
        var rules = LoadStreamDeckAutomationRules();
        var report = new
        {
            generatedAt = DateTimeOffset.Now,
            suiteVersion = "6.5.0",
            automationLocked = StreamDeckAutomationManualLockBox?.IsChecked == true,
            summary = new { total = rules.Count, enabled = rules.Count(r => r.Enabled), matches = rules.Sum(r => r.MatchCount), successes = rules.Sum(r => r.SuccessCount), failures = rules.Sum(r => r.FailureCount) },
            rules,
            recentDecisions = _streamDeckRuleHistory.TakeLast(30).ToArray()
        };
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        StreamDeckRuleStatusText.Text = $"Diagnosebericht exportiert: {dialog.FileName}";
        StreamDeckRuleStatusText.Foreground = Brushes.LightGreen;
    }

    private async Task CreateStreamDeckActionAsync()
    {
        try
        {
            var title = string.IsNullOrWhiteSpace(StreamDeckActionTitleBox.Text) ? "Neue Aktion" : StreamDeckActionTitleBox.Text.Trim();
            var item = StreamDeckActionCommandBox.SelectedItem as ComboBoxItem;
            var command = item?.Tag?.ToString() ?? "workflow.prepare";
            var parameter = StreamDeckActionParameterBox.Text.Trim();
            var profile = string.IsNullOrWhiteSpace(StreamDeckProfileNameBox.Text) ? "Standard" : StreamDeckProfileNameBox.Text.Trim();
            var page = string.IsNullOrWhiteSpace(StreamDeckPageNameBox.Text) ? "Hauptseite" : StreamDeckPageNameBox.Text.Trim();
            var condition = (StreamDeckStateConditionBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
            var trueLabel = StreamDeckTrueLabelBox.Text.Trim();
            var falseLabel = StreamDeckFalseLabelBox.Text.Trim();
            var toggleMode = StreamDeckToggleModeBox.IsChecked == true;
            var alternateCommand = (StreamDeckAlternateCommandBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
            var alternateParameter = StreamDeckAlternateParameterBox.Text.Trim();
            if (!int.TryParse(StreamDeckStepDelayBox.Text, out var stepDelayMs) || stepDelayMs < 0 || stepDelayMs > 10000) throw new InvalidOperationException("Die Schrittverzögerung muss zwischen 0 und 10000 ms liegen.");
            if (!int.TryParse(StreamDeckRetryCountBox.Text, out var retryCount) || retryCount < 0 || retryCount > 5) throw new InvalidOperationException("Die Wiederholungszahl muss zwischen 0 und 5 liegen.");
            if (!int.TryParse(StreamDeckCooldownBox.Text, out var cooldownMs) || cooldownMs < 0 || cooldownMs > 60000) throw new InvalidOperationException("Die Tastensperre muss zwischen 0 und 60000 ms liegen.");
            if (toggleMode && string.IsNullOrWhiteSpace(condition)) throw new InvalidOperationException("Für eine Toggle-Taste muss eine Zustandsbindung ausgewählt werden.");
            if (!int.TryParse(StreamDeckSlotBox.Text, out var slot) || slot < 1 || slot > 32) throw new InvalidOperationException("Die Position muss zwischen 1 und 32 liegen.");
            var steps = new List<(string Command, string Parameter)>();
            foreach (var line in StreamDeckMultiActionBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|', 2);
                var stepCommand = parts[0].Trim();
                if (string.IsNullOrWhiteSpace(stepCommand)) continue;
                steps.Add((stepCommand, parts.Length > 1 ? parts[1].Trim() : string.Empty));
            }
            if (steps.Count == 0) steps.Add((command, parameter));
            if (steps.Count > 20) throw new InvalidOperationException("Eine Mehrfachaktion darf höchstens 20 Schritte enthalten.");

            var safeName = string.Concat(title.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).Trim();
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "Neue Aktion";
            Directory.CreateDirectory(StreamDeckActionsDirectory);
            var clientPath = Path.Combine(AppContext.BaseDirectory, "CreatorControlSuite.CommandClient.exe");
            var cmdPath = Path.Combine(StreamDeckActionsDirectory, safeName + ".cmd");
            var content = new StringBuilder("@echo off\r\n");
            if (toggleMode)
            {
                var stateExpression = condition switch
                {
                    "stream.live" => "$s.stream.isLive",
                    "obs.connected" => "$s.obs.connected",
                    "spotify.playing" => "$s.spotify.isPlaying",
                    _ => "$false"
                };
                content.AppendLine($"powershell -NoProfile -ExecutionPolicy Bypass -Command \"$s=Get-Content -Raw '{StreamDeckRuntimeStateFile.Replace("'", "''")}'|ConvertFrom-Json; if({stateExpression}){{exit 0}}else{{exit 1}}\"");
                content.AppendLine("if errorlevel 1 goto stateoff");
                var alternateArgs = string.IsNullOrWhiteSpace(alternateParameter) ? alternateCommand : $"{alternateCommand} value=\"{alternateParameter.Replace("\"", "\"\"")}\"";
                content.AppendLine($"start \"\" /wait /min \"{clientPath}\" {alternateArgs}");
                content.AppendLine("goto end");
                content.AppendLine(":stateoff");
            }
            var stepNumber = 0;
            foreach (var step in steps)
            {
                stepNumber++;
                var args = string.IsNullOrWhiteSpace(step.Parameter) ? step.Command : $"{step.Command} value=\"{step.Parameter.Replace("\"", "\"\"")}\"";
                var successLabel = $"step_{stepNumber}_ok";
                for (var attempt = 0; attempt <= retryCount; attempt++)
                {
                    content.AppendLine($"start \"\" /wait /min \"{clientPath}\" {args}");
                    content.AppendLine($"if not errorlevel 1 goto {successLabel}");
                }
                content.AppendLine($":{successLabel}");
                if (stepDelayMs > 0) content.AppendLine($"powershell -NoProfile -Command \"Start-Sleep -Milliseconds {stepDelayMs}\"");
            }
            if (toggleMode) content.AppendLine(":end");
            if (cooldownMs > 0) content.AppendLine($"powershell -NoProfile -Command \"Start-Sleep -Milliseconds {cooldownMs}\"");
            await File.WriteAllTextAsync(cmdPath, content.ToString());
            var meta = new { title, command = steps[0].Command, parameter = steps[0].Parameter, profile, page, slot, steps = steps.Select(step => new { command = step.Command, parameter = step.Parameter }).ToArray(), locked = false, condition, trueLabel, falseLabel, toggleMode, alternateCommand, alternateParameter, stepDelayMs, retryCount, cooldownMs, createdAt = DateTimeOffset.Now };
            await File.WriteAllTextAsync(Path.ChangeExtension(cmdPath, ".json"), System.Text.Json.JsonSerializer.Serialize(meta, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            StreamDeckActionCreateStatusText.Text = $"Aktionstaste erstellt: {cmdPath}";
            StreamDeckActionCreateStatusText.Foreground = new SolidColorBrush(Color.FromRgb(92, 184, 92));
            RefreshStreamDeckActionsList();
        }
        catch (Exception ex)
        {
            StreamDeckActionCreateStatusText.Text = ex.Message;
            StreamDeckActionCreateStatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 90, 90));
        }
    }

    private void OpenStreamDeckActionsFolder()
    {
        Directory.CreateDirectory(StreamDeckActionsDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", StreamDeckActionsDirectory) { UseShellExecute = true });
    }

    private void DeleteSelectedStreamDeckAction()
    {
        if (StreamDeckCreatedActionsList.SelectedItem is not ListBoxItem item || item.Tag is not string file) return;
        if (File.Exists(file)) File.Delete(file);
        var json = Path.ChangeExtension(file, ".json");
        if (File.Exists(json)) File.Delete(json);
        RefreshStreamDeckActionsList();
    }

    private async Task ExportStreamDeckProfileAsync()
    {
        try
        {
            var package =
                await _streamDeckModule.BuildDefaultProfileAsync();

            StreamDeckStatusText.Text =
                "Profil exportiert: " + package.Path;

            StreamDeckStatusText.Foreground =
                System.Windows.Media.Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            StreamDeckStatusText.Text = exception.Message;
            StreamDeckStatusText.Foreground =
                System.Windows.Media.Brushes.IndianRed;
        }
    }

    private void OpenLocalDataFolder(string child)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "CreatorControlSuite",
            child);

        Directory.CreateDirectory(path);

        Process.Start(
            new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
    }

    private async Task CheckUpdatesAsync(bool silent = false)
    {
        try
        {
            InstallUpdateButton.IsEnabled = false;
            _pendingUpdatePackage = null;

            if (!silent)
            {
                UpdateStatusText.Text = "Suche nach Updates …";
                UpdateStatusText.Foreground = System.Windows.Media.Brushes.Gray;
            }

            var result = await _updateService.CheckAsync();
            _pendingUpdatePackage = result.Package;
            InstallUpdateButton.IsEnabled = result.UpdateAvailable && result.Package is not null;

            if (result.UpdateAvailable && result.Package is not null)
            {
                var notes = string.IsNullOrWhiteSpace(result.Package.ReleaseNotes)
                    ? string.Empty
                    : " — " + Truncate(result.Package.ReleaseNotes, 160);
                UpdateStatusText.Text =
                    $"Update verfügbar: {result.Package.Version} (aktuell {result.CurrentVersion}){notes}";
                UpdateStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
            }
            else
            {
                UpdateStatusText.Text = result.Detail;
                UpdateStatusText.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }
        catch (Exception exception)
        {
            UpdateStatusText.Text = exception.Message;
            UpdateStatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
            InstallUpdateButton.IsEnabled = false;
            _pendingUpdatePackage = null;
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (_pendingUpdatePackage is null)
        {
            UpdateStatusText.Text = "Kein Update ausgewählt. Bitte zuerst suchen.";
            UpdateStatusText.Foreground = System.Windows.Media.Brushes.Gray;
            return;
        }

        try
        {
            InstallUpdateButton.IsEnabled = false;
            CheckUpdatesButton.IsEnabled = false;
            UpdateStatusText.Text = "Update wird heruntergeladen …";
            UpdateStatusText.Foreground = System.Windows.Media.Brushes.Gray;

            var progress = new Progress<double>(value =>
            {
                UpdateStatusText.Text =
                    $"Update wird heruntergeladen … {value:P0}";
            });

            var packagePath = await _updateService.DownloadAsync(
                _pendingUpdatePackage,
                progress);

            if (_settings.Updates.BackupBeforeUpdate)
            {
                UpdateStatusText.Text = "Backup vor Update …";
                await _updateService.CreateBackupAsync(GetCurrentProductVersion());
                BackupsList.ItemsSource = await _updateService.ListBackupsAsync();
            }

            UpdateStatusText.Text = "Updater wird gestartet. Die App wird beendet …";
            await _updateService.ApplyAsync(packagePath);

            Application.Current.Shutdown(0);
        }
        catch (Exception exception)
        {
            UpdateStatusText.Text = exception.Message;
            UpdateStatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
            InstallUpdateButton.IsEnabled = _pendingUpdatePackage is not null;
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..maxLength].TrimEnd() + "…";
    }

    private void SelectUpdateChannelBox(string channel)
    {
        var normalized = string.IsNullOrWhiteSpace(channel) ? "Alpha" : channel.Trim();
        foreach (var item in UpdateChannelBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(
                    item.Content?.ToString(),
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
            {
                UpdateChannelBox.SelectedItem = item;
                return;
            }
        }

        UpdateChannelBox.SelectedIndex = 2;
    }

    private string GetSelectedUpdateChannel()
    {
        if (UpdateChannelBox.SelectedItem is ComboBoxItem item &&
            item.Content is string content &&
            !string.IsNullOrWhiteSpace(content))
        {
            return content.Trim();
        }

        return "Alpha";
    }

    private static string GetCurrentProductVersion()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var informationalVersion = assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var metadataSeparator = informationalVersion.IndexOf('+');
            return metadataSeparator >= 0
                ? informationalVersion[..metadataSeparator]
                : informationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    private async Task CreateBackupAsync()
    {
        try
        {
            var backup = await _updateService.CreateBackupAsync(
                GetCurrentProductVersion());

            BackupsList.ItemsSource =
                await _updateService.ListBackupsAsync();

            UpdateStatusText.Text =
                "Backup erstellt: " + backup.Path;

            UpdateStatusText.Foreground =
                System.Windows.Media.Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            UpdateStatusText.Text = exception.Message;
            UpdateStatusText.Foreground =
                System.Windows.Media.Brushes.IndianRed;
        }
    }

    private void RefreshConfiguredDashboardScenes()
    {
        var scenes = new[]
        {
            _settings.Obs.StartScene,
            _settings.Obs.LiveScene,
            _settings.Obs.PauseScene,
            _settings.Obs.EndScene
        }
        .Concat(_settings.AdditionalScenes)
        .Where(scene => !string.IsNullOrWhiteSpace(scene))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        DashboardSceneBox.ItemsSource = scenes;
        DashboardSceneBox.SelectedItem = scenes.FirstOrDefault(scene =>
            string.Equals(scene, _settings.Obs.LiveScene, StringComparison.OrdinalIgnoreCase));
    }

    private void AddDashboardViewerTrendSample(int viewers)
    {
        _dashboardViewerTrendSamples.Enqueue(Math.Max(0, viewers));

        while (_dashboardViewerTrendSamples.Count > 48)
        {
            _dashboardViewerTrendSamples.Dequeue();
        }

        DashboardViewerTrendLine.Points.Clear();
        var samples = _dashboardViewerTrendSamples.ToArray();

        if (samples.Length == 0)
        {
            return;
        }

        const double width = 430;
        const double height = 72;
        var maximum = Math.Max(1, samples.Max());

        for (var index = 0; index < samples.Length; index++)
        {
            var x = samples.Length == 1
                ? 0
                : width * index / (samples.Length - 1);
            var y = height - height * samples[index] / maximum;
            DashboardViewerTrendLine.Points.Add(new Point(x, y));
        }
    }

    private void OpenDashboardTwitchChat()
    {
        var channel = _twitchModule.GetSnapshot().ChannelName;

        if (string.IsNullOrWhiteSpace(channel))
        {
            channel = _settings.Twitch.ChannelName;
        }

        if (string.IsNullOrWhiteSpace(channel))
        {
            AddDashboardNotification(
                "Kein Twitch-Kanal für den Chat konfiguriert.",
                "Warnung");
            return;
        }

        OpenConfiguredTarget(
            $"https://www.twitch.tv/popout/{channel}/chat?popout=",
            "Twitch Chat");
    }

    private async Task SwitchDashboardSceneAsync()
    {
        if (DashboardSceneBox.SelectedItem is not string sceneName ||
            !_obsClient.IsConnected)
        {
            AddDashboardNotification(
                "OBS ist nicht verbunden oder es wurde keine Szene ausgewählt.",
                "Warnung");
            return;
        }

        await _obsClient.SetCurrentProgramSceneAsync(sceneName);
        await RefreshObsAsync();

        AddDashboardNotification(
            $"OBS-Szene gewechselt: {sceneName}",
            "Info");
    }

    private async Task SwitchDashboardNextSceneAsync()
    {
        var sceneName = DashboardNextSceneBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            sceneName = DashboardNextSceneBox.Text?.Trim();
        }
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            throw new InvalidOperationException("Keine nächste OBS-Szene ausgewählt.");
        }
        if (!_obsClient.IsConnected)
        {
            throw new InvalidOperationException("OBS ist nicht verbunden.");
        }

        await _obsClient.SetCurrentProgramSceneAsync(sceneName);
        DashboardSceneBox.SelectedItem = sceneName;
        DashboardCurrentSceneText.Text = sceneName;

        var scenes = (DashboardNextSceneBox.ItemsSource as IEnumerable<string>)?.ToList() ?? [];
        var followingScene = scenes.FirstOrDefault(scene =>
            !string.Equals(scene, sceneName, StringComparison.OrdinalIgnoreCase));
        if (followingScene is not null)
        {
            DashboardNextSceneBox.SelectedItem = followingScene;
        }
        await RefreshDashboardObsScenePreviewAsync(sceneName);
    }


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
            var followerTask = _twitchModule.GetFollowerCountAsync();
            var subscriptionTask = _twitchModule.GetActiveSubscriptionCountAsync();

            await Task.WhenAll(followerTask, subscriptionTask);

            _currentFollowerCount = Math.Max(0, followerTask.Result);
            _currentActiveSubscriptionCount =
                Math.Max(0, subscriptionTask.Result);

            _settings.Twitch.FollowerGoal.Current =
                _currentFollowerCount;
            _settings.Twitch.SubGoal.Current =
                _currentActiveSubscriptionCount;

            FollowerGoalCurrentBox.Text =
                _currentFollowerCount.ToString();
            SubGoalCurrentBox.Text =
                _currentActiveSubscriptionCount.ToString();

            DashboardFollowerTotalText.Text =
                $"Gesamt: {_currentFollowerCount}";
            DashboardHeroFollowerText.Text = _currentFollowerCount.ToString();
            DashboardChatAlertsText.Text = _currentActiveSubscriptionCount.ToString();
            DashboardTwitchGoalsText.Text =
                $"Follower-Ziel: {_currentFollowerCount:0} / {_settings.Twitch.FollowerGoal.Target:0} · " +
                $"Sub-Ziel: {_currentActiveSubscriptionCount:0} / {_settings.Twitch.SubGoal.Target:0}";

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
            var followerCount =
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

            var baseline = _streamFollowerBaseline > 0
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
                var twitch = root["twitch"] as JsonObject ?? new JsonObject();
                twitch["followers"] = _currentFollowerCount;
                twitch["followerGoal"] = _settings.Twitch.FollowerGoal.Target;
                var goal = twitch["followerGoalState"] as JsonObject ?? new JsonObject();
                goal["title"] = _settings.Twitch.FollowerGoal.Title;
                goal["current"] = _currentFollowerCount;
                goal["target"] = _settings.Twitch.FollowerGoal.Target;
                goal["fontFace"] = _settings.Twitch.FollowerGoal.FontFace;
                goal["fontSize"] = _settings.Twitch.FollowerGoal.FontSize;
                twitch["followerGoalState"] = goal;
                root["twitch"] = twitch;
            });
            DashboardTwitchGoalsText.Text =
                $"Follower-Ziel: {_currentFollowerCount:0} / {_settings.Twitch.FollowerGoal.Target:0} · " +
                $"Sub-Ziel: {_currentActiveSubscriptionCount:0} / {_settings.Twitch.SubGoal.Target:0}";
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

        var twitchSnapshot = _twitchModule.GetSnapshot();
        if (!twitchSnapshot.Authenticated)
        {
            _currentLiveViewerCount = 0;
            DashboardHeroViewerText.Text = "0";
            AddDashboardViewerTrendSample(0);
            RefreshTwitchProfessionalUi();
            return;
        }

        var channel = !string.IsNullOrWhiteSpace(twitchSnapshot.ChannelName)
            ? twitchSnapshot.ChannelName
            : twitchSnapshot.Login;

        if (string.IsNullOrWhiteSpace(channel))
        {
            return;
        }

        _liveViewerSampleRunning = true;

        try
        {
            var status = await _twitchModule.GetRaidTargetStatusAsync(channel);

            if (status is null || !status.IsOnline)
            {
                _currentLiveViewerCount = 0;
                DashboardHeroViewerText.Text = "0";
                AddDashboardViewerTrendSample(0);
                return;
            }

            _currentLiveViewerCount = Math.Max(0, status.ViewerCount);
            await _creatorIntelligence.RecordAsync("twitch.viewer.sample", new { viewers = _currentLiveViewerCount, scene = _servicesObsCurrentScene, category = status.GameName, title = status.StreamTitle });
            RefreshTwitchProfessionalUi(status);
            DashboardHeroViewerText.Text =
                $"{_currentLiveViewerCount} Zuschauer";

            await _workflowModule.Service.AddViewerSampleAsync(
                _currentLiveViewerCount);

            RefreshWorkflowUi(_workflowModule.Service.State);

            await _overlayModule.Service.UpdateAsync(data =>
            {
                data.Stream.ViewerCount = _currentLiveViewerCount;
            });
            await UpdateActiveOverlayJsonAsync(root =>
            {
                var stream = root["stream"] as JsonObject ?? new JsonObject();
                stream["viewerCount"] = _currentLiveViewerCount;
                stream["isLive"] = true;
                stream["startedAt"] = _streamSessionStartedAt;
                stream["elapsedSeconds"] = _streamSessionStartedAt.HasValue
                    ? Math.Max(0, (long)(DateTimeOffset.Now - _streamSessionStartedAt.Value).TotalSeconds)
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

    private async Task RefreshRaidTargetStatusAsync(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            SetRaidTargetStatusText("Kein Ziel ausgewählt");
            DashboardRaidAssistantText.Text = "Kein Raid-Ziel ausgewählt.";
            DashboardRaidLiveDurationText.Text = "Live-Dauer: -";
            ServicesTwitchRaidLiveDurationText.Text = "Live-Dauer: -";
            DashboardRaidProfileImage.Source = null;
            ServicesTwitchRaidProfileImage.Source = null;
            return;
        }

        try
        {
            var status = await _twitchModule.GetRaidTargetStatusAsync(channel.Trim());
            if (status is null)
            {
                SetRaidTargetStatusText($"{channel}: Kanal nicht gefunden");
                return;
            }

            var liveDuration = status.IsOnline && status.StartedAt is not null
                ? DateTimeOffset.Now - status.StartedAt.Value
                : TimeSpan.Zero;

            var text = status.IsOnline
                ? $"{status.DisplayName} ist ONLINE · {status.ViewerCount} Zuschauer · {status.GameName}" +
                  (string.IsNullOrWhiteSpace(status.StreamTitle) ? "" : $" · {status.StreamTitle}")
                : $"{status.DisplayName} ist OFFLINE";

            SetRaidTargetStatusText(text);
            DashboardRaidAssistantText.Text = text;
            DashboardRaidLiveDurationText.Text = status.IsOnline
                ? $"Live seit {FormatRaidLiveDuration(liveDuration)}"
                : "Live-Dauer: -";
            ServicesTwitchRaidLiveDurationText.Text = DashboardRaidLiveDurationText.Text;
            await LoadRaidProfileImageAsync(status.ProfileImageUrl);
        }
        catch (Exception ex)
        {
            SetRaidTargetStatusText($"Status nicht verfügbar: {ex.Message}");
        }
    }

    private async Task LoadRaidProfileImageAsync(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            DashboardRaidProfileImage.Source = null;
            ServicesTwitchRaidProfileImage.Source = null;
            return;
        }

        try
        {
            var bytes = await RaidProfileHttpClient.GetByteArrayAsync(imageUrl);
            await Dispatcher.InvokeAsync(() =>
            {
                using var stream = new MemoryStream(bytes);
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                DashboardRaidProfileImage.Source = image;
                ServicesTwitchRaidProfileImage.Source = image;
            });
        }
        catch
        {
            DashboardRaidProfileImage.Source = null;
            ServicesTwitchRaidProfileImage.Source = null;
        }
    }

    private static string FormatRaidLiveDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}:{duration.Minutes:00} Std.";
        }

        return $"{Math.Max(0, duration.Minutes)} Min.";
    }

    private void SetRaidTargetStatusText(string text)
    {
        DashboardRaidTargetStatusText.Text = text;
        ServicesTwitchRaidTargetStatusText.Text = text;
    }

    private void OpenSelectedRaidChannel()
    {
        var channel = DashboardRaidChannelBox.SelectedItem as string
                      ?? ServicesTwitchRaidTargetBox.SelectedItem as string
                      ?? _settings.Twitch.SelectedRaidChannel;

        if (string.IsNullOrWhiteSpace(channel))
        {
            MessageBox.Show("Bitte zuerst ein Raid-Ziel auswählen.", "Twitch");
            return;
        }

        var url = "https://www.twitch.tv/" + Uri.EscapeDataString(channel.Trim().TrimStart('@'));
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void RefreshRaidChannelSelectors()
    {
        var channels = _settings.Twitch.RaidChannels
            .Select(channel => channel.Trim().TrimStart('@'))
            .Where(channel => !string.IsNullOrWhiteSpace(channel))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(channel => channel, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _settings.Twitch.RaidChannels = channels;
        ServicesTwitchRaidChannelsBox.Text = string.Join(Environment.NewLine, channels);
        ServicesTwitchRaidChannelsList.ItemsSource = channels;
        DashboardRaidChannelBox.ItemsSource = channels;
        ServicesTwitchRaidTargetBox.ItemsSource = channels;
        DashboardRaidChannelBox.SelectedItem = channels.FirstOrDefault(channel => string.Equals(channel, _settings.Twitch.SelectedRaidChannel, StringComparison.OrdinalIgnoreCase));
        ServicesTwitchRaidTargetBox.SelectedItem = DashboardRaidChannelBox.SelectedItem;
    }

    private void UpdateDashboardRaidControlsVisibility()
    {
        var visibility = _settings.Twitch.RaidOnStreamEnd
            ? Visibility.Visible
            : Visibility.Collapsed;
        DashboardRaidSelectionPanel.Visibility = visibility;
        DashboardRaidStatusPanel.Visibility = visibility;
        DashboardOpenRaidChannelButton.Visibility = visibility;
    }

    private async Task AddRaidChannelAsync()
    {
        var channel = ServicesTwitchNewRaidChannelBox.Text.Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(channel))
        {
            return;
        }

        if (!_settings.Twitch.RaidChannels.Contains(channel, StringComparer.OrdinalIgnoreCase))
        {
            _settings.Twitch.RaidChannels.Add(channel);
        }

        _settings.Twitch.SelectedRaidChannel = channel;
        ServicesTwitchNewRaidChannelBox.Clear();
        RefreshRaidChannelSelectors();
        DashboardRaidChannelBox.SelectedItem = channel;
        ServicesTwitchRaidTargetBox.SelectedItem = channel;
        await _settingsStore.SaveAsync(_settings);
        await RefreshRaidTargetStatusAsync(channel);
    }

    private async Task RemoveSelectedRaidChannelAsync()
    {
        if (ServicesTwitchRaidChannelsList.SelectedItem is not string channel)
        {
            return;
        }

        _settings.Twitch.RaidChannels.RemoveAll(item => string.Equals(item, channel, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(_settings.Twitch.SelectedRaidChannel, channel, StringComparison.OrdinalIgnoreCase))
        {
            _settings.Twitch.SelectedRaidChannel = _settings.Twitch.RaidChannels.FirstOrDefault() ?? "";
        }

        RefreshRaidChannelSelectors();
        await _settingsStore.SaveAsync(_settings);
        await RefreshRaidTargetStatusAsync(_settings.Twitch.SelectedRaidChannel);
    }

    private void ApplySpotifyAutomationFieldsToSettings()
    {
        _settings.Workflow.AutoStartSpotifyPlaylist = ServicesSpotifyAutoStartOnStreamBox.IsChecked == true;
        _settings.Workflow.AutoPlayEndMusic = ServicesSpotifyEndMusicBox.IsChecked == true;
        _settings.Workflow.PauseSpotifyOnStreamEnd = ServicesSpotifyPauseOnStreamEndBox.IsChecked == true;
        _settings.Spotify.SetVolumeOnLiveTransition = ServicesSpotifySetLiveVolumeBox.IsChecked == true;
        _settings.Spotify.LiveVolumePercent = int.TryParse(ServicesSpotifyLiveVolumeBox.Text, out var liveVolume)
            ? Math.Clamp(liveVolume, 0, 100)
            : 75;
        _settings.Spotify.MuteDuringAlerts = ServicesSpotifyMuteDuringAlertsBox.IsChecked == true;
        _settings.Spotify.AlertDuckingMode = _settings.Spotify.MuteDuringAlerts ? "Reduce" : "None";
        _settings.Spotify.AlertMuteVolumePercent = int.TryParse(ServicesSpotifyAlertVolumeBox.Text, out var alertVolume)
            ? Math.Clamp(alertVolume, 0, 100)
            : 50;
        _settings.Spotify.AlertFadeOutMilliseconds = int.TryParse(ServicesSpotifyAlertFadeOutMsBox.Text, out var fadeOutMs)
            ? Math.Clamp(fadeOutMs, 0, 10000)
            : 500;
        _settings.Spotify.AlertFadeInMilliseconds = int.TryParse(ServicesSpotifyAlertFadeInMsBox.Text, out var fadeInMs)
            ? Math.Clamp(fadeInMs, 0, 10000)
            : 500;
        _settings.Spotify.ShuffleSelectedPlaylist = ServicesSpotifyShufflePlaylistBox.IsChecked == true;
        if (ServicesSpotifyStartPlaylistBox.SelectedItem is SpotifyPlaylist playlist)
        {
            _settings.Spotify.StartPlaylistUri = playlist.Uri;
        }
    }

    private async Task SaveSpotifyAutomationSettingsAsync()
    {
        if (_loadingSettingsIntoUi) return;
        ApplySpotifyAutomationFieldsToSettings();
        await _settingsStore.SaveAsync(_settings);
        ServicesSpotifyAutomationStatusText.Text = "Spotify-Automatik gespeichert.";
    }

    private void ApplyTwitchEndFieldsToSettings()
    {
        _settings.Twitch.RaidOnStreamEnd = ServicesTwitchRaidEnabledBox.IsChecked == true;
        _settings.Twitch.RaidCountdownSeconds = int.TryParse(ServicesTwitchRaidCountdownSecondsBox.Text, out var raidSeconds)
            ? Math.Clamp(raidSeconds, 5, 300)
            : 90;
        _settings.Twitch.StopStreamAfterRaid = ServicesTwitchStopStreamAfterRaidBox.IsChecked != false;
        _settings.Twitch.StopSpotifyAfterRaid = ServicesTwitchStopSpotifyAfterRaidBox.IsChecked != false;
        _settings.Twitch.RaidChannels = ServicesTwitchRaidChannelsBox.Text
            .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ServicesTwitchRaidTargetBox.SelectedItem is string raidTarget)
        {
            _settings.Twitch.SelectedRaidChannel = raidTarget;
        }
        else if (!_settings.Twitch.RaidChannels.Contains(_settings.Twitch.SelectedRaidChannel, StringComparer.OrdinalIgnoreCase))
        {
            _settings.Twitch.SelectedRaidChannel = _settings.Twitch.RaidChannels.FirstOrDefault() ?? "";
        }
        _settings.Workflow.EndSceneSeconds = int.TryParse(ServicesTwitchEndSceneSecondsBox.Text, out var seconds) ? Math.Max(0, seconds) : 60;

        if (double.TryParse(ServicesTwitchEndFollowerGoalTargetBox.Text.Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var followerTarget))
        {
            _settings.Twitch.FollowerGoal.Target = Math.Max(1, followerTarget);
        }

        // Beide Eingabefelder bearbeiten denselben Zielwert.
        FollowerGoalTargetBox.Text = _settings.Twitch.FollowerGoal.Target.ToString("0");
        ServicesTwitchEndFollowerGoalTargetBox.Text = _settings.Twitch.FollowerGoal.Target.ToString("0");
    }

    private async Task RefreshTwitchRewardsAsync()
    {
        try
        {
            var rewards = await _twitchModule.GetCustomRewardsAsync();
            ServicesRewardsList.ItemsSource = rewards.Select(reward => $"{reward.Title} · {reward.Cost:N0} Punkte" ).ToList();
        }
        catch (Exception exception)
        {
            ServicesRewardsList.ItemsSource = new[] { "Fehler: " + exception.Message };
        }
    }

    private async Task CreateTwitchRewardAsync()
    {
        try
        {
            var title = ServicesRewardTitleBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("Bitte einen Titel für die Belohnung eingeben.");
            if (!int.TryParse(ServicesRewardCostBox.Text, out var cost) || cost < 1) throw new InvalidOperationException("Die Punktekosten müssen mindestens 1 betragen.");
            var reward = await _twitchModule.CreateCustomRewardAsync(title, cost, ServicesRewardPromptBox.Text);
            ServicesRewardTitleBox.Clear();
            ServicesRewardPromptBox.Clear();
            await RefreshTwitchRewardsAsync();
            ServicesRewardsList.ToolTip = $"Belohnung '{reward.Title}' wurde erstellt.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Twitch Channel Points", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task CreateTwitchPollAsync()
    {
        try
        {
            var choices = ServicesPollChoicesBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (string.IsNullOrWhiteSpace(ServicesPollTitleBox.Text)) throw new InvalidOperationException("Bitte eine Umfragefrage eingeben.");
            if (choices.Count < 2 || choices.Count > 5) throw new InvalidOperationException("Eine Umfrage benötigt zwei bis fünf Antworten.");
            var duration = int.TryParse(ServicesPollDurationBox.Text, out var parsed) ? Math.Clamp(parsed, 15, 1800) : 60;
            var poll = await _twitchModule.CreatePollAsync(ServicesPollTitleBox.Text, choices, duration);
            _activeTwitchPollId = poll.Id;
            ServicesPollStatusText.Text = $"Aktiv: {poll.Title} · {duration} Sekunden";
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Twitch-Umfrage", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task EndTwitchPollAsync(string status)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_activeTwitchPollId)) throw new InvalidOperationException("Es ist keine in dieser Sitzung gestartete Umfrage vorhanden.");
            var poll = await _twitchModule.EndPollAsync(_activeTwitchPollId, status);
            ServicesPollStatusText.Text = $"{poll.Status}: {poll.Title}";
            if (!status.Equals("TERMINATED", StringComparison.OrdinalIgnoreCase)) _activeTwitchPollId = null;
        }
        catch (Exception ex) { ShowError("Umfrage konnte nicht aktualisiert werden", ex); }
    }

    private async Task EndTwitchPredictionAsync(string status)
    {
        try
        {
            if (_activeTwitchPrediction is null) throw new InvalidOperationException("Es ist keine in dieser Sitzung gestartete Vorhersage vorhanden.");
            _activeTwitchPrediction = await _twitchModule.EndPredictionAsync(_activeTwitchPrediction.Id, status, null);
            ServicesPredictionWinnerBox.ItemsSource = _activeTwitchPrediction.Outcomes;
            ServicesPredictionStatusText.Text = $"{_activeTwitchPrediction.Status}: {_activeTwitchPrediction.Title}";
        }
        catch (Exception ex) { ShowError("Vorhersage konnte nicht aktualisiert werden", ex); }
    }

    private async Task ResolveTwitchPredictionAsync()
    {
        try
        {
            if (_activeTwitchPrediction is null) throw new InvalidOperationException("Es ist keine in dieser Sitzung gestartete Vorhersage vorhanden.");
            if (ServicesPredictionWinnerBox.SelectedItem is not TwitchPredictionOutcome winner) throw new InvalidOperationException("Bitte das Gewinnergebnis auswählen.");
            _activeTwitchPrediction = await _twitchModule.EndPredictionAsync(_activeTwitchPrediction.Id, "RESOLVED", winner.Id);
            ServicesPredictionStatusText.Text = $"Aufgelöst: {winner.Title}";
        }
        catch (Exception ex) { ShowError("Vorhersage konnte nicht aufgelöst werden", ex); }
    }

    private async Task RefreshTwitchRedemptionsAsync()
    {
        try
        {
            if (ServicesRewardsList.SelectedItem is not TwitchChannelPointReward reward) throw new InvalidOperationException("Bitte zuerst eine Channel-Point-Belohnung auswählen.");
            var redemptions = await _twitchModule.GetRewardRedemptionsAsync(reward.Id);
            _twitchRedemptionItems.Clear();
            foreach (var redemption in redemptions) _twitchRedemptionItems.Add(new TwitchRewardRedemptionItem(redemption));
        }
        catch (Exception ex) { ShowError("Einlösungen konnten nicht geladen werden", ex); }
    }

    private async Task UpdateSelectedTwitchRedemptionAsync(string status)
    {
        try
        {
            if (ServicesRedemptionsList.SelectedItem is not TwitchRewardRedemptionItem selected) throw new InvalidOperationException("Bitte eine offene Einlösung auswählen.");
            await _twitchModule.UpdateRewardRedemptionStatusAsync(selected.Redemption.RewardId, selected.Redemption.Id, status);
            _twitchRedemptionItems.Remove(selected);
        }
        catch (Exception ex) { ShowError("Einlösung konnte nicht aktualisiert werden", ex); }
    }

    private async Task CreateTwitchPredictionAsync()
    {
        try
        {
            var outcomes = ServicesPredictionOutcomesBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (string.IsNullOrWhiteSpace(ServicesPredictionTitleBox.Text)) throw new InvalidOperationException("Bitte eine Vorhersagefrage eingeben.");
            if (outcomes.Count < 2 || outcomes.Count > 10) throw new InvalidOperationException("Eine Vorhersage benötigt zwei bis zehn Ergebnisse.");
            var window = int.TryParse(ServicesPredictionWindowBox.Text, out var parsed) ? Math.Clamp(parsed, 30, 1800) : 120;
            var prediction = await _twitchModule.CreatePredictionAsync(ServicesPredictionTitleBox.Text, outcomes, window);
            _activeTwitchPrediction = prediction;
            ServicesPredictionWinnerBox.ItemsSource = prediction.Outcomes;
            ServicesPredictionStatusText.Text = $"Aktiv: {prediction.Title} · {window} Sekunden";
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Twitch-Vorhersage", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task SaveTwitchEndSettingsAsync()
    {
        ApplyTwitchEndFieldsToSettings();
        RefreshRaidChannelSelectors();
        DashboardRaidEnabledBox.IsChecked = _settings.Twitch.RaidOnStreamEnd;

        // Speichert das Follower-Ziel und schreibt es zugleich in die aktive overlay-data.json.
        await SaveTwitchGoalsAsync();
    }

    private async Task RestoreSelectedBackupAsync()
    {
        if (BackupsList.SelectedItem is not UpdateBackup backup)
        {
            return;
        }

        var result = MessageBox.Show(
            "Backup wirklich wiederherstellen?\n\n" +
            "Die aktuellen Einstellungen und Profildaten werden überschrieben.",
            "Backup wiederherstellen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await _updateService.RestoreBackupAsync(backup.Id);
        await LoadSettingsAsync();

        UpdateStatusText.Text =
            "Backup wurde wiederhergestellt.";
        UpdateStatusText.Foreground =
            System.Windows.Media.Brushes.LightGreen;
    }

    private async Task DetectLegacyAsync()
    {
        var candidates =
            await _migrationService.DetectAsync();

        LegacyCandidatesList.ItemsSource = candidates;

        MigrationStatusText.Text = candidates.Count == 0
            ? "Keine alte Suite automatisch gefunden."
            : $"{candidates.Count} möglicher Installationsordner gefunden.";
    }

    private async Task ImportSelectedLegacyAsync()
    {
        if (LegacyCandidatesList.SelectedItem
            is not MigrationCandidate candidate)
        {
            return;
        }

        var result = await _migrationService.ImportAsync(
            candidate.SourcePath);

        await LoadSettingsAsync();

        MigrationStatusText.Text =
            result.Detail +
            "\nImportiert: " +
            string.Join(", ", result.ImportedItems) +
            (result.Warnings.Count > 0
                ? "\nHinweise: " +
                  string.Join(" | ", result.Warnings)
                : "");

        MigrationStatusText.Foreground =
            result.Success
                ? System.Windows.Media.Brushes.LightGreen
                : System.Windows.Media.Brushes.IndianRed;
    }

    private async Task LoadOverlayProjectsAsync()
    {
        var selectedId = (OverlayProjectList.SelectedItem as OverlayProjectDefinition)?.Id;
        _overlayProjects.Clear();
        foreach (var project in await _overlayProjectService.LoadAsync()) _overlayProjects.Add(project);
        OverlayProjectList.SelectedItem = _overlayProjects.FirstOrDefault(x => x.Id == selectedId) ?? _overlayProjects.FirstOrDefault();
        if (_obsClient.IsConnected)
        {
            OverlayProjectObsSceneBox.ItemsSource = (await _obsClient.GetSceneListAsync()).Select(x => x.Name).ToList();
        }
        RefreshSelectedOverlayProject();
        RefreshSpotifyOverlayProjectSelector();
    }

    private void RefreshSelectedOverlayProject()
    {
        _overlayProjectItems.Clear();
        if (OverlayProjectList.SelectedItem is not OverlayProjectDefinition project)
        {
            OverlayProjectTitleText.Text = "Kein Projekt ausgewählt";
            OverlayProjectPathText.Text = "";
            return;
        }
        OverlayProjectTitleText.Text = $"{project.Name} · Version {project.Version}";
        OverlayProjectPathText.Text = string.IsNullOrWhiteSpace(project.RootPath) ? "Quelle: OBS" : project.RootPath;
        foreach (var item in project.Items) _overlayProjectItems.Add(item);
        OverlayProjectStatusText.Text = project.Status;
        OverlayProjectItemsList.SelectedIndex = project.Items.Count > 0 ? 0 : -1;
    }

    private void RefreshSelectedOverlayProjectItem()
    {
        if (OverlayProjectItemsList.SelectedItem is not OverlayProjectItem item) return;
        OverlayProjectObsSceneBox.SelectedItem = item.ObsScene;
        OverlayProjectObsSourceBox.Text = item.ObsSource;
    }

    private void BrowseOverlayManifest()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Eigene overlay.json auswählen",
            Filter = "Overlay-Projektdatei (overlay.json)|overlay.json|JSON-Dateien (*.json)|*.json|Alle Dateien (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        OverlayManifestPathBox.Text = dialog.FileName;
        _settings.General.OverlayManifestPath = dialog.FileName;
        UpdateOverlayManifestStatus();
    }

    private async Task CreateOverlayManifestAsync()
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Neue overlay.json anlegen",
                FileName = "overlay.json",
                DefaultExt = ".json",
                Filter = "Overlay-Projektdatei (overlay.json)|overlay.json|JSON-Dateien (*.json)|*.json"
            };
            if (dialog.ShowDialog(this) != true) return;
            var path = await _overlayProjectService.CreateManifestAsync(dialog.FileName);
            OverlayManifestPathBox.Text = path;
            _settings.General.OverlayManifestPath = path;
            await _settingsStore.SaveAsync(_settings);
            UpdateOverlayManifestStatus("Neue overlay.json wurde angelegt und gespeichert.", Brushes.LightGreen);
        }
        catch (Exception ex)
        {
            UpdateOverlayManifestStatus(ex.Message, Brushes.IndianRed);
        }
    }

    private void OpenOverlayManifestFolder()
    {
        var path = OverlayManifestPathBox.Text.Trim();
        var folder = string.IsNullOrWhiteSpace(path) ? "" : Path.GetDirectoryName(Path.GetFullPath(path));
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            UpdateOverlayManifestStatus("Der Ordner der overlay.json wurde nicht gefunden.", Brushes.IndianRed);
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
    }

    private void UpdateOverlayManifestStatus(string? message = null, Brush? brush = null)
    {
        if (OverlayManifestStatusText is null || OverlayManifestPathBox is null) return;
        var path = OverlayManifestPathBox.Text.Trim();
        OverlayManifestStatusText.Text = message ?? (string.IsNullOrWhiteSpace(path)
            ? "Noch keine overlay.json ausgewählt. Beim nächsten Overlay-Import wird sie automatisch im Projektordner angelegt."
            : File.Exists(path) ? $"Aktive Datei: {path}" : $"Die Datei wird beim Erstellen/Importieren angelegt: {path}");
        OverlayManifestStatusText.Foreground = brush ?? (File.Exists(path) ? Brushes.LightGreen : Brushes.LightGray);
    }

    private async Task ImportManagedOverlayAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Vorhandenen Overlay-Hauptordner auswählen"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var overlayRoot = Path.GetFullPath(dialog.FolderName);
            var manifestPath = Path.Combine(overlayRoot, "overlay.json");
            var rootDataPath = Path.Combine(overlayRoot, "data", "overlay-data.json");
            var nestedDataPath = Path.Combine(overlayRoot, "Overlay", "data", "overlay-data.json");
            // Ältere DenverJohn-Overlays enthalten die tatsächlich von den HTML-Szenen
            // geladene Laufzeitdatei im Unterordner Overlay\data. Diese Datei hat
            // Vorrang vor einer zusätzlich vorhandenen, veralteten Kopie in data.
            var dataPath = File.Exists(nestedDataPath) ? nestedDataPath : rootDataPath;

            if (!File.Exists(manifestPath))
                throw new InvalidOperationException("Im ausgewählten Ordner wurde keine overlay.json gefunden.");

            if (!File.Exists(dataPath))
                throw new InvalidOperationException(@"Im ausgewählten Ordner wurde weder Overlay\data\overlay-data.json noch data\overlay-data.json gefunden.");

            await DisableLegacyOverlayWriterAsync(overlayRoot);

            _settings.Overlay.RootPath = overlayRoot;
            _settings.Overlay.DataFilePath = dataPath;
            _settings.Overlay.DataFileName = "overlay-data.json";
            _settings.General.OverlayManifestPath = manifestPath;
            await _settingsStore.SaveAsync(_settings);

            OverlayRootBox.Text = overlayRoot;
            OverlayManifestPathBox.Text = manifestPath;
            ServicesSpotifyDataJsonPathBox.Text = dataPath;

            await _overlayModule.Service.UpdateAsync(data =>
            {
                data.Spotify.ShowInOverlay = true;
                data.Spotify.ShowTitle = true;
                data.Spotify.ShowArtist = true;
                data.Spotify.ShowAlbumCover = true;
                data.Spotify.ShowProgress = true;
                data.Spotify.HideWhenPaused = false;
                data.Spotify.HideWhenMuted = _settings.Spotify.OverlayHideWhenMuted;
                data.Spotify.Cover = data.Spotify.CoverUrl;
            });

            var project = await _overlayProjectService.ImportFolderAsync(overlayRoot);
            var existing = _overlayProjects.FirstOrDefault(x => string.Equals(x.RootPath, overlayRoot, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) _overlayProjects.Remove(existing);
            if (_overlayProjects.Any(x => string.Equals(x.Id, project.Id, StringComparison.OrdinalIgnoreCase)))
                project.Id = Guid.NewGuid().ToString("N");
            _overlayProjects.Add(project);
            await _overlayProjectService.SaveAsync(_overlayProjects);

            OverlayProjectList.SelectedItem = project;
            UpdateOverlayManifestStatus();
            RefreshSpotifyOverlayProjectSelector();

            OverlayProjectStatusText.Text = $"Vorhandenes Overlay aktiviert: {project.Items.Count} HTML-Dateien. OBS-Pfade bleiben unverändert.";
            OverlayProjectStatusText.Foreground = Brushes.LightGreen;
            ServicesSpotifyOverlayPathText.Text = $"Aktive JSON: {dataPath}";
            ServicesSpotifyOverlayStatusText.Text = @"Die Suite schreibt direkt in die vorhandene data\overlay-data.json. Es wird keine zweite Overlay-Kopie angelegt.";
            ServicesSpotifyOverlayStatusText.Foreground = Brushes.LightGreen;

            await SynchronizeSpotifyOverlayVisibilityAsync(_spotifyModule.GetSnapshot().Playback);
        }
        catch (Exception exception)
        {
            OverlayProjectStatusText.Text = "Overlay-Verzeichnis konnte nicht aktiviert werden: " + exception.Message;
            OverlayProjectStatusText.Foreground = Brushes.IndianRed;
        }
    }

    private static void CopyOverlayDirectory(string sourceRoot, string targetRoot)
    {
        Directory.CreateDirectory(targetRoot);
        foreach (var directory in Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(targetRoot, relative));
        }
        foreach (var file in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            var target = Path.Combine(targetRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private async Task ImportOverlayProjectAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Ordner des HTML-Overlay-Projekts auswählen" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var project = await _overlayProjectService.ImportFolderAsync(dialog.FolderName);
            var existing = _overlayProjects.FirstOrDefault(x => string.Equals(x.RootPath, project.RootPath, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) _overlayProjects.Remove(existing);

            // Ein aus einem anderen Ordner kopiertes overlay.json darf nicht dazu führen,
            // dass zwei unterschiedliche Overlay-Projekte dieselbe interne ID besitzen.
            if (_overlayProjects.Any(x => string.Equals(x.Id, project.Id, StringComparison.OrdinalIgnoreCase)))
            {
                project.Id = Guid.NewGuid().ToString("N");
                project.Name = new DirectoryInfo(project.RootPath).Name;
                await _overlayProjectService.WriteManifestAsync(project, project.ManifestPath);
            }

            _overlayProjects.Add(project);
            await _overlayProjectService.SaveAsync(_overlayProjects);
            OverlayProjectList.SelectedItem = project;
            OverlayManifestPathBox.Text = project.ManifestPath;
            _settings.General.OverlayManifestPath = project.ManifestPath;
            await _settingsStore.SaveAsync(_settings);
            UpdateOverlayManifestStatus();
            OverlayProjectStatusText.Text = $"Projekt importiert: {project.Items.Count} HTML-Dateien erkannt. overlay.json wurde angelegt/aktualisiert.";
            OverlayProjectStatusText.Foreground = Brushes.LightGreen;
        }
        catch (Exception ex)
        {
            OverlayProjectStatusText.Text = ex.Message;
            OverlayProjectStatusText.Foreground = Brushes.IndianRed;
        }
    }

    private async Task ImportOverlayFromObsAsync()
    {
        try
        {
            var project = await _overlayProjectService.ImportFromObsAsync("OBS Szenensammlung " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            _overlayProjects.Add(project);
            await _overlayProjectService.SaveAsync(_overlayProjects);
            OverlayProjectList.SelectedItem = project;
            OverlayManifestPathBox.Text = project.ManifestPath;
            _settings.General.OverlayManifestPath = project.ManifestPath;
            await _settingsStore.SaveAsync(_settings);
            UpdateOverlayManifestStatus();
            OverlayProjectStatusText.Text = project.Status + " · overlay.json wurde angelegt.";
            OverlayProjectStatusText.Foreground = Brushes.LightGreen;
        }
        catch (Exception ex)
        {
            OverlayProjectStatusText.Text = ex.Message;
            OverlayProjectStatusText.Foreground = Brushes.IndianRed;
        }
    }


    private async Task AddOverlaySceneAsync()
    {
        if (OverlayProjectList.SelectedItem is not OverlayProjectDefinition project)
        {
            MessageBox.Show("Bitte wähle zuerst ein Overlay-Projekt aus.", "Overlay-Szene", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(project.RootPath) || !Directory.Exists(project.RootPath))
        {
            MessageBox.Show("Das ausgewählte Projekt besitzt keinen gültigen lokalen Projektordner.", "Overlay-Szene", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var sceneNameBox = new TextBox { Margin = new Thickness(0, 8, 0, 12), MinWidth = 320 };
        var createButton = new Button { Content = "DATEIEN AUSWÄHLEN UND SZENE ANLEGEN", IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancelButton = new Button { Content = "ABBRECHEN", IsCancel = true };
        var dialog = new Window
        {
            Title = "Neue Overlay-Szene",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(17, 24, 29)),
            Foreground = Brushes.White,
            Content = new StackPanel
            {
                Margin = new Thickness(18),
                Children =
                {
                    new TextBlock { Text = "Name der neuen Szene", FontWeight = FontWeights.Bold },
                    sceneNameBox,
                    new TextBlock { Text = "Danach kannst du HTML-, Bild-, Video-, Audio- und weitere Web-Assets auswählen. Die Dateien werden in das Overlay-Projekt kopiert und geeignete Quellen direkt in OBS angelegt.", Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, MaxWidth = 440, Margin = new Thickness(0,0,0,12) },
                    new WrapPanel { Children = { createButton, cancelButton } }
                }
            }
        };
        createButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(sceneNameBox.Text))
            {
                MessageBox.Show(dialog, "Bitte gib einen Namen für die Szene ein.", "Overlay-Szene", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            dialog.DialogResult = true;
        };
        if (dialog.ShowDialog() != true) return;

        var filesDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Dateien für die neue Overlay-Szene auswählen",
            Multiselect = true,
            CheckFileExists = true,
            Filter = "Geeignete Overlay-Dateien|*.html;*.htm;*.css;*.js;*.json;*.png;*.jpg;*.jpeg;*.gif;*.webp;*.svg;*.bmp;*.mp4;*.webm;*.mov;*.mkv;*.mp3;*.wav;*.ogg;*.m4a;*.woff;*.woff2;*.ttf;*.otf|HTML-Dateien|*.html;*.htm|Bilder|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.svg;*.bmp|Video und Audio|*.mp4;*.webm;*.mov;*.mkv;*.mp3;*.wav;*.ogg;*.m4a|Web-Assets|*.css;*.js;*.json;*.woff;*.woff2;*.ttf;*.otf|Alle Dateien|*.*"
        };
        if (filesDialog.ShowDialog(this) != true || filesDialog.FileNames.Length == 0) return;

        try
        {
            var added = await _overlayProjectService.AddSceneAsync(project, sceneNameBox.Text.Trim(), filesDialog.FileNames);
            await _overlayProjectService.SaveAsync(_overlayProjects);
            _overlayProjectItems.Clear();
            foreach (var item in project.Items) _overlayProjectItems.Add(item);
            OverlayProjectItemsList.Items.Refresh();
            OverlayProjectList.Items.Refresh();
            OverlayProjectItemsList.SelectedItem = added.FirstOrDefault();
            OverlayManifestPathBox.Text = project.ManifestPath;
            _settings.General.OverlayManifestPath = project.ManifestPath;
            await _settingsStore.SaveAsync(_settings);
            OverlayProjectStatusText.Text = $"Szene '{sceneNameBox.Text.Trim()}' wurde mit {added.Count} geeigneten Quellen gespeichert und in OBS übernommen.";
            OverlayProjectStatusText.Foreground = Brushes.LightGreen;
        }
        catch (Exception ex)
        {
            OverlayProjectStatusText.Text = ex.Message;
            OverlayProjectStatusText.Foreground = Brushes.IndianRed;
        }
    }

    private async Task DeleteOverlayProjectAsync()
    {
        if (OverlayProjectList.SelectedItem is not OverlayProjectDefinition project) return;
        if (MessageBox.Show($"Overlay-Projekt '{project.Name}' aus der Suite entfernen? Die Originaldateien werden nicht gelöscht.", "Overlay-Projekt", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _overlayProjects.Remove(project);
        await _overlayProjectService.SaveAsync(_overlayProjects);
        RefreshSelectedOverlayProject();
    }

    private async Task SaveOverlayProjectMappingAsync()
    {
        if (OverlayProjectItemsList.SelectedItem is not OverlayProjectItem item) return;
        item.ObsScene = OverlayProjectObsSceneBox.SelectedItem?.ToString() ?? "";
        item.ObsSource = OverlayProjectObsSourceBox.Text.Trim();
        await _overlayProjectService.SaveAsync(_overlayProjects);
        if (OverlayProjectList.SelectedItem is OverlayProjectDefinition project)
            await _overlayProjectService.WriteManifestAsync(project);
        OverlayProjectItemsList.Items.Refresh();
        OverlayProjectStatusText.Text = "OBS-Zuordnung und overlay.json gespeichert.";
        OverlayProjectStatusText.Foreground = Brushes.LightGreen;
    }

    private async Task SynchronizeOverlayProjectAsync()
    {
        if (OverlayProjectList.SelectedItem is not OverlayProjectDefinition project) return;
        try
        {
            await SaveOverlayProjectMappingAsync();
            await _overlayProjectService.SynchronizeWithObsAsync(project);
            await _overlayProjectService.SaveAsync(_overlayProjects);
            OverlayProjectItemsList.Items.Refresh();
            OverlayProjectList.Items.Refresh();
            OverlayProjectStatusText.Text = project.Status;
            OverlayProjectStatusText.Foreground = Brushes.LightGreen;
        }
        catch (Exception ex)
        {
            OverlayProjectStatusText.Text = ex.Message;
            OverlayProjectStatusText.Foreground = Brushes.IndianRed;
        }
    }

    private void OpenSelectedOverlayProjectFolder()
    {
        if (OverlayProjectList.SelectedItem is not OverlayProjectDefinition project || string.IsNullOrWhiteSpace(project.RootPath) || !Directory.Exists(project.RootPath))
        {
            OverlayProjectStatusText.Text = "Dieses Projekt besitzt keinen lokalen Projektordner.";
            return;
        }
        Process.Start(new ProcessStartInfo { FileName = project.RootPath, UseShellExecute = true });
    }

    private async Task InstallOverlayAsync()
    {
        try
        {
            await SaveSettingsAsync();
            await _overlayModule.Service.InstallBundledOverlayAsync();
            await WriteOverlayConfigurationAsync();
            await _overlayModule.Service.InitializeAsync();

            OverlayStatusText.Text = "Standard-Overlay wurde installiert.";
            OverlayStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            OverlayStatusText.Text = exception.Message;
            OverlayStatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
        }
    }

    private async Task OpenOverlayFolderAsync()
    {
        var root = await _overlayModule.Service.GetOverlayRootAsync();
        Directory.CreateDirectory(root);

        Process.Start(
            new ProcessStartInfo
            {
                FileName = root,
                UseShellExecute = true
            });
    }

    private async Task ValidateOverlayAsync()
    {
        try
        {
            var root = await _overlayModule.Service.GetOverlayRootAsync();
            var data = await _overlayModule.Service.GetDataFilePathAsync();

            var required = new[]
            {
                Path.Combine(root, "assets", "base.css"),
                Path.Combine(root, "assets", "data-client.js"),
                Path.Combine(root, "modules", "content-name.html"),
                Path.Combine(root, "modules", "scene-text.html"),
                Path.Combine(root, "modules", "start-timer.html"),
                Path.Combine(root, "modules", "spotify-info.html"),
                Path.Combine(root, "modules", "live-info.html"),
                Path.Combine(root, "modules", "meta-status.html"),
                Path.Combine(root, "modules", "pause-text.html"),
                Path.Combine(root, "modules", "stream-stats.html"),
                Path.Combine(root, "modules", "reaction-title.html"),
                Path.Combine(root, "modules", "reaction-frame.html"),
                Path.Combine(root, "modules", "reaction-text.html"),
                Path.Combine(root, "modules", "frame.html"),
                Path.Combine(root, "scenes", "start.html"),
                Path.Combine(root, "scenes", "game.html"),
                Path.Combine(root, "scenes", "pause.html"),
                Path.Combine(root, "scenes", "metaschutz.html"),
                Path.Combine(root, "scenes", "reactions.html"),
                Path.Combine(root, "scenes", "ende.html"),
                data
            };

            var missing = required.Where(path => !File.Exists(path)).ToList();

            OverlayStatusText.Text = missing.Count == 0
                ? "Overlay vollständig. Daten: " + data
                : "Fehlende Dateien: " +
                  string.Join(", ", missing.Select(Path.GetFileName));

            OverlayStatusText.Foreground = missing.Count == 0
                ? System.Windows.Media.Brushes.LightGreen
                : System.Windows.Media.Brushes.IndianRed;
        }
        catch (Exception exception)
        {
            OverlayStatusText.Text = exception.Message;
            OverlayStatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
        }
    }

    private async Task InstallObsBrowserSourcesAsync()
    {
        try
        {
            var installed = await _obsBrowserSourceInstaller.InstallAsync();

            OverlayStatusText.Text =
                "Eigene Overlay-Szenen aus dem ausgewählten Pfad wurden in OBS eingerichtet:\n" +
                string.Join("\n", installed.Select(item => "• " + item));

            OverlayStatusText.Foreground =
                System.Windows.Media.Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            _appLogger.Write(
                AppLogLevel.Error,
                "OBS",
                "Browserquellen konnten nicht eingerichtet werden.",
                exception);

            OverlayStatusText.Text = exception.Message;
            OverlayStatusText.Foreground =
                System.Windows.Media.Brushes.IndianRed;
        }
    }

    private async Task ExecuteWorkflowAsync(Func<Task> action)
    {
        try
        {
            await action();
            RefreshWorkflowUi(_workflowModule.Service.State);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Stream-Workflow",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task AddViewerSampleAsync()
    {
        if (!int.TryParse(WorkflowViewerSampleBox.Text.Trim(), out var viewers))
        {
            return;
        }

        await _workflowModule.Service.AddViewerSampleAsync(viewers);
        RefreshWorkflowUi(_workflowModule.Service.State);
    }

    private void RefreshWorkflowUi(WorkflowState state)
    {
        // Workflow- und Twitch-Ereignisse können aus Hintergrundthreads kommen.
        // WPF-Steuerelemente dürfen ausschließlich vom UI-Thread geändert werden.
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => RefreshWorkflowUi(state));
            return;
        }

        WorkflowStatusText.Text = state.Phase + " · " + state.Detail;
        WorkflowPhaseText.Text = state.Phase.ToString();
        WorkflowSceneText.Text = string.IsNullOrWhiteSpace(state.CurrentScene)
            ? "-"
            : state.CurrentScene;

        WorkflowCountdownText.Text =
            TimeSpan.FromSeconds(Math.Max(0, state.CountdownRemainingSeconds))
                .ToString(@"mm\:ss");

        var stats = _workflowModule.Service.SessionStats;
        WorkflowPeakViewersText.Text = stats.PeakViewers.ToString();
        WorkflowAverageViewersText.Text = stats.AverageViewers.ToString("0.0");
        WorkflowFollowersText.Text = stats.FollowersGained.ToString();
        WorkflowChatAlertsText.Text =
            stats.ChatMessages + " / " + stats.AlertsPlayed;

        // The dashboard must reflect the actual OBS output as well as streams
        // started through the suite workflow. Otherwise a stream started
        // directly in OBS (or through another controller) remains "OFFLINE".
        var isLive = state.Phase == StreamPhase.Live || _lastObsStreamActive;
        var liveDetail = _lastObsStreamActive && _streamSessionStartedAt.HasValue
            ? (DateTimeOffset.Now - _streamSessionStartedAt.Value).ToString(@"hh\:mm\:ss")
            : state.Detail;

        StreamDashboardStatus.Text = isLive ? "LIVE" : "OFFLINE";
        DashboardHeroStreamStatusText.Text = isLive ? "LIVE" : "OFFLINE";
        DashboardHeroStreamStatusBadge.Background = isLive
            ? System.Windows.Media.Brushes.Firebrick
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(75, 83, 89));
        UpdateStreamLivePulse(isLive);

        StreamDashboardLamp.Fill =
            isLive
                ? System.Windows.Media.Brushes.LimeGreen
                : System.Windows.Media.Brushes.IndianRed;

        DashboardStreamDetailText.Text = isLive ? liveDetail : "00:00:00";
        DashboardHeroViewerText.Text = _currentLiveViewerCount.ToString() + " Zuschauer";
        DashboardHeroLiveTimeText.Text = isLive ? liveDetail : "00:00:00";
        DashboardHeroViewerText.Text = _currentLiveViewerCount.ToString();
        DashboardHeroFollowerText.Text = _currentFollowerCount.ToString();
        DashboardPeakViewersText.Text = stats.PeakViewers.ToString();
        DashboardAverageViewersText.Text = stats.AverageViewers.ToString("0.0");
        DashboardFollowersGainedText.Text = stats.FollowersGained.ToString();
        DashboardChatAlertsText.Text = _currentActiveSubscriptionCount.ToString();
        DashboardTwitchSessionMetricsText.Text =
            $"Subs {stats.NewSubscriptions} · Gift-Subs {stats.GiftSubscriptions} · Bits {stats.BitsCheered} · Raids {stats.IncomingRaids}";
        RefreshTwitchProfessionalUi();
        UpdateDashboardSelectedStatistic();
    }

    private async Task RefreshObsPreviewTickAsync()
    {
        if (_obsPreviewRefreshRunning || !_obsClient.IsConnected || DashboardPage.Visibility != Visibility.Visible)
        {
            return;
        }

        _obsPreviewRefreshRunning = true;
        try
        {
            await RefreshDashboardObsScenePreviewAsync();
        }
        catch
        {
            // Die nächste Aktualisierung versucht es erneut.
        }
        finally
        {
            _obsPreviewRefreshRunning = false;
        }
    }

    private async Task RefreshDashboardLiveDataAsync()
    {
        if (_dashboardLiveRefreshRunning)
        {
            return;
        }

        _dashboardLiveRefreshRunning = true;

        try
        {
            // OBS + stream state
            if (_obsClient.IsConnected)
            {
                try
                {
                    await RefreshObsAsync();
                    await RefreshDashboardStreamQualityAsync();
                }
                catch
                {
                    // Watchdog handles reconnects; dashboard refresh must stay resilient.
                }
            }

            else
            {
                ResetDashboardStreamQuality("OBS nicht verbunden");
            }

            // Twitch live data
            if (_twitchModule.GetSnapshot().Authenticated)
            {
                try
                {
                    await RefreshLiveViewerSampleAsync();
                    await RefreshTwitchFollowerCountAsync();
                    await RefreshTwitchGoalsAsync();
                    await RefreshTwitchUsersAsync();
                    RefreshTwitchUi();
                }
                catch
                {
                    // Keep the refresh loop alive even if Twitch rate limits temporarily.
                }
            }

            // Spotify playback state
            if (_spotifyModule.GetSnapshot().Authenticated)
            {
                try
                {
                    await _spotifyModule.RefreshPlaybackAsync();
                    RefreshSpotifyUi();
                }
                catch
                {
                    // Spotify can temporarily rate-limit; the next cycle retries.
                }
            }
            else
            {
                RefreshSpotifyUi();
            }

            // Streamer.bot top status
            var streamerBotConnected =
                _streamerBotSocket?.State ==
                System.Net.WebSockets.WebSocketState.Open;

            StreamerBotDashboardStatus.Text =
                streamerBotConnected ? "VERBUNDEN" : "NICHT VERBUNDEN";
            StreamerBotDashboardLamp.Fill =
                streamerBotConnected
                    ? System.Windows.Media.Brushes.LimeGreen
                    : System.Windows.Media.Brushes.IndianRed;

            // Alerts status is driven by the alerts module state callback.
            RefreshDashboardServiceActionButtons();
            RefreshDashboardAutomationSummary();
            RefreshDashboardResourceUsage();
        }
        finally
        {
            _dashboardLiveRefreshRunning = false;
        }
    }

    private async Task RefreshDashboardStreamQualityAsync()
    {
        try
        {
            var stream = await _obsClient.GetStreamStatusAsync();
            var stats = await _obsClient.GetStatsAsync();
            var now = DateTimeOffset.Now;

            if (_lastObsBitrateSampleAt.HasValue && stream.OutputBytes >= _lastObsOutputBytes)
            {
                var seconds = Math.Max(0.25, (now - _lastObsBitrateSampleAt.Value).TotalSeconds);
                _currentObsBitrateKbps = (stream.OutputBytes - _lastObsOutputBytes) * 8d / seconds / 1000d;
            }
            else if (!stream.OutputActive)
            {
                _currentObsBitrateKbps = 0;
            }

            _lastObsOutputBytes = stream.OutputBytes;
            _lastObsBitrateSampleAt = now;

            var outputDropped = Math.Max(stream.OutputSkippedFrames, stats.OutputSkippedFrames);
            var outputTotal = Math.Max(stream.OutputTotalFrames, stats.OutputTotalFrames);
            var droppedPercent = outputTotal > 0 ? outputDropped * 100d / outputTotal : 0d;
            var renderPercent = stats.RenderTotalFrames > 0 ? stats.RenderSkippedFrames * 100d / stats.RenderTotalFrames : 0d;

            DashboardStreamBitrateText.Text = $"{_currentObsBitrateKbps:0} kbps";
            DashboardStreamFpsText.Text = $"{stats.ActiveFps:0.0} / 60";
            DashboardDroppedFramesText.Text = $"{outputDropped:N0} ({droppedPercent:0.00} %)";
            DashboardRenderLagText.Text = $"{stats.RenderSkippedFrames:N0} ({renderPercent:0.00} %)";

            if (!stream.OutputActive)
            {
                DashboardStreamQualityStatusText.Text = "OFFLINE";
                DashboardStreamQualityLamp.Fill = Brushes.Gray;
                DashboardStreamQualityDetailText.Text = "OBS ist verbunden, der Stream läuft derzeit nicht.";
                return;
            }

            if (stream.OutputReconnecting || droppedPercent >= 2 || renderPercent >= 2 || stats.ActiveFps < 50)
            {
                DashboardStreamQualityStatusText.Text = "INSTABIL";
                DashboardStreamQualityLamp.Fill = Brushes.IndianRed;
                DashboardStreamQualityDetailText.Text = stream.OutputReconnecting
                    ? "OBS versucht, die Streaming-Verbindung wiederherzustellen."
                    : "Hohe Frameverluste oder eine zu niedrige Bildrate wurden erkannt.";
            }
            else if (droppedPercent >= 0.25 || renderPercent >= 0.25 || stats.ActiveFps < 57 || _currentObsBitrateKbps < 1000)
            {
                DashboardStreamQualityStatusText.Text = "BEOBACHTEN";
                DashboardStreamQualityLamp.Fill = Brushes.Goldenrod;
                DashboardStreamQualityDetailText.Text = "Der Stream läuft, zeigt aber leichte Schwankungen.";
            }
            else
            {
                DashboardStreamQualityStatusText.Text = "STABIL";
                DashboardStreamQualityLamp.Fill = Brushes.LimeGreen;
                DashboardStreamQualityDetailText.Text = "Bitrate, FPS und Frameausgabe sind unauffällig.";
            }
        }
        catch
        {
            ResetDashboardStreamQuality("Messung nicht verfügbar");
        }
    }

    private void ResetDashboardStreamQuality(string status)
    {
        _lastObsOutputBytes = 0;
        _lastObsBitrateSampleAt = null;
        _currentObsBitrateKbps = 0;
        DashboardStreamQualityStatusText.Text = status;
        DashboardStreamQualityLamp.Fill = Brushes.Gray;
        DashboardStreamBitrateText.Text = "0 kbps";
        DashboardStreamFpsText.Text = "0 / 60";
        DashboardDroppedFramesText.Text = "0 (0,00 %)";
        DashboardRenderLagText.Text = "0 (0,00 %)";
        DashboardStreamQualityDetailText.Text = "Keine aktuellen OBS-Streamingdaten.";
    }

    private void RefreshDashboardAutomationSummary()
    {
        var items = new List<string>();
        var state = _workflowModule.Service.State;

        items.Add(
            $"Workflow · {state.Phase} · {state.Detail}");

        if (_settings.Workflow.AutoSwitchScenes)
        {
            items.Add("Automatik · Szenenwechsel aktiv");
        }

        if (_settings.Twitch.RaidOnStreamEnd)
        {
            var raidTarget = string.IsNullOrWhiteSpace(
                    _settings.Twitch.SelectedRaidChannel)
                ? "kein Ziel"
                : _settings.Twitch.SelectedRaidChannel;

            items.Add(
                $"Streamende · Raid geplant · {raidTarget}");
        }

        if (_settings.Dashboard.AutoFocusModeOnStreamStart)
        {
            items.Add("Dashboard · Fokusmodus beim Streamstart");
        }

        if (items.Count == 1)
        {
            items.Add("Keine weiteren Automatisierungen aktiv");
        }

        DashboardAutomationList.ItemsSource = items;
    }

    private void RefreshDashboardResourceUsage()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var now = DateTimeOffset.Now;
            var cpuNow = process.TotalProcessorTime;
            var elapsedMs = Math.Max(1, (now - _lastDashboardResourceSample).TotalMilliseconds);
            var cpuMs = Math.Max(0, (cpuNow - _lastDashboardCpuTime).TotalMilliseconds);
            var cpu = Math.Clamp(cpuMs / elapsedMs / Math.Max(1, Environment.ProcessorCount) * 100.0, 0, 100);
            _lastDashboardCpuTime = cpuNow;
            _lastDashboardResourceSample = now;

            var ramMb = process.WorkingSet64 / 1024d / 1024d;
            var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            var ramPercent = available > 0 ? Math.Clamp(process.WorkingSet64 / (double)available * 100.0, 0, 100) : 0;

            DashboardCpuText.Text = $"CPU: {cpu:0}%";
            DashboardCpuBar.Value = cpu;
            DashboardRamText.Text = $"RAM: {ramMb:0} MB";
            DashboardRamBar.Value = ramPercent;
        }
        catch
        {
            DashboardCpuText.Text = "CPU: -";
            DashboardRamText.Text = "RAM: -";
        }
    }

    private sealed record AlertAudioOutputDevice(string ID, string FriendlyName);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct WaveOutCaps
    {
        public ushort ManufacturerId;
        public ushort ProductId;
        public uint DriverVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ProductName;

        public uint Formats;
        public ushort Channels;
        public ushort Reserved;
        public uint Support;
    }

    [DllImport("winmm.dll")]
    private static extern uint waveOutGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern uint waveOutGetDevCaps(
        UIntPtr deviceId,
        out WaveOutCaps capabilities,
        uint capabilitiesSize);

    private void LoadAlertAudioOutputDevices()
    {
        try
        {
            var selected = AlertAudioOutputDeviceBox.SelectedValue?.ToString();
            var devices = new List<AlertAudioOutputDevice>
            {
                new("default", "Windows-Standardausgabe")
            };

            var deviceCount = waveOutGetNumDevs();
            var capsSize = (uint)Marshal.SizeOf<WaveOutCaps>();
            for (uint index = 0; index < deviceCount; index++)
            {
                if (waveOutGetDevCaps((UIntPtr)index, out var capabilities, capsSize) == 0)
                {
                    var name = string.IsNullOrWhiteSpace(capabilities.ProductName)
                        ? $"Audioausgabe {index + 1}"
                        : capabilities.ProductName.Trim();
                    devices.Add(new AlertAudioOutputDevice($"waveout:{index}", name));
                }
            }

            AlertAudioOutputDeviceBox.ItemsSource = devices;
            if (!string.IsNullOrWhiteSpace(selected))
                AlertAudioOutputDeviceBox.SelectedValue = selected;
            if (AlertAudioOutputDeviceBox.SelectedIndex < 0)
                AlertAudioOutputDeviceBox.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            AlertAudioOutputDeviceBox.ItemsSource = new[]
            {
                new AlertAudioOutputDevice("default", "Windows-Standardausgabe")
            };
            AlertAudioOutputDeviceBox.SelectedIndex = 0;
            AlertPreviewStatusText.Text = "Audioausgänge konnten nicht vollständig eingelesen werden: " + ex.Message;
        }
    }

    private void LoadAlertAudioPreviewSource()
    {
        StopAlertAudioPreview();
        var path = AlertSoundPathBox.Text.Trim();
        if (!File.Exists(path)) return;
        AlertAudioPreviewMedia.Source = new Uri(path, UriKind.Absolute);
    }

    private void PlaySelectedAlertAudioRange()
    {
        var path = AlertSoundPathBox.Text.Trim();
        if (!File.Exists(path))
        {
            AlertPreviewStatusText.Text = "Bitte zuerst eine vorhandene Audiodatei auswählen.";
            return;
        }
        if (AlertAudioPreviewMedia.Source is null) LoadAlertAudioPreviewSource();
        AlertAudioPreviewMedia.Position = TimeSpan.FromSeconds(AlertAudioStartSlider.Value);
        AlertAudioPreviewMedia.Volume = 1.0;
        AlertAudioPreviewMedia.Play();
        _alertAudioPreviewTimer.Start();
    }

    private void StopAlertAudioPreview()
    {
        _alertAudioPreviewTimer.Stop();
        AlertAudioPreviewMedia.Stop();
        AlertAudioPreviewMedia.Position = TimeSpan.FromSeconds(AlertAudioStartSlider.Value);
    }

    private static string FormatAlertAudioTime(double seconds)
        => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"mm\:ss\.fff");

    private void UpdateAlertAudioTrimLabels()
    {
        // ValueChanged can fire while InitializeComponent is still creating the XAML controls.
        // At that point one or more of these fields can legitimately still be null.
        if (AlertAudioStartText is null ||
            AlertAudioEndText is null ||
            AlertAudioStartSlider is null ||
            AlertAudioEndSlider is null)
        {
            return;
        }

        AlertAudioStartText.Text = "Start: " + FormatAlertAudioTime(AlertAudioStartSlider.Value);
        AlertAudioEndText.Text = "Ende: " + FormatAlertAudioTime(AlertAudioEndSlider.Value);
    }

    private void AlertAudioPreviewMedia_OnMediaOpened(object sender, RoutedEventArgs e)
    {
        if (!AlertAudioPreviewMedia.NaturalDuration.HasTimeSpan) return;
        var duration = Math.Max(0.1, AlertAudioPreviewMedia.NaturalDuration.TimeSpan.TotalSeconds);
        _updatingAlertAudioTrimUi = true;
        AlertAudioStartSlider.Maximum = duration;
        AlertAudioEndSlider.Maximum = duration;
        if (AlertAudioEndSlider.Value <= AlertAudioStartSlider.Value || AlertAudioEndSlider.Value <= 1)
            AlertAudioEndSlider.Value = duration;
        _updatingAlertAudioTrimUi = false;
        UpdateAlertAudioTrimLabels();
    }

    private void AlertAudioTrimSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingAlertAudioTrimUi || AlertAudioStartSlider is null || AlertAudioEndSlider is null) return;
        _updatingAlertAudioTrimUi = true;
        if (AlertAudioStartSlider.Value > AlertAudioEndSlider.Value)
        {
            if (ReferenceEquals(sender, AlertAudioStartSlider)) AlertAudioEndSlider.Value = AlertAudioStartSlider.Value;
            else AlertAudioStartSlider.Value = AlertAudioEndSlider.Value;
        }
        _updatingAlertAudioTrimUi = false;
        UpdateAlertAudioTrimLabels();
    }

    private sealed record AlertLibraryItem(string Type, bool Enabled)
    {
        public string DisplayName => $"{(Enabled ? "●" : "○")} {Type}";
    }

    private void RefreshAlertLibrary(string? selectType = null)
    {
        // During InitializeComponent the alert controls may not have been created yet.
        // ValueChanged/SelectionChanged handlers can call this method while XAML is still loading.
        if (AlertTypeBox is null || AlertLibraryList is null)
            return;

        selectType ??= AlertTypeBox.SelectedItem as string;
        var keys = _settings.Alerts.Definitions.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        AlertTypeBox.ItemsSource = keys;
        AlertLibraryList.ItemsSource = keys.Select(key => new AlertLibraryItem(key, _settings.Alerts.Definitions[key].Enabled)).ToList();
        if (!string.IsNullOrWhiteSpace(selectType) && _settings.Alerts.Definitions.ContainsKey(selectType))
            AlertTypeBox.SelectedItem = selectType;
        else if (keys.Count > 0)
            AlertTypeBox.SelectedIndex = 0;
        SyncAlertLibrarySelection();
    }

    private void SyncAlertLibrarySelection()
    {
        if (AlertLibraryList is null || AlertTypeBox?.SelectedItem is not string type) return;
        AlertLibraryList.SelectedItem = AlertLibraryList.Items.Cast<AlertLibraryItem>()
            .FirstOrDefault(item => string.Equals(item.Type, type, StringComparison.OrdinalIgnoreCase));
    }

    private string CreateUniqueAlertType(string baseName)
    {
        var cleaned = string.IsNullOrWhiteSpace(baseName) ? "Eigener Alert" : baseName.Trim();
        if (!_settings.Alerts.Definitions.ContainsKey(cleaned)) return cleaned;
        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{cleaned} {i}";
            if (!_settings.Alerts.Definitions.ContainsKey(candidate)) return candidate;
        }
        return cleaned + " " + Guid.NewGuid().ToString("N")[..6];
    }

    private async Task CreateAlertDefinitionAsync()
    {
        var type = CreateUniqueAlertType("Eigener Alert");
        _settings.Alerts.Definitions[type] = new AlertDefinitionSettings
        {
            Type = type, Enabled = true, TextTemplate = "{user} hat einen Alert ausgelöst!"
        };
        await _settingsStore.SaveAsync(_settings);
        RefreshAlertLibrary(type);
        AlertLibraryStatusText.Text = $"{type} wurde angelegt.";
    }

    private async Task DuplicateAlertDefinitionAsync()
    {
        if (AlertTypeBox.SelectedItem is not string sourceType || !_settings.Alerts.Definitions.TryGetValue(sourceType, out var source)) return;
        SaveAlertDefinitionToSettings();
        var type = CreateUniqueAlertType(sourceType + " Kopie");
        _settings.Alerts.Definitions[type] = new AlertDefinitionSettings
        {
            Type = type, Enabled = source.Enabled, TextTemplate = source.TextTemplate, MediaPath = source.MediaPath,
            SoundPath = source.SoundPath, DurationSeconds = source.DurationSeconds, Priority = source.Priority,
            FontFace = source.FontFace, FontSize = source.FontSize, FontColor = source.FontColor, Animation = source.Animation,
            X = source.X, Y = source.Y, Width = source.Width, Height = source.Height, VolumePercent = source.VolumePercent,
            SoundStartSeconds = source.SoundStartSeconds, SoundEndSeconds = source.SoundEndSeconds,
            AudioOutputDeviceId = source.AudioOutputDeviceId
        };
        await _settingsStore.SaveAsync(_settings);
        RefreshAlertLibrary(type);
        AlertLibraryStatusText.Text = $"{type} wurde erstellt.";
    }

    private async Task ToggleAlertDefinitionAsync()
    {
        if (AlertTypeBox.SelectedItem is not string type || !_settings.Alerts.Definitions.TryGetValue(type, out var definition)) return;
        definition.Enabled = !definition.Enabled;
        await _settingsStore.SaveAsync(_settings);
        RefreshAlertLibrary(type);
        AlertLibraryStatusText.Text = definition.Enabled ? "Alert ist aktiv." : "Alert ist deaktiviert.";
    }

    private async Task DeleteAlertDefinitionAsync()
    {
        if (AlertTypeBox.SelectedItem is not string type) return;
        if (_settings.Alerts.Definitions.Count <= 1)
        {
            AlertLibraryStatusText.Text = "Mindestens ein Alert muss erhalten bleiben.";
            return;
        }
        var answer = MessageBox.Show($"Alert '{type}' wirklich löschen?", "Alert löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        _settings.Alerts.Definitions.Remove(type);
        await _settingsStore.SaveAsync(_settings);
        RefreshAlertLibrary();
        AlertLibraryStatusText.Text = "Alert wurde gelöscht.";
    }

    private async Task LoadSelectedAlertDefinitionAsync()
    {
        if (AlertTypeBox.SelectedItem is not string type ||
            !_settings.Alerts.Definitions.TryGetValue(
                type,
                out var definition))
        {
            return;
        }

        AlertTextTemplateBox.Text =
            definition.TextTemplate;

        AlertMediaPathBox.Text =
            definition.MediaPath;

        AlertSoundPathBox.Text =
            definition.SoundPath;

        LoadAlertAudioOutputDevices();
        AlertAudioOutputDeviceBox.SelectedValue = definition.AudioOutputDeviceId;
        LoadAlertAudioPreviewSource();
        AlertAudioStartSlider.Value = Math.Max(0, definition.SoundStartSeconds);
        if (definition.SoundEndSeconds > 0) AlertAudioEndSlider.Value = definition.SoundEndSeconds;

        AlertDurationBox.Text =
            definition.DurationSeconds.ToString();

        AlertPriorityBox.Text =
            definition.Priority.ToString();

        AlertFontFaceBox.Text =
            definition.FontFace;

        AlertFontSizeBox.Text =
            definition.FontSize.ToString();

        AlertFontColorBox.Text =
            definition.FontColor;

        foreach (var item in AlertAnimationBox.Items)
        {
            if (item is System.Windows.Controls.ComboBoxItem comboItem &&
                string.Equals(
                    comboItem.Content?.ToString(),
                    definition.Animation,
                    StringComparison.OrdinalIgnoreCase))
            {
                AlertAnimationBox.SelectedItem = comboItem;
                break;
            }
        }

        await PreviewAlertAsync();
    }

    private async Task SaveSelectedAlertDefinitionAsync()
    {
        try
        {
            SaveAlertDefinitionToSettings();

            await _settingsStore.SaveAsync(_settings);

            RefreshAlertLibrary(AlertTypeBox.SelectedItem as string);
            AlertPreviewStatusText.Text =
                "Alert gespeichert.";

            AlertPreviewStatusText.Foreground =
                System.Windows.Media.Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            AlertPreviewStatusText.Text =
                exception.Message;

            AlertPreviewStatusText.Foreground =
                System.Windows.Media.Brushes.IndianRed;
        }
    }

    private void SaveAlertDefinitionToSettings()
    {
        if (AlertTypeBox.SelectedItem is not string type ||
            !_settings.Alerts.Definitions.TryGetValue(
                type,
                out var definition))
        {
            return;
        }

        definition.TextTemplate =
            AlertTextTemplateBox.Text.Trim();

        definition.MediaPath =
            AlertMediaPathBox.Text.Trim();

        definition.SoundPath =
            AlertSoundPathBox.Text.Trim();

        definition.AudioOutputDeviceId = AlertAudioOutputDeviceBox.SelectedValue?.ToString() ?? "";
        definition.SoundStartSeconds = AlertAudioStartSlider.Value;
        definition.SoundEndSeconds = AlertAudioEndSlider.Value;

        definition.DurationSeconds =
            int.Parse(AlertDurationBox.Text.Trim());

        definition.Priority =
            int.Parse(AlertPriorityBox.Text.Trim());

        definition.FontFace =
            AlertFontFaceBox.Text.Trim();

        definition.FontSize =
            int.Parse(AlertFontSizeBox.Text.Trim());

        definition.FontColor =
            AlertFontColorBox.Text.Trim();

        definition.Animation =
            (AlertAnimationBox.SelectedItem
                as System.Windows.Controls.ComboBoxItem)
                ?.Content
                ?.ToString()
            ?? "Fade";
    }

    private async Task PreviewAlertAsync()
    {
        if (AlertTypeBox.SelectedItem is not string type)
        {
            return;
        }

        try
        {
            SaveAlertDefinitionToSettings();

            var variables = CreateAlertTestVariables(type);

            var preview = await _alertsModule.BuildPreviewAsync(
                type,
                AlertTestUserBox.Text.Trim(),
                variables);

            AlertPreviewTypeText.Text =
                preview.Type.ToUpperInvariant();

            AlertPreviewMessageText.Text =
                preview.Text;

            AlertPreviewMessageText.FontFamily =
                new System.Windows.Media.FontFamily(
                    preview.FontFace);

            AlertPreviewMessageText.FontSize =
                preview.FontSize;

            AlertPreviewMessageText.Foreground =
                new System.Windows.Media.BrushConverter()
                    .ConvertFromString(
                        preview.FontColor)
                as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.White;

            AlertPreviewMedia.Stop();
            AlertPreviewMedia.Source = null;

            if (!string.IsNullOrWhiteSpace(
                    preview.MediaPath) &&
                File.Exists(preview.MediaPath))
            {
                AlertPreviewMedia.Source =
                    new Uri(
                        preview.MediaPath,
                        UriKind.Absolute);

                AlertPreviewMedia.Position =
                    TimeSpan.Zero;

                AlertPreviewMedia.Play();
            }

            AlertPreviewStatusText.Text =
                "Vorschau aktualisiert.";

            AlertPreviewStatusText.Foreground =
                System.Windows.Media.Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            AlertPreviewStatusText.Text =
                exception.Message;

            AlertPreviewStatusText.Foreground =
                System.Windows.Media.Brushes.IndianRed;
        }
    }

    private async Task TestAlertInObsAsync()
    {
        if (AlertTypeBox.SelectedItem is not string type)
        {
            return;
        }

        try
        {
            SaveAlertDefinitionToSettings();
            await _settingsStore.SaveAsync(_settings);

            var variables = CreateAlertTestVariables(type);

            await _alertsModule.EnqueueAsync(
                type,
                AlertTestUserBox.Text.Trim(),
                variables,
                _settings.Alerts.Definitions[type].Priority);

            AlertPreviewStatusText.Text =
                "Alert wurde in die OBS-Queue eingereiht.";

            AlertPreviewStatusText.Foreground =
                System.Windows.Media.Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            AlertPreviewStatusText.Text =
                exception.Message;

            AlertPreviewStatusText.Foreground =
                System.Windows.Media.Brushes.IndianRed;

            MessageBox.Show(
                exception.Message,
                "Alert-Test fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static IReadOnlyDictionary<string, string>
        CreateAlertTestVariables(string type)
    {
        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        switch (type)
        {
            case "Raid":
                values["viewers"] = "25";
                break;

            case "Cheer":
                values["bits"] = "500";
                break;

            case "GiftSub":
                values["count"] = "5";
                break;

            case "ReSub":
                values["months"] = "12";
                break;
        }

        return values;
    }

    private async Task AuthorizeSpotifyAsync()
    {
        try
        {
            await SaveSettingsAsync();

            SpotifyConnectionStatusText.Text =
                "Spotify-Autorisierung wird geöffnet ...";
            SpotifyConnectionStatusText.Foreground =
                System.Windows.Media.Brushes.Goldenrod;

            await _spotifyModule.AuthorizeAsync();

            RefreshSpotifyUi();
        }
        catch (SpotifyRateLimitException exception)
        {
            BeginSpotifyRateLimitCooldown(exception.RetryAfter);
        }
        catch (Exception exception)
        {
            SpotifyConnectionStatusText.Text = exception.Message;
            SpotifyConnectionStatusText.Foreground =
                System.Windows.Media.Brushes.IndianRed;

            MessageBox.Show(
                exception.Message,
                "Spotify-Autorisierung fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ConnectSpotifyAsync(
        bool showErrorDialog = true)
    {
        try
        {
            SpotifyConnectionStatusText.Text =
                "Spotify wird verbunden ...";
            SpotifyConnectionStatusText.Foreground =
                System.Windows.Media.Brushes.Goldenrod;

            await _spotifyModule.ConnectAsync(CancellationToken.None);
            _spotifyOverlayConnectionLatched = true;
            _lastSpotifyOverlayMuted = null;

            RefreshSpotifyUi();
        }
        catch (SpotifyRateLimitException exception)
        {
            BeginSpotifyRateLimitCooldown(exception.RetryAfter);
        }
        catch (Exception exception)
        {
            SpotifyConnectionStatusText.Text = exception.Message;
            SpotifyConnectionStatusText.Foreground =
                System.Windows.Media.Brushes.IndianRed;

            if (showErrorDialog)
            {
                MessageBox.Show(
                    exception.Message,
                    "Spotify-Verbindung fehlgeschlagen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private async Task DisconnectSpotifyAsync()
    {
        _spotifyExplicitDisconnectInProgress = true;
        await _spotifyModule.DisconnectAsync(CancellationToken.None);
        _spotifyOverlayConnectionLatched = false;
        _lastStableSpotifyPlayback = null;
        _lastSpotifyOverlayMuted = null;

        try
        {
            await UpdateActiveOverlayJsonAsync(root =>
            {
                var spotify = root["spotify"] as JsonObject ?? new JsonObject();
                spotify["connected"] = false;
                spotify["isPlaying"] = false;
                spotify["showInOverlay"] = false;
                spotify["visible"] = false;
                root["spotify"] = spotify;
            });
        }
        catch (Exception exception)
        {
            _appLogger.Write(AppLogLevel.Debug, "Spotify", "Spotify-Trennstatus konnte nicht in die Overlay-JSON geschrieben werden: " + exception.Message);
        }

        SpotifyDashboardStatus.Text = "NICHT VERBUNDEN";
        SpotifyConnectionStatusText.Text = "Nicht verbunden";
        SpotifyConnectionStatusText.Foreground =
            System.Windows.Media.Brushes.Gray;
        SpotifyDeviceBox.ItemsSource = null;
        SpotifyPlaylistBox.ItemsSource = null;
        SpotifyTrackText.Text = "Kein Titel";
        SpotifyPlaybackDetailText.Text =
            "Playerstatus unbekannt";
    
        RefreshDashboardServiceActionButtons();
}


private Task ApplyCombinedAlertDuckingAsync()
    {
        var externalCount = _externalAlertActivity.ActiveCount;
        var isRunning = _suiteAlertRunning || externalCount > 0;
        var pending = _suiteAlertQueueLength + Math.Max(0, externalCount - (isRunning ? 1 : 0));
        var detail = externalCount > 0 ? $"Streamer.bot/externe Alerts aktiv: {externalCount}" : "Suite-Alertstatus";
        return HandleSpotifyAlertMuteAsync(new AlertPlaybackState(isRunning, null, pending, isRunning ? DateTimeOffset.Now : null, detail));
    }

    private async Task HandleSpotifyAlertMuteAsync(AlertPlaybackState state)
    {
        await _spotifyAlertMuteGate.WaitAsync();
        try
        {
            if (!_settings.Spotify.MuteDuringAlerts || string.Equals(_settings.Spotify.AlertDuckingMode, "None", StringComparison.OrdinalIgnoreCase))
            {
                if (!state.IsRunning && _spotifyAlertMuteActive)
                {
                    await RestoreSpotifyVolumeAfterAlertAsync();
                }
                return;
            }

            if (state.IsRunning)
            {
                if (_spotifyAlertMuteActive)
                {
                    return;
                }

                var snapshot = _spotifyModule.GetSnapshot();
                var playback = snapshot.Playback;
                if (!snapshot.Authenticated || !playback.IsPlaying || playback.Device is null)
                {
                    Dispatcher.Invoke(() =>
                        ServicesSpotifyAlertMuteStatusText.Text = "Kein laufender Spotify-Titel – keine Lautstärkeabsenkung nötig.");
                    return;
                }

                _spotifyVolumeBeforeAlert = Math.Clamp(playback.Device.VolumePercent, 0, 100);
                _spotifyWasPlayingBeforeAlert = playback.IsPlaying;
                _spotifyAlertMuteActive = true;

                    var alertVolume = Math.Clamp(_settings.Spotify.AlertMuteVolumePercent, 0, 100);
                await FadeSpotifyVolumeAsync(_spotifyVolumeBeforeAlert.Value, alertVolume, _settings.Spotify.AlertFadeOutMilliseconds);

                Dispatcher.Invoke(() =>
                {
                    ServicesSpotifyAlertMuteStatusText.Text =
                        $"Alert läuft: Spotify {_spotifyVolumeBeforeAlert}% → {alertVolume}%";
                    ServicesSpotifyAlertMuteStatusText.Foreground = Brushes.Orange;
                });
                return;
            }

            // Bei mehreren Alerts bleibt die Musik abgesenkt, bis auch die Queue leer ist.
            if (state.QueueLength > 0 || !_spotifyAlertMuteActive)
            {
                return;
            }

            await RestoreSpotifyVolumeAfterAlertAsync();
        }
        catch (Exception ex)
        {
            _appLogger.Write(AppLogLevel.Warning, "Spotify", "Spotify konnte für den Alert nicht automatisch geregelt werden.", ex);
            Dispatcher.Invoke(() =>
            {
                ServicesSpotifyAlertMuteStatusText.Text = "Spotify-Alert-Ducking fehlgeschlagen: " + ex.Message;
                ServicesSpotifyAlertMuteStatusText.Foreground = Brushes.IndianRed;
            });
        }
        finally
        {
            _spotifyAlertMuteGate.Release();
        }
    }

    private async Task RestoreSpotifyVolumeAfterAlertAsync()
    {
        var restoreVolume = _spotifyVolumeBeforeAlert;
        var shouldRestore = _spotifyWasPlayingBeforeAlert && restoreVolume.HasValue;

        _spotifyAlertMuteActive = false;
        _spotifyVolumeBeforeAlert = null;
        _spotifyWasPlayingBeforeAlert = false;

        if (!shouldRestore)
        {
            return;
        }

        var currentVolume = Math.Clamp(_spotifyModule.GetSnapshot().Playback.Device?.VolumePercent ?? 0, 0, 100);
        await FadeSpotifyVolumeAsync(currentVolume, restoreVolume!.Value, _settings.Spotify.AlertFadeInMilliseconds);

        Dispatcher.Invoke(() =>
        {
            ServicesSpotifyAlertMuteStatusText.Text =
                $"Alert beendet: Spotify auf {restoreVolume.Value}% zurückgestellt.";
            ServicesSpotifyAlertMuteStatusText.Foreground = Brushes.LightGreen;
        });
    }

    private static void SelectMillisecondsComboItem(ComboBox comboBox, int milliseconds)
    {
        if (comboBox is null) return;
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (int.TryParse(item.Tag?.ToString(), out var value) && value == milliseconds)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
        comboBox.SelectedIndex = 2;
    }

    private static int GetMillisecondsComboValue(ComboBox comboBox, int fallback)
    {
        return comboBox?.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var value)
            ? value
            : fallback;
    }

    private async Task FadeSpotifyVolumeAsync(int fromVolume, int toVolume, int durationMilliseconds)
    {
        fromVolume = Math.Clamp(fromVolume, 0, 100);
        toVolume = Math.Clamp(toVolume, 0, 100);
        durationMilliseconds = Math.Clamp(durationMilliseconds, 0, 5000);
        if (durationMilliseconds == 0 || fromVolume == toVolume)
        {
            await SetSpotifyVolumeTrackedAsync(toVolume);
            return;
        }

        var steps = Math.Clamp(durationMilliseconds / 100, 2, 10);
        var delay = Math.Max(50, durationMilliseconds / steps);
        for (var step = 1; step <= steps; step++)
        {
            var volume = (int)Math.Round(fromVolume + ((toVolume - fromVolume) * (step / (double)steps)));
            await SetSpotifyVolumeTrackedAsync(Math.Clamp(volume, 0, 100));
            if (step < steps) await Task.Delay(delay);
        }
    }

    private async Task QueueSpotifyVolumeUpdateAsync(
    int debounceMilliseconds,
    int? explicitVolume = null)
{
    SpotifyVolumeValueText.Text =
        $"{(int)Math.Round(SpotifyVolumeSlider.Value)} %";

    if (_updatingSpotifyUi)
    {
        return;
    }

    _spotifyVolumeChangeCts?.Cancel();
    _spotifyVolumeChangeCts?.Dispose();

    _spotifyVolumeChangeCts =
        new CancellationTokenSource();

    var cancellationToken =
        _spotifyVolumeChangeCts.Token;

    try
    {
        if (debounceMilliseconds > 0)
        {
            await Task.Delay(
                debounceMilliseconds,
                cancellationToken);
        }

        var volume = explicitVolume ??
            (int)Math.Round(
                SpotifyVolumeSlider.Value);

        volume = Math.Clamp(volume, 0, 100);
        _lastRequestedSpotifyVolumePercent = volume;
        _lastRequestedSpotifyVolumeAt = DateTimeOffset.UtcNow;

        await _spotifyModule.SetVolumeAsync(
            volume,
            cancellationToken);

        // Die Spotify Web API meldet die neue Gerätelautstärke teilweise erst
        // beim nächsten Polling. Die Overlay-Sichtbarkeit deshalb sofort anhand
        // des vom Benutzer gesetzten Werts aktualisieren.
        await ApplySpotifyOverlayMuteStateAsync(volume <= 0);
        await WriteSpotifyOverlayRuntimeDataAsync(_spotifyModule.GetSnapshot(), _spotifyModule.GetSnapshot().Playback);
    }
    catch (OperationCanceledException)
    {
        // A newer slider position superseded this update.
    }
    catch (Exception exception)
    {
        SpotifyPlaybackDetailText.Text =
            "Lautstärke konnte nicht gesetzt werden: " +
            exception.Message;

        SpotifyPlaybackDetailText.Foreground =
            System.Windows.Media.Brushes.IndianRed;
    }
}


    private void RefreshSpotifyStatisticsUi()
    {
        var statistics = _spotifyListeningStatistics.GetSnapshot();
        ServicesSpotifyStatisticsSummaryText.Text = $"{statistics.TotalPlays} erkannte Titelstarts · {statistics.TotalListeningTime:hh\\:mm\\:ss} Wiedergabezeit";
        ServicesSpotifyTopTracksList.ItemsSource = statistics.TopTracks;
        ServicesSpotifyTopArtistsList.ItemsSource = statistics.TopArtists;
        ServicesSpotifyStatisticsEmptyText.Visibility = statistics.TotalPlays == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task RefreshSpotifyAsync()
    {
        try
        {
            await _spotifyModule.RefreshAsync();
            RefreshSpotifyUi();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Spotify konnte nicht aktualisiert werden",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task StartSpotifyPlaylistAsync()
    {
        await ExecuteSpotifyAsync(
            () => _spotifyModule.StartConfiguredPlaylistAsync());
    }

    private async Task TestSpotifyFadeAsync()
    {
        var seconds = int.Parse(
            SpotifyFadeOutSecondsBox.Text.Trim());

        await ExecuteSpotifyAsync(
            () => _spotifyModule.FadeToAsync(
                targetVolumePercent: 0,
                duration: TimeSpan.FromSeconds(seconds),
                pauseAtEnd:
                    SpotifyPauseAfterFadeBox.IsChecked == true));
    }

    private async Task ExecuteSpotifyAsync(
        Func<Task> action)
    {
        if (DateTimeOffset.Now < _spotifyRateLimitUntil)
        {
            UpdateSpotifyRateLimitStatus();
            return;
        }

        try
        {
            await action();
            await Task.Delay(500);
            await _spotifyModule.RefreshPlaybackAsync();
            await _spotifyModule.RefreshLibraryIfStaleAsync();
            ClearSpotifyRateLimitStatus();
            RefreshSpotifyUi();
        }
        catch (SpotifyRateLimitException exception)
        {
            BeginSpotifyRateLimitCooldown(exception.RetryAfter);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Spotify-Aktion fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void BeginSpotifyRateLimitCooldown(TimeSpan retryAfter)
    {
        var effectiveDelay = retryAfter <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(5)
            : retryAfter;

        _spotifyRateLimitUntil = DateTimeOffset.Now.Add(effectiveDelay);
        UpdateSpotifyRateLimitStatus();

        if (DateTimeOffset.Now - _lastSpotifyRateLimitNotice > TimeSpan.FromMinutes(1))
        {
            _lastSpotifyRateLimitNotice = DateTimeOffset.Now;
            AddDashboardNotification(
                $"Spotify API-Limit erreicht. Steuerung wird für etwa {Math.Ceiling(effectiveDelay.TotalSeconds):0} Sekunden pausiert.",
                "Warnung");
        }

        _spotifyRateLimitResetCts?.Cancel();
        _spotifyRateLimitResetCts?.Dispose();
        _spotifyRateLimitResetCts = new CancellationTokenSource();
        _ = ResetSpotifyRateLimitAfterDelayAsync(effectiveDelay, _spotifyRateLimitResetCts.Token);
    }

    private async Task ResetSpotifyRateLimitAfterDelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        try
        {
            while (DateTimeOffset.Now < _spotifyRateLimitUntil)
            {
                await Dispatcher.InvokeAsync(UpdateSpotifyRateLimitStatus);
                var remaining = _spotifyRateLimitUntil - DateTimeOffset.Now;
                await Task.Delay(
                    remaining > TimeSpan.FromSeconds(1)
                        ? TimeSpan.FromSeconds(1)
                        : remaining,
                    cancellationToken);
            }

            await Dispatcher.InvokeAsync(ClearSpotifyRateLimitStatus);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void UpdateSpotifyRateLimitStatus()
    {
        var remaining = Math.Max(1, (int)Math.Ceiling((_spotifyRateLimitUntil - DateTimeOffset.Now).TotalSeconds));
        var message = $"Spotify-Limit erreicht – Steuerung in etwa {remaining} Sek. wieder verfügbar.";

        ServicesSpotifyNowPlayingText.Text = message;
        ServicesSpotifyNowPlayingText.Foreground = System.Windows.Media.Brushes.Orange;

        SpotifyConnectionStatusText.Text = message;
        SpotifyConnectionStatusText.Foreground = System.Windows.Media.Brushes.Orange;
    }

    private void ClearSpotifyRateLimitStatus()
    {
        _spotifyRateLimitUntil = DateTimeOffset.MinValue;
        RefreshSpotifyUi();
    }

    private void BrowseExecutable(System.Windows.Controls.TextBox target, string filter)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter, CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) target.Text = dialog.FileName;
    }

    private void LaunchConfiguredExecutable(string? path, string displayName, bool showMissingMessage = true)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            if (showMissingMessage) MessageBox.Show($"Bitte zuerst unter Einstellungen den Programmpfad für {displayName} hinterlegen.", $"{displayName} starten", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var processName = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrWhiteSpace(processName) && Process.GetProcessesByName(processName).Length > 0)
            {
                return;
            }

            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? ""
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, $"{displayName} konnte nicht gestartet werden", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BrowseOverlayFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Overlay-Ordner auswählen",
            Multiselect = false
        };

        var currentPath = OverlayRootBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath))
        {
            dialog.InitialDirectory = currentPath;
        }

        if (dialog.ShowDialog(this) == true)
        {
            OverlayRootBox.Text = dialog.FolderName;
        }
    }

    private void OpenConfiguredTarget(string? target, string displayName, bool showMissingMessage = true)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            if (showMissingMessage) MessageBox.Show($"Bitte zuerst unter Einstellungen die URL für {displayName} hinterlegen.", displayName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, $"{displayName} konnte nicht geöffnet werden", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private Task PrepareStreamAsync() => PrepareStreamWithConfiguredServicesAsync();

    private async Task PrepareStreamWithConfiguredServicesAsync()
    {
        try
        {
            SetPrepareProgress(5, "Programme werden gestartet …", true);
            if (_settings.Obs.ConnectOnPrepare) LaunchConfiguredExecutable(_settings.Obs.ExecutablePath, "OBS", showMissingMessage: false);
            if (_settings.Spotify.ConnectOnPrepare) LaunchConfiguredExecutable(_settings.Spotify.ExecutablePath, "Spotify", showMissingMessage: false);
            if (_settings.StreamerBot.ConnectOnPrepare) LaunchConfiguredExecutable(_settings.StreamerBot.ExecutablePath, "Streamer.bot", showMissingMessage: false);
            if (_settings.Twitch.ConnectOnPrepare && !string.IsNullOrWhiteSpace(_settings.Twitch.CreatorDashboardUrl)) OpenConfiguredTarget(_settings.Twitch.CreatorDashboardUrl, "Twitch Creator Dashboard", showMissingMessage: false);

            SetPrepareProgress(20, "Warte auf gestartete Dienste …", true);
            await Task.Delay(1500);

            SetPrepareProgress(35, "OBS wird gestartet und vorbereitet …", true);
            if (_settings.Obs.ConnectOnPrepare)
            {
                await WaitForObsReadyDuringPreparationAsync();
            }

            SetPrepareProgress(50, "Twitch wird verbunden …", true);
            if (_settings.Twitch.ConnectOnPrepare && !_twitchModule.GetSnapshot().Authenticated) await ConnectTwitchAsync(showErrorDialog: false);

            SetPrepareProgress(65, "Spotify wird verbunden …", true);
            if (_settings.Spotify.ConnectOnPrepare && !_spotifyModule.GetSnapshot().Authenticated) await ConnectSpotifyAsync(showErrorDialog: false);

            SetPrepareProgress(78, "Streamer.bot wird verbunden …", true);
            if (_settings.StreamerBot.ConnectOnPrepare && (_streamerBotSocket is null || _streamerBotSocket.State != System.Net.WebSockets.WebSocketState.Open)) await ConnectStreamerBotAsync();

            SetPrepareProgress(88, "Workflow und Startszene werden vorbereitet …", true);
            await ExecuteWorkflowAsync(() => _workflowModule.Service.PrepareAsync());

            SetPrepareProgress(95, "Preflight-Check läuft …", true);
            await RunDashboardPreflightAsync();

            SetPrepareProgress(100, "Stream ist vorbereitet.", true);
            AddDashboardNotification("Stream vorbereiten abgeschlossen.", "Info");
            await Task.Delay(1200);
            SetPrepareProgress(100, "Stream ist vorbereitet.", false);
        }
        catch (Exception exception)
        {
            SetPrepareProgress(0, "Vorbereitung fehlgeschlagen: " + exception.Message, true);
            MessageBox.Show(exception.Message, "Stream vorbereiten fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }


    private async Task WaitForObsReadyDuringPreparationAsync()
    {
        const int maximumAttempts = 25;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            if (!_obsClient.IsConnected)
            {
                await ConnectObsAsync(showErrorDialog: false);
            }

            if (_obsClient.IsConnected)
            {
                try
                {
                    // OBS WebSocket kann bereits verbunden sein, während OBS selbst noch
                    // keine Frontend-/Szenenbefehle akzeptiert. Eine erfolgreiche Abfrage
                    // der Szenenliste dient deshalb als Bereitschaftstest.
                    await _obsClient.GetSceneListAsync();
                    return;
                }
                catch (Exception exception)
                {
                    lastException = exception;
                }
            }

            SetPrepareProgress(
                35,
                $"OBS wird vorbereitet … Versuch {attempt}/{maximumAttempts}",
                true);
            await Task.Delay(800);
        }

        throw new InvalidOperationException(
            "OBS wurde gestartet, ist aber noch nicht bereit. Bitte prüfe, ob OBS vollständig geöffnet ist und der WebSocket-Server aktiv ist.",
            lastException);
    }

    private void SetPrepareProgress(double value, string message, bool visible)
    {
        void Apply()
        {
            var normalizedValue = Math.Clamp(value, 0, 100);
            DashboardPrepareProgressBar.Value = normalizedValue;
            DashboardPrepareProgressPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            DashboardPrepareProgressText.Text = message;
            DashboardPrepareProgressPercentText.Text = $"{normalizedValue:0} %";
            DashboardCommandCenterSummaryText.Text = message;
        }

        if (Dispatcher.CheckAccess()) Apply();
        else Dispatcher.BeginInvoke(Apply);
    }

    private async Task LoadSpotifyAlbumCoverAsync(string? imageUrl)
    {
        if (string.Equals(_lastSpotifyAlbumCoverUrl, imageUrl, StringComparison.Ordinal))
        {
            return;
        }

        _lastSpotifyAlbumCoverUrl = imageUrl;
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            SpotifyAlbumCoverImage.Source = null;
            ServicesSpotifyAlbumCoverImage.Source = null;
            DashboardSpotifyAlbumCoverImage.Source = null;
            return;
        }
        try
        {
            var bytes = await AlbumCoverHttpClient.GetByteArrayAsync(imageUrl);
            await Dispatcher.InvokeAsync(() =>
            {
                using var stream = new System.IO.MemoryStream(bytes);
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                SpotifyAlbumCoverImage.Source = image;
                ServicesSpotifyAlbumCoverImage.Source = image;
                DashboardSpotifyAlbumCoverImage.Source = image;
            });
        }
        catch
        {
            await Dispatcher.InvokeAsync(() =>
            {
                SpotifyAlbumCoverImage.Source = null;
                ServicesSpotifyAlbumCoverImage.Source = null;
                DashboardSpotifyAlbumCoverImage.Source = null;
            });
        }
    }

    private async Task SearchSpotifyTracksAsync()
    {
        var query = ServicesSpotifyTrackSearchBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(query))
        {
            ServicesSpotifyTrackSearchResultsList.ItemsSource = null;
            ServicesSpotifyTrackSearchStatusText.Text = "Bitte einen Suchbegriff eingeben.";
            return;
        }

        await ExecuteUiActionAsync(
            ServicesSpotifyTrackSearchButton,
            "Spotify-Titel suchen",
            async () =>
            {
                var tracks = await _spotifyModule.SearchTracksAsync(query);
                var items = tracks.Select(track => new SpotifyTrackSearchItem(track)).ToList();
                ServicesSpotifyTrackSearchResultsList.ItemsSource = items;
                ServicesSpotifyTrackSearchStatusText.Text = items.Count == 0
                    ? "Keine Titel gefunden."
                    : $"{items.Count} Titel gefunden.";
            });
    }

    private sealed record SpotifyTrackSearchItem(SpotifyTrack Track)
    {
        public string DisplayText => $"{Track.Artist} – {Track.Name} ({Track.Album})";
    }

    private sealed record SpotifyPlaylistTrackItem(SpotifyTrack Track)
    {
        public string DisplayText => $"{Track.Artist} – {Track.Name} ({Track.Album})";
    }

    private sealed record SpotifyQueueItem(SpotifyTrack Track, int Position)
    {
        public string DisplayText => $"{Position}. {Track.Artist} – {Track.Name} ({Track.Album})";
    }

    private sealed record SpotifySavedTrackItem(SpotifyTrack Track)
    {
        public string DisplayText => $"{Track.Artist} – {Track.Name} · {Track.Album}";
    }

    private sealed record SpotifyHistoryItem(SpotifyRecentlyPlayedItem Item)
    {
        public string DisplayText =>
            $"{Item.PlayedAt.ToLocalTime():dd.MM. HH:mm} · {Item.Track.Artist} – {Item.Track.Name}";
    }

    private async Task StartSpotifyPlaylistAndRememberAsync(SpotifyPlaylist playlist)
    {
        await ExecuteSpotifyAsync(() => _spotifyModule.StartPlaylistAsync(playlist.Uri));

        _settings.Spotify.RecentPlaylistUris.RemoveAll(uri =>
            string.Equals(uri, playlist.Uri, StringComparison.OrdinalIgnoreCase));
        _settings.Spotify.RecentPlaylistUris.Insert(0, playlist.Uri);
        if (_settings.Spotify.RecentPlaylistUris.Count > 5)
        {
            _settings.Spotify.RecentPlaylistUris.RemoveRange(
                5,
                _settings.Spotify.RecentPlaylistUris.Count - 5);
        }

        await _settingsStore.SaveAsync(_settings);
        RefreshSpotifyQuickPlaylists();
    }

    private async Task StartSpotifyQuickPlaylistAsync(SpotifyPlaylist? playlist)
    {
        if (playlist is null)
        {
            ServicesSpotifyPlaylistStatusText.Text = "Bitte zuerst eine Favoriten- oder zuletzt verwendete Playlist auswählen.";
            return;
        }

        await StartSpotifyPlaylistAndRememberAsync(playlist);
        ServicesSpotifyPlaylistStatusText.Text = $"Gestartet: {playlist.Name}";
    }

    private async Task ToggleSelectedSpotifyPlaylistFavoriteAsync()
    {
        if (ServicesSpotifyPlaylistBox.SelectedItem is not SpotifyPlaylist playlist)
        {
            ServicesSpotifyPlaylistStatusText.Text = "Bitte zuerst eine Playlist auswählen.";
            return;
        }

        var existing = _settings.Spotify.FavoritePlaylistUris.FirstOrDefault(uri =>
            string.Equals(uri, playlist.Uri, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            _settings.Spotify.FavoritePlaylistUris.Add(playlist.Uri);
            ServicesSpotifyPlaylistStatusText.Text = $"Favorit hinzugefügt: {playlist.Name}";
        }
        else
        {
            _settings.Spotify.FavoritePlaylistUris.Remove(existing);
            ServicesSpotifyPlaylistStatusText.Text = $"Favorit entfernt: {playlist.Name}";
        }

        await _settingsStore.SaveAsync(_settings);
        UpdateSpotifyFavoriteButton();
        RefreshSpotifyQuickPlaylists();
    }

    private void UpdateSpotifyFavoriteButton()
    {
        if (ServicesSpotifyPlaylistBox.SelectedItem is not SpotifyPlaylist playlist)
        {
            ServicesSpotifyToggleFavoritePlaylistButton.Content = "☆ FAVORIT";
            return;
        }

        var isFavorite = _settings.Spotify.FavoritePlaylistUris.Any(uri =>
            string.Equals(uri, playlist.Uri, StringComparison.OrdinalIgnoreCase));
        ServicesSpotifyToggleFavoritePlaylistButton.Content = isFavorite
            ? "★ FAVORIT ENTFERNEN"
            : "☆ ALS FAVORIT";
    }

    private void RefreshSpotifyQuickPlaylists()
    {
        var playlists = _spotifyModule.GetSnapshot().Playlists;
        var byUri = playlists
            .Where(playlist => !string.IsNullOrWhiteSpace(playlist.Uri))
            .GroupBy(playlist => playlist.Uri, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var orderedUris = _settings.Spotify.FavoritePlaylistUris
            .Concat(_settings.Spotify.RecentPlaylistUris)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var quickPlaylists = orderedUris
            .Where(byUri.ContainsKey)
            .Select(uri => byUri[uri])
            .ToList();

        var selectedUri = (ServicesSpotifyQuickPlaylistBox.SelectedItem as SpotifyPlaylist)?.Uri
                          ?? (DashboardSpotifyQuickPlaylistBox.SelectedItem as SpotifyPlaylist)?.Uri;
        ServicesSpotifyQuickPlaylistBox.ItemsSource = quickPlaylists;
        DashboardSpotifyQuickPlaylistBox.ItemsSource = quickPlaylists;

        var selected = !string.IsNullOrWhiteSpace(selectedUri)
            ? quickPlaylists.FirstOrDefault(playlist =>
                string.Equals(playlist.Uri, selectedUri, StringComparison.OrdinalIgnoreCase))
            : quickPlaylists.FirstOrDefault();
        ServicesSpotifyQuickPlaylistBox.SelectedItem = selected;
        DashboardSpotifyQuickPlaylistBox.SelectedItem = selected;
    }

    private void ApplySpotifyPlaylistFilter()
    {
        var playlists = _spotifyModule.GetSnapshot().Playlists;
        var filter = ServicesSpotifyPlaylistFilterBox.Text?.Trim() ?? "";
        var filtered = string.IsNullOrWhiteSpace(filter)
            ? playlists
            : playlists.Where(playlist =>
                    playlist.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    playlist.OwnerName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var selectedUri = (ServicesSpotifyPlaylistBox.SelectedItem as SpotifyPlaylist)?.Uri;
        ServicesSpotifyPlaylistBox.ItemsSource = filtered;
        if (!string.IsNullOrWhiteSpace(selectedUri))
        {
            ServicesSpotifyPlaylistBox.SelectedItem = filtered.FirstOrDefault(p => p.Uri == selectedUri);
        }
        ServicesSpotifyPlaylistStatusText.Text = $"{filtered.Count} von {playlists.Count} Playlists";
    }

    private async Task LoadSelectedSpotifyPlaylistTracksAsync()
    {
        if (ServicesSpotifyPlaylistBox.SelectedItem is not SpotifyPlaylist playlist)
        {
            ServicesSpotifyPlaylistStatusText.Text = "Bitte zuerst eine Playlist auswählen.";
            return;
        }

        await ExecuteUiActionAsync(
            ServicesSpotifyLoadPlaylistTracksButton,
            "Spotify-Playlisttitel laden",
            async () =>
            {
                var tracks = await _spotifyModule.GetPlaylistTracksAsync(playlist);
                ServicesSpotifyPlaylistTracksList.ItemsSource = tracks
                    .Select(track => new SpotifyPlaylistTrackItem(track))
                    .ToList();
                ServicesSpotifyPlaylistStatusText.Text = tracks.Count == 0
                    ? "Die Playlist enthält keine verfügbaren Titel."
                    : $"{tracks.Count} Titel geladen.";
            });
    }

    private async Task ExecuteSelectedSpotifyPlaylistTrackAsync(bool playImmediately)
    {
        if (ServicesSpotifyPlaylistTracksList.SelectedItem is not SpotifyPlaylistTrackItem selected)
        {
            ServicesSpotifyPlaylistStatusText.Text = "Bitte zuerst einen Playlist-Titel auswählen.";
            return;
        }

        var button = playImmediately
            ? ServicesSpotifyPlayPlaylistTrackButton
            : ServicesSpotifyQueuePlaylistTrackButton;
        await ExecuteUiActionAsync(
            button,
            playImmediately ? "Spotify-Titel abspielen" : "Spotify-Titel vormerken",
            async () =>
            {
                if (playImmediately)
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.PlayTrackAsync(selected.Track));
                    ServicesSpotifyPlaylistStatusText.Text =
                        $"Wiedergabe gestartet: {selected.Track.Artist} – {selected.Track.Name}";
                }
                else
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.AddToQueueAsync(selected.Track));
                    ServicesSpotifyPlaylistStatusText.Text =
                        $"Zur Warteschlange hinzugefügt: {selected.Track.Artist} – {selected.Track.Name}";
                }
                RefreshSpotifyUi();
            });
    }

    private void UpdateSpotifyDeviceSelectionUi()
    {
        if (ServicesSpotifyDeviceBox.SelectedItem is not SpotifyDevice device)
        {
            ServicesSpotifyDeviceStatusText.Text = "Kein Gerät ausgewählt.";
            return;
        }

        var active = device.IsActive ? "aktiv" : "inaktiv";
        var volume = device.SupportsVolume ? $" · Lautstärke {device.VolumePercent} %" : " · Lautstärke nicht steuerbar";
        var restricted = device.IsRestricted ? " · eingeschränkt" : string.Empty;
        var preferred = string.Equals(
            _settings.Spotify.PreferredDeviceId,
            device.Id,
            StringComparison.Ordinal)
            ? " · Standardgerät"
            : string.Empty;

        var automatic = _settings.Spotify.AutoTransferToPreferredDevice
            ? " · automatische Übernahme aktiv"
            : string.Empty;
        ServicesSpotifyDeviceStatusText.Text =
            $"{device.Type} · {active}{volume}{restricted}{preferred}{automatic}";
    }


    private async Task SaveSpotifyDeviceBehaviorAsync()
    {
        _settings.Spotify.AutoTransferToPreferredDevice = ServicesSpotifyAutoTransferPreferredBox.IsChecked == true;
        _settings.Spotify.UseActiveDeviceWhenPreferredUnavailable = ServicesSpotifyUseActiveFallbackBox.IsChecked == true;
        _settings.Spotify.SmartAutomationEnabled = ServicesSpotifySmartAutomationBox.IsChecked == true;
        _settings.Spotify.HealthMonitorEnabled = ServicesSpotifyHealthMonitorBox.IsChecked == true;
        _settings.Spotify.AutoRecoverPlayback = ServicesSpotifyAutoRecoverBox.IsChecked == true;
        await _settingsStore.SaveAsync(_settings);
        UpdateSpotifyDeviceSelectionUi();
    }

    private async Task ActivatePreferredSpotifyDeviceAsync()
    {
        await ExecuteUiActionAsync(
            ServicesSpotifyActivatePreferredDeviceButton,
            "Spotify-Standardgerät aktivieren",
            async () =>
            {
                SpotifyDevice? device = null;
                await ExecuteSpotifyAsync(async () =>
                {
                    device = await _spotifyModule.ActivatePreferredDeviceAsync(play: false);
                });
                if (device is null) return;
                ServicesSpotifyDeviceBox.SelectedItem = device;
                SpotifyDeviceBox.SelectedItem = device;
                ServicesSpotifyDeviceStatusText.Text = $"{device.Name} wurde als Wiedergabegerät aktiviert.";
                RefreshSpotifyUi();
            });
    }

    private async Task TransferSelectedSpotifyDeviceAsync(bool play)
    {
        if (ServicesSpotifyDeviceBox.SelectedItem is not SpotifyDevice device)
        {
            ServicesSpotifyDeviceStatusText.Text = "Bitte zuerst ein Spotify-Gerät auswählen.";
            return;
        }

        await ExecuteUiActionAsync(
            play ? ServicesSpotifyTransferAndPlayDeviceButton : ServicesSpotifyTransferDeviceButton,
            play ? "Spotify-Wiedergabe übertragen und starten" : "Spotify-Wiedergabe übertragen",
            async () =>
            {
                await ExecuteSpotifyAsync(() => _spotifyModule.TransferPlaybackAsync(device.Id, play));
                _settings.Spotify.PreferredDeviceId = device.Id;
                await _settingsStore.SaveAsync(_settings);
                await RefreshSpotifyAsync();
            });
    }

    private async Task SaveSelectedSpotifyDeviceAsync()
    {
        if (ServicesSpotifyDeviceBox.SelectedItem is not SpotifyDevice device)
        {
            ServicesSpotifyDeviceStatusText.Text = "Bitte zuerst ein Spotify-Gerät auswählen.";
            return;
        }

        _settings.Spotify.PreferredDeviceId = device.Id;
        await _settingsStore.SaveAsync(_settings);
        SpotifyDeviceBox.SelectedItem = device;
        UpdateSpotifyDeviceSelectionUi();
        ServicesSpotifyDeviceStatusText.Text += " · gespeichert";
    }

    private async Task SaveSpotifySmartAutomationSettingsAsync()
    {
        _settings.Spotify.SmartAutomationEnabled = ServicesSpotifySmartAutomationBox.IsChecked == true;
        _settings.Spotify.HealthMonitorEnabled = ServicesSpotifyHealthMonitorBox.IsChecked == true;
        _settings.Spotify.AutoRecoverPlayback = ServicesSpotifyAutoRecoverBox.IsChecked == true;
        await _settingsStore.SaveAsync(_settings);
        RefreshSpotifyAutomationLogUi();
    }

    private async Task CreateDefaultSpotifyAutomationRulesAsync()
    {
        var rules = new List<SpotifyAutomationRuleSettings>();
        if (!string.IsNullOrWhiteSpace(_settings.Obs.StartScene))
            rules.Add(new() { Name = "Startszene-Musik", TriggerValue = _settings.Obs.StartScene, ActionType = "StartPlaylist", PlaylistUri = _settings.Spotify.StartPlaylistUri, Shuffle = _settings.Spotify.ShuffleSelectedPlaylist });
        if (!string.IsNullOrWhiteSpace(_settings.Obs.LiveScene))
            rules.Add(new() { Name = "Live-Szene fortsetzen", TriggerValue = _settings.Obs.LiveScene, ActionType = "Resume" });
        if (!string.IsNullOrWhiteSpace(_settings.Obs.EndScene))
            rules.Add(new() { Name = "Endszene-Musik", TriggerValue = _settings.Obs.EndScene, ActionType = "StartPlaylist", PlaylistUri = _settings.Spotify.StartPlaylistUri, Shuffle = true });
        _settings.Spotify.AutomationRules = rules;
        await _settingsStore.SaveAsync(_settings);
        _spotifyAutomationLog.Add("Regeln", $"{rules.Count} Standardregeln aus den OBS-Szenen erstellt.");
        RefreshSpotifyAutomationLogUi();
    }

    private async Task ExecuteSpotifySceneAutomationAsync(string sceneName, bool force = false)
    {
        if ((!_settings.Spotify.SmartAutomationEnabled && !force) || string.IsNullOrWhiteSpace(sceneName)) return;
        if (!await _spotifyAutomationLock.WaitAsync(0)) return;
        try
        {
            var rules = _settings.Spotify.AutomationRules
                .Where(r => r.Enabled && string.Equals(r.TriggerType, "ObsSceneChanged", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.TriggerValue, sceneName, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var rule in rules)
            {
                try
                {
                    if (rule.DelaySeconds > 0) await Task.Delay(TimeSpan.FromSeconds(rule.DelaySeconds));
                    var isConfiguredLiveScene = string.Equals(
                        sceneName,
                        string.IsNullOrWhiteSpace(_settings.Obs.LiveScene) ? "Game" : _settings.Obs.LiveScene.Trim(),
                        StringComparison.OrdinalIgnoreCase);
                    if (isConfiguredLiveScene &&
                        string.Equals(rule.ActionType, "Pause", StringComparison.OrdinalIgnoreCase) &&
                        _settings.Spotify.SetVolumeOnLiveTransition &&
                        !_settings.Spotify.MuteOnLiveTransition)
                    {
                        _spotifyAutomationLog.Add(rule.Name,
                            "Veraltete Pause-Regel für die Game-Szene übersprungen; konfiguriert ist nur eine Lautstärkeänderung.");
                        continue;
                    }

                    switch (rule.ActionType)
                    {
                        case "StartPlaylist":
                            if (string.IsNullOrWhiteSpace(rule.PlaylistUri)) throw new InvalidOperationException("Keine Playlist in der Regel hinterlegt.");
                            await _spotifyModule.StartPlaylistAsync(rule.PlaylistUri, rule.Shuffle);
                            break;
                        case "Pause": await _spotifyModule.PauseAsync(); break;
                        case "SetVolume": await _spotifyModule.SetVolumeAsync(Math.Clamp(rule.VolumePercent, 0, 100)); break;
                        default: await _spotifyModule.ResumeAsync(); break;
                    }
                    _spotifyAutomationLog.Add(rule.Name, $"Aktion {rule.ActionType} für Szene '{sceneName}' ausgeführt.");
                }
                catch (Exception ex)
                {
                    _spotifyAutomationLog.Add(rule.Name, ex.Message, false);
                }
            }
        }
        finally
        {
            _spotifyAutomationLock.Release();
            RefreshSpotifyAutomationLogUi();
        }
    }

    private async Task RunSpotifyHealthMonitorAsync(SpotifySnapshot snapshot)
    {
        if (!_settings.Spotify.HealthMonitorEnabled || !snapshot.Authenticated) return;
        var status = snapshot.Playback.Device is null ? "Kein aktives Gerät" : snapshot.Playback.Device.IsRestricted ? "Gerät nicht steuerbar" : snapshot.Playback.IsPlaying ? "Wiedergabe aktiv" : "Bereit / pausiert";
        ServicesSpotifyHealthStatusText.Text = status;
        if (!_settings.Spotify.AutoRecoverPlayback || snapshot.Playback.Device is not null || DateTimeOffset.UtcNow - _lastSpotifyHealthRecoveryAt < TimeSpan.FromMinutes(2)) return;
        _lastSpotifyHealthRecoveryAt = DateTimeOffset.UtcNow;
        try
        {
            var device = await _spotifyModule.ActivatePreferredDeviceAsync(play: false);
            _spotifyAutomationLog.Add("Health Monitor", $"Wiedergabegerät '{device.Name}' automatisch wieder aktiviert.");
        }
        catch (Exception ex)
        {
            _spotifyAutomationLog.Add("Health Monitor", "Automatische Gerätewiederherstellung fehlgeschlagen: " + ex.Message, false);
        }
        RefreshSpotifyAutomationLogUi();
    }

    private void RefreshSpotifyAutomationUi(SpotifySnapshot snapshot)
    {
        ServicesSpotifyAutomationStatusText.Text = _settings.Spotify.SmartAutomationEnabled
            ? $"Aktiv · {_settings.Spotify.AutomationRules.Count(r => r.Enabled)} Regeln"
            : "Deaktiviert";
        ServicesSpotifyAutomationRulesList.ItemsSource = _settings.Spotify.AutomationRules.Select(r => $"{(r.Enabled ? "✓" : "–")} {r.Name}: {r.TriggerValue} → {r.ActionType}").ToList();
        ServicesSpotifyHealthStatusText.Text = !snapshot.Authenticated ? "Spotify nicht verbunden" : snapshot.Playback.Device is null ? "Kein aktives Gerät" : snapshot.Playback.Device.IsRestricted ? "Gerät nicht fernsteuerbar" : "Verbindung gesund";
        RefreshSpotifyAutomationLogUi();
    }

    private void RefreshSpotifyAutomationLogUi()
    {
        ServicesSpotifyAutomationLogList.ItemsSource = _spotifyAutomationLog.GetRecent().Select(e => e.DisplayText).ToList();
    }

    private void RefreshSpotifyUi()
    {
        var snapshot = _spotifyModule.GetSnapshot();
        _spotifyListeningStatistics.Observe(snapshot.Playback);
        RefreshSpotifyStatisticsUi();
        RefreshSpotifyAutomationUi(snapshot);
        _ = RunSpotifyHealthMonitorAsync(snapshot);

        SpotifyDashboardStatus.Text = snapshot.Authenticated
            ? "VERBUNDEN"
            : "NICHT VERBUNDEN";

        SpotifyDashboardLamp.Fill = snapshot.Authenticated
            ? System.Windows.Media.Brushes.LimeGreen
            : System.Windows.Media.Brushes.IndianRed;

        SpotifyConnectionStatusText.Text = snapshot.Authenticated
            ? "Verbunden als " + snapshot.UserDisplayName
            : "Nicht verbunden";

        SpotifyConnectionStatusText.Foreground = snapshot.Authenticated
            ? System.Windows.Media.Brushes.LightGreen
            : System.Windows.Media.Brushes.Gray;

        SpotifyDeviceBox.ItemsSource = snapshot.Devices;
        SpotifyPlaylistBox.ItemsSource = snapshot.Playlists;
        ServicesSpotifyDeviceBox.ItemsSource = snapshot.Devices;
        ApplySpotifyPlaylistFilter();
        ServicesSpotifyStartPlaylistBox.ItemsSource = snapshot.Playlists;
        DashboardSpotifyPlaylistBox.ItemsSource = snapshot.Playlists;
        RefreshSpotifyQuickPlaylists();
        UpdateSpotifyFavoriteButton();
        ServicesSpotifyNowPlayingText.Text = snapshot.Playback.Track is null
            ? "Kein Titel"
            : snapshot.Playback.Track.Artist + " – " + snapshot.Playback.Track.Name;
        ServicesSpotifyAlbumText.Text = snapshot.Playback.Track is null ||
                                        string.IsNullOrWhiteSpace(snapshot.Playback.Track.Album)
            ? "Album: -"
            : "Album: " + snapshot.Playback.Track.Album;
        DashboardSpotifyTrackText.Text = snapshot.Playback.Track is null
            ? "Kein Spotify-Titel"
            : snapshot.Playback.Track.Artist + " – " + snapshot.Playback.Track.Name;
        DashboardSpotifyAlbumText.Text = snapshot.Playback.Track is null ||
                                         string.IsNullOrWhiteSpace(snapshot.Playback.Track.Album)
            ? "Album: -"
            : "Album: " + snapshot.Playback.Track.Album;
        DashboardSpotifyPlaybackStateText.Text = snapshot.Playback.Track is null
            ? "BEREIT"
            : snapshot.Playback.IsPlaying ? "WIEDERGABE LÄUFT" : "PAUSIERT";
        DashboardSpotifyDeviceText.Text = snapshot.Playback.Device is null
            ? "Gerät: keines aktiv"
            : $"Gerät: {snapshot.Playback.Device.Name}" +
              (snapshot.Playback.Device.IsRestricted ? " · nicht fernsteuerbar" : string.Empty);
        DashboardSpotifyPlayButton.Content = snapshot.Playback.IsPlaying ? "▶ LÄUFT" : "▶ PLAY";
        DashboardSpotifyPauseButton.Content = snapshot.Playback.IsPlaying ? "Ⅱ PAUSE" : "Ⅱ PAUSIERT";
        DashboardSpotifyPlayButton.IsEnabled = snapshot.Authenticated;
        DashboardSpotifyPauseButton.IsEnabled = snapshot.Authenticated && snapshot.Playback.Track is not null;
        DashboardSpotifyPreviousButton.IsEnabled = snapshot.Authenticated && snapshot.Playback.Track is not null;
        DashboardSpotifyNextButton.IsEnabled = snapshot.Authenticated && snapshot.Playback.Track is not null;

        ServicesSpotifyQueueCurrentText.Text = snapshot.Queue.CurrentlyPlaying is null
            ? "Aktuell: -"
            : $"Aktuell: {snapshot.Queue.CurrentlyPlaying.Artist} – {snapshot.Queue.CurrentlyPlaying.Name}";
        ServicesSpotifyQueueList.ItemsSource = snapshot.Queue.Upcoming
            .Select((track, index) => new SpotifyQueueItem(track, index + 1))
            .ToList();
        ServicesSpotifyPlayQueueItemButton.IsEnabled = snapshot.Queue.Upcoming.Count > 0;
        ServicesSpotifySkipCurrentButton.IsEnabled = snapshot.Playback.Track is not null;
        ServicesSpotifyQueueEmptyText.Visibility = snapshot.Queue.Upcoming.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        ServicesSpotifyHistoryList.ItemsSource = snapshot.RecentlyPlayed
            .Select(item => new SpotifyHistoryItem(item))
            .ToList();
        if (snapshot.RecentlyPlayed.Count == 0)
        {
            ServicesSpotifyHistoryStatusText.Text =
                "Noch keine zuletzt gespielten Titel verfügbar.";
        }

        ServicesSpotifySavedTracksList.ItemsSource = snapshot.SavedTracks
            .Select(track => new SpotifySavedTrackItem(track))
            .ToList();
        ServicesSpotifyPlaySavedTrackButton.IsEnabled = snapshot.SavedTracks.Count > 0;
        ServicesSpotifyRemoveSavedTrackButton.IsEnabled = snapshot.SavedTracks.Count > 0;
        ServicesSpotifyToggleCurrentSavedButton.IsEnabled = snapshot.Playback.Track is not null;
        if (snapshot.SavedTracks.Count == 0)
        {
            ServicesSpotifySavedTracksStatusText.Text =
                "Noch keine gespeicherten Titel verfügbar.";
        }

        if (!string.IsNullOrWhiteSpace(
                _settings.Spotify.PreferredDeviceId))
        {
            SpotifyDeviceBox.SelectedItem =
                snapshot.Devices.FirstOrDefault(
                    device =>
                        device.Id ==
                        _settings.Spotify.PreferredDeviceId);
            ServicesSpotifyDeviceBox.SelectedItem = SpotifyDeviceBox.SelectedItem;
        }
        else if (snapshot.Playback.Device is not null)
        {
            ServicesSpotifyDeviceBox.SelectedItem = snapshot.Devices.FirstOrDefault(
                device => device.Id == snapshot.Playback.Device.Id);
        }

        UpdateSpotifyDeviceSelectionUi();

        var spotifyErrors = _spotifyModule.LastRefreshErrors;
        if (spotifyErrors.TryGetValue("Wiedergabegeräte", out var deviceError))
        {
            ServicesSpotifyDeviceStatusText.Text = "Geräte konnten nicht geladen werden: " + deviceError;
            ServicesSpotifyDeviceStatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
        }
        else
        {
            ServicesSpotifyDeviceStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(174, 184, 191));
            if (snapshot.Devices.Count == 0)
            {
                ServicesSpotifyDeviceStatusText.Text =
                    "Kein aktives Spotify-Gerät gefunden. Spotify auf PC oder Handy öffnen und dort kurz einen Titel starten.";
            }
        }

        if (spotifyErrors.TryGetValue("Playlists", out var playlistError))
        {
            ServicesSpotifyPlaylistStatusText.Text = "Playlists konnten nicht geladen werden: " + playlistError;
            ServicesSpotifyPlaylistStatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
        }
        else
        {
            ServicesSpotifyPlaylistStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(127, 137, 145));
        }

        if (!string.IsNullOrWhiteSpace(
                _settings.Spotify.StartPlaylistUri))
        {
            SpotifyPlaylistBox.SelectedItem =
                snapshot.Playlists.FirstOrDefault(
                    playlist =>
                        playlist.Uri ==
                        _settings.Spotify.StartPlaylistUri);
            ServicesSpotifyPlaylistBox.SelectedItem = SpotifyPlaylistBox.SelectedItem;
            ServicesSpotifyStartPlaylistBox.SelectedItem = SpotifyPlaylistBox.SelectedItem;
            DashboardSpotifyPlaylistBox.SelectedItem = SpotifyPlaylistBox.SelectedItem;
        }

        var playback = snapshot.Playback;

        var progressMs = Math.Max(0, playback.ProgressMs);
        var durationMs = Math.Max(0, playback.Track?.DurationMs ?? 0);
        _updatingSpotifyUi = true;
        try
        {
            DashboardSpotifyProgressBar.Maximum = Math.Max(1, durationMs);
            DashboardSpotifyProgressBar.Value = Math.Min(progressMs, Math.Max(1, durationMs));
            DashboardSpotifyProgressBar.IsEnabled = playback.Track is not null && durationMs > 0;
        }
        finally
        {
            _updatingSpotifyUi = false;
        }
        DashboardSpotifyProgressText.Text = TimeSpan.FromMilliseconds(progressMs).ToString(@"mm\:ss");
        DashboardSpotifyDurationText.Text = TimeSpan.FromMilliseconds(durationMs).ToString(@"mm\:ss");
        DashboardSpotifyShuffleButton.Content = playback.ShuffleEnabled ? "⤨ EIN" : "⤨";
        DashboardSpotifyShuffleButton.ToolTip = playback.ShuffleEnabled
            ? "Zufallswiedergabe ist aktiv – klicken zum Ausschalten"
            : "Zufallswiedergabe ist aus – klicken zum Einschalten";
        ServicesSpotifyShuffleButton.Content = playback.ShuffleEnabled ? "Shuffle: Ein" : "Shuffle: Aus";
        ServicesSpotifyShuffleButton.ToolTip = playback.ShuffleEnabled
            ? "Zufallswiedergabe ist aktiv – klicken zum Ausschalten"
            : "Zufallswiedergabe ist aus – klicken zum Einschalten";
        DashboardSpotifyRepeatButton.Content = playback.RepeatMode?.ToLowerInvariant() switch
        {
            "context" => "↻ LISTE",
            "track" => "↻ 1",
            _ => "↻"
        };
        DashboardSpotifyRepeatButton.ToolTip = playback.RepeatMode?.ToLowerInvariant() switch
        {
            "context" => "Wiederholung der aktuellen Playlist – klicken für Titelwiederholung",
            "track" => "Wiederholung des aktuellen Titels – klicken zum Ausschalten",
            _ => "Wiederholung ist aus – klicken, um die Playlist zu wiederholen"
        };
        ServicesSpotifyRepeatButton.Content = playback.RepeatMode?.ToLowerInvariant() switch
        {
            "context" => "Wiederholung: Playlist",
            "track" => "Wiederholung: Titel",
            _ => "Wiederholung: Aus"
        };
        ServicesSpotifyRepeatButton.ToolTip = DashboardSpotifyRepeatButton.ToolTip;

        SpotifyTrackText.Text = playback.Track is null
            ? "Kein Titel"
            : playback.Track.Artist +
              " – " +
              playback.Track.Name;
        var intelligenceTrackId = playback.Track is null ? null : $"{playback.Track.Artist}|{playback.Track.Name}|{playback.Track.Album}";
        if (!string.IsNullOrWhiteSpace(intelligenceTrackId) && !string.Equals(_lastCreatorIntelligenceTrackId, intelligenceTrackId, StringComparison.Ordinal))
        {
            _lastCreatorIntelligenceTrackId = intelligenceTrackId;
            _ = _creatorIntelligence.RecordAsync("spotify.track.changed", new
            {
                artist = playback.Track!.Artist,
                title = playback.Track.Name,
                album = playback.Track.Album,
                isPlaying = playback.IsPlaying,
                viewers = _currentLiveViewerCount,
                scene = _servicesObsCurrentScene
            });
        }

        SpotifyAlbumText.Text = playback.Track is null ||
                               string.IsNullOrWhiteSpace(
                                   playback.Track.Album)
            ? "Album: -"
            : "Album: " +
              playback.Track.Album;

        SpotifyPlaybackDetailText.Text = playback.Track is null
            ? "Verbunden · Pause"
            : (playback.IsPlaying
                ? "Verbunden · Spielt"
                : "Verbunden · Pause") +
              " · Gerät: " +
              (playback.Device?.Name ?? "unbekannt");

        _updatingSpotifyUi = true;

        try
        {
            SpotifyVolumeSlider.Value =
                playback.Device?.VolumePercent
                ?? _settings.Spotify.StartVolumePercent;

            SpotifyVolumeValueText.Text =
                $"{(int)Math.Round(SpotifyVolumeSlider.Value)} %";
            DashboardSpotifyVolumeSlider.Value = playback.Device?.VolumePercent ?? _settings.Spotify.StartVolumePercent;
            ServicesSpotifyVolumeSlider.Value = DashboardSpotifyVolumeSlider.Value;
            DashboardSpotifyVolumeText.Text = $"{(int)Math.Round(DashboardSpotifyVolumeSlider.Value)} %";
            ServicesSpotifyVolumeText.Text = DashboardSpotifyVolumeText.Text;
            ServicesSpotifyProgressBar.Maximum = Math.Max(1, durationMs);
            ServicesSpotifyProgressBar.Value = Math.Clamp(progressMs, 0, Math.Max(1, durationMs));
            ServicesSpotifyProgressBar.IsEnabled = playback.Track is not null && durationMs > 0;
            ServicesSpotifyProgressText.Text = TimeSpan.FromMilliseconds(progressMs).ToString(@"mm\:ss");
            ServicesSpotifyDurationText.Text = TimeSpan.FromMilliseconds(durationMs).ToString(@"mm\:ss");
        }
        finally
        {
            _updatingSpotifyUi = false;
        }

        _ = LoadSpotifyAlbumCoverAsync(playback.Track?.AlbumImageUrl);

        if (playback.Track is not null)
            _lastStableSpotifyPlayback = playback;
        if (playback.IsPlaying)
            _lastSpotifyPlayingAt = DateTimeOffset.UtcNow;

        var overlayPlayback = StabilizeSpotifyOverlayPlayback(playback);
        _ = WriteSpotifyOverlayRuntimeDataAsync(snapshot, overlayPlayback);
        // Die OBS-Quelle bei jedem Playback-Update synchronisieren. Zuvor wurde
        // dies nur beim Import eines Overlays ausgeführt, wodurch eine einmal
        // ausgeblendete Spotify-Quelle beim nächsten Titel nicht wieder erschien.
        _ = SynchronizeSpotifyOverlayVisibilityAsync(overlayPlayback);
    }

    private SpotifyPlaybackState StabilizeSpotifyOverlayPlayback(SpotifyPlaybackState playback)
    {
        // Ein leerer Spotify-Snapshot ist während einer bestehenden Verbindung kein
        // zuverlässiger Trennstatus. Die Web API liefert bei Token-Erneuerungen,
        // Gerätewechseln und einzelnen Pollfehlern kurzfristig Track=null. Würden wir
        // diesen Zustand in die JSON schreiben, löscht die Suite Titel, Cover und
        // Fortschritt und spotify.html blendet sich sofort aus.
        //
        // Solange die Verbindung durch einen früheren erfolgreichen Snapshot gehalten
        // wird, bleibt deshalb der letzte gültige Titel aktiv. Nur DisconnectSpotifyAsync
        // setzt _spotifyOverlayConnectionLatched=false und darf die JSON leeren.
        if (playback.Track is null &&
            _spotifyOverlayConnectionLatched &&
            _lastStableSpotifyPlayback?.Track is not null)
        {
            return _lastStableSpotifyPlayback with
            {
                ProgressMs = playback.ProgressMs > 0
                    ? playback.ProgressMs
                    : _lastStableSpotifyPlayback.ProgressMs
            };
        }

        return playback;
    }

    private async Task WriteSpotifyOverlayRuntimeDataAsync(SpotifySnapshot snapshot, SpotifyPlaybackState playback)
    {
        // Auch direkte Aufrufer (Lautstärke, Anzeigeoptionen usw.) dürfen einen
        // kurzfristig leeren Snapshot nicht als vollständigen Reset in die JSON schreiben.
        playback = StabilizeSpotifyOverlayPlayback(playback);

        // Der Schalter steuert ausschließlich das Schreiben der Spotify-Laufzeitdaten.
        // Sein Zustand wird in den Einstellungen gespeichert und beim Start geladen.
        if (!_settings.Spotify.OverlayEnabled)
            return;

        await OverlayDataWriteCoordinator.Lock.WaitAsync();

        try
        {
            // Die vorhandenen DenverJohn-v18-Overlays laden ihre Spotify-Daten
            // direkt aus <OverlayRoot>\Overlay\data\overlay-data.json. Deshalb
            // schreiben wir hier bewusst direkt in diese Datei und umgehen den
            // allgemeinen OverlayDataService, dessen gespeicherter Pfad bei älteren
            // Installationen noch auf die zweite JSON im Root zeigen kann.
            var overlayRoot = OverlayRootBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(overlayRoot))
                overlayRoot = _settings.Overlay.RootPath?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(overlayRoot))
                throw new InvalidOperationException("Es ist kein Overlay-Ordner ausgewählt.");

            overlayRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(overlayRoot));

            await DisableLegacyOverlayWriterAsync(overlayRoot);

            // Der im Spotify-Bereich ausdrücklich ausgewählte JSON-Pfad hat Vorrang.
            // Hotfix 6 leitete den Zielpfad erneut nur aus dem Overlay-Root ab und
            // konnte dadurch eine andere overlay-data.json beschreiben als die von
            // der OBS-HTML geladene Datei.
            var targetPath = ResolveActiveOverlayDataPath();

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            JsonObject rootObject;
            if (File.Exists(targetPath))
            {
                try
                {
                    var existingJson = await File.ReadAllTextAsync(targetPath);
                    rootObject = JsonNode.Parse(existingJson) as JsonObject ?? new JsonObject();
                }
                catch (JsonException)
                {
                    rootObject = new JsonObject();
                }
            }
            else
            {
                rootObject = new JsonObject();
            }

            var spotify = rootObject["spotify"] as JsonObject ?? new JsonObject();
            // Der Overlay-Verbindungsstatus wird nach einer erfolgreichen Verbindung
            // bis zu einem ausdrücklichen Trennen gehalten. Kurzlebige Poll-/Token-
            // Snapshots dürfen die Anzeige nicht sekündlich auf "nicht verbunden" setzen.
            if (snapshot.Authenticated || playback.Track is not null)
                _spotifyOverlayConnectionLatched = true;

            // Nur ein ausdrücklich vom Benutzer gestarteter Disconnect darf den
            // öffentlichen Overlay-Verbindungsstatus auf false setzen. Polling,
            // leere API-Antworten und Token-Erneuerungen dürfen das niemals.
            var overlayConnected = _spotifyExplicitDisconnectInProgress
                ? false
                : _spotifyOverlayConnectionLatched || snapshot.Authenticated || playback.Track is not null;
            if (overlayConnected)
                _spotifyOverlayConnectionLatched = true;
            spotify["connected"] = overlayConnected;
            spotify["isPlaying"] = playback.IsPlaying;
            spotify["title"] = playback.Track?.Name ?? "";
            spotify["artist"] = playback.Track?.Artist ?? "";
            spotify["album"] = playback.Track?.Album ?? "";
            spotify["coverUrl"] = playback.Track?.AlbumImageUrl ?? "";
            spotify["cover"] = playback.Track?.AlbumImageUrl ?? "";
            // Die für die HTML zwingend erforderliche Sichtbarkeit wird bei jedem
            // Spotify-Schreibvorgang mitgeführt. Dadurch bleibt showInOverlay nicht
            // versehentlich fehlend/false, wenn die separate Mute-Routine wegen ihres
            // Cachewerts keinen erneuten Schreibvorgang ausführt.
            var overlayVisible = overlayConnected && _lastSpotifyOverlayMuted != true;
            spotify["showInOverlay"] = overlayVisible;
            spotify["visible"] = overlayVisible;
            spotify["showTitle"] = true;
            spotify["showArtist"] = true;
            spotify["showAlbumCover"] = true;
            spotify["showProgress"] = true;
            spotify["hideWhenPaused"] = _settings.Spotify.OverlayHideWhenPaused;
            spotify["hideWhenMuted"] = _settings.Spotify.OverlayHideWhenMuted;
            spotify["progressMs"] = Math.Max(0, playback.ProgressMs);
            spotify["durationMs"] = Math.Max(0, playback.Track?.DurationMs ?? 0);
            spotify["statusText"] = !overlayConnected
                ? "Nicht verbunden"
                : playback.IsPlaying ? "Spielt" : "Pause";

            rootObject["spotify"] = spotify;
            rootObject["updatedAt"] = DateTimeOffset.UtcNow;

            var json = rootObject.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            });

            // Direkt in dieselbe Datei schreiben. Das frühere Ersetzen über
            // eine temporäre Datei erzeugte bei einigen OBS-Browserquellen ein
            // Dateiwechsel-Ereignis, das wie kurzes Aus-/Einblenden wirkte.
            await File.WriteAllTextAsync(targetPath, json);

            ServicesSpotifyDataJsonPathBox.Text = targetPath;
            ServicesSpotifyOverlayPathText.Text = $"Aktive JSON: {targetPath}";
            ServicesSpotifyOverlayStatusText.Text =
                $"Spotify-Daten geschrieben: {DateTime.Now:HH:mm:ss} · connected={overlayConnected} · sichtbar={spotify["showInOverlay"]?.GetValue<bool?>() != false}";
            _appLogger.Write(
                AppLogLevel.Debug,
                "Spotify",
                $"Overlay-JSON aktualisiert: Pfad='{targetPath}', connected={overlayConnected}, showInOverlay={spotify["showInOverlay"]}, Titel='{playback.Track?.Name ?? ""}'.");
            ServicesSpotifyOverlayStatusText.Foreground = Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            _appLogger.Write(AppLogLevel.Error, "Spotify", $"Spotify-JSON konnte nicht aktualisiert werden: {exception.Message}", exception);
            ServicesSpotifyOverlayStatusText.Text = "Spotify-JSON konnte nicht aktualisiert werden: " + exception.Message;
            ServicesSpotifyOverlayStatusText.Foreground = Brushes.IndianRed;
        }
        finally
        {
            OverlayDataWriteCoordinator.Lock.Release();
        }
    }

    private async Task DisableLegacyOverlayWriterAsync(string overlayRoot)
    {
        if (_legacyOverlayWriterChecked)
            return;

        _legacyOverlayWriterChecked = true;

        try
        {
            var legacyRoot = Path.Combine(overlayRoot, "StreamingSuite");
            var legacyScript = Path.Combine(legacyRoot, "Start.ps1");
            if (!File.Exists(legacyScript))
                return;

            // Die alte DenverJohn-StreamingSuite schreibt periodisch in dieselbe
            // Overlay/data/overlay-data.json wie die Creator Control Suite. Ein
            // paralleler Betrieb erzeugt wechselnde connected-/Live-Zustände.
            // Beende ausschließlich Prozesse, deren Befehlszeile exakt auf dieses
            // Legacy-Skript verweist.
            var escapedScript = legacyScript.Replace("'", "''");
            var stopCommand =
                "$target='" + escapedScript + "'; " +
                "Get-CimInstance Win32_Process | Where-Object { " +
                "$_.CommandLine -and $_.CommandLine.IndexOf($target,[System.StringComparison]::OrdinalIgnoreCase) -ge 0 " +
                "} | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }";

            using (var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + stopCommand.Replace("\"", "\\\"") + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }))
            {
                if (process is not null)
                    await process.WaitForExitAsync();
            }

            // Verhindere einen versehentlichen Neustart der alten Suite. Die Dateien
            // bleiben als Sicherung erhalten und können bei Bedarf manuell
            // zurückbenannt werden.
            foreach (var fileName in new[] { "Start.bat", "Start.vbs", "Start.ps1" })
            {
                var source = Path.Combine(legacyRoot, fileName);
                if (!File.Exists(source))
                    continue;

                var disabled = source + ".disabled-by-creator-control-suite";
                if (File.Exists(disabled))
                    File.Delete(disabled);
                File.Move(source, disabled);
            }

            var markerPath = Path.Combine(legacyRoot, "LEGACY-WRITER-DISABLED.txt");
            await File.WriteAllTextAsync(markerPath,
                "Die alte DenverJohn StreamingSuite wurde deaktiviert, weil sie parallel zur Creator Control Suite in Overlay\\data\\overlay-data.json geschrieben hat.\r\n" +
                "Dadurch wechselten Spotify- und Live-Status zwischen unterschiedlichen Zuständen.\r\n" +
                "Deaktiviert am: " + DateTimeOffset.Now.ToString("O"));

            _appLogger.Write(AppLogLevel.Warning, "Overlay",
                "Alter DenverJohn-Overlay-Schreiber wurde beendet und deaktiviert: " + legacyScript);
        }
        catch (Exception exception)
        {
            _appLogger.Write(AppLogLevel.Warning, "Overlay",
                "Der alte DenverJohn-Overlay-Schreiber konnte nicht automatisch deaktiviert werden: " + exception.Message,
                exception);
        }
    }

    private string ResolveActiveOverlayDataPath()
    {
        var overlayRoot = OverlayRootBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(overlayRoot))
            overlayRoot = _settings.Overlay.RootPath?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(overlayRoot))
            throw new InvalidOperationException("Es ist kein Overlay-Ordner ausgewählt.");

        overlayRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(overlayRoot));

        // Bei DenverJohn v18.x ist der Pfad durch die HTML-Struktur eindeutig:
        // Overlay/modules/ui/*.html lädt ../../data/overlay-data.json. Eine alte
        // gespeicherte Root/data-Einstellung darf diesen Pfad nicht überstimmen.
        var denverUi = Path.Combine(overlayRoot, "Overlay", "modules", "ui");
        if (Directory.Exists(denverUi) &&
            (File.Exists(Path.Combine(denverUi, "spotify.html")) ||
             File.Exists(Path.Combine(denverUi, "live-status.html"))))
        {
            return Path.Combine(overlayRoot, "Overlay", "data", "overlay-data.json");
        }

        var configuredPath = _settings.Overlay.DataFilePath?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredPath));

        return ResolveOverlayDataPathFromRoot(overlayRoot);
    }

    private static string ResolveOverlayDataPathFromRoot(string overlayRoot)
    {
        var nestedPath = Path.Combine(overlayRoot, "Overlay", "data", "overlay-data.json");
        var rootPath = Path.Combine(overlayRoot, "data", "overlay-data.json");
        return File.Exists(nestedPath) || Directory.Exists(Path.GetDirectoryName(nestedPath)!)
            ? nestedPath
            : rootPath;
    }

    private async Task UpdateActiveOverlayJsonAsync(Action<JsonObject> update)
    {
        await OverlayDataWriteCoordinator.Lock.WaitAsync();
        try
        {
            var targetPath = ResolveActiveOverlayDataPath();
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            JsonObject root;
            if (File.Exists(targetPath))
            {
                try { root = JsonNode.Parse(await File.ReadAllTextAsync(targetPath)) as JsonObject ?? new JsonObject(); }
                catch (JsonException) { root = new JsonObject(); }
            }
            else root = new JsonObject();

            update(root);
            root["updatedAt"] = DateTimeOffset.UtcNow;
            var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            // In-place schreiben: Ein Ersetzen per File.Move trennt Hardlinks und
            // lässt verschiedene OBS-Browserquellen anschließend unterschiedliche
            // Dateiknoten lesen. Die globale Sperre verhindert zugleich verlorene
            // Read-Modify-Write-Updates zwischen Spotify, Live, Twitch und OBS.
            await using var stream = new FileStream(
                targetPath, FileMode.Create, FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete, 16 * 1024, useAsync: true);
            await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
            await writer.WriteAsync(json);
            await writer.FlushAsync();
            await stream.FlushAsync();
        }
        finally { OverlayDataWriteCoordinator.Lock.Release(); }
    }

    private async Task StartLegacyStreamAutomationAsync()
    {
        _streamStartAutomationCts?.Cancel();
        _streamStartAutomationCts?.Dispose();
        _streamStartAutomationCts = new CancellationTokenSource();
        var token = _streamStartAutomationCts.Token;
        var startScene = string.IsNullOrWhiteSpace(_settings.Obs.StartScene) ? "Start" : _settings.Obs.StartScene.Trim();
        var gameScene = string.IsNullOrWhiteSpace(_settings.Obs.LiveScene) ? "Game" : _settings.Obs.LiveScene.Trim();

        try
        {
            _streamSessionStartedAt ??= DateTimeOffset.Now;
            await _creatorIntelligence.StartSessionAsync(_streamSessionStartedAt.Value, DashboardTwitchTitleBox.Text, DashboardTwitchCategorySearchBox.Text);
            await _workflowModule.Service.ResetSessionStatsAsync(_streamSessionStartedAt);
            await RefreshTwitchFollowerCountAsync(initializeStreamBaseline: true);
            if (_obsClient.IsConnected)
            {
                await _obsClient.SetCurrentProgramSceneAsync(startScene, token);
                await _obsClient.SetSceneItemEnabledAsync(startScene, "Start_Testbild", true, token);
            }

            await UpdateActiveOverlayJsonAsync(root =>
            {
                var stream = root["stream"] as JsonObject ?? new JsonObject();
                stream["isLive"] = true;
                stream["phase"] = "Starting";
                stream["startedAt"] = _streamSessionStartedAt;
                stream["elapsedSeconds"] = 0;
                stream["currentScene"] = startScene;
                stream["startTimerSeconds"] = 600;
                root["stream"] = stream;
            });

            await Task.Delay(TimeSpan.FromMinutes(5), token);
            if (_obsClient.IsConnected)
                await _obsClient.SetSceneItemEnabledAsync(startScene, "Start_Testbild", false, token);

            await Task.Delay(TimeSpan.FromMinutes(5), token);
            if (_obsClient.IsConnected)
                await _obsClient.SetCurrentProgramSceneAsync(gameScene, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _appLogger.Write(AppLogLevel.Warning, "Automation", "10-Minuten-Streamstart-Automation fehlgeschlagen: " + ex.Message, ex);
        }
    }

    private int? ResolveEffectiveSpotifyVolume(SpotifyPlaybackState playback)
    {
        // Direkt nach einer Änderung in der Suite hat der angeforderte Wert kurz Vorrang,
        // weil Spotify die neue Gerätelautstärke häufig erst mit Verzögerung zurückmeldet.
        // Danach ist wieder der tatsächlich von Spotify gemeldete Wert maßgeblich, damit
        // auch ein Mute in der Spotify-App oder auf einem anderen Gerät erkannt wird.
        if (_lastRequestedSpotifyVolumePercent.HasValue &&
            _lastRequestedSpotifyVolumeAt.HasValue &&
            DateTimeOffset.UtcNow - _lastRequestedSpotifyVolumeAt.Value < TimeSpan.FromSeconds(4))
        {
            return _lastRequestedSpotifyVolumePercent.Value;
        }

        var reportedVolume = playback.Device?.VolumePercent;
        if (reportedVolume.HasValue)
        {
            _lastRequestedSpotifyVolumePercent = reportedVolume.Value;
            _lastRequestedSpotifyVolumeAt = null;
        }

        return reportedVolume;
    }

    private async Task SetSpotifyVolumeTrackedAsync(int volume, CancellationToken cancellationToken = default)
    {
        volume = Math.Clamp(volume, 0, 100);
        _lastRequestedSpotifyVolumePercent = volume;
        _lastRequestedSpotifyVolumeAt = DateTimeOffset.UtcNow;
        await _spotifyModule.SetVolumeAsync(volume, cancellationToken);
        await ApplySpotifyOverlayMuteStateAsync(volume <= 0);
    }

    private async Task SynchronizeSpotifyOverlayVisibilityAsync(SpotifyPlaybackState playback)
    {
        // Mehrere Spotify-Polls dürfen die Sichtbarkeit nicht parallel und in
        // unterschiedlicher Reihenfolge anwenden. Das war die Hauptursache für
        // das wiederholte Ein-/Ausblenden der Browserquelle.
        await _spotifyOverlayVisibilityLock.WaitAsync();
        try
        {
            if (!_settings.Spotify.OverlayHideWhenMuted && !_settings.Spotify.OverlayHideWhenPaused)
            {
                _lastSpotifyOverlayMuted = null;
                await ApplySpotifyOverlayMuteStateAsync(false);
                return;
            }

            var hideBecausePaused = _settings.Spotify.OverlayHideWhenPaused &&
                                    !playback.IsPlaying &&
                                    DateTimeOffset.UtcNow - _lastSpotifyPlayingAt >= TimeSpan.FromSeconds(3);
            var hideBecauseVolume = false;
            var hideBecauseObsMute = false;

            if (_settings.Spotify.OverlayHideWhenMuted && _settings.Spotify.OverlayMuteDetectionSpotifyVolume)
            {
                var volumePercent = ResolveEffectiveSpotifyVolume(playback);
                hideBecauseVolume = volumePercent.HasValue && volumePercent.Value <= 0;
            }

            if (_settings.Spotify.OverlayHideWhenMuted && _settings.Spotify.OverlayMuteDetectionObsSource)
            {
                if (_obsClient.IsConnected)
                {
                    var audioSource = _settings.Spotify.OverlayObsAudioSource?.Trim();
                    if (!string.IsNullOrWhiteSpace(audioSource))
                    {
                        try
                        {
                            var audioState = await _obsClient.GetInputAudioStateAsync(audioSource);
                            _lastKnownSpotifyObsMute = audioState.Muted;
                        }
                        catch (Exception exception)
                        {
                            // Bei einem kurzen OBS-Abfragefehler den zuletzt sicher
                            // bekannten Zustand behalten. Früher wurde hier implizit
                            // "nicht gemutet" angenommen und das Overlay kurz eingeblendet.
                            _appLogger.Write(AppLogLevel.Debug, "Spotify", $"OBS-Mute-Status für '{audioSource}' konnte nicht gelesen werden: {exception.Message}");
                        }
                    }
                }

                hideBecauseObsMute = _lastKnownSpotifyObsMute == true;
            }

            await ApplySpotifyOverlayMuteStateAsync(hideBecausePaused || hideBecauseVolume || hideBecauseObsMute);
        }
        finally
        {
            _spotifyOverlayVisibilityLock.Release();
        }
    }

    private async Task ApplySpotifyOverlayMuteStateAsync(bool isMuted)
    {
        if (!_settings.Spotify.OverlayHideWhenMuted && !_settings.Spotify.OverlayHideWhenPaused)
            isMuted = false;

        if (_lastSpotifyOverlayMuted == isMuted)
            return;

        // Die JSON-Sichtbarkeit wird unabhängig von OBS aktualisiert. Damit
        // funktioniert das Ausblenden auch bei Overlays, die nur das Feld
        // spotify.showInOverlay bzw. spotify.visible auswerten.
        try
        {
            await UpdateActiveOverlayJsonAsync(root =>
            {
                var spotify = root["spotify"] as JsonObject ?? new JsonObject();
                spotify["hideWhenMuted"] = _settings.Spotify.OverlayHideWhenMuted;
                spotify["hideWhenPaused"] = _settings.Spotify.OverlayHideWhenPaused;
                spotify["muteDetectionObsSource"] = _settings.Spotify.OverlayMuteDetectionObsSource;
                spotify["muteDetectionSpotifyVolume"] = _settings.Spotify.OverlayMuteDetectionSpotifyVolume;
                spotify["obsAudioSource"] = _settings.Spotify.OverlayObsAudioSource;
                spotify["showInOverlay"] = !isMuted;
                spotify["visible"] = !isMuted;
                root["spotify"] = spotify;
            });
        }
        catch (Exception exception)
        {
            _appLogger.Write(AppLogLevel.Warning, "Spotify", "Spotify-Mute-Status konnte nicht in die Overlay-JSON geschrieben werden.", exception);
        }

        // Ist eine OBS-Szene und -Quelle hinterlegt, wird zusätzlich die Quelle
        // geschaltet. Fehlt OBS, bleibt wenigstens die JSON-Steuerung wirksam.
        if (_obsClient.IsConnected)
        {
            var sceneName = _settings.Spotify.OverlayObsScene?.Trim();
            var sourceName = _settings.Spotify.OverlayObsSource?.Trim();
            if (!string.IsNullOrWhiteSpace(sceneName) && !string.IsNullOrWhiteSpace(sourceName))
            {
                try
                {
                    await _obsClient.SetSceneItemEnabledAsync(sceneName, sourceName, !isMuted);
                }
                catch (Exception exception)
                {
                    _appLogger.Write(AppLogLevel.Warning, "Spotify", $"Spotify-Overlay-Sichtbarkeit konnte nicht geändert werden: {exception.Message}", exception);
                }
            }
        }

        _lastSpotifyOverlayMuted = isMuted;
        _appLogger.Write(AppLogLevel.Information, "Spotify",
            isMuted ? "Spotify-Overlay wegen Mute/Pause ausgeblendet." : "Spotify-Overlay wieder eingeblendet.");
    }

    private async Task HandleStartToGameSpotifyVolumeAsync(string? currentScene)
    {
        var previousScene = _lastObservedObsProgramScene;
        _lastObservedObsProgramScene = currentScene;

        if (string.IsNullOrWhiteSpace(previousScene) || string.IsNullOrWhiteSpace(currentScene)) return;

        var startScene = string.IsNullOrWhiteSpace(_settings.Obs.StartScene) ? "Start" : _settings.Obs.StartScene.Trim();
        var gameScene = string.IsNullOrWhiteSpace(_settings.Obs.LiveScene) ? "Game" : _settings.Obs.LiveScene.Trim();

        if (!string.Equals(previousScene, startScene, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(currentScene, gameScene, StringComparison.OrdinalIgnoreCase) ||
            _spotifyStartToGameVolumeChangeRunning ||
            !_spotifyModule.GetSnapshot().Authenticated)
        {
            return;
        }

        _spotifyStartToGameVolumeChangeRunning = true;
        var wasPlayingBeforeTransition = _spotifyModule.GetSnapshot().Playback.IsPlaying;
        try
        {
            if (!_settings.Spotify.SetVolumeOnLiveTransition)
            {
                return;
            }

            var liveVolume = Math.Clamp(_settings.Spotify.LiveVolumePercent, 0, 100);
            await _spotifyModule.SetVolumeAsync(liveVolume);
            if (wasPlayingBeforeTransition && !_spotifyModule.GetSnapshot().Playback.IsPlaying)
            {
                await _spotifyModule.ResumeAsync();
            }
            _appLogger.Write(
                AppLogLevel.Information,
                "Spotify",
                $"Szenenwechsel {startScene} → {gameScene}: Spotify-Lautstärke auf {liveVolume} % gesetzt.");
            AddDashboardNotification($"Start → Game: Spotify auf {liveVolume} % gesetzt.", "Spotify");
        }
        catch (Exception exception)
        {
            _appLogger.Write(
                AppLogLevel.Warning,
                "Spotify",
                $"Spotify-Lautstärke beim Wechsel {startScene} → {gameScene} konnte nicht gesetzt werden: {exception.Message}",
                exception);
        }
        finally
        {
            _spotifyStartToGameVolumeChangeRunning = false;
        }
    }

    private async Task AuthorizeTwitchAsync()
    {
        try
        {
            await SaveSettingsAsync();

            TwitchConnectionStatusText.Text =
                "Gerätecode wird angefordert ...";

            var deviceCode =
                await _twitchModule.StartAuthorizationAsync();

            Clipboard.SetText(deviceCode.UserCode);

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = deviceCode.VerificationUri,
                    UseShellExecute = true
                });

            var result = MessageBox.Show(
                "Twitch wurde im Browser geöffnet.\n\n" +
                "Code: " + deviceCode.UserCode + "\n\n" +
                "Der Code wurde in die Zwischenablage kopiert.\n" +
                "Nach der Bestätigung auf Twitch hier auf OK klicken. " +
                "Die Suite wartet danach automatisch auf den Token.",
                "Twitch autorisieren",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.OK)
            {
                TwitchConnectionStatusText.Text =
                    "Autorisierung abgebrochen.";

                return;
            }

            var progress = new Progress<string>(
                text => TwitchConnectionStatusText.Text = text);

            await _twitchModule.CompleteAuthorizationAsync(
                deviceCode,
                progress);

            RefreshTwitchUi();
        }
        catch (Exception exception)
        {
            TwitchConnectionStatusText.Text = exception.Message;
            TwitchConnectionStatusText.Foreground =
                System.Windows.Media.Brushes.IndianRed;

            MessageBox.Show(
                exception.Message,
                "Twitch-Autorisierung fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ConnectTwitchAsync(
        bool showErrorDialog = true)
    {
        try
        {
            TwitchConnectionStatusText.Text =
                "Twitch wird verbunden ...";

            await _twitchModule.ConnectAsync(CancellationToken.None);

            RefreshTwitchUi();
            await RefreshTwitchUsersAsync();
            await RefreshLiveViewerSampleAsync();
            await RefreshTwitchFollowerCountAsync();
        }
        catch (Exception exception)
        {
            TwitchConnectionStatusText.Text = exception.Message;
            TwitchConnectionStatusText.Foreground =
                System.Windows.Media.Brushes.IndianRed;

            if (showErrorDialog)
            {
                MessageBox.Show(
                    exception.Message,
                    "Twitch-Verbindung fehlgeschlagen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private async Task DisconnectTwitchAsync()
    {
        await _twitchModule.DisconnectAsync(CancellationToken.None);

        TwitchDashboardStatus.Text = "NICHT VERBUNDEN";
        TwitchConnectionStatusText.Text =
            "Nicht verbunden";
        TwitchConnectionStatusText.Foreground =
            System.Windows.Media.Brushes.Gray;
    
        RefreshDashboardServiceActionButtons();
}

    private async Task SearchTwitchCategoriesAsync(System.Windows.Controls.TextBox searchBox, System.Windows.Controls.ComboBox resultsBox)
    {
        try
        {
            var query = searchBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query)) return;
            resultsBox.ItemsSource = await _twitchModule.SearchCategoriesAsync(query);
            resultsBox.IsDropDownOpen = true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Kategoriesuche fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task SaveTwitchChannelAsync(System.Windows.Controls.TextBox titleBox, System.Windows.Controls.ComboBox categoryBox)
    {
        try
        {
            var category = categoryBox.SelectedItem as TwitchCategory;
            await _twitchModule.UpdateChannelAsync(titleBox.Text.Trim(), category?.Id);
            RefreshTwitchUi();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Twitch-Kanal konnte nicht aktualisiert werden", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task SearchTwitchCategoriesAsync()
    {
        try
        {
            var query =
                TwitchCategorySearchBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(query))
            {
                return;
            }

            TwitchCategoryResultsBox.ItemsSource =
                await _twitchModule.SearchCategoriesAsync(query);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Kategoriesuche fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task SaveTwitchChannelAsync()
    {
        try
        {
            var category =
                TwitchCategoryResultsBox.SelectedItem
                    as TwitchCategory;

            await _twitchModule.UpdateChannelAsync(
                TwitchTitleBox.Text.Trim(),
                category?.Id);

            RefreshTwitchUi();

            MessageBox.Show(
                "Streamtitel und Kategorie wurden gespeichert.",
                "Twitch",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Twitch-Kanal konnte nicht aktualisiert werden",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task RunStartupStepSafelyAsync(string stepName, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            _appLogger.Write(
                AppLogLevel.Error,
                "Startup",
                $"Startschritt '{stepName}' ist fehlgeschlagen. Die Suite wird im eingeschränkten Modus fortgesetzt.",
                exception);
        }
    }

    private void SelectDashboardStatisticInUi()
    {
        var metric = string.IsNullOrWhiteSpace(_settings.Dashboard.DashboardStatistic)
            ? "ViewerCount"
            : _settings.Dashboard.DashboardStatistic;
        foreach (var entry in StatisticsDashboardMetricBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(entry.Tag as string, metric, StringComparison.OrdinalIgnoreCase))
            {
                StatisticsDashboardMetricBox.SelectedItem = entry;
                break;
            }
        }
        UpdateDashboardSelectedStatistic();
    }

    private void UpdateDashboardSelectedStatistic()
    {
        var stats = _workflowModule.Service.SessionStats;
        var metric = _settings.Dashboard.DashboardStatistic ?? "ViewerCount";
        (DashboardSelectedStatisticLabel.Text, DashboardSelectedStatisticValue.Text) = metric switch
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
        if (!isLive) return;
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

    private static void CopySelectedModerationUser(ListBox list, TextBox target)
    {
        if (list.SelectedItem is not null)
        {
            target.Text = list.SelectedItem.ToString()?.TrimStart('@') ?? string.Empty;
        }
    }

    private async Task ModerateTwitchUserAsync(string userName, bool ban, string? durationMinutesText, string? reason)
    {
        var cleanName = (userName ?? string.Empty).Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            MessageBox.Show("Bitte zuerst einen Twitch-User auswählen oder eingeben.", "Twitch-Moderation", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int? durationSeconds = null;
        if (!ban)
        {
            if (!int.TryParse(durationMinutesText, out var minutes) || minutes < 1)
            {
                MessageBox.Show("Bitte eine Timeout-Dauer von mindestens einer Minute eingeben.", "Twitch-Moderation", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            durationSeconds = Math.Clamp(minutes * 60, 1, 1_209_600);
        }

        try
        {
            await _twitchModule.ModerateUserAsync(cleanName, durationSeconds, reason);
            var resultText = ban
                ? $"{cleanName} wurde gebannt."
                : $"{cleanName} erhielt einen Timeout von {durationSeconds / 60} Minuten.";
            AddDashboardNotification(resultText, "Info");
            await AddTwitchModerationLogAsync(ban ? "BAN" : "TIMEOUT", cleanName, reason, resultText);
        }
        catch (Exception exception)
        {
            await AddTwitchModerationLogAsync(ban ? "BAN FEHLER" : "TIMEOUT FEHLER", cleanName, reason, exception.Message);
            MessageBox.Show(exception.Message, "Twitch-Moderation fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task UnbanTwitchUserAsync(string userName)
    {
        var cleanName = (userName ?? string.Empty).Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            MessageBox.Show("Bitte zuerst einen Twitch-User auswählen oder eingeben.", "Twitch-Moderation", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            await _twitchModule.UnbanUserAsync(cleanName);
            var resultText = $"Ban oder Timeout für {cleanName} wurde aufgehoben.";
            AddDashboardNotification(resultText, "Info");
            await AddTwitchModerationLogAsync("AUFHEBEN", cleanName, null, resultText);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Twitch-Moderation fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string GetTwitchModerationLogPath()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CreatorControlSuite", "Logs");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "twitch-moderation.log");
    }

    private async Task AddTwitchModerationLogAsync(string action, string userName, string? reason, string result)
    {
        var line = $"{DateTimeOffset.Now:dd.MM.yyyy HH:mm:ss} · {action} · @{userName}" +
                   (string.IsNullOrWhiteSpace(reason) ? string.Empty : $" · Grund: {reason.Trim()}") +
                   $" · {result}";
        _twitchModerationLogItems.Insert(0, line);
        while (_twitchModerationLogItems.Count > 100) _twitchModerationLogItems.RemoveAt(_twitchModerationLogItems.Count - 1);
        await File.AppendAllTextAsync(GetTwitchModerationLogPath(), line + Environment.NewLine, new System.Text.UTF8Encoding(true));
    }

    private async Task ExportTwitchModerationLogAsync()
    {
        var source = GetTwitchModerationLogPath();
        if (!File.Exists(source))
        {
            MessageBox.Show("Es sind noch keine Moderationsaktionen gespeichert.", "Twitch-Moderation", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var target = Path.Combine(Path.GetDirectoryName(source)!, $"twitch-moderation-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        await Task.Run(() => File.Copy(source, target, true));
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    private async Task SendTwitchChatAsync()
    {
        var message = TwitchChatMessageBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            await _twitchModule.SendChatMessageAsync(message);

            TwitchChatMessageBox.Clear();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Chatnachricht konnte nicht gesendet werden",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ScrollTwitchChatToLatest()
    {
        if (_twitchChatItems.Count == 0)
        {
            return;
        }

        var latest = _twitchChatItems[^1];
        TwitchChatList.ScrollIntoView(latest);
        DashboardTwitchChatList.ScrollIntoView(latest);
        ServicesTwitchChatList.ScrollIntoView(latest);
    }

    private async Task LoadTwitchProfessionalHistoryAsync()
    {
        _twitchProfessionalHistoryItems.Clear();
        var path = GetStreamHistoryFilePath();
        if (!File.Exists(path))
        {
            ServicesTwitchProfessionalTotalStreamsText.Text = "0";
            ServicesTwitchProfessionalRecordPeakText.Text = "0";
            ServicesTwitchProfessionalRecordAverageText.Text = "0,0";
            ServicesTwitchProfessionalTotalDurationText.Text = "00:00";
            ServicesTwitchProfessionalTotalFollowersText.Text = "0";
            ServicesTwitchProfessionalPeakTrendText.Text = "-";
            ServicesTwitchProfessionalAverageTrendText.Text = "-";
            ServicesTwitchProfessionalChatRateText.Text = "0";
            ServicesTwitchProfessionalBestCategoryText.Text = "-";
            ServicesTwitchProfessionalEngagementRateText.Text = "0";
            ServicesTwitchProfessionalFollowerRateText.Text = "0";
            ServicesTwitchProfessionalConsistencyText.Text = "-";
            ServicesTwitchProfessionalSummaryText.Text = "Noch keine Trenddaten verfügbar.";
            _twitchProfessionalHistoryItems.Add("Noch keine abgeschlossenen Streams gespeichert.");
            return;
        }

        var rows = new List<(DateTimeOffset StartedAt, long DurationSeconds, int Peak, double Average, int Followers, int Chat, int Events, string Category, string Title)>();
        foreach (var line in await File.ReadAllLinesAsync(path))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                rows.Add((
                    root.GetProperty("StartedAt").GetDateTimeOffset(),
                    root.TryGetProperty("DurationSeconds", out var duration) ? duration.GetInt64() : 0,
                    root.TryGetProperty("PeakViewers", out var peak) ? peak.GetInt32() : 0,
                    root.TryGetProperty("AverageViewers", out var average) ? average.GetDouble() : 0,
                    root.TryGetProperty("FollowersGained", out var followers) ? followers.GetInt32() : 0,
                    root.TryGetProperty("ChatMessages", out var chat) ? chat.GetInt32() : 0,
                    root.TryGetProperty("AlertsPlayed", out var eventsCount) ? eventsCount.GetInt32() : 0,
                    root.TryGetProperty("Category", out var category) ? category.GetString() ?? "-" : "-",
                    root.TryGetProperty("Title", out var title) ? title.GetString() ?? "-" : "-"));
            }
            catch
            {
                // Ungültige oder ältere Zeilen werden übersprungen.
            }
        }

        ServicesTwitchProfessionalTotalStreamsText.Text = rows.Count.ToString();
        ServicesTwitchProfessionalRecordPeakText.Text = rows.Count == 0 ? "0" : rows.Max(x => x.Peak).ToString();
        ServicesTwitchProfessionalRecordAverageText.Text = rows.Count == 0 ? "0,0" : rows.Max(x => x.Average).ToString("0.0");
        ServicesTwitchProfessionalTotalDurationText.Text = FormatStatisticsDuration(rows.Sum(x => x.DurationSeconds));
        ServicesTwitchProfessionalTotalFollowersText.Text = rows.Sum(x => x.Followers).ToString();

        var recent = rows.OrderBy(x => x.StartedAt).TakeLast(10).ToList();
        if (recent.Count >= 2)
        {
            var split = Math.Max(1, recent.Count / 2);
            var earlier = recent.Take(split).Average(x => x.Average);
            var later = recent.Skip(split).Average(x => x.Average);
            var delta = later - earlier;
            ServicesTwitchProfessionalViewerTrendText.Text = $"Zuschauertrend: {(delta >= 0 ? "+" : string.Empty)}{delta:0.0} Ø Zuschauer";
            ServicesTwitchProfessionalFollowerTrendText.Text = $"Followertrend: {recent.Average(x => x.Followers):0.0} pro Stream";
        }
        else
        {
            ServicesTwitchProfessionalViewerTrendText.Text = "Zuschauertrend: Noch nicht genügend Daten";
            ServicesTwitchProfessionalFollowerTrendText.Text = "Followertrend: Noch nicht genügend Daten";
        }
        ServicesTwitchProfessionalCategoryTrendText.Text = "Häufigste Kategorie: " + (rows.Where(x => !string.IsNullOrWhiteSpace(x.Category) && x.Category != "-").GroupBy(x => x.Category).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key ?? "-");
        ServicesTwitchProfessionalDurationTrendText.Text = "Ø Streamdauer: " + FormatStatisticsDuration(rows.Count == 0 ? 0 : (long)rows.Average(x => x.DurationSeconds));

        var ordered = rows.OrderByDescending(x => x.StartedAt).ToList();
        var latestFive = ordered.Take(5).ToList();
        var previousFive = ordered.Skip(5).Take(5).ToList();
        static string PercentTrend(double current, double previous) => previous <= 0 ? "-" : $"{((current - previous) / previous) * 100:+0.0;-0.0;0.0}%";
        var latestPeak = latestFive.Count == 0 ? 0 : latestFive.Average(x => x.Peak);
        var previousPeak = previousFive.Count == 0 ? 0 : previousFive.Average(x => x.Peak);
        var latestAverage = latestFive.Count == 0 ? 0 : latestFive.Average(x => x.Average);
        var previousAverage = previousFive.Count == 0 ? 0 : previousFive.Average(x => x.Average);
        ServicesTwitchProfessionalPeakTrendText.Text = PercentTrend(latestPeak, previousPeak);
        ServicesTwitchProfessionalAverageTrendText.Text = PercentTrend(latestAverage, previousAverage);
        var totalHours = rows.Sum(x => x.DurationSeconds) / 3600d;
        ServicesTwitchProfessionalChatRateText.Text = totalHours <= 0 ? "0" : (rows.Sum(x => x.Chat) / totalHours).ToString("0.0");
        var bestCategory = rows.Where(x => !string.IsNullOrWhiteSpace(x.Category) && x.Category != "-")
            .GroupBy(x => x.Category).Select(g => new { Name = g.Key, Average = g.Average(x => x.Average) })
            .OrderByDescending(x => x.Average).FirstOrDefault();
        ServicesTwitchProfessionalBestCategoryText.Text = bestCategory?.Name ?? "-";
        var totalEngagement = rows.Sum(x => x.Chat + x.Events);
        ServicesTwitchProfessionalEngagementRateText.Text = totalHours <= 0 ? "0" : (totalEngagement / totalHours).ToString("0.0");
        ServicesTwitchProfessionalFollowerRateText.Text = totalHours <= 0 ? "0" : (rows.Sum(x => x.Followers) / totalHours).ToString("0.00");
        var recentAverages = latestFive.Select(x => x.Average).ToList();
        if (recentAverages.Count < 2 || recentAverages.Average() <= 0)
        {
            ServicesTwitchProfessionalConsistencyText.Text = "-";
        }
        else
        {
            var mean = recentAverages.Average();
            var variance = recentAverages.Sum(value => Math.Pow(value - mean, 2)) / recentAverages.Count;
            var coefficient = Math.Sqrt(variance) / mean;
            ServicesTwitchProfessionalConsistencyText.Text = coefficient switch
            {
                <= 0.15 => "Sehr stabil",
                <= 0.30 => "Stabil",
                <= 0.50 => "Schwankend",
                _ => "Stark schwankend"
            };
        }
        ServicesTwitchProfessionalSummaryText.Text = rows.Count == 0 ? "Noch keine Trenddaten verfügbar." :
            $"Letzte {latestFive.Count} Streams: Ø {latestAverage:0.0} Zuschauer, mittlerer Peak {latestPeak:0.0}. Insgesamt {rows.Sum(x => x.Chat)} Chatnachrichten und {rows.Sum(x => x.Followers)} neue Follower.";

        foreach (var row in ordered.Take(20))
        {
            var local = row.StartedAt.ToLocalTime();
            var duration = TimeSpan.FromSeconds(Math.Max(0, row.DurationSeconds));
            _twitchProfessionalHistoryItems.Add(
                $"{local:dd.MM.yyyy HH:mm} · {duration:hh\\:mm\\:ss} · Peak {row.Peak} · Ø {row.Average:0.0} · +{row.Followers} Follower · {row.Category}");
        }

        if (_twitchProfessionalHistoryItems.Count == 0)
        {
            _twitchProfessionalHistoryItems.Add("Noch keine gültigen Stream-Sessions vorhanden.");
        }
    }

    private void RefreshTwitchProfessionalUi(TwitchRaidTargetStatus? liveStatus = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => RefreshTwitchProfessionalUi(liveStatus));
            return;
        }

        var snapshot = _twitchModule.GetSnapshot();
        var stats = _workflowModule.Service.SessionStats;
        var live = liveStatus?.IsOnline ?? _lastObsStreamActive;
        var startedAt = liveStatus?.StartedAt ?? _streamSessionStartedAt ?? _twitchSessionObservedAt;
        var duration = startedAt.HasValue
            ? DateTimeOffset.Now - startedAt.Value
            : TimeSpan.Zero;

        ServicesTwitchProfessionalLiveText.Text = live ? "LIVE" : "OFFLINE";
        ServicesTwitchProfessionalLiveText.Foreground = live
            ? System.Windows.Media.Brushes.LightGreen
            : System.Windows.Media.Brushes.Gray;
        ServicesTwitchProfessionalViewerText.Text = _currentLiveViewerCount.ToString();
        ServicesTwitchProfessionalPeakText.Text = stats.PeakViewers.ToString();
        ServicesTwitchProfessionalAverageText.Text = stats.AverageViewers.ToString("0.0");
        ServicesTwitchProfessionalDurationText.Text = duration.ToString(@"hh\:mm\:ss");
        ServicesTwitchProfessionalChatText.Text = _twitchSessionChatMessages.ToString();
        ServicesTwitchProfessionalUniqueChattersText.Text = _twitchSessionUniqueChatters.Count.ToString();
        ServicesTwitchProfessionalEventsText.Text = _twitchSessionEvents.ToString();
        ServicesTwitchProfessionalFollowersText.Text = stats.FollowersGained.ToString();
        ServicesTwitchProfessionalCategoryText.Text = string.IsNullOrWhiteSpace(liveStatus?.GameName)
            ? (string.IsNullOrWhiteSpace(snapshot.CategoryName) ? "-" : snapshot.CategoryName)
            : liveStatus.GameName;
        ServicesTwitchProfessionalTitleText.Text = string.IsNullOrWhiteSpace(liveStatus?.StreamTitle)
            ? (string.IsNullOrWhiteSpace(snapshot.ChannelTitle) ? "-" : snapshot.ChannelTitle)
            : liveStatus.StreamTitle;
    }

    private void RefreshTwitchUi()
    {
        var snapshot = _twitchModule.GetSnapshot();

        TwitchDashboardStatus.Text = snapshot.Authenticated
            ? "VERBUNDEN"
            : "NICHT VERBUNDEN";

        TwitchDashboardLamp.Fill = snapshot.Authenticated
            ? System.Windows.Media.Brushes.LimeGreen
            : System.Windows.Media.Brushes.IndianRed;

        TwitchConnectionStatusText.Text = snapshot.Authenticated
            ? $"Verbunden als {snapshot.Login} · " +
              $"EventSub: {(snapshot.EventSubConnected ? "aktiv" : "offline")}"
            : "Nicht verbunden";

        TwitchConnectionStatusText.Foreground = snapshot.Authenticated
            ? System.Windows.Media.Brushes.LightGreen
            : System.Windows.Media.Brushes.Gray;
        ServicesTwitchStatusText.Text = TwitchConnectionStatusText.Text;
        ServicesTwitchStatusText.Foreground = TwitchConnectionStatusText.Foreground;

        TwitchTitleBox.Text = snapshot.ChannelTitle;
        TwitchCategorySearchBox.Text = snapshot.CategoryName;
        DashboardTwitchTitleBox.Text = snapshot.ChannelTitle;
        DashboardTwitchCategorySearchBox.Text = snapshot.CategoryName;
        ServicesTwitchTitleBox.Text = snapshot.ChannelTitle;
        ServicesTwitchCategorySearchBox.Text = snapshot.CategoryName;
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
                        badge,
                        "moderator",
                        StringComparison.OrdinalIgnoreCase)))
        {
            return "[MOD] ";
        }

        if (message.Badges.Any(
                badge =>
                    string.Equals(
                        badge,
                        "vip",
                        StringComparison.OrdinalIgnoreCase)))
        {
            return "[VIP] ";
        }

        if (message.Badges.Any(
                badge =>
                    string.Equals(
                        badge,
                        "subscriber",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        badge,
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
        var userId = string.IsNullOrWhiteSpace(message.ChatterUserId)
            ? message.ChatterLogin
            : message.ChatterUserId;
        var userName = string.IsNullOrWhiteSpace(message.ChatterName)
            ? message.ChatterLogin
            : message.ChatterName;

        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(userName))
        {
            return;
        }

        var display = role + userName;

        if (_twitchUserDisplayById.TryGetValue(userId, out var previous))
        {
            var index = _twitchUserItems.IndexOf(previous);

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
        foreach (var prefix in new[]
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
            foreach (var key in keys)
            {
                if (data.TryGetValue(key, out var value) &&
                    int.TryParse(value, out var parsed))
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

    private async Task ExecuteDashboardActionAsync(
        Button button,
        string actionName,
        Func<Task> action,
        bool refreshDashboard = true)
    {
        if (!button.IsEnabled)
        {
            return;
        }

        var originalContent = button.Content;
        button.IsEnabled = false;

        try
        {
            if (originalContent is string text &&
                !string.IsNullOrWhiteSpace(text))
            {
                button.Content = text + " …";
            }

            await action();

            if (refreshDashboard)
            {
                await RefreshDashboardLiveDataAsync();
            }
        }
        catch (Exception ex)
        {
            AddDashboardNotification(
                $"{actionName} fehlgeschlagen: {ex.Message}",
                "Fehler");
        }
        finally
        {
            button.Content = originalContent;
            button.IsEnabled = true;
            RefreshDashboardServiceActionButtons();
        }
    }

    private async Task ToggleObsFromDashboardAsync()
    {
        if (_obsClient.IsConnected)
        {
            await DisconnectObsAsync();
            RefreshDashboardServiceActionButtons();
            AddDashboardNotification("OBS wurde getrennt.", "Info");
            return;
        }

        await ConnectObsFromDashboardAsync();
        RefreshDashboardServiceActionButtons();
    }

    private async Task ToggleTwitchFromDashboardAsync()
    {
        if (_twitchModule.GetSnapshot().Authenticated)
        {
            await DisconnectTwitchAsync();
            RefreshTwitchUi();
            RefreshDashboardServiceActionButtons();
            AddDashboardNotification("Twitch wurde getrennt.", "Info");
            return;
        }

        await ConnectTwitchFromDashboardAsync();
        RefreshDashboardServiceActionButtons();
    }

    private async Task ToggleSpotifyFromDashboardAsync()
    {
        if (_spotifyModule.GetSnapshot().Authenticated)
        {
            await DisconnectSpotifyAsync();
            RefreshSpotifyUi();
            RefreshDashboardServiceActionButtons();
            AddDashboardNotification("Spotify wurde getrennt.", "Info");
            return;
        }

        await ConnectSpotifyFromDashboardAsync();
        RefreshDashboardServiceActionButtons();
    }

    private async Task ToggleStreamerBotFromDashboardAsync()
    {
        var connected =
            _streamerBotSocket?.State ==
            System.Net.WebSockets.WebSocketState.Open;

        if (connected)
        {
            await DisconnectStreamerBotAsync();
            RefreshDashboardServiceActionButtons();
            AddDashboardNotification("Streamer.bot wurde getrennt.", "Info");
            return;
        }

        await ConnectStreamerBotFromDashboardAsync();
        RefreshDashboardServiceActionButtons();
    }

    private void RefreshDashboardServiceActionButtons()
    {
        DashboardServiceConnectObsButton.Content =
            _obsClient.IsConnected ? "TRENNEN" : "VERBINDEN";

        DashboardServiceConnectTwitchButton.Content =
            _twitchModule.GetSnapshot().Authenticated
                ? "TRENNEN"
                : "VERBINDEN";

        DashboardServiceConnectSpotifyButton.Content =
            _spotifyModule.GetSnapshot().Authenticated
                ? "TRENNEN"
                : "VERBINDEN";

        DashboardServiceConnectStreamerBotButton.Content =
            _streamerBotSocket?.State ==
            System.Net.WebSockets.WebSocketState.Open
                ? "TRENNEN"
                : "VERBINDEN";

        DashboardTopConnectObsButton.Content = DashboardServiceConnectObsButton.Content;
        DashboardTopConnectTwitchButton.Content = DashboardServiceConnectTwitchButton.Content;
        DashboardTopConnectSpotifyButton.Content = DashboardServiceConnectSpotifyButton.Content;
        DashboardTopConnectStreamerBotButton.Content = DashboardServiceConnectStreamerBotButton.Content;

        DashboardServiceConnectObsButton.ToolTip =
            _obsClient.IsConnected
                ? "OBS-Verbindung trennen"
                : "OBS verbinden";

        DashboardServiceConnectTwitchButton.ToolTip =
            _twitchModule.GetSnapshot().Authenticated
                ? "Twitch-Verbindung trennen"
                : "Twitch verbinden";

        DashboardServiceConnectSpotifyButton.ToolTip =
            _spotifyModule.GetSnapshot().Authenticated
                ? "Spotify-Verbindung trennen"
                : "Spotify verbinden";

        DashboardServiceConnectStreamerBotButton.ToolTip =
            _streamerBotSocket?.State ==
            System.Net.WebSockets.WebSocketState.Open
                ? "Streamer.bot-Verbindung trennen"
                : "Streamer.bot verbinden";

        DashboardTopConnectObsButton.ToolTip = DashboardServiceConnectObsButton.ToolTip;
        DashboardTopConnectTwitchButton.ToolTip = DashboardServiceConnectTwitchButton.ToolTip;
        DashboardTopConnectSpotifyButton.ToolTip = DashboardServiceConnectSpotifyButton.ToolTip;
        DashboardTopConnectStreamerBotButton.ToolTip = DashboardServiceConnectStreamerBotButton.ToolTip;
    }

    private async Task ConnectObsFromDashboardAsync()
    {
        await ConnectObsAsync();

        if (_obsClient.IsConnected)
        {
            await RefreshObsAsync();
        }

        AddDashboardNotification(
            _obsClient.IsConnected
                ? "OBS ist verbunden."
                : "OBS konnte nicht verbunden werden.",
            _obsClient.IsConnected ? "Info" : "Warnung");
    
        RefreshDashboardServiceActionButtons();
}

    private async Task ConnectTwitchFromDashboardAsync()
    {
        await ConnectTwitchAsync();

        var connected = _twitchModule.GetSnapshot().Authenticated;
        RefreshTwitchUi();

        if (connected)
        {
            await RefreshTwitchFollowerCountAsync();
            await RefreshTwitchGoalsAsync();
            await RefreshLiveViewerSampleAsync();
        }

        AddDashboardNotification(
            connected
                ? "Twitch ist verbunden."
                : "Twitch konnte nicht verbunden werden.",
            connected ? "Info" : "Warnung");
    
        RefreshDashboardServiceActionButtons();
}

    private async Task ConnectSpotifyFromDashboardAsync()
    {
        await ConnectSpotifyAsync();

        var connected = _spotifyModule.GetSnapshot().Authenticated;

        if (connected)
        {
            await RefreshSpotifyAsync();
        }
        else
        {
            RefreshSpotifyUi();
        }

        AddDashboardNotification(
            connected
                ? "Spotify ist verbunden."
                : "Spotify konnte nicht verbunden werden.",
            connected ? "Info" : "Warnung");
    
        RefreshDashboardServiceActionButtons();
}

    private async Task ConnectStreamerBotFromDashboardAsync()
    {
        await ConnectStreamerBotAsync();

        var connected =
            _streamerBotSocket?.State ==
            System.Net.WebSockets.WebSocketState.Open;

        StreamerBotDashboardStatus.Text =
            connected ? "VERBUNDEN" : "NICHT VERBUNDEN";
        StreamerBotDashboardLamp.Fill =
            connected
                ? System.Windows.Media.Brushes.LimeGreen
                : System.Windows.Media.Brushes.IndianRed;

        AddDashboardNotification(
            connected
                ? "Streamer.bot ist verbunden."
                : "Streamer.bot konnte nicht verbunden werden.",
            connected ? "Info" : "Warnung");
    
        RefreshDashboardServiceActionButtons();
}

    private async Task ConnectObsAsync(bool showErrorDialog = true)
    {
        try
        {
            ObsConnectionStatusText.Text = "Verbindung wird hergestellt ...";
            ObsConnectionStatusText.Foreground =
                System.Windows.Media.Brushes.Goldenrod;

            await _secretStore.SaveAsync(
                "obs.password",
                ObsPasswordBox.Password);

            await _obsClient.ConnectAsync(
                new ObsConnectionOptions(
                    ObsHostBox.Text.Trim(),
                    int.Parse(ObsPortBox.Text.Trim()),
                    ObsPasswordBox.Password,
                    TimeSpan.FromSeconds(8),
                    TimeSpan.FromSeconds(8)));

            await RefreshObsAsync();

            ObsConnectionStatusText.Text = "Verbunden";
            ObsConnectionStatusText.Foreground =
                System.Windows.Media.Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            ObsConnectionStatusText.Text = exception.Message;
            ObsConnectionStatusText.Foreground =
                System.Windows.Media.Brushes.IndianRed;

            if (showErrorDialog)
            {
                MessageBox.Show(
                    exception.Message,
                    "OBS-Verbindung fehlgeschlagen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private async Task DisconnectObsAsync()
    {
        await _obsClient.DisconnectAsync();
        ObsScenesList.ItemsSource = null;
        ObsInputsList.ItemsSource = null;
        ServicesObsScenesList.ItemsSource = null;
        ServicesObsAutomationSceneBox.ItemsSource = null;
        ServicesObsAutomationSourceBox.ItemsSource = null;
        ServicesObsInputsList.ItemsSource = null;
        ServicesObsTransitionBox.ItemsSource = null;
        ServicesSpotifyOverlaySceneBox.ItemsSource = null;
        ServicesSpotifyOverlaySourceBox.ItemsSource = null;
        OverlayProjectObsSceneBox.ItemsSource = null;
        ServicesObsTransitionStateText.Text = "OBS ist nicht verbunden.";
        DashboardObsAudioInputBox.ItemsSource = null;
        DashboardObsAudioStateText.Text = "OBS ist nicht verbunden.";
        ObsServerInfoText.Text =
            "OBS-Informationen erscheinen nach der Verbindung.";
        ObsStreamStatusText.Text = "Streamstatus unbekannt";
        ObsDashboardStatus.Text = "NICHT VERBUNDEN";
        ObsDashboardLamp.Fill = System.Windows.Media.Brushes.IndianRed;
    
        RefreshDashboardServiceActionButtons();
}

    private void RefreshSimpleObsAutomationRulesList()
    {
        if (ServicesObsAutomationRulesList is null) return;
        ServicesObsAutomationRulesList.ItemsSource = _settings.Workflow.TimedAutomations
            .Where(rule => (string.Equals(rule.TriggerType, "SceneElapsed", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rule.TriggerType, "StreamElapsed", StringComparison.OrdinalIgnoreCase))
                && string.Equals(rule.ActionType, "SetSourceVisibility", StringComparison.OrdinalIgnoreCase))
            .OrderBy(rule => rule.DelaySeconds)
            .ToList();
    }

    private async Task RefreshSimpleObsAutomationSourcesAsync()
    {
        if (!_obsClient.IsConnected || ServicesObsAutomationSceneBox.SelectedItem is not ObsSceneInfo scene)
        {
            ServicesObsAutomationSourceBox.ItemsSource = Array.Empty<string>();
            return;
        }

        try
        {
            var sources = (await _obsClient.GetSceneItemListAsync(scene.Name))
                .Select(item => item.SourceName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ServicesObsAutomationSourceBox.ItemsSource = sources;
            if (sources.Count > 0 && ServicesObsAutomationSourceBox.SelectedItem is null)
                ServicesObsAutomationSourceBox.SelectedIndex = 0;
            ServicesObsAutomationStatusText.Text = $"{sources.Count} Quellen aus Szene ‘{scene.Name}’ geladen.";
        }
        catch (Exception exception)
        {
            ServicesObsAutomationStatusText.Text = "Quellen konnten nicht geladen werden: " + exception.Message;
        }
    }

    private async Task AddSimpleObsAutomationRuleAsync()
    {
        if (ServicesObsAutomationSceneBox.SelectedItem is not ObsSceneInfo scene)
        {
            ServicesObsAutomationStatusText.Text = "Bitte zuerst eine Szene auswählen.";
            return;
        }
        var source = ServicesObsAutomationSourceBox.SelectedItem as string ?? ServicesObsAutomationSourceBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(source))
        {
            ServicesObsAutomationStatusText.Text = "Bitte eine Quelle auswählen.";
            return;
        }
        if (!int.TryParse(ServicesObsAutomationDelayBox.Text, out var seconds) || seconds < 0)
        {
            ServicesObsAutomationStatusText.Text = "Bitte eine gültige Zeit in Sekunden eingeben.";
            return;
        }

        var show = string.Equals((ServicesObsAutomationActionBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), "Show", StringComparison.OrdinalIgnoreCase);
        var rule = new TimedAutomationRuleSettings
        {
            Name = $"{scene.Name} → {source}: nach {seconds} Sek. {(show ? "einblenden" : "ausblenden")}",
            Enabled = true,
            TriggerType = "SceneElapsed",
            TriggerScene = scene.Name,
            DelaySeconds = seconds,
            ActionType = "SetSourceVisibility",
            ObsScene = scene.Name,
            ObsSource = source,
            SourceVisible = show,
            OncePerStream = true
        };
        _settings.Workflow.TimedAutomations.Add(rule);
        await _settingsStore.SaveAsync(_settings);
        RefreshTimedAutomationRules();
        RefreshSimpleObsAutomationRulesList();
        ServicesObsAutomationStatusText.Text = "Regel wurde hinzugefügt und gespeichert.";
    }

    private async Task DeleteSimpleObsAutomationRuleAsync()
    {
        if (ServicesObsAutomationRulesList.SelectedItem is not TimedAutomationRuleSettings rule)
        {
            ServicesObsAutomationStatusText.Text = "Bitte eine Regel aus der Liste auswählen.";
            return;
        }
        _settings.Workflow.TimedAutomations.Remove(rule);
        await _settingsStore.SaveAsync(_settings);
        RefreshTimedAutomationRules();
        RefreshSimpleObsAutomationRulesList();
        ServicesObsAutomationStatusText.Text = "Regel wurde gelöscht.";
    }

    private async Task TestSimpleObsAutomationRuleAsync()
    {
        if (ServicesObsAutomationRulesList.SelectedItem is not TimedAutomationRuleSettings rule)
        {
            ServicesObsAutomationStatusText.Text = "Bitte eine Regel aus der Liste auswählen.";
            return;
        }
        if (!_obsClient.IsConnected)
        {
            ServicesObsAutomationStatusText.Text = "OBS ist nicht verbunden.";
            return;
        }
        await _obsClient.SetSceneItemEnabledAsync(rule.ObsScene, rule.ObsSource, rule.SourceVisible);
        ServicesObsAutomationStatusText.Text = "Regel wurde sofort in OBS getestet.";
    }

    private async Task ExecuteObsControlAsync(string operation, Func<Task> action)
    {
        if (!_obsClient.IsConnected)
        {
            ServicesObsControlStatusText.Text = "OBS ist nicht verbunden.";
            return;
        }
        try
        {
            ServicesObsControlStatusText.Text = operation + " …";
            await action();
            ServicesObsControlStatusText.Text = operation + " wurde ausgeführt.";
            await Task.Delay(250);
            await RefreshObsAsync();
        }
        catch (Exception ex)
        {
            ServicesObsControlStatusText.Text = operation + " fehlgeschlagen: " + ex.Message;
        }
    }

    private async Task ToggleObsRecordPauseAsync()
    {
        if (!_obsClient.IsConnected) return;
        try
        {
            var status = await _obsClient.GetRecordStatusAsync();
            if (!status.Active)
            {
                ServicesObsControlStatusText.Text = "Es läuft keine Aufnahme.";
                return;
            }
            if (status.Paused)
                await ExecuteObsControlAsync("Aufnahme fortsetzen", () => _obsClient.ResumeRecordAsync());
            else
                await ExecuteObsControlAsync("Aufnahme pausieren", () => _obsClient.PauseRecordAsync());
        }
        catch (Exception ex)
        {
            ServicesObsControlStatusText.Text = "Aufnahmestatus konnte nicht gelesen werden: " + ex.Message;
        }
    }

    private async Task RefreshObsProfessionalControlAsync(ObsStreamStatus? stream)
    {
        ServicesObsStreamStateText.Text = stream?.OutputActive == true
            ? $"Live · {stream.OutputTimecode}"
            : "Offline";
        try
        {
            var stats = await _obsClient.GetStatsAsync();
            ServicesObsCpuText.Text = $"CPU: {stats.CpuUsage:0.0} %";
            ServicesObsFpsText.Text = $"FPS: {stats.ActiveFps:0.0}";
            ServicesObsMemoryText.Text = $"RAM: {stats.MemoryUsage:0} MB";
            ServicesObsRenderLagText.Text = $"Render-Lag: {stats.RenderSkippedFrames}/{stats.RenderTotalFrames}";
            ServicesObsOutputLagText.Text = $"Encoding-Lag: {stats.OutputSkippedFrames}/{stats.OutputTotalFrames}";
        }
        catch { }
        try
        {
            var record = await _obsClient.GetRecordStatusAsync();
            ServicesObsRecordStateText.Text = !record.Active ? "Gestoppt" : record.Paused ? "Pausiert" : $"Läuft · {record.Timecode}";
            ServicesObsPauseRecordButton.Content = record.Paused ? "FORTSETZEN" : "PAUSE";
        }
        catch { ServicesObsRecordStateText.Text = "Nicht verfügbar"; }
        try
        {
            var replay = await _obsClient.GetReplayBufferStatusAsync();
            ServicesObsReplayStateText.Text = replay.Active ? "Aktiv" : "Gestoppt";
        }
        catch { ServicesObsReplayStateText.Text = "Nicht verfügbar"; }
        try
        {
            var virtualCam = await _obsClient.GetVirtualCamStatusAsync();
            ServicesObsVirtualCamStateText.Text = virtualCam ? "Aktiv" : "Gestoppt";
        }
        catch { ServicesObsVirtualCamStateText.Text = "Nicht verfügbar"; }
    }

    private async Task RefreshObsAsync()
    {
        if (!_obsClient.IsConnected)
        {
            SetObsDisconnectedUi("OBS ist nicht verbunden.");
            return;
        }

        try
        {
            await RefreshObsCoreAsync();
        }
        catch (InvalidOperationException ex) when
            (ex.Message.Contains("nicht verbunden", StringComparison.OrdinalIgnoreCase))
        {
            SetObsDisconnectedUi("OBS ist nicht verbunden.");
        }
        catch (Exception ex)
        {
            SetObsDisconnectedUi("OBS konnte nicht aktualisiert werden: " + ex.Message);
        }
    }

    private void SetObsDisconnectedUi(string message)
    {
        void UpdateUi()
        {
            ObsDashboardStatus.Text = "NICHT VERBUNDEN";
            ObsDashboardLamp.Fill = System.Windows.Media.Brushes.IndianRed;
            ObsConnectionStatusText.Text = message;
            ObsConnectionStatusText.Foreground = System.Windows.Media.Brushes.Gray;
            ServicesObsStatusText.Text = message;
            ServicesObsStatusText.Foreground = System.Windows.Media.Brushes.Gray;
        }

        if (Dispatcher.CheckAccess())
        {
            UpdateUi();
        }
        else
        {
            Dispatcher.BeginInvoke(UpdateUi);
        }
    }

    private async Task RefreshObsCoreAsync()
    {
        if (!_obsClient.IsConnected)
        {
            throw new InvalidOperationException("OBS ist nicht verbunden.");
        }

        var snapshot = await _obsClient.GetSnapshotAsync();
        var transitions = await _obsClient.GetSceneTransitionListAsync();

        if (!string.Equals(_automationCurrentScene, snapshot.CurrentProgramScene, StringComparison.OrdinalIgnoreCase))
        {
            _automationCurrentScene = snapshot.CurrentProgramScene;
            _automationSceneActivatedAt = DateTimeOffset.UtcNow;
            foreach (var sceneRule in _settings.Workflow.TimedAutomations
                         .Where(rule => string.Equals(rule.TriggerType, "SceneElapsed", StringComparison.OrdinalIgnoreCase)
                                        && string.Equals(rule.TriggerScene, snapshot.CurrentProgramScene, StringComparison.OrdinalIgnoreCase)))
            {
                _executedTimedAutomationRuleIds.Remove(sceneRule.Id);
            }
        }

        ObsScenesList.ItemsSource = snapshot.Scenes;
        ObsInputsList.ItemsSource = snapshot.Inputs;
        var selectedObsInputName = (ServicesObsInputsList.SelectedItem as ObsInputInfo)?.Name;
        var selectedTransitionName = (ServicesObsTransitionBox.SelectedItem as ObsTransitionInfo)?.Name;
        _servicesObsScenes = snapshot.Scenes;
        _servicesObsCurrentScene = snapshot.CurrentProgramScene;
        ServicesObsCurrentSceneText.Text = "Aktuelle Szene: " + snapshot.CurrentProgramScene;
        ApplyServicesObsSceneFilter();
        ServicesObsAutomationSceneBox.ItemsSource = snapshot.Scenes;
        if (ServicesObsAutomationSceneBox.SelectedItem is not ObsSceneInfo)
            ServicesObsAutomationSceneBox.SelectedItem = snapshot.Scenes.FirstOrDefault();
        _servicesObsInputs = snapshot.Inputs;
        ApplyServicesObsInputFilter();
        RefreshSimpleObsAutomationRulesList();
        await RefreshSimpleObsAutomationSourcesAsync();
        ServicesObsTransitionBox.ItemsSource = transitions;
        if (!string.IsNullOrWhiteSpace(selectedTransitionName))
            ServicesObsTransitionBox.SelectedItem = transitions.FirstOrDefault(transition => string.Equals(transition.Name, selectedTransitionName, StringComparison.OrdinalIgnoreCase));
        if (ServicesObsTransitionBox.SelectedItem is not ObsTransitionInfo)
            ServicesObsTransitionBox.SelectedItem = transitions.FirstOrDefault();
        ServicesObsTransitionStateText.Text = transitions.Count == 0
            ? "OBS hat keine auswählbaren Übergänge gemeldet."
            : $"{transitions.Count} Übergänge geladen. Auswahl und Dauer werden erst mit „Übergang übernehmen“ an OBS gesendet.";
        if (!string.IsNullOrWhiteSpace(selectedObsInputName))
            ServicesObsInputsList.SelectedItem = snapshot.Inputs.FirstOrDefault(input => string.Equals(input.Name, selectedObsInputName, StringComparison.OrdinalIgnoreCase));
        if (ServicesObsInputsList.SelectedItem is not ObsInputInfo) ServicesObsInputsList.SelectedItem = snapshot.Inputs.FirstOrDefault();
        if (ServicesObsScenesList.SelectedItem is not ObsSceneInfo)
            ServicesObsScenesList.SelectedItem = snapshot.Scenes.FirstOrDefault(scene => string.Equals(scene.Name, snapshot.CurrentProgramScene, StringComparison.OrdinalIgnoreCase)) ?? snapshot.Scenes.FirstOrDefault();
        await RefreshServicesObsSceneItemsAsync();
        DashboardObsAudioInputBox.ItemsSource = snapshot.Inputs;
        if (DashboardObsAudioInputBox.SelectedItem is null && snapshot.Inputs.Count > 0)
        {
            DashboardObsAudioInputBox.SelectedIndex = 0;
        }

        ObsServerInfoText.Text =
            $"OBS {snapshot.Server?.ObsVersion} · " +
            $"WebSocket {snapshot.Server?.WebSocketVersion} · " +
            $"Aktuelle Szene: {snapshot.CurrentProgramScene}";

        await HandleStartToGameSpotifyVolumeAsync(snapshot.CurrentProgramScene);
        DashboardCurrentSceneText.Text = snapshot.CurrentProgramScene;
        var dashboardScenes = snapshot.Scenes
            .Select(scene => scene.Name)
            .Where(scene => !string.IsNullOrWhiteSpace(scene))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Keep every OBS-scene selector on the same live scene list. Previously the
        // Spotify selector copied OverlayProjectObsSceneBox.ItemsSource only while
        // overlay projects were loaded. If OBS connected afterwards, it stayed empty.
        var requestedSpotifyOverlayScene = ServicesSpotifyOverlaySceneBox.Text?.Trim();
        var requestedOverlayProjectScene = OverlayProjectObsSceneBox.Text?.Trim();
        var requestedStartScene = StartSceneBox.Text?.Trim();
        var requestedLiveScene = LiveSceneBox.Text?.Trim();
        var requestedPauseScene = PauseSceneBox.Text?.Trim();
        var requestedEndScene = EndSceneBox.Text?.Trim();
        StartSceneBox.ItemsSource = snapshot.Scenes;
        LiveSceneBox.ItemsSource = snapshot.Scenes;
        PauseSceneBox.ItemsSource = snapshot.Scenes;
        EndSceneBox.ItemsSource = snapshot.Scenes;
        StartSceneBox.Text = requestedStartScene ?? _settings.Obs.StartScene;
        LiveSceneBox.Text = requestedLiveScene ?? _settings.Obs.LiveScene;
        PauseSceneBox.Text = requestedPauseScene ?? _settings.Obs.PauseScene;
        EndSceneBox.Text = requestedEndScene ?? _settings.Obs.EndScene;
        ServicesSpotifyOverlaySceneBox.ItemsSource = dashboardScenes;
        OverlayProjectObsSceneBox.ItemsSource = dashboardScenes;

        if (!string.IsNullOrWhiteSpace(requestedSpotifyOverlayScene))
        {
            ServicesSpotifyOverlaySceneBox.Text = requestedSpotifyOverlayScene;
        }
        else if (!string.IsNullOrWhiteSpace(_settings.Spotify.OverlayObsScene))
        {
            ServicesSpotifyOverlaySceneBox.Text = _settings.Spotify.OverlayObsScene;
        }

        if (!string.IsNullOrWhiteSpace(requestedOverlayProjectScene))
        {
            OverlayProjectObsSceneBox.Text = requestedOverlayProjectScene;
        }

        await RefreshSpotifyOverlayBrowserSourcesAsync();

        // Do not overwrite a scene the user is currently choosing. The dashboard
        // refresh runs periodically and previously reset the ComboBox to the active
        // OBS scene before the user could press “Szene wechseln”.
        var requestedDashboardScene = DashboardSceneBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(requestedDashboardScene))
        {
            requestedDashboardScene = DashboardSceneBox.Text?.Trim();
        }

        var existingDashboardScenes = DashboardSceneBox.ItemsSource as IEnumerable<string>;
        var sceneListChanged = existingDashboardScenes is null ||
            !existingDashboardScenes.SequenceEqual(dashboardScenes, StringComparer.OrdinalIgnoreCase);

        if (!DashboardSceneBox.IsDropDownOpen && sceneListChanged)
        {
            DashboardSceneBox.ItemsSource = dashboardScenes;
        }

        var sceneToKeep = dashboardScenes.FirstOrDefault(scene =>
            string.Equals(scene, requestedDashboardScene, StringComparison.OrdinalIgnoreCase));
        sceneToKeep ??= dashboardScenes.FirstOrDefault(scene =>
            string.Equals(scene, snapshot.CurrentProgramScene, StringComparison.OrdinalIgnoreCase));

        if (!DashboardSceneBox.IsDropDownOpen && DashboardSceneBox.SelectedItem is null && sceneToKeep is not null)
        {
            DashboardSceneBox.SelectedItem = sceneToKeep;
        }

        var requestedNextScene = DashboardNextSceneBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(requestedNextScene))
        {
            requestedNextScene = DashboardNextSceneBox.Text?.Trim();
        }
        var existingNextScenes = DashboardNextSceneBox.ItemsSource as IEnumerable<string>;
        var nextSceneListChanged = existingNextScenes is null ||
            !existingNextScenes.SequenceEqual(dashboardScenes, StringComparer.OrdinalIgnoreCase);
        if (!DashboardNextSceneBox.IsDropDownOpen && nextSceneListChanged)
        {
            DashboardNextSceneBox.ItemsSource = dashboardScenes;
        }
        var nextSceneToKeep = dashboardScenes.FirstOrDefault(scene =>
            string.Equals(scene, requestedNextScene, StringComparison.OrdinalIgnoreCase));
        nextSceneToKeep ??= dashboardScenes.FirstOrDefault(scene =>
            !string.Equals(scene, snapshot.CurrentProgramScene, StringComparison.OrdinalIgnoreCase));
        if (!DashboardNextSceneBox.IsDropDownOpen && DashboardNextSceneBox.SelectedItem is null && nextSceneToKeep is not null)
        {
            DashboardNextSceneBox.SelectedItem = nextSceneToKeep;
        }
        await RefreshDashboardObsScenePreviewAsync(snapshot.CurrentProgramScene);

        ObsStreamStatusText.Text = snapshot.Stream?.OutputActive == true
            ? $"LIVE · {snapshot.Stream.OutputTimecode}"
            : "Offline";
        await RefreshObsProfessionalControlAsync(snapshot.Stream);
        DashboardHeaderStreamActionButton.Content =
            snapshot.Stream?.OutputActive == true
                ? "■  STREAM BEENDEN"
                : "●  LIVE GEHEN";

        var obsReportsStreamActive = snapshot.Stream?.OutputActive == true;
        if (obsReportsStreamActive)
        {
            _consecutiveObsStreamInactivePolls = 0;
        }
        else if (snapshot.Connected && (_lastObsStreamActive || _streamSessionStartedAt.HasValue))
        {
            // Nur eine ausdrücklich verbundene OBS-Instanz darf einen laufenden
            // Stream als inaktiv bestätigen. Ein nicht erreichbarer Remote-PC,
            // ein Verbindungswechsel oder ein unvollständiger Snapshot ist kein
            // Streamende und darf den Live-Latch nicht lösen.
            _consecutiveObsStreamInactivePolls++;
        }

        // OBS liefert beim Aktualisieren des Output-Status gelegentlich einen
        // leeren/false Zwischenwert. Erst nach fünf aufeinanderfolgenden
        // bestätigten Offline-Abfragen wird der Stream als beendet behandelt.
        // Während Verbindungsabbrüchen bleibt der zuletzt bestätigte Zustand bestehen.
        var streamActiveNow = obsReportsStreamActive ||
            ((_lastObsStreamActive || _streamSessionStartedAt.HasValue) && _consecutiveObsStreamInactivePolls < ConfirmedObsOfflinePollsRequired);

        if (streamActiveNow && !_lastObsStreamActive)
        {
            // Ein Stream kann auch direkt in OBS, über Streamer.bot oder über
            // einen anderen Steuerweg gestartet worden sein. In diesem Fall
            // existiert bislang keine Session-Startzeit. Ohne startedAt zeigt
            // live-status.html trotz isLive=true weiterhin OFFLINE an.
            _streamSessionStartedAt ??= ResolveObservedObsStreamStartedAt(snapshot.Stream?.OutputTimecode);
            _ = HandleObservedStreamStartAsync();
        }
        else if (!streamActiveNow && _lastObsStreamActive)
        {
            _streamStartAutomationCts?.Cancel();
            _streamSessionStartedAt = null;
            _spotifyStartPlaylistTriggeredForCurrentStream = false;
            _consecutiveObsStreamInactivePolls = 0;
        }
        _lastObsStreamActive = streamActiveNow;
        RefreshWorkflowUi(_workflowModule.Service.State);

        var microphoneMuted = await GetTrackedObsInputMuteAsync(
            snapshot.Inputs,
            _settings.Obs.MicrophoneSource,
            new[] { "Mic", "Mikrofon", "Microphone" },
            new[] { "mikrofon", "microphone", "mic" });

        var desktopAudioMuted = await GetTrackedObsInputMuteAsync(
            snapshot.Inputs,
            _settings.Obs.DesktopAudioSource,
            new[] { "Broadcast", "Desktop Audio", "Desktop-Audio", "Spiel- und Streamsound" },
            new[] { "broadcast", "desktop audio", "desktop-audio", "streamsound", "spiel- und streamsound" });

        await _overlayModule.Service.UpdateAsync(
            data =>
            {
                data.Obs.Connected = snapshot.Connected;
                data.Obs.CurrentScene = snapshot.CurrentProgramScene;
                data.Obs.MicrophoneMuted = microphoneMuted;
                data.Obs.DesktopAudioMuted = desktopAudioMuted;
                data.Stream.CurrentScene = snapshot.CurrentProgramScene;
            });

        await UpdateActiveOverlayJsonAsync(root =>
        {
            var obs = root["obs"] as JsonObject ?? new JsonObject();
            obs["connected"] = snapshot.Connected;
            obs["currentScene"] = snapshot.CurrentProgramScene;
            obs["microphoneMuted"] = microphoneMuted;
            obs["desktopAudioMuted"] = desktopAudioMuted;
            root["obs"] = obs;

            var stream = root["stream"] as JsonObject ?? new JsonObject();
            stream["isLive"] = streamActiveNow;
            stream["currentScene"] = snapshot.CurrentProgramScene;
            stream["startedAt"] = _streamSessionStartedAt;
            stream["elapsedSeconds"] = _streamSessionStartedAt.HasValue
                ? Math.Max(0, (long)(DateTimeOffset.Now - _streamSessionStartedAt.Value).TotalSeconds)
                : 0;
            stream["viewerCount"] = _currentLiveViewerCount;
            root["stream"] = stream;

            var stats = root["stats"] as JsonObject ?? new JsonObject();
            var sessionStats = _workflowModule.Service.SessionStats;
            stats["followersGained"] = sessionStats.FollowersGained;
            stats["peakViewers"] = sessionStats.PeakViewers;
            stats["averageViewers"] = sessionStats.AverageViewers;
            stats["streamTimeSeconds"] = sessionStats.StreamTimeSeconds;
            stats["chatMessages"] = sessionStats.ChatMessages;
            stats["alertsPlayed"] = sessionStats.AlertsPlayed;
            stats["newSubscriptions"] = sessionStats.NewSubscriptions;
            stats["giftSubscriptions"] = sessionStats.GiftSubscriptions;
            stats["bitsCheered"] = sessionStats.BitsCheered;
            stats["incomingRaids"] = sessionStats.IncomingRaids;
            root["stats"] = stats;
        });
    }

    private static DateTimeOffset ResolveObservedObsStreamStartedAt(string? outputTimecode)
    {
        // OBS liefert üblicherweise HH:mm:ss.fff. Dadurch kann die Suite auch
        // nach einem Neustart während eines laufenden Streams die bisherige
        // Laufzeit rekonstruieren. Bei unbekanntem Format beginnt die Anzeige
        // mit dem Zeitpunkt, zu dem der Stream erstmals erkannt wurde.
        if (!string.IsNullOrWhiteSpace(outputTimecode) &&
            TimeSpan.TryParse(outputTimecode, System.Globalization.CultureInfo.InvariantCulture, out var elapsed) &&
            elapsed >= TimeSpan.Zero && elapsed < TimeSpan.FromDays(30))
        {
            return DateTimeOffset.Now - elapsed;
        }

        return DateTimeOffset.Now;
    }

    private async Task HandleObservedStreamStartAsync()
    {
        try
        {
            await StartLegacyStreamAutomationAsync();
        }
        catch (Exception exception)
        {
            _appLogger.Write(AppLogLevel.Warning, "StreamStart",
                "Streamstart-Automation konnte nicht vollständig gestartet werden: " + exception.Message, exception);
        }

        if (_spotifyStartPlaylistTriggeredForCurrentStream)
        {
            return;
        }

        try
        {
            await StartConfiguredSpotifyPlaylistAtStreamStartAsync();
        }
        catch (Exception exception)
        {
            _appLogger.Write(AppLogLevel.Warning, "Spotify.StartPlaylist",
                "Ausgewählte Startplaylist konnte beim erkannten Streamstart nicht gestartet werden: " + exception.Message, exception);
            AddDashboardNotification(
                "Spotify-Startplaylist konnte nicht gestartet werden: " + exception.Message,
                "Warnung");
        }
    }

    private async Task<bool> GetTrackedObsInputMuteAsync(
        IReadOnlyList<ObsInputInfo> inputs,
        string configuredSource,
        IReadOnlyList<string> preferredExactNames,
        IReadOnlyList<string> fallbackNameParts)
    {
        if (!_obsClient.IsConnected || inputs.Count == 0)
        {
            return false;
        }

        ObsInputInfo? input = null;

        if (!string.IsNullOrWhiteSpace(configuredSource))
        {
            input = inputs.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, configuredSource.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        input ??= preferredExactNames
            .Select(name => inputs.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(candidate => candidate is not null);

        input ??= inputs.FirstOrDefault(candidate =>
            fallbackNameParts.Any(part =>
                candidate.Name.Contains(part, StringComparison.OrdinalIgnoreCase)));

        if (input is null)
        {
            return false;
        }

        try
        {
            var state = await _obsClient.GetInputAudioStateAsync(input.Name);
            return state.Muted;
        }
        catch (Exception exception)
        {
            _appLogger.Write(
                AppLogLevel.Warning,
                "OBS.MuteState",
                $"Mute-Status für OBS-Quelle '{input.Name}' konnte nicht gelesen werden: {exception.Message}",
                exception);
            return false;
        }
    }

    private async Task RefreshDashboardObsScenePreviewAsync(string? sceneName = null)
    {
        try
        {
            if (!_obsClient.IsConnected)
            {
                DashboardObsScenePreviewImage.Source = null;
                DashboardObsScenePreviewPlaceholder.Visibility = Visibility.Visible;
                return;
            }

            sceneName ??= await _obsClient.GetCurrentProgramSceneAsync();
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return;
            }

            var bytes = await _obsClient.GetSourceScreenshotAsync(sceneName, 800, 450);
            if (bytes.Length == 0)
            {
                DashboardObsScenePreviewImage.Source = null;
                DashboardObsScenePreviewPlaceholder.Visibility = Visibility.Visible;
                return;
            }

            using var stream = new System.IO.MemoryStream(bytes);
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            DashboardObsScenePreviewImage.Source = bitmap;
            DashboardObsScenePreviewPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            DashboardObsScenePreviewImage.Source = null;
            DashboardObsScenePreviewPlaceholder.Visibility = Visibility.Visible;
            _appLogger.Write(AppLogLevel.Warning, "OBS", "OBS-Szenenvorschau konnte nicht geladen werden.", exception);
        }
    }

    private async Task SwitchObsSceneAsync()
    {
        if (ObsScenesList.SelectedItem is not ObsSceneInfo scene)
        {
            MessageBox.Show(
                "Bitte zuerst eine Szene auswählen.",
                "OBS",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        await _obsClient.SetCurrentProgramSceneAsync(scene.Name);
        await RefreshObsAsync();
    }

    private async Task ToggleDashboardHeaderStreamAsync()
    {
        try
        {
            if (!_obsClient.IsConnected)
            {
                AddDashboardNotification(
                    "OBS ist nicht verbunden.",
                    "Warnung");
                return;
            }

            var snapshot = await _obsClient.GetSnapshotAsync();

            if (snapshot.Stream?.OutputActive == true)
            {
                await StopObsStreamAsync();
            }
            else
            {
                await StartObsStreamAsync();
            }

            await RefreshObsAsync();
        }
        catch (Exception ex)
        {
            AddDashboardNotification(
                "Stream-Aktion fehlgeschlagen: " + ex.Message,
                "Fehler");
        }
    }

    private async Task StartConfiguredSpotifyPlaylistAtStreamStartAsync()
    {
        // Beim automatischen Streamstart niemals UI-Standardwerte zurück in die
        // Einstellungen schreiben. Seit der Remote-PC-Erweiterung konnte dieser
        // Hintergrundpfad noch nicht vollständig geladene Steuerelemente lesen und
        // damit AutoStart bzw. die Playlist-URI wieder leeren. Maßgeblich ist die
        // zuletzt dauerhaft gespeicherte Konfiguration.
        var persisted = await _settingsStore.LoadAsync(CancellationToken.None);

        if (!persisted.Workflow.AutoStartSpotifyPlaylist)
        {
            _appLogger.Write(AppLogLevel.Information, "Spotify.StartPlaylist",
                "Automatischer Playliststart ist in den gespeicherten Einstellungen deaktiviert.");
            return;
        }

        var playlistUri = persisted.Spotify.StartPlaylistUri?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(playlistUri))
        {
            throw new InvalidOperationException(
                "Für den Streamstart ist keine dauerhaft gespeicherte Spotify-Playlist ausgewählt.");
        }

        if (!_spotifyModule.GetSnapshot().Authenticated)
        {
            await _spotifyModule.ConnectAsync(CancellationToken.None);
        }

        // Spotify kann unmittelbar nach dem erkannten OBS-Start kurz noch kein
        // aktives Wiedergabegerät melden. Deshalb wird der identische Start genau
        // einmal verzögert wiederholt, ohne die Playlist mehrfach auszulösen.
        Exception? firstFailure = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                await _spotifyModule.StartPlaylistAsync(
                    playlistUri,
                    startVolumePercent: 100,
                    CancellationToken.None);

                _spotifyStartPlaylistTriggeredForCurrentStream = true;
                AddDashboardNotification("Spotify-Startplaylist wurde gestartet.", "Info");
                _appLogger.Write(AppLogLevel.Information, "Spotify.StartPlaylist",
                    $"Gespeicherte Startplaylist wurde gestartet: {playlistUri}");
                return;
            }
            catch (Exception exception) when (attempt == 1)
            {
                firstFailure = exception;
                _appLogger.Write(AppLogLevel.Warning, "Spotify.StartPlaylist",
                    "Erster Startversuch fehlgeschlagen; erneuter Versuch in 2 Sekunden: " + exception.Message, exception);
                await Task.Delay(TimeSpan.FromSeconds(2));
                if (!_spotifyModule.GetSnapshot().Authenticated)
                {
                    await _spotifyModule.ConnectAsync(CancellationToken.None);
                }
            }
        }

        throw new InvalidOperationException(
            "Spotify konnte die gespeicherte Startplaylist auch nach dem Wiederholungsversuch nicht starten.",
            firstFailure);
    }

    private async Task StartObsStreamAsync()
    {
        var result = MessageBox.Show(
            "OBS-Stream wirklich starten?",
            "Stream starten",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _spotifyStartPlaylistTriggeredForCurrentStream = false;
            if (!string.IsNullOrWhiteSpace(_settings.Obs.StartScene))
            {
                await _obsClient.SetCurrentProgramSceneAsync(_settings.Obs.StartScene);
                DashboardWorkflowStageText.Text = "STARTSZENE → STREAMSTART → LIVE";
                SetWorkflowVisualStage("Start", $"Startszene aktiv: {_settings.Obs.StartScene}");
            }

            // Die Startplaylist wird ausschließlich durch den zentral bestätigten
            // OBS-Übergang OFFLINE -> LIVE ausgelöst. So gibt es unabhängig vom
            // Startweg (Suite, OBS, Streamer.bot oder Remote-PC) nur ein Ereignis
            // und keinen zu frühen Spotify-Aufruf vor dem tatsächlichen Streamstart.
            await _obsClient.StartStreamAsync();
            _streamSessionStartedAt = DateTimeOffset.Now;
            await _creatorIntelligence.StartSessionAsync(_streamSessionStartedAt.Value, DashboardTwitchTitleBox.Text, DashboardTwitchCategorySearchBox.Text);

            // Der Start-Countdown darf erst beginnen, nachdem OBS den Stream
            // tatsächlich gestartet hat. "Stream vorbereiten" stellt lediglich
            // die Startszene her und lässt den Timer bei seinem Ausgangswert stehen.
            _ = StartWorkflowCountdownAfterObsStreamStartAsync();
            await RefreshTwitchFollowerCountAsync(
                initializeStreamBaseline: true);
            AddDashboardNotification($"OBS-Stream wurde gestartet.", "Info");

            await Task.Delay(500);

            if (false && _settings.Workflow.AutoSwitchScenes &&
                !string.IsNullOrWhiteSpace(_settings.Obs.LiveScene))
            {
                await _obsClient.SetCurrentProgramSceneAsync(_settings.Obs.LiveScene);
                DashboardWorkflowStageText.Text = "LIVE";
                SetWorkflowVisualStage("Live", $"Stream läuft · Szene: {_settings.Obs.LiveScene}");
            }

            await RefreshObsAsync();
            await RefreshLiveViewerSampleAsync();
            if (_settings.Dashboard.AutoFocusModeOnStreamStart &&
                !_dashboardFocusModeActive)
            {
                EnterDashboardFocusMode();
            }
        }
        catch (Exception exception)
        {
            AddDashboardNotification($"Streamstart fehlgeschlagen: {exception.Message}", "Fehler");
            MessageBox.Show(
                exception.Message,
                "Streamstart fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task StartWorkflowCountdownAfterObsStreamStartAsync()
    {
        try
        {
            await ExecuteWorkflowAsync(
                () => _workflowModule.Service.StartCountdownAsync());
        }
        catch (OperationCanceledException)
        {
            // Ein bewusst abgebrochener Countdown ist kein Programmfehler.
        }
        catch (Exception exception)
        {
            AddDashboardNotification(
                "Start-Countdown konnte nicht gestartet werden: " + exception.Message,
                "Warnung");
        }
    }

    private async Task UpdateCurrentStreamStatsForEndSceneAsync(bool finalize)
    {
        var endedAt = finalize ? DateTimeOffset.Now : (DateTimeOffset?)null;

        // Letzte Live-Werte unmittelbar vor der Endszene abrufen.
        await RefreshLiveViewerSampleAsync();
        await RefreshTwitchFollowerCountAsync();
        if (endedAt.HasValue)
        {
            await _workflowModule.Service.FinalizeSessionStatsAsync(endedAt);
        }

        var sessionStats = _workflowModule.Service.SessionStats;
        await UpdateActiveOverlayJsonAsync(root =>
        {
            var stream = root["stream"] as JsonObject ?? new JsonObject();
            stream["isLive"] = true;
            stream["phase"] = "Ending";
            stream["startedAt"] = _streamSessionStartedAt ?? sessionStats.StartedAt;
            stream["endedAt"] = endedAt;
            stream["elapsedSeconds"] = sessionStats.StreamTimeSeconds;
            stream["viewerCount"] = _currentLiveViewerCount;
            root["stream"] = stream;

            var stats = root["stats"] as JsonObject ?? new JsonObject();
            stats["followersGained"] = sessionStats.FollowersGained;
            stats["peakViewers"] = sessionStats.PeakViewers;
            stats["averageViewers"] = Math.Round(sessionStats.AverageViewers, 1);
            stats["streamTimeSeconds"] = sessionStats.StreamTimeSeconds;
            stats["chatMessages"] = sessionStats.ChatMessages;
            stats["alertsPlayed"] = sessionStats.AlertsPlayed;
            stats["newSubscriptions"] = sessionStats.NewSubscriptions;
            stats["giftSubscriptions"] = sessionStats.GiftSubscriptions;
            stats["bitsCheered"] = sessionStats.BitsCheered;
            stats["incomingRaids"] = sessionStats.IncomingRaids;
            stats["finalizedAt"] = endedAt;
            root["stats"] = stats;
        });
    }

    private async Task<bool> RunRaidCountdownAsync(string displayName, int seconds)
    {
        _raidCountdownCts?.Cancel();
        _raidCountdownCts?.Dispose();
        _raidCountdownCts = new CancellationTokenSource();
        var token = _raidCountdownCts.Token;
        _raidCountdownActive = true;
        DashboardRaidStatusPanel.Visibility = Visibility.Visible;
        DashboardRaidCountdownTitleText.Text = "RAID LÄUFT";
        DashboardRaidCountdownTargetText.Text = $"Ziel: {displayName}";
        DashboardRaidViewerText.Text = $"Aktuelle Zuschauer: {_currentLiveViewerCount}";
        DashboardRaidCountdownProgress.Minimum = 0;
        DashboardRaidCountdownProgress.Maximum = Math.Max(1, seconds);

        try
        {
            for (var remaining = seconds; remaining >= 0; remaining--)
            {
                token.ThrowIfCancellationRequested();
                DashboardRaidCountdownText.Text = $"Raid in: {TimeSpan.FromSeconds(remaining):mm\\:ss}";
                DashboardRaidCountdownProgress.Value = seconds - remaining;
                DashboardWorkflowStageText.Text = $"RAID → {displayName} · noch {remaining}s";
                if (remaining > 0)
                {
                    await Task.Delay(1000, token);
                }
            }

            DashboardRaidCountdownTitleText.Text = "RAID AUSGEFÜHRT";
            DashboardRaidCountdownText.Text = "Stream wird beendet …";
            DashboardRaidCountdownProgress.Value = seconds;
            return true;
        }
        catch (OperationCanceledException)
        {
            DashboardRaidCountdownTitleText.Text = "RAID ABGEBROCHEN";
            DashboardRaidCountdownText.Text = "Stream bleibt aktiv";
            DashboardWorkflowStageText.Text = "RAID ABGEBROCHEN · STREAM LÄUFT WEITER";
            return false;
        }
        finally
        {
            _raidCountdownActive = false;
        }
    }

    private async Task CancelActiveRaidAsync()
    {
        if (!_raidCountdownActive)
        {
            return;
        }

        try
        {
            await _twitchModule.CancelRaidAsync();
            _raidCountdownCts?.Cancel();
            AddDashboardNotification("Twitch-Raid wurde abgebrochen. Der Stream bleibt aktiv.", "Info");
        }
        catch (Exception exception)
        {
            AddDashboardNotification($"Raid konnte nicht abgebrochen werden: {exception.Message}", "Fehler");
        }
    }

    private async Task StopObsStreamAsync()
    {
        var result = MessageBox.Show(
            "Streamende starten? Die konfigurierte Endszene wird vor dem tatsächlichen Stop angezeigt.",
            "Stream beenden",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            // Eine eventuell noch laufende Startautomatik darf während der
            // Endszene und nach dem Streamende nicht mehr auf Game wechseln.
            _streamStartAutomationCts?.Cancel();

            DashboardWorkflowStageText.Text = "STATISTIKEN ABSCHLIESSEN";
            SetWorkflowVisualStage("End", "Letzte Twitch- und Zuschauerwerte werden gespeichert.");
            await UpdateCurrentStreamStatsForEndSceneAsync(finalize: false);

            DashboardWorkflowStageText.Text = "ENDSZENE";
            SetWorkflowVisualStage("End", "Endszene läuft. Streamende wird vorbereitet.");

            if (!string.IsNullOrWhiteSpace(_settings.Obs.EndScene))
            {
                await _obsClient.SetCurrentProgramSceneAsync(_settings.Obs.EndScene);
            }

            if (_settings.Workflow.AutoPlayEndMusic && !string.IsNullOrWhiteSpace(_settings.Spotify.StartPlaylistUri))
            {
                try
                {
                    if (!_spotifyModule.GetSnapshot().Authenticated)
                    {
                        await _spotifyModule.ConnectAsync(CancellationToken.None);
                    }

                    await _spotifyModule.StartPlaylistAsync(
                        _settings.Spotify.StartPlaylistUri,
                        applyConfiguredStartVolume: true);
                    AddDashboardNotification("Spotify-Endmusik wurde gestartet.", "Info");
                }
                catch (Exception spotifyException)
                {
                    AddDashboardNotification($"Spotify-Endmusik konnte nicht gestartet werden: {spotifyException.Message}", "Warnung");
                }
            }

            var endSeconds = Math.Max(0, _settings.Twitch.EndSceneDurationSeconds);
            for (var remaining = endSeconds; remaining > 0; remaining--)
            {
                DashboardWorkflowStageText.Text = $"ENDSZENE · Streamende in {remaining}s";
                await Task.Delay(1000);
            }

            if (_settings.Twitch.RaidOnStreamEnd &&
                !string.IsNullOrWhiteSpace(_settings.Twitch.SelectedRaidChannel))
            {
                var raidChannel = _settings.Twitch.SelectedRaidChannel.Trim();
                DashboardWorkflowStageText.Text = $"RAID-ZIEL PRÜFEN · {raidChannel}";
                AddDashboardNotification($"Automatischer Raid wird vorbereitet: {raidChannel}", "Info");

                var raidStatus = await _twitchModule.GetRaidTargetStatusAsync(raidChannel);
                if (raidStatus is null)
                {
                    AddDashboardNotification($"Raid abgebrochen: Kanal nicht gefunden.", "Fehler");
                }
                else if (!raidStatus.IsOnline)
                {
                    AddDashboardNotification($"Raid nicht ausgeführt: {raidStatus.DisplayName} ist offline.", "Warnung");
                    DashboardWorkflowStageText.Text = "RAID-ZIEL OFFLINE · STREAM WIRD BEENDET";
                }
                else
                {
                    SetWorkflowVisualStage("Raid", $"Raid zu {raidStatus.DisplayName} wird gestartet.");
                    DashboardWorkflowStageText.Text =
                        $"RAID → {raidStatus.DisplayName} · {raidStatus.ViewerCount} Zuschauer · {raidStatus.GameName}";
                    SetRaidTargetStatusText(
                        $"{raidStatus.DisplayName} ist ONLINE · {raidStatus.ViewerCount} Zuschauer · {raidStatus.GameName}" +
                        (string.IsNullOrWhiteSpace(raidStatus.StreamTitle) ? "" : $" · {raidStatus.StreamTitle}"));

                    await _twitchModule.StartRaidAsync(raidChannel);
                    AddDashboardNotification($"Twitch-Raid zu {raidStatus.DisplayName} wurde gestartet.", "Info");
                    var raidCompleted = await RunRaidCountdownAsync(
                        raidStatus.DisplayName,
                        Math.Clamp(_settings.Twitch.RaidCountdownSeconds, 5, 300));
                    if (!raidCompleted)
                    {
                        return;
                    }

                    if (_settings.Twitch.StopSpotifyAfterRaid)
                    {
                        try
                        {
                            await _spotifyModule.PauseAsync();
                        }
                        catch (Exception spotifyException)
                        {
                            AddDashboardNotification($"Spotify konnte nach dem Raid nicht pausiert werden: {spotifyException.Message}", "Warnung");
                        }
                    }

                    if (!_settings.Twitch.StopStreamAfterRaid)
                    {
                        DashboardWorkflowStageText.Text = "RAID AUSGEFÜHRT · STREAM LÄUFT WEITER";
                        AddDashboardNotification("Raid wurde ausgeführt. Automatisches Streamende ist deaktiviert.", "Info");
                        return;
                    }
                }
            }

            // Erst unmittelbar vor dem tatsächlichen OBS-Stopp wird die
            // Streamdauer eingefroren. So zählt die komplette Endszene mit.
            await UpdateCurrentStreamStatsForEndSceneAsync(finalize: true);
            await _obsClient.StopStreamAsync();

            if (!string.IsNullOrWhiteSpace(_settings.Obs.StartScene))
            {
                await _obsClient.SetCurrentProgramSceneAsync(_settings.Obs.StartScene);
            }

            if (_settings.Workflow.PauseSpotifyOnStreamEnd)
            {
                try
                {
                    await _spotifyModule.PauseAsync();
                    AddDashboardNotification("Spotify wurde nach dem Streamende pausiert.", "Info");
                }
                catch (Exception spotifyException)
                {
                    AddDashboardNotification($"Spotify konnte nach dem Streamende nicht pausiert werden: {spotifyException.Message}", "Warnung");
                }
            }

            _currentLiveViewerCount = 0;
            DashboardHeroViewerText.Text = "0 Zuschauer";
            if (_settings.Dashboard.AutoExitFocusModeOnStreamEnd &&
                _dashboardFocusModeActive)
            {
                ExitDashboardFocusMode();
            }
            await SaveCurrentStreamHistoryAsync();
            DashboardWorkflowStageText.Text = _settings.Twitch.RaidOnStreamEnd
                ? "STREAM BEENDET · RAID-ZIEL PRÜFEN"
                : "STREAM BEENDET";
            AddDashboardNotification($"OBS-Stream wurde beendet.", "Info");
            await Task.Delay(500);
            await RefreshObsAsync();
            await LoadStreamHistoryAsync();
            await RefreshStatisticsAsync();
        }
        catch (Exception exception)
        {
            AddDashboardNotification($"Streamende fehlgeschlagen: {exception.Message}", "Fehler");
            MessageBox.Show(
                exception.Message,
                "Streamende fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task RunDiagnosticsAsync()
    {
        try
        {
            DiagnosticsGrid.ItemsSource =
                await _diagnostics.RunAsync();

            _appLogger.Write(
                AppLogLevel.Information,
                "Diagnostics",
                "Moduldiagnose wurde ausgeführt.");
        }
        catch (Exception exception)
        {
            _appLogger.Write(
                AppLogLevel.Error,
                "Diagnostics",
                "Moduldiagnose ist fehlgeschlagen.",
                exception);

            throw;
        }
    }

    private Task ValidateSettingsAsync()
    {
        var report = _settingsValidator.Validate(_settings);
        ValidationGrid.ItemsSource = report.Issues;

        _appLogger.Write(
            report.IsValid
                ? AppLogLevel.Information
                : AppLogLevel.Warning,
            "Validation",
            report.IsValid
                ? "Konfiguration ist gültig."
                : $"Konfiguration enthält {report.Issues.Count} Hinweise.");

        return Task.CompletedTask;
    }

    private async Task RunConnectionWatchdogAsync()
    {
        if (_connectionWatchdogRunning ||
            !_settings.General.ConnectionWatchdogEnabled)
        {
            return;
        }

        _connectionWatchdogRunning = true;

        try
        {
            if (_settings.General.ReconnectObs &&
                (_settings.Obs.AutoConnect || _settings.Obs.ConnectOnPrepare) &&
                !_obsClient.IsConnected &&
                CanAttemptReconnect("OBS"))
            {
                MarkReconnectAttempt("OBS");
                AddDashboardNotification(
                    "OBS-Verbindung unterbrochen. Automatische Wiederverbindung wird versucht.",
                    "Warnung");

                await ConnectObsAsync(showErrorDialog: false);

                if (_obsClient.IsConnected)
                {
                    AddDashboardNotification(
                        "OBS wurde automatisch wieder verbunden.",
                        "Info");
                }
            }

            var twitchConnected =
                _twitchModule.GetSnapshot().Authenticated;

            if (_settings.General.ReconnectTwitch &&
                (_settings.Twitch.AutoConnect || _settings.Twitch.ConnectOnPrepare) &&
                !twitchConnected &&
                !string.IsNullOrWhiteSpace(_settings.Twitch.ClientId) &&
                CanAttemptReconnect("Twitch"))
            {
                MarkReconnectAttempt("Twitch");
                AddDashboardNotification(
                    "Twitch-Verbindung unterbrochen. Automatische Wiederverbindung wird versucht.",
                    "Warnung");

                await ConnectTwitchAsync(showErrorDialog: false);

                if (_twitchModule.GetSnapshot().Authenticated)
                {
                    AddDashboardNotification(
                        "Twitch wurde automatisch wieder verbunden.",
                        "Info");
                }
            }

            var spotifyConnected =
                _spotifyModule.GetSnapshot().Authenticated;

            if (_settings.General.ReconnectSpotify &&
                (_settings.Spotify.AutoConnect || _settings.Spotify.ConnectOnPrepare) &&
                !spotifyConnected &&
                !string.IsNullOrWhiteSpace(_settings.Spotify.ClientId) &&
                CanAttemptReconnect("Spotify"))
            {
                MarkReconnectAttempt("Spotify");
                AddDashboardNotification(
                    "Spotify-Verbindung unterbrochen. Automatische Wiederverbindung wird versucht.",
                    "Warnung");

                await ConnectSpotifyAsync(showErrorDialog: false);

                if (_spotifyModule.GetSnapshot().Authenticated)
                {
                    AddDashboardNotification(
                        "Spotify wurde automatisch wieder verbunden.",
                        "Info");
                }
            }

            var streamerBotConnected =
                _streamerBotSocket is not null &&
                _streamerBotSocket.State ==
                    System.Net.WebSockets.WebSocketState.Open;

            if (_settings.General.ReconnectStreamerBot &&
                (_settings.StreamerBot.AutoConnect ||
                 _settings.StreamerBot.ConnectOnPrepare) &&
                !streamerBotConnected &&
                CanAttemptReconnect("Streamer.bot"))
            {
                MarkReconnectAttempt("Streamer.bot");
                AddDashboardNotification(
                    "Streamer.bot-Verbindung unterbrochen. Automatische Wiederverbindung wird versucht.",
                    "Warnung");

                await ConnectStreamerBotAsync();

                if (_streamerBotSocket is not null &&
                    _streamerBotSocket.State ==
                        System.Net.WebSockets.WebSocketState.Open)
                {
                    AddDashboardNotification(
                        "Streamer.bot wurde automatisch wieder verbunden.",
                        "Info");
                }
            }
        }
        catch (Exception exception)
        {
            _appLogger.Write(
                AppLogLevel.Warning,
                "ConnectionWatchdog",
                "Verbindungsüberwachung konnte einen Dienst nicht wiederherstellen.",
                exception);
        }
        finally
        {
            _connectionWatchdogRunning = false;
        }
    }

    private bool CanAttemptReconnect(string serviceName)
    {
        if (!_lastReconnectAttempt.TryGetValue(
                serviceName,
                out var lastAttempt))
        {
            return true;
        }

        var cooldown = TimeSpan.FromSeconds(
            Math.Max(
                10,
                _settings.General.ConnectionWatchdogSeconds * 2));

        return DateTimeOffset.Now - lastAttempt >= cooldown;
    }

    private void MarkReconnectAttempt(string serviceName)
    {
        _lastReconnectAttempt[serviceName] =
            DateTimeOffset.Now;
    }

    private async Task RefreshRuntimeHealthAsync()
    {
        RuntimeHealthGrid.ItemsSource =
            await _runtimeHealthService.CheckAsync();
    }

    private async Task RefreshDiagnosticsPageSafelyAsync()
    {
        // A defect in one diagnostics module must never close the complete application.
        await RunDiagnosticsStepSafelyAsync("Moduldiagnose", RunDiagnosticsAsync);
        await RunDiagnosticsStepSafelyAsync("Konfigurationsprüfung", ValidateSettingsAsync);
        await RunDiagnosticsStepSafelyAsync("Laufzeitprüfung", RefreshRuntimeHealthAsync);
        await RunDiagnosticsStepSafelyAsync("Protokolle", RefreshLogsAsync);
        await RunDiagnosticsStepSafelyAsync("Beta-Readiness", RefreshBetaReadinessAsync);
    }

    private async Task RunDiagnosticsStepSafelyAsync(string stepName, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _appLogger.Write(
                AppLogLevel.Error,
                "Diagnostics",
                $"{stepName} konnte nicht geladen werden.",
                ex);

            // Keep the diagnostics page usable and show the error as a log entry.
            _visibleLogs.Insert(0, new AppLogEntry(
                DateTimeOffset.Now,
                AppLogLevel.Error,
                "Diagnostics",
                $"{stepName} konnte nicht geladen werden: {ex.Message}",
                ex.ToString(),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
        }
    }

    private async Task RefreshLogsAsync()
    {
        if (_logsPaused)
        {
            return;
        }

        var entries = await _appLogger.ReadRecentAsync(1000);
        var validEntries = entries.Where(IsUsableLogEntry).ToList();
        var filtered = validEntries.Where(LogMatchesFilter).ToList();

        _visibleLogs.Clear();

        foreach (var entry in filtered)
        {
            _visibleLogs.Add(entry);
        }

        await RefreshSpotifyInspectorAsync(validEntries);
    }

    private async Task RefreshSpotifyInspectorAsync(IReadOnlyList<AppLogEntry>? suppliedEntries = null)
    {
        var entries = suppliedEntries ?? await _appLogger.ReadRecentAsync(2000);
        var spotifyEntries = entries
            .Where(IsUsableLogEntry)
            .Where(entry => entry.Category.StartsWith("Spotify.", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.Timestamp)
            .ToList();

        var oneMinuteAgo = DateTimeOffset.Now.AddMinutes(-1);
        SpotifyInspectorRequestsPerMinuteText.Text = spotifyEntries.Count(entry => entry.Timestamp >= oneMinuteAgo).ToString();

        var methodSummary = spotifyEntries
            .Select(ToSpotifyInspectorRow)
            .Where(row => row.Time != string.Empty)
            .GroupBy(row => string.IsNullOrWhiteSpace(row.Method) ? "–" : row.Method, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => $"{group.Key}: {group.Count()}")
            .Take(6);
        SpotifyInspectorTypeSummaryText.Text = spotifyEntries.Count == 0
            ? "Noch keine Aufrufe."
            : string.Join(" · ", methodSummary);

        var latest = spotifyEntries.FirstOrDefault();
        SpotifyInspectorLastStatusText.Text = latest is null
            ? "Noch keine Anfrage"
            : GetProperty(latest, "statusCode", latest.Level.ToString());
        SpotifyInspectorRetryAfterText.Text = latest is null
            ? "–"
            : FormatRetryAfter(GetProperty(latest, "retryAfterSeconds", "none"));

        var filter = (SpotifyInspectorFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";
        var rows = spotifyEntries.Select(ToSpotifyInspectorRow);
        rows = filter switch
        {
            "GET" => rows.Where(row => string.Equals(row.Method, "GET", StringComparison.OrdinalIgnoreCase)),
            "WRITE" => rows.Where(row => row.Method is "POST" or "PUT" or "PATCH" or "DELETE"),
            "ERROR" => rows.Where(row => !int.TryParse(row.Status, out var code) || code >= 400),
            "OAUTH" => rows.Where(row => string.Equals(row.Category, "OAuth", StringComparison.OrdinalIgnoreCase)),
            _ => rows
        };

        _spotifyInspectorRows.Clear();
        foreach (var row in rows.Take(100))
        {
            _spotifyInspectorRows.Add(row);
        }
    }

    private static SpotifyApiInspectorRow ToSpotifyInspectorRow(AppLogEntry entry)
    {
        var endpoint = GetProperty(entry, "endpoint", "");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            endpoint = InferEndpointFromMessage(entry.Message);
        }
        var operation = GetProperty(entry, "operation", "");
        var method = GetProperty(entry, "method", "");
        if (string.IsNullOrWhiteSpace(method))
        {
            method = !string.IsNullOrWhiteSpace(operation)
                ? operation
                : InferMethodFromMessage(entry.Message);
        }

        return new SpotifyApiInspectorRow(
            entry.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
            entry.Category.EndsWith("OAuth", StringComparison.OrdinalIgnoreCase) ? "OAuth" : "Web API",
            InferSpotifyRequestOrigin(entry.Category, method, endpoint),
            method,
            endpoint,
            GetProperty(entry, "statusCode", entry.Level.ToString()),
            GetProperty(entry, "durationMs", "–") is var duration && duration != "–" ? duration + " ms" : "–",
            FormatRetryAfter(GetProperty(entry, "retryAfterSeconds", "none")),
            entry.Message);
    }

    private static string InferSpotifyRequestOrigin(string category, string method, string endpoint)
    {
        if (category.EndsWith("OAuth", StringComparison.OrdinalIgnoreCase))
        {
            return "Verbindung";
        }

        if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint.Contains("/me/player", StringComparison.OrdinalIgnoreCase)
                ? "Statusabfrage"
                : "Datenabfrage";
        }

        if (endpoint.Contains("/player", StringComparison.OrdinalIgnoreCase))
        {
            return "Steuerbefehl";
        }

        return "API-Aufruf";
    }

    private static string InferMethodFromMessage(string message)
    {
        foreach (var method in new[] { "GET", "POST", "PUT", "DELETE", "PATCH" })
        {
            if (message.Contains(method + " ", StringComparison.OrdinalIgnoreCase))
            {
                return method;
            }
        }
        return "–";
    }

    private static string InferEndpointFromMessage(string message)
    {
        var marker = message.IndexOf("/v1/", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return "–";
        }

        var end = message.IndexOf(" ->", marker, StringComparison.OrdinalIgnoreCase);
        return end > marker ? message[marker..end].Trim() : message[marker..].Trim();
    }

    private static bool IsUsableLogEntry(AppLogEntry? entry)
        => entry is not null &&
           !string.IsNullOrWhiteSpace(entry.Category) &&
           !string.IsNullOrWhiteSpace(entry.Message);

    private static string GetProperty(AppLogEntry entry, string key, string fallback)
        => entry.Properties is not null &&
           entry.Properties.TryGetValue(key, out var value) &&
           !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;

    private static string FormatRetryAfter(string value)
        => int.TryParse(value, out var seconds) && seconds > 0
            ? $"{seconds} Sek."
            : "–";

    private void CopySelectedSpotifyInspectorEntry()
    {
        if (SpotifyInspectorGrid.SelectedItem is not SpotifyApiInspectorRow row)
        {
            return;
        }

        Clipboard.SetText($"{row.Time} | {row.Category} | {row.Origin} | {row.Method} | {row.Endpoint} | {row.Status} | {row.Duration} | Retry-After: {row.RetryAfter}\n{row.Message}");
    }

    private sealed record SpotifyApiInspectorRow(
        string Time,
        string Category,
        string Origin,
        string Method,
        string Endpoint,
        string Status,
        string Duration,
        string RetryAfter,
        string Message);

    private bool LogMatchesFilter(AppLogEntry entry)
    {
        var search = LogSearchBox.Text.Trim();

        if (!string.IsNullOrWhiteSpace(search) &&
            !entry.Message.Contains(
                search,
                StringComparison.OrdinalIgnoreCase) &&
            !entry.Category.Contains(
                search,
                StringComparison.OrdinalIgnoreCase) &&
            !(entry.Exception?.Contains(
                search,
                StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return false;
        }

        var selected =
            (LogLevelFilterBox.SelectedItem
                as System.Windows.Controls.ComboBoxItem)
                ?.Content
                ?.ToString()
            ?? "Alle";

        return selected == "Alle" ||
               string.Equals(
                   selected,
                   entry.Level.ToString(),
                   StringComparison.OrdinalIgnoreCase);
    }

    private void CopySelectedLog()
    {
        if (LogsGrid.SelectedItem is not AppLogEntry entry)
        {
            return;
        }

        Clipboard.SetText(
            $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} " +
            $"[{entry.Level}] {entry.Category}: {entry.Message}" +
            (string.IsNullOrWhiteSpace(entry.Exception)
                ? ""
                : Environment.NewLine + entry.Exception));
    }

    private async Task ExportLogsAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Textdatei (*.txt)|*.txt",
            FileName =
                "CreatorControlSuite-Logs-" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss") +
                ".txt"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await _appLogger.ExportAsync(dialog.FileName);

        MessageBox.Show(
            "Logs wurden exportiert.",
            "Creator Control Suite",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static void ShowNotImplemented(string feature)
    {
        MessageBox.Show(
            $"{feature} ist in dieser Alpha bereits in der Oberfläche vorbereitet und wird im nächsten Modul-Meilenstein produktiv angeschlossen.",
            "Creator Control Suite",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private int _obsSceneItemsRefreshVersion;
    private async Task RefreshServicesObsSceneItemsAsync()
    {
        var refreshVersion = ++_obsSceneItemsRefreshVersion;
        if (!_obsClient.IsConnected || ServicesObsScenesList.SelectedItem is not ObsSceneInfo scene)
        { ServicesObsSceneItemsList.ItemsSource=null; ServicesObsSceneItemsList.SelectedItem=null; ServicesObsShowSceneItemButton.IsEnabled=false; ServicesObsHideSceneItemButton.IsEnabled=false;
          ServicesObsLockSceneItemButton.IsEnabled=false; ServicesObsUnlockSceneItemButton.IsEnabled=false;
          ServicesObsMoveSceneItemUpButton.IsEnabled=false; ServicesObsMoveSceneItemDownButton.IsEnabled=false; SetObsSceneItemTransformControlsEnabled(false); ClearObsSourceFilters("Zuerst eine Quelle auswählen."); ServicesObsSelectedSceneItemStateText.Text="Zuerst eine Szene auswählen."; return; }
        var selectedSourceName=(ServicesObsSceneItemsList.SelectedItem as ObsSceneItemInfo)?.SourceName;
        ServicesObsSelectedSceneItemStateText.Text=$"Quellen für „{scene.Name}“ werden geladen …";
        try
        {
            var items=await _obsClient.GetSceneItemListAsync(scene.Name);
            if (refreshVersion!=_obsSceneItemsRefreshVersion || ServicesObsScenesList.SelectedItem is not ObsSceneInfo currentScene || !string.Equals(currentScene.Name,scene.Name,StringComparison.OrdinalIgnoreCase)) return;
            _servicesObsSceneItems = items;
            ApplyServicesObsSourceFilter();
            if (!string.IsNullOrWhiteSpace(selectedSourceName)) ServicesObsSceneItemsList.SelectedItem=items.FirstOrDefault(item=>string.Equals(item.SourceName,selectedSourceName,StringComparison.OrdinalIgnoreCase));
            var valid=ServicesObsSceneItemsList.SelectedItem is ObsSceneItemInfo; ServicesObsShowSceneItemButton.IsEnabled=valid; ServicesObsHideSceneItemButton.IsEnabled=valid;
            ServicesObsLockSceneItemButton.IsEnabled=valid; ServicesObsUnlockSceneItemButton.IsEnabled=valid;
            ServicesObsMoveSceneItemUpButton.IsEnabled=valid; ServicesObsMoveSceneItemDownButton.IsEnabled=valid; SetObsSceneItemTransformControlsEnabled(valid);
            ServicesObsSelectedSceneItemStateText.Text=$"{items.Count} Quellen in „{scene.Name}“";
        }
        catch(Exception exception)
        { if(refreshVersion!=_obsSceneItemsRefreshVersion)return; ServicesObsSceneItemsList.ItemsSource=null; ServicesObsSceneItemsList.SelectedItem=null; ServicesObsShowSceneItemButton.IsEnabled=false; ServicesObsHideSceneItemButton.IsEnabled=false;
          ServicesObsLockSceneItemButton.IsEnabled=false; ServicesObsUnlockSceneItemButton.IsEnabled=false;
          ServicesObsMoveSceneItemUpButton.IsEnabled=false; ServicesObsMoveSceneItemDownButton.IsEnabled=false; SetObsSceneItemTransformControlsEnabled(false); ClearObsSourceFilters("Filter konnten nicht geladen werden."); ServicesObsSelectedSceneItemStateText.Text=$"Quellen konnten nicht geladen werden: {exception.Message}"; }
    }
    private async Task RefreshSelectedObsSceneItemStateAsync()
    {
        var valid=ServicesObsSceneItemsList.SelectedItem is ObsSceneItemInfo; ServicesObsShowSceneItemButton.IsEnabled=valid; ServicesObsHideSceneItemButton.IsEnabled=valid;
            ServicesObsLockSceneItemButton.IsEnabled=valid; ServicesObsUnlockSceneItemButton.IsEnabled=valid;
            ServicesObsMoveSceneItemUpButton.IsEnabled=valid; ServicesObsMoveSceneItemDownButton.IsEnabled=valid; SetObsSceneItemTransformControlsEnabled(valid);
        if(ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo item){ ServicesObsRestartMediaButton.IsEnabled = false;
            ServicesObsStopMediaButton.IsEnabled = false;
            ServicesObsRefreshBrowserButton.IsEnabled = false; ClearObsSourceFilters("Quelle auswählen, um Filter zu laden."); if(ServicesObsScenesList.SelectedItem is ObsSceneInfo scene) ServicesObsSelectedSceneItemStateText.Text=$"Quelle in „{scene.Name}“ auswählen."; return; }
        ServicesObsLockSceneItemButton.IsEnabled = !item.Locked;
        ServicesObsUnlockSceneItemButton.IsEnabled = item.Locked;
        var itemCount = (ServicesObsSceneItemsList.ItemsSource as IEnumerable<ObsSceneItemInfo>)?.Count() ?? 0;
        ServicesObsMoveSceneItemUpButton.IsEnabled = item.Index < Math.Max(0, itemCount - 1);
        ServicesObsMoveSceneItemDownButton.IsEnabled = item.Index > 0;
        ServicesObsSelectedSceneItemStateText.Text=$"{item.SourceName}: {(item.Enabled?"sichtbar":"ausgeblendet")} · {(item.Locked?"gesperrt":"entsperrt")} · Ebene {item.Index}"+(item.IsGroup?" · Gruppe":string.Empty);
        ServicesObsRestartMediaButton.IsEnabled = _obsClient.IsConnected && IsRestartableObsMediaSource(item.SourceType);
        ServicesObsStopMediaButton.IsEnabled = _obsClient.IsConnected && IsRestartableObsMediaSource(item.SourceType);
        ServicesObsRefreshBrowserButton.IsEnabled = _obsClient.IsConnected && IsObsBrowserSource(item.SourceType);
        await LoadSelectedObsSceneItemTransformAsync(showNotification: false);
        await RefreshSelectedObsSourceFiltersAsync();
    }


    private void ApplyServicesObsSceneFilter()
    {
        if (ServicesObsScenesList is null) return;
        var selectedName = (ServicesObsScenesList.SelectedItem as ObsSceneInfo)?.Name;
        var search = ServicesObsSceneSearchBox?.Text?.Trim() ?? string.Empty;
        var filtered = _servicesObsScenes
            .Where(scene => string.IsNullOrWhiteSpace(search) || scene.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(scene => string.Equals(scene.Name, _servicesObsCurrentScene, StringComparison.OrdinalIgnoreCase))
            .ThenBy(scene => scene.Index)
            .ToList();
        ServicesObsScenesList.ItemsSource = filtered;
        ServicesObsScenesList.SelectedItem = filtered.FirstOrDefault(scene => string.Equals(scene.Name, selectedName, StringComparison.OrdinalIgnoreCase))
            ?? filtered.FirstOrDefault(scene => string.Equals(scene.Name, _servicesObsCurrentScene, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyServicesObsSourceFilter()
    {
        if (ServicesObsSceneItemsList is null) return;
        var selectedName = (ServicesObsSceneItemsList.SelectedItem as ObsSceneItemInfo)?.SourceName;
        var search = ServicesObsSourceSearchBox?.Text?.Trim() ?? string.Empty;
        var filtered = _servicesObsSceneItems
            .Where(item => string.IsNullOrWhiteSpace(search) || item.SourceName.Contains(search, StringComparison.OrdinalIgnoreCase) || item.SourceType.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();
        ServicesObsSceneItemsList.ItemsSource = filtered;
        ServicesObsSceneItemsList.SelectedItem = filtered.FirstOrDefault(item => string.Equals(item.SourceName, selectedName, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyServicesObsInputFilter()
    {
        if (ServicesObsInputsList is null) return;
        var selectedName = (ServicesObsInputsList.SelectedItem as ObsInputInfo)?.Name;
        var search = ServicesObsInputSearchBox?.Text?.Trim() ?? string.Empty;
        var mode = (ServicesObsInputFilterBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
        var filtered = _servicesObsInputs
            .Where(input => string.IsNullOrWhiteSpace(search) || input.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || input.Kind.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Where(input => mode switch
            {
                "muted" => IsObsInputMuted(input.Name),
                "all" => true,
                _ => string.Equals(ClassifyObsAudioInput(input), mode, StringComparison.OrdinalIgnoreCase)
            })
            .OrderBy(input => ClassifyObsAudioInput(input))
            .ThenBy(input => input.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ServicesObsInputsList.ItemsSource = filtered;
        ServicesObsInputsList.SelectedItem = filtered.FirstOrDefault(input => string.Equals(input.Name, selectedName, StringComparison.OrdinalIgnoreCase)) ?? filtered.FirstOrDefault();
    }

    private static string ClassifyObsAudioInput(ObsInputInfo input)
    {
        var value = $"{input.Name} {input.Kind} {input.UnversionedKind}".ToLowerInvariant();
        if (value.Contains("mic") || value.Contains("mikro") || value.Contains("yeti") || value.Contains("rode") || value.Contains("voice")) return "microphone";
        if (value.Contains("spotify") || value.Contains("music") || value.Contains("musik")) return "music";
        if (value.Contains("browser") || value.Contains("alert") || value.Contains("streamelements")) return "browser";
        return "game";
    }

    private bool IsObsInputMuted(string inputName) =>
        _servicesObsInputsMuted.TryGetValue(inputName, out var muted) && muted;

    private readonly Dictionary<string, bool> _servicesObsInputsMuted = new(StringComparer.OrdinalIgnoreCase);

    private void UpdateObsLiveMeters(IReadOnlyList<ObsInputVolumeMeter> meters)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var meter in meters)
        {
            _obsLiveMeters[meter.InputName] = meter;
            if (!_obsPeakHold.TryGetValue(meter.InputName, out var held) || meter.PeakDb >= held.PeakDb || now - held.At > TimeSpan.FromSeconds(2))
                _obsPeakHold[meter.InputName] = (meter.PeakDb, now);
        }
        if (ServicesObsInputsList.SelectedItem is not ObsInputInfo selected || !_obsLiveMeters.TryGetValue(selected.Name, out var current)) return;
        var heldPeak = _obsPeakHold.TryGetValue(selected.Name, out var peak) ? peak.PeakDb : current.PeakDb;
        ServicesObsLiveMeterBar.Value = Math.Clamp(current.MagnitudeDb, -60, 10);
        ServicesObsLiveMeterText.Text = $"Live-Pegel: {current.MagnitudeDb:0.0} dB · Peak {current.PeakDb:0.0} dB";
        ServicesObsPeakHoldText.Text = $"Peak-Hold: {heldPeak:0.0} dB" + (heldPeak >= -0.1 ? " · CLIPPING" : string.Empty);
    }

    private async Task SetObsInputsMuteAsync(IEnumerable<ObsInputInfo> inputs, bool muted, string label)
    {
        if (!_obsClient.IsConnected) { AddDashboardNotification("OBS ist nicht verbunden.", "Warnung"); return; }
        var applied = 0;
        foreach (var input in inputs)
        {
            try { await _obsClient.SetInputMuteAsync(input.Name, muted); _servicesObsInputsMuted[input.Name] = muted; applied++; } catch { }
        }
        ApplyServicesObsInputFilter();
        AddDashboardNotification($"{label}: {applied} Quellen {(muted ? "gemutet" : "entmutet")}.", "Info");
    }

    private async Task SoloObsAudioCategoryAsync(string category)
    {
        foreach (var input in _servicesObsInputs)
        {
            try
            {
                var muted = !string.Equals(ClassifyObsAudioInput(input), category, StringComparison.OrdinalIgnoreCase);
                await _obsClient.SetInputMuteAsync(input.Name, muted);
                _servicesObsInputsMuted[input.Name] = muted;
            }
            catch { }
        }
        ApplyServicesObsInputFilter();
        AddDashboardNotification(category == "microphone" ? "Nur Mikrofone sind aktiv." : "Nur Spiel/Desktop ist aktiv.", "Info");
    }

    private string SelectedObsAudioGroup() => (ServicesObsAudioGroupBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "game";

    private async Task SetSelectedObsAudioGroupMuteAsync(bool muted)
    {
        var group = SelectedObsAudioGroup();
        await SetObsInputsMuteAsync(_servicesObsInputs.Where(input => ClassifyObsAudioInput(input) == group), muted, "Audiogruppe");
    }

    private async Task ApplyObsAudioGroupVolumeAsync()
    {
        if (!double.TryParse(ServicesObsGroupVolumeBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var db))
        {
            AddDashboardNotification("Ungültiger Gruppenpegel.", "Warnung");
            return;
        }
        db = Math.Clamp(db, -100, 26);
        var group = SelectedObsAudioGroup();
        var applied = 0;
        foreach (var input in _servicesObsInputs.Where(input => ClassifyObsAudioInput(input) == group))
        {
            try { await _obsClient.SetInputVolumeDbAsync(input.Name, db); applied++; } catch { }
        }
        AddDashboardNotification($"Gruppenpegel auf {db:0.0} dB gesetzt ({applied} Quellen).", "Info");
        await RefreshSelectedObsInputStateAsync();
    }

    private static bool IsRestartableObsMediaSource(string sourceType) =>
        sourceType.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase) ||
        sourceType.Contains("vlc", StringComparison.OrdinalIgnoreCase) ||
        sourceType.Contains("media", StringComparison.OrdinalIgnoreCase);

    private static bool IsObsBrowserSource(string sourceType) =>
        sourceType.Contains("browser", StringComparison.OrdinalIgnoreCase);

    private async Task RestartSelectedObsMediaInputAsync()
    {
        if (!_obsClient.IsConnected || ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo item)
        {
            AddDashboardNotification("Keine OBS-Medienquelle ausgewählt.", "Warnung");
            return;
        }

        try
        {
            await _obsClient.RestartMediaInputAsync(item.SourceName);
            AddDashboardNotification($"OBS-Medienquelle „{item.SourceName}“ wurde neu gestartet.", "Info");
        }
        catch (Exception exception)
        {
            AddDashboardNotification($"Medienquelle konnte nicht neu gestartet werden: {exception.Message}", "Fehler");
        }
    }

    private async Task StopSelectedObsMediaInputAsync()
    {
        if (!_obsClient.IsConnected || ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo item)
        {
            AddDashboardNotification("Keine OBS-Medienquelle ausgewählt.", "Warnung");
            return;
        }

        try
        {
            await _obsClient.StopMediaInputAsync(item.SourceName);
            AddDashboardNotification($"OBS-Medienquelle „{item.SourceName}“ wurde gestoppt.", "Info");
        }
        catch (Exception exception)
        {
            AddDashboardNotification($"Medienquelle konnte nicht gestoppt werden: {exception.Message}", "Fehler");
        }
    }

    private async Task RefreshSelectedObsBrowserInputAsync()
    {
        if (!_obsClient.IsConnected || ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo item)
        {
            AddDashboardNotification("Keine OBS-Browserquelle ausgewählt.", "Warnung");
            return;
        }

        try
        {
            await _obsClient.PressInputPropertiesButtonAsync(item.SourceName, "refreshnocache");
            AddDashboardNotification($"OBS-Browserquelle „{item.SourceName}“ wurde ohne Cache neu geladen.", "Info");
        }
        catch (Exception exception)
        {
            AddDashboardNotification($"Browserquelle konnte nicht neu geladen werden: {exception.Message}", "Fehler");
        }
    }

    private void ClearObsSourceFilters(string state)
    {
        ServicesObsSourceFiltersList.ItemsSource = null;
        ServicesObsSourceFiltersList.SelectedItem = null;
        ServicesObsEnableSourceFilterButton.IsEnabled = false;
        ServicesObsDisableSourceFilterButton.IsEnabled = false;
        ServicesObsRefreshSourceFiltersButton.IsEnabled = ServicesObsSceneItemsList.SelectedItem is ObsSceneItemInfo && _obsClient.IsConnected;
        ServicesObsSourceFilterStateText.Text = state;
    }

    private async Task RefreshSelectedObsSourceFiltersAsync()
    {
        if (!_obsClient.IsConnected || ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo item)
        {
            ClearObsSourceFilters("Quelle auswählen, um Filter zu laden.");
            return;
        }

        var selectedFilterName = (ServicesObsSourceFiltersList.SelectedItem as ObsSourceFilterInfo)?.Name;
        ServicesObsRefreshSourceFiltersButton.IsEnabled = true;
        ServicesObsSourceFilterStateText.Text = $"Filter für „{item.SourceName}“ werden geladen …";
        try
        {
            var filters = await _obsClient.GetSourceFilterListAsync(item.SourceName);
            if (ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo current || !string.Equals(current.SourceName, item.SourceName, StringComparison.OrdinalIgnoreCase))
                return;

            ServicesObsSourceFiltersList.ItemsSource = filters;
            ServicesObsSourceFiltersList.SelectedItem = !string.IsNullOrWhiteSpace(selectedFilterName)
                ? filters.FirstOrDefault(filter => string.Equals(filter.Name, selectedFilterName, StringComparison.OrdinalIgnoreCase))
                : filters.FirstOrDefault();
            ServicesObsSourceFilterStateText.Text = filters.Count == 0
                ? $"„{item.SourceName}“ hat keine Filter."
                : $"{filters.Count} Filter für „{item.SourceName}“ geladen.";
            RefreshSelectedObsSourceFilterState();
        }
        catch (Exception exception)
        {
            ClearObsSourceFilters($"Filter konnten nicht geladen werden: {exception.Message}");
        }
    }

    private void RefreshSelectedObsSourceFilterState()
    {
        if (ServicesObsSourceFiltersList.SelectedItem is not ObsSourceFilterInfo filter)
        {
            ServicesObsEnableSourceFilterButton.IsEnabled = false;
            ServicesObsDisableSourceFilterButton.IsEnabled = false;
            return;
        }

        ServicesObsEnableSourceFilterButton.IsEnabled = !filter.Enabled;
        ServicesObsDisableSourceFilterButton.IsEnabled = filter.Enabled;
        ServicesObsSourceFilterStateText.Text = $"{filter.Name}: {(filter.Enabled ? "aktiv" : "deaktiviert")} · {filter.Kind}";
    }

    private async Task SetSelectedObsSourceFilterEnabledAsync(bool enabled)
    {
        if (!_obsClient.IsConnected || ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo item || ServicesObsSourceFiltersList.SelectedItem is not ObsSourceFilterInfo filter)
        {
            AddDashboardNotification("OBS-Filter kann nicht geschaltet werden: Quelle oder Filter fehlt.", "Warnung");
            return;
        }

        try
        {
            await _obsClient.SetSourceFilterEnabledAsync(item.SourceName, filter.Name, enabled);
            await RefreshSelectedObsSourceFiltersAsync();
            AddDashboardNotification($"Filter „{filter.Name}“ wurde {(enabled ? "aktiviert" : "deaktiviert")}.", "Info");
        }
        catch (Exception exception)
        {
            ServicesObsSourceFilterStateText.Text = $"Filter konnte nicht geschaltet werden: {exception.Message}";
            AddDashboardNotification(ServicesObsSourceFilterStateText.Text, "Fehler");
        }
    }

    private async Task SetSelectedObsSceneItemVisibilityAsync(bool enabled)
    {
        if(!_obsClient.IsConnected || ServicesObsScenesList.SelectedItem is not ObsSceneInfo scene || ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo item){ AddDashboardNotification("OBS-Quelle kann nicht geschaltet werden: Szene oder Quelle fehlt.","Warnung"); return; }
        try
        {
            var currentItems=await _obsClient.GetSceneItemListAsync(scene.Name);
            var currentItem=currentItems.FirstOrDefault(candidate=>string.Equals(candidate.SourceName,item.SourceName,StringComparison.OrdinalIgnoreCase));
            if(currentItem is null){ AddDashboardNotification($"OBS-Quelle „{item.SourceName}“ existiert in „{scene.Name}“ nicht mehr.","Warnung"); await RefreshServicesObsSceneItemsAsync(); return; }
            await _obsClient.SetSceneItemEnabledAsync(scene.Name,currentItem.SourceName,enabled); await RefreshServicesObsSceneItemsAsync();
            AddDashboardNotification($"{currentItem.SourceName} wurde in {scene.Name} {(enabled?"eingeblendet":"ausgeblendet")}.","Info");
        }
        catch(Exception exception){ AddDashboardNotification($"OBS-Quelle konnte nicht geschaltet werden: {exception.Message}","Fehler"); }
    }

    private async Task SetSelectedObsSceneItemLockAsync(bool locked)
    {
        if (!_obsClient.IsConnected || ServicesObsScenesList.SelectedItem is not ObsSceneInfo scene || ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo item)
        {
            AddDashboardNotification("OBS-Quelle kann nicht gesperrt werden: Szene oder Quelle fehlt.", "Warnung");
            return;
        }

        try
        {
            await _obsClient.SetSceneItemLockedAsync(scene.Name, item.SourceName, locked);
            await RefreshServicesObsSceneItemsAsync();
            AddDashboardNotification($"{item.SourceName} wurde in {scene.Name} {(locked ? "gesperrt" : "entsperrt")}.", "Info");
        }
        catch (Exception exception)
        {
            AddDashboardNotification($"OBS-Quelle konnte nicht {(locked ? "gesperrt" : "entsperrt")} werden: {exception.Message}", "Fehler");
        }
    }

    private async Task MoveSelectedObsSceneItemAsync(int indexDelta)
    {
        if (!_obsClient.IsConnected || ServicesObsScenesList.SelectedItem is not ObsSceneInfo scene || ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo item)
        {
            AddDashboardNotification("OBS-Quelle kann nicht verschoben werden: Szene oder Quelle fehlt.", "Warnung");
            return;
        }

        try
        {
            var items = await _obsClient.GetSceneItemListAsync(scene.Name);
            var current = items.FirstOrDefault(candidate => candidate.ItemId == item.ItemId)
                ?? items.FirstOrDefault(candidate => string.Equals(candidate.SourceName, item.SourceName, StringComparison.OrdinalIgnoreCase));
            if (current is null)
            {
                await RefreshServicesObsSceneItemsAsync();
                AddDashboardNotification($"OBS-Quelle „{item.SourceName}“ existiert nicht mehr.", "Warnung");
                return;
            }

            var maximumIndex = Math.Max(0, items.Count - 1);
            var targetIndex = Math.Clamp(current.Index + indexDelta, 0, maximumIndex);
            if (targetIndex == current.Index)
                return;

            await _obsClient.SetSceneItemIndexAsync(scene.Name, current.SourceName, targetIndex);
            await RefreshServicesObsSceneItemsAsync();
            AddDashboardNotification($"{current.SourceName} wurde in {scene.Name} eine Ebene {(indexDelta > 0 ? "nach oben" : "nach unten")} verschoben.", "Info");
        }
        catch (Exception exception)
        {
            AddDashboardNotification($"OBS-Quelle konnte nicht verschoben werden: {exception.Message}", "Fehler");
        }
    }

    private void SetObsSceneItemTransformControlsEnabled(bool enabled)
    {
        ServicesObsSceneItemXBox.IsEnabled = enabled;
        ServicesObsSceneItemYBox.IsEnabled = enabled;
        ServicesObsSceneItemWidthBox.IsEnabled = enabled;
        ServicesObsSceneItemHeightBox.IsEnabled = enabled;
        ServicesObsSceneItemRotationBox.IsEnabled = enabled;
        ServicesObsSceneItemCropLeftBox.IsEnabled = enabled;
        ServicesObsSceneItemCropTopBox.IsEnabled = enabled;
        ServicesObsSceneItemCropRightBox.IsEnabled = enabled;
        ServicesObsSceneItemCropBottomBox.IsEnabled = enabled;
        ServicesObsApplySceneItemTransformButton.IsEnabled = enabled;
        ServicesObsReloadSceneItemTransformButton.IsEnabled = enabled;
        ServicesObsResetSceneItemTransformButton.IsEnabled = enabled;
        ServicesObsSceneItemFullscreenButton.IsEnabled = enabled;
        ServicesObsSceneItemCentered720Button.IsEnabled = enabled;
    }

    private static bool TryParseObsTransformValue(string? value, out double result)
    {
        return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out result)
            || double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result);
    }

    private async Task LoadSelectedObsSceneItemTransformAsync(bool showNotification = true)
    {
        if (!_obsClient.IsConnected
            || ServicesObsScenesList.SelectedItem is not ObsSceneInfo scene
            || ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo item)
            return;

        try
        {
            var transform = await _obsClient.GetSceneItemTransformAsync(scene.Name, item.SourceName);
            ServicesObsSceneItemXBox.Text = transform.PositionX.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            ServicesObsSceneItemYBox.Text = transform.PositionY.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            ServicesObsSceneItemWidthBox.Text = transform.Width.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            ServicesObsSceneItemHeightBox.Text = transform.Height.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            ServicesObsSceneItemRotationBox.Text = transform.Rotation.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            ServicesObsSceneItemCropLeftBox.Text = transform.CropLeft.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ServicesObsSceneItemCropTopBox.Text = transform.CropTop.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ServicesObsSceneItemCropRightBox.Text = transform.CropRight.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ServicesObsSceneItemCropBottomBox.Text = transform.CropBottom.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (showNotification)
                AddDashboardNotification($"Transformation von {item.SourceName} wurde aus OBS geladen.", "Info");
        }
        catch (Exception exception)
        {
            if (showNotification)
                AddDashboardNotification($"Transformation konnte nicht geladen werden: {exception.Message}", "Fehler");
        }
    }

    private async Task ResetSelectedObsSceneItemTransformAsync()
    {
        if (!_obsClient.IsConnected
            || ServicesObsScenesList.SelectedItem is not ObsSceneInfo scene
            || ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo item)
            return;

        try
        {
            await _obsClient.ResetSceneItemTransformAsync(scene.Name, item.SourceName);
            await LoadSelectedObsSceneItemTransformAsync(showNotification: false);
            AddDashboardNotification($"Transformation von {item.SourceName} wurde in OBS zurückgesetzt.", "Info");
        }
        catch (Exception exception)
        {
            AddDashboardNotification($"Transformation konnte nicht zurückgesetzt werden: {exception.Message}", "Fehler");
        }
    }

    private async Task ApplySelectedObsSceneItemTransformAsync()
    {
        if (!TryParseObsTransformValue(ServicesObsSceneItemXBox.Text, out var x)
            || !TryParseObsTransformValue(ServicesObsSceneItemYBox.Text, out var y)
            || !TryParseObsTransformValue(ServicesObsSceneItemWidthBox.Text, out var width)
            || !TryParseObsTransformValue(ServicesObsSceneItemHeightBox.Text, out var height)
            || !TryParseObsTransformValue(ServicesObsSceneItemRotationBox.Text, out var rotation)
            || !int.TryParse(ServicesObsSceneItemCropLeftBox.Text, out var cropLeft)
            || !int.TryParse(ServicesObsSceneItemCropTopBox.Text, out var cropTop)
            || !int.TryParse(ServicesObsSceneItemCropRightBox.Text, out var cropRight)
            || !int.TryParse(ServicesObsSceneItemCropBottomBox.Text, out var cropBottom))
        {
            AddDashboardNotification("Transformation enthält ungültige Zahlen.", "Warnung");
            return;
        }

        if (width < 1 || height < 1 || width > 16384 || height > 16384)
        {
            AddDashboardNotification("Breite und Höhe müssen zwischen 1 und 16384 Pixeln liegen.", "Warnung");
            return;
        }
        if (rotation < -3600 || rotation > 3600 || new[] { cropLeft, cropTop, cropRight, cropBottom }.Any(value => value < 0 || value > 16384))
        {
            AddDashboardNotification("Drehung oder Zuschnitt liegt außerhalb des gültigen Bereichs.", "Warnung");
            return;
        }

        await ApplyObsSceneItemTransformAsync(x, y, width, height, rotation, cropLeft, cropTop, cropRight, cropBottom);
    }

    private async Task ApplyObsSceneItemTransformPresetAsync(double x, double y, double width, double height)
    {
        ServicesObsSceneItemXBox.Text = x.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        ServicesObsSceneItemYBox.Text = y.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        ServicesObsSceneItemWidthBox.Text = width.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        ServicesObsSceneItemHeightBox.Text = height.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        ServicesObsSceneItemRotationBox.Text = "0";
        ServicesObsSceneItemCropLeftBox.Text = "0";
        ServicesObsSceneItemCropTopBox.Text = "0";
        ServicesObsSceneItemCropRightBox.Text = "0";
        ServicesObsSceneItemCropBottomBox.Text = "0";
        await ApplyObsSceneItemTransformAsync(x, y, width, height, 0, 0, 0, 0, 0);
    }

    private async Task ApplyObsSceneItemTransformAsync(double x, double y, double width, double height, double rotation, int cropLeft, int cropTop, int cropRight, int cropBottom)
    {
        if (!_obsClient.IsConnected
            || ServicesObsScenesList.SelectedItem is not ObsSceneInfo scene
            || ServicesObsSceneItemsList.SelectedItem is not ObsSceneItemInfo item)
        {
            AddDashboardNotification("OBS-Quelle kann nicht transformiert werden: Szene oder Quelle fehlt.", "Warnung");
            return;
        }

        try
        {
            var currentItems = await _obsClient.GetSceneItemListAsync(scene.Name);
            var current = currentItems.FirstOrDefault(candidate => candidate.ItemId == item.ItemId)
                ?? currentItems.FirstOrDefault(candidate => string.Equals(candidate.SourceName, item.SourceName, StringComparison.OrdinalIgnoreCase));
            if (current is null)
            {
                await RefreshServicesObsSceneItemsAsync();
                AddDashboardNotification($"OBS-Quelle „{item.SourceName}“ existiert nicht mehr.", "Warnung");
                return;
            }

            await _obsClient.SetSceneItemDetailedTransformAsync(scene.Name, current.SourceName, x, y, width, height, rotation, cropLeft, cropTop, cropRight, cropBottom);
            AddDashboardNotification($"{current.SourceName}: Transformation übernommen (Position {x:0.#}/{y:0.#}, Größe {width:0.#} × {height:0.#}, Drehung {rotation:0.#}°).", "Info");
        }
        catch (Exception exception)
        {
            AddDashboardNotification($"OBS-Quelle konnte nicht transformiert werden: {exception.Message}", "Fehler");
        }
    }

    private async Task SwitchServicesObsSceneAsync()
    {
        if (ServicesObsScenesList.SelectedItem is not ObsSceneInfo scene) return;
        await _obsClient.SetCurrentProgramSceneAsync(scene.Name);
        await RefreshObsAsync();
        await RefreshServicesObsSceneItemsAsync();
    }

    private async Task RefreshDashboardObsAudioStateAsync()
    {
        if (!_obsClient.IsConnected ||
            DashboardObsAudioInputBox.SelectedItem is not ObsInputInfo input)
        {
            DashboardObsAudioStateText.Text = "Audioquelle auswählen";
            return;
        }

        try
        {
            var state = await _obsClient.GetInputAudioStateAsync(input.Name);
            DashboardObsAudioStateText.Text =
                $"{state.Name}: {(state.Muted ? "GEMUTET" : "AKTIV")} · {state.VolumeDb:0.0} dB";
            DashboardObsAudioVolumeBox.Text =
                state.VolumeDb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            DashboardObsAudioStateText.Text =
                "Diese OBS-Quelle besitzt keine steuerbaren Audioeigenschaften.";
        }
    }

    private async Task SetDashboardObsAudioMuteAsync(bool muted)
    {
        if (!_obsClient.IsConnected ||
            DashboardObsAudioInputBox.SelectedItem is not ObsInputInfo input)
        {
            AddDashboardNotification("OBS-Audio kann nicht gesteuert werden: keine verbundene Audioquelle ausgewählt.", "Warnung");
            return;
        }

        try
        {
            await _obsClient.SetInputMuteAsync(input.Name, muted);
            await RefreshDashboardObsAudioStateAsync();
            AddDashboardNotification(
                $"{input.Name} wurde {(muted ? "gemutet" : "aktiviert")}.",
                "Info");
        }
        catch (Exception exception)
        {
            AddDashboardNotification(
                $"OBS-Audiofehler bei {input.Name}: {exception.Message}",
                "Fehler");
        }
    }

    private async Task SetDashboardObsAudioVolumeAsync()
    {
        if (!_obsClient.IsConnected ||
            DashboardObsAudioInputBox.SelectedItem is not ObsInputInfo input)
        {
            AddDashboardNotification("OBS-Lautstärke kann nicht gesetzt werden: keine Audioquelle ausgewählt.", "Warnung");
            return;
        }

        if (!double.TryParse(
                DashboardObsAudioVolumeBox.Text.Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var db))
        {
            AddDashboardNotification("Ungültiger dB-Wert für den OBS-Audiomixer.", "Warnung");
            return;
        }

        db = Math.Clamp(db, -100, 26);
        try
        {
            await _obsClient.SetInputVolumeDbAsync(input.Name, db);
            await RefreshDashboardObsAudioStateAsync();
            AddDashboardNotification($"{input.Name}: Lautstärke auf {db:0.0} dB gesetzt.", "Info");
        }
        catch (Exception exception)
        {
            AddDashboardNotification(
                $"OBS-Lautstärke konnte nicht gesetzt werden: {exception.Message}",
                "Fehler");
        }
    }


    private async Task ApplySelectedObsTransitionAsync()
    {
        if (!_obsClient.IsConnected)
        {
            ServicesObsTransitionStateText.Text = "OBS ist nicht verbunden.";
            AddDashboardNotification("OBS-Übergang kann nicht gesetzt werden: OBS ist nicht verbunden.", "Warnung");
            return;
        }

        if (ServicesObsTransitionBox.SelectedItem is not ObsTransitionInfo transition)
        {
            ServicesObsTransitionStateText.Text = "Bitte zuerst einen OBS-Übergang auswählen.";
            AddDashboardNotification("Bitte zuerst einen OBS-Übergang auswählen.", "Warnung");
            return;
        }

        if (!int.TryParse(ServicesObsTransitionDurationBox.Text.Trim(), out var durationMilliseconds))
        {
            ServicesObsTransitionStateText.Text = "Die Übergangsdauer muss eine ganze Zahl sein.";
            AddDashboardNotification("Ungültige OBS-Übergangsdauer.", "Warnung");
            return;
        }

        durationMilliseconds = Math.Clamp(durationMilliseconds, 0, 20000);
        ServicesObsTransitionDurationBox.Text = durationMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ServicesObsApplyTransitionButton.IsEnabled = false;
        ServicesObsTransitionStateText.Text = $"„{transition.Name}“ wird angewendet …";

        try
        {
            await _obsClient.SetCurrentSceneTransitionAsync(transition.Name);
            await _obsClient.SetCurrentSceneTransitionDurationAsync(durationMilliseconds);
            ServicesObsTransitionStateText.Text = $"Aktiv: {transition.Name} · {durationMilliseconds} ms";
            AddDashboardNotification($"OBS-Übergang „{transition.Name}“ mit {durationMilliseconds} ms übernommen.", "Info");
        }
        catch (Exception exception)
        {
            ServicesObsTransitionStateText.Text = $"Übergang konnte nicht gesetzt werden: {exception.Message}";
            AddDashboardNotification($"OBS-Übergang konnte nicht gesetzt werden: {exception.Message}", "Fehler");
        }
        finally
        {
            ServicesObsApplyTransitionButton.IsEnabled = _obsClient.IsConnected;
        }
    }

    private int _obsInputStateRefreshVersion;
    private bool _updatingObsMixerVolumeUi;
    private void SetServicesObsAudioControlsEnabled(bool enabled)
    {
        ServicesObsMuteInputButton.IsEnabled = enabled;
        ServicesObsUnmuteInputButton.IsEnabled = enabled;
        ServicesObsVolumeDbBox.IsEnabled = enabled;
        ServicesObsVolumeSlider.IsEnabled = enabled;
        ServicesObsSetVolumeButton.IsEnabled = enabled;
        ServicesObsVolumeMinus20Button.IsEnabled = enabled;
        ServicesObsVolumeMinus10Button.IsEnabled = enabled;
        ServicesObsVolumeZeroButton.IsEnabled = enabled;
        ServicesObsMonitoringBox.IsEnabled = enabled;
        ServicesObsSyncOffsetBox.IsEnabled = enabled;
        ServicesObsApplyAdvancedAudioButton.IsEnabled = enabled;
    }
    private static double DbToPercent(double db)
    {
        if (db <= -60) return 0;
        var multiplier = Math.Pow(10, db / 20.0);
        return Math.Clamp(multiplier * 100.0, 0, 316);
    }

    private async Task RefreshSelectedObsInputStateAsync()
    {
        var refreshVersion=++_obsInputStateRefreshVersion;
        if(!_obsClient.IsConnected || ServicesObsInputsList.SelectedItem is not ObsInputInfo input){ SetServicesObsAudioControlsEnabled(false); ServicesObsSelectedInputStateText.Text="Audioquelle auswählen"; return; }
        SetServicesObsAudioControlsEnabled(false); ServicesObsSelectedInputStateText.Text=$"{input.Name}: Status wird geladen …";
        try
        {
            var state=await _obsClient.GetInputAudioStateAsync(input.Name);
            var advancedState=await _obsClient.GetInputAdvancedAudioStateAsync(input.Name);
            if(refreshVersion!=_obsInputStateRefreshVersion || ServicesObsInputsList.SelectedItem is not ObsInputInfo currentInput || !string.Equals(currentInput.Name,input.Name,StringComparison.OrdinalIgnoreCase)) return;
            _servicesObsInputsMuted[state.Name] = state.Muted;
            ServicesObsSelectedInputStateText.Text = $"{state.Name}: {(state.Muted ? "GEMUTET" : "AKTIV")} · {state.VolumeDb:0.0} dB · Sync {advancedState.SyncOffsetMilliseconds} ms";
            _updatingObsMixerVolumeUi = true;
            try
            {
                var sliderValue = Math.Clamp(state.VolumeDb, ServicesObsVolumeSlider.Minimum, ServicesObsVolumeSlider.Maximum);
                ServicesObsVolumeSlider.Value = sliderValue;
                ServicesObsVolumeDbBox.Text = state.VolumeDb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                ServicesObsSyncOffsetBox.Text = advancedState.SyncOffsetMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                foreach (var item in ServicesObsMonitoringBox.Items.OfType<ComboBoxItem>())
                {
                    if (string.Equals(item.Tag?.ToString(), advancedState.MonitorType, StringComparison.OrdinalIgnoreCase))
                    {
                        ServicesObsMonitoringBox.SelectedItem = item;
                        break;
                    }
                }
            }
            finally
            {
                _updatingObsMixerVolumeUi = false;
            }
            SetServicesObsAudioControlsEnabled(true);
        }
        catch(Exception exception){ if(refreshVersion!=_obsInputStateRefreshVersion)return; SetServicesObsAudioControlsEnabled(false); ServicesObsSelectedInputStateText.Text=$"Keine steuerbaren Audioeigenschaften: {exception.Message}"; }
    }
    private async Task SetSelectedObsInputMuteAsync(bool muted)
    {
        if(!_obsClient.IsConnected || ServicesObsInputsList.SelectedItem is not ObsInputInfo input){ AddDashboardNotification("OBS-Audio kann nicht gesteuert werden: keine gültige Audioquelle ausgewählt.","Warnung"); return; }
        try{ await _obsClient.SetInputMuteAsync(input.Name,muted); await RefreshSelectedObsInputStateAsync(); AddDashboardNotification($"{input.Name} wurde {(muted?"gemutet":"aktiviert")}.","Info"); }
        catch(Exception exception){ AddDashboardNotification($"OBS-Audiofehler bei {input.Name}: {exception.Message}","Fehler"); await RefreshSelectedObsInputStateAsync(); }
    }
    private async Task SetSelectedObsInputVolumeAsync()
    {
        if(!_obsClient.IsConnected || ServicesObsInputsList.SelectedItem is not ObsInputInfo input){ AddDashboardNotification("OBS-Lautstärke kann nicht gesetzt werden: keine gültige Audioquelle ausgewählt.","Warnung"); return; }
        if(!double.TryParse(ServicesObsVolumeDbBox.Text.Replace(',','.'),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var db)){ AddDashboardNotification("Ungültige OBS-Lautstärke. Bitte einen dB-Wert zwischen -100 und 26 eingeben.","Warnung"); await RefreshSelectedObsInputStateAsync(); return; }
        db=Math.Clamp(db,-100,26);
        try{ await _obsClient.SetInputVolumeDbAsync(input.Name,db); await RefreshSelectedObsInputStateAsync(); AddDashboardNotification($"{input.Name}: Lautstärke auf {db:0.0} dB gesetzt.","Info"); }
        catch(Exception exception){ AddDashboardNotification($"OBS-Lautstärke konnte für {input.Name} nicht gesetzt werden: {exception.Message}","Fehler"); await RefreshSelectedObsInputStateAsync(); }
    }

    private async Task ApplyObsMixerPresetAsync(double db)
    {
        if (!_obsClient.IsConnected || ServicesObsInputsList.SelectedItem is not ObsInputInfo input)
        {
            AddDashboardNotification("OBS-Pegel kann nicht gesetzt werden: keine Audioquelle ausgewählt.", "Warnung");
            return;
        }

        try
        {
            SetServicesObsAudioControlsEnabled(false);
            await _obsClient.SetInputVolumeDbAsync(input.Name, db);
            await RefreshSelectedObsInputStateAsync();
            AddDashboardNotification($"{input.Name}: Schnellpegel {db:0} dB übernommen.", "Info");
        }
        catch (Exception exception)
        {
            AddDashboardNotification($"OBS-Schnellpegel konnte nicht gesetzt werden: {exception.Message}", "Fehler");
            await RefreshSelectedObsInputStateAsync();
        }
    }


    private async Task ApplySelectedObsAdvancedAudioAsync()
    {
        if (!_obsClient.IsConnected || ServicesObsInputsList.SelectedItem is not ObsInputInfo input)
        {
            AddDashboardNotification("Erweiterte OBS-Audioeinstellungen können nicht gesetzt werden: keine Audioquelle ausgewählt.", "Warnung");
            return;
        }

        if (ServicesObsMonitoringBox.SelectedItem is not ComboBoxItem monitoringItem || string.IsNullOrWhiteSpace(monitoringItem.Tag?.ToString()))
        {
            AddDashboardNotification("Bitte einen Monitoring-Modus auswählen.", "Warnung");
            return;
        }

        if (!int.TryParse(ServicesObsSyncOffsetBox.Text, out var syncOffsetMilliseconds))
        {
            AddDashboardNotification("Der Audio-Sync-Wert muss eine ganze Millisekunden-Zahl sein.", "Warnung");
            return;
        }

        syncOffsetMilliseconds = Math.Clamp(syncOffsetMilliseconds, -950, 20000);
        try
        {
            SetServicesObsAudioControlsEnabled(false);
            await _obsClient.SetInputAudioMonitorTypeAsync(input.Name, monitoringItem.Tag!.ToString()!);
            await _obsClient.SetInputAudioSyncOffsetAsync(input.Name, syncOffsetMilliseconds);
            await RefreshSelectedObsInputStateAsync();
            AddDashboardNotification($"{input.Name}: Monitoring und Audio-Sync wurden übernommen.", "Info");
        }
        catch (Exception exception)
        {
            AddDashboardNotification($"Erweiterte OBS-Audioeinstellungen konnten nicht gesetzt werden: {exception.Message}", "Fehler");
            await RefreshSelectedObsInputStateAsync();
        }
    }


    private void RefreshObsAudioProfilesUi(string? selectedName = null)
    {
        _settings.Obs.AudioProfiles ??= [];
        ServicesObsAudioProfileBox.ItemsSource = null;
        ServicesObsAudioProfileBox.ItemsSource = _settings.Obs.AudioProfiles.OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var selected = _settings.Obs.AudioProfiles.FirstOrDefault(profile => string.Equals(profile.Name, selectedName, StringComparison.OrdinalIgnoreCase));
        if (selected is not null) ServicesObsAudioProfileBox.SelectedItem = selected;
        else if (ServicesObsAudioProfileBox.Items.Count > 0) ServicesObsAudioProfileBox.SelectedIndex = 0;
        ServicesObsApplyAudioProfileButton.IsEnabled = _obsClient.IsConnected && ServicesObsAudioProfileBox.Items.Count > 0;
        ServicesObsDeleteAudioProfileButton.IsEnabled = ServicesObsAudioProfileBox.Items.Count > 0;
        ServicesObsSaveAudioProfileButton.IsEnabled = _obsClient.IsConnected;
    }

    private async Task SaveObsAudioProfileAsync()
    {
        if (!_obsClient.IsConnected)
        {
            AddDashboardNotification("Audio-Profil kann nicht gespeichert werden: OBS ist nicht verbunden.", "Warnung");
            return;
        }
        var name = ServicesObsAudioProfileNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            AddDashboardNotification("Bitte einen Namen für das Audio-Profil eingeben.", "Warnung");
            return;
        }
        try
        {
            ServicesObsAudioProfileStateText.Text = "Audioquellen werden gelesen …";
            var inputs = await _obsClient.GetInputListAsync();
            var entries = new List<ObsAudioProfileEntrySettings>();
            foreach (var input in inputs)
            {
                try
                {
                    var state = await _obsClient.GetInputAudioStateAsync(input.Name);
                    var advanced = await _obsClient.GetInputAdvancedAudioStateAsync(input.Name);
                    entries.Add(new ObsAudioProfileEntrySettings
                    {
                        InputName = input.Name,
                        VolumeDb = state.VolumeDb,
                        Muted = state.Muted,
                        MonitorType = advanced.MonitorType,
                        SyncOffsetMilliseconds = advanced.SyncOffsetMilliseconds
                    });
                }
                catch
                {
                    // Nicht jede OBS-Quelle besitzt Audioeigenschaften.
                }
            }
            if (entries.Count == 0)
            {
                ServicesObsAudioProfileStateText.Text = "Keine steuerbaren Audioquellen gefunden.";
                AddDashboardNotification("OBS meldet keine steuerbaren Audioquellen für das Profil.", "Warnung");
                return;
            }
            _settings.Obs.AudioProfiles.RemoveAll(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase));
            _settings.Obs.AudioProfiles.Add(new ObsAudioProfileSettings { Name = name, Inputs = entries });
            await _settingsStore.SaveAsync(_settings);
            RefreshObsAudioProfilesUi(name);
            ServicesObsAudioProfileStateText.Text = $"Profil „{name}“ mit {entries.Count} Audioquellen gespeichert.";
            AddDashboardNotification($"OBS-Audio-Profil „{name}“ gespeichert.", "Info");
        }
        catch (Exception exception)
        {
            ServicesObsAudioProfileStateText.Text = "Profil konnte nicht gespeichert werden: " + exception.Message;
            AddDashboardNotification("OBS-Audio-Profil konnte nicht gespeichert werden: " + exception.Message, "Fehler");
        }
    }

    private async Task ApplySelectedObsAudioProfileAsync()
    {
        if (!_obsClient.IsConnected || ServicesObsAudioProfileBox.SelectedItem is not ObsAudioProfileSettings profile)
        {
            AddDashboardNotification("Bitte OBS verbinden und ein Audio-Profil auswählen.", "Warnung");
            return;
        }
        var applied = 0;
        var missing = new List<string>();
        ServicesObsApplyAudioProfileButton.IsEnabled = false;
        try
        {
            foreach (var entry in profile.Inputs)
            {
                try
                {
                    if (!await _obsClient.InputExistsAsync(entry.InputName))
                    {
                        missing.Add(entry.InputName);
                        continue;
                    }
                    await _obsClient.SetInputVolumeDbAsync(entry.InputName, Math.Clamp(entry.VolumeDb, -100, 26));
                    await _obsClient.SetInputMuteAsync(entry.InputName, entry.Muted);
                    await _obsClient.SetInputAudioMonitorTypeAsync(entry.InputName, entry.MonitorType);
                    await _obsClient.SetInputAudioSyncOffsetAsync(entry.InputName, Math.Clamp(entry.SyncOffsetMilliseconds, -950, 20000));
                    applied++;
                }
                catch
                {
                    missing.Add(entry.InputName);
                }
            }
            ServicesObsAudioProfileStateText.Text = missing.Count == 0
                ? $"Profil „{profile.Name}“ vollständig angewendet ({applied} Quellen)."
                : $"Profil angewendet: {applied} erfolgreich, {missing.Count} nicht verfügbar.";
            AddDashboardNotification($"OBS-Audio-Profil „{profile.Name}“ angewendet: {applied} Quellen.", missing.Count == 0 ? "Info" : "Warnung");
            await RefreshSelectedObsInputStateAsync();
        }
        finally
        {
            ServicesObsApplyAudioProfileButton.IsEnabled = true;
        }
    }

    private async Task DeleteSelectedObsAudioProfileAsync()
    {
        if (ServicesObsAudioProfileBox.SelectedItem is not ObsAudioProfileSettings profile) return;
        _settings.Obs.AudioProfiles.RemoveAll(item => string.Equals(item.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
        await _settingsStore.SaveAsync(_settings);
        RefreshObsAudioProfilesUi();
        ServicesObsAudioProfileNameBox.Clear();
        ServicesObsAudioProfileStateText.Text = $"Profil „{profile.Name}“ gelöscht.";
        AddDashboardNotification($"OBS-Audio-Profil „{profile.Name}“ gelöscht.", "Info");
    }

    private async Task SaveSpotifyDisplayOptionsImmediatelyAsync()
    {
        // Während des initialen Ladens werden die CheckBox-Ereignisse ebenfalls ausgelöst.
        // Erst speichern, wenn das Fenster vollständig geladen ist.
        if (!IsLoaded || _loadingSettingsIntoUi) return;
        try
        {
            await SaveSpotifyOverlaySettingsAsync();
            await WriteSpotifyOverlayRuntimeDataAsync(_spotifyModule.GetSnapshot(), _spotifyModule.GetSnapshot().Playback);
            ServicesSpotifyOverlayStatusText.Text = "Anzeigeoptionen gespeichert und sofort in die Overlay-JSON geschrieben.";
            ServicesSpotifyOverlayStatusText.Foreground = Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            ServicesSpotifyOverlayStatusText.Text = "Anzeigeoptionen konnten nicht gespeichert werden: " + exception.Message;
            ServicesSpotifyOverlayStatusText.Foreground = Brushes.IndianRed;
        }
    }

    private async Task PulseExternalAlertAsync(string source, string id, TimeSpan duration)
    {
        _externalAlertActivity.Start(source, id);
        try
        {
            await Task.Delay(duration);
        }
        finally
        {
            _externalAlertActivity.End(source, id);
        }
    }

    private async Task SaveSpotifyOverlaySettingsAsync()
    {
        // Die Spotify-Anzeige ist wieder fest aktiviert. Nur das Ausblenden bei Mute bleibt konfigurierbar.
        _settings.Spotify.OverlayShowTitle = true;
        _settings.Spotify.OverlayShowArtist = true;
        _settings.Spotify.OverlayShowAlbumCover = true;
        _settings.Spotify.OverlayShowProgress = true;
        _settings.Spotify.OverlayHideWhenPaused = ServicesSpotifyHidePausedBox.IsChecked == true;
        _settings.Spotify.OverlayHideWhenMuted = ServicesSpotifyHideMutedBox.IsChecked == true;
        _settings.Spotify.OverlayMuteDetectionObsSource = ServicesSpotifyDetectObsMuteBox.IsChecked == true;
        _settings.Spotify.OverlayMuteDetectionSpotifyVolume = ServicesSpotifyDetectVolumeMuteBox.IsChecked == true;
        _settings.Spotify.OverlayObsAudioSource = ServicesSpotifyObsAudioSourceBox.Text?.Trim() ?? "Spotify";
        _settings.Spotify.OverlayEnabled = true;

        var requestedPath = ServicesSpotifyDataJsonPathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            requestedPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CreatorControlSuite", "Overlay", "data", "overlay-data.json");
            ServicesSpotifyDataJsonPathBox.Text = requestedPath;
        }

        requestedPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(requestedPath));
        if (!string.Equals(Path.GetExtension(requestedPath), ".json", StringComparison.OrdinalIgnoreCase))
            requestedPath += ".json";

        // Wurde versehentlich die Overlay-Projektdatei (overlay.json) gewählt,
        // verwende automatisch deren DataSourcePath statt die Projektdefinition
        // mit Laufzeitdaten zu überschreiben.
        if (File.Exists(requestedPath))
        {
            try
            {
                using var selectedJson = JsonDocument.Parse(await File.ReadAllTextAsync(requestedPath));
                if (selectedJson.RootElement.ValueKind == JsonValueKind.Object &&
                    selectedJson.RootElement.TryGetProperty("DataSourcePath", out var dataSourcePathElement) &&
                    dataSourcePathElement.ValueKind == JsonValueKind.String)
                {
                    var manifestDataPath = dataSourcePathElement.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(manifestDataPath))
                    {
                        requestedPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(manifestDataPath));
                        ServicesSpotifyDataJsonPathBox.Text = requestedPath;
                    }
                }
            }
            catch (JsonException)
            {
                // Normale oder noch leere Datendateien werden wie gewählt verwendet.
            }
        }

        _settings.Overlay.DataFilePath = requestedPath;
        _settings.Overlay.DataFileName = Path.GetFileName(requestedPath);
        await _settingsStore.SaveAsync(_settings);

        await _overlayModule.Service.UpdateAsync(data =>
        {
            data.Spotify.ShowTitle = true;
            data.Spotify.ShowArtist = true;
            data.Spotify.ShowAlbumCover = true;
            data.Spotify.ShowProgress = true;
            data.Spotify.HideWhenPaused = false;
                data.Spotify.HideWhenMuted = _settings.Spotify.OverlayHideWhenMuted;
            data.Spotify.ShowInOverlay = true;
            data.Spotify.Cover = data.Spotify.CoverUrl;
        });

        ServicesSpotifyDataJsonPathBox.Text = requestedPath;
        ServicesSpotifyOverlayPathText.Text = $"JSON: {requestedPath}";
        ServicesSpotifyOverlayStatusText.Text = File.Exists(requestedPath)
            ? "JSON-Pfad gespeichert. Die Suite schreibt aktuelle Spotify-Daten direkt in diese Datei; HTML und OBS bleiben unverändert."
            : "JSON-Pfad gespeichert. Die Datei wird beim nächsten Spotify-Datenupdate automatisch angelegt.";
        ServicesSpotifyOverlayStatusText.Foreground = Brushes.LightGreen;

        var patchedFiles = await PatchSpotifyOverlayHtmlAsync(requestedPath);
        if (patchedFiles > 0)
        {
            ServicesSpotifyOverlayStatusText.Text += $" Anzeigeoptionen wurden in {patchedFiles} Spotify-HTML-Datei(en) aktiviert.";
        }
    }

    private async Task<int> PatchSpotifyOverlayHtmlAsync(string dataJsonPath)
    {
        try
        {
            var dataDirectory = Path.GetDirectoryName(dataJsonPath);
            var overlayDirectory = dataDirectory is null ? null : Directory.GetParent(dataDirectory)?.FullName;
            if (string.IsNullOrWhiteSpace(overlayDirectory) || !Directory.Exists(overlayDirectory))
                return 0;

            const string marker = "CCS-SPOTIFY-DISPLAY-OPTIONS-V2";
            const string compatibilityScript = @"
<script>
// CCS-SPOTIFY-DISPLAY-OPTIONS-V2
(() => {
  if (!window.CreatorOverlayData || typeof window.CreatorOverlayData.subscribe !== 'function') return;
  window.CreatorOverlayData.subscribe(data => {
    const spotify = (data && data.spotify) || {};
    const byId = id => document.getElementById(id);
    const setVisible = (element, visible) => { if (element) element.style.display = visible ? '' : 'none'; };
    setVisible(byId('title'), spotify.showTitle !== false);
    setVisible(byId('artist'), spotify.showArtist !== false);
    setVisible(byId('album'), spotify.showArtist !== false);
    setVisible(byId('cover'), spotify.showAlbumCover !== false);
    const progress = byId('prog') || byId('progress') || document.querySelector('.spotify-progress-row,.progress-row,.spotify-progress');
    setVisible(progress, spotify.showProgress !== false);
    const container = byId('box') || document.querySelector('.spotify-card,.spotify-container,.spotify-widget') || document.body;
    const shouldHide = spotify.showInOverlay === false || (spotify.hideWhenPaused === true && spotify.isPlaying !== true);
    if (container) container.style.display = shouldHide ? 'none' : '';
  });
})();
</script>";

            var patched = 0;
            var candidates = Directory.EnumerateFiles(overlayDirectory, "*.html", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(path).Contains("spotify", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var htmlPath in candidates)
            {
                var html = await File.ReadAllTextAsync(htmlPath);
                if (html.Contains(marker, StringComparison.Ordinal)) continue;
                if (!html.Contains("CreatorOverlayData", StringComparison.OrdinalIgnoreCase)) continue;

                var bodyIndex = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                html = bodyIndex >= 0
                    ? html.Insert(bodyIndex, compatibilityScript)
                    : html + compatibilityScript;
                await File.WriteAllTextAsync(htmlPath, html);
                patched++;
            }
            return patched;
        }
        catch (Exception exception)
        {
            _appLogger.Write(AppLogLevel.Warning, "Spotify", "Spotify-HTML konnte nicht für die Anzeigeoptionen aktualisiert werden.", exception);
            return 0;
        }
    }

    private void BrowseSpotifyDataJsonPath()
    {
        var current = ServicesSpotifyDataJsonPathBox.Text?.Trim();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "JSON-Datei für Spotify-Daten auswählen oder anlegen",
            Filter = "JSON-Datei (*.json)|*.json|Alle Dateien (*.*)|*.*",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = string.IsNullOrWhiteSpace(current) ? "overlay-data.json" : Path.GetFileName(current),
            InitialDirectory = !string.IsNullOrWhiteSpace(current) && Directory.Exists(Path.GetDirectoryName(current))
                ? Path.GetDirectoryName(current)
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) == true)
        {
            ServicesSpotifyDataJsonPathBox.Text = dialog.FileName;
            ServicesSpotifyOverlayPathText.Text = $"JSON: {dialog.FileName}";
            ServicesSpotifyOverlayStatusText.Text = File.Exists(dialog.FileName)
                ? "Vorhandene JSON-Datei ausgewählt. Beim Speichern werden die Daten dort fortgeschrieben."
                : "Neue JSON-Datei ausgewählt. Sie wird beim Speichern automatisch angelegt.";
            ServicesSpotifyOverlayStatusText.Foreground = Brushes.LightGray;
        }
    }

    private async Task WriteSpotifyDataJsonNowAsync()
    {
        try
        {
            await SaveSpotifyOverlaySettingsAsync();
            await _overlayModule.Service.WriteAsync();
            var path = await _overlayModule.Service.GetDataFilePathAsync();
            ServicesSpotifyOverlayStatusText.Text = $"JSON wurde aktualisiert: {path}";
            ServicesSpotifyOverlayStatusText.Foreground = Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            ServicesSpotifyOverlayStatusText.Text = "JSON konnte nicht geschrieben werden: " + exception.Message;
            ServicesSpotifyOverlayStatusText.Foreground = Brushes.IndianRed;
        }
    }

    private void OpenSpotifyDataJsonFolder()
    {
        try
        {
            var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(ServicesSpotifyDataJsonPathBox.Text.Trim()));
            var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Der JSON-Ordner konnte nicht bestimmt werden.");
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo("explorer.exe", directory) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ServicesSpotifyOverlayStatusText.Text = exception.Message;
            ServicesSpotifyOverlayStatusText.Foreground = Brushes.IndianRed;
        }
    }

    private void OpenSpotifyDataJsonFile()
    {
        try
        {
            var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(ServicesSpotifyDataJsonPathBox.Text.Trim()));
            if (!File.Exists(path))
                throw new FileNotFoundException("Die JSON-Datei existiert noch nicht. Klicke zuerst auf JSON-PFAD SPEICHERN oder JSON JETZT SCHREIBEN.", path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ServicesSpotifyOverlayStatusText.Text = exception.Message;
            ServicesSpotifyOverlayStatusText.Foreground = Brushes.IndianRed;
        }
    }

    private async Task RefreshSpotifyOverlayBrowserSourcesAsync()
    {
        if (ServicesSpotifyOverlaySourceBox is null) return;

        var sceneName = ServicesSpotifyOverlaySceneBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            sceneName = ServicesSpotifyOverlaySceneBox.Text?.Trim();
        }

        var requestedSource = ServicesSpotifyOverlaySourceBox.Text?.Trim();
        if (!_obsClient.IsConnected || string.IsNullOrWhiteSpace(sceneName))
        {
            ServicesSpotifyOverlaySourceBox.ItemsSource = Array.Empty<string>();
            return;
        }

        try
        {
            var sceneItems = await _obsClient.GetSceneItemListAsync(sceneName);
            var allInputs = await _obsClient.GetInputListAsync();
            ServicesSpotifyObsAudioSourceBox.ItemsSource = allInputs
                .Select(input => input.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var browserInputNames = allInputs
                .Where(input => string.Equals(input.Kind, "browser_source", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(input.UnversionedKind, "browser_source", StringComparison.OrdinalIgnoreCase))
                .Select(input => input.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var browserSources = sceneItems
                .Select(item => item.SourceName)
                .Where(name => !string.IsNullOrWhiteSpace(name) && browserInputNames.Contains(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ServicesSpotifyOverlaySourceBox.ItemsSource = browserSources;

            var preferredSource = !string.IsNullOrWhiteSpace(requestedSource)
                ? requestedSource
                : _settings.Spotify.OverlayObsSource;
            var matchingSource = browserSources.FirstOrDefault(source =>
                string.Equals(source, preferredSource, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(matchingSource))
            {
                ServicesSpotifyOverlaySourceBox.SelectedItem = matchingSource;
                ServicesSpotifyOverlaySourceBox.Text = matchingSource;
            }
            else if (!string.IsNullOrWhiteSpace(preferredSource))
            {
                ServicesSpotifyOverlaySourceBox.Text = preferredSource;
            }
            else if (browserSources.Count == 1)
            {
                ServicesSpotifyOverlaySourceBox.SelectedItem = browserSources[0];
                ServicesSpotifyOverlaySourceBox.Text = browserSources[0];
            }

            ServicesSpotifyOverlayStatusText.Text = browserSources.Count == 0
                ? $"In der Szene ‘{sceneName}’ wurde keine Browserquelle gefunden."
                : $"{browserSources.Count} Browserquelle(n) aus Szene ‘{sceneName}’ geladen.";
            ServicesSpotifyOverlayStatusText.Foreground = browserSources.Count == 0 ? Brushes.Goldenrod : Brushes.LightGray;
        }
        catch (Exception exception)
        {
            ServicesSpotifyOverlaySourceBox.ItemsSource = Array.Empty<string>();
            ServicesSpotifyOverlayStatusText.Text = "Browserquellen konnten nicht geladen werden: " + exception.Message;
            ServicesSpotifyOverlayStatusText.Foreground = Brushes.IndianRed;
        }
    }

    private void RefreshSpotifyOverlayProjectSelector()
    {
        if (ServicesSpotifyOverlayProjectBox is null) return;
        ServicesSpotifyOverlayProjectBox.ItemsSource = _overlayProjects;
        // The live OBS refresh supplies this selector directly. As a fallback, reuse
        // the overlay-project scene list when it is already available.
        if (ServicesSpotifyOverlaySceneBox.ItemsSource is null)
            ServicesSpotifyOverlaySceneBox.ItemsSource = OverlayProjectObsSceneBox.ItemsSource;
        ServicesSpotifyOverlayProjectBox.SelectedItem = _overlayProjects.FirstOrDefault(x => x.Id == _settings.Spotify.OverlayProjectId)
            ?? _overlayProjects.FirstOrDefault(x => x.Items.Any(i => i.Name.Contains("spotify", StringComparison.OrdinalIgnoreCase) || i.RelativePath.Contains("spotify", StringComparison.OrdinalIgnoreCase)))
            ?? _overlayProjects.FirstOrDefault();
        RefreshSpotifyOverlayProjectItems();
    }

    private void RefreshSpotifyOverlayProjectItems()
    {
        if (ServicesSpotifyOverlayItemBox is null) return;
        var project = ServicesSpotifyOverlayProjectBox.SelectedItem as OverlayProjectDefinition;
        var candidates = project?.Items.Where(x => x.RelativePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)).ToList() ?? [];
        ServicesSpotifyOverlayItemBox.ItemsSource = candidates;
        ServicesSpotifyOverlayItemBox.SelectedItem = candidates.FirstOrDefault(x => x.Id == _settings.Spotify.OverlayItemId)
            ?? candidates.FirstOrDefault(x => x.Name.Contains("spotify", StringComparison.OrdinalIgnoreCase) || x.RelativePath.Contains("spotify", StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault();
        RefreshSpotifyOverlaySelectionDetails();
    }

    private void RefreshSpotifyOverlaySelectionDetails()
    {
        if (ServicesSpotifyOverlayPathText is null || ServicesSpotifyOverlayStatusText is null) return;
        if (ServicesSpotifyOverlayProjectBox.SelectedItem is not OverlayProjectDefinition project || ServicesSpotifyOverlayItemBox.SelectedItem is not OverlayProjectItem item)
        {
            var jsonPath = ServicesSpotifyDataJsonPathBox?.Text?.Trim();
            ServicesSpotifyOverlayPathText.Text = string.IsNullOrWhiteSpace(jsonPath) ? "Noch keine JSON-Datei ausgewählt." : $"JSON: {jsonPath}";
            ServicesSpotifyOverlayStatusText.Text = "Die HTML- und OBS-Konfiguration wird nicht verändert. Die Suite schreibt nur in die ausgewählte JSON-Datei.";
            ServicesSpotifyOverlayStatusText.Foreground = Brushes.LightGray;
            return;
        }
        var path = item.IsLocalFile && !Path.IsPathRooted(item.RelativePath) ? Path.Combine(project.RootPath, item.RelativePath) : item.RelativePath;
        ServicesSpotifyOverlayPathText.Text = $"HTML: {path}";
        if (!string.IsNullOrWhiteSpace(item.ObsScene)) ServicesSpotifyOverlaySceneBox.Text = item.ObsScene;
        if (!string.IsNullOrWhiteSpace(item.ObsSource)) ServicesSpotifyOverlaySourceBox.Text = item.ObsSource;
        var fileOk = !item.IsLocalFile || File.Exists(path);
        ServicesSpotifyOverlayStatusText.Text = $"{(fileOk ? "HTML-Datei gefunden" : "HTML-Datei fehlt")} · {(string.IsNullOrWhiteSpace(item.ObsSource) ? "Browserquelle noch nicht festgelegt" : "Browserquelle: " + item.ObsSource)} · {project.DataReferenceStatus} · {(_obsClient.IsConnected ? "OBS verbunden" : "OBS nicht verbunden")}";
        ServicesSpotifyOverlayStatusText.Foreground = fileOk ? Brushes.LightGray : Brushes.IndianRed;
    }

    private async Task SynchronizeSpotifyOverlayAsync()
    {
        try
        {
            await SaveSpotifyOverlaySettingsAsync();
            if (ServicesSpotifyOverlayProjectBox.SelectedItem is not OverlayProjectDefinition project || ServicesSpotifyOverlayItemBox.SelectedItem is not OverlayProjectItem)
                throw new InvalidOperationException("Bitte ein Overlay-Projekt und ein HTML-Modul auswählen.");
            if (!_obsClient.IsConnected) throw new InvalidOperationException("OBS ist nicht verbunden.");
            await _overlayProjectService.SynchronizeWithObsAsync(project);
            await _overlayProjectService.SaveAsync(_overlayProjects);
            ServicesSpotifyOverlayStatusText.Text = "Spotify-Browserquelle wurde in OBS erstellt bzw. aktualisiert.";
            ServicesSpotifyOverlayStatusText.Foreground = Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            ServicesSpotifyOverlayStatusText.Text = exception.Message;
            ServicesSpotifyOverlayStatusText.Foreground = Brushes.IndianRed;
        }
    }

    private void PreviewSpotifyOverlay()
    {
        try
        {
            if (ServicesSpotifyOverlayProjectBox.SelectedItem is not OverlayProjectDefinition project || ServicesSpotifyOverlayItemBox.SelectedItem is not OverlayProjectItem item)
                throw new InvalidOperationException("Bitte ein Overlay-Projekt und ein HTML-Modul auswählen.");
            var path = item.IsLocalFile && !Path.IsPathRooted(item.RelativePath) ? Path.Combine(project.RootPath, item.RelativePath) : item.RelativePath;
            if (item.IsLocalFile && !File.Exists(path)) throw new FileNotFoundException("Die ausgewählte HTML-Datei wurde nicht gefunden.", path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            ServicesSpotifyOverlayStatusText.Text = "Overlay-Vorschau wurde im Standardbrowser geöffnet.";
            ServicesSpotifyOverlayStatusText.Foreground = Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            ServicesSpotifyOverlayStatusText.Text = exception.Message;
            ServicesSpotifyOverlayStatusText.Foreground = Brushes.IndianRed;
        }
    }

    private void ApplyTwitchGoalFieldsToSettings()
    {
        static double D(string text, double fallback) => double.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
        static int I(string text, int fallback) => int.TryParse(text, out var value) ? value : fallback;
        _settings.Obs.GoalOverlayScene = string.IsNullOrWhiteSpace(GoalOverlaySceneBox.Text)
            ? "CCS Ziele & Overlay-Daten"
            : GoalOverlaySceneBox.Text.Trim();
        _settings.Twitch.FollowerGoal.Title = string.IsNullOrWhiteSpace(FollowerGoalTitleBox.Text) ? "Follower-Ziel" : FollowerGoalTitleBox.Text.Trim();
        _settings.Twitch.FollowerGoal.Current =
            _currentFollowerCount > 0
                ? _currentFollowerCount
                : D(FollowerGoalCurrentBox.Text, _settings.Twitch.FollowerGoal.Current);
        _settings.Twitch.FollowerGoal.Target = D(FollowerGoalTargetBox.Text, _settings.Twitch.FollowerGoal.Target);
        _settings.Twitch.FollowerGoal.FontFace = FollowerGoalFontBox.Text.Trim();
        _settings.Twitch.FollowerGoal.FontSize = I(FollowerGoalFontSizeBox.Text, 36);
        _settings.Twitch.SubGoal.Title = string.IsNullOrWhiteSpace(SubGoalTitleBox.Text) ? "Sub-Ziel" : SubGoalTitleBox.Text.Trim();
        _settings.Twitch.SubGoal.Current =
            _currentActiveSubscriptionCount > 0
                ? _currentActiveSubscriptionCount
                : D(SubGoalCurrentBox.Text, _settings.Twitch.SubGoal.Current);
        _settings.Twitch.SubGoal.Target = D(SubGoalTargetBox.Text, _settings.Twitch.SubGoal.Target);
        _settings.Twitch.SubGoal.FontFace = SubGoalFontBox.Text.Trim();
        _settings.Twitch.SubGoal.FontSize = I(SubGoalFontSizeBox.Text, 36);
        _settings.Twitch.DonationGoal.Title = string.IsNullOrWhiteSpace(DonationGoalTitleBox.Text) ? "Donation-Ziel" : DonationGoalTitleBox.Text.Trim();
        _settings.Twitch.DonationGoal.Current = D(DonationGoalCurrentBox.Text, _settings.Twitch.DonationGoal.Current);
        _settings.Twitch.DonationGoal.Target = D(DonationGoalTargetBox.Text, _settings.Twitch.DonationGoal.Target);
        _settings.Twitch.DonationGoal.Currency = DonationGoalCurrencyBox.Text.Trim();
        _settings.Twitch.DonationGoal.FontFace = DonationGoalFontBox.Text.Trim();
        _settings.Twitch.DonationGoal.FontSize = I(DonationGoalFontSizeBox.Text, 36);
    }

    private async Task SaveTwitchGoalsAsync()
    {
        ApplyTwitchGoalFieldsToSettings();
        await _settingsStore.SaveAsync(_settings);
        await _overlayModule.Service.UpdateAsync(data =>
        {
            data.Twitch.FollowerGoalState = ToOverlayGoal(_settings.Twitch.FollowerGoal);
            data.Twitch.SubGoalState = ToOverlayGoal(_settings.Twitch.SubGoal);
            data.Twitch.DonationGoalState = ToOverlayGoal(_settings.Twitch.DonationGoal);
        });
        await UpdateActiveOverlayJsonAsync(root =>
        {
            var twitch = root["twitch"] as JsonObject ?? new JsonObject();
            twitch["followers"] = _currentFollowerCount;
            twitch["followerGoal"] = _settings.Twitch.FollowerGoal.Target;
            var followerGoal = twitch["followerGoalState"] as JsonObject ?? new JsonObject();
            followerGoal["title"] = _settings.Twitch.FollowerGoal.Title;
            followerGoal["current"] = _currentFollowerCount > 0 ? _currentFollowerCount : _settings.Twitch.FollowerGoal.Current;
            followerGoal["target"] = _settings.Twitch.FollowerGoal.Target;
            followerGoal["fontFace"] = _settings.Twitch.FollowerGoal.FontFace;
            followerGoal["fontSize"] = _settings.Twitch.FollowerGoal.FontSize;
            twitch["followerGoalState"] = followerGoal;
            root["twitch"] = twitch;
        });
    }

    private static CreatorControlSuite.Modules.Overlay.Models.OverlayGoalState ToOverlayGoal(TwitchGoalSettings goal) => new()
    {
        Title = goal.Title, Current = goal.Current, Target = goal.Target, FontFace = goal.FontFace, FontSize = goal.FontSize, Currency = goal.Currency
    };

    private async Task InstallGoalInObsAsync(string goalType)
    {
        await SaveTwitchGoalsAsync();
        var result = await _obsBrowserSourceInstaller.InstallGoalAsync(goalType);
        MessageBox.Show(result, "OBS-Zielquelle", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task InstallAllGoalsSceneInObsAsync()
    {
        await SaveTwitchGoalsAsync();
        var result = await _obsBrowserSourceInstaller.InstallAllGoalsAsync();
        MessageBox.Show(result, "OBS-Zielszene", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task RefreshTwitchUsersAsync()
    {
        try
        {
            var users = await _twitchModule.GetChattersAsync();
            await Dispatcher.InvokeAsync(() =>
            {
                var merged = users
                    .Where(user => !string.IsNullOrWhiteSpace(user))
                    .Concat(_twitchUserDisplayById.Values)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(user => user, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                _twitchUserItems.Clear();
                foreach (var user in merged)
                {
                    _twitchUserItems.Add(user);
                }

                DashboardTwitchUsersHeaderText.Text =
                    $"TWITCH · USER ({_twitchUserItems.Count})";
                ServicesTwitchUsersHeaderText.Text =
                    $"User ({_twitchUserItems.Count})";
            });
        }
        catch (Exception exception)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                DashboardTwitchUsersHeaderText.Text =
                    $"TWITCH · USER ({_twitchUserItems.Count})";
                ServicesTwitchUsersHeaderText.Text =
                    $"User ({_twitchUserItems.Count}) · Aktualisierung fehlgeschlagen";
                ServicesTwitchUsersHeaderText.ToolTip = exception.Message;
            });

            // Die User-Liste ist optional. Chat und EventSub laufen bei einem
            // vorübergehenden API- oder Berechtigungsfehler weiter.
        }
    }

    private static void BrowseAlertFile(System.Windows.Controls.TextBox target, string filter)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            target.Text = dialog.FileName;
        }
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private async Task ApplyStreamerBotAlertSuppressionAsync()
    {
        if (_streamerBotSocket is null ||
            _streamerBotSocket.State != System.Net.WebSockets.WebSocketState.Open)
        {
            StreamerBotAlertControlStatusText.Text =
                "Streamer.bot ist nicht verbunden. Die Einstellung wird beim nächsten Verbindungsaufbau angewendet.";
            return;
        }

        var suppress = SuiteAlertsEnabledBox.IsChecked == true &&
                       SuppressStreamerBotAlertsBox.IsChecked == true;
        await SetStreamerBotAlertsEnabledAsync(!suppress, showSuccess: false);
    }

    private void BindStreamerBotActionSelectors()
    {
        StreamerBotDisableAlertsActionBox.ItemsSource = _streamerBotActions;
        StreamerBotEnableAlertsActionBox.ItemsSource = _streamerBotActions;
        SettingsStreamerBotDisableAlertsActionBox.ItemsSource = _streamerBotActions;
        SettingsStreamerBotEnableAlertsActionBox.ItemsSource = _streamerBotActions;
        RunOfShowStreamerBotActionBox.ItemsSource = _streamerBotActions;
        TimedAutomationStreamerBotActionBox.ItemsSource = _streamerBotActions;
    }

    private static string GetStreamerBotActionName(params object[] values)
    {
        foreach (var value in values)
        {
            if (value is System.Windows.Controls.ComboBox combo)
            {
                if (combo.SelectedItem is StreamerBotActionOption option && !string.IsNullOrWhiteSpace(option.Name)) return option.Name;
                if (!string.IsNullOrWhiteSpace(combo.Text)) return combo.Text.Trim();
            }
            else if (value is string text && !string.IsNullOrWhiteSpace(text)) return text.Trim();
        }
        return string.Empty;
    }

    private static string GetStreamerBotActionId(params System.Windows.Controls.ComboBox[] boxes)
    {
        return boxes.Select(box => box.SelectedItem as StreamerBotActionOption)
            .FirstOrDefault(option => option is not null)?.Id ?? string.Empty;
    }

    private void SyncStreamerBotActionSelectorText()
    {
        StreamerBotDisableAlertsActionBox.Text = _settings.StreamerBot.DisableAlertsActionName;
        StreamerBotEnableAlertsActionBox.Text = _settings.StreamerBot.EnableAlertsActionName;
        SettingsStreamerBotDisableAlertsActionBox.Text = _settings.StreamerBot.DisableAlertsActionName;
        SettingsStreamerBotEnableAlertsActionBox.Text = _settings.StreamerBot.EnableAlertsActionName;
    }

    private async Task RefreshStreamerBotActionsAsync(bool showStatus)
    {
        if (_streamerBotSocket is null || _streamerBotSocket.State != System.Net.WebSockets.WebSocketState.Open)
        {
            StreamerBotAlertControlStatusText.Text = "Streamer.bot ist nicht verbunden.";
            return;
        }

        try
        {
            var response = await SendStreamerBotRequestAsync(new { request = "GetActions" });
            if (!response.RootElement.TryGetProperty("actions", out var actionsElement) || actionsElement.ValueKind != System.Text.Json.JsonValueKind.Array)
                throw new InvalidOperationException("Streamer.bot hat keine Aktionsliste zurückgegeben.");

            var previousDisable = _settings.StreamerBot.DisableAlertsActionName;
            var previousEnable = _settings.StreamerBot.EnableAlertsActionName;
            _streamerBotActions.Clear();
            foreach (var action in actionsElement.EnumerateArray())
            {
                var id = action.TryGetProperty("id", out var idNode) ? idNode.GetString() ?? "" : "";
                var name = action.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? "" : "";
                var group = action.TryGetProperty("group", out var groupNode) ? groupNode.GetString() ?? "Ohne Gruppe" : "Ohne Gruppe";
                var enabled = !action.TryGetProperty("enabled", out var enabledNode) || enabledNode.GetBoolean();
                if (!string.IsNullOrWhiteSpace(name)) _streamerBotActions.Add(new StreamerBotActionOption(id, name, group, enabled));
            }

            var ordered = _streamerBotActions.OrderBy(x => x.Group).ThenBy(x => x.Name).ToList();
            _streamerBotActions.Clear();
            foreach (var option in ordered) _streamerBotActions.Add(option);
            ApplyStreamerBotActionFilter();

            SelectStreamerBotAction(StreamerBotDisableAlertsActionBox, _settings.StreamerBot.DisableAlertsActionId, previousDisable);
            SelectStreamerBotAction(SettingsStreamerBotDisableAlertsActionBox, _settings.StreamerBot.DisableAlertsActionId, previousDisable);
            SelectStreamerBotAction(StreamerBotEnableAlertsActionBox, _settings.StreamerBot.EnableAlertsActionId, previousEnable);
            SelectStreamerBotAction(SettingsStreamerBotEnableAlertsActionBox, _settings.StreamerBot.EnableAlertsActionId, previousEnable);
            if (RunOfShowStepsList.SelectedItem is RunOfShowStepSettings selectedRunOfShowStep)
                SelectStreamerBotAction(RunOfShowStreamerBotActionBox, selectedRunOfShowStep.StreamerBotActionId, selectedRunOfShowStep.StreamerBotActionName);

            var groups = ordered.Select(x => x.Group).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            StreamerBotAlertGroupsText.Text = groups.Count == 0
                ? "Keine Aktionsgruppen gefunden."
                : "Gefundene Aktionsgruppen: " + string.Join(", ", groups);
            if (showStatus) StreamerBotAlertControlStatusText.Text = $"{ordered.Count} Streamer.bot-Aktionen geladen. Wähle je eine Hilfsaktion zum Deaktivieren und Aktivieren aus.";
        }
        catch (Exception ex)
        {
            StreamerBotAlertControlStatusText.Text = "Streamer.bot-Aktionen konnten nicht geladen werden: " + ex.Message;
        }
    }

    private void ApplyStreamerBotActionFilter()
    {
        var search = ServicesStreamerBotActionSearchBox.Text?.Trim() ?? string.Empty;
        var filtered = string.IsNullOrWhiteSpace(search)
            ? _streamerBotActions.AsEnumerable()
            : _streamerBotActions.Where(action =>
                action.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                action.Group.Contains(search, StringComparison.OrdinalIgnoreCase));
        ServicesStreamerBotActionsList.ItemsSource = filtered
            .OrderByDescending(action => _streamerBotFavoriteActionIds.Contains(action.Id))
            .ThenBy(action => action.Group)
            .ThenBy(action => action.Name)
            .ToList();
        ServicesStreamerBotActionResultText.Text = string.IsNullOrWhiteSpace(search)
            ? $"{_streamerBotActions.Count} Aktionen verfügbar."
            : $"{ServicesStreamerBotActionsList.Items.Count} von {_streamerBotActions.Count} Aktionen gefunden.";
    }

    private void UpdateSelectedStreamerBotAction()
    {
        if (ServicesStreamerBotActionsList.SelectedItem is not StreamerBotActionOption action)
        {
            ServicesStreamerBotSelectedActionText.Text = "Keine Aktion ausgewählt.";
            ServicesStreamerBotActionDetailsText.Text = "Wähle eine Aktion aus, um Details und Parameter zu sehen.";
            ServicesStreamerBotFavoriteActionButton.IsEnabled = false;
            ServicesStreamerBotRunActionButton.IsEnabled = false;
            return;
        }

        ServicesStreamerBotSelectedActionText.Text = $"{action.Name} · {action.Group}";
        ServicesStreamerBotActionDetailsText.Text = $"ID: {action.Id} · Status: {(action.Enabled ? "Aktiv" : "Deaktiviert")} · Gruppe: {action.Group}";
        ServicesStreamerBotFavoriteActionButton.IsEnabled = true;
        ServicesStreamerBotFavoriteActionButton.Content = _streamerBotFavoriteActionIds.Contains(action.Id) ? "★ FAVORIT" : "☆ FAVORIT";
        ServicesStreamerBotRunActionButton.IsEnabled = action.Enabled &&
            _streamerBotSocket is { State: System.Net.WebSockets.WebSocketState.Open };
        ServicesStreamerBotActionResultText.Text = action.Enabled
            ? "Bereit zur Ausführung. Optionale Parameter können als JSON übergeben werden."
            : "Diese Streamer.bot-Aktion ist deaktiviert.";
    }

    private async Task RunSelectedStreamerBotActionAsync()
    {
        if (ServicesStreamerBotActionsList.SelectedItem is not StreamerBotActionOption action) return;
        var repeatCount = int.TryParse(ServicesStreamerBotRepeatCountBox.Text, out var count) ? Math.Clamp(count, 1, 20) : 1;
        var delayMs = int.TryParse(ServicesStreamerBotRepeatDelayBox.Text, out var delay) ? Math.Clamp(delay, 0, 10000) : 500;
        ServicesStreamerBotRepeatCountBox.Text = repeatCount.ToString();
        ServicesStreamerBotRepeatDelayBox.Text = delayMs.ToString();
        ServicesStreamerBotRunActionButton.IsEnabled = false;
        try
        {
            for (var index = 1; index <= repeatCount; index++)
            {
                ServicesStreamerBotActionResultText.Text = $"„{action.Name}“ wird ausgeführt ({index}/{repeatCount}) …";
                await ExecuteStreamerBotActionOnceAsync(action);
                if (index < repeatCount && delayMs > 0) await Task.Delay(delayMs);
            }
        }
        finally { UpdateSelectedStreamerBotAction(); }
    }

    private async Task ExecuteStreamerBotActionOnceAsync(StreamerBotActionOption action)
    {
        try
        {
            var started = DateTimeOffset.UtcNow;
            var arguments = ParseStreamerBotArguments(ServicesStreamerBotActionArgumentsBox.Text);
            arguments["source"] = "Creator Control Suite";
            arguments["manual"] = true;
            using var response = await SendStreamerBotRequestAsync(new
            {
                request = "DoAction",
                action = new { id = action.Id, name = action.Name },
                args = arguments
            });
            var status = response.RootElement.TryGetProperty("status", out var node) ? node.GetString() : null;
            ServicesStreamerBotLastResponseBox.Text = System.Text.Json.JsonSerializer.Serialize(
                response.RootElement,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Streamer.bot hat die Aktion nicht bestätigt.");
            var elapsed = DateTimeOffset.UtcNow - started;
            ServicesStreamerBotActionResultText.Text = $"Aktion erfolgreich ausgeführt · {elapsed.TotalMilliseconds:0} ms";
            ServicesStreamerBotActionResultText.Foreground = Brushes.LightGreen;
            AddStreamerBotHistory(action, true, $"{elapsed.TotalMilliseconds:0} ms", ServicesStreamerBotActionArgumentsBox.Text, ServicesStreamerBotLastResponseBox.Text);
        }
        catch (Exception exception)
        {
            ServicesStreamerBotActionResultText.Text = "Aktion fehlgeschlagen: " + exception.Message;
            ServicesStreamerBotActionResultText.Foreground = Brushes.IndianRed;
            ServicesStreamerBotLastResponseBox.Text = exception.Message;
            AddStreamerBotHistory(action, false, exception.Message, ServicesStreamerBotActionArgumentsBox.Text, exception.Message);
            throw;
        }
    }

    private void SaveSelectedStreamerBotTemplate()
    {
        if (ServicesStreamerBotActionsList.SelectedItem is not StreamerBotActionOption action)
        {
            ServicesStreamerBotActionResultText.Text = "Zum Speichern einer Vorlage zuerst eine Aktion auswählen.";
            return;
        }
        try { _ = ParseStreamerBotArguments(ServicesStreamerBotActionArgumentsBox.Text); }
        catch (Exception exception) { ServicesStreamerBotActionResultText.Text = "Vorlage nicht gespeichert: " + exception.Message; return; }
        var name = ServicesStreamerBotTemplateNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = action.Name;
        var existing = _streamerBotActionTemplates.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) _streamerBotActionTemplates.Remove(existing);
        var template = new StreamerBotActionTemplate(name, action.Id, action.Name, ServicesStreamerBotActionArgumentsBox.Text.Trim());
        _streamerBotActionTemplates.Add(template);
        ServicesStreamerBotTemplateBox.SelectedItem = template;
        ServicesStreamerBotActionResultText.Text = $"Vorlage „{name}“ gespeichert.";
    }

    private void LoadSelectedStreamerBotTemplate()
    {
        if (ServicesStreamerBotTemplateBox.SelectedItem is not StreamerBotActionTemplate template) return;
        var action = _streamerBotActions.FirstOrDefault(x => string.Equals(x.Id, template.ActionId, StringComparison.OrdinalIgnoreCase))
            ?? _streamerBotActions.FirstOrDefault(x => string.Equals(x.Name, template.ActionName, StringComparison.OrdinalIgnoreCase));
        if (action is not null) ServicesStreamerBotActionsList.SelectedItem = action;
        ServicesStreamerBotActionArgumentsBox.Text = template.ArgumentsJson;
        ServicesStreamerBotTemplateNameBox.Text = template.Name;
        ServicesStreamerBotActionResultText.Text = $"Vorlage „{template.Name}“ geladen.";
    }

    private void DeleteSelectedStreamerBotTemplate()
    {
        if (ServicesStreamerBotTemplateBox.SelectedItem is not StreamerBotActionTemplate template) return;
        _streamerBotActionTemplates.Remove(template);
        ServicesStreamerBotActionResultText.Text = $"Vorlage „{template.Name}“ gelöscht.";
    }

    private async Task ScheduleSelectedStreamerBotActionAsync()
    {
        if (ServicesStreamerBotActionsList.SelectedItem is not StreamerBotActionOption action) return;
        var minutes = double.TryParse(ServicesStreamerBotScheduleMinutesBox.Text, out var value) ? Math.Clamp(value, 0.05, 1440) : 1;
        CancelScheduledStreamerBotAction();
        _streamerBotScheduledActionCts = new CancellationTokenSource();
        ServicesStreamerBotCancelScheduleButton.IsEnabled = true;
        ServicesStreamerBotActionResultText.Text = $"„{action.Name}“ startet in {minutes:0.##} Minute(n).";
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(minutes), _streamerBotScheduledActionCts.Token);
            await ExecuteStreamerBotActionOnceAsync(action);
        }
        catch (OperationCanceledException) { ServicesStreamerBotActionResultText.Text = "Geplante Ausführung wurde abgebrochen."; }
        catch (Exception) { }
        finally
        {
            _streamerBotScheduledActionCts?.Dispose();
            _streamerBotScheduledActionCts = null;
            ServicesStreamerBotCancelScheduleButton.IsEnabled = false;
        }
    }

    private void CancelScheduledStreamerBotAction()
    {
        _streamerBotScheduledActionCts?.Cancel();
    }

    private Dictionary<string, object?> ParseStreamerBotArguments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, object?>();
        using var document = System.Text.Json.JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            throw new InvalidOperationException("Die Parameter müssen ein JSON-Objekt sein.");
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(document.RootElement.GetRawText())
            ?? new Dictionary<string, object?>();
    }

    private void ToggleSelectedStreamerBotFavorite()
    {
        if (ServicesStreamerBotActionsList.SelectedItem is not StreamerBotActionOption action) return;
        if (!_streamerBotFavoriteActionIds.Add(action.Id)) _streamerBotFavoriteActionIds.Remove(action.Id);
        ApplyStreamerBotActionFilter();
        ServicesStreamerBotActionsList.SelectedItem = action;
        UpdateSelectedStreamerBotAction();
    }

    private void AddStreamerBotHistory(StreamerBotActionOption action, bool success, string detail, string argumentsJson, string responseJson)
    {
        _streamerBotExecutionHistory.Insert(0, new StreamerBotExecutionHistoryItem(DateTimeOffset.Now, action.Name, success, detail, argumentsJson, responseJson));
        while (_streamerBotExecutionHistory.Count > 50) _streamerBotExecutionHistory.RemoveAt(_streamerBotExecutionHistory.Count - 1);
    }

    private void FormatStreamerBotArgumentsJson()
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(ServicesStreamerBotActionArgumentsBox.Text);
            ServicesStreamerBotActionArgumentsBox.Text = System.Text.Json.JsonSerializer.Serialize(
                document.RootElement,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            ServicesStreamerBotActionResultText.Text = "JSON wurde geprüft und formatiert.";
            ServicesStreamerBotActionResultText.Foreground = Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            ServicesStreamerBotActionResultText.Text = "JSON ist ungültig: " + exception.Message;
            ServicesStreamerBotActionResultText.Foreground = Brushes.IndianRed;
        }
    }

    private void ExportStreamerBotHistoryCsv()
    {
        if (_streamerBotExecutionHistory.Count == 0)
        {
            ServicesStreamerBotActionResultText.Text = "Es sind keine Historieneinträge zum Exportieren vorhanden.";
            return;
        }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Streamer.bot-Ausführungshistorie exportieren",
            Filter = "CSV-Datei|*.csv",
            FileName = $"streamerbot-history-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };
        if (dialog.ShowDialog(this) != true) return;
        static string Csv(string? value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
        var lines = new List<string> { "Zeitpunkt;Aktion;Erfolg;Detail;Argumente;Antwort" };
        lines.AddRange(_streamerBotExecutionHistory.Select(item => string.Join(";",
            Csv(item.Timestamp.ToString("O")), Csv(item.ActionName), Csv(item.Success ? "Ja" : "Nein"),
            Csv(item.Detail), Csv(item.ArgumentsJson), Csv(item.ResponseJson))));
        System.IO.File.WriteAllLines(dialog.FileName, lines, new System.Text.UTF8Encoding(true));
        ServicesStreamerBotActionResultText.Text = $"Historie exportiert: {dialog.FileName}";
        ServicesStreamerBotActionResultText.Foreground = Brushes.LightGreen;
    }

    private async Task ReconnectStreamerBotAsync()
    {
        ServicesStreamerBotDiagnosticText.Text = "Verbindung wird neu aufgebaut …";
        ServicesStreamerBotDiagnosticText.Foreground = Brushes.Gold;
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await DisconnectStreamerBotAsync();
                await Task.Delay(attempt * 400);
                await ConnectStreamerBotAsync();
                if (_streamerBotSocket?.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    await RefreshStreamerBotActionsAsync(true);
                    ServicesStreamerBotDiagnosticText.Text = $"Neu verbunden · Versuch {attempt}/3 · Aktionen aktualisiert.";
                    ServicesStreamerBotDiagnosticText.Foreground = Brushes.LightGreen;
                    return;
                }
            }
            catch (Exception exception) { lastError = exception; }
        }
        ServicesStreamerBotDiagnosticText.Text = "Neuverbinden fehlgeschlagen: " + (lastError?.Message ?? "Keine WebSocket-Verbindung.");
        ServicesStreamerBotDiagnosticText.Foreground = Brushes.IndianRed;
    }

    private async Task DiagnoseStreamerBotAsync()
    {
        if (_streamerBotSocket is null || _streamerBotSocket.State != System.Net.WebSockets.WebSocketState.Open)
        {
            ServicesStreamerBotDiagnosticText.Text = "Nicht verbunden – zuerst die WebSocket-Verbindung herstellen.";
            ServicesStreamerBotDiagnosticText.Foreground = Brushes.IndianRed;
            return;
        }

        try
        {
            var started = DateTimeOffset.UtcNow;
            using var response = await SendStreamerBotRequestAsync(new { request = "GetActions" }, TimeSpan.FromSeconds(5));
            var elapsed = DateTimeOffset.UtcNow - started;
            var actionCount = response.RootElement.TryGetProperty("actions", out var actions) && actions.ValueKind == System.Text.Json.JsonValueKind.Array
                ? actions.GetArrayLength()
                : 0;
            ServicesStreamerBotDiagnosticText.Text = $"WebSocket OK · Antwort {elapsed.TotalMilliseconds:0} ms · {actionCount} Aktionen · Event-Listener {(_streamerBotEventSocket?.State == System.Net.WebSockets.WebSocketState.Open ? "aktiv" : "inaktiv")}";
            ServicesStreamerBotDiagnosticText.Foreground = Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            ServicesStreamerBotDiagnosticText.Text = "Diagnose fehlgeschlagen: " + exception.Message;
            ServicesStreamerBotDiagnosticText.Foreground = Brushes.IndianRed;
        }
    }

    private static void SelectStreamerBotAction(System.Windows.Controls.ComboBox box, string id, string name)
    {
        if (box.ItemsSource is not IEnumerable<StreamerBotActionOption> actions) { box.Text = name; return; }
        var selected = actions.FirstOrDefault(x => !string.IsNullOrWhiteSpace(id) && string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? actions.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (selected is not null) box.SelectedItem = selected;
        else box.Text = name;
    }

    private async Task<System.Text.Json.JsonDocument> SendStreamerBotRequestAsync(object requestBody, TimeSpan? timeout = null)
    {
        if (_streamerBotSocket is null || _streamerBotSocket.State != System.Net.WebSockets.WebSocketState.Open)
            throw new InvalidOperationException("Streamer.bot ist nicht verbunden.");

        await _streamerBotRequestGate.WaitAsync();
        try
        {
            var id = "ccs-" + Guid.NewGuid().ToString("N");
            var json = System.Text.Json.JsonSerializer.Serialize(requestBody);
            using var bodyDocument = System.Text.Json.JsonDocument.Parse(json);
            var dictionary = bodyDocument.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
            dictionary["id"] = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(id)).RootElement.Clone();
            var payload = System.Text.Json.JsonSerializer.Serialize(dictionary);
            var bytes = System.Text.Encoding.UTF8.GetBytes(payload);
            await _streamerBotSocket.SendAsync(new ArraySegment<byte>(bytes), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);

            using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(8));
            var buffer = new byte[64 * 1024];
            using var stream = new MemoryStream();
            while (true)
            {
                var result = await _streamerBotSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) throw new InvalidOperationException("Streamer.bot hat die WebSocket-Verbindung geschlossen.");
                stream.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;
                var response = System.Text.Json.JsonDocument.Parse(stream.ToArray());
                if (!response.RootElement.TryGetProperty("id", out var responseId) || !string.Equals(responseId.GetString(), id, StringComparison.Ordinal))
                {
                    response.Dispose();
                    stream.SetLength(0);
                    continue;
                }
                if (response.RootElement.TryGetProperty("status", out var status) && string.Equals(status.GetString(), "error", StringComparison.OrdinalIgnoreCase))
                {
                    var message = response.RootElement.TryGetProperty("message", out var messageNode) ? messageNode.GetString() : "Unbekannter Streamer.bot-Fehler";
                    response.Dispose();
                    throw new InvalidOperationException(message);
                }
                return response;
            }
        }
        finally { _streamerBotRequestGate.Release(); }
    }

    private async Task SetStreamerBotAlertsEnabledAsync(bool enabled, bool showSuccess = true)
    {
        if (_streamerBotSocket is null || _streamerBotSocket.State != System.Net.WebSockets.WebSocketState.Open)
        {
            StreamerBotAlertControlStatusText.Text = "Streamer.bot ist nicht verbunden.";
            return;
        }

        var primaryBox = enabled ? StreamerBotEnableAlertsActionBox : StreamerBotDisableAlertsActionBox;
        var settingsBox = enabled ? SettingsStreamerBotEnableAlertsActionBox : SettingsStreamerBotDisableAlertsActionBox;
        var selected = primaryBox.SelectedItem as StreamerBotActionOption ?? settingsBox.SelectedItem as StreamerBotActionOption;
        var actionName = selected?.Name ?? GetStreamerBotActionName(primaryBox, settingsBox, enabled ? _settings.StreamerBot.EnableAlertsActionName : _settings.StreamerBot.DisableAlertsActionName);
        var actionId = selected?.Id ?? (enabled ? _settings.StreamerBot.EnableAlertsActionId : _settings.StreamerBot.DisableAlertsActionId);
        if (string.IsNullOrWhiteSpace(actionName) && string.IsNullOrWhiteSpace(actionId))
        {
            StreamerBotAlertControlStatusText.Text = "Bitte zuerst eine vorhandene Streamer.bot-Hilfsaktion auswählen.";
            return;
        }

        try
        {
            var action = !string.IsNullOrWhiteSpace(actionId) ? new { id = actionId, name = actionName } : new { id = "", name = actionName };
            using var response = await SendStreamerBotRequestAsync(new
            {
                request = "DoAction",
                action,
                args = new { source = "Creator Control Suite", alertsEnabled = enabled }
            });
            var status = response.RootElement.TryGetProperty("status", out var statusNode) ? statusNode.GetString() : null;
            if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Streamer.bot hat die Aktion nicht bestätigt.");

            if (enabled)
            {
                _settings.StreamerBot.EnableAlertsActionName = actionName;
                _settings.StreamerBot.EnableAlertsActionId = actionId;
            }
            else
            {
                _settings.StreamerBot.DisableAlertsActionName = actionName;
                _settings.StreamerBot.DisableAlertsActionId = actionId;
            }
            StreamerBotAlertControlStatusText.Text = showSuccess
                ? $"Streamer.bot hat die Aktion „{actionName}“ bestätigt."
                : enabled ? "Streamer.bot-Alerts bleiben aktiv." : "Suite-Alerts aktiv: Deaktivierungsaktion wurde von Streamer.bot bestätigt.";
        }
        catch (Exception ex)
        {
            StreamerBotAlertControlStatusText.Text = "Streamer.bot-Alertsteuerung fehlgeschlagen: " + ex.Message;
        }
    }

    private async Task ConnectStreamerBotAsync()
    {
        await DisconnectStreamerBotAsync();
        try
        {
            _streamerBotSocket = new System.Net.WebSockets.ClientWebSocket();
            var endpoint = string.IsNullOrWhiteSpace(_settings.StreamerBot.Endpoint) ? "/" : _settings.StreamerBot.Endpoint;
            if (!endpoint.StartsWith('/')) endpoint = "/" + endpoint;
            if (!string.IsNullOrWhiteSpace(_settings.StreamerBot.Password))
            {
                _streamerBotSocket.Options.SetRequestHeader("Authorization", "Bearer " + _settings.StreamerBot.Password);
                var separator = endpoint.Contains('?') ? "&" : "?";
                endpoint += separator + "password=" + Uri.EscapeDataString(_settings.StreamerBot.Password);
            }
            await _streamerBotSocket.ConnectAsync(new Uri($"ws://{_settings.StreamerBot.Host}:{_settings.StreamerBot.Port}{endpoint}"), CancellationToken.None);
            await RefreshStreamerBotActionsAsync(false);
            await StartStreamerBotEventListenerAsync();
            ServicesStreamerBotStatusText.Text = $"Verbunden · {_settings.StreamerBot.Host}:{_settings.StreamerBot.Port}";
            ServicesStreamerBotDiagnosticText.Text = $"WebSocket verbunden · {_streamerBotActions.Count} Aktionen geladen · Event-Listener aktiv";
            ServicesStreamerBotDiagnosticText.Foreground = Brushes.LightGreen;
            ServicesStreamerBotStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
            StreamerBotDashboardStatus.Text = "VERBUNDEN";
            StreamerBotDashboardLamp.Fill =
                System.Windows.Media.Brushes.LimeGreen;
            ServicesStreamerBotServicesList.ItemsSource = new[] { "WebSocket API · verbunden", "OBS · Status über Streamer.bot API verfügbar", "Twitch · Status über Streamer.bot API verfügbar", "YouTube · falls in Streamer.bot eingerichtet" };
            await ApplyStreamerBotAlertSuppressionAsync();
        }
        catch (Exception ex)
        {
            ServicesStreamerBotStatusText.Text = ex.Message;
            ServicesStreamerBotStatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
        }
    }

    private async Task StartStreamerBotEventListenerAsync()
    {
        _streamerBotEventCts?.Cancel();
        _streamerBotEventSocket?.Dispose();
        _streamerBotEventCts = new CancellationTokenSource();
        _streamerBotEventSocket = new System.Net.WebSockets.ClientWebSocket();

        var endpoint = string.IsNullOrWhiteSpace(_settings.StreamerBot.Endpoint) ? "/" : _settings.StreamerBot.Endpoint;
        if (!endpoint.StartsWith('/')) endpoint = "/" + endpoint;
        if (!string.IsNullOrWhiteSpace(_settings.StreamerBot.Password))
        {
            _streamerBotEventSocket.Options.SetRequestHeader("Authorization", "Bearer " + _settings.StreamerBot.Password);
            endpoint += (endpoint.Contains('?') ? "&" : "?") + "password=" + Uri.EscapeDataString(_settings.StreamerBot.Password);
        }

        await _streamerBotEventSocket.ConnectAsync(
            new Uri($"ws://{_settings.StreamerBot.Host}:{_settings.StreamerBot.Port}{endpoint}"),
            _streamerBotEventCts.Token);

        var subscribe = System.Text.Json.JsonSerializer.Serialize(new
        {
            request = "Subscribe",
            id = "ccs-events-" + Guid.NewGuid().ToString("N"),
            events = new
            {
                Twitch = new[] { "Follow", "Cheer", "Sub", "ReSub", "GiftSub", "GiftBomb", "Raid" },
                General = new[] { "Custom" }
            }
        });
        var bytes = System.Text.Encoding.UTF8.GetBytes(subscribe);
        await _streamerBotEventSocket.SendAsync(bytes, System.Net.WebSockets.WebSocketMessageType.Text, true, _streamerBotEventCts.Token);
        _ = Task.Run(() => ListenForStreamerBotAlertEventsAsync(_streamerBotEventCts.Token));
    }

    private async Task ListenForStreamerBotAlertEventsAsync(CancellationToken token)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (!token.IsCancellationRequested && _streamerBotEventSocket is { State: System.Net.WebSockets.WebSocketState.Open })
            {
                using var stream = new MemoryStream();
                System.Net.WebSockets.WebSocketReceiveResult result;
                do
                {
                    result = await _streamerBotEventSocket.ReceiveAsync(buffer, token);
                    if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) return;
                    stream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                using var document = System.Text.Json.JsonDocument.Parse(stream.ToArray());
                var root = document.RootElement;
                if (!root.TryGetProperty("event", out var eventNode)) continue;
                var source = eventNode.TryGetProperty("source", out var sourceNode) ? sourceNode.GetString() ?? "Streamer.bot" : "Streamer.bot";
                var type = eventNode.TryGetProperty("type", out var typeNode) ? typeNode.GetString() ?? "Alert" : "Alert";
                var normalized = (source + " " + type).ToLowerInvariant();
                var summary = BuildStreamerBotEventSummary(root, source, type);

                await Dispatcher.InvokeAsync(() =>
                {
                    _streamerBotLiveEvents.Insert(0, new StreamerBotLiveEventItem(DateTimeOffset.Now, source, type, summary));
                    while (_streamerBotLiveEvents.Count > 100)
                        _streamerBotLiveEvents.RemoveAt(_streamerBotLiveEvents.Count - 1);
                    ServicesStreamerBotLiveEventStatusText.Text = $"Letztes Ereignis: {type} · {DateTime.Now:HH:mm:ss}";
                    ServicesStreamerBotLiveEventsList.ScrollIntoView(_streamerBotLiveEvents.FirstOrDefault());
                });

                var isKnownAlert = normalized.Contains("follow") || normalized.Contains("cheer") || normalized.Contains("sub") ||
                                   normalized.Contains("raid") || normalized.Contains("alert");
                if (!isKnownAlert) continue;

                var id = Guid.NewGuid().ToString("N");
                _ = PulseExternalAlertAsync("Streamer.bot", id, TimeSpan.FromSeconds(8));
                await Dispatcher.InvokeAsync(() =>
                {
                    ServicesSpotifyAlertMuteStatusText.Text = $"Streamer.bot-Alert erkannt: {type}";
                    ServicesSpotifyAlertMuteStatusText.Foreground = Brushes.Orange;
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _appLogger.Write(AppLogLevel.Warning, "Streamer.bot", "Event-Listener für Alert-Ducking wurde beendet.", exception);
        }
    }


    private static string BuildStreamerBotEventSummary(System.Text.Json.JsonElement root, string source, string type)
    {
        static string? ReadString(System.Text.Json.JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String)
                    return value.GetString();
            }
            return null;
        }

        var data = root.TryGetProperty("data", out var dataNode) && dataNode.ValueKind == System.Text.Json.JsonValueKind.Object
            ? dataNode
            : root;
        var user = ReadString(data, "user_name", "userName", "displayName", "user", "from");
        var message = ReadString(data, "message", "text", "input", "reason");
        var amount = ReadString(data, "amount", "bits", "months", "viewers");
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(user)) parts.Add(user);
        if (!string.IsNullOrWhiteSpace(amount)) parts.Add(amount);
        if (!string.IsNullOrWhiteSpace(message)) parts.Add(message);
        return parts.Count > 0 ? string.Join(" · ", parts) : $"{source} · {type}";
    }

    private async Task DisconnectStreamerBotAsync()
    {
        _streamerBotEventCts?.Cancel();
        if (_streamerBotEventSocket is { State: System.Net.WebSockets.WebSocketState.Open })
        {
            try { await _streamerBotEventSocket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "disconnect", CancellationToken.None); } catch { }
        }
        _streamerBotEventSocket?.Dispose();
        _streamerBotEventSocket = null;
        _streamerBotEventCts?.Dispose();
        _streamerBotEventCts = null;

        if (_streamerBotSocket is { State: System.Net.WebSockets.WebSocketState.Open })
        {
            try { await _streamerBotSocket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "disconnect", CancellationToken.None); } catch { }
        }
        _streamerBotSocket?.Dispose();
        _streamerBotSocket = null;
        ServicesStreamerBotStatusText.Text = "Nicht verbunden";
        ServicesStreamerBotStatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
        StreamerBotDashboardStatus.Text = "NICHT VERBUNDEN";
        StreamerBotDashboardLamp.Fill =
            System.Windows.Media.Brushes.IndianRed;
        ServicesStreamerBotServicesList.ItemsSource = null;
        ServicesStreamerBotActionsList.ItemsSource = null;
        ServicesStreamerBotDiagnosticText.Text = "Verbindung getrennt.";
        ServicesStreamerBotDiagnosticText.Foreground = Brushes.Gray;
        ServicesStreamerBotSelectedActionText.Text = "Keine Aktion ausgewählt.";
        ServicesStreamerBotRunActionButton.IsEnabled = false;
    
        RefreshDashboardServiceActionButtons();
}

    private void SetWorkflowVisualStage(string stage, string summary)
    {
        var inactive = new SolidColorBrush(Color.FromRgb(51, 55, 59));
        var complete = new SolidColorBrush(Color.FromRgb(45, 125, 70));
        var active = new SolidColorBrush(Color.FromRgb(112, 70, 190));

        WorkflowPrepareNode.Background = inactive;
        WorkflowReadyNode.Background = inactive;
        WorkflowStartNode.Background = inactive;
        WorkflowLiveNode.Background = inactive;
        WorkflowEndNode.Background = inactive;
        WorkflowRaidNode.Background = inactive;

        switch (stage)
        {
            case "Ready":
                WorkflowPrepareNode.Background = complete;
                WorkflowReadyNode.Background = active;
                break;
            case "Start":
                WorkflowPrepareNode.Background = complete;
                WorkflowReadyNode.Background = complete;
                WorkflowStartNode.Background = active;
                break;
            case "Live":
                WorkflowPrepareNode.Background = complete;
                WorkflowReadyNode.Background = complete;
                WorkflowStartNode.Background = complete;
                WorkflowLiveNode.Background = active;
                break;
            case "End":
                WorkflowPrepareNode.Background = complete;
                WorkflowReadyNode.Background = complete;
                WorkflowStartNode.Background = complete;
                WorkflowLiveNode.Background = complete;
                WorkflowEndNode.Background = active;
                break;
            case "Raid":
                WorkflowPrepareNode.Background = complete;
                WorkflowReadyNode.Background = complete;
                WorkflowStartNode.Background = complete;
                WorkflowLiveNode.Background = complete;
                WorkflowEndNode.Background = complete;
                WorkflowRaidNode.Background = active;
                break;
            default:
                WorkflowPrepareNode.Background = active;
                break;
        }

        DashboardCommandCenterSummaryText.Text = summary;
    }

    private async Task RunDashboardPreflightAsync()
    {
        _dashboardPreflightItems.Clear();
        AddDashboardNotification($"Preflight gestartet.", "Info");

        void AddCheck(bool ok, string text)
        {
            _dashboardPreflightItems.Add($"{(ok ? "✓" : "⚠")} {text}");
        }

        AddCheck(_obsClient.IsConnected, "OBS WebSocket verbunden");
        AddCheck(_twitchModule.GetSnapshot().Authenticated, "Twitch verbunden");
        AddCheck(_spotifyModule.GetSnapshot().Authenticated, "Spotify verbunden");
        AddCheck(_streamerBotSocket is not null && _streamerBotSocket.State == System.Net.WebSockets.WebSocketState.Open, "Streamer.bot verbunden");
        AddCheck(!string.IsNullOrWhiteSpace(_settings.Obs.StartScene), $"Startszene: {_settings.Obs.StartScene}");
        AddCheck(!string.IsNullOrWhiteSpace(_settings.Obs.LiveScene), $"Live-Szene: {_settings.Obs.LiveScene}");
        AddCheck(!string.IsNullOrWhiteSpace(DashboardTwitchTitleBox.Text), "Streamtitel gesetzt");
        AddCheck(DashboardTwitchCategoryResultsBox.SelectedItem is not null || !string.IsNullOrWhiteSpace(DashboardTwitchCategorySearchBox.Text), "Twitch-Kategorie gewählt oder suchbar");
        AddCheck(!_settings.Workflow.AutoStartSpotifyPlaylist || !string.IsNullOrWhiteSpace(_settings.Spotify.StartPlaylistUri), "Spotify-Startplaylist konfiguriert");
        AddCheck(!_settings.Twitch.RaidOnStreamEnd || !string.IsNullOrWhiteSpace(_settings.Twitch.SelectedRaidChannel), "Raid-Ziel für Streamende gesetzt");

        var warningCount = _dashboardPreflightItems.Count(x => x.StartsWith("⚠", StringComparison.Ordinal));
        DashboardWorkflowStageText.Text = warningCount == 0
            ? "BEREIT → START → LIVE → ENDE → RAID"
            : $"VORBEREITEN · {warningCount} Punkt(e) prüfen";

        AddDashboardNotification(
            warningCount == 0
                ? "Preflight erfolgreich: Stream ist bereit."
                : $"Preflight: {warningCount} Punkt(e) benötigen Aufmerksamkeit.",
            warningCount == 0 ? "Info" : "Warnung");

        await Task.CompletedTask;
    }

    private async Task SwitchDashboardConfiguredSceneAsync(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            AddDashboardNotification($"Kein Szenenname konfiguriert.", "Info");
            return;
        }

        DashboardSceneBox.SelectedItem = sceneName;
        if (DashboardSceneBox.SelectedItem is null)
        {
            DashboardSceneBox.Text = sceneName;
        }

        await SwitchDashboardSceneAsync();
        AddDashboardNotification($"Szenenwechsel angefordert: {sceneName}", "Info");
    }


    private async Task ApplyDashboardProfileAndPrepareAsync()
    {
        if (DashboardProfileBox.SelectedItem is not ProfileSummary summary)
        {
            AddDashboardNotification($"Kein Stream-Profil ausgewählt.", "Info");
            return;
        }

        try
        {
            DashboardWorkflowStageText.Text = $"PROFIL LADEN · {summary.Name}";
            await _profileService.ApplyAsync(summary.Id);
            await LoadSettingsAsync();

            AddDashboardNotification($"Profil „{summary.Name}“ wurde angewendet.", "Info");

            DashboardWorkflowStageText.Text = $"PROFIL {summary.Name} · STREAM VORBEREITEN";
            await PrepareStreamAsync();
        }
        catch (Exception exception)
        {
            DashboardWorkflowStageText.Text = "PROFIL FEHLER";
            AddDashboardNotification($"Profil konnte nicht angewendet werden: {exception.Message}", "Fehler");
        }
    }

    private sealed class DashboardNotificationEntry
    {
        public DateTimeOffset Timestamp { get; set; }
        public string Severity { get; set; } = "Info";
        public string Message { get; set; } = "";
        public bool IsRead { get; set; }
    }

    private string GetDashboardNotificationFilePath()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CreatorControlSuite");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "notifications.json");
    }

    private void AddDashboardNotification(string message, string severity = "Info")
    {
        var normalizedSeverity = severity switch
        {
            "Error" => "Fehler",
            "Warning" => "Warnung",
            "Fehler" => "Fehler",
            "Warnung" => "Warnung",
            _ => "Info"
        };

        _dashboardNotifications.Add(new DashboardNotificationEntry
        {
            Timestamp = DateTimeOffset.Now,
            Severity = normalizedSeverity,
            Message = message,
            IsRead = false
        });

        if (_dashboardNotifications.Count > 250)
        {
            _dashboardNotifications.RemoveRange(0, _dashboardNotifications.Count - 250);
        }

        RefreshDashboardNotificationView();
        _ = SaveDashboardNotificationsAsync();
    }

    private void RefreshDashboardNotificationView()
    {
        if (DashboardNotificationList is null || DashboardNotificationFilterBox is null)
        {
            return;
        }

        var selectedFilter = (DashboardNotificationFilterBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString()
            ?? "Alle";

        IEnumerable<DashboardNotificationEntry> query = _dashboardNotifications;
        query = selectedFilter switch
        {
            "Info" => query.Where(item => item.Severity == "Info"),
            "Warnungen" => query.Where(item => item.Severity == "Warnung"),
            "Fehler" => query.Where(item => item.Severity == "Fehler"),
            _ => query
        };

        _dashboardNotificationItems.Clear();
        foreach (var item in query.OrderByDescending(item => item.Timestamp).Take(100))
        {
            var icon = item.Severity switch
            {
                "Fehler" => "✕",
                "Warnung" => "⚠",
                _ => "ℹ"
            };
            var unread = item.IsRead ? "" : " •";
            _dashboardNotificationItems.Add(
                $"{icon} {item.Timestamp:HH:mm:ss} · {item.Message}{unread}");
        }

        var unreadCount = _dashboardNotifications.Count(item => !item.IsRead);
        DashboardNotificationCountText.Text = unreadCount == 0
            ? $"{_dashboardNotifications.Count} Meldungen"
            : $"{unreadCount} ungelesen";
    }

    private async Task LoadDashboardNotificationsAsync()
    {
        _dashboardNotifications.Clear();
        var path = GetDashboardNotificationFilePath();
        if (File.Exists(path))
        {
            try
            {
                var json = await File.ReadAllTextAsync(path);
                var items = System.Text.Json.JsonSerializer.Deserialize<List<DashboardNotificationEntry>>(json);
                if (items is not null)
                {
                    _dashboardNotifications.AddRange(items.TakeLast(250));
                }
            }
            catch
            {
                // A corrupt notification cache must never prevent application startup.
            }
        }

        RefreshDashboardNotificationView();
    }

    private async Task SaveDashboardNotificationsAsync()
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(
                _dashboardNotifications.TakeLast(250),
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(GetDashboardNotificationFilePath(), json);
        }
        catch
        {
            // Notifications are non-critical and must not interrupt streaming workflows.
        }
    }

    private sealed class StreamStatisticsRow
    {
        public string Date { get; init; } = "";
        public string Duration { get; init; } = "";
        public double AverageViewers { get; init; }
        public int PeakViewers { get; init; }
        public int FollowersGained { get; init; }
        public int NewSubscriptions { get; init; }
        public int GiftSubscriptions { get; init; }
        public int BitsCheered { get; init; }
        public string Category { get; init; } = "";
        public string Title { get; init; } = "";
        public long DurationSeconds { get; init; }
        public DateTimeOffset StartedAt { get; init; }
    }

    private async Task RefreshStatisticsAsync()
    {
        var rows = new List<StreamStatisticsRow>();
        var path = GetStreamHistoryFilePath();

        if (File.Exists(path))
        {
            foreach (var line in await File.ReadAllLinesAsync(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    using var document = System.Text.Json.JsonDocument.Parse(line);
                    var item = document.RootElement;

                    var startedAt = item.TryGetProperty("StartedAt", out var startedProperty)
                        ? startedProperty.GetDateTimeOffset().ToLocalTime()
                        : DateTimeOffset.MinValue;
                    var durationSeconds = item.TryGetProperty("DurationSeconds", out var durationProperty)
                        ? durationProperty.GetInt64()
                        : 0;
                    var averageViewers = item.TryGetProperty("AverageViewers", out var averageProperty)
                        ? averageProperty.GetDouble()
                        : 0;
                    var peakViewers = item.TryGetProperty("PeakViewers", out var peakProperty)
                        ? peakProperty.GetInt32()
                        : 0;
                    var followers = item.TryGetProperty("FollowersGained", out var followersProperty)
                        ? followersProperty.GetInt32()
                        : 0;
                    var newSubscriptions = item.TryGetProperty("NewSubscriptions", out var subscriptionsProperty)
                        ? subscriptionsProperty.GetInt32()
                        : 0;
                    var giftSubscriptions = item.TryGetProperty("GiftSubscriptions", out var giftSubscriptionsProperty)
                        ? giftSubscriptionsProperty.GetInt32()
                        : 0;
                    var bitsCheered = item.TryGetProperty("BitsCheered", out var bitsProperty)
                        ? bitsProperty.GetInt32()
                        : 0;
                    var category = item.TryGetProperty("Category", out var categoryProperty)
                        ? categoryProperty.ToString()
                        : "";
                    var title = item.TryGetProperty("Title", out var titleProperty)
                        ? titleProperty.GetString() ?? ""
                        : "";

                    rows.Add(new StreamStatisticsRow
                    {
                        Date = startedAt == DateTimeOffset.MinValue ? "-" : startedAt.ToString("dd.MM.yyyy HH:mm"),
                        Duration = TimeSpan.FromSeconds(Math.Max(0, durationSeconds)).ToString(@"hh\:mm\:ss"),
                        AverageViewers = Math.Round(averageViewers, 1),
                        PeakViewers = peakViewers,
                        FollowersGained = followers,
                        NewSubscriptions = newSubscriptions,
                        GiftSubscriptions = giftSubscriptions,
                        BitsCheered = bitsCheered,
                        Category = string.IsNullOrWhiteSpace(category) ? "Nicht angegeben" : category,
                        Title = title,
                        DurationSeconds = durationSeconds,
                        StartedAt = startedAt
                    });
                }
                catch
                {
                    // Ignore malformed history entries and continue with valid sessions.
                }
            }
        }

        var ordered = rows
            .OrderByDescending(row => row.StartedAt)
            .ToList();

        StatisticsSessionsGrid.ItemsSource = ordered;

        var totalStreams = rows.Count;
        var totalSeconds = rows.Sum(row => Math.Max(0, row.DurationSeconds));
        var weightedAverageViewers = totalSeconds > 0
            ? rows.Sum(row => row.AverageViewers * Math.Max(0, row.DurationSeconds)) / totalSeconds
            : rows.Count > 0 ? rows.Average(row => row.AverageViewers) : 0;
        var peak = rows.Count > 0 ? rows.Max(row => row.PeakViewers) : 0;
        var followersTotal = rows.Sum(row => row.FollowersGained);
        var averageDurationSeconds = rows.Count > 0 ? totalSeconds / rows.Count : 0;

        StatisticsTotalStreamsText.Text = totalStreams.ToString();
        StatisticsTotalDurationText.Text = FormatStatisticsDuration(totalSeconds);
        StatisticsAverageViewersText.Text = weightedAverageViewers.ToString("0.0");
        StatisticsPeakViewersText.Text = peak.ToString();
        StatisticsFollowersText.Text = followersTotal.ToString();
        StatisticsAverageDurationText.Text = FormatStatisticsDuration(averageDurationSeconds);

        StatisticsCategoriesList.Items.Clear();
        foreach (var category in rows
                     .GroupBy(row => row.Category, StringComparer.OrdinalIgnoreCase)
                     .Select(group => new
                     {
                         Name = group.Key,
                         Count = group.Count(),
                         Hours = group.Sum(row => row.DurationSeconds) / 3600.0,
                         Average = group.Average(row => row.AverageViewers)
                     })
                     .OrderByDescending(item => item.Count)
                     .ThenByDescending(item => item.Hours))
        {
            StatisticsCategoriesList.Items.Add(
                $"{category.Name} · {category.Count} Stream(s) · {category.Hours:0.0} h · Ø {category.Average:0.0} Viewer");
        }

        if (StatisticsCategoriesList.Items.Count == 0)
        {
            StatisticsCategoriesList.Items.Add("Noch keine Kategorien gespeichert.");
        }

        StatisticsDevelopmentList.Items.Clear();
        foreach (var row in rows
                     .Where(row => row.StartedAt != DateTimeOffset.MinValue)
                     .OrderBy(row => row.StartedAt)
                     .TakeLast(20))
        {
            StatisticsDevelopmentList.Items.Add(
                $"{row.StartedAt:dd.MM.} · Ø {row.AverageViewers:0.0} · Peak {row.PeakViewers} · +{row.FollowersGained} Follower");
        }

        if (StatisticsDevelopmentList.Items.Count == 0)
        {
            StatisticsDevelopmentList.Items.Add("Noch keine Verlaufsdaten vorhanden.");
        }
    }

    private static string FormatStatisticsDuration(long totalSeconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        if (duration.TotalHours >= 24)
        {
            return $"{(int)duration.TotalDays}d {duration.Hours:00}:{duration.Minutes:00}";
        }

        return duration.ToString(@"hh\:mm");
    }

    private async Task RefreshCreatorIntelligenceAsync()
    {
        var summary = await _creatorIntelligence.AnalyzeLatestSessionAsync();
        if (summary is null)
        {
            ServicesCreatorIntelligenceStatusText.Text = _creatorIntelligence.IsRecording
                ? "Session-Aufzeichnung aktiv · erste Auswertung nach Streamende."
                : "Noch keine Creator-Intelligence-Session vorhanden.";
            ServicesCreatorIntelligenceScoreText.Text = "–";
            _creatorIntelligenceRecommendations.Clear();
            _creatorIntelligenceRecommendations.Add("Starte einen Stream, damit Twitch-, OBS- und Ereignisdaten gemeinsam aufgezeichnet werden.");
            ApplyCreatorIntelligenceDashboard(await _creatorIntelligence.AnalyzeDashboardAsync(30));
            ApplyCreatorContentPerformance(await _creatorIntelligence.AnalyzeContentPerformanceAsync(30));
            ApplyCreatorEventCorrelations(await _creatorIntelligence.AnalyzeEventCorrelationsAsync(30));
            ApplyCreatorActionPlan(await _creatorIntelligence.AnalyzeActionPlanAsync());
            ApplyCreatorActionEffectiveness(await _creatorIntelligence.AnalyzeActionEffectivenessAsync());
            return;
        }

        ServicesCreatorIntelligenceScoreText.Text = summary.CreatorScore.ToString();
        ServicesCreatorIntelligenceStatusText.Text = $"Letzte Session: {summary.StartedAt:dd.MM.yyyy HH:mm} · Ø {summary.AverageViewers:0.0} · Peak {summary.PeakViewers}";
        ServicesCreatorIntelligenceRetentionText.Text = $"{summary.RetentionPercent:0}%";
        ServicesCreatorIntelligenceEngagementText.Text = $"{summary.ChatMessagesPerHour:0.0}/h";
        ServicesCreatorIntelligenceGrowthText.Text = $"{summary.FollowersPerHour:0.0}/h";
        ServicesCreatorIntelligenceContextText.Text = $"{summary.DistinctScenes} Szenen · {summary.TracksPlayed} Songs · {summary.ChatMessages} Chatnachrichten";
        _creatorIntelligenceRecommendations.Clear();
        foreach (var recommendation in summary.Recommendations) _creatorIntelligenceRecommendations.Add("• " + recommendation);

        var dashboard = await _creatorIntelligence.AnalyzeDashboardAsync(30);
        ApplyCreatorIntelligenceDashboard(dashboard);
        ApplyCreatorContentPerformance(await _creatorIntelligence.AnalyzeContentPerformanceAsync(30));
        ApplyCreatorEventCorrelations(await _creatorIntelligence.AnalyzeEventCorrelationsAsync(30));
        ApplyCreatorActionPlan(await _creatorIntelligence.AnalyzeActionPlanAsync());
        ApplyCreatorActionEffectiveness(await _creatorIntelligence.AnalyzeActionEffectivenessAsync());
        ApplyCreatorExperiments(await _creatorIntelligence.AnalyzeExperimentsAsync());
    }

    private void ApplyCreatorIntelligenceDashboard(CreatorIntelligenceDashboard dashboard)
    {
        ServicesCreatorIntelligenceRecentSessionsList.Items.Clear();
        if (dashboard.SessionCount == 0)
        {
            ServicesCreatorIntelligenceDashboardStatusText.Text = "Keine vollständigen Sessions";
            ServicesCreatorIntelligenceQualityIndexText.Text = "–";
            ServicesCreatorIntelligenceEngagementIndexText.Text = "–";
            ServicesCreatorIntelligenceGrowthIndexText.Text = "–";
            ServicesCreatorIntelligenceForecastText.Text = "–";
            ServicesCreatorIntelligencePeriodText.Text = "Woche: – · Monat: –";
            ServicesCreatorIntelligenceTrendText.Text = "Trend: Noch keine Daten";
            ServicesCreatorIntelligenceBestTimeText.Text = "Beste Startzeit: –";
            ServicesCreatorIntelligenceBestCategoryText.Text = "Beste Kategorie: –";
            ServicesCreatorIntelligenceRecentSessionsList.Items.Add("Noch keine vollständigen Sessions im 30-Tage-Zeitraum.");
            return;
        }

        ServicesCreatorIntelligenceDashboardStatusText.Text = $"{dashboard.SessionCount} Sessions · Ø Score {dashboard.AverageCreatorScore:0.0}";
        ServicesCreatorIntelligenceQualityIndexText.Text = dashboard.StreamQualityIndex.ToString();
        ServicesCreatorIntelligenceEngagementIndexText.Text = dashboard.EngagementIndex.ToString();
        ServicesCreatorIntelligenceGrowthIndexText.Text = dashboard.GrowthIndex.ToString();
        ServicesCreatorIntelligenceForecastText.Text = $"Score {dashboard.PredictedCreatorScore} · Ø {dashboard.PredictedAverageViewers:0.0}";
        ServicesCreatorIntelligencePeriodText.Text = $"Woche: {dashboard.WeeklySessionCount} Streams · Ø Score {dashboard.WeeklyAverageCreatorScore:0.0} · Monat: {dashboard.SessionCount} Streams · Ø Score {dashboard.AverageCreatorScore:0.0}";
        var scoreDirection = dashboard.CreatorScoreTrend > .5 ? "+" : string.Empty;
        var viewerDirection = dashboard.ViewerTrendPerStream > .05 ? "+" : string.Empty;
        ServicesCreatorIntelligenceTrendText.Text = $"Trend: Score {scoreDirection}{dashboard.CreatorScoreTrend:0.0} · Zuschauer {viewerDirection}{dashboard.ViewerTrendPerStream:0.0} je Stream";
        ServicesCreatorIntelligenceBestTimeText.Text = $"Beste Startzeit: {dashboard.BestDay.ToGermanDayName()} gegen {dashboard.BestStartHour:00}:00 Uhr";
        ServicesCreatorIntelligenceBestCategoryText.Text = $"Beste Kategorie: {dashboard.BestCategory} · Ø Bindung {dashboard.AverageRetentionPercent:0}%";

        foreach (var session in dashboard.RecentSessions)
        {
            var category = string.IsNullOrWhiteSpace(session.Category) ? "Ohne Kategorie" : session.Category;
            ServicesCreatorIntelligenceRecentSessionsList.Items.Add(
                $"{session.StartedAt:dd.MM. HH:mm} · Score {session.CreatorScore} · Ø {session.AverageViewers:0.0} · {session.RetentionPercent:0}% Bindung · {category}");
        }

        foreach (var insight in dashboard.Insights)
        {
            _creatorIntelligenceRecommendations.Add("◆ " + insight);
        }
    }


    private void ApplyCreatorContentPerformance(CreatorContentPerformance performance)
    {
        ServicesCreatorIntelligenceScenesList.Items.Clear();
        ServicesCreatorIntelligenceTracksList.Items.Clear();
        ServicesCreatorIntelligenceHeatmapList.Items.Clear();

        if (performance.SessionCount == 0)
        {
            ServicesCreatorIntelligenceScenesList.Items.Add("Noch keine vollständigen Daten.");
            ServicesCreatorIntelligenceTracksList.Items.Add("Noch keine vollständigen Daten.");
            ServicesCreatorIntelligenceHeatmapList.Items.Add("Noch keine vollständigen Daten.");
            return;
        }

        foreach (var scene in performance.Scenes)
        {
            var delta = scene.ViewerDelta > 0 ? $"+{scene.ViewerDelta:0.0}" : $"{scene.ViewerDelta:0.0}";
            ServicesCreatorIntelligenceScenesList.Items.Add($"{scene.Name} · {delta} Zuschauer · Ø {scene.AverageViewers:0.0} · {scene.Occurrences}×");
        }
        if (performance.Scenes.Count == 0) ServicesCreatorIntelligenceScenesList.Items.Add("Keine OBS-Szenenwechsel aufgezeichnet.");

        foreach (var track in performance.Tracks)
        {
            var delta = track.ViewerDelta > 0 ? $"+{track.ViewerDelta:0.0}" : $"{track.ViewerDelta:0.0}";
            ServicesCreatorIntelligenceTracksList.Items.Add($"{track.Name} · {delta} Zuschauer · Ø {track.AverageViewers:0.0}");
        }
        if (performance.Tracks.Count == 0) ServicesCreatorIntelligenceTracksList.Items.Add("Keine Spotify-Titelwechsel aufgezeichnet.");

        foreach (var cell in performance.Heatmap)
        {
            ServicesCreatorIntelligenceHeatmapList.Items.Add($"{cell.Day.ToGermanDayName()} {cell.Hour:00}:00 · Ø {cell.AverageViewers:0.0} · {cell.SampleCount} Samples");
        }
        if (performance.Heatmap.Count == 0) ServicesCreatorIntelligenceHeatmapList.Items.Add("Keine Zuschauer-Samples vorhanden.");

        foreach (var insight in performance.Insights)
        {
            _creatorIntelligenceRecommendations.Add("◇ " + insight);
        }
    }


    private void ApplyCreatorEventCorrelations(CreatorEventCorrelationReport report)
    {
        ServicesCreatorIntelligenceCorrelationList.Items.Clear();
        ServicesCreatorIntelligenceRaidList.Items.Clear();
        ServicesCreatorIntelligenceActionsList.Items.Clear();

        foreach (var row in report.Correlations)
        {
            var delta5 = row.ViewerDelta5Minutes > 0 ? $"+{row.ViewerDelta5Minutes:0.0}" : $"{row.ViewerDelta5Minutes:0.0}";
            var delta10 = row.ViewerDelta10Minutes > 0 ? $"+{row.ViewerDelta10Minutes:0.0}" : $"{row.ViewerDelta10Minutes:0.0}";
            ServicesCreatorIntelligenceCorrelationList.Items.Add($"{row.EventName} · 5 Min {delta5} · 10 Min {delta10} · {row.Occurrences}×");
        }
        if (report.Correlations.Count == 0) ServicesCreatorIntelligenceCorrelationList.Items.Add("Noch keine belastbare Ereigniskorrelation.");

        foreach (var raid in report.Raids)
            ServicesCreatorIntelligenceRaidList.Items.Add($"{raid.RaidSummary} · 5m {raid.ViewersAfter5:0} · 10m {raid.ViewersAfter10:0} · 30m {raid.ViewersAfter30:0} · {raid.Retention30Percent:0}%");
        if (report.Raids.Count == 0) ServicesCreatorIntelligenceRaidList.Items.Add("Noch keine Raid-Daten mit Zuschauer-Samples.");

        foreach (var action in report.Actions)
        {
            ServicesCreatorIntelligenceActionsList.Items.Add(action);
            _creatorIntelligenceRecommendations.Add("▶ " + action);
        }
    }

    private void ApplyCreatorActionPlan(CreatorActionPlan plan)
    {
        ServicesCreatorIntelligenceActionPlanList.Items.Clear();
        ServicesCreatorIntelligenceActionStatusText.Text = $"{plan.OpenCount} offen · {plan.CompletedCount} erledigt";
        foreach (var item in plan.Items.Take(20))
        {
            var priority = item.Priority == 1 ? "HOCH" : item.Priority == 2 ? "MITTEL" : "NORMAL";
            var progress = item.Metric == "manual" ? string.Empty : $" · {item.CurrentValue ?? item.Baseline:0.0}/{item.Target:0.0}";
            ServicesCreatorIntelligenceActionPlanList.Items.Add(new CreatorActionListItem(item.Id, $"[{item.Status}] [{priority}] {item.Title}{progress}"));
        }
        if (plan.Items.Count == 0) ServicesCreatorIntelligenceActionPlanList.Items.Add(new CreatorActionListItem(string.Empty, "Noch keine Maßnahmen vorhanden."));
    }

    private void ApplyCreatorActionEffectiveness(CreatorActionEffectivenessReport report)
    {
        ServicesCreatorIntelligenceEffectivenessList.Items.Clear();
        ServicesCreatorIntelligenceEffectivenessStatusText.Text = $"{report.ImprovedCount} verbessert · {report.ReachedCount} erreicht · {report.DeclinedCount} rückläufig";
        ServicesCreatorIntelligenceEffectivenessSummaryText.Text = report.Summary;
        foreach (var row in report.Rows.Take(15))
        {
            var delta = row.Improvement > 0 ? $"+{row.Improvement:0.0}" : $"{row.Improvement:0.0}";
            ServicesCreatorIntelligenceEffectivenessList.Items.Add($"[{row.Status}] {row.Title} · {row.Baseline:0.0} → {row.Current:0.0} · Δ {delta} · {row.ProgressPercent:0}% · {row.Verdict}");
        }
        if (report.Rows.Count == 0) ServicesCreatorIntelligenceEffectivenessList.Items.Add("Noch keine messbaren Maßnahmen vorhanden.");
    }


    private void ApplyCreatorExperiments(CreatorExperimentReport report)
    {
        ServicesCreatorIntelligenceExperimentList.Items.Clear();
        ServicesCreatorIntelligenceExperimentStatusText.Text = $"{report.ActiveCount} aktiv · {report.CompletedCount} ausgewertet · {report.PositiveCount} positiv";
        ServicesCreatorIntelligenceExperimentSummaryText.Text = report.Summary;
        foreach (var row in report.Rows.Take(15))
        {
            var delta = row.Delta > 0 ? $"+{row.Delta:0.0}" : $"{row.Delta:0.0}";
            ServicesCreatorIntelligenceExperimentList.Items.Add($"[{row.Status}] {row.Title} · {row.SessionCount}/{row.TargetSessions} Streams · {row.Baseline:0.0} → {row.Current:0.0} · Δ {delta} · {row.Confidence} · {row.Verdict}");
        }
        if (report.Rows.Count == 0) ServicesCreatorIntelligenceExperimentList.Items.Add("Noch keine Experimente vorhanden.");
    }

    private async Task StartSelectedCreatorExperimentAsync()
    {
        if (ServicesCreatorIntelligenceActionPlanList.SelectedItem is not CreatorActionListItem item || string.IsNullOrWhiteSpace(item.Id))
        {
            ServicesCreatorIntelligenceStatusText.Text = "Bitte zuerst eine messbare Maßnahme auswählen.";
            return;
        }
        await _creatorIntelligence.StartExperimentFromActionAsync(item.Id);
        ApplyCreatorExperiments(await _creatorIntelligence.AnalyzeExperimentsAsync());
        ServicesCreatorIntelligenceStatusText.Text = "Experiment gestartet. Die nächsten drei vollständigen Streams werden verglichen.";
    }

    private async Task CompleteSelectedCreatorActionAsync()
    {
        if (ServicesCreatorIntelligenceActionPlanList.SelectedItem is not CreatorActionListItem item || string.IsNullOrWhiteSpace(item.Id)) return;
        await _creatorIntelligence.CompleteActionAsync(item.Id);
        ApplyCreatorActionPlan(await _creatorIntelligence.AnalyzeActionPlanAsync());
        ApplyCreatorActionEffectiveness(await _creatorIntelligence.AnalyzeActionEffectivenessAsync());
        ApplyCreatorExperiments(await _creatorIntelligence.AnalyzeExperimentsAsync());
        ServicesCreatorIntelligenceStatusText.Text = "Maßnahme als erledigt markiert.";
    }

    private async Task CreateCreatorIntelligenceWeeklyReportAsync()
    {
        try
        {
            var path = await _creatorIntelligence.GenerateWeeklyReportAsync();
            ServicesCreatorIntelligenceStatusText.Text = "Wochenbericht erstellt.";
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ServicesCreatorIntelligenceStatusText.Text = "Wochenbericht konnte nicht erstellt werden: " + ex.Message;
        }
    }

    private async Task AddCreatorIntelligenceNoteAsync()
    {
        var note = ServicesCreatorIntelligenceNoteBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(note)) return;
        await _creatorIntelligence.RecordAsync("session.note", new { note, scene = _servicesObsCurrentScene, viewers = _currentLiveViewerCount });
        ServicesCreatorIntelligenceNoteBox.Clear();
        ServicesCreatorIntelligenceStatusText.Text = "Session-Notiz gespeichert.";
    }

    private void OpenCreatorIntelligenceFolder()
    {
        Directory.CreateDirectory(_creatorIntelligence.RootDirectory);
        Process.Start(new ProcessStartInfo(_creatorIntelligence.RootDirectory) { UseShellExecute = true });
    }

    private sealed record CreatorActionListItem(string Id, string DisplayText);

    private string GetStreamHistoryDirectory()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CreatorControlSuite",
            "StreamHistory");
        Directory.CreateDirectory(root);
        return root;
    }

    private string GetStreamHistoryFilePath() =>
        Path.Combine(GetStreamHistoryDirectory(), "history.jsonl");

    private async Task SaveCurrentStreamHistoryAsync()
    {
        var stats = _workflowModule.Service.SessionStats;
        var endedAt = DateTimeOffset.Now;
        var startedAt = _streamSessionStartedAt ?? endedAt;
        var item = new
        {
            StartedAt = startedAt,
            EndedAt = endedAt,
            DurationSeconds = Math.Max(0, (long)(endedAt - startedAt).TotalSeconds),
            PeakViewers = stats.PeakViewers,
            AverageViewers = stats.AverageViewers,
            FollowersGained = stats.FollowersGained,
            ChatMessages = stats.ChatMessages,
            AlertsPlayed = stats.AlertsPlayed,
            NewSubscriptions = stats.NewSubscriptions,
            GiftSubscriptions = stats.GiftSubscriptions,
            BitsCheered = stats.BitsCheered,
            IncomingRaids = stats.IncomingRaids,
            RaidEnabled = _settings.Twitch.RaidOnStreamEnd,
            RaidTarget = _settings.Twitch.SelectedRaidChannel,
            Category = DashboardTwitchCategoryResultsBox.SelectedItem?.ToString() ?? DashboardTwitchCategorySearchBox.Text,
            Title = DashboardTwitchTitleBox.Text
        };

        var line = System.Text.Json.JsonSerializer.Serialize(item);
        await File.AppendAllTextAsync(GetStreamHistoryFilePath(), line + Environment.NewLine);
        await LoadTwitchProfessionalHistoryAsync();
        await _creatorIntelligence.CompleteSessionAsync(endedAt);
        await RefreshCreatorIntelligenceAsync();
        _streamSessionStartedAt = null;
    }

    private async Task LoadStreamHistoryAsync()
    {
        _streamHistoryItems.Clear();
        var path = GetStreamHistoryFilePath();
        if (!File.Exists(path))
        {
            _streamHistoryItems.Add("Noch keine abgeschlossenen Streams gespeichert.");
            return;
        }

        var lines = await File.ReadAllLinesAsync(path);
        foreach (var line in lines.Reverse().Take(50))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(line);
                var root = doc.RootElement;
                var started = root.GetProperty("StartedAt").GetDateTimeOffset().ToLocalTime();
                var duration = TimeSpan.FromSeconds(root.GetProperty("DurationSeconds").GetInt64());
                var peak = root.GetProperty("PeakViewers").GetInt32();
                var avg = root.GetProperty("AverageViewers").GetDouble();
                var followers = root.GetProperty("FollowersGained").GetInt32();
                _streamHistoryItems.Add(
                    $"{started:dd.MM.yyyy HH:mm} · {duration:hh\\:mm\\:ss} · Peak {peak} · Ø {avg:0.0} · +{followers} Follower");
            }
            catch
            {
                // Ignore malformed legacy lines and continue loading valid history entries.
            }
        }
    }

    private async Task CopyLatestTwitchProfessionalSummaryAsync()
    {
        var path = GetStreamHistoryFilePath();
        if (!File.Exists(path))
        {
            MessageBox.Show("Es ist noch kein abgeschlossener Stream gespeichert.", "Twitch Professional", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var line in (await File.ReadAllLinesAsync(path)).Reverse())
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var startedAt = root.GetProperty("StartedAt").GetDateTimeOffset().ToLocalTime();
                var durationSeconds = root.TryGetProperty("DurationSeconds", out var duration) ? duration.GetInt64() : 0;
                var peak = root.TryGetProperty("PeakViewers", out var peakElement) ? peakElement.GetInt32() : 0;
                var average = root.TryGetProperty("AverageViewers", out var averageElement) ? averageElement.GetDouble() : 0;
                var followers = root.TryGetProperty("FollowersGained", out var followerElement) ? followerElement.GetInt32() : 0;
                var chat = root.TryGetProperty("ChatMessages", out var chatElement) ? chatElement.GetInt32() : 0;
                var category = root.TryGetProperty("Category", out var categoryElement) ? categoryElement.GetString() ?? "-" : "-";
                var title = root.TryGetProperty("Title", out var titleElement) ? titleElement.GetString() ?? "-" : "-";
                var summary = $"Stream-Zusammenfassung vom {startedAt:dd.MM.yyyy}\n" +
                              $"Titel: {title}\nKategorie: {category}\n" +
                              $"Dauer: {TimeSpan.FromSeconds(Math.Max(0, durationSeconds)):hh\\:mm\\:ss}\n" +
                              $"Peak: {peak} Zuschauer | Durchschnitt: {average:0.0}\n" +
                              $"Neue Follower: {followers} | Chatnachrichten: {chat}";
                Clipboard.SetText(summary);
                AddDashboardNotification("Stream-Zusammenfassung wurde in die Zwischenablage kopiert.", "Info");
                return;
            }
            catch
            {
                // Ungültige Historienzeilen werden übersprungen.
            }
        }
    }

    private async Task CreateTwitchProfessionalReportAsync()
    {
        var path = GetStreamHistoryFilePath();
        if (!File.Exists(path))
        {
            MessageBox.Show("Für einen Stream-Report werden abgeschlossene Streams benötigt.", "Twitch Professional", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var rows = new List<(DateTimeOffset StartedAt, long DurationSeconds, int Peak, double Average, int Followers, int Chat, int Events, string Category, string Title)>();
        foreach (var line in await File.ReadAllLinesAsync(path))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                rows.Add((
                    root.GetProperty("StartedAt").GetDateTimeOffset(),
                    root.TryGetProperty("DurationSeconds", out var duration) ? duration.GetInt64() : 0,
                    root.TryGetProperty("PeakViewers", out var peak) ? peak.GetInt32() : 0,
                    root.TryGetProperty("AverageViewers", out var average) ? average.GetDouble() : 0,
                    root.TryGetProperty("FollowersGained", out var followers) ? followers.GetInt32() : 0,
                    root.TryGetProperty("ChatMessages", out var chat) ? chat.GetInt32() : 0,
                    root.TryGetProperty("AlertsPlayed", out var eventsCount) ? eventsCount.GetInt32() : 0,
                    root.TryGetProperty("Category", out var category) ? category.GetString() ?? "-" : "-",
                    root.TryGetProperty("Title", out var title) ? title.GetString() ?? "-" : "-"));
            }
            catch { }
        }

        if (rows.Count == 0)
        {
            MessageBox.Show("Es wurden keine gültigen Stream-Sessions gefunden.", "Twitch Professional", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        static string H(string value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
        var ordered = rows.OrderByDescending(x => x.StartedAt).ToList();
        var recent = ordered.Take(5).ToList();
        var totalHours = rows.Sum(x => x.DurationSeconds) / 3600d;
        var bestCategory = rows.Where(x => !string.IsNullOrWhiteSpace(x.Category) && x.Category != "-")
            .GroupBy(x => x.Category)
            .Select(group => new { Name = group.Key, Average = group.Average(x => x.Average) })
            .OrderByDescending(x => x.Average)
            .FirstOrDefault();
        var tableRows = string.Join(Environment.NewLine, ordered.Take(50).Select(row =>
            $"<tr><td>{row.StartedAt.ToLocalTime():dd.MM.yyyy HH:mm}</td><td>{H(row.Title)}</td><td>{H(row.Category)}</td><td>{TimeSpan.FromSeconds(Math.Max(0, row.DurationSeconds)):hh\\:mm\\:ss}</td><td>{row.Peak}</td><td>{row.Average:0.0}</td><td>{row.Followers}</td><td>{row.Chat}</td></tr>"));
        var html = $$"""<!doctype html><html lang="de"><head><meta charset="utf-8"><title>Twitch Stream-Report</title><style>body{font-family:Segoe UI,Arial;background:#0b1014;color:#eef3f6;margin:32px}h1,h2{color:#fff}.cards{display:flex;flex-wrap:wrap;gap:12px}.card{background:#151d23;border:1px solid #2a3740;border-radius:10px;padding:16px;min-width:160px}.value{font-size:26px;font-weight:700;margin-top:5px}table{width:100%;border-collapse:collapse;margin-top:16px;background:#11181d}th,td{border-bottom:1px solid #2a3740;padding:10px;text-align:left}th{background:#192229}.muted{color:#aeb8bf}</style></head><body><h1>Creator Control Suite – Twitch Stream-Report</h1><p class="muted">Erstellt am {{DateTime.Now:dd.MM.yyyy HH:mm}}</p><div class="cards"><div class="card">Streams<div class="value">{{rows.Count}}</div></div><div class="card">Rekord-Peak<div class="value">{{rows.Max(x => x.Peak)}}</div></div><div class="card">Bestes Ø<div class="value">{{rows.Max(x => x.Average):0.0}}</div></div><div class="card">Livezeit<div class="value">{{FormatStatisticsDuration(rows.Sum(x => x.DurationSeconds))}}</div></div><div class="card">Follower<div class="value">{{rows.Sum(x => x.Followers)}}</div></div><div class="card">Chat / Std.<div class="value">{{(totalHours <= 0 ? 0 : rows.Sum(x => x.Chat) / totalHours):0.0}}</div></div></div><h2>Auswertung</h2><p>Die letzten {{recent.Count}} Streams erreichten durchschnittlich {{recent.Average(x => x.Average):0.0}} Zuschauer bei einem mittleren Peak von {{recent.Average(x => x.Peak):0.0}}. Beste Kategorie nach Zuschauerdurchschnitt: <strong>{{H(bestCategory?.Name ?? "-")}}</strong>.</p><h2>Letzte Streams</h2><table><thead><tr><th>Start</th><th>Titel</th><th>Kategorie</th><th>Dauer</th><th>Peak</th><th>Ø</th><th>Follower</th><th>Chat</th></tr></thead><tbody>{{tableRows}}</tbody></table></body></html>""";
        var reportPath = Path.Combine(GetStreamHistoryDirectory(), $"twitch-stream-report-{DateTime.Now:yyyyMMdd-HHmmss}.html");
        await File.WriteAllTextAsync(reportPath, html, new System.Text.UTF8Encoding(true));
        Process.Start(new ProcessStartInfo(reportPath) { UseShellExecute = true });
    }

    private async Task ExportTwitchProfessionalHistoryCsvAsync()
    {
        var path = GetStreamHistoryFilePath();
        if (!File.Exists(path)) return;
        var csvPath = Path.Combine(GetStreamHistoryDirectory(), $"twitch-history-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        var lines = new List<string> { "StartedAt;EndedAt;DurationSeconds;PeakViewers;AverageViewers;FollowersGained;ChatMessages;Category;Title" };
        foreach (var line in await File.ReadAllLinesAsync(path))
        {
            try
            {
                using var doc = JsonDocument.Parse(line); var r = doc.RootElement;
                string V(string n) => r.TryGetProperty(n, out var v) ? v.ToString().Replace(";", ",").Replace("\r", " ").Replace("\n", " ") : string.Empty;
                lines.Add(string.Join(";", new[] { V("StartedAt"), V("EndedAt"), V("DurationSeconds"), V("PeakViewers"), V("AverageViewers"), V("FollowersGained"), V("ChatMessages"), V("Category"), V("Title") }));
            }
            catch { }
        }
        await File.WriteAllLinesAsync(csvPath, lines, new System.Text.UTF8Encoding(true));
        Process.Start(new ProcessStartInfo(csvPath) { UseShellExecute = true });
    }

    private void OpenStreamHistoryFolder()
    {
        var folder = GetStreamHistoryDirectory();
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }


    private void UpdateOverlayFrameColorPreview(string color)
    {
        try
        {
            var converted = System.Windows.Media.ColorConverter.ConvertFromString(color);
            if (converted is System.Windows.Media.Color parsed)
                OverlayFrameColorPreview.Background = new System.Windows.Media.SolidColorBrush(parsed);
        }
        catch
        {
            // Ungültige Eingaben bleiben im Textfeld sichtbar; die letzte Vorschau bleibt erhalten.
        }
    }

    private static void SelectComboBoxTag(ComboBox box, string value)
    {
        foreach (var entry in box.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(entry.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = entry;
                return;
            }
        }
        if (box.Items.Count > 0) box.SelectedIndex = 0;
    }

    private async Task WriteOverlayConfigurationAsync()
    {
        var root = await _overlayModule.Service.GetOverlayRootAsync();
        var dataFolder = Path.Combine(root, "data");
        Directory.CreateDirectory(dataFolder);
        var config = new
        {
            contentName = _settings.Branding.DisplayName,
            startText = _settings.Overlay.StartText,
            pauseText = _settings.Overlay.PauseText,
            endText = _settings.Overlay.EndText,
            sharedSceneText = _settings.Overlay.SharedSceneText,
            fontFamily = _settings.Overlay.FontFamily,
            fontSize = _settings.Overlay.FontSize,
            fontColor = _settings.Overlay.FontColor,
            startTimerSeconds = _settings.Overlay.StartTimerSeconds,
            timerX = _settings.Overlay.TimerX,
            timerY = _settings.Overlay.TimerY,
            frameStyle = _settings.Overlay.FrameStyle,
            frameColor = _settings.Overlay.FrameColor,
            frameEffect = _settings.Overlay.FrameEffect
        };
        var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        await File.WriteAllTextAsync(Path.Combine(dataFolder, "overlay-config.json"), json);
    }

    private async Task InstallSelectedOverlayContentAsync()
    {
        try
        {
            await SaveSettingsAsync();
            var scene = OverlayObsSceneTargetBox.SelectedItem?.ToString() ?? OverlayObsSceneTargetBox.Text;
            var item = OverlayContentTypeBox.SelectedItem as ComboBoxItem;
            var type = item?.Tag?.ToString() ?? "content-name";
            var result = await _obsBrowserSourceInstaller.InstallContentAsync(scene, type);
            OverlayStatusText.Text = result;
            OverlayStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            OverlayStatusText.Text = exception.Message;
            OverlayStatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
        }
    }


    private RunOfShowPlanSettings EnsureRunOfShowPlansInitialized()
    {
        _settings.Workflow.RunOfShowPlans ??= [];
        _settings.Obs.AudioProfiles ??= [];
        RefreshObsAudioProfilesUi();
        if (_settings.Workflow.RunOfShowPlans.Count == 0)
        {
            var legacySteps = _settings.Workflow.RunOfShowSteps ?? [];
            var initialPlan = new RunOfShowPlanSettings
            {
                Name = "Standard",
                Steps = legacySteps
            };
            _settings.Workflow.RunOfShowPlans.Add(initialPlan);
            _settings.Workflow.ActiveRunOfShowPlanId = initialPlan.Id;
        }

        var active = _settings.Workflow.RunOfShowPlans.FirstOrDefault(x =>
            string.Equals(x.Id, _settings.Workflow.ActiveRunOfShowPlanId, StringComparison.OrdinalIgnoreCase))
            ?? _settings.Workflow.RunOfShowPlans[0];
        active.Steps ??= [];
        _settings.Workflow.ActiveRunOfShowPlanId = active.Id;
        _settings.Workflow.RunOfShowSteps = active.Steps;
        return active;
    }

    private RunOfShowPlanSettings? CurrentRunOfShowPlan()
        => _settings.Workflow.RunOfShowPlans.FirstOrDefault(x =>
            string.Equals(x.Id, _settings.Workflow.ActiveRunOfShowPlanId, StringComparison.OrdinalIgnoreCase));

    private void RefreshRunOfShowPlanSelector()
    {
        var active = EnsureRunOfShowPlansInitialized();
        _updatingRunOfShowPlanUi = true;
        try
        {
            RunOfShowPlanBox.ItemsSource = null;
            RunOfShowPlanBox.ItemsSource = _settings.Workflow.RunOfShowPlans;
            RunOfShowPlanBox.SelectedItem = active;
            RunOfShowPlanBox.Text = active.Name;
            DeleteRunOfShowPlanButton.IsEnabled = _settings.Workflow.RunOfShowPlans.Count > 1;
        }
        finally
        {
            _updatingRunOfShowPlanUi = false;
        }
    }

    private void RefreshRunOfShowSteps()
    {
        var active = EnsureRunOfShowPlansInitialized();
        RefreshRunOfShowPlanSelector();
        _runOfShowSteps.Clear();
        foreach (var step in active.Steps) _runOfShowSteps.Add(step);
        if (_runOfShowSteps.Count > 0 && RunOfShowStepsList.SelectedItem is null)
            RunOfShowStepsList.SelectedIndex = 0;
        _runOfShowCurrentIndex = -1;
        UpdateRunOfShowStatus();
    }

    private async Task PersistRunOfShowAsync()
    {
        var active = EnsureRunOfShowPlansInitialized();
        active.Steps = _runOfShowSteps.ToList();
        _settings.Workflow.RunOfShowSteps = active.Steps;
        await _settingsStore.SaveAsync(_settings);
    }

    private async Task SwitchRunOfShowPlanAsync()
    {
        if (_updatingRunOfShowPlanUi || RunOfShowPlanBox.SelectedItem is not RunOfShowPlanSettings selected) return;
        StopAutomaticRunOfShow();
        if (CurrentRunOfShowPlan() is not null) await PersistRunOfShowAsync();
        _settings.Workflow.ActiveRunOfShowPlanId = selected.Id;
        _settings.Workflow.RunOfShowSteps = selected.Steps ?? [];
        _runOfShowSteps.Clear();
        foreach (var step in _settings.Workflow.RunOfShowSteps) _runOfShowSteps.Add(step);
        _runOfShowCurrentIndex = -1;
        RunOfShowStepsList.SelectedIndex = _runOfShowSteps.Count > 0 ? 0 : -1;
        await _settingsStore.SaveAsync(_settings);
        UpdateRunOfShowStatus();
        RunOfShowStatusText.Text = $"Regieplan '{selected.Name}' geladen.";
    }

    private async Task CreateRunOfShowPlanAsync()
    {
        await PersistRunOfShowAsync();
        var baseName = "Neuer Regieplan";
        var name = baseName;
        var counter = 2;
        while (_settings.Workflow.RunOfShowPlans.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName} {counter++}";
        var plan = new RunOfShowPlanSettings { Name = name };
        _settings.Workflow.RunOfShowPlans.Add(plan);
        _settings.Workflow.ActiveRunOfShowPlanId = plan.Id;
        _settings.Workflow.RunOfShowSteps = plan.Steps;
        _runOfShowSteps.Clear();
        _runOfShowCurrentIndex = -1;
        RefreshRunOfShowPlanSelector();
        RunOfShowPlanBox.SelectedItem = plan;
        RunOfShowPlanBox.Text = plan.Name;
        await _settingsStore.SaveAsync(_settings);
        UpdateRunOfShowStatus();
        RunOfShowStatusText.Text = $"Regieplan '{plan.Name}' erstellt. Namen im Feld ändern und UMBENENNEN wählen.";
    }

    private async Task RenameRunOfShowPlanAsync()
    {
        var plan = CurrentRunOfShowPlan();
        if (plan is null) return;
        var name = RunOfShowPlanBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            RunOfShowStatusText.Text = "Bitte einen Namen für den Regieplan eingeben.";
            return;
        }
        if (_settings.Workflow.RunOfShowPlans.Any(x => x != plan && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            RunOfShowStatusText.Text = "Ein Regieplan mit diesem Namen existiert bereits.";
            return;
        }
        plan.Name = name;
        RefreshRunOfShowPlanSelector();
        await _settingsStore.SaveAsync(_settings);
        RunOfShowStatusText.Text = $"Regieplan in '{name}' umbenannt.";
    }

    private async Task DeleteRunOfShowPlanAsync()
    {
        var plan = CurrentRunOfShowPlan();
        if (plan is null || _settings.Workflow.RunOfShowPlans.Count <= 1)
        {
            RunOfShowStatusText.Text = "Der letzte Regieplan kann nicht gelöscht werden.";
            return;
        }
        var answer = MessageBox.Show(this, $"Regieplan '{plan.Name}' einschließlich aller Schritte löschen?",
            "Regieplan löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        StopAutomaticRunOfShow();
        _settings.Workflow.RunOfShowPlans.Remove(plan);
        var next = _settings.Workflow.RunOfShowPlans[0];
        _settings.Workflow.ActiveRunOfShowPlanId = next.Id;
        _settings.Workflow.RunOfShowSteps = next.Steps;
        _runOfShowSteps.Clear();
        foreach (var step in next.Steps) _runOfShowSteps.Add(step);
        _runOfShowCurrentIndex = -1;
        RefreshRunOfShowPlanSelector();
        RunOfShowStepsList.SelectedIndex = _runOfShowSteps.Count > 0 ? 0 : -1;
        await _settingsStore.SaveAsync(_settings);
        UpdateRunOfShowStatus();
        RunOfShowStatusText.Text = $"Regieplan '{plan.Name}' gelöscht. '{next.Name}' ist jetzt aktiv.";
    }

    private void CreateNewRunOfShowStep()
    {
        var step = new RunOfShowStepSettings();
        _settings.Workflow.RunOfShowSteps.Add(step);
        _runOfShowSteps.Add(step);
        RunOfShowStepsList.SelectedItem = step;
        RunOfShowStepsList.ScrollIntoView(step);
    }

    private async Task DuplicateSelectedRunOfShowStepAsync()
    {
        if (RunOfShowStepsList.SelectedItem is not RunOfShowStepSettings source) return;
        var copy = new RunOfShowStepSettings
        {
            Name = source.Name + " (Kopie)",
            Enabled = source.Enabled,
            ObsScene = source.ObsScene,
            TransitionName = source.TransitionName,
            TransitionDurationMilliseconds = source.TransitionDurationMilliseconds,
            SpotifyAction = source.SpotifyAction,
            SpotifyVolumePercent = source.SpotifyVolumePercent,
            SpotifyPlaylistUri = source.SpotifyPlaylistUri,
            SpotifyPlaylistShuffle = source.SpotifyPlaylistShuffle,
            SpotifyActionDelaySeconds = source.SpotifyActionDelaySeconds,
            SpotifyFadeSeconds = source.SpotifyFadeSeconds,
            SpotifyPriority = source.SpotifyPriority,
            StreamerBotActionId = source.StreamerBotActionId,
            StreamerBotActionName = source.StreamerBotActionName,
            ActionDelayMilliseconds = source.ActionDelayMilliseconds,
            ContinueOnActionError = source.ContinueOnActionError,
            UpdateTwitchChannel = source.UpdateTwitchChannel,
            TwitchTitle = source.TwitchTitle,
            TwitchCategoryId = source.TwitchCategoryId,
            TwitchCategoryName = source.TwitchCategoryName,
            ContinueOnTwitchError = source.ContinueOnTwitchError,
            AutoAdvance = source.AutoAdvance,
            AutoAdvanceDelaySeconds = source.AutoAdvanceDelaySeconds
        };
        var index = _runOfShowSteps.IndexOf(source) + 1;
        _settings.Workflow.RunOfShowSteps.Insert(index, copy);
        _runOfShowSteps.Insert(index, copy);
        RunOfShowStepsList.SelectedItem = copy;
        RunOfShowStepsList.ScrollIntoView(copy);
        await PersistRunOfShowAsync();
        UpdateRunOfShowStatus();
    }

    private async Task MoveSelectedRunOfShowStepAsync(int direction)
    {
        if (RunOfShowStepsList.SelectedItem is not RunOfShowStepSettings step) return;
        var oldIndex = _runOfShowSteps.IndexOf(step);
        var newIndex = oldIndex + direction;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= _runOfShowSteps.Count) return;
        _runOfShowSteps.Move(oldIndex, newIndex);
        _settings.Workflow.RunOfShowSteps.Remove(step);
        _settings.Workflow.RunOfShowSteps.Insert(newIndex, step);
        RunOfShowStepsList.SelectedItem = step;
        if (_runOfShowCurrentIndex == oldIndex) _runOfShowCurrentIndex = newIndex;
        else if (_runOfShowCurrentIndex == newIndex) _runOfShowCurrentIndex = oldIndex;
        await PersistRunOfShowAsync();
        UpdateRunOfShowStatus();
    }

    private void LoadSelectedRunOfShowStep()
    {
        if (RunOfShowStepsList.SelectedItem is not RunOfShowStepSettings step) return;
        RunOfShowEnabledBox.IsChecked = step.Enabled;
        RunOfShowNameBox.Text = step.Name;
        RunOfShowSceneBox.Text = step.ObsScene;
        RunOfShowTransitionBox.Text = step.TransitionName;
        RunOfShowTransitionDurationBox.Text = step.TransitionDurationMilliseconds.ToString();
        SelectComboByTag(RunOfShowSpotifyActionBox, step.SpotifyAction);
        RunOfShowSpotifyVolumeBox.Text = step.SpotifyVolumePercent.ToString();
        RunOfShowActionDelayBox.Text = step.ActionDelayMilliseconds.ToString();
        RunOfShowContinueOnActionErrorBox.IsChecked = step.ContinueOnActionError;
        SelectStreamerBotAction(RunOfShowStreamerBotActionBox, step.StreamerBotActionId, step.StreamerBotActionName);
        RunOfShowUpdateTwitchBox.IsChecked = step.UpdateTwitchChannel;
        RunOfShowTwitchTitleBox.Text = step.TwitchTitle;
        RunOfShowTwitchCategorySearchBox.Text = step.TwitchCategoryName;
        RunOfShowTwitchCategoryResultsBox.ItemsSource = string.IsNullOrWhiteSpace(step.TwitchCategoryId)
            ? null
            : new[] { new TwitchCategory(step.TwitchCategoryId, step.TwitchCategoryName, "") };
        RunOfShowTwitchCategoryResultsBox.SelectedIndex = string.IsNullOrWhiteSpace(step.TwitchCategoryId) ? -1 : 0;
        RunOfShowContinueOnTwitchErrorBox.IsChecked = step.ContinueOnTwitchError;
        RunOfShowAutoAdvanceBox.IsChecked = step.AutoAdvance;
        RunOfShowAutoAdvanceDelayBox.Text = step.AutoAdvanceDelaySeconds.ToString();
    }

    private RunOfShowStepSettings ReadRunOfShowEditor(RunOfShowStepSettings? target = null)
    {
        var step = target ?? new RunOfShowStepSettings();
        step.Enabled = RunOfShowEnabledBox.IsChecked == true;
        step.Name = string.IsNullOrWhiteSpace(RunOfShowNameBox.Text) ? "Neuer Regieschritt" : RunOfShowNameBox.Text.Trim();
        step.ObsScene = RunOfShowSceneBox.Text.Trim();
        step.TransitionName = RunOfShowTransitionBox.Text.Trim();
        step.TransitionDurationMilliseconds = int.TryParse(RunOfShowTransitionDurationBox.Text, out var duration) ? Math.Clamp(duration, 50, 20000) : 1000;
        step.SpotifyAction = ComboTag(RunOfShowSpotifyActionBox, "None");
        step.SpotifyVolumePercent = int.TryParse(RunOfShowSpotifyVolumeBox.Text, out var volume) ? Math.Clamp(volume, 0, 100) : 35;
        var streamerAction = RunOfShowStreamerBotActionBox.SelectedItem as StreamerBotActionOption;
        step.StreamerBotActionId = streamerAction?.Id ?? "";
        step.StreamerBotActionName = streamerAction?.Name ?? RunOfShowStreamerBotActionBox.Text.Trim();
        step.ActionDelayMilliseconds = int.TryParse(RunOfShowActionDelayBox.Text, out var actionDelay) ? Math.Clamp(actionDelay, 0, 60000) : 0;
        step.ContinueOnActionError = RunOfShowContinueOnActionErrorBox.IsChecked == true;
        step.UpdateTwitchChannel = RunOfShowUpdateTwitchBox.IsChecked == true;
        step.TwitchTitle = RunOfShowTwitchTitleBox.Text.Trim();
        var twitchCategory = RunOfShowTwitchCategoryResultsBox.SelectedItem as TwitchCategory;
        step.TwitchCategoryId = twitchCategory?.Id ?? step.TwitchCategoryId;
        step.TwitchCategoryName = twitchCategory?.Name ?? RunOfShowTwitchCategorySearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(step.TwitchCategoryName)) step.TwitchCategoryId = "";
        step.ContinueOnTwitchError = RunOfShowContinueOnTwitchErrorBox.IsChecked == true;
        step.AutoAdvance = RunOfShowAutoAdvanceBox.IsChecked == true;
        step.AutoAdvanceDelaySeconds = int.TryParse(RunOfShowAutoAdvanceDelayBox.Text, out var autoDelay) ? Math.Clamp(autoDelay, 1, 86400) : 10;
        return step;
    }

    private async Task SaveSelectedRunOfShowStepAsync()
    {
        var step = RunOfShowStepsList.SelectedItem as RunOfShowStepSettings;
        if (step is null)
        {
            CreateNewRunOfShowStep();
            step = RunOfShowStepsList.SelectedItem as RunOfShowStepSettings;
        }
        if (step is null) return;
        ReadRunOfShowEditor(step);
        RunOfShowStepsList.Items.Refresh();
        await PersistRunOfShowAsync();
        RunOfShowStatusText.Text = "Regieschritt gespeichert.";
    }

    private async Task DeleteSelectedRunOfShowStepAsync()
    {
        if (RunOfShowStepsList.SelectedItem is not RunOfShowStepSettings step) return;
        var index = _runOfShowSteps.IndexOf(step);
        _settings.Workflow.RunOfShowSteps.Remove(step);
        _runOfShowSteps.Remove(step);
        if (_runOfShowCurrentIndex >= _runOfShowSteps.Count) _runOfShowCurrentIndex = _runOfShowSteps.Count - 1;
        if (_runOfShowSteps.Count > 0) RunOfShowStepsList.SelectedIndex = Math.Clamp(index, 0, _runOfShowSteps.Count - 1);
        await PersistRunOfShowAsync();
        UpdateRunOfShowStatus();
    }

    private async Task RefreshRunOfShowObsListsAsync()
    {
        if (!_obsClient.IsConnected)
        {
            RunOfShowStatusText.Text = "OBS ist nicht verbunden.";
            return;
        }
        var previousScene = RunOfShowSceneBox.Text;
        var previousTransition = RunOfShowTransitionBox.Text;
        var scenes = (await _obsClient.GetSceneListAsync()).Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var transitions = (await _obsClient.GetSceneTransitionListAsync()).Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        RunOfShowSceneBox.ItemsSource = scenes;
        RunOfShowTransitionBox.ItemsSource = transitions;
        RunOfShowSceneBox.Text = previousScene;
        RunOfShowTransitionBox.Text = previousTransition;
        RunOfShowStatusText.Text = $"{scenes.Count} Szenen und {transitions.Count} Übergänge geladen.";
    }

    private async Task RefreshRunOfShowStreamerBotActionsAsync(bool showStatus)
    {
        await RefreshStreamerBotActionsAsync(false);
        if (!showStatus) return;
        if (_streamerBotSocket is null || _streamerBotSocket.State != System.Net.WebSockets.WebSocketState.Open)
        {
            RunOfShowStatusText.Text = "Streamer.bot ist nicht verbunden.";
            return;
        }
        RunOfShowStatusText.Text = $"{_streamerBotActions.Count} Streamer.bot-Aktionen für den Regieplan geladen.";
    }

    private async Task SearchRunOfShowTwitchCategoriesAsync()
    {
        try
        {
            var query = RunOfShowTwitchCategorySearchBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                RunOfShowStatusText.Text = "Bitte einen Kategorienamen eingeben.";
                return;
            }

            var categories = await _twitchModule.SearchCategoriesAsync(query);
            RunOfShowTwitchCategoryResultsBox.ItemsSource = categories;
            RunOfShowTwitchCategoryResultsBox.SelectedIndex = categories.Count > 0 ? 0 : -1;
            RunOfShowStatusText.Text = categories.Count > 0
                ? $"{categories.Count} Twitch-Kategorien gefunden."
                : "Keine passende Twitch-Kategorie gefunden.";
        }
        catch (Exception ex)
        {
            RunOfShowStatusText.Text = "Twitch-Kategoriesuche fehlgeschlagen: " + ex.Message;
            _appLogger.Write(AppLogLevel.Error, "RunOfShow.Twitch", RunOfShowStatusText.Text);
        }
    }


    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '-');
        return string.IsNullOrWhiteSpace(value) ? "Mein-Regieplan.ccs-regieplan.json" : value;
    }

    private sealed class RunOfShowExportDocument
    {
        public int FormatVersion { get; set; } = 1;
        public string Name { get; set; } = "Creator Control Suite Regieplan";
        public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.Now;
        public List<RunOfShowStepSettings> Steps { get; set; } = [];
    }

    private async Task ExportRunOfShowAsync()
    {
        try
        {
            if (RunOfShowStepsList.SelectedItem is RunOfShowStepSettings selected) ReadRunOfShowEditor(selected);
            if (_runOfShowSteps.Count == 0)
            {
                RunOfShowStatusText.Text = "Der Regieplan enthält noch keine Schritte.";
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Regieplan exportieren",
                Filter = "Creator Control Suite Regieplan (*.ccs-regieplan.json)|*.ccs-regieplan.json|JSON-Datei (*.json)|*.json",
                DefaultExt = ".ccs-regieplan.json",
                AddExtension = true,
                FileName = SanitizeFileName((CurrentRunOfShowPlan()?.Name ?? "Mein-Regieplan") + ".ccs-regieplan.json")
            };
            if (dialog.ShowDialog(this) != true) return;

            var document = new RunOfShowExportDocument
            {
                Name = CurrentRunOfShowPlan()?.Name ?? "Creator Control Suite Regieplan",
                Steps = _runOfShowSteps.Select(CloneRunOfShowStep).ToList()
            };
            var json = System.Text.Json.JsonSerializer.Serialize(document, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(dialog.FileName, json);
            RunOfShowStatusText.Text = $"Regieplan exportiert: {Path.GetFileName(dialog.FileName)}";
            _appLogger.Write(AppLogLevel.Information, "RunOfShow.Export", RunOfShowStatusText.Text);
        }
        catch (Exception ex)
        {
            RunOfShowStatusText.Text = "Regieplan konnte nicht exportiert werden: " + ex.Message;
            _appLogger.Write(AppLogLevel.Error, "RunOfShow.Export", RunOfShowStatusText.Text);
        }
    }

    private async Task ImportRunOfShowAsync()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Regieplan importieren",
                Filter = "Creator Control Suite Regieplan (*.ccs-regieplan.json;*.json)|*.ccs-regieplan.json;*.json|Alle Dateien (*.*)|*.*",
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return;

            var json = await File.ReadAllTextAsync(dialog.FileName);
            var document = System.Text.Json.JsonSerializer.Deserialize<RunOfShowExportDocument>(json, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (document?.Steps is null || document.Steps.Count == 0)
                throw new InvalidDataException("Die Datei enthält keine Regieschritte.");
            if (document.FormatVersion < 1 || document.FormatVersion > 1)
                throw new InvalidDataException($"Nicht unterstützte Regieplan-Version: {document.FormatVersion}.");

            var answer = MessageBox.Show(this,
                $"{document.Steps.Count} Regieschritte wurden gefunden. Soll der aktuelle Regieplan ersetzt werden?{Environment.NewLine}{Environment.NewLine}Ja = ersetzen{Environment.NewLine}Nein = anhängen",
                "Regieplan importieren", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) return;

            StopAutomaticRunOfShow();
            if (answer == MessageBoxResult.Yes) _runOfShowSteps.Clear();
            foreach (var imported in document.Steps)
            {
                var step = CloneRunOfShowStep(imported);
                step.Id = Guid.NewGuid().ToString("N");
                step.TransitionDurationMilliseconds = Math.Clamp(step.TransitionDurationMilliseconds, 0, 20000);
                step.SpotifyVolumePercent = Math.Clamp(step.SpotifyVolumePercent, 0, 100);
                step.ActionDelayMilliseconds = Math.Clamp(step.ActionDelayMilliseconds, 0, 60000);
                step.AutoAdvanceDelaySeconds = Math.Clamp(step.AutoAdvanceDelaySeconds, 1, 86400);
                _runOfShowSteps.Add(step);
            }

            _runOfShowCurrentIndex = -1;
            await PersistRunOfShowAsync();
            RunOfShowStepsList.ItemsSource = null;
            RunOfShowStepsList.ItemsSource = _runOfShowSteps;
            RunOfShowStepsList.SelectedIndex = _runOfShowSteps.Count > 0 ? 0 : -1;
            UpdateRunOfShowStatus();
            RunOfShowStatusText.Text = $"{document.Steps.Count} Regieschritte importiert.";
            _appLogger.Write(AppLogLevel.Information, "RunOfShow.Import", RunOfShowStatusText.Text);
        }
        catch (Exception ex)
        {
            RunOfShowStatusText.Text = "Regieplan konnte nicht importiert werden: " + ex.Message;
            _appLogger.Write(AppLogLevel.Error, "RunOfShow.Import", RunOfShowStatusText.Text);
        }
    }

    private async Task ValidateRunOfShowAsync()
    {
        try
        {
            if (RunOfShowStepsList.SelectedItem is RunOfShowStepSettings selected) ReadRunOfShowEditor(selected);
            if (_runOfShowSteps.Count == 0)
            {
                RunOfShowStatusText.Text = "Der Regieplan enthält noch keine Schritte.";
                return;
            }

            var issues = new List<string>();
            var obsScenes = _obsClient.IsConnected ? await _obsClient.GetSceneListAsync() : [];
            var sceneNames = obsScenes.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var duplicateNames = _runOfShowSteps.Where(x => x.Enabled).GroupBy(x => x.Name.Trim(), StringComparer.OrdinalIgnoreCase).Where(x => !string.IsNullOrWhiteSpace(x.Key) && x.Count() > 1).Select(x => x.Key);
            foreach (var name in duplicateNames) issues.Add($"Doppelter Schrittname: {name}");

            for (var i = 0; i < _runOfShowSteps.Count; i++)
            {
                var step = _runOfShowSteps[i];
                var label = $"Schritt {i + 1} ({(string.IsNullOrWhiteSpace(step.Name) ? "ohne Name" : step.Name)})";
                if (string.IsNullOrWhiteSpace(step.Name)) issues.Add(label + ": Name fehlt.");
                if (step.Enabled && string.IsNullOrWhiteSpace(step.ObsScene)) issues.Add(label + ": Keine OBS-Szene ausgewählt.");
                else if (step.Enabled && _obsClient.IsConnected && !sceneNames.Contains(step.ObsScene)) issues.Add(label + $": OBS-Szene '{step.ObsScene}' wurde nicht gefunden.");
                if (step.UpdateTwitchChannel && string.IsNullOrWhiteSpace(step.TwitchTitle) && string.IsNullOrWhiteSpace(step.TwitchCategoryId)) issues.Add(label + ": Twitch-Aktualisierung ist aktiv, aber Titel und Kategorie fehlen.");
                if (step.AutoAdvance && step.AutoAdvanceDelaySeconds < 1) issues.Add(label + ": Automatische Wartezeit muss mindestens 1 Sekunde betragen.");
            }

            if (issues.Count == 0)
            {
                RunOfShowStatusText.Text = $"Regieplan geprüft: {_runOfShowSteps.Count} Schritte, keine Fehler gefunden.";
                MessageBox.Show(this, RunOfShowStatusText.Text, "Regieplanprüfung", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                RunOfShowStatusText.Text = $"Regieplanprüfung: {issues.Count} Hinweis(e) gefunden.";
                MessageBox.Show(this, string.Join(Environment.NewLine, issues.Take(25)) + (issues.Count > 25 ? $"{Environment.NewLine}... und {issues.Count - 25} weitere." : string.Empty), "Regieplanprüfung", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            _appLogger.Write(issues.Count == 0 ? AppLogLevel.Information : AppLogLevel.Warning, "RunOfShow.Validation", RunOfShowStatusText.Text);
        }
        catch (Exception ex)
        {
            RunOfShowStatusText.Text = "Regieplanprüfung fehlgeschlagen: " + ex.Message;
            _appLogger.Write(AppLogLevel.Error, "RunOfShow.Validation", RunOfShowStatusText.Text);
        }
    }

    private static RunOfShowStepSettings CloneRunOfShowStep(RunOfShowStepSettings source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Enabled = source.Enabled,
        ObsScene = source.ObsScene,
        TransitionName = source.TransitionName,
        TransitionDurationMilliseconds = source.TransitionDurationMilliseconds,
        SpotifyAction = source.SpotifyAction,
        SpotifyVolumePercent = source.SpotifyVolumePercent,
        StreamerBotActionId = source.StreamerBotActionId,
        StreamerBotActionName = source.StreamerBotActionName,
        ActionDelayMilliseconds = source.ActionDelayMilliseconds,
        ContinueOnActionError = source.ContinueOnActionError,
        UpdateTwitchChannel = source.UpdateTwitchChannel,
        TwitchTitle = source.TwitchTitle,
        TwitchCategoryId = source.TwitchCategoryId,
        TwitchCategoryName = source.TwitchCategoryName,
        ContinueOnTwitchError = source.ContinueOnTwitchError,
        AutoAdvance = source.AutoAdvance,
        AutoAdvanceDelaySeconds = source.AutoAdvanceDelaySeconds
    };

    private async Task ExecuteSelectedRunOfShowStepAsync()
    {
        if (RunOfShowStepsList.SelectedItem is not RunOfShowStepSettings step) return;
        ReadRunOfShowEditor(step);
        await ExecuteRunOfShowStepAsync(step);
        _runOfShowCurrentIndex = _runOfShowSteps.IndexOf(step);
        UpdateRunOfShowStatus();
    }

    private async Task ExecuteNextRunOfShowStepAsync()
    {
        if (_runOfShowSteps.Count == 0) { RunOfShowStatusText.Text = "Noch keine Regieschritte vorhanden."; return; }
        var nextIndex = _runOfShowCurrentIndex + 1;
        while (nextIndex < _runOfShowSteps.Count && !_runOfShowSteps[nextIndex].Enabled) nextIndex++;
        if (nextIndex >= _runOfShowSteps.Count) { RunOfShowStatusText.Text = "Regieplan ist beendet."; return; }
        var step = _runOfShowSteps[nextIndex];
        RunOfShowStepsList.SelectedItem = step;
        await ExecuteRunOfShowStepAsync(step);
        _runOfShowCurrentIndex = nextIndex;
        UpdateRunOfShowStatus();
    }

    private async Task ExecuteRunOfShowStepAsync(RunOfShowStepSettings step)
    {
        try
        {
            string? executionWarning = null;
            if (!_obsClient.IsConnected) throw new InvalidOperationException("OBS ist nicht verbunden.");
            if (string.IsNullOrWhiteSpace(step.ObsScene)) throw new InvalidOperationException("Keine OBS-Szene ausgewählt.");
            if (!string.IsNullOrWhiteSpace(step.TransitionName))
            {
                await _obsClient.SetCurrentSceneTransitionAsync(step.TransitionName);
                await _obsClient.SetCurrentSceneTransitionDurationAsync(step.TransitionDurationMilliseconds);
            }
            await _obsClient.SetCurrentProgramSceneAsync(step.ObsScene);
            if (string.Equals(step.SpotifyAction, "Pause", StringComparison.OrdinalIgnoreCase)) await _spotifyModule.PauseAsync();
            else if (string.Equals(step.SpotifyAction, "Resume", StringComparison.OrdinalIgnoreCase)) await _spotifyModule.ResumeAsync();
            else if (string.Equals(step.SpotifyAction, "SetVolume", StringComparison.OrdinalIgnoreCase)) await _spotifyModule.SetVolumeAsync(step.SpotifyVolumePercent);

            if (step.UpdateTwitchChannel)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(step.TwitchTitle) && string.IsNullOrWhiteSpace(step.TwitchCategoryId))
                        throw new InvalidOperationException("Für die Twitch-Aktualisierung ist weder ein Titel noch eine Kategorie eingetragen.");
                    await _twitchModule.UpdateChannelAsync(
                        string.IsNullOrWhiteSpace(step.TwitchTitle) ? null : step.TwitchTitle,
                        string.IsNullOrWhiteSpace(step.TwitchCategoryId) ? null : step.TwitchCategoryId);
                    _appLogger.Write(AppLogLevel.Information, "RunOfShow.Twitch", $"{step.Name}: Twitch-Kanal aktualisiert.");
                }
                catch (Exception twitchException)
                {
                    _appLogger.Write(AppLogLevel.Error, "RunOfShow.Twitch", $"{step.Name}: {twitchException.Message}");
                    if (!step.ContinueOnTwitchError) throw;
                    executionWarning = string.IsNullOrWhiteSpace(executionWarning)
                        ? "Twitch: " + twitchException.Message
                        : executionWarning + " | Twitch: " + twitchException.Message;
                }
            }

            if (!string.IsNullOrWhiteSpace(step.StreamerBotActionId) || !string.IsNullOrWhiteSpace(step.StreamerBotActionName))
            {
                if (step.ActionDelayMilliseconds > 0) await Task.Delay(step.ActionDelayMilliseconds);
                try
                {
                    if (_streamerBotSocket is null || _streamerBotSocket.State != System.Net.WebSockets.WebSocketState.Open)
                        throw new InvalidOperationException("Streamer.bot ist nicht verbunden.");
                    var action = new { id = step.StreamerBotActionId, name = step.StreamerBotActionName };
                    using var response = await SendStreamerBotRequestAsync(new
                    {
                        request = "DoAction",
                        action,
                        args = new { source = "Creator Control Suite", runOfShowStep = step.Name }
                    });
                    var status = response.RootElement.TryGetProperty("status", out var statusNode) ? statusNode.GetString() : null;
                    if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Streamer.bot hat die Regieaktion nicht bestätigt.");
                }
                catch (Exception actionException)
                {
                    _appLogger.Write(AppLogLevel.Error, "RunOfShow.StreamerBot", $"{step.Name}: {actionException.Message}");
                    if (!step.ContinueOnActionError) throw;
                    executionWarning = actionException.Message;
                }
            }
            _appLogger.Write(AppLogLevel.Information, "RunOfShow", $"Regieschritt ausgeführt: {step.Name}");
            RunOfShowStatusText.Text = executionWarning is null
                ? $"Ausgeführt: {step.Name}"
                : $"Ausgeführt mit Warnung: {step.Name} – {executionWarning}";
        }
        catch (Exception ex)
        {
            RunOfShowStatusText.Text = "Regieschritt fehlgeschlagen: " + ex.Message;
            _appLogger.Write(AppLogLevel.Error, "RunOfShow", RunOfShowStatusText.Text);
        }
    }

    private async Task StartAutomaticRunOfShowAsync()
    {
        if (_runOfShowAutoCts is not null)
        {
            RunOfShowStatusText.Text = "Der automatische Regieplan läuft bereits.";
            return;
        }
        if (_runOfShowSteps.All(x => !x.Enabled))
        {
            RunOfShowStatusText.Text = "Es ist kein aktiver Regieschritt vorhanden.";
            return;
        }

        _runOfShowAutoCts = new CancellationTokenSource();
        var token = _runOfShowAutoCts.Token;
        StartAutomaticRunOfShowButton.IsEnabled = false;
        StopAutomaticRunOfShowButton.IsEnabled = true;
        RunOfShowStatusText.Text = "Automatischer Regieplan gestartet.";

        try
        {
            while (!token.IsCancellationRequested)
            {
                var nextIndex = _runOfShowCurrentIndex + 1;
                while (nextIndex < _runOfShowSteps.Count && !_runOfShowSteps[nextIndex].Enabled) nextIndex++;
                if (nextIndex >= _runOfShowSteps.Count)
                {
                    RunOfShowStatusText.Text = "Automatischer Regieplan beendet.";
                    break;
                }

                var step = _runOfShowSteps[nextIndex];
                RunOfShowStepsList.SelectedItem = step;
                RunOfShowStepsList.ScrollIntoView(step);
                await ExecuteRunOfShowStepAsync(step);
                _runOfShowCurrentIndex = nextIndex;
                UpdateRunOfShowStatus();

                if (!step.AutoAdvance)
                {
                    RunOfShowStatusText.Text = $"Automatik wartet nach: {step.Name}. Nächsten Schritt manuell starten oder Automatik erneut starten.";
                    break;
                }

                var delaySeconds = Math.Clamp(step.AutoAdvanceDelaySeconds, 1, 86400);
                for (var remaining = delaySeconds; remaining > 0; remaining--)
                {
                    token.ThrowIfCancellationRequested();
                    RunOfShowStatusText.Text = $"{step.Name} ausgeführt. Nächster Schritt in {remaining} s.";
                    await Task.Delay(TimeSpan.FromSeconds(1), token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            RunOfShowStatusText.Text = "Automatischer Regieplan gestoppt.";
        }
        catch (Exception ex)
        {
            RunOfShowStatusText.Text = "Automatischer Regieplan fehlgeschlagen: " + ex.Message;
            _appLogger.Write(AppLogLevel.Error, "RunOfShow.Auto", RunOfShowStatusText.Text);
        }
        finally
        {
            _runOfShowAutoCts?.Dispose();
            _runOfShowAutoCts = null;
            StartAutomaticRunOfShowButton.IsEnabled = true;
            StopAutomaticRunOfShowButton.IsEnabled = false;
        }
    }

    private void StopAutomaticRunOfShow()
    {
        _runOfShowAutoCts?.Cancel();
    }

    private void ResetRunOfShow()
    {
        StopAutomaticRunOfShow();
        _runOfShowCurrentIndex = -1;
        UpdateRunOfShowStatus();
    }

    private void UpdateRunOfShowStatus()
    {
        if (RunOfShowStatusText is null) return;
        var next = _runOfShowSteps.Skip(_runOfShowCurrentIndex + 1).FirstOrDefault(x => x.Enabled);
        RunOfShowCurrentText.Text = _runOfShowCurrentIndex >= 0 && _runOfShowCurrentIndex < _runOfShowSteps.Count ? _runOfShowSteps[_runOfShowCurrentIndex].Name : "Noch nicht gestartet";
        RunOfShowNextText.Text = next?.Name ?? "Kein weiterer Schritt";
        RunOfShowProgressText.Text = _runOfShowSteps.Count == 0 ? "0 / 0" : $"{Math.Max(0, _runOfShowCurrentIndex + 1)} / {_runOfShowSteps.Count}";
    }

    private void RefreshTimedAutomationRules()
    {
        if (TimedAutomationRulesList is null) return;
        _timedAutomationRules.Clear();
        foreach (var rule in _settings.Workflow.TimedAutomations) _timedAutomationRules.Add(rule);
        if (TimedAutomationNextRuleBox is not null) TimedAutomationNextRuleBox.ItemsSource = _timedAutomationRules.ToList();
        if (_timedAutomationRules.Count > 0 && TimedAutomationRulesList.SelectedItem is null)
            TimedAutomationRulesList.SelectedIndex = 0;
    }

    private static string ComboTag(ComboBox box, string fallback)
        => (box.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private async Task ExportTimedAutomationsAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Automatisierungsregeln exportieren",
            Filter = "Creator-Control-Automationen (*.ccsautomation.json)|*.ccsautomation.json|JSON (*.json)|*.json",
            FileName = $"CreatorControlSuite-Automationen-{DateTime.Now:yyyyMMdd-HHmm}.ccsautomation.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        var package = new TimedAutomationExportPackage
        {
            ExportedAt = DateTimeOffset.Now,
            Rules = _timedAutomationRules.Select(CloneTimedAutomationRule).ToList()
        };
        await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(package, new JsonSerializerOptions { WriteIndented = true }));
        AddTimedAutomationDiagnostic($"Exportiert: {package.Rules.Count} Regeln nach {Path.GetFileName(dialog.FileName)}.");
    }

    private async Task ImportTimedAutomationsAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Automatisierungsregeln importieren",
            Filter = "Creator-Control-Automationen (*.ccsautomation.json;*.json)|*.ccsautomation.json;*.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var package = JsonSerializer.Deserialize<TimedAutomationExportPackage>(await File.ReadAllTextAsync(dialog.FileName), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (package?.Rules is null || package.Rules.Count == 0) throw new InvalidDataException("Die Datei enthält keine Regeln.");
            var idMap = package.Rules.ToDictionary(x => x.Id, _ => Guid.NewGuid().ToString("N"), StringComparer.OrdinalIgnoreCase);
            foreach (var imported in package.Rules)
            {
                var clone = CloneTimedAutomationRule(imported);
                clone.Id = idMap[imported.Id];
                clone.Name = EnsureUniqueAutomationName(clone.Name);
                clone.NextRuleId = !string.IsNullOrWhiteSpace(imported.NextRuleId) && idMap.TryGetValue(imported.NextRuleId, out var nextId) ? nextId : "";
                clone.DependencyRuleId = !string.IsNullOrWhiteSpace(imported.DependencyRuleId) && idMap.TryGetValue(imported.DependencyRuleId, out var dependencyId) ? dependencyId : "";
                clone.FailureRuleId = !string.IsNullOrWhiteSpace(imported.FailureRuleId) && idMap.TryGetValue(imported.FailureRuleId, out var failureId) ? failureId : "";
                clone.RollbackRuleId = !string.IsNullOrWhiteSpace(imported.RollbackRuleId) && idMap.TryGetValue(imported.RollbackRuleId, out var rollbackId) ? rollbackId : "";
                _settings.Workflow.TimedAutomations.Add(clone);
            }
            await _settingsStore.SaveAsync(_settings);
            RefreshTimedAutomationRules();
            AddTimedAutomationDiagnostic($"Importiert: {package.Rules.Count} Regeln aus {Path.GetFileName(dialog.FileName)}.");
            ValidateTimedAutomationRules();
        }
        catch (Exception ex)
        {
            AddTimedAutomationDiagnostic($"Import fehlgeschlagen: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Import fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task AddTimedAutomationTemplateAsync()
    {
        var result = MessageBox.Show(this,
            "Vorlage '10-Minuten-Streamstart' anlegen?\n\nSie erstellt drei verkettete Regeln: direkt beim Streamstart, nach 5 Minuten Intro-Quelle ausblenden und nach 10 Minuten auf die Game-Szene wechseln.",
            "Automationsvorlage", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;
        var start = new TimedAutomationRuleSettings { Name = EnsureUniqueAutomationName("Streamstart – Initialisierung"), TriggerType = "StreamStarted", DelaySeconds = 0, ActionType = "SpotifyOnly", SpotifyAction = "Resume", OncePerStream = true };
        var intro = new TimedAutomationRuleSettings { Name = EnsureUniqueAutomationName("Streamstart – Intro ausblenden"), TriggerType = "StreamElapsed", DelaySeconds = 300, ActionType = "SetSourceVisibility", ObsScene = "Start", ObsSource = "Intro", SourceVisible = false, OncePerStream = true };
        var game = new TimedAutomationRuleSettings { Name = EnsureUniqueAutomationName("Streamstart – Game wechseln"), TriggerType = "StreamElapsed", DelaySeconds = 600, ActionType = "SwitchScene", TargetScene = "Game", OncePerStream = true };
        start.NextRuleId = intro.Id;
        intro.NextRuleId = game.Id;
        _settings.Workflow.TimedAutomations.Add(start);
        _settings.Workflow.TimedAutomations.Add(intro);
        _settings.Workflow.TimedAutomations.Add(game);
        await _settingsStore.SaveAsync(_settings);
        RefreshTimedAutomationRules();
        TimedAutomationRulesList.SelectedItem = start;
        AddTimedAutomationDiagnostic("Vorlage angelegt: 10-Minuten-Streamstart. Szenen- und Quellnamen bitte prüfen.");
        ValidateTimedAutomationRules();
    }

    private string EnsureUniqueAutomationName(string baseName)
    {
        var name = string.IsNullOrWhiteSpace(baseName) ? "Importierte Automatisierung" : baseName.Trim();
        var existing = _settings.Workflow.TimedAutomations.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(name)) return name;
        for (var i = 2; ; i++) if (!existing.Contains($"{name} ({i})")) return $"{name} ({i})";
    }

    private static TimedAutomationRuleSettings CloneTimedAutomationRule(TimedAutomationRuleSettings rule)
        => JsonSerializer.Deserialize<TimedAutomationRuleSettings>(JsonSerializer.Serialize(rule)) ?? new TimedAutomationRuleSettings();

    private static string DescribeTimedAutomationAction(TimedAutomationRuleSettings rule)
    {
        var action = rule.ActionType switch
        {
            "SwitchScene" => $"Szene '{rule.TargetScene}' aktivieren",
            "SetSourceVisibility" => $"Quelle '{rule.ObsSource}' in '{rule.ObsScene}' {(rule.SourceVisible ? "einblenden" : "ausblenden")}",
            "SetInputMute" => $"Audioquelle '{rule.ObsInput}' {(rule.InputMuted ? "muten" : "aktivieren")}",
            "StartObsStream" => "OBS-Stream starten",
            "StopObsStream" => "OBS-Stream stoppen",
            "StreamerBotAction" => $"Streamer.bot-Aktion '{rule.StreamerBotActionName}' ausführen",
            _ => "keine OBS-Aktion"
        };
        if (!string.Equals(rule.SpotifyAction, "None", StringComparison.OrdinalIgnoreCase)) action += $", Spotify: {rule.SpotifyAction}";
        return action;
    }

    private void CreateNewTimedAutomationRule()
    {
        var rule = new TimedAutomationRuleSettings();
        _settings.Workflow.TimedAutomations.Add(rule);
        _timedAutomationRules.Add(rule);
        TimedAutomationRulesList.SelectedItem = rule;
    }

    private void LoadSelectedTimedAutomationRule()
    {
        if (TimedAutomationRulesList.SelectedItem is not TimedAutomationRuleSettings rule) return;
        TimedAutomationEnabledBox.IsChecked = rule.Enabled;
        TimedAutomationNameBox.Text = rule.Name;
        SelectComboByTag(TimedAutomationTriggerTypeBox, rule.TriggerType);
        TimedAutomationTriggerSceneBox.Text = rule.TriggerScene;
        TimedAutomationDelayBox.Text = rule.DelaySeconds.ToString();
        TimedAutomationScheduleTimeBox.Text = rule.ScheduleTime;
        TimedAutomationScheduleDaysBox.Text = rule.ScheduleDays;
        TimedAutomationScheduleDateBox.Text = rule.ScheduleDate;
        TimedAutomationActiveFromBox.Text = rule.ActiveFromDate;
        TimedAutomationActiveUntilBox.Text = rule.ActiveUntilDate;
        TimedAutomationExcludedDatesBox.Text = rule.ExcludedDates;
        TimedAutomationBlackoutRangesBox.Text = rule.BlackoutRanges;
        SelectComboByTag(TimedAutomationMissedRunBehaviorBox, rule.MissedRunBehavior);
        TimedAutomationCatchUpGraceBox.Text = rule.CatchUpGraceMinutes.ToString();
        TimedAutomationNextRunText.Text = $"Nächster geplanter Lauf: {DescribeNextScheduledRun(rule)}";
        SelectComboByTag(TimedAutomationActionTypeBox, rule.ActionType);
        TimedAutomationTargetSceneBox.Text = rule.TargetScene;
        TimedAutomationTransitionBox.Text = rule.TransitionName;
        TimedAutomationTransitionDurationBox.Text = rule.TransitionDurationMilliseconds.ToString();
        TimedAutomationSourceSceneBox.Text = rule.ObsScene;
        TimedAutomationSourceBox.Text = rule.ObsSource;
        TimedAutomationSourceVisibleBox.IsChecked = rule.SourceVisible;
        TimedAutomationResetSourceBox.IsChecked = rule.ResetSourceAtStreamEnd;
        TimedAutomationResetVisibleBox.IsChecked = rule.ResetSourceVisible;
        SelectComboByTag(TimedAutomationSpotifyActionBox, rule.SpotifyAction);
        TimedAutomationSpotifyVolumeBox.Text = rule.SpotifyVolumePercent.ToString();
        TimedAutomationSpotifyPlaylistUriBox.Text = rule.SpotifyPlaylistUri;
        TimedAutomationSpotifyPlaylistShuffleBox.IsChecked = rule.SpotifyPlaylistShuffle;
        TimedAutomationSpotifyDelayBox.Text = rule.SpotifyActionDelaySeconds.ToString();
        TimedAutomationSpotifyFadeBox.Text = rule.SpotifyFadeSeconds.ToString();
        TimedAutomationSpotifyPriorityBox.Text = rule.SpotifyPriority.ToString();
        TimedAutomationSpotifyGroupBox.Text = string.IsNullOrWhiteSpace(rule.SpotifyAutomationGroup) ? "Standard" : rule.SpotifyAutomationGroup;
        TimedAutomationSpotifyExclusiveGroupBox.IsChecked = rule.SpotifyExclusiveGroup;
        TimedAutomationSpotifySavePreviousBox.IsChecked = rule.SpotifySavePreviousState;
        TimedAutomationSpotifyAutoRestoreBox.IsChecked = rule.SpotifyAutoRestorePreviousState;
        TimedAutomationSpotifyAutoRestoreDelayBox.Text = rule.SpotifyAutoRestoreDelaySeconds.ToString();
        TimedAutomationSpotifyAutoRestoreSameSceneBox.IsChecked = rule.SpotifyAutoRestoreRequireSameScene;
        TimedAutomationSpotifyAutoRestoreSameGroupBox.IsChecked = rule.SpotifyAutoRestoreRequireSameGroup;
        TimedAutomationSpotifyAutoRestoreUnchangedPlaybackBox.IsChecked = rule.SpotifyAutoRestoreRequireUnchangedPlayback;
        TimedAutomationOncePerStreamBox.IsChecked = rule.OncePerStream;
        TimedAutomationInputBox.Text = rule.ObsInput;
        TimedAutomationInputMutedBox.IsChecked = rule.InputMuted;
        SelectStreamerBotAction(TimedAutomationStreamerBotActionBox, rule.StreamerBotActionId, rule.StreamerBotActionName);
        SelectComboByTag(TimedAutomationConditionTypeBox, rule.ConditionType);
        TimedAutomationConditionValueBox.Text = rule.ConditionValue;
        TimedAutomationConditionNegatedBox.IsChecked = rule.ConditionNegated;
        TimedAutomationNextRuleBox.ItemsSource = _timedAutomationRules.Where(x => !string.Equals(x.Id, rule.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        TimedAutomationNextRuleBox.SelectedItem = _timedAutomationRules.FirstOrDefault(x => string.Equals(x.Id, rule.NextRuleId, StringComparison.OrdinalIgnoreCase));
        TimedAutomationNextRuleDelayBox.Text = rule.NextRuleDelaySeconds.ToString();
        TimedAutomationContinueChainOnErrorBox.IsChecked = rule.ContinueChainOnError;
        TimedAutomationPriorityBox.Text = rule.Priority.ToString();
        TimedAutomationTimeoutBox.Text = rule.TimeoutSeconds.ToString();
        SelectComboByTag(TimedAutomationExecutionModeBox, rule.ExecutionMode);
        TimedAutomationDependencyRuleBox.ItemsSource = _timedAutomationRules.Where(x => !string.Equals(x.Id, rule.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        TimedAutomationDependencyRuleBox.SelectedItem = _timedAutomationRules.FirstOrDefault(x => string.Equals(x.Id, rule.DependencyRuleId, StringComparison.OrdinalIgnoreCase));
        TimedAutomationRetryCountBox.Text = rule.RetryCount.ToString();
        TimedAutomationRetryDelayBox.Text = rule.RetryDelaySeconds.ToString();
        TimedAutomationFailureRuleBox.ItemsSource = _timedAutomationRules.Where(x => !string.Equals(x.Id, rule.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        TimedAutomationFailureRuleBox.SelectedItem = _timedAutomationRules.FirstOrDefault(x => string.Equals(x.Id, rule.FailureRuleId, StringComparison.OrdinalIgnoreCase));
        TimedAutomationWorkflowGroupBox.Text = rule.WorkflowGroup;
        TimedAutomationWorkflowOrderBox.Text = rule.WorkflowOrder.ToString();
        TimedAutomationStartWorkflowBox.IsChecked = rule.StartWorkflowGroup;
        SelectComboByTag(TimedAutomationWorkflowFailureModeBox, rule.WorkflowFailureMode);
        TimedAutomationRollbackRuleBox.ItemsSource = _timedAutomationRules.Where(x => !string.Equals(x.Id, rule.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        TimedAutomationRollbackRuleBox.SelectedItem = _timedAutomationRules.FirstOrDefault(x => string.Equals(x.Id, rule.RollbackRuleId, StringComparison.OrdinalIgnoreCase));
        TimedAutomationHistoryText.Text = $"Letzter Lauf: {(string.IsNullOrWhiteSpace(rule.LastRunAt) ? "Noch nie" : rule.LastRunAt)} | Status: {rule.LastRunStatus} | Erfolgreich: {rule.SuccessfulRuns} | Fehler: {rule.FailedRuns} | Übersprungen: {rule.SkippedRuns}";
    }

    private static void SelectComboByTag(ComboBox box, string tag)
    {
        foreach (var item in box.Items.OfType<ComboBoxItem>())
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase)) { box.SelectedItem = item; return; }
    }

    private TimedAutomationRuleSettings ReadTimedAutomationEditor(TimedAutomationRuleSettings? target = null)
    {
        var rule = target ?? new TimedAutomationRuleSettings();
        rule.Enabled = TimedAutomationEnabledBox.IsChecked == true;
        rule.Name = string.IsNullOrWhiteSpace(TimedAutomationNameBox.Text) ? "Neue Automatisierung" : TimedAutomationNameBox.Text.Trim();
        rule.TriggerType = ComboTag(TimedAutomationTriggerTypeBox, "StreamElapsed");
        rule.TriggerScene = TimedAutomationTriggerSceneBox.Text.Trim();
        rule.DelaySeconds = int.TryParse(TimedAutomationDelayBox.Text, out var delay) ? Math.Max(0, delay) : 10;
        rule.ScheduleTime = TimeOnly.TryParse(TimedAutomationScheduleTimeBox.Text, out var scheduleTime) ? scheduleTime.ToString("HH:mm") : "20:00";
        rule.ScheduleDays = TimedAutomationScheduleDaysBox.Text.Trim();
        rule.ScheduleDate = DateOnly.TryParse(TimedAutomationScheduleDateBox.Text, out var scheduleDate) ? scheduleDate.ToString("yyyy-MM-dd") : "";
        rule.ActiveFromDate = DateOnly.TryParse(TimedAutomationActiveFromBox.Text, out var activeFrom) ? activeFrom.ToString("yyyy-MM-dd") : "";
        rule.ActiveUntilDate = DateOnly.TryParse(TimedAutomationActiveUntilBox.Text, out var activeUntil) ? activeUntil.ToString("yyyy-MM-dd") : "";
        rule.ExcludedDates = TimedAutomationExcludedDatesBox.Text.Trim();
        rule.BlackoutRanges = TimedAutomationBlackoutRangesBox.Text.Trim();
        rule.MissedRunBehavior = ComboTag(TimedAutomationMissedRunBehaviorBox, "SameDay");
        rule.CatchUpGraceMinutes = int.TryParse(TimedAutomationCatchUpGraceBox.Text, out var graceMinutes) ? Math.Clamp(graceMinutes, 0, 1440) : 30;
        rule.ActionType = ComboTag(TimedAutomationActionTypeBox, "SwitchScene");
        rule.TargetScene = TimedAutomationTargetSceneBox.Text.Trim();
        rule.TransitionName = TimedAutomationTransitionBox.Text.Trim();
        rule.TransitionDurationMilliseconds = int.TryParse(TimedAutomationTransitionDurationBox.Text, out var transitionMs) ? Math.Clamp(transitionMs, 50, 20000) : 1000;
        rule.ObsScene = TimedAutomationSourceSceneBox.Text.Trim();
        rule.ObsSource = TimedAutomationSourceBox.Text.Trim();
        rule.SourceVisible = TimedAutomationSourceVisibleBox.IsChecked == true;
        rule.ResetSourceAtStreamEnd = TimedAutomationResetSourceBox.IsChecked == true;
        rule.ResetSourceVisible = TimedAutomationResetVisibleBox.IsChecked == true;
        rule.SpotifyAction = ComboTag(TimedAutomationSpotifyActionBox, "None");
        rule.SpotifyVolumePercent = int.TryParse(TimedAutomationSpotifyVolumeBox.Text, out var volume) ? Math.Clamp(volume, 0, 100) : 35;
        rule.SpotifyPlaylistUri = TimedAutomationSpotifyPlaylistUriBox.Text.Trim();
        rule.SpotifyPlaylistShuffle = TimedAutomationSpotifyPlaylistShuffleBox.IsChecked == true;
        rule.SpotifyActionDelaySeconds = int.TryParse(TimedAutomationSpotifyDelayBox.Text, out var spotifyDelay) ? Math.Clamp(spotifyDelay, 0, 3600) : 0;
        rule.SpotifyFadeSeconds = int.TryParse(TimedAutomationSpotifyFadeBox.Text, out var spotifyFade) ? Math.Clamp(spotifyFade, 0, 120) : 0;
        rule.SpotifyPriority = int.TryParse(TimedAutomationSpotifyPriorityBox.Text, out var spotifyPriority) ? Math.Clamp(spotifyPriority, -1000, 1000) : 0;
        rule.SpotifyAutomationGroup = string.IsNullOrWhiteSpace(TimedAutomationSpotifyGroupBox.Text) ? "Standard" : TimedAutomationSpotifyGroupBox.Text.Trim();
        rule.SpotifyExclusiveGroup = TimedAutomationSpotifyExclusiveGroupBox.IsChecked == true;
        rule.SpotifySavePreviousState = TimedAutomationSpotifySavePreviousBox.IsChecked == true;
        rule.SpotifyAutoRestorePreviousState = TimedAutomationSpotifyAutoRestoreBox.IsChecked == true;
        rule.SpotifyAutoRestoreDelaySeconds = int.TryParse(TimedAutomationSpotifyAutoRestoreDelayBox.Text, out var spotifyAutoRestoreDelay) ? Math.Clamp(spotifyAutoRestoreDelay, 1, 86400) : 30;
        rule.SpotifyAutoRestoreRequireSameScene = TimedAutomationSpotifyAutoRestoreSameSceneBox.IsChecked == true;
        rule.SpotifyAutoRestoreRequireSameGroup = TimedAutomationSpotifyAutoRestoreSameGroupBox.IsChecked == true;
        rule.SpotifyAutoRestoreRequireUnchangedPlayback = TimedAutomationSpotifyAutoRestoreUnchangedPlaybackBox.IsChecked == true;
        rule.OncePerStream = TimedAutomationOncePerStreamBox.IsChecked == true;
        rule.ObsInput = TimedAutomationInputBox.Text.Trim();
        rule.InputMuted = TimedAutomationInputMutedBox.IsChecked == true;
        var timedAction = TimedAutomationStreamerBotActionBox.SelectedItem as StreamerBotActionOption;
        rule.StreamerBotActionId = timedAction?.Id ?? "";
        rule.StreamerBotActionName = timedAction?.Name ?? TimedAutomationStreamerBotActionBox.Text.Trim();
        rule.ConditionType = ComboTag(TimedAutomationConditionTypeBox, "None");
        rule.ConditionValue = TimedAutomationConditionValueBox.Text.Trim();
        rule.ConditionNegated = TimedAutomationConditionNegatedBox.IsChecked == true;
        rule.NextRuleId = (TimedAutomationNextRuleBox.SelectedItem as TimedAutomationRuleSettings)?.Id ?? "";
        rule.NextRuleDelaySeconds = int.TryParse(TimedAutomationNextRuleDelayBox.Text, out var nextDelay) ? Math.Clamp(nextDelay, 0, 86400) : 0;
        rule.ContinueChainOnError = TimedAutomationContinueChainOnErrorBox.IsChecked == true;
        rule.Priority = int.TryParse(TimedAutomationPriorityBox.Text, out var priority) ? Math.Clamp(priority, -1000, 1000) : 0;
        rule.TimeoutSeconds = int.TryParse(TimedAutomationTimeoutBox.Text, out var timeout) ? Math.Clamp(timeout, 1, 86400) : 60;
        rule.ExecutionMode = ComboTag(TimedAutomationExecutionModeBox, "SkipIfRunning");
        rule.DependencyRuleId = (TimedAutomationDependencyRuleBox.SelectedItem as TimedAutomationRuleSettings)?.Id ?? "";
        rule.DependencyRequiredStatus = "Erfolgreich";
        rule.RetryCount = int.TryParse(TimedAutomationRetryCountBox.Text, out var retryCount) ? Math.Clamp(retryCount, 0, 20) : 0;
        rule.RetryDelaySeconds = int.TryParse(TimedAutomationRetryDelayBox.Text, out var retryDelay) ? Math.Clamp(retryDelay, 0, 3600) : 5;
        rule.FailureRuleId = (TimedAutomationFailureRuleBox.SelectedItem as TimedAutomationRuleSettings)?.Id ?? "";
        rule.WorkflowGroup = TimedAutomationWorkflowGroupBox.Text.Trim();
        rule.WorkflowOrder = int.TryParse(TimedAutomationWorkflowOrderBox.Text, out var workflowOrder) ? Math.Clamp(workflowOrder, -1000, 1000) : 0;
        rule.StartWorkflowGroup = TimedAutomationStartWorkflowBox.IsChecked == true;
        rule.WorkflowFailureMode = ComboTag(TimedAutomationWorkflowFailureModeBox, "Stop");
        rule.RollbackRuleId = (TimedAutomationRollbackRuleBox.SelectedItem as TimedAutomationRuleSettings)?.Id ?? "";
        return rule;
    }

    private async Task SaveTimedAutomationRuleAsync()
    {
        var selected = TimedAutomationRulesList.SelectedItem as TimedAutomationRuleSettings;
        if (selected is null) { CreateNewTimedAutomationRule(); selected = TimedAutomationRulesList.SelectedItem as TimedAutomationRuleSettings; }
        if (selected is null) return;
        ReadTimedAutomationEditor(selected);
        TimedAutomationRulesList.Items.Refresh();
        _settings.Workflow.TimedAutomations = _timedAutomationRules
            .Select(rule => rule)
            .ToList();
        await _settingsStore.SaveAsync(_settings);
        TimedAutomationTestStatusText.Text = "Regel gespeichert.";
    }

    private async Task DeleteSelectedTimedAutomationRuleAsync()
    {
        if (TimedAutomationRulesList.SelectedItem is not TimedAutomationRuleSettings rule) return;
        _settings.Workflow.TimedAutomations.Remove(rule); _timedAutomationRules.Remove(rule);
        await _settingsStore.SaveAsync(_settings);
    }

    private async Task RefreshTimedAutomationObsListsAsync(bool force = true)
    {
        if (_timedAutomationObsRefreshRunning)
        {
            return;
        }

        if (!force && DateTimeOffset.UtcNow - _lastTimedAutomationObsRefresh < TimeSpan.FromSeconds(3))
        {
            return;
        }

        if (!_obsClient.IsConnected)
        {
            TimedAutomationTestStatusText.Text = "OBS ist nicht verbunden.";
            return;
        }

        _timedAutomationObsRefreshRunning = true;
        try
        {
        var previousTrigger = TimedAutomationTriggerSceneBox.Text;
        var previousTarget = TimedAutomationTargetSceneBox.Text;
        var previousSourceScene = TimedAutomationSourceSceneBox.Text;
        var previousTransition = TimedAutomationTransitionBox.Text;

        var scenes = (await _obsClient.GetSceneListAsync())
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var transitions = (await _obsClient.GetSceneTransitionListAsync())
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        TimedAutomationTriggerSceneBox.ItemsSource = scenes;
        TimedAutomationTargetSceneBox.ItemsSource = scenes;
        TimedAutomationSourceSceneBox.ItemsSource = scenes;
        TimedAutomationTransitionBox.ItemsSource = transitions;

        TimedAutomationTriggerSceneBox.Text = previousTrigger;
        TimedAutomationTargetSceneBox.Text = previousTarget;
        TimedAutomationSourceSceneBox.Text = previousSourceScene;
        TimedAutomationTransitionBox.Text = previousTransition;

        var inputs = (await _obsClient.GetInputListAsync())
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var previousInput = TimedAutomationInputBox.Text;
        TimedAutomationInputBox.ItemsSource = inputs;
        TimedAutomationInputBox.Text = previousInput;
        await RefreshStreamerBotActionsAsync(false);
        TimedAutomationTestStatusText.Text = $"{scenes.Count} Szenen, {transitions.Count} Übergänge und {inputs.Count} Eingaben aus OBS geladen.";
        await RefreshTimedAutomationSourceListAsync();
        _lastTimedAutomationObsRefresh = DateTimeOffset.UtcNow;
        }
        finally
        {
            _timedAutomationObsRefreshRunning = false;
        }
    }

    private async Task RefreshTimedAutomationSourceListAsync()
    {
        var sceneName = TimedAutomationSourceSceneBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            sceneName = TimedAutomationSourceSceneBox.Text?.Trim();
        }
        if (!_obsClient.IsConnected || string.IsNullOrWhiteSpace(sceneName))
        {
            TimedAutomationSourceBox.ItemsSource = Array.Empty<string>();
            return;
        }

        var previousSource = TimedAutomationSourceBox.Text;
        try
        {
            var sources = (await _obsClient.GetSceneItemListAsync(sceneName))
                .Select(x => x.SourceName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            TimedAutomationSourceBox.ItemsSource = sources;
            TimedAutomationSourceBox.Text = previousSource;
            TimedAutomationTestStatusText.Text = $"{sources.Count} Quellen aus Szene ‘{sceneName}’ geladen.";
        }
        catch (Exception ex)
        {
            TimedAutomationSourceBox.ItemsSource = Array.Empty<string>();
            TimedAutomationTestStatusText.Text = "Quellen konnten nicht geladen werden: " + ex.Message;
        }
    }

    private async Task TestSelectedTimedAutomationRuleAsync()
    {
        var rule = ReadTimedAutomationEditor(TimedAutomationRulesList.SelectedItem as TimedAutomationRuleSettings);
        if (!_obsClient.IsConnected) { TimedAutomationTestStatusText.Text = "OBS verbinden, bevor der Test gestartet wird."; return; }
        _timedAutomationTestCts?.Cancel(); _timedAutomationTestCts = new CancellationTokenSource();
        var seconds = int.TryParse(TimedAutomationTestSecondsBox.Text, out var value) ? Math.Clamp(value, 0, 60) : 3;
        try
        {
            TimedAutomationTestStatusText.Text = $"Test läuft · Aktion in {seconds} Sekunde(n). Der Stream bleibt aus.";
            await Task.Delay(TimeSpan.FromSeconds(seconds), _timedAutomationTestCts.Token);
            await ExecuteTimedAutomationRuleAsync(rule, _timedAutomationTestCts.Token, simulate: TimedAutomationSimulationBox.IsChecked == true);
            TimedAutomationTestStatusText.Text = "Test erfolgreich in OBS ausgeführt.";
        }
        catch (OperationCanceledException) { TimedAutomationTestStatusText.Text = "Test abgebrochen."; }
        catch (Exception ex) { TimedAutomationTestStatusText.Text = "Test fehlgeschlagen: " + ex.Message; }
    }

    private async Task RunShortStreamTestAsync()
    {
        _timedAutomationTestCts?.Cancel();
        _timedAutomationTestCts = new CancellationTokenSource();
        var token = _timedAutomationTestCts.Token;
        ShortStreamTestResultsList.Items.Clear();
        StartShortStreamTestButton.IsEnabled = false;
        ShortStreamTestStatusText.Text = "Kurztest läuft. OBS-Streaming bleibt ausgeschaltet.";

        async Task AddResultAsync(string name, Func<Task> test)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await test();
                ShortStreamTestResultsList.Items.Add($"✓ {name}");
            }
            catch (Exception ex)
            {
                ShortStreamTestResultsList.Items.Add($"✗ {name}: {ex.Message}");
            }
        }

        try
        {
            if (ShortTestObsBox.IsChecked == true)
            {
                await AddResultAsync("OBS-Verbindung und Szenen", async () =>
                {
                    if (!_obsClient.IsConnected) throw new InvalidOperationException("OBS ist nicht verbunden.");
                    var scenes = await _obsClient.GetSceneListAsync(token);
                    var transitions = await _obsClient.GetSceneTransitionListAsync(token);
                    if (scenes.Count == 0) throw new InvalidOperationException("Keine OBS-Szenen gefunden.");
                    ShortStreamTestResultsList.Items.Add($"  {scenes.Count} Szenen · {transitions.Count} Übergänge");
                });
            }

            if (ShortTestSpotifyBox.IsChecked == true)
            {
                await AddResultAsync("Spotify", async () =>
                {
                    if (!_spotifyModule.GetSnapshot().Authenticated) throw new InvalidOperationException("Spotify ist nicht verbunden.");
                    await RefreshSpotifyInspectorAsync();
                });
            }

            if (ShortTestStreamerBotBox.IsChecked == true)
            {
                await AddResultAsync("Streamer.bot", () =>
                {
                    if (_streamerBotSocket is null || _streamerBotSocket.State != System.Net.WebSockets.WebSocketState.Open)
                        throw new InvalidOperationException("Streamer.bot ist nicht verbunden.");
                    return Task.CompletedTask;
                });
            }

            if (ShortTestOverlayBox.IsChecked == true)
            {
                await AddResultAsync("Overlay", () =>
                {
                    var path = _settings.General.OverlayManifestPath;
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                        throw new InvalidOperationException("overlay.json wurde nicht gefunden.");
                    return Task.CompletedTask;
                });
            }

            if (ShortTestAlertBox.IsChecked == true)
            {
                await AddResultAsync("Suite-Alert", async () => await TestAlertInObsAsync());
            }

            if (ShortTestAutomationBox.IsChecked == true)
            {
                await AddResultAsync("Automatisierungsregel", async () =>
                {
                    if (!_obsClient.IsConnected) throw new InvalidOperationException("OBS ist nicht verbunden.");
                    var rule = ReadTimedAutomationEditor(TimedAutomationRulesList.SelectedItem as TimedAutomationRuleSettings);
                    await ExecuteTimedAutomationRuleAsync(rule, token);
                });
            }

            ShortStreamTestStatusText.Text = "Kurztest abgeschlossen. Der Stream wurde nicht gestartet.";
        }
        catch (OperationCanceledException)
        {
            ShortStreamTestStatusText.Text = "Kurztest abgebrochen.";
        }
        finally
        {
            StartShortStreamTestButton.IsEnabled = true;
        }
    }

    private async Task EvaluateTimedAutomationRulesAsync()
    {
        if (_timedAutomationEvaluationRunning || !_obsClient.IsConnected) return;
        _timedAutomationEvaluationRunning = true;
        try
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var rule in _settings.Workflow.TimedAutomations.Where(x => x.Enabled).OrderByDescending(x => x.Priority).ThenBy(x => x.Name).ToList())
            {
                if (rule.OncePerStream && _executedTimedAutomationRuleIds.Contains(rule.Id)) continue;
                bool due = false;
                if (string.Equals(rule.TriggerType, "StreamElapsed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(rule.TriggerType, "StreamStarted", StringComparison.OrdinalIgnoreCase))
                    due = _streamSessionStartedAt.HasValue && now - _streamSessionStartedAt.Value >= TimeSpan.FromSeconds(string.Equals(rule.TriggerType, "StreamStarted", StringComparison.OrdinalIgnoreCase) ? 0 : rule.DelaySeconds);
                else if (string.Equals(rule.TriggerType, "SceneElapsed", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(rule.TriggerType, "SceneActivated", StringComparison.OrdinalIgnoreCase))
                    due = _automationSceneActivatedAt.HasValue && string.Equals(_automationCurrentScene, rule.TriggerScene, StringComparison.OrdinalIgnoreCase) && now - _automationSceneActivatedAt.Value >= TimeSpan.FromSeconds(string.Equals(rule.TriggerType, "SceneActivated", StringComparison.OrdinalIgnoreCase) ? 0 : rule.DelaySeconds);
                else if (rule.TriggerType is "DailySchedule" or "WeeklySchedule" or "OneTimeSchedule")
                {
                    var localNow = DateTime.Now;
                    due = IsScheduledAutomationDue(rule, localNow);
                }
                if (!due) continue;
                await StartTimedAutomationRuleAsync(rule, simulate: false);
                if (rule.OncePerStream) _executedTimedAutomationRuleIds.Add(rule.Id);
            }
        }
        finally { _timedAutomationEvaluationRunning = false; }
    }


    private static bool IsScheduledAutomationDue(TimedAutomationRuleSettings rule, DateTime localNow)
    {
        if (!TimeOnly.TryParse(rule.ScheduleTime, out var scheduledTime)) return false;
        var today = DateOnly.FromDateTime(localNow);
        if (DateOnly.TryParse(rule.ActiveFromDate, out var activeFrom) && today < activeFrom) return false;
        if (DateOnly.TryParse(rule.ActiveUntilDate, out var activeUntil) && today > activeUntil) return false;
        if (IsAutomationDateExcluded(rule, today) || IsAutomationDateInBlackout(rule, today)) return false;
        var scheduledDateTime = today.ToDateTime(scheduledTime);
        if (localNow < scheduledDateTime) return false;
        var missedBy = localNow - scheduledDateTime;
        if (string.Equals(rule.MissedRunBehavior, "Skip", StringComparison.OrdinalIgnoreCase) && missedBy > TimeSpan.FromMinutes(1)) return false;
        if (string.Equals(rule.MissedRunBehavior, "WithinGrace", StringComparison.OrdinalIgnoreCase) && missedBy > TimeSpan.FromMinutes(Math.Clamp(rule.CatchUpGraceMinutes, 0, 1440))) return false;
        if (string.Equals(rule.LastScheduledRunDate, localNow.ToString("yyyy-MM-dd"), StringComparison.Ordinal)) return false;
        if (string.Equals(rule.TriggerType, "WeeklySchedule", StringComparison.OrdinalIgnoreCase))
        {
            var days = (rule.ScheduleDays ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!days.Any(x => string.Equals(x, localNow.DayOfWeek.ToString(), StringComparison.OrdinalIgnoreCase))) return false;
        }
        if (string.Equals(rule.TriggerType, "OneTimeSchedule", StringComparison.OrdinalIgnoreCase))
            return DateOnly.TryParse(rule.ScheduleDate, out var scheduledDate) && today == scheduledDate;
        return true;
    }


    private static bool IsAutomationDateExcluded(TimedAutomationRuleSettings rule, DateOnly day)
    {
        return (rule.ExcludedDates ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(value => DateOnly.TryParse(value, out var excluded) && excluded == day);
    }

    private static bool IsAutomationDateInBlackout(TimedAutomationRuleSettings rule, DateOnly day)
    {
        foreach (var entry in (rule.BlackoutRanges ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bounds = entry.Split("..", StringSplitOptions.TrimEntries);
            if (bounds.Length != 2 || !DateOnly.TryParse(bounds[0], out var start) || !DateOnly.TryParse(bounds[1], out var end)) continue;
            if (start <= day && day <= end) return true;
        }
        return false;
    }

    private static string DescribeNextScheduledRun(TimedAutomationRuleSettings rule)
    {
        if (rule.TriggerType is not ("DailySchedule" or "WeeklySchedule" or "OneTimeSchedule")) return "nicht zeitplanbasiert";
        if (!TimeOnly.TryParse(rule.ScheduleTime, out var time)) return "ungültige Uhrzeit";
        var now = DateTime.Now;
        for (var offset = 0; offset <= 370; offset++)
        {
            var day = DateOnly.FromDateTime(now.Date.AddDays(offset));
            if (DateOnly.TryParse(rule.ActiveFromDate, out var from) && day < from) continue;
            if (DateOnly.TryParse(rule.ActiveUntilDate, out var until) && day > until) break;
            if (IsAutomationDateExcluded(rule, day) || IsAutomationDateInBlackout(rule, day)) continue;
            if (rule.TriggerType == "WeeklySchedule")
            {
                var names = (rule.ScheduleDays ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (!names.Any(x => string.Equals(x, day.DayOfWeek.ToString(), StringComparison.OrdinalIgnoreCase))) continue;
            }
            if (rule.TriggerType == "OneTimeSchedule" && (!DateOnly.TryParse(rule.ScheduleDate, out var once) || day != once)) continue;
            var candidate = day.ToDateTime(time);
            if (candidate <= now) continue;
            return candidate.ToString("dd.MM.yyyy HH:mm");
        }
        return "kein Termin im gültigen Zeitraum";
    }

    private async Task StartTimedAutomationRuleAsync(TimedAutomationRuleSettings rule, bool simulate)
    {
        if (rule.StartWorkflowGroup && !string.IsNullOrWhiteSpace(rule.WorkflowGroup))
        {
            await ExecuteTimedAutomationWorkflowAsync(rule, simulate);
            return;
        }

        if (!string.IsNullOrWhiteSpace(rule.DependencyRuleId))
        {
            var dependency = _settings.Workflow.TimedAutomations.FirstOrDefault(x => string.Equals(x.Id, rule.DependencyRuleId, StringComparison.OrdinalIgnoreCase));
            if (dependency is null || !string.Equals(dependency.LastRunStatus, rule.DependencyRequiredStatus, StringComparison.OrdinalIgnoreCase))
            {
                rule.SkippedRuns++;
                rule.LastRunAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
                rule.LastRunStatus = "Abhängigkeit nicht erfüllt";
                AddTimedAutomationDiagnostic($"Übersprungen: '{rule.Name}' – erforderliche Vorgängerregel war nicht erfolgreich.");
                await _settingsStore.SaveAsync(_settings);
                return;
            }
        }

        CancellationTokenSource? previous = null;
        lock (_timedAutomationRunSync)
        {
            if (_activeTimedAutomationRuns.TryGetValue(rule.Id, out previous))
            {
                if (string.Equals(rule.ExecutionMode, "SkipIfRunning", StringComparison.OrdinalIgnoreCase))
                {
                    rule.SkippedRuns++;
                    rule.LastRunAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
                    rule.LastRunStatus = "Übersprungen";
                    AddTimedAutomationDiagnostic($"Übersprungen: '{rule.Name}' läuft bereits.");
                    _ = _settingsStore.SaveAsync(_settings);
                    return;
                }
                if (string.Equals(rule.ExecutionMode, "Restart", StringComparison.OrdinalIgnoreCase)) previous.Cancel();
            }
        }

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(rule.TimeoutSeconds, 1, 86400)));
        lock (_timedAutomationRunSync)
        {
            if (!string.Equals(rule.ExecutionMode, "Parallel", StringComparison.OrdinalIgnoreCase))
                _activeTimedAutomationRuns[rule.Id] = timeoutCts;
            UpdateTimedAutomationRuntimeStatus();
        }

        Exception? finalError = null;
        var maxAttempts = Math.Clamp(rule.RetryCount, 0, 20) + 1;
        try
        {
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    if (attempt > 1) AddTimedAutomationDiagnostic($"Wiederholungsversuch {attempt}/{maxAttempts}: '{rule.Name}'.");
                    await ExecuteTimedAutomationRuleAsync(rule, timeoutCts.Token, simulate: simulate);
                    finalError = null;
                    break;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    finalError = ex;
                    if (attempt >= maxAttempts) break;
                    AddTimedAutomationDiagnostic($"Fehler bei '{rule.Name}': {ex.Message} – neuer Versuch in {rule.RetryDelaySeconds} Sekunden.");
                    if (rule.RetryDelaySeconds > 0)
                        await Task.Delay(TimeSpan.FromSeconds(rule.RetryDelaySeconds), timeoutCts.Token);
                }
            }

            if (finalError is not null) throw finalError;
            if (!simulate)
            {
                rule.SuccessfulRuns++;
                rule.LastRunStatus = "Erfolgreich";
                rule.LastRunAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
                if (rule.TriggerType is "DailySchedule" or "WeeklySchedule" or "OneTimeSchedule") rule.LastScheduledRunDate = DateTime.Now.ToString("yyyy-MM-dd");
                await _settingsStore.SaveAsync(_settings);
            }
        }
        catch (OperationCanceledException)
        {
            rule.FailedRuns++;
            rule.LastRunStatus = "Abgebrochen/Timeout";
            rule.LastRunAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
            AddTimedAutomationDiagnostic($"Abgebrochen/Timeout: '{rule.Name}'.");
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception ex)
        {
            rule.FailedRuns++;
            rule.LastRunStatus = "Fehler";
            rule.LastRunAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
            AddTimedAutomationDiagnostic($"Endgültig fehlgeschlagen: '{rule.Name}' – {ex.Message}");
            await _settingsStore.SaveAsync(_settings);

            if (!string.IsNullOrWhiteSpace(rule.FailureRuleId))
            {
                var fallback = _settings.Workflow.TimedAutomations.FirstOrDefault(x => string.Equals(x.Id, rule.FailureRuleId, StringComparison.OrdinalIgnoreCase));
                if (fallback is not null)
                {
                    AddTimedAutomationDiagnostic($"Ersatzregel: '{rule.Name}' → '{fallback.Name}'.");
                    await StartTimedAutomationRuleAsync(fallback, simulate);
                }
            }
        }
        finally
        {
            lock (_timedAutomationRunSync)
            {
                if (_activeTimedAutomationRuns.TryGetValue(rule.Id, out var current) && ReferenceEquals(current, timeoutCts))
                    _activeTimedAutomationRuns.Remove(rule.Id);
                UpdateTimedAutomationRuntimeStatus();
            }
        }
    }

    private async Task ExecuteTimedAutomationWorkflowAsync(TimedAutomationRuleSettings starter, bool simulate)
    {
        var steps = _settings.Workflow.TimedAutomations
            .Where(x => x.Enabled && string.Equals(x.WorkflowGroup, starter.WorkflowGroup, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.WorkflowOrder)
            .ThenByDescending(x => x.Priority)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (steps.Count == 0)
        {
            AddTimedAutomationDiagnostic($"Ablaufgruppe '{starter.WorkflowGroup}' enthält keine aktiven Schritte.");
            return;
        }

        var runId = Guid.NewGuid().ToString("N")[..8];
        var completed = new List<TimedAutomationRuleSettings>();
        AddTimedAutomationDiagnostic($"Workflow {runId} gestartet: '{starter.WorkflowGroup}' mit {steps.Count} Schritt(en).");
        foreach (var step in steps)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(step.TimeoutSeconds, 1, 86400)));
                Exception? lastError = null;
                var attempts = Math.Clamp(step.RetryCount, 0, 20) + 1;
                for (var attempt = 1; attempt <= attempts; attempt++)
                {
                    try
                    {
                        await ExecuteTimedAutomationRuleAsync(step, timeout.Token, simulate: simulate);
                        lastError = null;
                        break;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        lastError = ex;
                        if (attempt < attempts && step.RetryDelaySeconds > 0)
                            await Task.Delay(TimeSpan.FromSeconds(step.RetryDelaySeconds), timeout.Token);
                    }
                }
                if (lastError is not null) throw lastError;
                completed.Add(step);
                AddTimedAutomationDiagnostic($"Workflow {runId}: Schritt '{step.Name}' abgeschlossen.");
            }
            catch (Exception ex)
            {
                AddTimedAutomationDiagnostic($"Workflow {runId}: Schritt '{step.Name}' fehlgeschlagen – {ex.Message}");
                if (string.Equals(starter.WorkflowFailureMode, "Continue", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(starter.WorkflowFailureMode, "Rollback", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var done in completed.AsEnumerable().Reverse())
                    {
                        var rollback = _settings.Workflow.TimedAutomations.FirstOrDefault(x => string.Equals(x.Id, done.RollbackRuleId, StringComparison.OrdinalIgnoreCase));
                        if (rollback is null) continue;
                        try
                        {
                            AddTimedAutomationDiagnostic($"Workflow {runId}: Rückabwicklung '{done.Name}' → '{rollback.Name}'.");
                            await ExecuteTimedAutomationRuleAsync(rollback, CancellationToken.None, simulate: simulate);
                        }
                        catch (Exception rollbackError)
                        {
                            AddTimedAutomationDiagnostic($"Workflow {runId}: Rückabwicklung '{rollback.Name}' fehlgeschlagen – {rollbackError.Message}");
                        }
                    }
                }
                starter.FailedRuns++;
                starter.LastRunAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
                starter.LastRunStatus = "Workflow fehlgeschlagen";
                await _settingsStore.SaveAsync(_settings);
                return;
            }
        }
        if (!simulate)
        {
            starter.SuccessfulRuns++;
            starter.LastRunAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
            starter.LastRunStatus = "Workflow erfolgreich";
            await _settingsStore.SaveAsync(_settings);
        }
        AddTimedAutomationDiagnostic($"Workflow {runId} abgeschlossen: '{starter.WorkflowGroup}'.");
    }

    private void StopAllTimedAutomations()
    {
        List<CancellationTokenSource> running;
        lock (_timedAutomationRunSync) running = _activeTimedAutomationRuns.Values.Distinct().ToList();
        foreach (var cts in running) cts.Cancel();
        AddTimedAutomationDiagnostic($"Abbruch angefordert: {running.Count} laufende Automation(en).");
    }

    private void UpdateTimedAutomationRuntimeStatus()
    {
        if (TimedAutomationRuntimeStatusText is null) return;
        var count = _activeTimedAutomationRuns.Count;
        Dispatcher.InvokeAsync(() => TimedAutomationRuntimeStatusText.Text = count == 0 ? "Keine laufende Automation." : $"{count} Automation(en) laufen.");
    }

    private async Task ExecuteTimedAutomationRuleAsync(TimedAutomationRuleSettings rule, CancellationToken cancellationToken, HashSet<string>? chain = null, bool simulate = false)
    {
        chain ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!chain.Add(rule.Id))
        {
            AddTimedAutomationDiagnostic($"Kette abgebrochen: Schleife bei '{rule.Name}' erkannt.");
            return;
        }

        if (!await EvaluateTimedAutomationConditionAsync(rule, cancellationToken))
        {
            AddTimedAutomationDiagnostic($"Übersprungen: '{rule.Name}' – Bedingung nicht erfüllt.");
            return;
        }

        Exception? executionError = null;
        try
        {
            if (simulate)
            {
                AddTimedAutomationDiagnostic($"Simulation: '{rule.Name}' → {DescribeTimedAutomationAction(rule)}");
            }
            else
            {
                await ExecuteTimedAutomationActionAsync(rule, cancellationToken);
                AddTimedAutomationDiagnostic($"Ausgeführt: '{rule.Name}'.");
            }
            _appLogger.Write(AppLogLevel.Information, "Automation", $"Regel ausgeführt: {rule.Name}");
        }
        catch (Exception ex)
        {
            executionError = ex;
            AddTimedAutomationDiagnostic($"Fehler: '{rule.Name}' – {ex.Message}");
            _appLogger.Write(AppLogLevel.Error, "Automation", $"Regel fehlgeschlagen ({rule.Name}): {ex.Message}");
            if (!rule.ContinueChainOnError) throw;
        }

        if (!string.IsNullOrWhiteSpace(rule.NextRuleId) && (executionError is null || rule.ContinueChainOnError))
        {
            var next = _settings.Workflow.TimedAutomations.FirstOrDefault(x => string.Equals(x.Id, rule.NextRuleId, StringComparison.OrdinalIgnoreCase));
            if (next is null)
            {
                AddTimedAutomationDiagnostic($"Kette unvollständig: Folgeregel für '{rule.Name}' wurde nicht gefunden.");
                return;
            }
            if (rule.NextRuleDelaySeconds > 0)
                await Task.Delay(TimeSpan.FromSeconds(rule.NextRuleDelaySeconds), cancellationToken);
            AddTimedAutomationDiagnostic($"Kette: '{rule.Name}' → '{next.Name}'.");
            await ExecuteTimedAutomationRuleAsync(next, cancellationToken, chain, simulate);
        }
    }

    private async Task<bool> EvaluateTimedAutomationConditionAsync(TimedAutomationRuleSettings rule, CancellationToken cancellationToken)
    {
        bool result = rule.ConditionType switch
        {
            "ObsConnected" => _obsClient.IsConnected,
            "StreamerBotConnected" => _streamerBotSocket is not null && _streamerBotSocket.State == System.Net.WebSockets.WebSocketState.Open,
            "StreamActive" => _streamSessionStartedAt.HasValue,
            "CurrentScene" => _obsClient.IsConnected && string.Equals(await _obsClient.GetCurrentProgramSceneAsync(cancellationToken), rule.ConditionValue, StringComparison.OrdinalIgnoreCase),
            _ => true
        };
        return rule.ConditionNegated ? !result : result;
    }

    private async Task ExecuteTimedAutomationActionAsync(TimedAutomationRuleSettings rule, CancellationToken cancellationToken)
    {
        if (string.Equals(rule.ActionType, "SwitchScene", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(rule.TargetScene)) throw new InvalidOperationException("Keine Zielszene gewählt.");
            if (!string.IsNullOrWhiteSpace(rule.TransitionName))
            {
                await _obsClient.SetCurrentSceneTransitionAsync(rule.TransitionName, cancellationToken);
                await _obsClient.SetCurrentSceneTransitionDurationAsync(rule.TransitionDurationMilliseconds, cancellationToken);
            }
            await _obsClient.SetCurrentProgramSceneAsync(rule.TargetScene, cancellationToken);
        }
        else if (string.Equals(rule.ActionType, "SetSourceVisibility", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(rule.ObsScene) || string.IsNullOrWhiteSpace(rule.ObsSource)) throw new InvalidOperationException("Szene und Quelle müssen gewählt sein.");
            await _obsClient.SetSceneItemEnabledAsync(rule.ObsScene, rule.ObsSource, rule.SourceVisible, cancellationToken);
        }
        else if (string.Equals(rule.ActionType, "SetInputMute", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(rule.ObsInput)) throw new InvalidOperationException("Keine OBS-Audioquelle gewählt.");
            await _obsClient.SetInputMuteAsync(rule.ObsInput, rule.InputMuted, cancellationToken);
        }
        else if (string.Equals(rule.ActionType, "StartObsStream", StringComparison.OrdinalIgnoreCase))
        {
            await _obsClient.StartStreamAsync(cancellationToken);
        }
        else if (string.Equals(rule.ActionType, "StopObsStream", StringComparison.OrdinalIgnoreCase))
        {
            await _obsClient.StopStreamAsync(cancellationToken);
        }
        else if (string.Equals(rule.ActionType, "StreamerBotAction", StringComparison.OrdinalIgnoreCase))
        {
            if (_streamerBotSocket is null || _streamerBotSocket.State != System.Net.WebSockets.WebSocketState.Open)
                throw new InvalidOperationException("Streamer.bot ist nicht verbunden.");
            if (string.IsNullOrWhiteSpace(rule.StreamerBotActionId) && string.IsNullOrWhiteSpace(rule.StreamerBotActionName))
                throw new InvalidOperationException("Keine Streamer.bot-Aktion gewählt.");
            using var response = await SendStreamerBotRequestAsync(new
            {
                request = "DoAction",
                action = new { id = rule.StreamerBotActionId, name = rule.StreamerBotActionName },
                args = new { source = "Creator Control Suite", automationRule = rule.Name }
            });
            var status = response.RootElement.TryGetProperty("status", out var statusNode) ? statusNode.GetString() : null;
            if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Streamer.bot hat die Aktion nicht bestätigt.");
        }

        if (!string.Equals(rule.SpotifyAction, "None", StringComparison.OrdinalIgnoreCase))
        {
            CancellationTokenSource spotifyRunCts;
            lock (_spotifyAutomationSync)
            {
                var incomingGroup = string.IsNullOrWhiteSpace(rule.SpotifyAutomationGroup) ? "Standard" : rule.SpotifyAutomationGroup.Trim();
                if (_spotifyAutomationCts is not null)
                {
                    var sameGroup = string.Equals(incomingGroup, _activeSpotifyAutomationGroup, StringComparison.OrdinalIgnoreCase);
                    var blockedByExclusiveGroup = !sameGroup && (_activeSpotifyAutomationExclusive || rule.SpotifyExclusiveGroup);
                    if (rule.SpotifyPriority < _activeSpotifyAutomationPriority ||
                        (blockedByExclusiveGroup && rule.SpotifyPriority <= _activeSpotifyAutomationPriority))
                    {
                        var reason = blockedByExclusiveGroup
                            ? $"Gruppe '{incomingGroup}' ist durch die aktive Gruppe '{_activeSpotifyAutomationGroup}' gesperrt"
                            : $"Priorität {rule.SpotifyPriority} ist niedriger als aktive Priorität {_activeSpotifyAutomationPriority}";
                        AddTimedAutomationDiagnostic($"Spotify-Aktion '{rule.Name}' übersprungen: {reason}.");
                        return;
                    }
                }

                _spotifyAutomationCts?.Cancel();
                _spotifyAutomationCts?.Dispose();
                _spotifyAutomationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _activeSpotifyAutomationPriority = rule.SpotifyPriority;
                _activeSpotifyAutomationGroup = string.IsNullOrWhiteSpace(rule.SpotifyAutomationGroup) ? "Standard" : rule.SpotifyAutomationGroup.Trim();
                _activeSpotifyAutomationExclusive = rule.SpotifyExclusiveGroup;
                spotifyRunCts = _spotifyAutomationCts;
            }

            try
            {
                var spotifyToken = spotifyRunCts.Token;
                if (rule.SpotifyActionDelaySeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(rule.SpotifyActionDelaySeconds), spotifyToken);

                var spotifyGroup = string.IsNullOrWhiteSpace(rule.SpotifyAutomationGroup) ? "Standard" : rule.SpotifyAutomationGroup.Trim();
                if (rule.SpotifySavePreviousState && !string.Equals(rule.SpotifyAction, "RestorePrevious", StringComparison.OrdinalIgnoreCase))
                    await SaveSpotifyAutomationStateAsync(spotifyGroup, spotifyToken);

                if (string.Equals(rule.SpotifyAction, "RestorePrevious", StringComparison.OrdinalIgnoreCase))
                    await RestoreSpotifyAutomationStateAsync(spotifyGroup, rule, spotifyToken);
                else if (string.Equals(rule.SpotifyAction, "Pause", StringComparison.OrdinalIgnoreCase)) await _spotifyModule.PauseAsync(spotifyToken);
                else if (string.Equals(rule.SpotifyAction, "Resume", StringComparison.OrdinalIgnoreCase))
                {
                    await _spotifyModule.ResumeAsync(spotifyToken);
                    await ApplySpotifyAutomationVolumeAsync(rule, spotifyToken);
                }
                else if (string.Equals(rule.SpotifyAction, "SetVolume", StringComparison.OrdinalIgnoreCase))
                    await ApplySpotifyAutomationVolumeAsync(rule, spotifyToken);
                else if (string.Equals(rule.SpotifyAction, "StartPlaylist", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(rule.SpotifyPlaylistUri))
                        throw new InvalidOperationException("Für die Spotify-Automation wurde keine Playlist-URI eingetragen.");
                    if (rule.SpotifyFadeSeconds > 0) await _spotifyModule.SetVolumeAsync(0, spotifyToken);
                    await _spotifyModule.SetShuffleAsync(rule.SpotifyPlaylistShuffle, spotifyToken);
                    await _spotifyModule.StartPlaylistAsync(rule.SpotifyPlaylistUri, applyConfiguredStartVolume: false, spotifyToken);
                    await ApplySpotifyAutomationVolumeAsync(rule, spotifyToken);
                }

                if (rule.SpotifyAutoRestorePreviousState &&
                    !string.Equals(rule.SpotifyAction, "RestorePrevious", StringComparison.OrdinalIgnoreCase))
                {
                    var hasSavedState = false;
                    lock (_spotifyAutomationSync) hasSavedState = _spotifyAutomationSavedStates.ContainsKey(spotifyGroup);
                    if (!hasSavedState)
                        AddTimedAutomationDiagnostic($"Spotify-Gruppe '{spotifyGroup}': Automatische Rückkehr übersprungen, weil kein Zustand gesichert wurde.");
                    else
                    {
                        var restoreDelay = Math.Clamp(rule.SpotifyAutoRestoreDelaySeconds, 1, 86400);
                        var expectedScene = _automationCurrentScene;
                        await _spotifyModule.RefreshPlaybackAsync(spotifyToken);
                        var expectedPlayback = _spotifyModule.GetSnapshot().Playback;
                        var expectedTrackUri = expectedPlayback.Track?.Uri ?? "";
                        var expectedContextUri = expectedPlayback.ContextUri ?? "";
                        AddTimedAutomationDiagnostic($"Spotify-Gruppe '{spotifyGroup}': Automatische Rückkehr in {restoreDelay} Sekunden vorgemerkt.");
                        await Task.Delay(TimeSpan.FromSeconds(restoreDelay), spotifyToken);

                        if (rule.SpotifyAutoRestoreRequireSameScene && !string.Equals(expectedScene, _automationCurrentScene, StringComparison.OrdinalIgnoreCase))
                        {
                            AddTimedAutomationDiagnostic($"Spotify-Gruppe '{spotifyGroup}': Automatische Rückkehr verworfen, weil die OBS-Szene von '{expectedScene}' zu '{_automationCurrentScene}' gewechselt wurde.");
                            return;
                        }
                        if (rule.SpotifyAutoRestoreRequireSameGroup)
                        {
                            lock (_spotifyAutomationSync)
                            {
                                if (!ReferenceEquals(_spotifyAutomationCts, spotifyRunCts) || !string.Equals(_activeSpotifyAutomationGroup, spotifyGroup, StringComparison.OrdinalIgnoreCase))
                                {
                                    AddTimedAutomationDiagnostic($"Spotify-Gruppe '{spotifyGroup}': Automatische Rückkehr verworfen, weil die Gruppe nicht mehr aktiv ist.");
                                    return;
                                }
                            }
                        }
                        if (rule.SpotifyAutoRestoreRequireUnchangedPlayback)
                        {
                            await _spotifyModule.RefreshPlaybackAsync(spotifyToken);
                            var currentPlayback = _spotifyModule.GetSnapshot().Playback;
                            var currentTrackUri = currentPlayback.Track?.Uri ?? "";
                            var currentContextUri = currentPlayback.ContextUri ?? "";
                            if (!string.Equals(expectedTrackUri, currentTrackUri, StringComparison.OrdinalIgnoreCase) ||
                                !string.Equals(expectedContextUri, currentContextUri, StringComparison.OrdinalIgnoreCase))
                            {
                                AddTimedAutomationDiagnostic($"Spotify-Gruppe '{spotifyGroup}': Automatische Rückkehr verworfen, weil die Wiedergabe zwischenzeitlich geändert wurde.");
                                return;
                            }
                        }
                        await RestoreSpotifyAutomationStateAsync(spotifyGroup, rule, spotifyToken);
                    }
                }
            }
            finally
            {
                lock (_spotifyAutomationSync)
                {
                    if (ReferenceEquals(_spotifyAutomationCts, spotifyRunCts))
                    {
                        _spotifyAutomationCts.Dispose();
                        _spotifyAutomationCts = null;
                        _activeSpotifyAutomationPriority = int.MinValue;
                        _activeSpotifyAutomationGroup = "";
                        _activeSpotifyAutomationExclusive = false;
                    }
                }
            }
        }
    }


    private string GetSpotifyAutomationEditorGroup()
    {
        return string.IsNullOrWhiteSpace(TimedAutomationSpotifyGroupBox.Text)
            ? "Standard"
            : TimedAutomationSpotifyGroupBox.Text.Trim();
    }

    private void RefreshSpotifySavedStateStatus()
    {
        var group = GetSpotifyAutomationEditorGroup();
        SpotifyAutomationSavedState? state;
        lock (_spotifyAutomationSync) _spotifyAutomationSavedStates.TryGetValue(group, out state);

        if (state is null)
        {
            TimedAutomationSpotifySavedStateText.Text = $"Für die Gruppe '{group}' ist kein Zustand gespeichert.";
            return;
        }

        var title = state.Track?.Name ?? "Unbekannter Titel";
        var artist = state.Track?.Artist ?? "Unbekannter Interpret";
        var position = TimeSpan.FromMilliseconds(Math.Max(0, state.ProgressMs));
        var playbackState = state.WasPlaying ? "lief" : "war pausiert";
        var age = DateTimeOffset.UtcNow - state.SavedAtUtc;
        var expiry = IsSpotifySavedStateExpired(state) ? " · ABGELAUFEN" : "";
        TimedAutomationSpotifySavedStateText.Text =
            $"Gruppe '{group}': {title} – {artist} bei {position:mm\\:ss}, Lautstärke {state.VolumePercent} %, " +
            $"Shuffle {(state.ShuffleEnabled ? "an" : "aus")}, Wiederholung {state.RepeatMode}, {playbackState}. " +
            $"Gesichert vor {FormatSpotifySavedStateAge(age)}{expiry}.";
    }

    private void RefreshSpotifySavedStatesOverview()
    {
        List<SpotifySavedStateOverviewItem> items;
        lock (_spotifyAutomationSync)
        {
            items = _spotifyAutomationSavedStates
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry =>
                {
                    var state = entry.Value;
                    var title = state.Track?.Name ?? "Unbekannter Titel";
                    var artist = state.Track?.Artist ?? "Unbekannter Interpret";
                    var position = TimeSpan.FromMilliseconds(Math.Max(0, state.ProgressMs));
                    var playbackState = state.WasPlaying ? "lief" : "pausiert";
                    var age = DateTimeOffset.UtcNow - state.SavedAtUtc;
                    var expired = IsSpotifySavedStateExpired(state);
                    var prefix = expired ? "[ABGELAUFEN] " : "";
                    var summary = $"{prefix}{entry.Key} · {title} – {artist} · {position:mm\\:ss} · {state.VolumePercent} % · {playbackState} · vor {FormatSpotifySavedStateAge(age)}";
                    return new SpotifySavedStateOverviewItem(entry.Key, summary, expired);
                })
                .ToList();
        }

        SpotifySavedStatesOverviewList.ItemsSource = items;
        var expiredCount = items.Count(item => item.IsExpired);
        SpotifySavedStatesOverviewStatusText.Text = items.Count == 0
            ? "Es ist aktuell kein Spotify-Zustand gespeichert."
            : expiredCount == 0
                ? $"{items.Count} gespeicherte Spotify-Zustände gefunden."
                : $"{items.Count} gespeicherte Spotify-Zustände gefunden · {expiredCount} abgelaufen.";
    }

    private void UpdateSpotifySavedStatesOverviewSelection()
    {
        if (SpotifySavedStatesOverviewList.SelectedItem is not SpotifySavedStateOverviewItem item) return;
        SpotifySavedStatesOverviewStatusText.Text = item.IsExpired
            ? $"Ausgewählt: Gruppe '{item.Group}' · Zustand ist abgelaufen, kann aber weiterhin manuell wiederhergestellt werden."
            : $"Ausgewählt: Gruppe '{item.Group}'.";
    }

    private async Task RestoreSelectedSpotifySavedStateAsync()
    {
        if (SpotifySavedStatesOverviewList.SelectedItem is not SpotifySavedStateOverviewItem item)
        {
            SpotifySavedStatesOverviewStatusText.Text = "Bitte zuerst einen gespeicherten Zustand auswählen.";
            return;
        }

        TimedAutomationSpotifyGroupBox.Text = item.Group;
        await RestoreSpotifySavedStateNowAsync();
        RefreshSpotifySavedStatesOverview();
    }

    private void DiscardSelectedSpotifySavedState()
    {
        if (SpotifySavedStatesOverviewList.SelectedItem is not SpotifySavedStateOverviewItem item)
        {
            SpotifySavedStatesOverviewStatusText.Text = "Bitte zuerst einen gespeicherten Zustand auswählen.";
            return;
        }

        var removed = false;
        lock (_spotifyAutomationSync) removed = _spotifyAutomationSavedStates.Remove(item.Group);
        AddTimedAutomationDiagnostic(removed
            ? $"Spotify-Gruppe '{item.Group}': Gespeicherter Zustand wurde über die Übersicht verworfen."
            : $"Spotify-Gruppe '{item.Group}': Zustand war bereits nicht mehr vorhanden.");
        if (removed)
        {
            _spotifySavedStateDiscardCount++;
            AddSpotifySavedStateHistory($"{item.Group}: Zustand über Übersicht verworfen");
        }
        RefreshSpotifySavedStateStatus();
        RefreshSpotifySavedStatesOverview();
    }

    private void DiscardAllSpotifySavedStates()
    {
        int count;
        lock (_spotifyAutomationSync)
        {
            count = _spotifyAutomationSavedStates.Count;
            _spotifyAutomationSavedStates.Clear();
        }

        AddTimedAutomationDiagnostic(count == 0
            ? "Spotify: Es waren keine gespeicherten Zustände vorhanden."
            : $"Spotify: {count} gespeicherte Zustände wurden verworfen.");
        if (count > 0)
        {
            _spotifySavedStateDiscardCount += count;
            AddSpotifySavedStateHistory($"Alle Zustände verworfen ({count})");
        }
        RefreshSpotifySavedStateStatus();
        RefreshSpotifySavedStatesOverview();
    }


    private void LoadSpotifySavedStateHistoryPersistence()
    {
        _loadingSpotifySavedStateHistoryPersistence = true;
        try
        {
            if (!File.Exists(SpotifySavedStateHistoryPersistencePath)) return;
            var state = JsonSerializer.Deserialize<SpotifySavedStateHistoryPersistence>(
                File.ReadAllText(SpotifySavedStateHistoryPersistencePath));
            if (state is null || state.FormatVersion != 1 || state.Entries is null) return;

            _spotifySavedStateHistory.Clear();
            _spotifySavedStateHistoryFavorites.Clear();
            _spotifySavedStateHistoryNotes.Clear();
            foreach (var entry in state.Entries.Where(entry => !string.IsNullOrWhiteSpace(entry)).Take(100))
                _spotifySavedStateHistory.Add(entry);
            foreach (var entry in state.FavoriteEntries ?? [])
                if (_spotifySavedStateHistory.Contains(entry)) _spotifySavedStateHistoryFavorites.Add(entry);
            foreach (var pair in state.Notes ?? new Dictionary<string, string>())
                if (_spotifySavedStateHistory.Contains(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    _spotifySavedStateHistoryNotes[pair.Key] = pair.Value;

            _spotifySavedStateSaveCount = Math.Max(0, state.SavedCount);
            _spotifySavedStateRestoreCount = Math.Max(0, state.RestoredCount);
            _spotifySavedStateDiscardCount = Math.Max(0, state.DiscardedCount);
            _spotifySavedStateCleanupCount = Math.Max(0, state.CleanupCount);
            SpotifySavedStateHistorySearchBox.Text = state.SearchText ?? "";
            SpotifySavedStateHistoryActionFilterBox.SelectedIndex = Math.Clamp(state.ActionFilterIndex, 0, Math.Max(0, SpotifySavedStateHistoryActionFilterBox.Items.Count - 1));
            SpotifySavedStateHistorySortBox.SelectedIndex = Math.Clamp(state.SortIndex, 0, Math.Max(0, SpotifySavedStateHistorySortBox.Items.Count - 1));
            SpotifySavedStateHistoryFavoritesOnlyBox.IsChecked = state.FavoritesOnly;
            AddTimedAutomationDiagnostic($"Spotify: {_spotifySavedStateHistory.Count} gespeicherte Verlaufseinträge aus der lokalen Sitzungshistorie geladen.");
        }
        catch (Exception exception)
        {
            AddTimedAutomationDiagnostic("Spotify: Lokale Sitzungshistorie konnte nicht geladen werden: " + exception.Message);
        }
        finally
        {
            _loadingSpotifySavedStateHistoryPersistence = false;
        }
    }

    private void SaveSpotifySavedStateHistoryPersistence()
    {
        if (_loadingSpotifySavedStateHistoryPersistence) return;
        try
        {
            var directory = Path.GetDirectoryName(SpotifySavedStateHistoryPersistencePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var state = new SpotifySavedStateHistoryPersistence(
                1,
                _spotifySavedStateSaveCount,
                _spotifySavedStateRestoreCount,
                _spotifySavedStateDiscardCount,
                _spotifySavedStateCleanupCount,
                _spotifySavedStateHistory.ToList(),
                _spotifySavedStateHistoryFavorites.ToList(),
                new Dictionary<string, string>(_spotifySavedStateHistoryNotes),
                SpotifySavedStateHistorySearchBox?.Text ?? "",
                SpotifySavedStateHistoryActionFilterBox?.SelectedIndex ?? 0,
                SpotifySavedStateHistorySortBox?.SelectedIndex ?? 0,
                SpotifySavedStateHistoryFavoritesOnlyBox?.IsChecked == true);
            if (File.Exists(SpotifySavedStateHistoryPersistencePath) &&
                DateTimeOffset.UtcNow - _lastSpotifySavedStateHistoryBackupUtc >= TimeSpan.FromMinutes(30))
            {
                CreateSpotifySavedStateHistoryBackup(manual: false);
            }
            var temporaryPath = SpotifySavedStateHistoryPersistencePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, SpotifySavedStateHistoryPersistencePath, true);
        }
        catch (Exception exception)
        {
            AddTimedAutomationDiagnostic("Spotify: Lokale Sitzungshistorie konnte nicht gespeichert werden: " + exception.Message);
        }
    }


    private void CreateSpotifySavedStateHistoryBackup(bool manual)
    {
        try
        {
            if (!File.Exists(SpotifySavedStateHistoryPersistencePath))
            {
                if (manual)
                    SpotifySavedStateHistoryStatusText.Text = "Es ist noch keine lokale Verlaufshistorie vorhanden, die gesichert werden kann.";
                return;
            }

            Directory.CreateDirectory(SpotifySavedStateHistoryBackupDirectory);
            var backupPath = Path.Combine(
                SpotifySavedStateHistoryBackupDirectory,
                $"spotify-saved-state-history-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(SpotifySavedStateHistoryPersistencePath, backupPath, overwrite: false);
            _lastSpotifySavedStateHistoryBackupUtc = DateTimeOffset.UtcNow;

            foreach (var oldBackup in new DirectoryInfo(SpotifySavedStateHistoryBackupDirectory)
                         .GetFiles("spotify-saved-state-history-*.json")
                         .OrderByDescending(file => file.CreationTimeUtc)
                         .Skip(10))
            {
                oldBackup.Delete();
            }

            AddTimedAutomationDiagnostic(manual
                ? "Spotify: Manueller Wiederherstellungspunkt für den Zustandsverlauf erstellt."
                : "Spotify: Automatischer Wiederherstellungspunkt für den Zustandsverlauf erstellt.");
            RefreshSpotifySavedStateHistoryBackups();
            if (manual)
                SpotifySavedStateHistoryStatusText.Text = "Wiederherstellungspunkt wurde erstellt. Es werden höchstens 10 Sicherungen aufbewahrt.";
        }
        catch (Exception exception)
        {
            AddTimedAutomationDiagnostic("Spotify: Verlaufssicherung konnte nicht erstellt werden: " + exception.Message);
            if (manual)
                SpotifySavedStateHistoryStatusText.Text = "Sicherung fehlgeschlagen: " + exception.Message;
        }
    }

    private void RefreshSpotifySavedStateHistoryBackups()
    {
        var selectedPath = (SpotifySavedStateHistoryBackupsList?.SelectedItem as SpotifySavedStateHistoryBackupItem)?.FullPath;
        _spotifySavedStateHistoryBackups.Clear();
        if (Directory.Exists(SpotifySavedStateHistoryBackupDirectory))
        {
            foreach (var file in new DirectoryInfo(SpotifySavedStateHistoryBackupDirectory)
                         .GetFiles("spotify-saved-state-history-*.json")
                         .OrderByDescending(file => file.LastWriteTimeUtc))
            {
                _spotifySavedStateHistoryBackups.Add(new SpotifySavedStateHistoryBackupItem(
                    file.FullName,
                    $"{file.LastWriteTime:dd.MM.yyyy HH:mm:ss} · {file.Length / 1024.0:0.0} KB",
                    file.LastWriteTime,
                    file.Length));
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedPath) && SpotifySavedStateHistoryBackupsList is not null)
            SpotifySavedStateHistoryBackupsList.SelectedItem = _spotifySavedStateHistoryBackups.FirstOrDefault(item => item.FullPath == selectedPath);
        UpdateSpotifySavedStateHistoryBackupDetail();
        UpdateSpotifySavedStateHistoryBackupPreview(showStatus: false);
    }

    private void UpdateSpotifySavedStateHistoryBackupDetail()
    {
        if (SpotifySavedStateHistoryBackupDetailText is null) return;
        if (SpotifySavedStateHistoryBackupsList?.SelectedItem is not SpotifySavedStateHistoryBackupItem backup)
        {
            SpotifySavedStateHistoryBackupDetailText.Text = _spotifySavedStateHistoryBackups.Count == 0
                ? "Es sind noch keine Wiederherstellungspunkte vorhanden."
                : $"{_spotifySavedStateHistoryBackups.Count} Sicherungen vorhanden. Bitte eine Sicherung auswählen.";
            return;
        }

        SpotifySavedStateHistoryBackupDetailText.Text =
            $"Ausgewählt: {backup.LastWriteTime:dd.MM.yyyy HH:mm:ss} · {backup.SizeBytes / 1024.0:0.0} KB\n{backup.FullPath}";
    }

    private void UpdateSpotifySavedStateHistoryBackupPreview(bool showStatus)
    {
        if (SpotifySavedStateHistoryBackupPreviewText is null) return;
        if (SpotifySavedStateHistoryBackupsList?.SelectedItem is not SpotifySavedStateHistoryBackupItem backup)
        {
            SpotifySavedStateHistoryBackupPreviewText.Text =
                "Sicherung auswählen, um Inhalt und Unterschiede zum aktuellen Verlauf anzuzeigen.";
            _spotifySavedStateHistoryBackupDifferences.Clear();
            if (showStatus) SpotifySavedStateHistoryStatusText.Text = "Bitte zuerst einen Wiederherstellungspunkt auswählen.";
            return;
        }

        try
        {
            var state = JsonSerializer.Deserialize<SpotifySavedStateHistoryPersistence>(File.ReadAllText(backup.FullPath));
            if (state is null || state.FormatVersion != 1 || state.Entries is null)
                throw new InvalidDataException("Die Sicherungsdatei enthält kein unterstütztes Verlaufsformat.");

            var backupEntries = new HashSet<string>(state.Entries, StringComparer.Ordinal);
            var currentEntries = new HashSet<string>(_spotifySavedStateHistory, StringComparer.Ordinal);
            var addedEntries = backupEntries.Except(currentEntries).OrderBy(entry => entry, StringComparer.CurrentCultureIgnoreCase).ToList();
            var replacedEntries = currentEntries.Except(backupEntries).OrderBy(entry => entry, StringComparer.CurrentCultureIgnoreCase).ToList();
            var unchangedEntries = backupEntries.Intersect(currentEntries).OrderBy(entry => entry, StringComparer.CurrentCultureIgnoreCase).ToList();
            var onlyInBackup = addedEntries.Count;
            var onlyCurrent = replacedEntries.Count;
            var common = unchangedEntries.Count;
            var backupFavorites = state.FavoriteEntries?.Count ?? 0;
            var backupNotes = state.Notes?.Count(pair => !string.IsNullOrWhiteSpace(pair.Value)) ?? 0;
            var currentNotes = _spotifySavedStateHistoryNotes.Count(pair => !string.IsNullOrWhiteSpace(pair.Value));

            _spotifySavedStateHistoryBackupDifferences.Clear();
            foreach (var entry in addedEntries.Take(50))
                _spotifySavedStateHistoryBackupDifferences.Add("+ HINZUKOMMEND: " + entry);
            foreach (var entry in replacedEntries.Take(50))
                _spotifySavedStateHistoryBackupDifferences.Add("− WIRD ERSETZT: " + entry);
            foreach (var entry in unchangedEntries.Take(25))
                _spotifySavedStateHistoryBackupDifferences.Add("= UNVERÄNDERT: " + entry);

            var hiddenDifferenceCount = Math.Max(0, addedEntries.Count - 50) + Math.Max(0, replacedEntries.Count - 50) + Math.Max(0, unchangedEntries.Count - 25);
            if (hiddenDifferenceCount > 0)
                _spotifySavedStateHistoryBackupDifferences.Add($"… {hiddenDifferenceCount} weitere Vergleichseinträge werden aus Übersichtsgründen nicht angezeigt.");
            if (_spotifySavedStateHistoryBackupDifferences.Count == 0)
                _spotifySavedStateHistoryBackupDifferences.Add("Keine Eintragsunterschiede vorhanden.");

            SpotifySavedStateHistoryBackupPreviewText.Text =
                $"Inhalt der Sicherung:\n" +
                $"• {state.Entries.Count} Verlaufseinträge, {backupFavorites} Favoriten, {backupNotes} Notizen\n" +
                $"• Zähler: gespeichert {state.SavedCount}, wiederhergestellt {state.RestoredCount}, verworfen {state.DiscardedCount}, bereinigt {state.CleanupCount}\n" +
                $"• Filter: Suche '{(string.IsNullOrWhiteSpace(state.SearchText) ? "–" : state.SearchText)}', Aktion #{state.ActionFilterIndex}, Sortierung #{state.SortIndex}, nur Favoriten {(state.FavoritesOnly ? "ja" : "nein")}\n\n" +
                $"Vergleich mit dem aktuellen Verlauf:\n" +
                $"• {common} identische Einträge\n" +
                $"• {onlyInBackup} Einträge würden hinzukommen\n" +
                $"• {onlyCurrent} aktuelle Einträge würden ersetzt\n" +
                $"• Favoriten: aktuell {_spotifySavedStateHistoryFavorites.Count}, Sicherung {backupFavorites}\n" +
                $"• Notizen: aktuell {currentNotes}, Sicherung {backupNotes}";

            if (showStatus)
                SpotifySavedStateHistoryStatusText.Text = $"Sicherung vom {backup.LastWriteTime:dd.MM.yyyy HH:mm:ss} wurde erfolgreich analysiert.";
        }
        catch (Exception exception)
        {
            SpotifySavedStateHistoryBackupPreviewText.Text = "Vorschau nicht verfügbar: " + exception.Message;
            _spotifySavedStateHistoryBackupDifferences.Clear();
            _spotifySavedStateHistoryBackupDifferences.Add("Vorschaufehler: " + exception.Message);
            AddTimedAutomationDiagnostic("Spotify: Sicherungsvorschau konnte nicht erstellt werden: " + exception.Message);
            if (showStatus) SpotifySavedStateHistoryStatusText.Text = "Sicherungsvorschau fehlgeschlagen: " + exception.Message;
        }
    }

    private void RestoreSelectedSpotifySavedStateHistoryBackup()
    {
        if (SpotifySavedStateHistoryBackupsList?.SelectedItem is not SpotifySavedStateHistoryBackupItem backup)
        {
            SpotifySavedStateHistoryStatusText.Text = "Bitte zuerst einen Wiederherstellungspunkt auswählen.";
            return;
        }

        var result = MessageBox.Show(
            $"Den Spotify-Zustandsverlauf aus der Sicherung vom {backup.LastWriteTime:dd.MM.yyyy HH:mm:ss} wiederherstellen?\n\nDer aktuelle Verlauf wird vorher automatisch gesichert.",
            "Spotify-Verlauf wiederherstellen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            var state = JsonSerializer.Deserialize<SpotifySavedStateHistoryPersistence>(File.ReadAllText(backup.FullPath));
            if (state is null || state.FormatVersion != 1 || state.Entries is null)
                throw new InvalidDataException("Die Sicherungsdatei enthält kein unterstütztes Verlaufsformat.");

            CreateSpotifySavedStateHistoryBackup(manual: false);
            Directory.CreateDirectory(Path.GetDirectoryName(SpotifySavedStateHistoryPersistencePath)!);
            File.Copy(backup.FullPath, SpotifySavedStateHistoryPersistencePath, overwrite: true);
            LoadSpotifySavedStateHistoryPersistence();
            ApplySpotifySavedStateHistorySort();
            RefreshSpotifySavedStateHistoryFilter();
            RefreshSpotifySavedStateStatistics();
            UpdateSpotifySavedStateHistoryDetail();
            RefreshSpotifySavedStateHistoryBackups();
            AddTimedAutomationDiagnostic($"Spotify: Zustandsverlauf aus Sicherung vom {backup.LastWriteTime:dd.MM.yyyy HH:mm:ss} wiederhergestellt.");
            SpotifySavedStateHistoryStatusText.Text = $"Verlauf aus dem Wiederherstellungspunkt vom {backup.LastWriteTime:dd.MM.yyyy HH:mm:ss} geladen.";
        }
        catch (Exception exception)
        {
            AddTimedAutomationDiagnostic("Spotify: Verlaufssicherung konnte nicht wiederhergestellt werden: " + exception.Message);
            SpotifySavedStateHistoryStatusText.Text = "Wiederherstellung fehlgeschlagen: " + exception.Message;
        }
    }


    private void LoadSpotifyHistoryRestoreProfiles()
    {
        _spotifyHistoryRestoreProfiles.Clear();
        _spotifyHistoryRestoreProfiles.Add(new SpotifyHistoryRestoreProfile("Nur Verlauf zusammenführen", true, false, false, false, false, true, true));
        _spotifyHistoryRestoreProfiles.Add(new SpotifyHistoryRestoreProfile("Verlauf + Favoriten", true, true, false, false, false, true, true));
        _spotifyHistoryRestoreProfiles.Add(new SpotifyHistoryRestoreProfile("Alles vollständig ersetzen", true, true, true, true, true, false, true));
        try
        {
            if (File.Exists(SpotifyHistoryRestoreProfilesPath))
            {
                var custom = JsonSerializer.Deserialize<List<SpotifyHistoryRestoreProfile>>(File.ReadAllText(SpotifyHistoryRestoreProfilesPath)) ?? [];
                foreach (var profile in custom.Where(profile => !string.IsNullOrWhiteSpace(profile.Name)))
                    _spotifyHistoryRestoreProfiles.Add(profile with { IsBuiltIn = false });
            }
        }
        catch (Exception exception)
        {
            AddTimedAutomationDiagnostic("Spotify: Wiederherstellungsprofile konnten nicht geladen werden: " + exception.Message);
        }
        SpotifyHistoryRestoreProfileBox.SelectedIndex = _spotifyHistoryRestoreProfiles.Count > 0 ? 0 : -1;
    }

    private void ApplySelectedSpotifyHistoryRestoreProfile()
    {
        if (SpotifyHistoryRestoreProfileBox.SelectedItem is not SpotifyHistoryRestoreProfile profile)
        {
            SpotifySavedStateHistoryStatusText.Text = "Bitte zuerst ein Wiederherstellungsprofil auswählen.";
            return;
        }
        RestoreSpotifyHistoryEntriesBox.IsChecked = profile.Entries;
        RestoreSpotifyHistoryFavoritesBox.IsChecked = profile.Favorites;
        RestoreSpotifyHistoryNotesBox.IsChecked = profile.Notes;
        RestoreSpotifyHistoryCountersBox.IsChecked = profile.Counters;
        RestoreSpotifyHistoryFiltersBox.IsChecked = profile.Filters;
        MergeSpotifyHistoryEntriesBox.IsChecked = profile.MergeEntries;
        SpotifySavedStateHistoryStatusText.Text = $"Wiederherstellungsprofil ‚{profile.Name}‘ angewendet.";
    }

    private void SaveSpotifyHistoryRestoreProfile()
    {
        var name = SpotifyHistoryRestoreProfileNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            SpotifySavedStateHistoryStatusText.Text = "Bitte einen Namen für das Wiederherstellungsprofil eingeben.";
            return;
        }
        var profile = new SpotifyHistoryRestoreProfile(name,
            RestoreSpotifyHistoryEntriesBox.IsChecked == true, RestoreSpotifyHistoryFavoritesBox.IsChecked == true,
            RestoreSpotifyHistoryNotesBox.IsChecked == true, RestoreSpotifyHistoryCountersBox.IsChecked == true,
            RestoreSpotifyHistoryFiltersBox.IsChecked == true, MergeSpotifyHistoryEntriesBox.IsChecked == true);
        var existing = _spotifyHistoryRestoreProfiles.FirstOrDefault(item => !item.IsBuiltIn && item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) _spotifyHistoryRestoreProfiles.Remove(existing);
        _spotifyHistoryRestoreProfiles.Add(profile);
        PersistSpotifyHistoryRestoreProfiles();
        SpotifyHistoryRestoreProfileBox.SelectedItem = profile;
        SpotifySavedStateHistoryStatusText.Text = $"Wiederherstellungsprofil ‚{name}‘ gespeichert.";
    }

    private void DeleteSpotifyHistoryRestoreProfile()
    {
        if (SpotifyHistoryRestoreProfileBox.SelectedItem is not SpotifyHistoryRestoreProfile profile) return;
        if (profile.IsBuiltIn)
        {
            SpotifySavedStateHistoryStatusText.Text = "Integrierte Wiederherstellungsprofile können nicht gelöscht werden.";
            return;
        }
        _spotifyHistoryRestoreProfiles.Remove(profile);
        PersistSpotifyHistoryRestoreProfiles();
        SpotifyHistoryRestoreProfileBox.SelectedIndex = _spotifyHistoryRestoreProfiles.Count > 0 ? 0 : -1;
        SpotifySavedStateHistoryStatusText.Text = $"Wiederherstellungsprofil ‚{profile.Name}‘ gelöscht.";
    }

    private void ExportSpotifyHistoryRestoreProfiles()
    {
        try
        {
            var customProfiles = _spotifyHistoryRestoreProfiles.Where(profile => !profile.IsBuiltIn).ToList();
            if (customProfiles.Count == 0)
            {
                SpotifySavedStateHistoryStatusText.Text = "Es sind keine eigenen Wiederherstellungsprofile zum Exportieren vorhanden.";
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Spotify-Wiederherstellungsprofile exportieren",
                Filter = "JSON-Datei (*.json)|*.json|Alle Dateien (*.*)|*.*",
                FileName = $"spotify-wiederherstellungsprofile-{DateTime.Now:yyyy-MM-dd}.json",
                DefaultExt = ".json",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != true) return;

            var exportModel = new
            {
                Format = "CreatorControlSuite.SpotifyHistoryRestoreProfiles",
                Version = 1,
                ExportedAt = DateTimeOffset.Now,
                Profiles = customProfiles
            };
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(exportModel, new JsonSerializerOptions { WriteIndented = true }));
            AddTimedAutomationDiagnostic($"Spotify: {customProfiles.Count} Wiederherstellungsprofil(e) exportiert: {dialog.FileName}");
            SpotifySavedStateHistoryStatusText.Text = $"{customProfiles.Count} eigene Wiederherstellungsprofile wurden exportiert.";
        }
        catch (Exception exception)
        {
            AddTimedAutomationDiagnostic("Spotify: Wiederherstellungsprofile konnten nicht exportiert werden: " + exception.Message);
            SpotifySavedStateHistoryStatusText.Text = "Profil-Export fehlgeschlagen: " + exception.Message;
        }
    }

    private void ImportSpotifyHistoryRestoreProfiles()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Spotify-Wiederherstellungsprofile prüfen",
                Filter = "JSON-Datei (*.json)|*.json|Alle Dateien (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return;

            var imported = ReadSpotifyHistoryRestoreProfilesImport(dialog.FileName);
            _pendingSpotifyHistoryRestoreProfileImport = imported;
            _pendingSpotifyHistoryRestoreProfileImportPath = dialog.FileName;
            _spotifyHistoryRestoreProfileImportPreview.Clear();

            var added = 0;
            var updated = 0;
            var unchanged = 0;
            foreach (var profile in imported)
            {
                var existing = _spotifyHistoryRestoreProfiles.FirstOrDefault(item => !item.IsBuiltIn && item.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    added++;
                    _spotifyHistoryRestoreProfileImportPreview.Add(new SpotifyHistoryRestoreProfileImportItem
                    {
                        Profile = profile, Status = "+ NEU", Description = DescribeSpotifyHistoryRestoreProfile(profile),
                        ActionOptions = ["Importieren", "Überspringen"], SelectedAction = "Importieren", CanSelect = true
                    });
                }
                else if (existing == profile)
                {
                    unchanged++;
                    _spotifyHistoryRestoreProfileImportPreview.Add(new SpotifyHistoryRestoreProfileImportItem
                    {
                        Profile = profile, Status = "= UNVERÄNDERT", Description = "Keine Änderung erforderlich",
                        ActionOptions = ["Überspringen"], SelectedAction = "Überspringen", CanSelect = false
                    });
                }
                else
                {
                    updated++;
                    _spotifyHistoryRestoreProfileImportPreview.Add(new SpotifyHistoryRestoreProfileImportItem
                    {
                        Profile = profile, Status = "~ KONFLIKT", Description = DescribeSpotifyHistoryRestoreProfile(profile),
                        ActionOptions = ["Überschreiben", "Als Kopie importieren", "Überspringen"], SelectedAction = "Überschreiben", CanSelect = true
                    });
                }
            }

            SpotifyHistoryRestoreProfileImportPreviewText.Text =
                $"Datei: {Path.GetFileName(dialog.FileName)} · {added} neu · {updated} aktualisieren · {unchanged} unverändert. " +
                "Für jedes Profil kann eine Importregel gewählt werden. Erst mit ‚IMPORT ÜBERNEHMEN‘ werden Änderungen gespeichert.";
            ConfirmSpotifyHistoryRestoreProfilesImportButton.IsEnabled = added + updated > 0;
            SpotifySavedStateHistoryStatusText.Text = $"Profil-Import geprüft: {added} neu, {updated} zu aktualisieren, {unchanged} unverändert.";
            AddTimedAutomationDiagnostic($"Spotify: Profil-Import geprüft: {added} neu, {updated} aktualisieren, {unchanged} unverändert ({dialog.FileName}).");
        }
        catch (Exception exception)
        {
            ResetPendingSpotifyHistoryRestoreProfileImport();
            AddTimedAutomationDiagnostic("Spotify: Wiederherstellungsprofil-Importprüfung fehlgeschlagen: " + exception.Message);
            SpotifySavedStateHistoryStatusText.Text = "Profil-Importprüfung fehlgeschlagen: " + exception.Message;
            SpotifyHistoryRestoreProfileImportPreviewText.Text = "Importdatei konnte nicht geprüft werden: " + exception.Message;
        }
    }

    private List<SpotifyHistoryRestoreProfile> ReadSpotifyHistoryRestoreProfilesImport(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        JsonElement profilesElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            profilesElement = root;
        }
        else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Profiles", out var wrappedProfiles))
        {
            if (root.TryGetProperty("Format", out var formatElement))
            {
                var format = formatElement.GetString();
                if (!string.IsNullOrWhiteSpace(format) && !format.Equals("CreatorControlSuite.SpotifyHistoryRestoreProfiles", StringComparison.Ordinal))
                    throw new InvalidDataException("Die Datei besitzt eine unbekannte Formatkennung.");
            }
            if (root.TryGetProperty("Version", out var versionElement) && versionElement.TryGetInt32(out var version) && version > 1)
                throw new InvalidDataException($"Die Profilversion {version} wird von dieser Suite-Version noch nicht unterstützt.");
            profilesElement = wrappedProfiles;
        }
        else
        {
            throw new InvalidDataException("Die Datei enthält keine gültige Profilliste.");
        }

        var imported = JsonSerializer.Deserialize<List<SpotifyHistoryRestoreProfile>>(profilesElement.GetRawText()) ?? [];
        imported = imported
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Name))
            .Select(profile => profile with { Name = profile.Name.Trim(), IsBuiltIn = false })
            .GroupBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();
        if (imported.Count == 0)
            throw new InvalidDataException("Die ausgewählte Datei enthält keine verwendbaren Profile.");
        return imported;
    }

    private void ConfirmSpotifyHistoryRestoreProfilesImport()
    {
        if (_pendingSpotifyHistoryRestoreProfileImport.Count == 0)
        {
            SpotifySavedStateHistoryStatusText.Text = "Bitte zuerst eine Importdatei prüfen.";
            return;
        }

        try
        {
            var replaced = 0;
            var added = 0;
            var unchanged = 0;
            SpotifyHistoryRestoreProfile? lastChanged = null;
            var actionableItems = _spotifyHistoryRestoreProfileImportPreview
                .Where(item => item.CanSelect && !item.SelectedAction.Equals("Überspringen", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (actionableItems.Count == 0)
            {
                SpotifySavedStateHistoryStatusText.Text = "Alle Profile sind auf ‚Überspringen‘ eingestellt.";
                return;
            }

            var copied = 0;
            foreach (var importItem in actionableItems)
            {
                var profile = importItem.Profile;
                var existing = _spotifyHistoryRestoreProfiles.FirstOrDefault(item => !item.IsBuiltIn && item.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));
                if (existing == profile)
                {
                    unchanged++;
                    continue;
                }

                if (importItem.SelectedAction.Equals("Als Kopie importieren", StringComparison.OrdinalIgnoreCase))
                {
                    var copyName = CreateUniqueSpotifyHistoryRestoreProfileName(profile.Name + " (Import)");
                    var copy = profile with { Name = copyName, IsBuiltIn = false };
                    _spotifyHistoryRestoreProfiles.Add(copy);
                    lastChanged = copy;
                    copied++;
                    continue;
                }

                if (existing is not null)
                {
                    _spotifyHistoryRestoreProfiles.Remove(existing);
                    replaced++;
                }
                else
                {
                    added++;
                }
                _spotifyHistoryRestoreProfiles.Add(profile);
                lastChanged = profile;
            }

            PersistSpotifyHistoryRestoreProfiles();
            if (lastChanged is not null) SpotifyHistoryRestoreProfileBox.SelectedItem = lastChanged;
            var fileName = Path.GetFileName(_pendingSpotifyHistoryRestoreProfileImportPath);
            AddTimedAutomationDiagnostic($"Spotify: Wiederherstellungsprofile übernommen: {added} neu, {replaced} überschrieben, {copied} als Kopie, {unchanged} unverändert ({fileName}).");
            SpotifySavedStateHistoryStatusText.Text = $"Profil-Import übernommen: {added} neu, {replaced} überschrieben, {copied} als Kopie. Übersprungene Profile blieben unverändert.";
            SpotifyHistoryRestoreProfileImportPreviewText.Text = $"Import aus {fileName} wurde erfolgreich übernommen.";
            ConfirmSpotifyHistoryRestoreProfilesImportButton.IsEnabled = false;
            _pendingSpotifyHistoryRestoreProfileImport = [];
            _pendingSpotifyHistoryRestoreProfileImportPath = "";
        }
        catch (Exception exception)
        {
            AddTimedAutomationDiagnostic("Spotify: Wiederherstellungsprofile konnten nicht übernommen werden: " + exception.Message);
            SpotifySavedStateHistoryStatusText.Text = "Profil-Import fehlgeschlagen: " + exception.Message;
        }
    }


    private string CreateUniqueSpotifyHistoryRestoreProfileName(string requestedName)
    {
        var baseName = string.IsNullOrWhiteSpace(requestedName) ? "Importiertes Profil" : requestedName.Trim();
        var candidate = baseName;
        var suffix = 2;
        while (_spotifyHistoryRestoreProfiles.Any(profile => profile.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            candidate = $"{baseName} {suffix++}";
        return candidate;
    }

    private static string DescribeSpotifyHistoryRestoreProfile(SpotifyHistoryRestoreProfile profile)
    {
        var parts = new List<string>();
        if (profile.Entries) parts.Add(profile.MergeEntries ? "Verlauf zusammenführen" : "Verlauf ersetzen");
        if (profile.Favorites) parts.Add("Favoriten");
        if (profile.Notes) parts.Add("Notizen");
        if (profile.Counters) parts.Add("Statistik");
        if (profile.Filters) parts.Add("Filter/Sortierung");
        return parts.Count == 0 ? "keine Bereiche aktiviert" : string.Join(", ", parts);
    }

    private void ResetPendingSpotifyHistoryRestoreProfileImport()
    {
        _pendingSpotifyHistoryRestoreProfileImport = [];
        _pendingSpotifyHistoryRestoreProfileImportPath = "";
        _spotifyHistoryRestoreProfileImportPreview.Clear();
        if (ConfirmSpotifyHistoryRestoreProfilesImportButton is not null)
            ConfirmSpotifyHistoryRestoreProfilesImportButton.IsEnabled = false;
    }

    private void PersistSpotifyHistoryRestoreProfiles()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SpotifyHistoryRestoreProfilesPath)!);
            var custom = _spotifyHistoryRestoreProfiles.Where(profile => !profile.IsBuiltIn).ToList();
            File.WriteAllText(SpotifyHistoryRestoreProfilesPath, JsonSerializer.Serialize(custom, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            AddTimedAutomationDiagnostic("Spotify: Wiederherstellungsprofile konnten nicht gespeichert werden: " + exception.Message);
            SpotifySavedStateHistoryStatusText.Text = "Profil konnte nicht gespeichert werden: " + exception.Message;
        }
    }

    private void RestoreSelectedSpotifySavedStateHistoryParts()
    {
        if (SpotifySavedStateHistoryBackupsList?.SelectedItem is not SpotifySavedStateHistoryBackupItem backup)
        {
            SpotifySavedStateHistoryStatusText.Text = "Bitte zuerst einen Wiederherstellungspunkt auswählen.";
            return;
        }

        var restoreEntries = RestoreSpotifyHistoryEntriesBox.IsChecked == true;
        var restoreFavorites = RestoreSpotifyHistoryFavoritesBox.IsChecked == true;
        var restoreNotes = RestoreSpotifyHistoryNotesBox.IsChecked == true;
        var restoreCounters = RestoreSpotifyHistoryCountersBox.IsChecked == true;
        var restoreFilters = RestoreSpotifyHistoryFiltersBox.IsChecked == true;
        if (!restoreEntries && !restoreFavorites && !restoreNotes && !restoreCounters && !restoreFilters)
        {
            SpotifySavedStateHistoryStatusText.Text = "Bitte mindestens einen Bereich für die Wiederherstellung auswählen.";
            return;
        }

        var selectedAreas = new List<string>();
        if (restoreEntries) selectedAreas.Add(MergeSpotifyHistoryEntriesBox.IsChecked == true ? "Verlauf (zusammenführen)" : "Verlauf (ersetzen)");
        if (restoreFavorites) selectedAreas.Add("Favoriten");
        if (restoreNotes) selectedAreas.Add("Notizen");
        if (restoreCounters) selectedAreas.Add("Statistikzähler");
        if (restoreFilters) selectedAreas.Add("Filter und Sortierung");

        var result = MessageBox.Show(
            $"Folgende Bereiche aus der Sicherung vom {backup.LastWriteTime:dd.MM.yyyy HH:mm:ss} laden?\n\n• {string.Join("\n• ", selectedAreas)}\n\nDer aktuelle Zustand wird vorher automatisch gesichert.",
            "Spotify-Verlauf selektiv wiederherstellen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            var state = JsonSerializer.Deserialize<SpotifySavedStateHistoryPersistence>(File.ReadAllText(backup.FullPath));
            if (state is null || state.FormatVersion != 1 || state.Entries is null)
                throw new InvalidDataException("Die Sicherungsdatei enthält kein unterstütztes Verlaufsformat.");

            CreateSpotifySavedStateHistoryBackup(manual: false);
            _loadingSpotifySavedStateHistoryPersistence = true;
            try
            {
                if (restoreEntries)
                {
                    if (MergeSpotifyHistoryEntriesBox.IsChecked == true)
                    {
                        var merged = state.Entries
                            .Concat(_spotifySavedStateHistory)
                            .Distinct(StringComparer.Ordinal)
                            .Take(100)
                            .ToList();
                        _spotifySavedStateHistory.Clear();
                        foreach (var entry in merged) _spotifySavedStateHistory.Add(entry);
                    }
                    else
                    {
                        _spotifySavedStateHistory.Clear();
                        foreach (var entry in state.Entries.Take(100)) _spotifySavedStateHistory.Add(entry);
                    }
                }

                if (restoreFavorites)
                {
                    _spotifySavedStateHistoryFavorites.Clear();
                    foreach (var entry in state.FavoriteEntries ?? [])
                        if (_spotifySavedStateHistory.Contains(entry)) _spotifySavedStateHistoryFavorites.Add(entry);
                }

                if (restoreNotes)
                {
                    _spotifySavedStateHistoryNotes.Clear();
                    foreach (var pair in state.Notes ?? new Dictionary<string, string>())
                        if (_spotifySavedStateHistory.Contains(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                            _spotifySavedStateHistoryNotes[pair.Key] = pair.Value;
                }

                if (restoreCounters)
                {
                    _spotifySavedStateSaveCount = state.SavedCount;
                    _spotifySavedStateRestoreCount = state.RestoredCount;
                    _spotifySavedStateDiscardCount = state.DiscardedCount;
                    _spotifySavedStateCleanupCount = state.CleanupCount;
                }

                if (restoreFilters)
                {
                    SpotifySavedStateHistorySearchBox.Text = state.SearchText ?? "";
                    SpotifySavedStateHistoryActionFilterBox.SelectedIndex = Math.Max(0, Math.Min(state.ActionFilterIndex, SpotifySavedStateHistoryActionFilterBox.Items.Count - 1));
                    SpotifySavedStateHistorySortBox.SelectedIndex = Math.Max(0, Math.Min(state.SortIndex, SpotifySavedStateHistorySortBox.Items.Count - 1));
                    SpotifySavedStateHistoryFavoritesOnlyBox.IsChecked = state.FavoritesOnly;
                }
            }
            finally
            {
                _loadingSpotifySavedStateHistoryPersistence = false;
            }

            ApplySpotifySavedStateHistorySort();
            RefreshSpotifySavedStateHistoryFilter();
            RefreshSpotifySavedStateStatistics();
            UpdateSpotifySavedStateHistoryDetail();
            SaveSpotifySavedStateHistoryPersistence();
            UpdateSpotifySavedStateHistoryBackupPreview(showStatus: false);
            AddTimedAutomationDiagnostic($"Spotify: Bereiche aus Verlaufssicherung selektiv wiederhergestellt: {string.Join(", ", selectedAreas)}.");
            SpotifySavedStateHistoryStatusText.Text = $"Selektiv wiederhergestellt: {string.Join(", ", selectedAreas)}.";
        }
        catch (Exception exception)
        {
            AddTimedAutomationDiagnostic("Spotify: Selektive Wiederherstellung fehlgeschlagen: " + exception.Message);
            SpotifySavedStateHistoryStatusText.Text = "Selektive Wiederherstellung fehlgeschlagen: " + exception.Message;
        }
    }

    private void DeleteSelectedSpotifySavedStateHistoryBackup()
    {
        if (SpotifySavedStateHistoryBackupsList?.SelectedItem is not SpotifySavedStateHistoryBackupItem backup)
        {
            SpotifySavedStateHistoryStatusText.Text = "Bitte zuerst eine Sicherung zum Löschen auswählen.";
            return;
        }

        var result = MessageBox.Show(
            $"Die Sicherung vom {backup.LastWriteTime:dd.MM.yyyy HH:mm:ss} endgültig löschen?",
            "Spotify-Sicherung löschen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            File.Delete(backup.FullPath);
            RefreshSpotifySavedStateHistoryBackups();
            AddTimedAutomationDiagnostic($"Spotify: Verlaufssicherung vom {backup.LastWriteTime:dd.MM.yyyy HH:mm:ss} gelöscht.");
            SpotifySavedStateHistoryStatusText.Text = "Ausgewählter Wiederherstellungspunkt wurde gelöscht.";
        }
        catch (Exception exception)
        {
            AddTimedAutomationDiagnostic("Spotify: Verlaufssicherung konnte nicht gelöscht werden: " + exception.Message);
            SpotifySavedStateHistoryStatusText.Text = "Sicherung konnte nicht gelöscht werden: " + exception.Message;
        }
    }

    private void OpenSpotifySavedStateHistoryBackupFolder()
    {
        try
        {
            Directory.CreateDirectory(SpotifySavedStateHistoryBackupDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = SpotifySavedStateHistoryBackupDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            AddTimedAutomationDiagnostic("Spotify: Sicherungsordner konnte nicht geöffnet werden: " + exception.Message);
            SpotifySavedStateHistoryStatusText.Text = "Sicherungsordner konnte nicht geöffnet werden: " + exception.Message;
        }
    }

    private void AddSpotifySavedStateHistory(string message)
    {
        _spotifySavedStateHistory.Insert(0, $"{DateTime.Now:HH:mm:ss} · {message}");
        while (_spotifySavedStateHistory.Count > 100) _spotifySavedStateHistory.RemoveAt(_spotifySavedStateHistory.Count - 1);
        RefreshSpotifySavedStateStatistics();
        SaveSpotifySavedStateHistoryPersistence();
    }

    private bool SpotifySavedStateHistoryMatchesFilter(object item)
    {
        if (item is not string entry) return false;
        var search = SpotifySavedStateHistorySearchBox?.Text?.Trim() ?? "";
        var note = _spotifySavedStateHistoryNotes.TryGetValue(entry, out var savedNote) ? savedNote : "";
        if (!string.IsNullOrWhiteSpace(search) &&
            entry.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
            note.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        if (SpotifySavedStateHistoryFavoritesOnlyBox?.IsChecked == true && !_spotifySavedStateHistoryFavorites.Contains(entry))
            return false;

        var action = (SpotifySavedStateHistoryActionFilterBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
        return action switch
        {
            "save" => entry.Contains("gespeichert", StringComparison.OrdinalIgnoreCase),
            "restore" => entry.Contains("wiederhergestellt", StringComparison.OrdinalIgnoreCase),
            "discard" => entry.Contains("verworfen", StringComparison.OrdinalIgnoreCase),
            "cleanup" => entry.Contains("Bereinigung", StringComparison.OrdinalIgnoreCase) || entry.Contains("bereinigt", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private void RefreshSpotifySavedStateHistoryFilter()
    {
        _spotifySavedStateHistoryView?.Refresh();
        RefreshSpotifySavedStateStatistics();
    }

    private void ResetSpotifySavedStateHistoryFilter()
    {
        SpotifySavedStateHistorySearchBox.Text = "";
        SpotifySavedStateHistoryActionFilterBox.SelectedIndex = 0;
        SpotifySavedStateHistorySortBox.SelectedIndex = 0;
        SpotifySavedStateHistoryFavoritesOnlyBox.IsChecked = false;
        RefreshSpotifySavedStateHistoryFilter();
    }

    private void ApplySpotifySavedStateHistorySort()
    {
        if (_spotifySavedStateHistoryView is not ListCollectionView listView) return;
        var mode = (SpotifySavedStateHistorySortBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "newest";
        listView.CustomSort = new SpotifySavedStateHistoryComparer(mode);
        UpdateSpotifySavedStateHistoryDetail();
        RefreshSpotifySavedStateStatistics();
    }

    private void UpdateSpotifySavedStateHistoryDetail()
    {
        if (SpotifySavedStateHistoryDetailText is null) return;
        var selectedEntries = SpotifySavedStateHistoryList?.SelectedItems.Cast<object>().OfType<string>().ToList() ?? [];
        if (selectedEntries.Count == 0)
        {
            SpotifySavedStateHistoryDetailText.Text = "Kein Verlaufseintrag ausgewählt.";
            SpotifySavedStateHistoryNoteBox.Text = "";
            ToggleSpotifySavedStateHistoryFavoriteButton.Content = "ALS FAVORIT MARKIEREN";
            return;
        }
        if (selectedEntries.Count > 1)
        {
            SpotifySavedStateHistoryDetailText.Text = $"{selectedEntries.Count} Verlaufseinträge ausgewählt. Die Sammelaktionen können diese Auswahl exportieren oder entfernen.";
            SpotifySavedStateHistoryNoteBox.Text = "";
            ToggleSpotifySavedStateHistoryFavoriteButton.Content = "ALS FAVORIT MARKIEREN";
            return;
        }

        var entry = selectedEntries[0];
        var separator = entry.IndexOf(" · ", StringComparison.Ordinal);
        var time = separator >= 0 ? entry[..separator] : "Unbekannt";
        var message = separator >= 0 ? entry[(separator + 3)..] : entry;
        var groupSeparator = message.IndexOf(':');
        var group = groupSeparator > 0 ? message[..groupSeparator].Trim() : "Allgemein";
        var action = groupSeparator > 0 ? message[(groupSeparator + 1)..].Trim() : message;
        var favorite = _spotifySavedStateHistoryFavorites.Contains(entry) ? "Ja" : "Nein";
        var note = _spotifySavedStateHistoryNotes.TryGetValue(entry, out var savedNote) ? savedNote : "";
        SpotifySavedStateHistoryDetailText.Text = $"Zeit: {time}\nGruppe: {group}\nAktion: {action}\nFavorit: {favorite}";
        SpotifySavedStateHistoryNoteBox.Text = note;
        ToggleSpotifySavedStateHistoryFavoriteButton.Content = favorite == "Ja" ? "FAVORIT ENTFERNEN" : "ALS FAVORIT MARKIEREN";
    }

    private void ToggleSpotifySavedStateHistoryFavorite()
    {
        var selected = GetSelectedSpotifySavedStateHistory();
        if (selected.Count != 1)
        {
            SpotifySavedStateHistoryStatusText.Text = "Bitte genau einen Verlaufseintrag auswählen.";
            return;
        }
        var entry = selected[0];
        if (!_spotifySavedStateHistoryFavorites.Add(entry))
            _spotifySavedStateHistoryFavorites.Remove(entry);
        UpdateSpotifySavedStateHistoryDetail();
        RefreshSpotifySavedStateHistoryFilter();
        SpotifySavedStateHistoryStatusText.Text = _spotifySavedStateHistoryFavorites.Contains(entry) ? "Eintrag als Favorit markiert." : "Favoritenmarkierung entfernt.";
        SaveSpotifySavedStateHistoryPersistence();
    }

    private void SaveSpotifySavedStateHistoryNote()
    {
        var selected = GetSelectedSpotifySavedStateHistory();
        if (selected.Count != 1)
        {
            SpotifySavedStateHistoryStatusText.Text = "Bitte genau einen Verlaufseintrag für die Notiz auswählen.";
            return;
        }
        var entry = selected[0];
        var note = SpotifySavedStateHistoryNoteBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(note))
            _spotifySavedStateHistoryNotes.Remove(entry);
        else
            _spotifySavedStateHistoryNotes[entry] = note;
        RefreshSpotifySavedStateHistoryFilter();
        SpotifySavedStateHistoryStatusText.Text = string.IsNullOrWhiteSpace(note) ? "Notiz entfernt." : "Notiz gespeichert.";
        SaveSpotifySavedStateHistoryPersistence();
    }

    private List<string> GetFilteredSpotifySavedStateHistory()
    {
        if (_spotifySavedStateHistoryView is null) return _spotifySavedStateHistory.ToList();
        return _spotifySavedStateHistoryView.Cast<object>().OfType<string>().ToList();
    }

    private List<string> GetSelectedSpotifySavedStateHistory()
    {
        return SpotifySavedStateHistoryList?.SelectedItems.Cast<object>().OfType<string>().ToList() ?? [];
    }

    private void SelectVisibleSpotifySavedStateHistory()
    {
        SpotifySavedStateHistoryList.SelectedItems.Clear();
        foreach (var entry in GetFilteredSpotifySavedStateHistory())
            SpotifySavedStateHistoryList.SelectedItems.Add(entry);
        UpdateSpotifySavedStateHistoryDetail();
        SpotifySavedStateHistoryStatusText.Text = $"{SpotifySavedStateHistoryList.SelectedItems.Count} sichtbare Verlaufseinträge ausgewählt.";
    }

    private void RemoveSelectedSpotifySavedStateHistory()
    {
        var selected = GetSelectedSpotifySavedStateHistory();
        if (selected.Count == 0)
        {
            SpotifySavedStateHistoryStatusText.Text = "Bitte zuerst mindestens einen Verlaufseintrag auswählen.";
            return;
        }

        foreach (var entry in selected)
        {
            _spotifySavedStateHistory.Remove(entry);
            _spotifySavedStateHistoryFavorites.Remove(entry);
            _spotifySavedStateHistoryNotes.Remove(entry);
        }
        SpotifySavedStateHistoryList.SelectedItems.Clear();
        UpdateSpotifySavedStateHistoryDetail();
        RefreshSpotifySavedStateStatistics();
        SpotifySavedStateHistoryStatusText.Text = $"{selected.Count} ausgewählte Verlaufseinträge entfernt.";
        SaveSpotifySavedStateHistoryPersistence();
        AddTimedAutomationDiagnostic($"Spotify: {selected.Count} ausgewählte Zustandsverlaufseinträge entfernt.");
    }

    private void ExportSelectedSpotifySavedStateHistory()
    {
        var entries = GetSelectedSpotifySavedStateHistory();
        if (entries.Count == 0)
        {
            SpotifySavedStateHistoryStatusText.Text = "Für den JSON-Export wurde kein Verlaufseintrag ausgewählt.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Spotify-Zustandsverlauf (*.json)|*.json",
            FileName = $"spotify-zustandsverlauf-auswahl-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            AddExtension = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var export = new SpotifySavedStateHistoryExport(
                2, DateTimeOffset.UtcNow, _spotifySavedStateSaveCount, _spotifySavedStateRestoreCount,
                _spotifySavedStateDiscardCount, _spotifySavedStateCleanupCount, entries,
                entries.Where(_spotifySavedStateHistoryFavorites.Contains).ToList(),
                _spotifySavedStateHistoryNotes.Where(pair => entries.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value));
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true }));
            SpotifySavedStateHistoryStatusText.Text = $"{entries.Count} ausgewählte Einträge als JSON exportiert: {Path.GetFileName(dialog.FileName)}";
            AddTimedAutomationDiagnostic($"Spotify: {entries.Count} ausgewählte Zustandsverlaufseinträge als JSON exportiert.");
        }
        catch (Exception exception)
        {
            SpotifySavedStateHistoryStatusText.Text = "Auswahl-Export fehlgeschlagen: " + exception.Message;
            AddTimedAutomationDiagnostic("Spotify: JSON-Auswahlexport fehlgeschlagen: " + exception.Message);
        }
    }

    private void ExportSelectedSpotifySavedStateHistoryCsv()
    {
        var entries = GetSelectedSpotifySavedStateHistory();
        if (entries.Count == 0)
        {
            SpotifySavedStateHistoryStatusText.Text = "Für den CSV-Export wurde kein Verlaufseintrag ausgewählt.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV-Datei (*.csv)|*.csv",
            FileName = $"spotify-zustandsverlauf-auswahl-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var csv = BuildSpotifySavedStateHistoryCsv(entries);
            File.WriteAllText(dialog.FileName, csv, new UTF8Encoding(true));
            SpotifySavedStateHistoryStatusText.Text = $"{entries.Count} ausgewählte Einträge als CSV exportiert: {Path.GetFileName(dialog.FileName)}";
            AddTimedAutomationDiagnostic($"Spotify: {entries.Count} ausgewählte Zustandsverlaufseinträge als CSV exportiert.");
        }
        catch (Exception exception)
        {
            SpotifySavedStateHistoryStatusText.Text = "CSV-Auswahlexport fehlgeschlagen: " + exception.Message;
            AddTimedAutomationDiagnostic("Spotify: CSV-Auswahlexport fehlgeschlagen: " + exception.Message);
        }
    }

    private string BuildSpotifySavedStateHistoryCsv(IEnumerable<string> entries)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Zeit;Aktion;Favorit;Notiz");
        foreach (var entry in entries)
        {
            var separator = entry.IndexOf(" · ", StringComparison.Ordinal);
            var time = separator >= 0 ? entry[..separator] : "";
            var action = separator >= 0 ? entry[(separator + 3)..] : entry;
            csv.Append('"').Append(time.Replace("\"", "\"\"")).Append("\";\"")
               .Append(action.Replace("\"", "\"\"")).Append("\";\"")
               .Append(_spotifySavedStateHistoryFavorites.Contains(entry) ? "Ja" : "Nein").Append("\";\"")
               .Append((_spotifySavedStateHistoryNotes.TryGetValue(entry, out var note) ? note : "").Replace("\"", "\"\"")).AppendLine("\"");
        }
        return csv.ToString();
    }

    private void ExportSpotifySavedStateHistoryCsv()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV-Datei (*.csv)|*.csv",
            FileName = $"spotify-zustandsverlauf-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var entries = GetFilteredSpotifySavedStateHistory();
            File.WriteAllText(dialog.FileName, BuildSpotifySavedStateHistoryCsv(entries), new UTF8Encoding(true));
            SpotifySavedStateHistoryStatusText.Text = $"{entries.Count} gefilterte Verlaufseinträge als CSV exportiert: {Path.GetFileName(dialog.FileName)}";
            AddTimedAutomationDiagnostic($"Spotify: {entries.Count} gefilterte Zustandsverlaufseinträge als CSV exportiert.");
        }
        catch (Exception exception)
        {
            SpotifySavedStateHistoryStatusText.Text = "CSV-Export fehlgeschlagen: " + exception.Message;
            AddTimedAutomationDiagnostic("Spotify: CSV-Export des Zustandsverlaufs fehlgeschlagen: " + exception.Message);
        }
    }

    private void ExportSpotifySavedStateHistory()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Spotify-Zustandsverlauf (*.json)|*.json",
            FileName = $"spotify-zustandsverlauf-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            AddExtension = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var export = new SpotifySavedStateHistoryExport(
                2,
                DateTimeOffset.UtcNow,
                _spotifySavedStateSaveCount,
                _spotifySavedStateRestoreCount,
                _spotifySavedStateDiscardCount,
                _spotifySavedStateCleanupCount,
                _spotifySavedStateHistory.ToList(),
                _spotifySavedStateHistoryFavorites.ToList(),
                new Dictionary<string, string>(_spotifySavedStateHistoryNotes));
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true }));
            SpotifySavedStateHistoryStatusText.Text = $"Verlauf exportiert: {Path.GetFileName(dialog.FileName)}";
            AddTimedAutomationDiagnostic($"Spotify: Zustandsverlauf mit {_spotifySavedStateHistory.Count} Einträgen exportiert.");
        }
        catch (Exception exception)
        {
            SpotifySavedStateHistoryStatusText.Text = "Export fehlgeschlagen: " + exception.Message;
            AddTimedAutomationDiagnostic("Spotify: Export des Zustandsverlaufs fehlgeschlagen: " + exception.Message);
        }
    }

    private void ImportSpotifySavedStateHistory()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Spotify-Zustandsverlauf (*.json)|*.json|JSON (*.json)|*.json",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var import = JsonSerializer.Deserialize<SpotifySavedStateHistoryExport>(File.ReadAllText(dialog.FileName));
            if (import is null || import.FormatVersion is < 1 or > 2 || import.Entries is null)
                throw new InvalidDataException("Die Datei besitzt kein unterstütztes Verlaufsformat.");

            _spotifySavedStateHistory.Clear();
            _spotifySavedStateHistoryFavorites.Clear();
            _spotifySavedStateHistoryNotes.Clear();
            foreach (var entry in import.Entries.Where(entry => !string.IsNullOrWhiteSpace(entry)).Take(100))
                _spotifySavedStateHistory.Add(entry);
            _spotifySavedStateSaveCount = Math.Max(0, import.SavedCount);
            _spotifySavedStateRestoreCount = Math.Max(0, import.RestoredCount);
            _spotifySavedStateDiscardCount = Math.Max(0, import.DiscardedCount);
            _spotifySavedStateCleanupCount = Math.Max(0, import.CleanupCount);
            foreach (var entry in import.FavoriteEntries ?? [])
                if (_spotifySavedStateHistory.Contains(entry)) _spotifySavedStateHistoryFavorites.Add(entry);
            foreach (var pair in import.Notes ?? new Dictionary<string, string>())
                if (_spotifySavedStateHistory.Contains(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value)) _spotifySavedStateHistoryNotes[pair.Key] = pair.Value;
            RefreshSpotifySavedStateStatistics();
            SpotifySavedStateHistoryStatusText.Text = $"Verlauf importiert: {_spotifySavedStateHistory.Count} Einträge aus {Path.GetFileName(dialog.FileName)}.";
            AddTimedAutomationDiagnostic($"Spotify: Zustandsverlauf mit {_spotifySavedStateHistory.Count} Einträgen importiert.");
            SaveSpotifySavedStateHistoryPersistence();
        }
        catch (Exception exception)
        {
            SpotifySavedStateHistoryStatusText.Text = "Import fehlgeschlagen: " + exception.Message;
            AddTimedAutomationDiagnostic("Spotify: Import des Zustandsverlaufs fehlgeschlagen: " + exception.Message);
        }
    }

    private void ClearSpotifySavedStateHistory()
    {
        _spotifySavedStateHistory.Clear();
        _spotifySavedStateHistoryFavorites.Clear();
        _spotifySavedStateHistoryNotes.Clear();
        SpotifySavedStateHistoryList.SelectedItem = null;
        UpdateSpotifySavedStateHistoryDetail();
        AddTimedAutomationDiagnostic("Spotify: Verlauf der gespeicherten Zustände wurde geleert.");
        RefreshSpotifySavedStateStatistics();
        SaveSpotifySavedStateHistoryPersistence();
    }

    private void RefreshSpotifySavedStateStatistics()
    {
        if (SpotifySavedStateStatisticsText is null) return;
        SpotifySavedStateStatisticsText.Text =
            $"Gespeichert: {_spotifySavedStateSaveCount} · Wiederhergestellt: {_spotifySavedStateRestoreCount} · " +
            $"Verworfen: {_spotifySavedStateDiscardCount} · Automatisch bereinigt: {_spotifySavedStateCleanupCount} · " +
            $"Aktuell vorhanden: {_spotifyAutomationSavedStates.Count}";
        var visibleCount = _spotifySavedStateHistoryView?.Cast<object>().Count() ?? _spotifySavedStateHistory.Count;
        SpotifySavedStateHistoryStatusText.Text = _spotifySavedStateHistory.Count == 0
            ? "Noch keine Zustandsaktionen in dieser Programmsitzung."
            : visibleCount == _spotifySavedStateHistory.Count
                ? $"{_spotifySavedStateHistory.Count} Einträge · neuester Eintrag oben."
                : $"{visibleCount} von {_spotifySavedStateHistory.Count} Einträgen sichtbar · Filter aktiv.";
    }

    private int GetSpotifySavedStateMaxAgeMinutes()
    {
        return int.TryParse(SpotifySavedStateMaxAgeBox.Text, out var minutes)
            ? Math.Clamp(minutes, 1, 10080)
            : 180;
    }

    private bool IsSpotifySavedStateExpired(SpotifyAutomationSavedState state)
    {
        return DateTimeOffset.UtcNow - state.SavedAtUtc > TimeSpan.FromMinutes(GetSpotifySavedStateMaxAgeMinutes());
    }

    private static string FormatSpotifySavedStateAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        if (age.TotalMinutes < 1) return "weniger als 1 Minute";
        if (age.TotalHours < 1) return $"{Math.Max(1, (int)age.TotalMinutes)} Min.";
        if (age.TotalDays < 1) return $"{(int)age.TotalHours} Std. {age.Minutes} Min.";
        return $"{(int)age.TotalDays} T. {age.Hours} Std.";
    }

    private int GetSpotifySavedStateCleanupIntervalMinutes()
    {
        return int.TryParse(SpotifySavedStateCleanupIntervalMinutesBox.Text, out var minutes)
            ? Math.Clamp(minutes, 1, 1440)
            : 15;
    }

    private void UpdateSpotifySavedStateCleanupTimer()
    {
        if (SpotifySavedStateCleanupIntervalBox is null || SpotifySavedStateCleanupIntervalMinutesBox is null) return;

        _spotifySavedStateCleanupTimer.Stop();
        var minutes = GetSpotifySavedStateCleanupIntervalMinutes();
        _spotifySavedStateCleanupTimer.Interval = TimeSpan.FromMinutes(minutes);

        if (SpotifySavedStateCleanupIntervalBox.IsChecked == true)
        {
            _spotifySavedStateCleanupTimer.Start();
            SpotifySavedStatesOverviewStatusText.Text = $"Automatische Bereinigung ist aktiv · alle {minutes} Minuten.";
        }
    }

    private void DiscardExpiredSpotifySavedStates(string reason, bool onlyLogWhenRemoved = false)
    {
        List<string> expiredGroups;
        lock (_spotifyAutomationSync)
        {
            expiredGroups = _spotifyAutomationSavedStates
                .Where(entry => IsSpotifySavedStateExpired(entry.Value))
                .Select(entry => entry.Key)
                .ToList();
            foreach (var group in expiredGroups) _spotifyAutomationSavedStates.Remove(group);
        }

        if (expiredGroups.Count > 0)
        {
            _spotifySavedStateCleanupCount += expiredGroups.Count;
            AddSpotifySavedStateHistory($"Bereinigung ({reason}): {expiredGroups.Count} entfernt");
            AddTimedAutomationDiagnostic($"Spotify ({reason}): {expiredGroups.Count} abgelaufene Zustände verworfen ({string.Join(", ", expiredGroups)}).");
        }
        else if (!onlyLogWhenRemoved)
        {
            AddTimedAutomationDiagnostic($"Spotify ({reason}): Keine abgelaufenen gespeicherten Zustände gefunden.");
        }

        RefreshSpotifySavedStateStatus();
        RefreshSpotifySavedStatesOverview();
    }

    private async Task RestoreSpotifySavedStateNowAsync()
    {
        var group = GetSpotifyAutomationEditorGroup();
        var fadeSeconds = int.TryParse(TimedAutomationSpotifyFadeBox.Text, out var fade)
            ? Math.Clamp(fade, 0, 300)
            : 0;
        var restoreRule = new TimedAutomationRuleSettings { SpotifyFadeSeconds = fadeSeconds };

        CancellationTokenSource restoreCts;
        lock (_spotifyAutomationSync)
        {
            _spotifyAutomationCts?.Cancel();
            _spotifyAutomationCts?.Dispose();
            _spotifyAutomationCts = new CancellationTokenSource();
            _activeSpotifyAutomationPriority = int.MaxValue;
            _activeSpotifyAutomationGroup = group;
            _activeSpotifyAutomationExclusive = true;
            restoreCts = _spotifyAutomationCts;
        }

        try
        {
            await RestoreSpotifyAutomationStateAsync(group, restoreRule, restoreCts.Token);
            RefreshSpotifySavedStateStatus();
        }
        catch (OperationCanceledException)
        {
            AddTimedAutomationDiagnostic($"Spotify-Gruppe '{group}': Manuelle Wiederherstellung abgebrochen.");
        }
        catch (Exception ex)
        {
            AddTimedAutomationDiagnostic($"Spotify-Gruppe '{group}': Manuelle Wiederherstellung fehlgeschlagen: {ex.Message}");
            TimedAutomationSpotifySavedStateText.Text = ex.Message;
        }
        finally
        {
            lock (_spotifyAutomationSync)
            {
                if (ReferenceEquals(_spotifyAutomationCts, restoreCts))
                {
                    _spotifyAutomationCts.Dispose();
                    _spotifyAutomationCts = null;
                    _activeSpotifyAutomationPriority = int.MinValue;
                    _activeSpotifyAutomationGroup = "";
                    _activeSpotifyAutomationExclusive = false;
                }
            }
        }
    }

    private void DiscardSpotifySavedState()
    {
        var group = GetSpotifyAutomationEditorGroup();
        var removed = false;
        lock (_spotifyAutomationSync) removed = _spotifyAutomationSavedStates.Remove(group);
        AddTimedAutomationDiagnostic(removed
            ? $"Spotify-Gruppe '{group}': Gespeicherter Zustand wurde verworfen."
            : $"Spotify-Gruppe '{group}': Es war kein gespeicherter Zustand vorhanden.");
        if (removed)
        {
            _spotifySavedStateDiscardCount++;
            AddSpotifySavedStateHistory($"{group}: Zustand verworfen");
        }
        RefreshSpotifySavedStateStatus();
        RefreshSpotifySavedStatesOverview();
    }

    private async Task SaveSpotifyAutomationStateAsync(string group, CancellationToken cancellationToken)
    {
        if (SpotifySavedStateCleanupOnSaveBox.IsChecked == true)
            DiscardExpiredSpotifySavedStates("vor neuem Speichern", onlyLogWhenRemoved: true);
        await _spotifyModule.RefreshPlaybackAsync(cancellationToken);
        var playback = _spotifyModule.GetSnapshot().Playback;
        if (!playback.HasPlayback || playback.Track is null)
        {
            AddTimedAutomationDiagnostic($"Spotify-Gruppe '{group}': Kein aktiver Wiedergabezustand zum Sichern vorhanden.");
            return;
        }

        var state = new SpotifyAutomationSavedState(
            playback.ContextUri ?? "",
            playback.Track,
            Math.Max(0, playback.ProgressMs),
            Math.Clamp(playback.Device?.VolumePercent ?? 0, 0, 100),
            playback.ShuffleEnabled,
            string.IsNullOrWhiteSpace(playback.RepeatMode) ? "off" : playback.RepeatMode,
            playback.IsPlaying,
            DateTimeOffset.UtcNow);
        lock (_spotifyAutomationSync) _spotifyAutomationSavedStates[group] = state;
        _spotifySavedStateSaveCount++;
        AddSpotifySavedStateHistory($"{group}: '{playback.Track.Name}' gespeichert");
        AddTimedAutomationDiagnostic($"Spotify-Gruppe '{group}': Wiedergabe '{playback.Track.Name}' gesichert.");
        Dispatcher.Invoke(() =>
        {
            RefreshSpotifySavedStateStatus();
            RefreshSpotifySavedStatesOverview();
        });
    }

    private async Task RestoreSpotifyAutomationStateAsync(string group, TimedAutomationRuleSettings rule, CancellationToken cancellationToken)
    {
        SpotifyAutomationSavedState? state;
        lock (_spotifyAutomationSync) _spotifyAutomationSavedStates.TryGetValue(group, out state);
        if (state is null)
            throw new InvalidOperationException($"Für die Spotify-Gruppe '{group}' wurde noch kein vorheriger Wiedergabezustand gesichert.");

        if (rule.SpotifyFadeSeconds > 0) await _spotifyModule.SetVolumeAsync(0, cancellationToken);
        await _spotifyModule.SetShuffleAsync(state.ShuffleEnabled, cancellationToken);
        await _spotifyModule.SetRepeatAsync(state.RepeatMode, cancellationToken);

        if (!string.IsNullOrWhiteSpace(state.ContextUri))
            await _spotifyModule.StartPlaylistAsync(state.ContextUri, applyConfiguredStartVolume: false, cancellationToken);
        else if (state.Track is not null)
            await _spotifyModule.PlayTrackAsync(state.Track, cancellationToken);

        if (state.ProgressMs > 0) await _spotifyModule.SeekAsync(state.ProgressMs, cancellationToken);

        var restoreVolumeRule = new TimedAutomationRuleSettings
        {
            SpotifyVolumePercent = state.VolumePercent,
            SpotifyFadeSeconds = rule.SpotifyFadeSeconds
        };
        await ApplySpotifyAutomationVolumeAsync(restoreVolumeRule, cancellationToken);
        if (!state.WasPlaying) await _spotifyModule.PauseAsync(cancellationToken);

        lock (_spotifyAutomationSync) _spotifyAutomationSavedStates.Remove(group);
        _spotifySavedStateRestoreCount++;
        AddSpotifySavedStateHistory($"{group}: Wiedergabe wiederhergestellt");
        AddTimedAutomationDiagnostic($"Spotify-Gruppe '{group}': Vorherige Wiedergabe wiederhergestellt.");
        Dispatcher.Invoke(() =>
        {
            RefreshSpotifySavedStateStatus();
            RefreshSpotifySavedStatesOverview();
        });
    }

    private async Task ApplySpotifyAutomationVolumeAsync(TimedAutomationRuleSettings rule, CancellationToken cancellationToken)
    {
        var target = Math.Clamp(rule.SpotifyVolumePercent, 0, 100);
        if (rule.SpotifyFadeSeconds <= 0)
        {
            await _spotifyModule.SetVolumeAsync(target, cancellationToken);
            return;
        }

        await _spotifyModule.RefreshPlaybackAsync(cancellationToken);
        var current = _spotifyModule.GetSnapshot().Playback.Device?.VolumePercent ?? 0;
        var steps = Math.Max(1, Math.Min(rule.SpotifyFadeSeconds * 4, 120));
        var delay = TimeSpan.FromMilliseconds(rule.SpotifyFadeSeconds * 1000d / steps);
        for (var step = 1; step <= steps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var volume = (int)Math.Round(current + ((target - current) * (step / (double)steps)));
            await _spotifyModule.SetVolumeAsync(Math.Clamp(volume, 0, 100), cancellationToken);
            if (step < steps) await Task.Delay(delay, cancellationToken);
        }
    }

    private void AddTimedAutomationDiagnostic(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {message}";
        _timedAutomationDiagnostics.Insert(0, line);
        while (_timedAutomationDiagnostics.Count > 100) _timedAutomationDiagnostics.RemoveAt(_timedAutomationDiagnostics.Count - 1);
    }

    private void ValidateTimedAutomationRules()
    {
        _timedAutomationDiagnostics.Clear();
        var ids = _timedAutomationRules.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var issues = 0;
        foreach (var rule in _timedAutomationRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Name)) { AddTimedAutomationDiagnostic("Hinweis: Eine Regel hat keinen Namen."); issues++; }
            if ((rule.TriggerType is "SceneElapsed" or "SceneActivated") && string.IsNullOrWhiteSpace(rule.TriggerScene)) { AddTimedAutomationDiagnostic($"Fehlt: Ausgangsszene bei '{rule.Name}'."); issues++; }
            if ((rule.TriggerType is "DailySchedule" or "WeeklySchedule" or "OneTimeSchedule") && !TimeOnly.TryParse(rule.ScheduleTime, out _)) { AddTimedAutomationDiagnostic($"Ungültige Uhrzeit bei '{rule.Name}'."); issues++; }
            if (rule.TriggerType == "WeeklySchedule" && string.IsNullOrWhiteSpace(rule.ScheduleDays)) { AddTimedAutomationDiagnostic($"Keine Wochentage bei '{rule.Name}'."); issues++; }
            if (rule.TriggerType == "OneTimeSchedule" && !DateOnly.TryParse(rule.ScheduleDate, out _)) { AddTimedAutomationDiagnostic($"Ungültiges einmaliges Datum bei '{rule.Name}'."); issues++; }
            if (DateOnly.TryParse(rule.ActiveFromDate, out var fromDate) && DateOnly.TryParse(rule.ActiveUntilDate, out var untilDate) && fromDate > untilDate) { AddTimedAutomationDiagnostic($"Aktivzeitraum ist umgekehrt bei '{rule.Name}'."); issues++; }
            foreach (var value in (rule.ExcludedDates ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) if (!DateOnly.TryParse(value, out _)) { AddTimedAutomationDiagnostic($"Ungültiger Ausnahmetag '{value}' bei '{rule.Name}'."); issues++; }
            foreach (var range in (rule.BlackoutRanges ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) { var bounds = range.Split("..", StringSplitOptions.TrimEntries); if (bounds.Length != 2 || !DateOnly.TryParse(bounds[0], out var blackoutStart) || !DateOnly.TryParse(bounds[1], out var blackoutEnd) || blackoutStart > blackoutEnd) { AddTimedAutomationDiagnostic($"Ungültiger Sperrzeitraum '{range}' bei '{rule.Name}'."); issues++; } }
            if (rule.ActionType == "SwitchScene" && string.IsNullOrWhiteSpace(rule.TargetScene)) { AddTimedAutomationDiagnostic($"Fehlt: Zielszene bei '{rule.Name}'."); issues++; }
            if (rule.ActionType == "SetSourceVisibility" && (string.IsNullOrWhiteSpace(rule.ObsScene) || string.IsNullOrWhiteSpace(rule.ObsSource))) { AddTimedAutomationDiagnostic($"Fehlt: Szene/Quelle bei '{rule.Name}'."); issues++; }
            if (rule.ActionType == "SetInputMute" && string.IsNullOrWhiteSpace(rule.ObsInput)) { AddTimedAutomationDiagnostic($"Fehlt: Audioquelle bei '{rule.Name}'."); issues++; }
            if (rule.ConditionType == "CurrentScene" && string.IsNullOrWhiteSpace(rule.ConditionValue)) { AddTimedAutomationDiagnostic($"Fehlt: Szenenname in Bedingung bei '{rule.Name}'."); issues++; }
            if (!string.IsNullOrWhiteSpace(rule.NextRuleId) && !ids.Contains(rule.NextRuleId)) { AddTimedAutomationDiagnostic($"Ungültige Folgeregel bei '{rule.Name}'."); issues++; }
            if (!string.IsNullOrWhiteSpace(rule.DependencyRuleId) && !ids.Contains(rule.DependencyRuleId)) { AddTimedAutomationDiagnostic($"Ungültige Abhängigkeitsregel bei '{rule.Name}'."); issues++; }
            if (!string.IsNullOrWhiteSpace(rule.FailureRuleId) && !ids.Contains(rule.FailureRuleId)) { AddTimedAutomationDiagnostic($"Ungültige Ersatzregel bei '{rule.Name}'."); issues++; }
            if (!string.IsNullOrWhiteSpace(rule.RollbackRuleId) && !ids.Contains(rule.RollbackRuleId)) { AddTimedAutomationDiagnostic($"Ungültige Rückabwicklungsregel bei '{rule.Name}'."); issues++; }
            if (rule.StartWorkflowGroup && string.IsNullOrWhiteSpace(rule.WorkflowGroup)) { AddTimedAutomationDiagnostic($"Workflow-Start ohne Gruppenname bei '{rule.Name}'."); issues++; }
            if (string.Equals(rule.DependencyRuleId, rule.Id, StringComparison.OrdinalIgnoreCase)) { AddTimedAutomationDiagnostic($"Selbstabhängigkeit bei '{rule.Name}'."); issues++; }
            if (string.Equals(rule.FailureRuleId, rule.Id, StringComparison.OrdinalIgnoreCase)) { AddTimedAutomationDiagnostic($"Ersatzregel verweist auf sich selbst bei '{rule.Name}'."); issues++; }
            if (string.Equals(rule.RollbackRuleId, rule.Id, StringComparison.OrdinalIgnoreCase)) { AddTimedAutomationDiagnostic($"Rückabwicklungsregel verweist auf sich selbst bei '{rule.Name}'."); issues++; }
        }
        foreach (var rule in _timedAutomationRules)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = rule;
            while (!string.IsNullOrWhiteSpace(current.NextRuleId))
            {
                if (!seen.Add(current.Id)) { AddTimedAutomationDiagnostic($"Schleife erkannt, beginnend bei '{rule.Name}'."); issues++; break; }
                current = _timedAutomationRules.FirstOrDefault(x => string.Equals(x.Id, current.NextRuleId, StringComparison.OrdinalIgnoreCase))!;
                if (current is null) break;
            }
        }
        if (issues == 0) AddTimedAutomationDiagnostic($"Prüfung abgeschlossen: {_timedAutomationRules.Count} Regeln, keine Fehler gefunden.");
        TimedAutomationTestStatusText.Text = issues == 0 ? "Alle Regeln sind gültig." : $"Regelprüfung: {issues} Hinweis(e).";
    }

    private void CancelPendingSceneAutomationExecutions()
    {
        // Szenen-Timer werden über den Aktivierungszeitpunkt neu gestartet.
    }

    private async Task ResetTimedAutomationsAtStreamEndAsync()
    {
        _executedTimedAutomationRuleIds.Clear();
        foreach (var rule in _settings.Workflow.TimedAutomations.Where(x => x.Enabled && x.ResetSourceAtStreamEnd))
        {
            if (!_obsClient.IsConnected || string.IsNullOrWhiteSpace(rule.ObsScene) || string.IsNullOrWhiteSpace(rule.ObsSource)) continue;
            try { await _obsClient.SetSceneItemEnabledAsync(rule.ObsScene, rule.ObsSource, rule.ResetSourceVisible); }
            catch (Exception ex) { _appLogger.Write(AppLogLevel.Warning, "Automation", $"Rücksetzen fehlgeschlagen ({rule.Name}): {ex.Message}"); }
        }
    }

    private sealed class TimedAutomationExportPackage
    {
        public string Format { get; set; } = "CreatorControlSuite.Automation";
        public int Version { get; set; } = 1;
        public DateTimeOffset ExportedAt { get; set; }
        public List<TimedAutomationRuleSettings> Rules { get; set; } = [];
    }

    private sealed record StreamerBotActionOption(string Id, string Name, string Group, bool Enabled)
    {
        public string DisplayName => $"{(Enabled ? "" : "[DEAKTIVIERT] ")}{Group} · {Name}";
    }

    private sealed record StreamerBotExecutionHistoryItem(DateTimeOffset Timestamp, string ActionName, bool Success, string Detail, string ArgumentsJson, string ResponseJson)
    {
        public string DisplayName => $"{Timestamp:HH:mm:ss} · {(Success ? "OK" : "FEHLER")} · {ActionName} · {Detail}";
    }

    private sealed record StreamerBotActionTemplate(string Name, string ActionId, string ActionName, string ArgumentsJson)
    {
        public string DisplayName => $"{Name} · {ActionName}";
    }


    private void RefreshWorkflowDesigner()
    {
        var groups = _timedAutomationRules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.WorkflowGroup))
            .Select(rule => rule.WorkflowGroup.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToList();

        var selected = WorkflowDesignerGroupBox.Text?.Trim() ?? "";
        WorkflowDesignerGroupBox.ItemsSource = groups;
        if (string.IsNullOrWhiteSpace(selected) && groups.Count > 0)
        {
            selected = groups[0];
            WorkflowDesignerGroupBox.SelectedItem = selected;
        }
        else
        {
            WorkflowDesignerGroupBox.Text = selected;
        }

        WorkflowDesignerCanvas.Children.Clear();
        if (string.IsNullOrWhiteSpace(selected))
        {
            WorkflowDesignerStatusText.Text = "Keine Workflow-Gruppe vorhanden. Weise Automatisierungsregeln zuerst einen Gruppennamen zu.";
            return;
        }

        var rules = _timedAutomationRules
            .Where(rule => string.Equals(rule.WorkflowGroup?.Trim(), selected, StringComparison.OrdinalIgnoreCase))
            .OrderBy(rule => rule.WorkflowOrder)
            .ThenBy(rule => rule.Name)
            .ToList();

        if (rules.Count == 0)
        {
            WorkflowDesignerStatusText.Text = $"Die Gruppe ‘{selected}’ enthält keine Regeln.";
            return;
        }

        for (var index = 0; index < rules.Count; index++)
        {
            var rule = rules[index];
            if (rule.DesignerX <= 0 && rule.DesignerY <= 0)
            {
                rule.DesignerX = 80 + index * 250;
                rule.DesignerY = 120;
            }
        }

        foreach (var rule in rules)
        {
            var next = ResolveWorkflowDesignerNextRule(rule, rules);
            if (next is not null)
            {
                DrawWorkflowDesignerConnection(rule, next, "Erfolg", Brushes.SeaGreen);
            }

            var failure = rules.FirstOrDefault(candidate => candidate.Id == rule.FailureRuleId);
            if (failure is not null)
            {
                DrawWorkflowDesignerConnection(rule, failure, "Fehler", Brushes.IndianRed);
            }
        }

        foreach (var rule in rules)
        {
            DrawWorkflowDesignerNode(rule);
        }

        WorkflowDesignerStatusText.Text = $"{rules.Count} Knoten in ‘{selected}’. Knoten können mit der Maus verschoben werden; Positionen werden beim Loslassen gespeichert.";
    }

    private TimedAutomationRuleSettings? ResolveWorkflowDesignerNextRule(TimedAutomationRuleSettings rule, IReadOnlyList<TimedAutomationRuleSettings> groupRules)
    {
        if (!string.IsNullOrWhiteSpace(rule.NextRuleId))
        {
            var explicitNext = groupRules.FirstOrDefault(candidate => candidate.Id == rule.NextRuleId);
            if (explicitNext is not null) return explicitNext;
        }

        return groupRules
            .Where(candidate => candidate.WorkflowOrder > rule.WorkflowOrder)
            .OrderBy(candidate => candidate.WorkflowOrder)
            .FirstOrDefault();
    }

    private void DrawWorkflowDesignerConnection(TimedAutomationRuleSettings from, TimedAutomationRuleSettings to, string label, Brush brush)
    {
        var x1 = from.DesignerX + 210;
        var y1 = from.DesignerY + 42;
        var x2 = to.DesignerX;
        var y2 = to.DesignerY + 42;
        var line = new System.Windows.Shapes.Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = brush,
            StrokeThickness = 3
        };
        WorkflowDesignerCanvas.Children.Add(line);

        var text = new TextBlock { Text = label, Foreground = brush, FontWeight = FontWeights.SemiBold, Background = Brushes.Black };
        Canvas.SetLeft(text, (x1 + x2) / 2 - 20);
        Canvas.SetTop(text, (y1 + y2) / 2 - 18);
        WorkflowDesignerCanvas.Children.Add(text);
    }

    private void DrawWorkflowDesignerNode(TimedAutomationRuleSettings rule)
    {
        var statusBrush = rule.LastRunStatus.Contains("Erfolg", StringComparison.OrdinalIgnoreCase)
            ? Brushes.SeaGreen
            : rule.LastRunStatus.Contains("Fehler", StringComparison.OrdinalIgnoreCase)
                ? Brushes.IndianRed
                : Brushes.DimGray;

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = rule.Name, FontWeight = FontWeights.Bold, FontSize = 14, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = $"{rule.ActionType} · Reihenfolge {rule.WorkflowOrder}", Foreground = Brushes.LightGray, Margin = new Thickness(0, 4, 0, 0) });
        panel.Children.Add(new TextBlock { Text = rule.LastRunStatus, Foreground = statusBrush, Margin = new Thickness(0, 5, 0, 0) });

        var border = new Border
        {
            Width = 210,
            MinHeight = 84,
            Background = new SolidColorBrush(Color.FromRgb(20, 29, 36)),
            BorderBrush = statusBrush,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12),
            Child = panel,
            Tag = rule,
            Cursor = Cursors.SizeAll
        };
        Canvas.SetLeft(border, rule.DesignerX);
        Canvas.SetTop(border, rule.DesignerY);

        Point dragOffset = default;
        border.MouseLeftButtonDown += (_, args) =>
        {
            dragOffset = args.GetPosition(border);
            border.CaptureMouse();
            args.Handled = true;
        };
        border.MouseMove += (_, args) =>
        {
            if (!border.IsMouseCaptured || args.LeftButton != MouseButtonState.Pressed) return;
            var position = args.GetPosition(WorkflowDesignerCanvas);
            Canvas.SetLeft(border, Math.Max(0, position.X - dragOffset.X));
            Canvas.SetTop(border, Math.Max(0, position.Y - dragOffset.Y));
        };
        border.MouseLeftButtonUp += async (_, args) =>
        {
            if (!border.IsMouseCaptured) return;
            border.ReleaseMouseCapture();
            rule.DesignerX = Canvas.GetLeft(border);
            rule.DesignerY = Canvas.GetTop(border);
            await _settingsStore.SaveAsync(_settings);
            RefreshWorkflowDesigner();
            args.Handled = true;
        };

        WorkflowDesignerCanvas.Children.Add(border);
    }

    private async Task AutoLayoutWorkflowDesignerAsync()
    {
        var group = WorkflowDesignerGroupBox.Text?.Trim() ?? "";
        var rules = _timedAutomationRules
            .Where(rule => string.Equals(rule.WorkflowGroup?.Trim(), group, StringComparison.OrdinalIgnoreCase))
            .OrderBy(rule => rule.WorkflowOrder)
            .ThenBy(rule => rule.Name)
            .ToList();
        for (var index = 0; index < rules.Count; index++)
        {
            rules[index].DesignerX = 70 + (index % 5) * 280;
            rules[index].DesignerY = 90 + (index / 5) * 170;
        }
        await _settingsStore.SaveAsync(_settings);
        RefreshWorkflowDesigner();
    }

    private void SetWorkflowDesignerZoom(double zoom)
    {
        zoom = Math.Clamp(zoom, 0.5, 2.0);
        WorkflowDesignerScale.ScaleX = zoom;
        WorkflowDesignerScale.ScaleY = zoom;
        ResetZoomWorkflowDesignerButton.Content = $"{zoom:P0}";
    }

    private void ValidateWorkflowDesigner()
    {
        var group = WorkflowDesignerGroupBox.Text?.Trim() ?? "";
        var rules = _timedAutomationRules
            .Where(rule => string.Equals(rule.WorkflowGroup?.Trim(), group, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var issues = new List<string>();
        if (rules.Count == 0) issues.Add("Die ausgewählte Gruppe enthält keine Regeln.");
        foreach (var duplicate in rules.GroupBy(rule => rule.WorkflowOrder).Where(g => g.Count() > 1))
            issues.Add($"Reihenfolge {duplicate.Key} ist mehrfach vergeben.");
        foreach (var rule in rules)
        {
            if (!string.IsNullOrWhiteSpace(rule.NextRuleId) && rules.All(candidate => candidate.Id != rule.NextRuleId))
                issues.Add($"{rule.Name}: Erfolgspfad zeigt außerhalb der Gruppe.");
            if (!string.IsNullOrWhiteSpace(rule.FailureRuleId) && rules.All(candidate => candidate.Id != rule.FailureRuleId))
                issues.Add($"{rule.Name}: Fehlerpfad zeigt außerhalb der Gruppe.");
        }
        WorkflowDesignerStatusText.Text = issues.Count == 0
            ? $"Graph ‘{group}’ ist gültig: {rules.Count} erreichbare Knoten, keine doppelten Reihenfolgen."
            : "Graphprüfung: " + string.Join(" | ", issues);
    }


    private async Task LoadRemoteObsConfigurationAsync()
    {
        var device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Kein Remote-Gerät ausgewählt."; return; }
        try
        {
            using var client = CreateTrustedAgentClient(device);
            client.DefaultRequestHeaders.Add("X-Agent-Key", device.AgentKey);
            var data = await client.GetFromJsonAsync<RemoteObsConfiguration>($"https://{device.Host}:{GetMultiPcAgentPort()}/api/obs/configuration");
            MultiPcObsProfilesBox.ItemsSource = data?.Profiles ?? [];
            MultiPcObsSceneCollectionsBox.ItemsSource = data?.SceneCollections ?? [];
            MultiPcObsProfilesBox.SelectedItem = data?.CurrentProfile;
            MultiPcObsSceneCollectionsBox.SelectedItem = data?.CurrentSceneCollection;
            MultiPcStatusText.Text = $"OBS-Konfiguration geladen: Profil {data?.CurrentProfile}, Sammlung {data?.CurrentSceneCollection}.";
        }
        catch (Exception ex) { MultiPcStatusText.Text = "OBS-Konfiguration konnte nicht geladen werden: " + ex.Message; }
    }

    private async Task ApplyRemoteObsConfigurationAsync(bool profile)
    {
        var device = GetSelectedRemoteDevice();
        if (device is null) return;
        var profileName = profile ? MultiPcObsProfilesBox.SelectedItem?.ToString() ?? "" : "";
        var collectionName = profile ? "" : MultiPcObsSceneCollectionsBox.SelectedItem?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(profileName) && string.IsNullOrWhiteSpace(collectionName)) return;
        try
        {
            using var client = CreateTrustedAgentClient(device);
            client.DefaultRequestHeaders.Add("X-Agent-Key", device.AgentKey);
            var response = await client.PostAsJsonAsync($"https://{device.Host}:{GetMultiPcAgentPort()}/api/obs/configuration", new { ProfileName = profileName, SceneCollectionName = collectionName });
            response.EnsureSuccessStatusCode();
            MultiPcStatusText.Text = profile ? $"OBS-Profil aktiviert: {profileName}" : $"OBS-Szenensammlung aktiviert: {collectionName}";
            await Task.Delay(750);
            await RefreshRemoteObsStateAsync();
        }
        catch (Exception ex) { MultiPcStatusText.Text = "OBS-Konfiguration konnte nicht aktiviert werden: " + ex.Message; }
    }

    private async Task LoadRemoteObsPresetsAsync()
    {
        var device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        try
        {
            using var client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/obs/presets");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            using var response = await client.SendAsync(request);
            var presets = await response.Content.ReadFromJsonAsync<RemoteObsPresetInfo[]>();
            if (!response.IsSuccessStatusCode) { MultiPcStatusText.Text = "OBS-Presets konnten nicht geladen werden."; return; }
            MultiPcObsPresetsBox.ItemsSource = presets?.Select(x => x.Name + " · " + x.CreatedAt.LocalDateTime.ToString("g")).ToArray() ?? [];
            if (MultiPcObsPresetsBox.Items.Count > 0) MultiPcObsPresetsBox.SelectedIndex = 0;
            MultiPcStatusText.Text = $"{presets?.Length ?? 0} Remote-OBS-Preset(s) geladen.";
        }
        catch (Exception ex) { MultiPcStatusText.Text = "OBS-Presets konnten nicht geladen werden: " + ex.Message; }
    }

    private async Task SaveRemoteObsPresetAsync()
    {
        var name = MultiPcObsPresetNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) { MultiPcStatusText.Text = "Bitte einen Preset-Namen eingeben."; return; }
        await PostRemoteObsAsync("presets/save", new { name }, $"OBS-Preset „{name}“ wurde gespeichert");
        await LoadRemoteObsPresetsAsync();
    }

    private string? SelectedRemoteObsPresetName() => MultiPcObsPresetsBox.SelectedItem?.ToString()?.Split(" · ", StringSplitOptions.None)[0];

    private async Task ApplyRemoteObsPresetAsync()
    {
        var name = SelectedRemoteObsPresetName();
        if (string.IsNullOrWhiteSpace(name)) { MultiPcStatusText.Text = "Bitte ein OBS-Preset auswählen."; return; }
        await PostRemoteObsAsync("presets/apply", new { name }, $"OBS-Preset „{name}“ wurde wiederhergestellt");
        await Task.Delay(750);
        await RefreshRemoteObsStateAsync();
        await LoadRemoteObsConfigurationAsync();
    }

    private async Task DeleteRemoteObsPresetAsync()
    {
        var name = SelectedRemoteObsPresetName();
        if (string.IsNullOrWhiteSpace(name)) { MultiPcStatusText.Text = "Bitte ein OBS-Preset auswählen."; return; }
        await PostRemoteObsAsync("presets/delete", new { name }, $"OBS-Preset „{name}“ wurde gelöscht");
        await LoadRemoteObsPresetsAsync();
    }

    private async Task LoadRemoteAgentLogsAsync()
    {
        var device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        try
        {
            using var client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/logs?lines=500");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            using var response = await client.SendAsync(request);
            var lines = await response.Content.ReadFromJsonAsync<string[]>();
            MultiPcAgentLogsBox.Text = response.IsSuccessStatusCode ? string.Join(Environment.NewLine, lines ?? []) : await response.Content.ReadAsStringAsync();
            MultiPcStatusText.Text = response.IsSuccessStatusCode ? $"{lines?.Length ?? 0} Agent-Logzeilen geladen." : "Agent-Logs konnten nicht geladen werden.";
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Agent-Logs konnten nicht geladen werden: " + ex.Message; }
    }

    private async Task DeployRemotePackageAsync(string endpoint, string title, string successText)
    {
        var device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        var requiredPermission = endpoint.StartsWith("update", StringComparison.OrdinalIgnoreCase) ? "updates.stage" : "files.deploy";
        if (!(device.AllowedCommands ?? []).Contains(requiredPermission, StringComparer.OrdinalIgnoreCase))
        { MultiPcStatusText.Text = $"Der Agent hat die Berechtigung {requiredPermission} nicht freigegeben."; return; }
        var dialog = new OpenFileDialog { Title = title, Filter = "ZIP-Archive (*.zip)|*.zip", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var bytes = await File.ReadAllBytesAsync(dialog.FileName);
            if (bytes.Length > 100 * 1024 * 1024) { MultiPcStatusText.Text = "Das Paket ist größer als 100 MB und wurde nicht übertragen."; return; }
            using var client = CreateTrustedMultiPcClient(device);
            client.Timeout = TimeSpan.FromMinutes(5);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/{endpoint}");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            request.Content = System.Net.Http.Json.JsonContent.Create(new { fileName = Path.GetFileName(dialog.FileName), base64Zip = Convert.ToBase64String(bytes) });
            using var response = await client.SendAsync(request);
            var result = await response.Content.ReadAsStringAsync();
            MultiPcStatusText.Text = response.IsSuccessStatusCode ? $"{device.Name}: {successText}." : "Remote-Dateifehler: " + result;
            AddMultiPcHistory(device.Name, endpoint, response.IsSuccessStatusCode ? "erfolgreich" : "Fehler");
            if (response.IsSuccessStatusCode) await LoadRemoteAgentLogsAsync();
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Paket konnte nicht übertragen werden: " + ex.Message; }
    }

    private async Task LoadRemoteUpdateStatusAsync()
    {
        var device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        try
        {
            using var client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/update/status");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            using var response = await client.SendAsync(request);
            var state = await response.Content.ReadFromJsonAsync<RemoteUpdateState>();
            if (!response.IsSuccessStatusCode || state is null) { MultiPcUpdateStatusText.Text = "Update-Status konnte nicht geladen werden."; return; }
            MultiPcUpdateStatusText.Text = string.Join(Environment.NewLine,
                $"Status: {state.Status} · Paket: {state.PackageName}",
                $"Bereitgestellt: {(state.StagedAt == DateTimeOffset.MinValue ? "-" : state.StagedAt.LocalDateTime.ToString("g"))}",
                $"Backup: {(string.IsNullOrWhiteSpace(state.BackupDirectory) ? "-" : state.BackupDirectory)}",
                $"Prüfsumme: {(string.IsNullOrWhiteSpace(state.Sha256) ? "-" : state.Sha256)}",
                $"Dateien: {state.FileCount} · Paketversion: {state.PackageVersion} · Mindest-Agent: {state.MinimumAgentVersion}",
                $"Manifest-Signatur: {(state.SignatureValid ? "gültig" : state.Validated ? "ungültig" : "noch nicht geprüft")} · Validiert: {(state.Validated ? "ja" : "nein")} · Wartungsmodus: {(state.MaintenanceMode ? "aktiv" : "aus")}",
                state.Message);
            MultiPcStatusText.Text = "Remote-Update-Status wurde geladen.";
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Update-Status konnte nicht geladen werden: " + ex.Message; }
    }


    private async Task LoadRemoteUpdateHistoryAsync()
    {
        var device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        try
        {
            using var client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/update/history");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            using var response = await client.SendAsync(request);
            var history = await response.Content.ReadFromJsonAsync<RemoteUpdateHistoryEntry[]>();
            if (!response.IsSuccessStatusCode || history is null) { MultiPcStatusText.Text = "Update-Historie konnte nicht geladen werden."; return; }
            MultiPcUpdateHistoryList.ItemsSource = history.Select(entry => $"{entry.At.LocalDateTime:g} · {entry.Action} · {entry.PackageVersion} · {(entry.Success ? "OK" : "Fehler")} · {entry.Message}").ToArray();
            MultiPcStatusText.Text = $"{history.Length} Update-Einträge geladen.";
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Update-Historie konnte nicht geladen werden: " + ex.Message; }
    }

    private async Task ExecuteRemoteUpdateActionAsync(string action)
    {
        var device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        if (!(device.AllowedCommands ?? []).Contains("updates.apply", StringComparer.OrdinalIgnoreCase))
        { MultiPcStatusText.Text = "Der Agent hat die Berechtigung updates.apply nicht freigegeben."; return; }
        try
        {
            using var client = CreateTrustedMultiPcClient(device);
            client.Timeout = TimeSpan.FromMinutes(2);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/update/{action}");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            request.Content = System.Net.Http.Json.JsonContent.Create(new { restartSuite = MultiPcRestartSuiteAfterUpdateCheckBox.IsChecked == true, automaticRollback = MultiPcAutomaticRollbackCheckBox.IsChecked == true });
            using var response = await client.SendAsync(request);
            var result = await response.Content.ReadAsStringAsync();
            MultiPcStatusText.Text = response.IsSuccessStatusCode
                ? action == "apply" ? "Remote-Update wird angewendet. Die Verbindung zum Agent kann kurz abbrechen." : action == "validate" ? "Remote-Updatepaket wurde geprüft." : "Remote-Rollback wird angewendet. Die Verbindung zum Agent kann kurz abbrechen."
                : "Remote-Updatefehler: " + result;
            AddMultiPcHistory(device.Name, "update/" + action, response.IsSuccessStatusCode ? "gestartet" : "Fehler");
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Remote-Updateaktion konnte nicht gestartet werden: " + ex.Message; }
    }


    private async Task StartRemoteUpdateRolloutAsync(string? scheduledPackagePath = null)
    {
        if (_multiPcRolloutCts is not null)
        {
            MultiPcStatusText.Text = "Es läuft bereits ein Update-Rollout.";
            return;
        }
        var selectedGroup = (MultiPcRolloutTargetGroupBox.Text ?? "Alle").Trim();
        var targets = _multiPcDevices
            .Where(device => (device.AllowedCommands ?? []).Contains("updates.stage", StringComparer.OrdinalIgnoreCase)
                          && (device.AllowedCommands ?? []).Contains("updates.apply", StringComparer.OrdinalIgnoreCase))
            .Where(device => string.IsNullOrWhiteSpace(selectedGroup) || selectedGroup.Equals("Alle", StringComparison.OrdinalIgnoreCase)
                          || (_multiPcRolloutGroups.TryGetValue(device.Id, out var group) && group.Equals(selectedGroup, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (targets.Length == 0)
        {
            MultiPcStatusText.Text = "Für die gewählte Rollout-Gruppe wurde kein geeigneter Agent gefunden.";
            return;
        }
        var packagePath = scheduledPackagePath;
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            var dialog = new OpenFileDialog { Title = "Update-ZIP für gestaffelten Rollout auswählen", Filter = "ZIP-Archive (*.zip)|*.zip", CheckFileExists = true, Multiselect = false };
            if (dialog.ShowDialog() != true) return;
            packagePath = dialog.FileName;
        }
        if (!File.Exists(packagePath)) { MultiPcStatusText.Text = "Das ausgewählte Update-Paket wurde nicht gefunden."; return; }
        var bytes = await File.ReadAllBytesAsync(packagePath);
        if (bytes.Length > 100 * 1024 * 1024)
        {
            MultiPcStatusText.Text = "Das Update-Paket ist größer als 100 MB.";
            return;
        }
        var delaySeconds = int.TryParse(MultiPcRolloutDelayBox.Text, out var parsedDelay) ? Math.Clamp(parsedDelay, 0, 600) : 20;
        var canaryCount = int.TryParse(MultiPcCanaryCountBox.Text, out var parsedCanary) ? Math.Clamp(parsedCanary, 0, targets.Length) : Math.Min(1, targets.Length);
        var maxFailurePercent = int.TryParse(MultiPcMaxFailurePercentBox.Text, out var parsedFailure) ? Math.Clamp(parsedFailure, 0, 100) : 25;
        var stopOnThreshold = MultiPcStopOnFailureThresholdCheckBox.IsChecked == true;
        _multiPcRolloutCts = new CancellationTokenSource();
        var token = _multiPcRolloutCts.Token;
        _multiPcRolloutItems.Clear();
        MultiPcStartRolloutButton.IsEnabled = false;
        MultiPcStatusText.Text = $"Rollout '{selectedGroup}' an {targets.Length} Remote-PC(s) gestartet · Canary: {canaryCount}.";
        var attempted = 0;
        var succeeded = 0;
        var failed = 0;
        try
        {
            for (var index = 0; index < targets.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                await WaitForMaintenanceWindowAsync(token);
                var device = targets[index];
                var phase = index < canaryCount ? "CANARY" : "ROLLOUT";
                UpdateRolloutLine(device.Name, $"{phase} · Paket wird übertragen …");
                attempted++;
                var staged = await StageUpdateForRolloutAsync(device, packagePath, bytes, token);
                if (!staged)
                {
                    failed++;
                    UpdateRolloutLine(device.Name, $"{phase} · FEHLER beim Bereitstellen");
                }
                else
                {
                    UpdateRolloutLine(device.Name, $"{phase} · Paket wird validiert …");
                    var validated = await SendRolloutUpdateActionAsync(device, "validate", token);
                    if (!validated)
                    {
                        failed++;
                        UpdateRolloutLine(device.Name, $"{phase} · FEHLER bei Validierung");
                    }
                    else
                    {
                        UpdateRolloutLine(device.Name, $"{phase} · Installation wird gestartet …");
                        var applied = await SendRolloutUpdateActionAsync(device, "apply", token);
                        if (applied) succeeded++; else failed++;
                        UpdateRolloutLine(device.Name, applied ? $"{phase} · Installation gestartet" : $"{phase} · FEHLER beim Installationsstart");
                        AddMultiPcHistory(device.Name, "rollout", applied ? $"{phase}: Installation gestartet" : $"{phase}: Fehler");
                    }
                }

                var failurePercent = attempted == 0 ? 0 : (int)Math.Round(failed * 100d / attempted);
                MultiPcStatusText.Text = $"Rollout läuft · {attempted}/{targets.Length} bearbeitet · {succeeded} erfolgreich · {failed} Fehler ({failurePercent} %).";
                if (stopOnThreshold && failed > 0 && failurePercent > maxFailurePercent)
                {
                    MultiPcStatusText.Text = $"Rollout automatisch gestoppt: Fehlerquote {failurePercent} % überschreitet Grenzwert {maxFailurePercent} %.";
                    AddMultiPcHistory("Rollout", selectedGroup, $"Automatischer Stopp bei {failurePercent} % Fehlerquote");
                    break;
                }
                if (canaryCount > 0 && index + 1 == canaryCount && failed > 0)
                {
                    MultiPcStatusText.Text = $"Canary-Phase beendet: {failed} Fehler. Der weitere Rollout wurde aus Sicherheitsgründen gestoppt.";
                    AddMultiPcHistory("Rollout", selectedGroup, "Canary-Stopp");
                    break;
                }
                if (index < targets.Length - 1 && delaySeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
            }
            if (attempted == targets.Length && failed == 0)
                MultiPcStatusText.Text = $"Rollout erfolgreich abgeschlossen: {succeeded}/{targets.Length} Geräte.";
            else if (attempted == targets.Length)
                MultiPcStatusText.Text = $"Rollout abgeschlossen: {succeeded} erfolgreich, {failed} fehlgeschlagen.";
        }
        catch (OperationCanceledException)
        {
            MultiPcStatusText.Text = "Update-Rollout wurde abgebrochen. Bereits gestartete Installationen laufen weiter.";
        }
        catch (Exception ex)
        {
            MultiPcStatusText.Text = "Update-Rollout ist fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            _multiPcRolloutCts.Dispose();
            _multiPcRolloutCts = null;
            MultiPcStartRolloutButton.IsEnabled = true;
        }
    }


    private async Task ScheduleRemoteUpdateRolloutAsync()
    {
        if (_scheduledMultiPcRolloutCts is not null) { MultiPcStatusText.Text = "Es ist bereits ein Rollout geplant."; return; }
        var when = ParseRolloutSchedule(MultiPcRolloutScheduleBox.Text);
        if (when is null || when <= DateTimeOffset.Now) { MultiPcStatusText.Text = "Bitte einen zukünftigen Zeitpunkt eingeben, z. B. 'morgen 02:00' oder '21.07.2026 02:00'."; return; }
        var dialog = new OpenFileDialog { Title = "Update-ZIP für geplanten Rollout auswählen", Filter = "ZIP-Archive (*.zip)|*.zip", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog() != true) return;
        Directory.CreateDirectory(MultiPcScheduledPackagesDirectory);
        var storedPackagePath = Path.Combine(MultiPcScheduledPackagesDirectory, $"{when.Value:yyyyMMdd-HHmmss}-{Path.GetFileName(dialog.FileName)}");
        File.Copy(dialog.FileName, storedPackagePath, true);
        var job = CaptureScheduledRolloutJob(when.Value, storedPackagePath);
        SaveScheduledRolloutJob(job);
        AddMultiPcHistory("Rollout", job.TargetGroup, $"geplant für {job.ScheduledAt.LocalDateTime:g}");
        await StartScheduledRolloutWaitAsync(job);
    }

    private ScheduledMultiPcRolloutJob CaptureScheduledRolloutJob(DateTimeOffset when, string packagePath) => new(
        when, packagePath, (MultiPcRolloutTargetGroupBox.Text ?? "Alle").Trim(), MultiPcRolloutDelayBox.Text ?? "20",
        MultiPcCanaryCountBox.Text ?? "1", MultiPcMaxFailurePercentBox.Text ?? "25", MultiPcStopOnFailureThresholdCheckBox.IsChecked == true,
        MultiPcUseMaintenanceWindowCheckBox.IsChecked == true, MultiPcMaintenanceStartBox.Text ?? "02:00", MultiPcMaintenanceEndBox.Text ?? "05:00");

    private void SaveScheduledRolloutJob(ScheduledMultiPcRolloutJob job)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MultiPcScheduledRolloutPath)!);
        File.WriteAllText(MultiPcScheduledRolloutPath, System.Text.Json.JsonSerializer.Serialize(job, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task RestoreScheduledRemoteUpdateRolloutAsync()
    {
        if (MultiPcResumePausedRolloutCheckBox.IsChecked != true || _scheduledMultiPcRolloutCts is not null || !File.Exists(MultiPcScheduledRolloutPath)) return;
        try
        {
            var job = System.Text.Json.JsonSerializer.Deserialize<ScheduledMultiPcRolloutJob>(File.ReadAllText(MultiPcScheduledRolloutPath));
            if (job is null || !File.Exists(job.PackagePath)) { File.Delete(MultiPcScheduledRolloutPath); return; }
            ApplyScheduledRolloutJobToUi(job);
            AddMultiPcHistory("Rollout", job.TargetGroup, "Planung nach Suite-Neustart wiederhergestellt");
            await StartScheduledRolloutWaitAsync(job);
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Gespeicherter Rollout konnte nicht wiederhergestellt werden: " + ex.Message; }
    }

    private void ApplyScheduledRolloutJobToUi(ScheduledMultiPcRolloutJob job)
    {
        MultiPcRolloutTargetGroupBox.Text = job.TargetGroup;
        MultiPcRolloutDelayBox.Text = job.DelaySeconds;
        MultiPcCanaryCountBox.Text = job.CanaryCount;
        MultiPcMaxFailurePercentBox.Text = job.MaxFailurePercent;
        MultiPcStopOnFailureThresholdCheckBox.IsChecked = job.StopOnFailureThreshold;
        MultiPcUseMaintenanceWindowCheckBox.IsChecked = job.UseMaintenanceWindow;
        MultiPcMaintenanceStartBox.Text = job.MaintenanceStart;
        MultiPcMaintenanceEndBox.Text = job.MaintenanceEnd;
    }

    private async Task StartScheduledRolloutWaitAsync(ScheduledMultiPcRolloutJob job)
    {
        _scheduledMultiPcRolloutCts = new CancellationTokenSource();
        var token = _scheduledMultiPcRolloutCts.Token;
        MultiPcScheduledRolloutStatusText.Text = $"Gespeichert: {job.ScheduledAt.LocalDateTime:g} · {Path.GetFileName(job.PackagePath)}";
        MultiPcStatusText.Text = "Der Update-Rollout wurde dauerhaft geplant.";
        try
        {
            var delay = job.ScheduledAt - DateTimeOffset.Now;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
            MultiPcScheduledRolloutStatusText.Text = "Planung wird jetzt ausgeführt …";
            ApplyScheduledRolloutJobToUi(job);
            await StartRemoteUpdateRolloutAsync(job.PackagePath);
            AddMultiPcHistory("Rollout", job.TargetGroup, "gespeicherter Auftrag ausgeführt");
            if (File.Exists(MultiPcScheduledRolloutPath)) File.Delete(MultiPcScheduledRolloutPath);
        }
        catch (OperationCanceledException) { MultiPcScheduledRolloutStatusText.Text = "Kein Rollout geplant."; }
        finally { _scheduledMultiPcRolloutCts?.Dispose(); _scheduledMultiPcRolloutCts = null; }
    }

    private void CancelScheduledRemoteUpdateRollout()
    {
        if (_scheduledMultiPcRolloutCts is null) { MultiPcStatusText.Text = "Aktuell ist kein Rollout geplant."; return; }
        _scheduledMultiPcRolloutCts.Cancel();
        try { if (File.Exists(MultiPcScheduledRolloutPath)) File.Delete(MultiPcScheduledRolloutPath); } catch { }
        AddMultiPcHistory("Rollout", "Planung", "aufgehoben");
        MultiPcStatusText.Text = "Die Rollout-Planung wurde aufgehoben.";
    }

    private DateTimeOffset? ParseRolloutSchedule(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.StartsWith("morgen ", StringComparison.OrdinalIgnoreCase) && TimeOnly.TryParse(text[7..], out var tomorrowTime))
            return new DateTimeOffset(DateTime.Today.AddDays(1).Add(tomorrowTime.ToTimeSpan()));
        if (DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var parsed)) return parsed;
        return null;
    }

    private async Task WaitForMaintenanceWindowAsync(CancellationToken token)
    {
        if (MultiPcUseMaintenanceWindowCheckBox.IsChecked != true) return;
        if (!TimeOnly.TryParse(MultiPcMaintenanceStartBox.Text, out var start) || !TimeOnly.TryParse(MultiPcMaintenanceEndBox.Text, out var end)) return;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var now = TimeOnly.FromDateTime(DateTime.Now);
            var inside = start <= end ? now >= start && now <= end : now >= start || now <= end;
            if (inside) return;
            MultiPcStatusText.Text = $"Rollout pausiert: Wartungsfenster {start:HH\\:mm}–{end:HH\\:mm}. Automatische Fortsetzung folgt.";
            await Task.Delay(TimeSpan.FromSeconds(30), token);
        }
    }

    private void LoadMultiPcRolloutGroups()
    {
        try
        {
            if (!File.Exists(MultiPcRolloutGroupsPath)) return;
            var values = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(MultiPcRolloutGroupsPath));
            if (values is null) return;
            _multiPcRolloutGroups.Clear();
            foreach (var pair in values.Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value)))
                _multiPcRolloutGroups[pair.Key] = pair.Value.Trim();
        }
        catch { }
    }

    private void SaveMultiPcRolloutGroups()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MultiPcRolloutGroupsPath)!);
            File.WriteAllText(MultiPcRolloutGroupsPath, System.Text.Json.JsonSerializer.Serialize(_multiPcRolloutGroups, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Rollout-Gruppen konnten nicht gespeichert werden: " + ex.Message; }
    }

    private void RefreshMultiPcRolloutGroupChoices()
    {
        var current = MultiPcRolloutTargetGroupBox.Text;
        var groups = new[] { "Alle" }.Concat(_multiPcRolloutGroups.Values.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase)).ToArray();
        MultiPcRolloutTargetGroupBox.ItemsSource = groups;
        MultiPcRolloutTargetGroupBox.Text = string.IsNullOrWhiteSpace(current) ? "Alle" : current;
    }

    private void AssignSelectedDeviceToRolloutGroup()
    {
        var device = GetSelectedRemoteDevice();
        if (device is null)
        {
            MultiPcStatusText.Text = "Bitte zuerst einen Remote-PC auswählen.";
            return;
        }
        var group = (MultiPcDeviceRolloutGroupBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(group))
        {
            _multiPcRolloutGroups.Remove(device.Id);
            MultiPcStatusText.Text = $"{device.Name} wurde aus seiner Rollout-Gruppe entfernt.";
        }
        else
        {
            _multiPcRolloutGroups[device.Id] = group;
            MultiPcStatusText.Text = $"{device.Name} wurde der Rollout-Gruppe '{group}' zugeordnet.";
        }
        SaveMultiPcRolloutGroups();
        RefreshMultiPcRolloutGroupChoices();
    }

    private void CancelRemoteUpdateRollout()
    {
        if (_multiPcRolloutCts is null)
        {
            MultiPcStatusText.Text = "Aktuell läuft kein Rollout.";
            return;
        }
        _multiPcRolloutCts.Cancel();
    }

    private async Task<bool> StageUpdateForRolloutAsync(MultiPcDeviceRecord device, string filePath, byte[] bytes, CancellationToken token)
    {
        try
        {
            using var client = CreateTrustedMultiPcClient(device);
            client.Timeout = TimeSpan.FromMinutes(5);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/update/stage");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            request.Content = System.Net.Http.Json.JsonContent.Create(new { fileName = Path.GetFileName(filePath), base64Zip = Convert.ToBase64String(bytes) });
            using var response = await client.SendAsync(request, token);
            return response.IsSuccessStatusCode;
        }
        catch when (!token.IsCancellationRequested) { return false; }
    }

    private async Task<bool> SendRolloutUpdateActionAsync(MultiPcDeviceRecord device, string action, CancellationToken token)
    {
        try
        {
            using var client = CreateTrustedMultiPcClient(device);
            client.Timeout = TimeSpan.FromMinutes(2);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/update/{action}");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            request.Content = System.Net.Http.Json.JsonContent.Create(new { restartSuite = MultiPcRestartSuiteAfterUpdateCheckBox.IsChecked == true, automaticRollback = MultiPcAutomaticRollbackCheckBox.IsChecked == true });
            using var response = await client.SendAsync(request, token);
            return response.IsSuccessStatusCode;
        }
        catch when (!token.IsCancellationRequested) { return false; }
    }

    private void UpdateRolloutLine(string deviceName, string status)
    {
        var prefix = deviceName + " · ";
        var existing = _multiPcRolloutItems.FirstOrDefault(item => item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        var line = $"{deviceName} · {status}";
        if (existing is null) _multiPcRolloutItems.Add(line);
        else _multiPcRolloutItems[_multiPcRolloutItems.IndexOf(existing)] = line;
    }

    private async Task ExecuteUiActionAsync(Button button, string actionName, Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(action);

        var wasEnabled = button.IsEnabled;
        try
        {
            button.IsEnabled = false;
            await action();
        }
        catch (Exception exception)
        {
            ShowError(actionName, exception);
        }
        finally
        {
            button.IsEnabled = wasEnabled;
        }
    }

    private void ShowError(string title, Exception exception)
    {
        var safeTitle = string.IsNullOrWhiteSpace(title) ? "Fehler" : title.Trim();
        _appLogger.Write(AppLogLevel.Error, "UI", $"{safeTitle}: {exception.Message}", exception);
        MessageBox.Show(exception.Message, safeTitle, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private sealed record StreamerBotLiveEventItem(DateTimeOffset Timestamp, string Source, string Type, string Summary)
    {
        public string DisplayName => $"{Timestamp:HH:mm:ss} · {Source} / {Type} · {Summary}";
    }

    private sealed record RemoteUpdateHistoryEntry(DateTimeOffset At, string Action, string PackageVersion, string Sha256, bool Success, string Message);
    private sealed record ScheduledMultiPcRolloutJob(DateTimeOffset ScheduledAt, string PackagePath, string TargetGroup, string DelaySeconds, string CanaryCount, string MaxFailurePercent, bool StopOnFailureThreshold, bool UseMaintenanceWindow, string MaintenanceStart, string MaintenanceEnd);
    private sealed record MultiPcRolloutAuditEntry(DateTimeOffset Timestamp, string Device, string Action, string Result);

    private sealed record RemoteUpdateState(string Status, string PackageName, string StagingDirectory, string PackageDirectory, string BackupDirectory, DateTimeOffset StagedAt, DateTimeOffset? AppliedAt, string Message, string Sha256, int FileCount, bool Validated, bool MaintenanceMode, bool? AutomaticRollback, string PackageVersion, string MinimumAgentVersion, string ManifestSignature, bool SignatureValid);

    private sealed record RemoteObsPresetInfo(string Name, DateTimeOffset CreatedAt, string ProfileName, string SceneCollectionName, string CurrentScene);

    private sealed record RemoteObsConfiguration(string CurrentProfile, string[] Profiles, string CurrentSceneCollection, string[] SceneCollections);

    private sealed record TwitchRewardRedemptionItem(TwitchRewardRedemption Redemption)
    {
        public string DisplayText => $"{Redemption.UserDisplayName} · {Redemption.RewardTitle}" +
            (string.IsNullOrWhiteSpace(Redemption.UserInput) ? "" : $" · {Redemption.UserInput}");
    }

}
