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
    private static Task<bool> ConfirmUpdateRestoreAsync() =>
        Task.FromResult(
            MessageBox.Show(
                "Backup wirklich wiederherstellen?\n\n" +
                "Die aktuellen Einstellungen und Profildaten werden überschrieben.",
                "Backup wiederherstellen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes);

    private async Task RestartOverlayWebServerFromSettingsAsync()
    {
        try
        {
            if (_settings.Overlay.WebServerEnabled)
            {
                await _overlayModule.WebServer.RestartAsync();
            }
            else
            {
                await _overlayModule.WebServer.StopAsync();
            }
        }
        catch (Exception exception)
        {
            _appLogger.Write(
                AppLogLevel.Warning,
                "Overlay",
                "Overlay-Webserver konnte nicht neu gestartet werden: " + exception.Message,
                exception);
            _overlayConnectionSettingsPageViewModel.UpdateServerStatus(
                "Webserver-Fehler: " + exception.Message);
            return;
        }

        RefreshOverlayWebServerStatusUi();
    }

    private void RefreshOverlayWebServerStatusUi()
    {
        _overlayConnectionSettingsPageViewModel.Load(_settings.Overlay);
        if (!_settings.Overlay.WebServerEnabled)
        {
            _overlayConnectionSettingsPageViewModel.UpdateServerStatus(
                "Webserver deaktiviert – OBS nutzt lokale Dateien.");
            return;
        }

        if (_overlayModule.WebServer.IsRunning)
        {
            string baseUrl = _overlayModule.WebServer.BaseUrl ?? _settings.Overlay.GetBaseUrl();
            _overlayConnectionSettingsPageViewModel.UpdateServerStatus(
                $"Läuft auf {baseUrl} · Chat: {baseUrl}/chat · WS: {baseUrl.Replace("http://", "ws://", StringComparison.Ordinal)}/ws");
            return;
        }

        _overlayConnectionSettingsPageViewModel.UpdateServerStatus(
            "Webserver aktiviert, aber nicht erreichbar.");
    }

    private async Task OpenOverlayFolderAsync()
    {
        string root = await _overlayModule.Service.GetOverlayRootAsync();
        Directory.CreateDirectory(root);

        Process.Start(
            new ProcessStartInfo
            {
                FileName = root,
                UseShellExecute = true
            });
    }
}
