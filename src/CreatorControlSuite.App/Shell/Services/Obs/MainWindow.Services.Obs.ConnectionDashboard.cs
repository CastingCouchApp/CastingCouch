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
    private async Task ConnectObsAsync(bool showErrorDialog = true)
    {
        try
        {
            SettingsPageViewHost.ObsConnectionStatusText.Text = "Verbindung wird hergestellt ...";
            SettingsPageViewHost.ObsConnectionStatusText.Foreground =
                System.Windows.Media.Brushes.Goldenrod;

            await _secretStore.SaveAsync(
                "obs.password",
                SettingsPageViewHost.ObsPasswordBox.Password);

            await _obsClient.ConnectAsync(
                new ObsConnectionOptions(
                    SettingsPageViewHost.ObsHostBox.Text.Trim(),
                    int.Parse(SettingsPageViewHost.ObsPortBox.Text.Trim()),
                    SettingsPageViewHost.ObsPasswordBox.Password,
                    TimeSpan.FromSeconds(8),
                    TimeSpan.FromSeconds(8)));

            await RefreshObsAsync();

            SettingsPageViewHost.ObsConnectionStatusText.Text = "Verbunden";
            SettingsPageViewHost.ObsConnectionStatusText.Foreground =
                System.Windows.Media.Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            SettingsPageViewHost.ObsConnectionStatusText.Text = exception.Message;
            SettingsPageViewHost.ObsConnectionStatusText.Foreground =
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
        SettingsPageViewHost.ObsScenesList.ItemsSource = null;
        SettingsPageViewHost.ObsInputsList.ItemsSource = null;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsScenesList.ItemsSource = null;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationSceneBox.ItemsSource = null;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationSourceBox.ItemsSource = null;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsInputsList.ItemsSource = null;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsTransitionBox.ItemsSource = null;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlaySceneBox.ItemsSource = null;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlaySourceBox.ItemsSource = null;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsTransitionStateText.Text = "OBS ist nicht verbunden.";
        DashboardPageViewHost.DashboardObsAudioInputBox.ItemsSource = null;
        DashboardPageViewHost.DashboardObsAudioStateText.Text = "OBS ist nicht verbunden.";
        SettingsPageViewHost.ObsServerInfoText.Text =
            "OBS-Informationen erscheinen nach der Verbindung.";
        SettingsPageViewHost.ObsStreamStatusText.Text = "Streamstatus unbekannt";
        ObsDashboardStatus.Text = "NICHT VERBUNDEN";
        ObsDashboardLamp.Fill = System.Windows.Media.Brushes.IndianRed;

        RefreshDashboardServiceActionButtons();
    }

    private void RefreshSimpleObsAutomationRulesList()
    {
        if (ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationRulesList is null)
        {
            return;
        }

        ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationRulesList.ItemsSource = _settings.Workflow.TimedAutomations
            .Where(rule => (string.Equals(rule.TriggerType, "SceneElapsed", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rule.TriggerType, "StreamElapsed", StringComparison.OrdinalIgnoreCase))
                && string.Equals(rule.ActionType, "SetSourceVisibility", StringComparison.OrdinalIgnoreCase))
            .OrderBy(rule => rule.DelaySeconds)
            .ToList();
    }

    private async Task RefreshSimpleObsAutomationSourcesAsync()
    {
        if (!_obsClient.IsConnected || ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationSceneBox.SelectedItem is not ObsSceneInfo scene)
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationSourceBox.ItemsSource = Array.Empty<string>();
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
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationSourceBox.ItemsSource = sources;
            if (sources.Count > 0 && ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationSourceBox.SelectedItem is null)
            {
                ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationSourceBox.SelectedIndex = 0;
            }

            ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationStatusText.Text = $"{sources.Count} Quellen aus Szene ‘{scene.Name}’ geladen.";
        }
        catch (Exception exception)
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationStatusText.Text = "Quellen konnten nicht geladen werden: " + exception.Message;
        }
    }

    private async Task AddSimpleObsAutomationRuleAsync()
    {
        if (ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationSceneBox.SelectedItem is not ObsSceneInfo scene)
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationStatusText.Text = "Bitte zuerst eine Szene auswählen.";
            return;
        }
        string? source = ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationSourceBox.SelectedItem as string ?? ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationSourceBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(source))
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationStatusText.Text = "Bitte eine Quelle auswählen.";
            return;
        }
        if (!int.TryParse(ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationDelayBox.Text, out int seconds) || seconds < 0)
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationStatusText.Text = "Bitte eine gültige Zeit in Sekunden eingeben.";
            return;
        }

        bool show = string.Equals((ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationActionBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), "Show", StringComparison.OrdinalIgnoreCase);
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
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationStatusText.Text = "Regel wurde hinzugefügt und gespeichert.";
    }

    private async Task DeleteSimpleObsAutomationRuleAsync()
    {
        if (ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationRulesList.SelectedItem is not TimedAutomationRuleSettings rule)
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationStatusText.Text = "Bitte eine Regel aus der Liste auswählen.";
            return;
        }
        _settings.Workflow.TimedAutomations.Remove(rule);
        await _settingsStore.SaveAsync(_settings);
        RefreshTimedAutomationRules();
        RefreshSimpleObsAutomationRulesList();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationStatusText.Text = "Regel wurde gelöscht.";
    }

    private async Task TestSimpleObsAutomationRuleAsync()
    {
        if (ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationRulesList.SelectedItem is not TimedAutomationRuleSettings rule)
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationStatusText.Text = "Bitte eine Regel aus der Liste auswählen.";
            return;
        }
        if (!_obsClient.IsConnected)
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationStatusText.Text = "OBS ist nicht verbunden.";
            return;
        }
        await _obsClient.SetSceneItemEnabledAsync(rule.ObsScene, rule.ObsSource, rule.SourceVisible);
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationStatusText.Text = "Regel wurde sofort in OBS getestet.";
    }

    private async Task ExecuteObsControlAsync(string operation, Func<Task> action)
    {
        if (!_obsClient.IsConnected)
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsControlStatusText.Text = "OBS ist nicht verbunden.";
            return;
        }
        try
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsControlStatusText.Text = operation + " …";
            await action();
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsControlStatusText.Text = operation + " wurde ausgeführt.";
            await Task.Delay(250);
            await RefreshObsAsync();
        }
        catch (Exception ex)
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsControlStatusText.Text = operation + " fehlgeschlagen: " + ex.Message;
        }
    }

    private async Task ToggleObsRecordPauseAsync()
    {
        if (!_obsClient.IsConnected)
        {
            return;
        }

        try
        {
            ObsOutputStatus status = await _obsClient.GetRecordStatusAsync();
            if (!status.Active)
            {
                ServicesPageViewHost.ObsServiceViewHost.ServicesObsControlStatusText.Text = "Es läuft keine Aufnahme.";
                return;
            }
            if (status.Paused)
            {
                await ExecuteObsControlAsync("Aufnahme fortsetzen", () => _obsClient.ResumeRecordAsync());
            }
            else
            {
                await ExecuteObsControlAsync("Aufnahme pausieren", () => _obsClient.PauseRecordAsync());
            }
        }
        catch (Exception ex)
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsControlStatusText.Text = "Aufnahmestatus konnte nicht gelesen werden: " + ex.Message;
        }
    }

    private async Task RefreshObsProfessionalControlAsync(ObsStreamStatus? stream)
    {
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsStreamStateText.Text = stream?.OutputActive == true
            ? $"Live · {stream.OutputTimecode}"
            : "Offline";
        try
        {
            ObsStats stats = await _obsClient.GetStatsAsync();
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsCpuText.Text = $"CPU: {stats.CpuUsage:0.0} %";
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsFpsText.Text = $"FPS: {stats.ActiveFps:0.0}";
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsMemoryText.Text = $"RAM: {stats.MemoryUsage:0} MB";
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsRenderLagText.Text = $"Render-Lag: {stats.RenderSkippedFrames}/{stats.RenderTotalFrames}";
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsOutputLagText.Text = $"Encoding-Lag: {stats.OutputSkippedFrames}/{stats.OutputTotalFrames}";
        }
        catch { }
        try
        {
            ObsOutputStatus record = await _obsClient.GetRecordStatusAsync();
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsRecordStateText.Text = !record.Active ? "Gestoppt" : record.Paused ? "Pausiert" : $"Läuft · {record.Timecode}";
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsPauseRecordButton.Content = record.Paused ? "FORTSETZEN" : "PAUSE";
        }
        catch { ServicesPageViewHost.ObsServiceViewHost.ServicesObsRecordStateText.Text = "Nicht verfügbar"; }
        try
        {
            ObsOutputStatus replay = await _obsClient.GetReplayBufferStatusAsync();
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsReplayStateText.Text = replay.Active ? "Aktiv" : "Gestoppt";
        }
        catch { ServicesPageViewHost.ObsServiceViewHost.ServicesObsReplayStateText.Text = "Nicht verfügbar"; }
        try
        {
            bool virtualCam = await _obsClient.GetVirtualCamStatusAsync();
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsVirtualCamStateText.Text = virtualCam ? "Aktiv" : "Gestoppt";
        }
        catch { ServicesPageViewHost.ObsServiceViewHost.ServicesObsVirtualCamStateText.Text = "Nicht verfügbar"; }
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
            SettingsPageViewHost.ObsConnectionStatusText.Text = message;
            SettingsPageViewHost.ObsConnectionStatusText.Foreground = System.Windows.Media.Brushes.Gray;
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsStatusText.Text = message;
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsStatusText.Foreground = System.Windows.Media.Brushes.Gray;
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

        ObsSnapshot snapshot = await _obsClient.GetSnapshotAsync();
        IReadOnlyList<ObsTransitionInfo> transitions = await _obsClient.GetSceneTransitionListAsync();

        if (!string.Equals(_automationCurrentScene, snapshot.CurrentProgramScene, StringComparison.OrdinalIgnoreCase))
        {
            _automationCurrentScene = snapshot.CurrentProgramScene;
            _automationSceneActivatedAt = DateTimeOffset.UtcNow;
            foreach (TimedAutomationRuleSettings? sceneRule in _settings.Workflow.TimedAutomations
                         .Where(rule => string.Equals(rule.TriggerType, "SceneElapsed", StringComparison.OrdinalIgnoreCase)
                                        && string.Equals(rule.TriggerScene, snapshot.CurrentProgramScene, StringComparison.OrdinalIgnoreCase)))
            {
                _executedTimedAutomationRuleIds.Remove(sceneRule.Id);
            }
        }

        SettingsPageViewHost.ObsScenesList.ItemsSource = snapshot.Scenes;
        SettingsPageViewHost.ObsInputsList.ItemsSource = snapshot.Inputs;
        string? selectedObsInputName = (ServicesPageViewHost.ObsServiceViewHost.ServicesObsInputsList.SelectedItem as ObsInputInfo)?.Name;
        string? selectedTransitionName = (ServicesPageViewHost.ObsServiceViewHost.ServicesObsTransitionBox.SelectedItem as ObsTransitionInfo)?.Name;
        _servicesObsScenes = snapshot.Scenes;
        _servicesObsCurrentScene = snapshot.CurrentProgramScene;
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsCurrentSceneText.Text = "Aktuelle Szene: " + snapshot.CurrentProgramScene;
        ApplyServicesObsSceneFilter();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationSceneBox.ItemsSource = snapshot.Scenes;
        if (ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationSceneBox.SelectedItem is not ObsSceneInfo)
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsAutomationSceneBox.SelectedItem = snapshot.Scenes.FirstOrDefault();
        }

        _servicesObsInputs = snapshot.Inputs;
        ApplyServicesObsInputFilter();
        RefreshSimpleObsAutomationRulesList();
        await RefreshSimpleObsAutomationSourcesAsync();
        ServicesPageViewHost.ObsServiceViewHost.ServicesObsTransitionBox.ItemsSource = transitions;
        if (!string.IsNullOrWhiteSpace(selectedTransitionName))
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsTransitionBox.SelectedItem = transitions.FirstOrDefault(transition => string.Equals(transition.Name, selectedTransitionName, StringComparison.OrdinalIgnoreCase));
        }

        if (ServicesPageViewHost.ObsServiceViewHost.ServicesObsTransitionBox.SelectedItem is not ObsTransitionInfo)
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsTransitionBox.SelectedItem = transitions.FirstOrDefault();
        }

        ServicesPageViewHost.ObsServiceViewHost.ServicesObsTransitionStateText.Text = transitions.Count == 0
            ? "OBS hat keine auswählbaren Übergänge gemeldet."
            : $"{transitions.Count} Übergänge geladen. Auswahl und Dauer werden erst mit „Übergang übernehmen“ an OBS gesendet.";
        if (!string.IsNullOrWhiteSpace(selectedObsInputName))
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsInputsList.SelectedItem = snapshot.Inputs.FirstOrDefault(input => string.Equals(input.Name, selectedObsInputName, StringComparison.OrdinalIgnoreCase));
        }

        if (ServicesPageViewHost.ObsServiceViewHost.ServicesObsInputsList.SelectedItem is not ObsInputInfo)
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsInputsList.SelectedItem = snapshot.Inputs.FirstOrDefault();
        }

        if (ServicesPageViewHost.ObsServiceViewHost.ServicesObsScenesList.SelectedItem is not ObsSceneInfo)
        {
            ServicesPageViewHost.ObsServiceViewHost.ServicesObsScenesList.SelectedItem = snapshot.Scenes.FirstOrDefault(scene => string.Equals(scene.Name, snapshot.CurrentProgramScene, StringComparison.OrdinalIgnoreCase)) ?? snapshot.Scenes.FirstOrDefault();
        }

        await RefreshServicesObsSceneItemsAsync();
        DashboardPageViewHost.DashboardObsAudioInputBox.ItemsSource = snapshot.Inputs;
        if (DashboardPageViewHost.DashboardObsAudioInputBox.SelectedItem is null && snapshot.Inputs.Count > 0)
        {
            DashboardPageViewHost.DashboardObsAudioInputBox.SelectedIndex = 0;
        }

        SettingsPageViewHost.ObsServerInfoText.Text =
            $"OBS {snapshot.Server?.ObsVersion} · " +
            $"WebSocket {snapshot.Server?.WebSocketVersion} · " +
            $"Aktuelle Szene: {snapshot.CurrentProgramScene}";

        DashboardPageViewHost.DashboardCurrentSceneText.Text = snapshot.CurrentProgramScene;
        var dashboardScenes = snapshot.Scenes
            .Select(scene => scene.Name)
            .Where(scene => !string.IsNullOrWhiteSpace(scene))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? requestedSpotifyOverlayScene = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlaySceneBox.Text?.Trim();
        string? requestedStartScene = SettingsPageViewHost.StartSceneBox.Text?.Trim();
        string? requestedLiveScene = SettingsPageViewHost.LiveSceneBox.Text?.Trim();
        string? requestedPauseScene = SettingsPageViewHost.PauseSceneBox.Text?.Trim();
        string? requestedEndScene = SettingsPageViewHost.EndSceneBox.Text?.Trim();
        SettingsPageViewHost.StartSceneBox.ItemsSource = snapshot.Scenes;
        SettingsPageViewHost.LiveSceneBox.ItemsSource = snapshot.Scenes;
        SettingsPageViewHost.PauseSceneBox.ItemsSource = snapshot.Scenes;
        SettingsPageViewHost.EndSceneBox.ItemsSource = snapshot.Scenes;
        SettingsPageViewHost.StartSceneBox.Text = requestedStartScene ?? _settings.Obs.StartScene;
        SettingsPageViewHost.LiveSceneBox.Text = requestedLiveScene ?? _settings.Obs.LiveScene;
        SettingsPageViewHost.PauseSceneBox.Text = requestedPauseScene ?? _settings.Obs.PauseScene;
        SettingsPageViewHost.EndSceneBox.Text = requestedEndScene ?? _settings.Obs.EndScene;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlaySceneBox.ItemsSource = dashboardScenes;

        if (!string.IsNullOrWhiteSpace(requestedSpotifyOverlayScene))
        {
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlaySceneBox.Text = requestedSpotifyOverlayScene;
        }
        else if (!string.IsNullOrWhiteSpace(_settings.Spotify.OverlayObsScene))
        {
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlaySceneBox.Text = _settings.Spotify.OverlayObsScene;
        }

        await RefreshSpotifyOverlayBrowserSourcesAsync();

        _dashboardObsSceneNames = dashboardScenes;
        HighlightDashboardSceneButtons(snapshot.CurrentProgramScene);
        await RefreshDashboardObsScenePreviewAsync(snapshot.CurrentProgramScene);

        SettingsPageViewHost.ObsStreamStatusText.Text = snapshot.Stream?.OutputActive == true
            ? $"LIVE · {snapshot.Stream.OutputTimecode}"
            : "Offline";
        await RefreshObsProfessionalControlAsync(snapshot.Stream);
        DashboardHeaderStreamActionButton.Content =
            snapshot.Stream?.OutputActive == true
                ? "■  STREAM BEENDEN"
                : "●  LIVE GEHEN";

        bool obsReportsStreamActive = snapshot.Stream?.OutputActive == true;
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
        bool streamActiveNow = obsReportsStreamActive ||
            ((_lastObsStreamActive || _streamSessionStartedAt.HasValue) && _consecutiveObsStreamInactivePolls < ConfirmedObsOfflinePollsRequired);

        if (streamActiveNow && !_lastObsStreamActive)
        {
            // Ein Stream kann auch direkt in OBS, über Streamer.bot oder über
            // einen anderen Steuerweg gestartet worden sein. In diesem Fall
            // existiert bislang keine Session-Startzeit. Ohne startedAt zeigt
            // live-status.html trotz isLive=true weiterhin OFFLINE an.
            _streamSessionStartedAt ??= ResolveObservedObsStreamStartedAt(snapshot.Stream?.OutputTimecode);
            _ = HandleObservedStreamStartAsync();
            if (_lastOverlayPublishedLive != true)
            {
                _lastOverlayPublishedLive = true;
                _ = PublishOverlayRealtimeEventAsync(OverlayEventBridge.AppStreamLive(true));
            }
        }
        else if (!streamActiveNow && _lastObsStreamActive)
        {
            _streamStartAutomationCts?.Cancel();
            _streamSessionStartedAt = null;
            _twitchStreamStartedAt = null;
            _spotifyStartPlaylistTriggeredForCurrentStream = false;
            _consecutiveObsStreamInactivePolls = 0;
            if (_lastOverlayPublishedLive != false)
            {
                _lastOverlayPublishedLive = false;
                _ = PublishOverlayRealtimeEventAsync(OverlayEventBridge.AppStreamLive(false));
            }
        }
        _lastObsStreamActive = streamActiveNow;
        RefreshWorkflowUi(_workflowModule.Service.State);

        bool microphoneMuted = await GetTrackedObsInputMuteAsync(
            snapshot.Inputs,
            _settings.Obs.MicrophoneSource,
            ["Mic", "Mikrofon", "Microphone"],
            ["mikrofon", "microphone", "mic"]);

        bool desktopAudioMuted = await GetTrackedObsInputMuteAsync(
            snapshot.Inputs,
            _settings.Obs.DesktopAudioSource,
            ["Broadcast", "Desktop Audio", "Desktop-Audio", "Spiel- und Streamsound"],
            ["broadcast", "desktop audio", "desktop-audio", "streamsound", "spiel- und streamsound"]);

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
            JsonObject obs = root["obs"] as JsonObject ?? [];
            obs["connected"] = snapshot.Connected;
            obs["currentScene"] = snapshot.CurrentProgramScene;
            obs["microphoneMuted"] = microphoneMuted;
            obs["desktopAudioMuted"] = desktopAudioMuted;
            root["obs"] = obs;

            JsonObject stream = root["stream"] as JsonObject ?? [];
            stream["isLive"] = streamActiveNow;
            stream["currentScene"] = snapshot.CurrentProgramScene;
            DateTimeOffset? liveStartedAt = ResolveLiveStreamStartedAt();
            stream["startedAt"] = liveStartedAt;
            stream["elapsedSeconds"] = liveStartedAt.HasValue
                ? Math.Max(0, (long)(DateTimeOffset.Now - liveStartedAt.Value).TotalSeconds)
                : 0;
            stream["viewerCount"] = _currentLiveViewerCount;
            root["stream"] = stream;

            JsonObject stats = root["stats"] as JsonObject ?? [];
            StreamSessionStats sessionStats = _workflowModule.Service.SessionStats;
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
            TimeSpan.TryParse(outputTimecode, System.Globalization.CultureInfo.InvariantCulture, out TimeSpan elapsed) &&
            elapsed >= TimeSpan.Zero && elapsed < TimeSpan.FromDays(30))
        {
            return DateTimeOffset.Now - elapsed;
        }

        return DateTimeOffset.Now;
    }

    private DateTimeOffset? ResolveLiveStreamStartedAt() =>
        _twitchStreamStartedAt ?? _streamSessionStartedAt ?? _twitchSessionObservedAt;

    private void ApplyTwitchLiveStreamStartedAt(DateTimeOffset? startedAt)
    {
        if (startedAt is null)
        {
            return;
        }

        _twitchStreamStartedAt = startedAt;

        // Helix started_at ist maßgeblich für die Live-Dauer. Lokale Startzeiten
        // (OBS-Timecode / Workflow) werden damit auf den Twitch-Start korrigiert.
        _streamSessionStartedAt = startedAt;

        StreamSessionStats stats = _workflowModule.Service.SessionStats;
        if (stats.EndedAt is null)
        {
            stats.StartedAt = startedAt;
        }
    }

    private async Task HandleObservedStreamStartAsync()
    {
        // Spotify muss sofort beim erkannten LIVE-Übergang starten und darf
        // nicht hinter der Legacy-Intro-Automation (5-Minuten-Delay) warten.
        if (!_spotifyStartPlaylistTriggeredForCurrentStream)
        {
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

        // Legacy-Intro (Startszene/Testbild + Delay) parallel weiterlaufen lassen.
        _ = StartLegacyStreamAutomationSafeAsync();
    }

    private async Task StartLegacyStreamAutomationSafeAsync()
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
            ObsInputAudioState state = await _obsClient.GetInputAudioStateAsync(input.Name);
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
                DashboardPageViewHost.DashboardObsScenePreviewImage.Source = null;
                DashboardPageViewHost.DashboardObsScenePreviewPlaceholder.Visibility = Visibility.Visible;
                return;
            }

            sceneName ??= await _obsClient.GetCurrentProgramSceneAsync();
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return;
            }

            double previewWidth = GetDashboardObsScenePreviewWidth(
                _settings.Dashboard.ObsScenePreviewSize);
            byte[] bytes = await _obsClient.GetSourceScreenshotAsync(
                sceneName,
                (int)Math.Clamp(previewWidth, 160, 1920),
                imageHeight: null);
            if (bytes.Length == 0)
            {
                DashboardPageViewHost.DashboardObsScenePreviewImage.Source = null;
                DashboardPageViewHost.DashboardObsScenePreviewPlaceholder.Visibility = Visibility.Visible;
                return;
            }

            using var stream = new System.IO.MemoryStream(bytes);
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            if (bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
            {
                _dashboardObsPreviewAspect = bitmap.PixelWidth / (double)bitmap.PixelHeight;
                ApplyDashboardObsScenePreviewSize();
            }

            DashboardPageViewHost.DashboardObsScenePreviewImage.Source = bitmap;
            DashboardPageViewHost.DashboardObsScenePreviewPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            DashboardPageViewHost.DashboardObsScenePreviewImage.Source = null;
            DashboardPageViewHost.DashboardObsScenePreviewPlaceholder.Visibility = Visibility.Visible;
            _appLogger.Write(AppLogLevel.Warning, "OBS", "OBS-Szenenvorschau konnte nicht geladen werden.", exception);
        }
    }

    private async Task SwitchObsSceneAsync()
    {
        if (SettingsPageViewHost.ObsScenesList.SelectedItem is not ObsSceneInfo scene)
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

            ObsSnapshot snapshot = await _obsClient.GetSnapshotAsync();

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
        AppSettings persisted = await _settingsStore.LoadAsync(CancellationToken.None);

        if (!persisted.Workflow.AutoStartSpotifyPlaylist)
        {
            _appLogger.Write(AppLogLevel.Information, "Spotify.StartPlaylist",
                "Automatischer Playliststart ist in den gespeicherten Einstellungen deaktiviert.");
            return;
        }

        string playlistUri = persisted.Spotify.StartPlaylistUri?.Trim() ?? "";
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
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                await _spotifyModule.StartPlaylistAsync(
                    playlistUri,
                    startVolumePercent: persisted.Spotify.StartVolumePercent,
                    cancellationToken: CancellationToken.None);

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
}
