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
    private void TitleBarMinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void TitleBarMaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void TitleBarCloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowMainWindowClose)
        {
            return;
        }

        if (_streamEndFlowActive)
        {
            e.Cancel = true;
            _activeStreamEndDialog?.Activate();
            MessageBox.Show(
                this,
                "Das Streamende läuft noch. Bitte warte, bis der Stream beendet ist, oder brich den Ablauf ab.",
                "Creator Control Suite",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!_lastObsStreamActive)
        {
            return;
        }

        e.Cancel = true;
        MessageBoxResult result = MessageBox.Show(
            this,
            "Der Stream läuft noch. Die Anwendung kann erst geschlossen werden, wenn der Stream beendet ist.\n\nStreamende-Dialog öffnen?",
            "Creator Control Suite",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _closeAfterStreamEnd = true;
        _ = StopObsStreamAsync();
    }

    private void TryCloseApplicationAfterStreamEnd()
    {
        if (!_closeAfterStreamEnd)
        {
            return;
        }

        _closeAfterStreamEnd = false;
        _allowMainWindowClose = true;
        Dispatcher.BeginInvoke(new Action(Close));
    }

    private void UpdateTitleBarMaximizeButton()
    {
        if (TitleBarMaximizeButton is null || TitleBarMaximizeIcon is null || TitleBarRestoreIcon is null)
        {
            return;
        }

        bool maximized = WindowState == WindowState.Maximized;
        TitleBarMaximizeIcon.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        TitleBarRestoreIcon.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
        TitleBarMaximizeButton.ToolTip = maximized ? "Wiederherstellen" : "Maximieren";
    }

    private void OnThemeChanged()
    {
        if (TryFindResource("AppFontFamily") is FontFamily fontFamily)
        {
            FontFamily = fontFamily;
        }

        // Code-behind local values would otherwise pin Classic colors after a theme swap.
        DashboardConnectionSummaryChip?.ClearValue(Border.BackgroundProperty);
        DashboardConnectionSummaryChip?.ClearValue(Border.BorderBrushProperty);
        DashboardServiceStatusSection?.ClearValue(Border.BackgroundProperty);

        Button? active = new Button?[]
        {
            DashboardButton, ServicesButton, WorkflowButton, StatisticsButton,
            OverlaysButton, AlertsButton, SettingsButton, DiagnosticsButton,
            ServicesSpotifyButton, ServicesTwitchButton, ServicesObsButton,
            ServicesStreamerBotButton, ServicesStreamDeckButton
        }.FirstOrDefault(b => b is not null && b.FontWeight == FontWeights.SemiBold);
        if (active is not null)
        {
            SetActiveNavigationButton(active);
        }
    }
}
