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

            // Twitch live data — parallel Helix calls for lower latency
            if (_twitchModule.GetSnapshot().Authenticated)
            {
                try
                {
                    await Task.WhenAll(
                        RefreshLiveViewerSampleAsync(),
                        RefreshTwitchFollowerCountAsync(),
                        RefreshTwitchGoalsAsync());
                    RefreshTwitchUi();
                    RefreshCommunityUi();
                }
                catch
                {
                    // Keep the refresh loop alive even if Twitch rate limits temporarily.
                }
            }

            // Music playback state (aktiver Provider)
            if (IsSpotifyMusicProvider() && _spotifyModule.GetSnapshot().Authenticated)
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
            else if (IsSpotifyMusicProvider())
            {
                RefreshSpotifyUi();
            }

            await RefreshMusicPlayerUiAsync();

            // Streamer.bot top status
            bool streamerBotConnected =
                _streamerBotClient.IsConnected;

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
            ObsStreamStatus stream = await _obsClient.GetStreamStatusAsync();
            ObsStats stats = await _obsClient.GetStatsAsync();
            DateTimeOffset now = DateTimeOffset.Now;

            if (_lastObsBitrateSampleAt.HasValue && stream.OutputBytes >= _lastObsOutputBytes)
            {
                double seconds = Math.Max(0.25, (now - _lastObsBitrateSampleAt.Value).TotalSeconds);
                _currentObsBitrateKbps = (stream.OutputBytes - _lastObsOutputBytes) * 8d / seconds / 1000d;
            }
            else if (!stream.OutputActive)
            {
                _currentObsBitrateKbps = 0;
            }

            _lastObsOutputBytes = stream.OutputBytes;
            _lastObsBitrateSampleAt = now;

            int outputDropped = Math.Max(stream.OutputSkippedFrames, stats.OutputSkippedFrames);
            int outputTotal = Math.Max(stream.OutputTotalFrames, stats.OutputTotalFrames);
            double droppedPercent = outputTotal > 0 ? outputDropped * 100d / outputTotal : 0d;
            double renderPercent = stats.RenderTotalFrames > 0 ? stats.RenderSkippedFrames * 100d / stats.RenderTotalFrames : 0d;

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
        WorkflowState state = _workflowModule.Service.State;

        items.Add(
            $"Workflow · {state.Phase} · {state.Detail}");

        if (_settings.Workflow.AutoSwitchScenes)
        {
            items.Add("Automatik · Szenenwechsel aktiv");
        }

        if (_settings.Twitch.RaidOnStreamEnd)
        {
            string raidTarget = string.IsNullOrWhiteSpace(
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

        DashboardPageViewHost.DashboardAutomationList.ItemsSource = items;
    }

    private void RefreshDashboardResourceUsage()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            DateTimeOffset now = DateTimeOffset.Now;
            TimeSpan cpuNow = process.TotalProcessorTime;
            double elapsedMs = Math.Max(1, (now - _lastDashboardResourceSample).TotalMilliseconds);
            double cpuMs = Math.Max(0, (cpuNow - _lastDashboardCpuTime).TotalMilliseconds);
            double cpu = Math.Clamp(cpuMs / elapsedMs / Math.Max(1, Environment.ProcessorCount) * 100.0, 0, 100);
            _lastDashboardCpuTime = cpuNow;
            _lastDashboardResourceSample = now;

            double ramMb = process.WorkingSet64 / 1024d / 1024d;
            long available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            double ramPercent = available > 0 ? Math.Clamp(process.WorkingSet64 / (double)available * 100.0, 0, 100) : 0;

            DashboardPageViewHost.DashboardCpuText.Text = $"CPU: {cpu:0}%";
            DashboardPageViewHost.DashboardCpuBar.Value = cpu;
            DashboardPageViewHost.DashboardRamText.Text = $"RAM: {ramMb:0} MB";
            DashboardPageViewHost.DashboardRamBar.Value = ramPercent;
        }
        catch
        {
            DashboardPageViewHost.DashboardCpuText.Text = "CPU: -";
            DashboardPageViewHost.DashboardRamText.Text = "RAM: -";
        }
    }
}
