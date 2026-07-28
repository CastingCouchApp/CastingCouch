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
    private Task<Stream?> OpenOverlayExtensionPackAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Overlay Extension Pack importieren",
            Filter = "Extension Pack (*.zip)|*.zip",
            CheckFileExists = true,
            Multiselect = false
        };

        return Task.FromResult<Stream?>(
            dialog.ShowDialog(this) == true
                ? File.OpenRead(dialog.FileName)
                : null);
    }

    private Task<bool> ConfirmOverlayExtensionPackUninstallAsync(
        OverlayExtensionPackSummary pack) =>
        Task.FromResult(
            MessageBox.Show(
                this,
                $"Extension Pack „{pack.Name}“ ({pack.Id}) wirklich deinstallieren?",
                "Extension Pack deinstallieren",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes);

    private Task<string?> PromptOverlayCanvasNameAsync(
        OverlayCanvasNameRequest request)
    {
        var dialog = new TextPromptWindow(
            request.Title,
            request.Prompt,
            request.InitialValue)
        {
            Owner = this
        };

        return Task.FromResult(
            dialog.ShowDialog() == true
                ? dialog.Value
                : null);
    }

    private Task<bool> ConfirmOverlayCanvasDeleteAsync(
        OverlayCanvasSettings canvas)
    {
        if (_settings.Overlay.Canvases.Count <= 1)
        {
            MessageBox.Show(
                this,
                "Das letzte Canvas kann nicht gelöscht werden.",
                "Canvas löschen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return Task.FromResult(false);
        }

        return Task.FromResult(
            MessageBox.Show(
                this,
                $"Canvas „{canvas.Name}“ wirklich löschen?\n" +
                $"Layout-Datei und OBS-URL /view/{canvas.Id} entfallen.",
                "Canvas löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes);
    }

    private async Task OpenOverlayEditorAsync(
        string url,
        string canvasName)
    {
        try
        {
            if (!_overlayModule.WebServer.IsRunning)
            {
                if (!_settings.Overlay.WebServerEnabled)
                {
                    _overlayCanvasPageViewModel.UpdateStatus(
                        "Overlay-Webserver läuft nicht. Bitte aktivieren und speichern.");
                    MessageBox.Show(
                        this,
                        "Der Overlay-Webserver ist deaktiviert.\n" +
                        "Bitte unter Overlay → Webserver aktivieren und speichern.",
                        "Overlay Editor",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                _overlayCanvasPageViewModel.UpdateStatus(
                    "Overlay-Webserver startet…");
                await _overlayModule.WebServer.RestartAsync();
                if (!_overlayModule.WebServer.IsRunning)
                {
                    throw new InvalidOperationException(
                        "Overlay-Webserver konnte nicht gestartet werden.");
                }

                RefreshOverlayWebServerStatusUi();
            }

            var window = new OverlayEditorWindow(url, canvasName)
            {
                Owner = this
            };
            window.Show();
            _overlayCanvasPageViewModel.UpdateStatus(
                "Editor geöffnet: " + url);
        }
        catch (Exception exception)
        {
            _overlayCanvasPageViewModel.UpdateStatus(
                "Editor konnte nicht geöffnet werden: " +
                exception.Message);
            MessageBox.Show(
                this,
                "Editor konnte nicht geöffnet werden:\n" +
                exception.Message,
                "Overlay Editor",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private Task<string?> BrowseOverlayChatBackgroundImageAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chat-Hintergrundbild wählen",
            Filter = "Bilder|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp|Alle Dateien|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        string currentPath =
            _overlayConnectionSettingsPageViewModel.BackgroundImagePath.Trim();
        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            try
            {
                string? directory = Path.GetDirectoryName(Path.GetFullPath(Environment.ExpandEnvironmentVariables(currentPath)));
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                {
                    dialog.InitialDirectory = directory;
                }
            }
            catch
            {
                // ignore
            }
        }

        if (dialog.ShowDialog(this) == true)
        {
            return Task.FromResult<string?>(dialog.FileName);
        }

        return Task.FromResult<string?>(null);
    }

    private Task<string?> BrowseOverlayAssetLibraryImageAsync()
    {
        var window = new Views.Dialogs.AssetLibraryWindow(_overlayModule.AssetStore)
        {
            Owner = this
        };
        if (window.ShowDialog() == true && window.SelectedAsset is not null)
        {
            return Task.FromResult<string?>(window.SelectedAsset.LocalPath);
        }

        return Task.FromResult<string?>(null);
    }

    private string ResolveConfiguredOverlayRoot()
    {
        string fromSettings = _settings.Overlay.RootPath?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(fromSettings))
        {
            return fromSettings;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CreatorControlSuite",
            "Overlay");
    }

    private void OpenConfiguredTarget(string? target, string displayName, bool showMissingMessage = true)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            if (showMissingMessage)
            {
                MessageBox.Show($"Bitte zuerst unter Einstellungen die URL für {displayName} hinterlegen.", displayName, MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return;
        }
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, $"{displayName} konnte nicht geöffnet werden", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
