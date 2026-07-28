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
    private ICollectionView InitializeTimedAutomationBindings()
    {
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationRulesList.ItemsSource = _timedAutomationRules;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationDiagnosticsList.ItemsSource = _timedAutomationDiagnostics;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationRulesList.SelectionChanged += (_, _) => LoadSelectedTimedAutomationRule();
        WorkflowPageViewHost.TimedAutomationViewHost.NewTimedAutomationButton.Click += (_, _) => CreateNewTimedAutomationRule();
        WorkflowPageViewHost.TimedAutomationViewHost.ImportTimedAutomationsButton.Click += async (_, _) => await ImportTimedAutomationsAsync();
        WorkflowPageViewHost.TimedAutomationViewHost.ExportTimedAutomationsButton.Click += async (_, _) => await ExportTimedAutomationsAsync();
        WorkflowPageViewHost.TimedAutomationViewHost.AddTimedAutomationTemplateButton.Click += async (_, _) => await AddTimedAutomationTemplateAsync();
        WorkflowPageViewHost.TimedAutomationViewHost.DeleteTimedAutomationButton.Click += async (_, _) => await DeleteSelectedTimedAutomationRuleAsync();
        WorkflowPageViewHost.TimedAutomationViewHost.SaveTimedAutomationButton.Click += async (_, _) => await SaveTimedAutomationRuleAsync();
        WorkflowPageViewHost.TimedAutomationViewHost.RefreshTimedAutomationObsButton.Click += async (_, _) => await RefreshTimedAutomationObsListsAsync(true);
        WorkflowPageViewHost.TimedAutomationViewHost.TestTimedAutomationButton.Click += async (_, _) => await TestSelectedTimedAutomationRuleAsync();
        WorkflowPageViewHost.TimedAutomationViewHost.CancelTimedAutomationTestButton.Click += (_, _) => _timedAutomationTestCts?.Cancel();
        WorkflowPageViewHost.TimedAutomationViewHost.ValidateTimedAutomationsButton.Click += (_, _) => ValidateTimedAutomationRules();
        WorkflowPageViewHost.TimedAutomationViewHost.ClearTimedAutomationDiagnosticsButton.Click += (_, _) => _timedAutomationDiagnostics.Clear();
        WorkflowPageViewHost.TimedAutomationViewHost.StopAllTimedAutomationsButton.Click += (_, _) => StopAllTimedAutomations();
        WorkflowPageViewHost.TimedAutomationViewHost.RefreshSpotifySavedStateButton.Click += (_, _) => RefreshSpotifySavedStateStatus();
        WorkflowPageViewHost.TimedAutomationViewHost.RestoreSpotifySavedStateNowButton.Click += async (_, _) => await RestoreSpotifySavedStateNowAsync();
        WorkflowPageViewHost.TimedAutomationViewHost.DiscardSpotifySavedStateButton.Click += (_, _) => DiscardSpotifySavedState();
        WorkflowPageViewHost.TimedAutomationViewHost.RefreshSpotifySavedStatesOverviewButton.Click += (_, _) => RefreshSpotifySavedStatesOverview();
        WorkflowPageViewHost.TimedAutomationViewHost.RestoreSelectedSpotifySavedStateButton.Click += async (_, _) => await RestoreSelectedSpotifySavedStateAsync();
        WorkflowPageViewHost.TimedAutomationViewHost.DiscardSelectedSpotifySavedStateButton.Click += (_, _) => DiscardSelectedSpotifySavedState();
        WorkflowPageViewHost.TimedAutomationViewHost.DiscardAllSpotifySavedStatesButton.Click += (_, _) => DiscardAllSpotifySavedStates();
        WorkflowPageViewHost.TimedAutomationViewHost.DiscardExpiredSpotifySavedStatesButton.Click += (_, _) => DiscardExpiredSpotifySavedStates("manuell");
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateMaxAgeBox.TextChanged += (_, _) => RefreshSpotifySavedStatesOverview();
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateCleanupIntervalBox.Checked += (_, _) => UpdateSpotifySavedStateCleanupTimer();
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateCleanupIntervalBox.Unchecked += (_, _) => UpdateSpotifySavedStateCleanupTimer();
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateCleanupIntervalMinutesBox.TextChanged += (_, _) => UpdateSpotifySavedStateCleanupTimer();
        _spotifySavedStateCleanupTimer.Tick += (_, _) => DiscardExpiredSpotifySavedStates("Intervall");
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStatesOverviewList.SelectionChanged += (_, _) => UpdateSpotifySavedStatesOverviewSelection();
        WorkflowPageViewHost.TimedAutomationViewHost.ExportSpotifySavedStateHistoryButton.Click += (_, _) => ExportSpotifySavedStateHistory();
        WorkflowPageViewHost.TimedAutomationViewHost.ExportSpotifySavedStateHistoryCsvButton.Click += (_, _) => ExportSpotifySavedStateHistoryCsv();
        WorkflowPageViewHost.TimedAutomationViewHost.ImportSpotifySavedStateHistoryButton.Click += (_, _) => ImportSpotifySavedStateHistory();
        WorkflowPageViewHost.TimedAutomationViewHost.SelectVisibleSpotifySavedStateHistoryButton.Click += (_, _) => SelectVisibleSpotifySavedStateHistory();
        WorkflowPageViewHost.TimedAutomationViewHost.ExportSelectedSpotifySavedStateHistoryButton.Click += (_, _) => ExportSelectedSpotifySavedStateHistory();
        WorkflowPageViewHost.TimedAutomationViewHost.ExportSelectedSpotifySavedStateHistoryCsvButton.Click += (_, _) => ExportSelectedSpotifySavedStateHistoryCsv();
        WorkflowPageViewHost.TimedAutomationViewHost.RemoveSelectedSpotifySavedStateHistoryButton.Click += (_, _) => RemoveSelectedSpotifySavedStateHistory();
        WorkflowPageViewHost.TimedAutomationViewHost.ClearSpotifySavedStateHistoryButton.Click += (_, _) => ClearSpotifySavedStateHistory();
        WorkflowPageViewHost.TimedAutomationViewHost.CreateSpotifySavedStateHistoryBackupButton.Click += (_, _) => CreateSpotifySavedStateHistoryBackup(manual: true);
        WorkflowPageViewHost.TimedAutomationViewHost.RefreshSpotifySavedStateHistoryBackupsButton.Click += (_, _) => RefreshSpotifySavedStateHistoryBackups();
        WorkflowPageViewHost.TimedAutomationViewHost.PreviewSelectedSpotifySavedStateHistoryBackupButton.Click += (_, _) => UpdateSpotifySavedStateHistoryBackupPreview(showStatus: true);
        WorkflowPageViewHost.TimedAutomationViewHost.RestoreSelectedSpotifySavedStateHistoryBackupButton.Click += (_, _) => RestoreSelectedSpotifySavedStateHistoryBackup();
        WorkflowPageViewHost.TimedAutomationViewHost.RestoreSelectedSpotifySavedStateHistoryPartsButton.Click += (_, _) => RestoreSelectedSpotifySavedStateHistoryParts();
        WorkflowPageViewHost.TimedAutomationViewHost.ApplySpotifyHistoryRestoreProfileButton.Click += (_, _) => ApplySelectedSpotifyHistoryRestoreProfile();
        WorkflowPageViewHost.TimedAutomationViewHost.SaveSpotifyHistoryRestoreProfileButton.Click += (_, _) => SaveSpotifyHistoryRestoreProfile();
        WorkflowPageViewHost.TimedAutomationViewHost.DeleteSpotifyHistoryRestoreProfileButton.Click += (_, _) => DeleteSpotifyHistoryRestoreProfile();
        WorkflowPageViewHost.TimedAutomationViewHost.ExportSpotifyHistoryRestoreProfilesButton.Click += (_, _) => ExportSpotifyHistoryRestoreProfiles();
        WorkflowPageViewHost.TimedAutomationViewHost.ImportSpotifyHistoryRestoreProfilesButton.Click += (_, _) => ImportSpotifyHistoryRestoreProfiles();
        WorkflowPageViewHost.TimedAutomationViewHost.ConfirmSpotifyHistoryRestoreProfilesImportButton.Click += (_, _) => ConfirmSpotifyHistoryRestoreProfilesImport();
        WorkflowPageViewHost.TimedAutomationViewHost.DeleteSelectedSpotifySavedStateHistoryBackupButton.Click += (_, _) => DeleteSelectedSpotifySavedStateHistoryBackup();
        WorkflowPageViewHost.TimedAutomationViewHost.OpenSpotifySavedStateHistoryBackupFolderButton.Click += (_, _) => OpenSpotifySavedStateHistoryBackupFolder();
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryBackupsList.SelectionChanged += (_, _) =>
        {
            UpdateSpotifySavedStateHistoryBackupDetail();
            UpdateSpotifySavedStateHistoryBackupPreview(showStatus: false);
        };
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryBackupsList.ItemsSource = _spotifySavedStateHistoryBackups;
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryBackupDifferencesList.ItemsSource = _spotifySavedStateHistoryBackupDifferences;
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifyHistoryRestoreProfileImportPreviewList.ItemsSource = _spotifyHistoryRestoreProfileImportPreview;
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifyHistoryRestoreProfileBox.ItemsSource = _spotifyHistoryRestoreProfiles;
        LoadSpotifyHistoryRestoreProfiles();
        WorkflowPageViewHost.TimedAutomationViewHost.ResetSpotifySavedStateHistoryFilterButton.Click += (_, _) => ResetSpotifySavedStateHistoryFilter();
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistorySearchBox.TextChanged += (_, _) => { RefreshSpotifySavedStateHistoryFilter(); SaveSpotifySavedStateHistoryPersistence(); };
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryActionFilterBox.SelectionChanged += (_, _) => { RefreshSpotifySavedStateHistoryFilter(); SaveSpotifySavedStateHistoryPersistence(); };
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryFavoritesOnlyBox.Checked += (_, _) => { RefreshSpotifySavedStateHistoryFilter(); SaveSpotifySavedStateHistoryPersistence(); };
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryFavoritesOnlyBox.Unchecked += (_, _) => { RefreshSpotifySavedStateHistoryFilter(); SaveSpotifySavedStateHistoryPersistence(); };
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistorySortBox.SelectionChanged += (_, _) => { ApplySpotifySavedStateHistorySort(); SaveSpotifySavedStateHistoryPersistence(); };
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryList.SelectionChanged += (_, _) => UpdateSpotifySavedStateHistoryDetail();
        WorkflowPageViewHost.TimedAutomationViewHost.ToggleSpotifySavedStateHistoryFavoriteButton.Click += (_, _) => ToggleSpotifySavedStateHistoryFavorite();
        WorkflowPageViewHost.TimedAutomationViewHost.SaveSpotifySavedStateHistoryNoteButton.Click += (_, _) => SaveSpotifySavedStateHistoryNote();
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryList.ItemsSource = _spotifySavedStateHistory;
        ICollectionView spotifySavedStateHistoryView =
            CollectionViewSource.GetDefaultView(_spotifySavedStateHistory);
        spotifySavedStateHistoryView.Filter = SpotifySavedStateHistoryMatchesFilter;
        return spotifySavedStateHistoryView;
    }

    private void InitializeTimedAutomationPostBindings()
    {
        LoadSpotifySavedStateHistoryPersistence();
        RefreshSpotifySavedStateHistoryBackups();
        ApplySpotifySavedStateHistorySort();
        RefreshSpotifySavedStatesOverview();
        RefreshSpotifySavedStateStatistics();
        Loaded += async (_, _) =>
        {
            await RunStartupStepSafelyAsync("Spotify-Zustände bereinigen", () =>
            {
                if (WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateCleanupOnStartupBox.IsChecked == true)
                {
                    DiscardExpiredSpotifySavedStates("Programmstart", onlyLogWhenRemoved: true);
                }

                UpdateSpotifySavedStateCleanupTimer();
                return Task.CompletedTask;
            });
        };
        WorkflowPageViewHost.WorkflowDesignerViewHost.RefreshWorkflowDesignerButton.Click += (_, _) => RefreshWorkflowDesigner();
        WorkflowPageViewHost.WorkflowDesignerViewHost.AutoLayoutWorkflowDesignerButton.Click += async (_, _) => await AutoLayoutWorkflowDesignerAsync();
        WorkflowPageViewHost.WorkflowDesignerViewHost.ValidateWorkflowDesignerButton.Click += (_, _) => ValidateWorkflowDesigner();
        WorkflowPageViewHost.WorkflowDesignerViewHost.ZoomInWorkflowDesignerButton.Click += (_, _) => SetWorkflowDesignerZoom(WorkflowPageViewHost.WorkflowDesignerViewHost.WorkflowDesignerScale.ScaleX + 0.1);
        WorkflowPageViewHost.WorkflowDesignerViewHost.ZoomOutWorkflowDesignerButton.Click += (_, _) => SetWorkflowDesignerZoom(WorkflowPageViewHost.WorkflowDesignerViewHost.WorkflowDesignerScale.ScaleX - 0.1);
        WorkflowPageViewHost.WorkflowDesignerViewHost.ResetZoomWorkflowDesignerButton.Click += (_, _) => SetWorkflowDesignerZoom(1.0);
        WorkflowPageViewHost.WorkflowDesignerViewHost.WorkflowDesignerGroupBox.SelectionChanged += (_, _) => RefreshWorkflowDesigner();
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSourceSceneBox.SelectionChanged += async (_, _) => await RefreshTimedAutomationSourceListAsync();
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSourceSceneBox.DropDownClosed += async (_, _) => await RefreshTimedAutomationSourceListAsync();
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTriggerSceneBox.DropDownOpened += async (_, _) => await RefreshTimedAutomationObsListsAsync(false);
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTargetSceneBox.DropDownOpened += async (_, _) => await RefreshTimedAutomationObsListsAsync(false);
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSourceSceneBox.DropDownOpened += async (_, _) => await RefreshTimedAutomationObsListsAsync(false);
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTransitionBox.DropDownOpened += async (_, _) => await RefreshTimedAutomationObsListsAsync(false);
        WorkflowPageViewHost.ShortStreamTestViewHost.StartShortStreamTestButton.Click += async (_, _) => await RunShortStreamTestAsync();
        WorkflowPageViewHost.ShortStreamTestViewHost.CancelShortStreamTestButton.Click += (_, _) => _timedAutomationTestCts?.Cancel();
        _timedAutomationTickSubscription = _eventBus.Subscribe<TimedAutomationTick>(tick =>
        {
            _ = Dispatcher.InvokeAsync(async () => await EvaluateTimedAutomationRulesAsync());
        });
        // Timer replaced by TimedAutomationTickPublisher -> IEventBus.

        _workflowModule.Service.StateChanged += (_, state) =>
        {
            Dispatcher.Invoke(() => RefreshWorkflowUi(state));
            _ = PublishOverlayWorkflowStateAsync(state);
        };
    }
}
