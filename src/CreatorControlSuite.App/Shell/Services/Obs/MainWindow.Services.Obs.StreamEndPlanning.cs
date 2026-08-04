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
    private async Task StartPlannedStreamEndAsync()
    {
        if (_plannedStreamEndActive || _streamEndFlowActive)
        {
            return;
        }

        if (!int.TryParse(DashboardPageViewHost.DashboardPlannedStreamEndSecondsBox.Text.Trim(), out int seconds) || seconds < 1)
        {
            AddDashboardNotification("Bitte eine gültige Sekundenanzahl für das geplante Streamende eingeben.", "Warnung");
            return;
        }

        MessageBoxResult confirm = MessageBox.Show(
            $"Streamende in {seconds} Sekunden planen? Danach startet der Endszene-/Raid-Ablauf automatisch.",
            "Streamende planen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _settings.Twitch.PlannedStreamEndSeconds = seconds;
        await _settingsStore.SaveAsync(_settings);

        _plannedStreamEndCts?.Cancel();
        _plannedStreamEndCts?.Dispose();
        _plannedStreamEndCts = new CancellationTokenSource();
        CancellationToken token = _plannedStreamEndCts.Token;
        _plannedStreamEndActive = true;
        UpdateDashboardStreamEndModuleVisibility();

        int totalSeconds = seconds;
        DashboardPageViewHost.DashboardStreamEndCountdownLabel.Text = "Zeit bis Streamende (geplant)";
        DashboardPageViewHost.DashboardStreamEndCountdownProgress.Minimum = 0;
        DashboardPageViewHost.DashboardStreamEndCountdownProgress.Maximum = Math.Max(1, totalSeconds);
        SetStreamEndStatus("Geplantes Streamende aktiv");
        AddDashboardNotification($"Streamende in {seconds} Sekunden geplant.", "Info");

        try
        {
            for (int remaining = totalSeconds; remaining >= 0; remaining--)
            {
                token.ThrowIfCancellationRequested();
                DashboardPageViewHost.DashboardStreamEndCountdownText.Text = FormatCountdownClock(remaining);
                DashboardPageViewHost.DashboardStreamEndCountdownProgress.Value = totalSeconds - remaining;
                DashboardPageViewHost.DashboardWorkflowStageText.Text = $"GEPLANTES STREAMENDE · noch {FormatCountdownClock(remaining)}";
                if (remaining > 0)
                {
                    await Task.Delay(1000, token);
                }
            }

            _plannedStreamEndActive = false;
            UpdateDashboardStreamEndModuleVisibility();
            await StopObsStreamAsync(skipConfirmation: true);
        }
        catch (OperationCanceledException)
        {
            DashboardPageViewHost.DashboardStreamEndCountdownText.Text = "—";
            DashboardPageViewHost.DashboardStreamEndCountdownProgress.Value = 0;
            SetStreamEndStatus("Planung abgebrochen");
            DashboardPageViewHost.DashboardWorkflowStageText.Text = "GEPLANTES STREAMENDE ABGEBROCHEN";
            AddDashboardNotification("Geplantes Streamende wurde abgebrochen.", "Info");
        }
        finally
        {
            _plannedStreamEndActive = false;
            UpdateDashboardStreamEndModuleVisibility();
        }
    }

    private void CancelPlannedStreamEnd()
    {
        if (!_plannedStreamEndActive)
        {
            return;
        }

        _plannedStreamEndCts?.Cancel();
    }

    private static string FormatCountdownClock(int totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
            : $"{ts.Minutes:00}:{ts.Seconds:00}";
    }

    private void SkipRaidAndFinishStreamEnd()
    {
        if (_streamEndRaidDecisionTcs is null || _streamEndRaidDecisionTcs.Task.IsCompleted)
        {
            _raidAutoStartCts?.Cancel();
            return;
        }

        _awaitingManualRaid = false;
        _streamEndRaidDecisionTcs.TrySetResult(false);
        _raidAutoStartCts?.Cancel();
        SetStreamEndStatus("Beenden ohne Raid");
        UpdateDashboardStreamEndModuleVisibility();
    }

    private async Task ExecuteRaidFromDashboardAsync()
    {
        if (_raidCountdownActive)
        {
            // Twitch entscheidet über die tatsächliche Ausführung. Erst das
            // ausgehende Raid-Event darf anschließend den Stream beenden.
            await RequestImmediateRaidAsync();
            return;
        }

        string? raidChannel = (DashboardPageViewHost.DashboardRaidChannelBox.SelectedItem as string
            ?? _settings.Twitch.SelectedRaidChannel)?.Trim();
        if (string.IsNullOrWhiteSpace(raidChannel))
        {
            AddDashboardNotification("Kein Raid-Ziel ausgewählt.", "Warnung");
            return;
        }

        DashboardPageViewHost.DashboardStartRaidButton.IsEnabled = false;
        SetStreamEndStatus("Raid wird gestartet …");

        try
        {
            TwitchRaidTargetStatus? raidStatus = await _twitchModule.GetRaidTargetStatusAsync(raidChannel);
            if (raidStatus is null)
            {
                AddDashboardNotification("Raid abgebrochen: Kanal nicht gefunden.", "Fehler");
                SetStreamEndStatus("Kanal nicht gefunden");
                UpdateDashboardRaidActionButtons();
                return;
            }

            _raidTargetIsOnline = raidStatus.IsOnline;
            SetRaidTargetStatusText(
                raidStatus.IsOnline
                    ? $"{raidStatus.DisplayName} ist ONLINE · {raidStatus.ViewerCount} Zuschauer · {raidStatus.GameName}" +
                      (string.IsNullOrWhiteSpace(raidStatus.StreamTitle) ? "" : $" · {raidStatus.StreamTitle}")
                    : $"{raidStatus.DisplayName} ist OFFLINE");

            if (!raidStatus.IsOnline)
            {
                AddDashboardNotification($"Raid nicht möglich: {raidStatus.DisplayName} ist offline.", "Warnung");
                SetStreamEndStatus("Ziel offline");
                UpdateDashboardRaidActionButtons();
                return;
            }

            // Früher Raid während Endszene: Countdown der Endszene abbrechen.
            _endSceneCountdownCts?.Cancel();

            try
            {
                await _twitchModule.StartRaidAsync(raidChannel);
            }
            catch (Exception startException)
            {
                AddDashboardNotification(
                    $"Twitch erlaubt den Raid noch nicht: {startException.Message}",
                    "Warnung");
                SetStreamEndStatus("Twitch erlaubt Raid noch nicht");
                UpdateDashboardRaidActionButtons();
                return;
            }

            AddDashboardNotification(
                $"Raid-Befehl (/raid {RaidChatCommand.NormalizeLogin(raidChannel)}) zu {raidStatus.DisplayName} wurde gestartet.",
                "Info");
            SetWorkflowVisualStage("Raid", $"Raid zu {raidStatus.DisplayName} wird gestartet.");

            bool raidCompleted = await RunRaidCountdownAsync(
                raidStatus.DisplayName,
                Math.Clamp(_settings.Twitch.RaidCountdownSeconds, 5, 300));

            if (!raidCompleted)
            {
                if (_streamEndFlowActive)
                {
                    _awaitingManualRaid = true;
                    EnsureStreamEndRaidDecisionTcs();
                    UpdateDashboardStreamEndModuleVisibility();
                    UpdateDashboardRaidActionButtons();
                }

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

            if (_streamEndFlowActive)
            {
                _awaitingManualRaid = false;
                EnsureStreamEndRaidDecisionTcs();
                _streamEndRaidDecisionTcs!.TrySetResult(true);
                return;
            }

            // Raid außerhalb des End-Flows: optional Stream beenden (ohne erneute Endszene).
            if (_settings.Twitch.StopStreamAfterRaid)
            {
                MessageBoxResult confirm = MessageBox.Show(
                    "Raid ist durch. OBS-Stream jetzt beenden?",
                    "Stream nach Raid beenden",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (confirm == MessageBoxResult.Yes)
                {
                    _streamEndFlowActive = true;
                    await FinalizeObsStreamStopAsync();
                }
            }
        }
        catch (Exception exception)
        {
            AddDashboardNotification($"Raid fehlgeschlagen: {exception.Message}", "Fehler");
            SetStreamEndStatus("Raid fehlgeschlagen");
            UpdateDashboardRaidActionButtons();
        }
    }

    private void EnsureStreamEndRaidDecisionTcs()
    {
        if (_streamEndRaidDecisionTcs is null || _streamEndRaidDecisionTcs.Task.IsCompleted)
        {
            _streamEndRaidDecisionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private void ResetStreamEndFlowState()
    {
        _streamEndFlowActive = false;
        _awaitingManualRaid = false;
        _streamEndRaidDecisionTcs = null;
        _endSceneCountdownCts?.Cancel();
        _raidAutoStartCts?.Cancel();
        _outgoingRaidCompletedTcs?.TrySetCanceled();
        _outgoingRaidCompletedTcs = null;
        _activeOutgoingRaidTarget = "";
        UpdateDashboardStreamEndModuleVisibility();
        UpdateDashboardRaidActionButtons();
    }

    private async Task RunEndSceneCountdownAsync(int endSeconds)
    {
        _endSceneCountdownCts?.Cancel();
        _endSceneCountdownCts?.Dispose();
        _endSceneCountdownCts = new CancellationTokenSource();
        CancellationToken token = _endSceneCountdownCts.Token;

        DashboardPageViewHost.DashboardStreamEndCountdownLabel.Text = "Zeit bis Streamende (Endszene)";
        DashboardPageViewHost.DashboardStreamEndCountdownProgress.Minimum = 0;
        DashboardPageViewHost.DashboardStreamEndCountdownProgress.Maximum = Math.Max(1, endSeconds);
        SetStreamEndStatus("Endszene läuft");

        try
        {
            for (int remaining = endSeconds; remaining > 0; remaining--)
            {
                token.ThrowIfCancellationRequested();
                string clock = FormatCountdownClock(remaining);
                DashboardPageViewHost.DashboardStreamEndCountdownText.Text = clock;
                DashboardPageViewHost.DashboardStreamEndCountdownProgress.Value = endSeconds - remaining;
                DashboardPageViewHost.DashboardWorkflowStageText.Text = $"ENDSZENE · Streamende in {remaining}s";
                _activeStreamEndDialog?.UpdateCountdown(
                    "Endszene",
                    clock,
                    endSeconds - remaining,
                    endSeconds);
                await Task.Delay(1000, token);
            }

            DashboardPageViewHost.DashboardStreamEndCountdownText.Text = "00:00";
            DashboardPageViewHost.DashboardStreamEndCountdownProgress.Value = endSeconds;
            _activeStreamEndDialog?.UpdateCountdown("Endszene", "00:00", endSeconds, endSeconds);
        }
        catch (OperationCanceledException)
        {
            // Früher Raid oder Abbruch – UI bleibt im aktuellen Zustand.
        }
    }

    private async Task FinalizeObsStreamStopAsync()
    {
        // Erst unmittelbar vor dem tatsächlichen OBS-Stopp wird die
        // Streamdauer eingefroren. So zählt die komplette Endszene mit.
        await UpdateCurrentStreamStatsForEndSceneAsync(finalize: true);

        Exception? lastStopError = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                if (!_obsClient.IsConnected)
                {
                    AddDashboardNotification(
                        attempt == 1
                            ? "OBS nicht verbunden – versuche Reconnect vor Stream-Stop…"
                            : $"OBS-Reconnect Versuch {attempt}/3…",
                        "Warnung");
                    await ConnectObsAsync(showErrorDialog: false);
                }

                if (!_obsClient.IsConnected)
                {
                    throw new InvalidOperationException("OBS ist nicht verbunden.");
                }

                await _obsClient.StopStreamAsync();
                lastStopError = null;
                break;
            }
            catch (Exception stopException)
            {
                lastStopError = stopException;
                if (attempt < 3)
                {
                    await Task.Delay(1000);
                }
            }
        }

        if (lastStopError is not null)
        {
            throw lastStopError;
        }

        if (!string.IsNullOrWhiteSpace(_settings.Obs.StartScene))
        {
            try
            {
                if (_obsClient.IsConnected)
                {
                    await _obsClient.SetCurrentProgramSceneAsync(_settings.Obs.StartScene);
                }
            }
            catch (Exception sceneException)
            {
                AddDashboardNotification(
                    $"Startszene konnte nach Streamende nicht gesetzt werden: {sceneException.Message}",
                    "Warnung");
            }
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
        DashboardHeroViewerText.Text = "0";
        RefreshCommunityUi();
        if (_settings.Dashboard.AutoExitFocusModeOnStreamEnd &&
            _dashboardFocusModeActive)
        {
            ExitDashboardFocusMode();
        }
        await SaveCurrentStreamHistoryAsync();
        DashboardPageViewHost.DashboardWorkflowStageText.Text = "STREAM BEENDET";
        SetStreamEndStatus("Stream beendet");
        AddDashboardNotification("OBS-Stream wurde beendet.", "Info");
        ResetStreamEndFlowState();
        await Task.Delay(500);
        await RefreshObsAsync();
        await LoadStreamHistoryAsync();
        await _statisticsPageViewModel.LoadAsync(
            GetStreamHistoryFilePath());
    }

    private void SetStreamEndStatus(string text)
    {
        DashboardPageViewHost.DashboardStreamEndStatusText.Text = text;
        _activeStreamEndDialog?.SetStatus(text);
    }

    private async Task StopObsStreamAsync(bool skipConfirmation = false, bool skipRaidPhase = false)
    {
        if (_streamEndFlowActive && !skipRaidPhase)
        {
            return;
        }

        if (!skipConfirmation)
        {
            await ShowStreamEndDialogAndRunAsync();
            return;
        }

        StreamEndMode mode = skipRaidPhase
            ? StreamEndMode.EndSceneThenStop
            : (_settings.Twitch.RaidOnStreamEnd
                ? StreamEndMode.EndSceneRaidThenStop
                : StreamEndMode.EndSceneThenStop);
        await ExecuteStreamEndFlowAsync(mode);
    }

    private async Task ShowStreamEndDialogAndRunAsync()
    {
        if (_activeStreamEndDialog is not null)
        {
            _activeStreamEndDialog.Activate();
            return;
        }

        int endSeconds = Math.Max(
            0,
            _settings.Twitch.EndSceneDurationSeconds > 0
                ? _settings.Twitch.EndSceneDurationSeconds
                : _settings.Workflow.EndSceneSeconds);
        var channels = _settings.Twitch.RaidChannels
            .Select(channel => channel.Trim().TrimStart('@'))
            .Where(channel => !string.IsNullOrWhiteSpace(channel))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var dialog = new StreamEndDialogWindow(
            _settings.Twitch.StreamEndMode,
            channels,
            _settings.Twitch.SelectedRaidChannel,
            endSeconds,
            OpenRaidChannelByName,
            SuggestRaidTargetsAsync,
            OnStreamEndRaidTargetChanged)
        {
            Owner = this
        };
        _activeStreamEndDialog = dialog;

        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void Cleanup()
        {
            if (ReferenceEquals(_activeStreamEndDialog, dialog))
            {
                _activeStreamEndDialog = null;
            }

            finished.TrySetResult();
        }

        dialog.Closed += (_, _) => Cleanup();
        dialog.StartRaidRequested += () => _ = ExecuteRaidFromDashboardAsync();
        dialog.SkipRaidRequested += SkipRaidAndFinishStreamEnd;
        dialog.CancelRaidRequested += () => _ = CancelActiveRaidAsync();
        dialog.CancelFlowRequested += () =>
        {
            if (!_streamEndFlowActive)
            {
                dialog.Close();
            }
            else
            {
                AbortStreamEndFlowFromDialog();
            }
        };
        dialog.SelectionConfirmed += (mode, channel, endSceneSeconds) =>
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    _settings.Twitch.StreamEndMode = mode;
                    if (mode is StreamEndMode.EndSceneThenStop or StreamEndMode.EndSceneRaidThenStop)
                    {
                        _settings.Twitch.EndSceneDurationSeconds = endSceneSeconds;
                        _settings.Workflow.EndSceneSeconds = endSceneSeconds;
                        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchEndSceneSecondsBox.Text = endSceneSeconds.ToString();
                        SettingsPageViewHost.EndSceneSecondsBox.Text = endSceneSeconds.ToString();
                    }

                    if (mode == StreamEndMode.EndSceneRaidThenStop)
                    {
                        if (!string.IsNullOrWhiteSpace(channel))
                        {
                            RememberRaidChannel(channel!);
                            DashboardPageViewHost.DashboardRaidChannelBox.SelectedItem = channel;
                            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidTargetBox.SelectedItem = channel;
                        }

                        _settings.Twitch.RaidOnStreamEnd = true;
                        DashboardPageViewHost.DashboardRaidEnabledBox.IsChecked = true;
                        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidEnabledBox.IsChecked = true;
                    }

                    await _settingsStore.SaveAsync(_settings);

                    dialog.EnterRunningPhase(
                        mode == StreamEndMode.Immediate ? "Stream wird beendet" : "Endszene",
                        mode == StreamEndMode.Immediate
                            ? "Stream wird sofort gestoppt …"
                            : $"Ablauf gestartet … ({endSceneSeconds}s Endszene)");

                    await ExecuteStreamEndFlowAsync(mode);
                    if (_streamEndAbortRequested)
                    {
                        dialog.MarkCompleted("Streamende abgebrochen. Stream läuft weiter.");
                    }
                    else
                    {
                        dialog.MarkCompleted("Stream beendet.");
                    }
                }
                catch (Exception exception)
                {
                    ResetStreamEndFlowState();
                    dialog.MarkCompleted($"Streamende fehlgeschlagen: {exception.Message}");
                    AddDashboardNotification($"Streamende fehlgeschlagen: {exception.Message}", "Fehler");
                }
            });
        };

        dialog.Show();
        await finished.Task;
    }

    private void OpenRaidChannelByName(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            return;
        }

        string url = "https://www.twitch.tv/" + Uri.EscapeDataString(channel.Trim().TrimStart('@'));
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void AbortStreamEndFlowFromDialog()
    {
        _streamEndAbortRequested = true;
        _endSceneCountdownCts?.Cancel();
        _raidCountdownCts?.Cancel();
        _raidAutoStartCts?.Cancel();
        if (_streamEndRaidDecisionTcs is { Task.IsCompleted: false })
        {
            _streamEndRaidDecisionTcs.TrySetResult(false);
        }

        if (_raidCountdownActive)
        {
            _ = CancelTwitchRaidBestEffortAsync("Streamende abgebrochen");
        }

        ResetStreamEndFlowState();
        _activeStreamEndDialog?.MarkCompleted("Streamende abgebrochen. Stream läuft weiter.");
    }

    private async Task CancelTwitchRaidBestEffortAsync(string context)
    {
        try
        {
            await _twitchModule.CancelRaidAsync();
            AddDashboardNotification($"{context}: Twitch-Raid wurde abgebrochen.", "Info");
        }
        catch (Exception exception)
        {
            AddDashboardNotification(
                $"{context}: Twitch-Raid konnte nicht abgebrochen werden ({exception.Message}).",
                "Warnung");
        }
    }
}
