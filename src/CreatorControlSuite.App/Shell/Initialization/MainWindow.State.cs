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
    private readonly SemaphoreSlim _spotifyOverlayWriteLock = new(1, 1);
    private readonly SpotifyListeningStatisticsService _spotifyListeningStatistics = new();
    private readonly SpotifyAutomationLogService _spotifyAutomationLog = new();
    private readonly SemaphoreSlim _spotifyAutomationLock = new(1, 1);
    private DateTimeOffset _lastSpotifyHealthRecoveryAt = DateTimeOffset.MinValue;
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
    private readonly ISettingsStore _settingsStore;
    private readonly ISecretStore _secretStore;
    private readonly IObsWebSocketClient _obsClient;
    private readonly TwitchModule _twitchModule;
    private readonly SpotifyModule _spotifyModule;
    private readonly YouTubeMusicModule _youTubeMusicModule;
    private readonly IMusicPlayerRouter _musicPlayerRouter;
    private readonly IMusicPlayerUiPresenter _musicPlayerUiPresenter;
    private readonly IMultiPcAgentClient _multiPcAgentClient;
    private readonly IMultiPcPairingClient _multiPcPairingClient;
    private readonly RemoteUpdateRolloutService _remoteUpdateRolloutService;
    private readonly IStreamerBotClient _streamerBotClient;
    private readonly INavigationService _navigationService;
    private readonly IEventBus _eventBus;
    private IDisposable? _timedAutomationTickSubscription;
    private readonly DiagnosticsPageViewModel _diagnosticsPageViewModel;
    private readonly ProfilesPageViewModel _profilesPageViewModel;
    private readonly AboutPageViewModel _aboutPageViewModel;
    private readonly MusicPlayerPageViewModel _musicPlayerPageViewModel;
    private readonly UpdatePageViewModel _updatePageViewModel;
    private readonly MigrationPageViewModel _migrationPageViewModel;
    private readonly GeneralSettingsPageViewModel _generalSettingsPageViewModel;
    private readonly TwitchGoalsPageViewModel _twitchGoalsPageViewModel;
    private readonly SpotifyAutomationPageViewModel _spotifyAutomationPageViewModel;
    private readonly WorkflowSessionPageViewModel _workflowSessionPageViewModel;
    private readonly OverlayConnectionSettingsPageViewModel
        _overlayConnectionSettingsPageViewModel;
    private readonly OverlayCanvasPageViewModel _overlayCanvasPageViewModel;
    private readonly OverlayExtensionPacksPageViewModel
        _overlayExtensionPacksPageViewModel;
    private readonly AlertLibraryPageViewModel _alertLibraryPageViewModel;
    private readonly AlertDefinitionEditorViewModel
        _alertDefinitionEditorViewModel;
    private readonly AlertRuntimePageViewModel _alertRuntimePageViewModel;
    private readonly StatisticsPageViewModel _statisticsPageViewModel;
    private readonly CreatorIntelligenceService _creatorIntelligence;
    private readonly AlertsModule _alertsModule;
    private readonly OverlayModule _overlayModule;
    private readonly IOverlayRealtimeHub _overlayRealtimeHub;
    private readonly IChatEmoteCatalog _chatEmoteCatalog;
    private readonly IChatBadgeCatalog _chatBadgeCatalog;
    private readonly ITwitchApiClient _twitchApiClient;
    private readonly WorkflowModule _workflowModule;
    private readonly IProfileService _profileService;
    private readonly StreamDeckModule _streamDeckModule;
    private readonly IAppLogger _appLogger;
    private readonly SettingsApplicationService _settingsApplicationService;
    private readonly RuntimeHealthService _runtimeHealthService;
    private readonly ICrashReporter _crashReporter;
    private readonly ILocalIpcServer _ipcServer;
    private readonly ISupportPackageService _supportPackageService;
    private readonly IReleaseReadinessService _releaseReadinessService;
    private readonly IWorkflowE2eService _workflowE2eService;
    private readonly IInstallerSelfTestService _installerSelfTestService;
    private readonly IBetaReadinessService _betaReadinessService;
    private readonly IThemeService _themeService;
    private readonly ObservableCollection<AppLogEntry> _visibleLogs = [];
    private readonly ObservableCollection<SpotifyApiInspectorRow> _spotifyInspectorRows = [];
    private bool _logsPaused;
    private readonly System.Windows.Threading.DispatcherTimer _alertAudioPreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private bool _updatingAlertAudioTrimUi;
    private readonly ObservableCollection<string> _twitchChatItems = [];
    private string? _lastTwitchWebChatUrl;
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
    private DateTimeOffset? _streamSessionStartedAt;
    private DateTimeOffset? _twitchStreamStartedAt;
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
    private readonly System.Windows.Threading.DispatcherTimer _spotifySavedStateCleanupTimer = new() { Interval = TimeSpan.FromMinutes(15) };
    private DateTimeOffset? _automationSceneActivatedAt;
    private string _automationCurrentScene = "";
    private CancellationTokenSource? _timedAutomationTestCts;
    private bool _timedAutomationEvaluationRunning;
    private DateTimeOffset _lastTimedAutomationObsRefresh = DateTimeOffset.MinValue;
    private bool _timedAutomationObsRefreshRunning;
    private readonly Dictionary<string, CancellationTokenSource> _activeTimedAutomationRuns = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _timedAutomationRunSync = new();

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
    private bool _settingsUiLoaded;
    private bool _updatingSpotifyUi;
    private string? _lastSpotifyAlbumCoverUrl;
    private string? _lastCreatorIntelligenceTrackId;
    private CancellationTokenSource? _spotifyVolumeChangeCts;
    private CancellationTokenSource? _spotifyAutomationCts;
    private readonly Lock _spotifyAutomationSync = new();
    private int _activeSpotifyAutomationPriority = int.MinValue;
    private string _activeSpotifyAutomationGroup = "";
    private bool _activeSpotifyAutomationExclusive;
    private readonly SpotifySavedStateStore _spotifySavedStateStore = new();
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
            string left = x as string ?? "";
            string right = y as string ?? "";
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
            int separator = entry.IndexOf(" · ", StringComparison.Ordinal);
            return separator >= 0 ? entry[..separator] : "";
        }

        private static string ExtractMessage(string entry)
        {
            int separator = entry.IndexOf(" · ", StringComparison.Ordinal);
            return separator >= 0 ? entry[(separator + 3)..] : entry;
        }

        private static string ExtractGroup(string entry)
        {
            string message = ExtractMessage(entry);
            int separator = message.IndexOf(':');
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
    private readonly ICollectionView? _spotifySavedStateHistoryView;
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
    private string? _lastOverlayPublishedPhase;
    private bool? _lastOverlayPublishedLive;
    private int? _lastOverlayPublishedCountdownRemaining;
    private bool? _lastOverlayPublishedCountdownRunning;
    private string? _lastOverlayPublishedScene;
    private string? _lastOverlayPublishedSpotifyTrack;
    private int? _spotifyVolumeBeforeAlert;
    private bool _spotifyWasPlayingBeforeAlert;
    private bool _spotifyAlertMuteActive;
    private bool _lastObsStreamActive;
    private CancellationTokenSource? _streamStartAutomationCts;
    private CancellationTokenSource? _raidCountdownCts;
    private CancellationTokenSource? _plannedStreamEndCts;
    private CancellationTokenSource? _endSceneCountdownCts;
    private CancellationTokenSource? _raidAutoStartCts;
    private bool _raidCountdownActive;
    private bool _raidCountdownSkipRequested;
    private bool _plannedStreamEndActive;
    private bool _streamEndFlowActive;
    private bool _streamEndAbortRequested;
    private bool _allowMainWindowClose;
    private bool _closeAfterStreamEnd;
    private bool _raidTargetIsOnline;
    private bool _awaitingManualRaid;
    private StreamEndDialogWindow? _activeStreamEndDialog;
    private IReadOnlyList<TwitchChannelSuggestion>? _followedRaidTargetCache;
    private DateTimeOffset _followedRaidTargetCacheAt = DateTimeOffset.MinValue;
    private IReadOnlyList<TwitchChannelSuggestion>? _followedLiveRaidTargetCache;
    private DateTimeOffset _followedLiveRaidTargetCacheAt = DateTimeOffset.MinValue;
    private CancellationTokenSource? _raidTargetSuggestStatusCts;
    private TaskCompletionSource<bool>? _streamEndRaidDecisionTcs;
    private System.Net.WebSockets.ClientWebSocket? _streamerBotEventSocket;
    private CancellationTokenSource? _streamerBotEventCts;
    private readonly ObservableCollection<StreamerBotActionOption> _streamerBotActions = [];
    private readonly ObservableCollection<StreamerBotExecutionHistoryItem> _streamerBotExecutionHistory = [];
    private readonly ObservableCollection<StreamerBotActionTemplate> _streamerBotActionTemplates = [];
    private readonly ObservableCollection<StreamerBotLiveEventItem> _streamerBotLiveEvents = [];
    private CancellationTokenSource? _streamerBotScheduledActionCts;
    private readonly HashSet<string> _streamerBotFavoriteActionIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Threading.DispatcherTimer _twitchUsersRefreshTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly System.Windows.Threading.DispatcherTimer _liveViewerSampleTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool _liveViewerSampleRunning;
    private bool _twitchUsersRefreshRunning;
    private DateTimeOffset _lastTwitchUsersRefreshUtc = DateTimeOffset.MinValue;
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
    private readonly List<string> _streamDeckRuleHistory = [];
    private bool _connectionWatchdogRunning;
    private readonly Dictionary<string, DateTimeOffset> _lastReconnectAttempt =
        new(StringComparer.OrdinalIgnoreCase);
    private TimeSpan _lastDashboardCpuTime = Process.GetCurrentProcess().TotalProcessorTime;
    private DateTimeOffset _lastDashboardResourceSample = DateTimeOffset.Now;
    private long _lastObsOutputBytes;
    private DateTimeOffset? _lastObsBitrateSampleAt;
    private double _currentObsBitrateKbps;
    private IReadOnlyList<ObsSceneInfo> _servicesObsScenes = [];
    private IReadOnlyList<ObsSceneItemInfo> _servicesObsSceneItems = [];
    private IReadOnlyList<ObsInputInfo> _servicesObsInputs = [];
    private readonly Dictionary<string, ObsInputVolumeMeter> _obsLiveMeters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (double PeakDb, DateTimeOffset At)> _obsPeakHold = new(StringComparer.OrdinalIgnoreCase);
    private string _servicesObsCurrentScene = string.Empty;
    private IReadOnlyList<string> _dashboardObsSceneNames = [];
}
