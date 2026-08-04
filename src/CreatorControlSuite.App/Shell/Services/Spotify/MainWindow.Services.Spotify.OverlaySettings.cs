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
    private async Task SaveSpotifyDisplayOptionsImmediatelyAsync()
    {
        // Während des initialen Ladens werden die CheckBox-Ereignisse ebenfalls ausgelöst.
        // Erst speichern, wenn das Fenster vollständig geladen ist.
        if (!IsLoaded || _loadingSettingsIntoUi)
        {
            return;
        }

        try
        {
            await SaveSpotifyOverlaySettingsAsync();
            await WriteSpotifyOverlayRuntimeDataAsync(_spotifyModule.GetSnapshot(), _spotifyModule.GetSnapshot().Playback);
        }
        catch (Exception exception)
        {
            _appLogger.Write(AppLogLevel.Error, "Spotify", "Anzeigeoptionen konnten nicht gespeichert werden: " + exception.Message, exception);
        }
    }

    private async Task SaveSpotifyOverlaySettingsAsync()
    {
        // Die Spotify-Anzeige ist wieder fest aktiviert. Nur das Ausblenden bei Mute bleibt konfigurierbar.
        _settings.Spotify.OverlayShowTitle = true;
        _settings.Spotify.OverlayShowArtist = true;
        _settings.Spotify.OverlayShowAlbumCover = true;
        _settings.Spotify.OverlayShowProgress = true;
        _settings.Spotify.OverlayHideWhenPaused = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyHidePausedBox.IsChecked == true;
        _settings.Spotify.OverlayHideWhenMuted = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyHideMutedBox.IsChecked == true;
        _settings.Spotify.OverlayMuteDetectionObsSource = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDetectObsMuteBox.IsChecked == true;
        _settings.Spotify.OverlayMuteDetectionSpotifyVolume = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDetectVolumeMuteBox.IsChecked == true;
        _settings.Spotify.OverlayObsAudioSource = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyObsAudioSourceBox.Text?.Trim() ?? "Spotify";
        _settings.Spotify.OverlayEnabled = true;

        await _settingsStore.SaveAsync(_settings);

        await _overlayModule.Service.UpdateAsync(data =>
        {
            data.Spotify.ShowTitle = true;
            data.Spotify.ShowArtist = true;
            data.Spotify.ShowAlbumCover = true;
            data.Spotify.ShowProgress = true;
            data.Spotify.HideWhenPaused = false;
            data.Spotify.HideWhenMuted = _settings.Spotify.OverlayHideWhenMuted;
            data.Spotify.ShowInOverlay = true;
            data.Spotify.Cover = data.Spotify.CoverUrl;
        });

    }

    private async Task RefreshSpotifyOverlayBrowserSourcesAsync()
    {
        if (ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlaySourceBox is null)
        {
            return;
        }

        string? sceneName = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlaySceneBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            sceneName = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlaySceneBox.Text?.Trim();
        }

        string? requestedSource = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlaySourceBox.Text?.Trim();
        if (!_obsClient.IsConnected || string.IsNullOrWhiteSpace(sceneName))
        {
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlaySourceBox.ItemsSource = Array.Empty<string>();
            return;
        }

        try
        {
            IReadOnlyList<ObsSceneItemInfo> sceneItems = await _obsClient.GetSceneItemListAsync(sceneName);
            IReadOnlyList<ObsInputInfo> allInputs = await _obsClient.GetInputListAsync();
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyObsAudioSourceBox.ItemsSource = allInputs
                .Select(input => input.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var browserInputNames = allInputs
                .Where(input => string.Equals(input.Kind, "browser_source", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(input.UnversionedKind, "browser_source", StringComparison.OrdinalIgnoreCase))
                .Select(input => input.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var browserSources = sceneItems
                .Select(item => item.SourceName)
                .Where(name => !string.IsNullOrWhiteSpace(name) && browserInputNames.Contains(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlaySourceBox.ItemsSource = browserSources;

            string preferredSource = !string.IsNullOrWhiteSpace(requestedSource)
                ? requestedSource
                : _settings.Spotify.OverlayObsSource;
            string? matchingSource = browserSources.FirstOrDefault(source =>
                string.Equals(source, preferredSource, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(matchingSource))
            {
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlaySourceBox.SelectedItem = matchingSource;
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlaySourceBox.Text = matchingSource;
            }
            else if (!string.IsNullOrWhiteSpace(preferredSource))
            {
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlaySourceBox.Text = preferredSource;
            }
            else if (browserSources.Count == 1)
            {
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlaySourceBox.SelectedItem = browserSources[0];
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlaySourceBox.Text = browserSources[0];
            }

            _appLogger.Write(
                browserSources.Count == 0 ? AppLogLevel.Warning : AppLogLevel.Debug,
                "Spotify",
                browserSources.Count == 0
                    ? $"In der Szene ‘{sceneName}’ wurde keine Browserquelle gefunden."
                    : $"{browserSources.Count} Browserquelle(n) aus Szene ‘{sceneName}’ geladen.");
        }
        catch (Exception exception)
        {
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOverlaySourceBox.ItemsSource = Array.Empty<string>();
            _appLogger.Write(AppLogLevel.Error, "Spotify", "Browserquellen konnten nicht geladen werden: " + exception.Message, exception);
        }
    }
}
