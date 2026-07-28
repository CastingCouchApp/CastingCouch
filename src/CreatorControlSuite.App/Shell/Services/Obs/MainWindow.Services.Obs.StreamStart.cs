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
    private async Task StartObsStreamAsync()
    {
        MessageBoxResult result = MessageBox.Show(
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
                DashboardPageViewHost.DashboardWorkflowStageText.Text = "STARTSZENE → STREAMSTART → LIVE";
                SetWorkflowVisualStage("Start", $"Startszene aktiv: {_settings.Obs.StartScene}");
            }

            // Die Startplaylist wird ausschließlich durch den zentral bestätigten
            // OBS-Übergang OFFLINE -> LIVE ausgelöst. So gibt es unabhängig vom
            // Startweg (Suite, OBS, Streamer.bot oder Remote-PC) nur ein Ereignis
            // und keinen zu frühen Spotify-Aufruf vor dem tatsächlichen Streamstart.
            await _obsClient.StartStreamAsync();
            _streamSessionStartedAt = DateTimeOffset.Now;
            await _creatorIntelligence.StartSessionAsync(_streamSessionStartedAt.Value, DashboardPageViewHost.DashboardTwitchTitleBox.Text, DashboardPageViewHost.DashboardTwitchCategorySearchBox.Text);

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
                DashboardPageViewHost.DashboardWorkflowStageText.Text = "LIVE";
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
        DateTimeOffset? endedAt = finalize ? DateTimeOffset.Now : (DateTimeOffset?)null;

        // Letzte Live-Werte unmittelbar vor der Endszene abrufen.
        await RefreshLiveViewerSampleAsync();
        await RefreshTwitchFollowerCountAsync();
        if (endedAt.HasValue)
        {
            await _workflowModule.Service.FinalizeSessionStatsAsync(endedAt);
        }

        StreamSessionStats sessionStats = _workflowModule.Service.SessionStats;
        await UpdateActiveOverlayJsonAsync(root =>
        {
            JsonObject stream = root["stream"] as JsonObject ?? [];
            stream["isLive"] = true;
            stream["phase"] = "Ending";
            stream["startedAt"] = ResolveLiveStreamStartedAt() ?? sessionStats.StartedAt;
            stream["endedAt"] = endedAt;
            stream["elapsedSeconds"] = sessionStats.StreamTimeSeconds;
            stream["viewerCount"] = _currentLiveViewerCount;
            root["stream"] = stream;

            JsonObject stats = root["stats"] as JsonObject ?? [];
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
        CancellationToken token = _raidCountdownCts.Token;
        _raidCountdownSkipRequested = false;
        _raidCountdownActive = true;
        // Endszene-Wartezeit beenden – Raid-Countdown läuft parallel zur Endszene-Anzeige.
        _endSceneCountdownCts?.Cancel();
        UpdateDashboardStreamEndModuleVisibility();
        UpdateDashboardRaidActionButtons();
        DashboardPageViewHost.DashboardRaidCountdownTitleText.Text = "RAID LÄUFT";
        DashboardPageViewHost.DashboardRaidCountdownTargetText.Text = $"Ziel: {displayName}";
        DashboardPageViewHost.DashboardRaidViewerText.Text = $"Aktuelle Zuschauer: {_currentLiveViewerCount}";
        DashboardPageViewHost.DashboardRaidCountdownProgress.Minimum = 0;
        DashboardPageViewHost.DashboardRaidCountdownProgress.Maximum = Math.Max(1, seconds);
        SetStreamEndStatus("Raid läuft · JETZT RAIDEN überspringt den Countdown");
        _activeStreamEndDialog?.ShowRaidActions(false);
        _activeStreamEndDialog?.SetCancelRaidEnabled(true);
        _activeStreamEndDialog?.SetRaidReady(true);

        try
        {
            for (int remaining = seconds; remaining >= 0; remaining--)
            {
                token.ThrowIfCancellationRequested();
                string clock = TimeSpan.FromSeconds(remaining).ToString(@"mm\:ss");
                DashboardPageViewHost.DashboardRaidCountdownText.Text = $"Raid in: {clock}";
                DashboardPageViewHost.DashboardRaidCountdownProgress.Value = seconds - remaining;
                DashboardPageViewHost.DashboardWorkflowStageText.Text = $"RAID → {displayName} · noch {remaining}s";
                _activeStreamEndDialog?.UpdateCountdown(
                    "Raid",
                    clock,
                    seconds - remaining,
                    seconds);
                _activeStreamEndDialog?.SetRaidTargetStatus($"Ziel: {displayName} · Zuschauer: {_currentLiveViewerCount}");
                if (remaining > 0)
                {
                    await Task.Delay(1000, token);
                }
            }

            DashboardPageViewHost.DashboardRaidCountdownTitleText.Text = "RAID AUSGEFÜHRT";
            DashboardPageViewHost.DashboardRaidCountdownText.Text = "Stream wird beendet …";
            DashboardPageViewHost.DashboardRaidCountdownProgress.Value = seconds;
            SetStreamEndStatus("Raid ausgeführt");
            _activeStreamEndDialog?.UpdateCountdown("Raid ausgeführt", "00:00", seconds, seconds);
            return true;
        }
        catch (OperationCanceledException)
        {
            RaidCountdownOutcome outcome = RaidCountdownPolicy.DecideAfterCancellation(
                _raidCountdownSkipRequested);
            if (RaidCountdownPolicy.IsSuccessful(outcome))
            {
                DashboardPageViewHost.DashboardRaidCountdownTitleText.Text = "RAID JETZT";
                DashboardPageViewHost.DashboardRaidCountdownText.Text = "Countdown übersprungen · Stream wird beendet …";
                DashboardPageViewHost.DashboardRaidCountdownProgress.Value = seconds;
                DashboardPageViewHost.DashboardWorkflowStageText.Text = $"RAID → {displayName} · sofort";
                SetStreamEndStatus("Raid-Countdown übersprungen");
                _activeStreamEndDialog?.UpdateCountdown("Raid jetzt", "00:00", seconds, seconds);
                AddDashboardNotification(
                    $"Raid-Countdown zu {displayName} übersprungen – Streamende geht weiter.",
                    "Info");
                return true;
            }

            DashboardPageViewHost.DashboardRaidCountdownTitleText.Text = "RAID ABGEBROCHEN";
            DashboardPageViewHost.DashboardRaidCountdownText.Text = "Stream bleibt aktiv";
            DashboardPageViewHost.DashboardWorkflowStageText.Text = "RAID ABGEBROCHEN · STREAM LÄUFT WEITER";
            SetStreamEndStatus("Raid abgebrochen");
            return false;
        }
        finally
        {
            _raidCountdownActive = false;
            _raidCountdownSkipRequested = false;
            UpdateDashboardStreamEndModuleVisibility();
            UpdateDashboardRaidActionButtons();
        }
    }

    private void SkipActiveRaidCountdown()
    {
        if (!_raidCountdownActive)
        {
            return;
        }

        _raidCountdownSkipRequested = true;
        _raidCountdownCts?.Cancel();
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
            if (_streamEndFlowActive)
            {
                _awaitingManualRaid = true;
                UpdateDashboardStreamEndModuleVisibility();
                UpdateDashboardRaidActionButtons();
            }
        }
        catch (Exception exception)
        {
            AddDashboardNotification($"Raid konnte nicht abgebrochen werden: {exception.Message}", "Fehler");
        }
    }
}
