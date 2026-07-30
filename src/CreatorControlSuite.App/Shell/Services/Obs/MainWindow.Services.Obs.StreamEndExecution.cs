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
    private async Task ExecuteStreamEndFlowAsync(StreamEndMode mode)
    {
        CancelPlannedStreamEnd();
        _streamEndAbortRequested = false;

        try
        {
            _streamStartAutomationCts?.Cancel();
            _streamEndFlowActive = true;
            UpdateDashboardStreamEndModuleVisibility();

            if (mode == StreamEndMode.Immediate)
            {
                SetStreamEndStatus("Stream wird sofort beendet …");
                _activeStreamEndDialog?.UpdateCountdown("Sofort beenden", "—", 1, 1);
                await FinalizeObsStreamStopAsync();
                return;
            }

            DashboardPageViewHost.DashboardWorkflowStageText.Text = "STATISTIKEN ABSCHLIESSEN";
            SetWorkflowVisualStage("End", "Letzte Twitch- und Zuschauerwerte werden gespeichert.");
            await UpdateCurrentStreamStatsForEndSceneAsync(finalize: false);
            if (_streamEndAbortRequested || !_streamEndFlowActive)
            {
                return;
            }

            DashboardPageViewHost.DashboardWorkflowStageText.Text = "ENDSZENE";
            SetWorkflowVisualStage("End", "Endszene läuft. Streamende wird vorbereitet.");
            SetStreamEndStatus("Endszene läuft");

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

            int endSeconds = Math.Max(
                0,
                _settings.Twitch.EndSceneDurationSeconds > 0
                    ? _settings.Twitch.EndSceneDurationSeconds
                    : _settings.Workflow.EndSceneSeconds);

            bool wantsRaid = mode == StreamEndMode.EndSceneRaidThenStop
                && !string.IsNullOrWhiteSpace(_settings.Twitch.SelectedRaidChannel);

            Task? endSceneTask = null;
            Task<bool>? raidTask = null;

            if (wantsRaid)
            {
                _awaitingManualRaid = true;
                EnsureStreamEndRaidDecisionTcs();
                UpdateDashboardStreamEndModuleVisibility();
                UpdateDashboardRaidActionButtons();
                SetStreamEndStatus("Raid startet sofort (/raid) …");
                DashboardPageViewHost.DashboardWorkflowStageText.Text = "RAID STARTET SOFORT";
                AddDashboardNotification(
                    $"Raid-Befehl (/raid {RaidChatCommand.NormalizeLogin(_settings.Twitch.SelectedRaidChannel)}) wird sofort gesendet. „JETZT RAIDEN“ überspringt den Countdown.",
                    "Info");
                _ = RefreshRaidTargetStatusAsync(_settings.Twitch.SelectedRaidChannel);
                // Sofort starten – nicht erst nach der Endszene warten.
                // Endszene bleibt sichtbar; der Raid-Countdown steuert die Wartezeit.
                raidTask = TryExecuteRaidWithRetriesAsync(_settings.Twitch.SelectedRaidChannel);
            }
            else if (endSeconds > 0)
            {
                endSceneTask = RunEndSceneCountdownAsync(endSeconds);
            }

            if (wantsRaid)
            {
                bool raided = await raidTask!;

                if (_streamEndAbortRequested || !_streamEndFlowActive)
                {
                    return;
                }

                if (raided)
                {
                    if (_settings.Twitch.StopStreamAfterRaid)
                    {
                        await FinalizeObsStreamStopAsync();
                    }
                    else
                    {
                        DashboardPageViewHost.DashboardWorkflowStageText.Text = "RAID AUSGEFÜHRT · STREAM LÄUFT WEITER";
                        AddDashboardNotification("Raid wurde ausgeführt. Automatisches Streamende ist deaktiviert.", "Info");
                        ResetStreamEndFlowState();
                    }

                    return;
                }

                await FinalizeObsStreamStopAsync();
                return;
            }

            if (endSceneTask is not null)
            {
                await endSceneTask;
            }

            if (_streamEndAbortRequested || !_streamEndFlowActive)
            {
                return;
            }

            await FinalizeObsStreamStopAsync();
        }
        catch (Exception exception)
        {
            ResetStreamEndFlowState();
            AddDashboardNotification($"Streamende fehlgeschlagen: {exception.Message}", "Fehler");
            throw;
        }
    }

    /// <summary>
    /// Immediately after stream-end starts (with raid): poll target status and retry StartRaid
    /// until success, skip, abort, permanent error, or timeout – then run the local countdown.
    /// „JETZT RAIDEN“ skips that countdown; RAID ABBRECHEN cancels the Twitch raid.
    /// </summary>
    private async Task<bool> TryExecuteRaidWithRetriesAsync(string raidChannel)
    {
        _raidAutoStartCts?.Cancel();
        _raidAutoStartCts?.Dispose();
        _raidAutoStartCts = new CancellationTokenSource();
        CancellationToken token = _raidAutoStartCts.Token;

        int timeoutSeconds = RaidStartPolicy.ClampTimeoutSeconds(
            _settings.Twitch.RaidStartTimeoutSeconds);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        CancellationToken timeoutToken = timeoutCts.Token;

        int attempt = 0;
        AddDashboardNotification(
            $"Raid startet sofort: Ziel „{raidChannel}“ wird bis zu {timeoutSeconds}s geprüft und per /raid gestartet.",
            "Info");

        try
        {
            while (!timeoutToken.IsCancellationRequested)
            {
                if (_streamEndAbortRequested || !_streamEndFlowActive)
                {
                    return false;
                }

                if (_streamEndRaidDecisionTcs is { Task.IsCompleted: true })
                {
                    return await _streamEndRaidDecisionTcs.Task;
                }

                // Manual start already running – wait for its decision.
                if (_raidCountdownActive)
                {
                    EnsureStreamEndRaidDecisionTcs();
                    return await _streamEndRaidDecisionTcs!.Task;
                }

                attempt++;
                SetStreamEndStatus($"Auto-Raid Versuch {attempt} …");
                UpdateDashboardRaidActionButtons();

                TwitchRaidTargetStatus? status;
                try
                {
                    status = await _twitchModule.GetRaidTargetStatusAsync(raidChannel, timeoutToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception statusException)
                {
                    AddDashboardNotification(
                        $"Raid-Status fehlgeschlagen: {statusException.Message}",
                        "Warnung");
                    if (RaidStartPolicy.DecideAfterStartError(statusException) == RaidStartDecision.GiveUp)
                    {
                        EnsureStreamEndRaidDecisionTcs();
                        _streamEndRaidDecisionTcs!.TrySetResult(false);
                        return false;
                    }

                    await Task.Delay(RaidStartPolicy.GetRetryDelay(attempt), timeoutToken);
                    continue;
                }

                RaidStartDecision statusDecision = RaidStartPolicy.DecideAfterStatus(
                    targetFound: status is not null,
                    isOnline: status?.IsOnline == true);

                if (status is not null)
                {
                    _raidTargetIsOnline = status.IsOnline;
                    SetRaidTargetStatusText(
                        status.IsOnline
                            ? $"{status.DisplayName} ist ONLINE · {status.ViewerCount} Zuschauer · {status.GameName}"
                            : $"{status.DisplayName} ist OFFLINE");
                    UpdateDashboardRaidActionButtons();
                }
                else
                {
                    _raidTargetIsOnline = false;
                    SetRaidTargetStatusText($"Kanal „{raidChannel}“ nicht gefunden");
                    UpdateDashboardRaidActionButtons();
                }

                if (statusDecision == RaidStartDecision.KeepPolling)
                {
                    SetStreamEndStatus(
                        status is null
                            ? "Auto-Raid · Kanal nicht gefunden – warte …"
                            : "Auto-Raid · Ziel offline – warte …");
                    await Task.Delay(RaidStartPolicy.PollInterval, timeoutToken);
                    continue;
                }

                // AttemptStart
                try
                {
                    await _twitchModule.StartRaidAsync(raidChannel, timeoutToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception startException)
                {
                    RaidStartDecision startDecision =
                        RaidStartPolicy.DecideAfterStartError(startException);
                    AddDashboardNotification(
                        $"Auto-Raid: {startException.Message}",
                        startDecision == RaidStartDecision.GiveUp ? "Fehler" : "Warnung");
                    SetStreamEndStatus(startException.Message);

                    if (startDecision == RaidStartDecision.GiveUp)
                    {
                        EnsureStreamEndRaidDecisionTcs();
                        _streamEndRaidDecisionTcs!.TrySetResult(false);
                        return false;
                    }

                    await Task.Delay(RaidStartPolicy.GetRetryDelay(attempt), timeoutToken);
                    continue;
                }

                string displayName = status?.DisplayName ?? raidChannel;
                AddDashboardNotification(
                    $"Raid-Befehl (/raid {RaidChatCommand.NormalizeLogin(raidChannel)}) zu {displayName} wurde gestartet.",
                    "Info");
                SetWorkflowVisualStage("Raid", $"Raid zu {displayName} wird gestartet.");

                bool raidCompleted = await RunRaidCountdownAsync(
                    displayName,
                    Math.Clamp(_settings.Twitch.RaidCountdownSeconds, 5, 300));

                if (!raidCompleted)
                {
                    // User cancelled the Twitch raid – keep trying until timeout/skip.
                    if (_streamEndAbortRequested || !_streamEndFlowActive)
                    {
                        return false;
                    }

                    if (_streamEndRaidDecisionTcs is { Task.IsCompleted: true })
                    {
                        return await _streamEndRaidDecisionTcs.Task;
                    }

                    _awaitingManualRaid = true;
                    EnsureStreamEndRaidDecisionTcs();
                    UpdateDashboardStreamEndModuleVisibility();
                    UpdateDashboardRaidActionButtons();
                    SetStreamEndStatus("Raid abgebrochen · Auto-Versuch läuft weiter …");
                    await Task.Delay(RaidStartPolicy.PollInterval, timeoutToken);
                    continue;
                }

                if (_settings.Twitch.StopSpotifyAfterRaid)
                {
                    try
                    {
                        await _spotifyModule.PauseAsync();
                    }
                    catch (Exception spotifyException)
                    {
                        AddDashboardNotification(
                            $"Spotify konnte nach dem Raid nicht pausiert werden: {spotifyException.Message}",
                            "Warnung");
                    }
                }

                _awaitingManualRaid = false;
                EnsureStreamEndRaidDecisionTcs();
                _streamEndRaidDecisionTcs!.TrySetResult(true);
                return true;
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout, skip, or abort.
        }

        if (_streamEndRaidDecisionTcs is { Task.IsCompleted: true })
        {
            return await _streamEndRaidDecisionTcs.Task;
        }

        if (_streamEndAbortRequested || !_streamEndFlowActive)
        {
            return false;
        }

        AddDashboardNotification(
            $"Auto-Raid-Timeout ({timeoutSeconds}s) – Stream wird ohne Raid beendet.",
            "Warnung");
        SetStreamEndStatus("Auto-Raid Timeout · Ende ohne Raid");
        EnsureStreamEndRaidDecisionTcs();
        _streamEndRaidDecisionTcs!.TrySetResult(false);
        return false;
    }
}
