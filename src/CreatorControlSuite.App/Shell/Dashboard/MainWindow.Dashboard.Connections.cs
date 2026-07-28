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

        object originalContent = button.Content;
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
        if (!IsSpotifyMusicProvider())
        {
            NowPlayingSnapshot snapshot = await _musicPlayerRouter.GetSnapshotAsync();
            if (snapshot.Connected || _youTubeMusicModule.IsBridgeRunning)
            {
                await _musicPlayerRouter.DisconnectActiveAsync();
                await RefreshMusicPlayerUiAsync();
                RefreshDashboardServiceActionButtons();
                AddDashboardNotification("YouTube Music wurde getrennt.", "Info");
                return;
            }

            await ExecuteMusicCommandAsync(() => _musicPlayerRouter.ConnectActiveAsync());
            RefreshDashboardServiceActionButtons();
            return;
        }

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
        bool connected =
            _streamerBotClient.IsConnected;

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
        bool obsConnected = _obsClient.IsConnected;
        bool twitchConnected = _twitchModule.GetSnapshot().Authenticated;
        bool musicConnected = GetActiveMusicConnected();
        bool streamerBotConnected =
            _streamerBotClient.IsConnected;
        string musicName = _musicPlayerRouter.ActiveDisplayName;

        DashboardPageViewHost.DashboardServiceConnectObsButton.Content =
            obsConnected ? "TRENNEN" : "VERBINDEN";

        DashboardPageViewHost.DashboardServiceConnectTwitchButton.Content =
            twitchConnected ? "TRENNEN" : "VERBINDEN";

        DashboardPageViewHost.DashboardServiceConnectSpotifyButton.Content =
            musicConnected ? "TRENNEN" : "VERBINDEN";

        DashboardPageViewHost.DashboardServiceConnectStreamerBotButton.Content =
            streamerBotConnected ? "TRENNEN" : "VERBINDEN";

        DashboardTopConnectObsButton.Content = DashboardPageViewHost.DashboardServiceConnectObsButton.Content;
        DashboardTopConnectTwitchButton.Content = DashboardPageViewHost.DashboardServiceConnectTwitchButton.Content;
        DashboardTopConnectSpotifyButton.Content = DashboardPageViewHost.DashboardServiceConnectSpotifyButton.Content;
        DashboardTopConnectStreamerBotButton.Content = DashboardPageViewHost.DashboardServiceConnectStreamerBotButton.Content;

        DashboardPageViewHost.DashboardServiceConnectObsButton.ToolTip =
            obsConnected
                ? "OBS-Verbindung trennen"
                : "OBS verbinden";

        DashboardPageViewHost.DashboardServiceConnectTwitchButton.ToolTip =
            twitchConnected
                ? "Twitch-Verbindung trennen"
                : "Twitch verbinden";

        DashboardPageViewHost.DashboardServiceConnectSpotifyButton.ToolTip =
            musicConnected
                ? $"{musicName}-Verbindung trennen"
                : $"{musicName} verbinden";

        DashboardPageViewHost.DashboardServiceConnectStreamerBotButton.ToolTip =
            streamerBotConnected
                ? "Streamer.bot-Verbindung trennen"
                : "Streamer.bot verbinden";

        DashboardTopConnectObsButton.ToolTip = DashboardPageViewHost.DashboardServiceConnectObsButton.ToolTip;
        DashboardTopConnectTwitchButton.ToolTip = DashboardPageViewHost.DashboardServiceConnectTwitchButton.ToolTip;
        DashboardTopConnectSpotifyButton.ToolTip = DashboardPageViewHost.DashboardServiceConnectSpotifyButton.ToolTip;
        DashboardTopConnectStreamerBotButton.ToolTip = DashboardPageViewHost.DashboardServiceConnectStreamerBotButton.ToolTip;

        RefreshDashboardConnectionSummary(
            obsConnected,
            twitchConnected,
            musicConnected,
            streamerBotConnected);
    }

    private void RefreshDashboardConnectionSummary(
        bool obsConnected,
        bool twitchConnected,
        bool musicConnected,
        bool streamerBotConnected)
    {
        const int total = 4;
        int connectedCount =
            (obsConnected ? 1 : 0) +
            (twitchConnected ? 1 : 0) +
            (musicConnected ? 1 : 0) +
            (streamerBotConnected ? 1 : 0);
        int brokenCount = total - connectedCount;
        bool allOk = brokenCount == 0;
        string musicName = _musicPlayerRouter.ActiveDisplayName;

        DashboardConnectionSummaryCount.Text = $"{connectedCount}/{total}";
        DashboardConnectionSummaryDetail.Text = allOk
            ? "alle online"
            : brokenCount == 1
                ? "1 offline"
                : $"{brokenCount} offline";
        DashboardConnectionSummaryDetail.Foreground = allOk
            ? (_themeService.GetBrush("SuccessBrush")
               ?? new SolidColorBrush(Color.FromRgb(0x4C, 0xD9, 0x64)))
            : (_themeService.GetBrush("WarningBrush")
               ?? new SolidColorBrush(Color.FromRgb(0xE8, 0xA2, 0x3A)));
        DashboardConnectionSummaryLamp.Fill = allOk
            ? (_themeService.GetBrush("SuccessBrush") ?? Brushes.LimeGreen)
            : (_themeService.GetBrush("DangerBrush") ?? Brushes.IndianRed);
        DashboardConnectionSummaryChip.BorderBrush = allOk
            ? (_themeService.GetBrush("SuccessBrush")
               ?? new SolidColorBrush(Color.FromRgb(0x2A, 0x3C, 0x32)))
            : (_themeService.GetBrush("WarningBrush")
               ?? new SolidColorBrush(Color.FromRgb(0x4A, 0x32, 0x2A)));
        DashboardConnectionSummaryChip.Background =
            _themeService.GetBrush("CardBackgroundBrush")
            ?? DashboardConnectionSummaryChip.Background;

        DashboardConnectionTooltipTitle.Text = allOk
            ? "Verbindungen"
            : brokenCount == 1
                ? "1 Verbindung offline"
                : $"{brokenCount} Verbindungen offline";

        DashboardConnectionTooltipListPanel.Children.Clear();
        DashboardConnectionTooltipListPanel.Children.Add(
            CreateConnectionTooltipRow("OBS", obsConnected));
        DashboardConnectionTooltipListPanel.Children.Add(
            CreateConnectionTooltipRow("Twitch", twitchConnected));
        DashboardConnectionTooltipListPanel.Children.Add(
            CreateConnectionTooltipRow(musicName, musicConnected));
        DashboardConnectionTooltipListPanel.Children.Add(
            CreateConnectionTooltipRow("Streamer.bot", streamerBotConnected));
    }

    private static System.Windows.FrameworkElement CreateConnectionTooltipRow(
        string name,
        bool connected)
    {
        Brush successBrush = Application.Current.TryFindResource("SuccessBrush") as Brush
            ?? new SolidColorBrush(Color.FromRgb(0x4C, 0xD9, 0x64));
        Brush warningBrush = Application.Current.TryFindResource("WarningBrush") as Brush
            ?? new SolidColorBrush(Color.FromRgb(0xE8, 0xA2, 0x3A));
        Brush dangerBrush = Application.Current.TryFindResource("DangerBrush") as Brush
            ?? System.Windows.Media.Brushes.IndianRed;

        var row = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 6)
        };
        row.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = connected ? successBrush : dangerBrush,
            Margin = new Thickness(0, 5, 8, 0)
        });
        row.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = name,
            FontWeight = FontWeights.SemiBold,
            Foreground = Application.Current.TryFindResource("TextPrimaryBrush") as Brush
                ?? new SolidColorBrush(Color.FromRgb(0xF4, 0xF7, 0xF9)),
            Width = 100
        });
        row.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = connected ? "Verbunden" : "Nicht verbunden",
            Foreground = connected ? successBrush : warningBrush,
            FontSize = 12
        });
        return row;
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

        bool connected = _twitchModule.GetSnapshot().Authenticated;
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

        bool connected = _spotifyModule.GetSnapshot().Authenticated;

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

        bool connected =
            _streamerBotClient.IsConnected;

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
}
