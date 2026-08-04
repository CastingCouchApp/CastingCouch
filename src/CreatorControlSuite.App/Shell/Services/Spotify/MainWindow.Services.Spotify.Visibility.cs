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
    private int? ResolveEffectiveSpotifyVolume(SpotifyPlaybackState playback)
    {
        // Direkt nach einer Änderung in der Suite hat der angeforderte Wert kurz Vorrang,
        // weil Spotify die neue Gerätelautstärke häufig erst mit Verzögerung zurückmeldet.
        // Danach ist wieder der tatsächlich von Spotify gemeldete Wert maßgeblich, damit
        // auch ein Mute in der Spotify-App oder auf einem anderen Gerät erkannt wird.
        if (_lastRequestedSpotifyVolumePercent.HasValue &&
            _lastRequestedSpotifyVolumeAt.HasValue &&
            DateTimeOffset.UtcNow - _lastRequestedSpotifyVolumeAt.Value < TimeSpan.FromSeconds(4))
        {
            return _lastRequestedSpotifyVolumePercent.Value;
        }

        int? reportedVolume = playback.Device?.VolumePercent;
        if (reportedVolume.HasValue)
        {
            _lastRequestedSpotifyVolumePercent = reportedVolume.Value;
            _lastRequestedSpotifyVolumeAt = null;
        }

        return reportedVolume;
    }

    private async Task SetSpotifyVolumeTrackedAsync(int volume, CancellationToken cancellationToken = default)
    {
        volume = Math.Clamp(volume, 0, 100);
        RememberSpotifyVolumeLevel(volume);
        await _spotifyModule.SetVolumeImmediateAsync(volume, cancellationToken);
        await ApplySpotifyOverlayMuteStateAsync(volume <= 0);
    }

    private void RememberSpotifyVolumeLevel(int level)
    {
        _lastRequestedSpotifyVolumePercent = Math.Clamp(level, 0, 100);
        _lastRequestedSpotifyVolumeAt = DateTimeOffset.UtcNow;
    }

    private async Task SynchronizeSpotifyOverlayVisibilityAsync(SpotifyPlaybackState playback)
    {
        // Mehrere Spotify-Polls dürfen die Sichtbarkeit nicht parallel und in
        // unterschiedlicher Reihenfolge anwenden. Das war die Hauptursache für
        // das wiederholte Ein-/Ausblenden der Browserquelle.
        await _spotifyOverlayVisibilityLock.WaitAsync();
        try
        {
            if (!_settings.Spotify.SmartAutomationEnabled)
            {
                _lastSpotifyOverlayMuted = null;
                await ApplySpotifyOverlayMuteStateAsync(false);
                return;
            }

            if (!_settings.Spotify.OverlayHideWhenMuted && !_settings.Spotify.OverlayHideWhenPaused)
            {
                _lastSpotifyOverlayMuted = null;
                await ApplySpotifyOverlayMuteStateAsync(false);
                return;
            }

            bool hideBecausePaused = _settings.Spotify.OverlayHideWhenPaused &&
                                    !playback.IsPlaying &&
                                    DateTimeOffset.UtcNow - _lastSpotifyPlayingAt >= TimeSpan.FromSeconds(3);
            bool hideBecauseVolume = false;
            bool hideBecauseObsMute = false;

            if (_settings.Spotify.OverlayHideWhenMuted && _settings.Spotify.OverlayMuteDetectionSpotifyVolume)
            {
                int? volumePercent = ResolveEffectiveSpotifyVolume(playback);
                hideBecauseVolume = volumePercent.HasValue && volumePercent.Value <= 0;
            }

            if (_settings.Spotify.OverlayHideWhenMuted && _settings.Spotify.OverlayMuteDetectionObsSource)
            {
                if (_obsClient.IsConnected)
                {
                    string? audioSource = _settings.Spotify.OverlayObsAudioSource?.Trim();
                    if (!string.IsNullOrWhiteSpace(audioSource))
                    {
                        try
                        {
                            ObsInputAudioState audioState = await _obsClient.GetInputAudioStateAsync(audioSource);
                            _lastKnownSpotifyObsMute = audioState.Muted;
                        }
                        catch (Exception exception)
                        {
                            // Bei einem kurzen OBS-Abfragefehler den zuletzt sicher
                            // bekannten Zustand behalten. Früher wurde hier implizit
                            // "nicht gemutet" angenommen und das Overlay kurz eingeblendet.
                            _appLogger.Write(AppLogLevel.Debug, "Spotify", $"OBS-Mute-Status für '{audioSource}' konnte nicht gelesen werden: {exception.Message}");
                        }
                    }
                }

                hideBecauseObsMute = _lastKnownSpotifyObsMute == true;
            }

            await ApplySpotifyOverlayMuteStateAsync(hideBecausePaused || hideBecauseVolume || hideBecauseObsMute);
        }
        finally
        {
            _spotifyOverlayVisibilityLock.Release();
        }
    }

    private async Task ApplySpotifyOverlayMuteStateAsync(bool isMuted)
    {
        if (!_settings.Spotify.SmartAutomationEnabled ||
            (!_settings.Spotify.OverlayHideWhenMuted && !_settings.Spotify.OverlayHideWhenPaused))
        {
            isMuted = false;
        }

        if (_lastSpotifyOverlayMuted == isMuted)
        {
            return;
        }

        // Die JSON-Sichtbarkeit wird unabhängig von OBS aktualisiert. Damit
        // funktioniert das Ausblenden auch bei Overlays, die nur das Feld
        // spotify.showInOverlay bzw. spotify.visible auswerten.
        try
        {
            await UpdateActiveOverlayJsonAsync(root =>
            {
                JsonObject spotify = root["spotify"] as JsonObject ?? [];
                spotify["hideWhenMuted"] = _settings.Spotify.OverlayHideWhenMuted;
                spotify["hideWhenPaused"] = _settings.Spotify.OverlayHideWhenPaused;
                spotify["muteDetectionObsSource"] = _settings.Spotify.OverlayMuteDetectionObsSource;
                spotify["muteDetectionSpotifyVolume"] = _settings.Spotify.OverlayMuteDetectionSpotifyVolume;
                spotify["obsAudioSource"] = _settings.Spotify.OverlayObsAudioSource;
                spotify["showInOverlay"] = !isMuted;
                spotify["visible"] = !isMuted;
                root["spotify"] = spotify;
            });
        }
        catch (Exception exception)
        {
            _appLogger.Write(AppLogLevel.Warning, "Spotify", "Spotify-Mute-Status konnte nicht in die Overlay-JSON geschrieben werden.", exception);
        }

        // Ist eine OBS-Szene und -Quelle hinterlegt, wird zusätzlich die Quelle
        // geschaltet. Fehlt OBS, bleibt wenigstens die JSON-Steuerung wirksam.
        if (_obsClient.IsConnected)
        {
            string? sceneName = _settings.Spotify.OverlayObsScene?.Trim();
            string? sourceName = _settings.Spotify.OverlayObsSource?.Trim();
            if (!string.IsNullOrWhiteSpace(sceneName) && !string.IsNullOrWhiteSpace(sourceName))
            {
                try
                {
                    await _obsClient.SetSceneItemEnabledAsync(sceneName, sourceName, !isMuted);
                }
                catch (Exception exception)
                {
                    _appLogger.Write(AppLogLevel.Warning, "Spotify", $"Spotify-Overlay-Sichtbarkeit konnte nicht geändert werden: {exception.Message}", exception);
                }
            }
        }

        _lastSpotifyOverlayMuted = isMuted;
        _appLogger.Write(AppLogLevel.Information, "Spotify",
            isMuted ? "Spotify-Overlay wegen Mute/Pause ausgeblendet." : "Spotify-Overlay wieder eingeblendet.");
    }
}
