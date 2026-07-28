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
    private Task PrepareStreamAsync() => PrepareStreamWithConfiguredServicesAsync();

    private async Task PrepareStreamWithConfiguredServicesAsync()
    {
        try
        {
            SetPrepareProgress(5, "Programme werden gestartet …", true);
            if (_settings.Obs.ConnectOnPrepare)
            {
                LaunchConfiguredExecutable(_settings.Obs.ExecutablePath, "OBS", showMissingMessage: false);
            }

            if (_settings.Spotify.ConnectOnPrepare)
            {
                LaunchConfiguredExecutable(_settings.Spotify.ExecutablePath, "Spotify", showMissingMessage: false);
            }

            if (_settings.StreamerBot.ConnectOnPrepare)
            {
                LaunchConfiguredExecutable(_settings.StreamerBot.ExecutablePath, "Streamer.bot", showMissingMessage: false);
            }

            if (_settings.Twitch.ConnectOnPrepare && !string.IsNullOrWhiteSpace(_settings.Twitch.CreatorDashboardUrl))
            {
                OpenConfiguredTarget(_settings.Twitch.CreatorDashboardUrl, "Twitch Creator Dashboard", showMissingMessage: false);
            }

            SetPrepareProgress(20, "Warte auf gestartete Dienste …", true);
            await Task.Delay(1500);

            SetPrepareProgress(35, "OBS wird gestartet und vorbereitet …", true);
            if (_settings.Obs.ConnectOnPrepare)
            {
                await WaitForObsReadyDuringPreparationAsync();
            }

            SetPrepareProgress(50, "Twitch wird verbunden …", true);
            if (_settings.Twitch.ConnectOnPrepare && !_twitchModule.GetSnapshot().Authenticated)
            {
                await ConnectTwitchAsync(showErrorDialog: false);
            }

            SetPrepareProgress(65, "Music Player wird verbunden …", true);
            if (IsSpotifyMusicProvider() &&
                _settings.Spotify.ConnectOnPrepare &&
                !_spotifyModule.GetSnapshot().Authenticated)
            {
                await ConnectSpotifyAsync(showErrorDialog: false);
            }
            else if (IsYouTubeMusicProvider() &&
                     _settings.YouTubeMusic.ConnectOnPrepare &&
                     !_youTubeMusicModule.IsBridgeRunning)
            {
                await _musicPlayerRouter.ApplyProviderAsync(MusicProviderIds.YouTubeMusic);
                await _musicPlayerRouter.ConnectActiveAsync();
            }

            SetPrepareProgress(78, "Streamer.bot wird verbunden …", true);
            if (_settings.StreamerBot.ConnectOnPrepare && (!_streamerBotClient.IsConnected))
            {
                await ConnectStreamerBotAsync();
            }

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

        for (int attempt = 1; attempt <= maximumAttempts; attempt++)
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
            double normalizedValue = Math.Clamp(value, 0, 100);
            DashboardPageViewHost.DashboardPrepareProgressBar.Value = normalizedValue;
            DashboardPageViewHost.DashboardPrepareProgressPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            DashboardPageViewHost.DashboardPrepareProgressText.Text = message;
            DashboardPageViewHost.DashboardPrepareProgressPercentText.Text = $"{normalizedValue:0} %";
            DashboardPageViewHost.DashboardCommandCenterSummaryText.Text = message;
        }

        if (Dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.BeginInvoke(Apply);
        }
    }

    private async Task StartLegacyStreamAutomationAsync()
    {
        _streamStartAutomationCts?.Cancel();
        _streamStartAutomationCts?.Dispose();
        _streamStartAutomationCts = new CancellationTokenSource();
        CancellationToken token = _streamStartAutomationCts.Token;
        string startScene = string.IsNullOrWhiteSpace(_settings.Obs.StartScene) ? "Start" : _settings.Obs.StartScene.Trim();

        try
        {
            _streamSessionStartedAt ??= DateTimeOffset.Now;
            await _creatorIntelligence.StartSessionAsync(_streamSessionStartedAt.Value, DashboardPageViewHost.DashboardTwitchTitleBox.Text, DashboardPageViewHost.DashboardTwitchCategorySearchBox.Text);
            await _workflowModule.Service.ResetSessionStatsAsync(_streamSessionStartedAt);
            await RefreshTwitchFollowerCountAsync(initializeStreamBaseline: true);
            // Keinen erzwungenen Szenenwechsel: die aktuelle OBS-Szene bleibt bestehen.
            // Legacy steuert nur noch die Intro-Quelle "Start_Testbild" in der konfigurierten Startszene.
            if (_obsClient.IsConnected)
            {
                await _obsClient.SetSceneItemEnabledAsync(startScene, "Start_Testbild", true, token);
            }

            await UpdateActiveOverlayJsonAsync(root =>
            {
                JsonObject stream = root["stream"] as JsonObject ?? [];
                stream["isLive"] = true;
                stream["phase"] = "Starting";
                stream["startedAt"] = _streamSessionStartedAt;
                stream["elapsedSeconds"] = 0;
                stream["startTimerSeconds"] = 600;
                root["stream"] = stream;
            });

            await Task.Delay(TimeSpan.FromMinutes(5), token);
            if (_obsClient.IsConnected)
            {
                await _obsClient.SetSceneItemEnabledAsync(startScene, "Start_Testbild", false, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _appLogger.Write(AppLogLevel.Warning, "Automation", "10-Minuten-Streamstart-Automation fehlgeschlagen: " + ex.Message, ex);
        }
    }
}
