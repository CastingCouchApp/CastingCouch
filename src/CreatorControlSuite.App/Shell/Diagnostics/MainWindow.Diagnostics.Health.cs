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
    private async Task RunDiagnosticsAsync()
    {
        try
        {
            await _diagnosticsPageViewModel.LoadStatusesAsync();

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
        ValidationReport report = _settingsApplicationService.Validate(_settings);
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

            bool twitchConnected =
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

            if (IsSpotifyMusicProvider() &&
                _settings.General.ReconnectSpotify &&
                (_settings.Spotify.AutoConnect || _settings.Spotify.ConnectOnPrepare) &&
                !_spotifyModule.GetSnapshot().Authenticated &&
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
            else if (IsYouTubeMusicProvider() &&
                     _settings.General.ReconnectYouTubeMusic &&
                     (_settings.YouTubeMusic.AutoConnect || _settings.YouTubeMusic.ConnectOnPrepare) &&
                     !_youTubeMusicModule.IsBridgeRunning &&
                     CanAttemptReconnect("YouTube Music"))
            {
                MarkReconnectAttempt("YouTube Music");
                AddDashboardNotification(
                    "YouTube-Music-Bridge unterbrochen. Automatischer Neustart wird versucht.",
                    "Warnung");
                try
                {
                    await _musicPlayerRouter.ApplyProviderAsync(MusicProviderIds.YouTubeMusic);
                    await _musicPlayerRouter.ConnectActiveAsync();
                    if (_youTubeMusicModule.IsBridgeRunning)
                    {
                        AddDashboardNotification(
                            "YouTube-Music-Bridge wurde automatisch neu gestartet.",
                            "Info");
                    }
                }
                catch
                {
                    // Watchdog bleibt resilient.
                }
            }

            bool streamerBotConnected = _streamerBotClient.IsConnected;

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

                if (_streamerBotClient.IsConnected)
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
                out DateTimeOffset lastAttempt))
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
}
