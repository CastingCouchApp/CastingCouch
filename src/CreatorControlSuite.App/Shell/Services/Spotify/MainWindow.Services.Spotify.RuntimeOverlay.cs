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
    private void RefreshSpotifyAutomationLogUi()
    {
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyAutomationLogList.ItemsSource = _spotifyAutomationLog.GetRecent().Select(e => e.DisplayText).ToList();
    }

    private void RefreshSpotifyUi()
    {
        SpotifySnapshot snapshot = _spotifyModule.GetSnapshot();
        if (IsSpotifyMusicProvider())
        {
            _spotifyListeningStatistics.Observe(snapshot.Playback);
        }

        RefreshSpotifyStatisticsUi();
        RefreshSpotifyAutomationUi(snapshot);
        if (IsSpotifyMusicProvider())
        {
            _ = RunSpotifyHealthMonitorAsync(snapshot);
        }

        if (IsSpotifyMusicProvider())
        {
            SpotifyDashboardStatus.Text = snapshot.Authenticated
                ? "VERBUNDEN"
                : "NICHT VERBUNDEN";

            SpotifyDashboardLamp.Fill = snapshot.Authenticated
                ? System.Windows.Media.Brushes.LimeGreen
                : System.Windows.Media.Brushes.IndianRed;
        }

        SettingsPageViewHost.SpotifyConnectionStatusText.Text = snapshot.Authenticated
            ? "Verbunden als " + snapshot.UserDisplayName
            : "Nicht verbunden";

        SettingsPageViewHost.SpotifyConnectionStatusText.Foreground = snapshot.Authenticated
            ? System.Windows.Media.Brushes.LightGreen
            : System.Windows.Media.Brushes.Gray;

        SettingsPageViewHost.SpotifyDeviceBox.ItemsSource = snapshot.Devices;
        SettingsPageViewHost.SpotifyPlaylistBox.ItemsSource = snapshot.Playlists;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDeviceBox.ItemsSource = snapshot.Devices;
        ApplySpotifyPlaylistFilter();
        _spotifyAutomationPageViewModel.UpdatePlaylists(snapshot.Playlists);
        DashboardPageViewHost.DashboardSpotifyPlaylistBox.ItemsSource = snapshot.Playlists;
        RefreshSpotifyQuickPlaylists();
        UpdateSpotifyFavoriteButton();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyNowPlayingText.Text = snapshot.Playback.Track is null
            ? "Kein Titel"
            : snapshot.Playback.Track.Artist + " – " + snapshot.Playback.Track.Name;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyAlbumText.Text = snapshot.Playback.Track is null ||
                                        string.IsNullOrWhiteSpace(snapshot.Playback.Track.Album)
            ? "Album: -"
            : "Album: " + snapshot.Playback.Track.Album;
        DashboardPageViewHost.DashboardSpotifyTrackText.Text = snapshot.Playback.Track is null
            ? "Kein Spotify-Titel"
            : snapshot.Playback.Track.Artist + " – " + snapshot.Playback.Track.Name;
        DashboardPageViewHost.DashboardSpotifyAlbumText.Text = snapshot.Playback.Track is null ||
                                         string.IsNullOrWhiteSpace(snapshot.Playback.Track.Album)
            ? "Album: -"
            : "Album: " + snapshot.Playback.Track.Album;
        DashboardPageViewHost.DashboardSpotifyPlaybackStateText.Text = snapshot.Playback.Track is null
            ? "BEREIT"
            : snapshot.Playback.IsPlaying ? "WIEDERGABE LÄUFT" : "PAUSIERT";
        DashboardPageViewHost.DashboardSpotifyDeviceText.Text = snapshot.Playback.Device is null
            ? "Gerät: keines aktiv"
            : $"Gerät: {snapshot.Playback.Device.Name}" +
              (snapshot.Playback.Device.IsRestricted ? " · nicht fernsteuerbar" : string.Empty);
        DashboardPageViewHost.DashboardSpotifyPlayButton.Content = snapshot.Playback.IsPlaying ? "▶ LÄUFT" : "▶ PLAY";
        DashboardPageViewHost.DashboardSpotifyPauseButton.Content = snapshot.Playback.IsPlaying ? "Ⅱ PAUSE" : "Ⅱ PAUSIERT";
        DashboardPageViewHost.DashboardSpotifyPlayButton.IsEnabled = snapshot.Authenticated;
        DashboardPageViewHost.DashboardSpotifyPauseButton.IsEnabled = snapshot.Authenticated && snapshot.Playback.Track is not null;
        DashboardPageViewHost.DashboardSpotifyPreviousButton.IsEnabled = snapshot.Authenticated && snapshot.Playback.Track is not null;
        DashboardPageViewHost.DashboardSpotifyNextButton.IsEnabled = snapshot.Authenticated && snapshot.Playback.Track is not null;

        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyQueueCurrentText.Text = snapshot.Queue.CurrentlyPlaying is null
            ? "Aktuell: -"
            : $"Aktuell: {snapshot.Queue.CurrentlyPlaying.Artist} – {snapshot.Queue.CurrentlyPlaying.Name}";
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyQueueList.ItemsSource = snapshot.Queue.Upcoming
            .Select((track, index) => new SpotifyQueueItem(track, index + 1))
            .ToList();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlayQueueItemButton.IsEnabled = snapshot.Queue.Upcoming.Count > 0;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifySkipCurrentButton.IsEnabled = snapshot.Playback.Track is not null;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyQueueEmptyText.Visibility = snapshot.Queue.Upcoming.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyHistoryList.ItemsSource = snapshot.RecentlyPlayed
            .Select(item => new SpotifyHistoryItem(item))
            .ToList();
        if (snapshot.RecentlyPlayed.Count == 0)
        {
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyHistoryStatusText.Text =
                "Noch keine zuletzt gespielten Titel verfügbar.";
        }

        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifySavedTracksList.ItemsSource = snapshot.SavedTracks
            .Select(track => new SpotifySavedTrackItem(track))
            .ToList();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaySavedTrackButton.IsEnabled = snapshot.SavedTracks.Count > 0;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyRemoveSavedTrackButton.IsEnabled = snapshot.SavedTracks.Count > 0;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyToggleCurrentSavedButton.IsEnabled = snapshot.Playback.Track is not null;
        if (snapshot.SavedTracks.Count == 0)
        {
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifySavedTracksStatusText.Text =
                "Noch keine gespeicherten Titel verfügbar.";
        }

        if (!string.IsNullOrWhiteSpace(
                _settings.Spotify.PreferredDeviceId))
        {
            SettingsPageViewHost.SpotifyDeviceBox.SelectedItem =
                snapshot.Devices.FirstOrDefault(
                    device =>
                        device.Id ==
                        _settings.Spotify.PreferredDeviceId);
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDeviceBox.SelectedItem = SettingsPageViewHost.SpotifyDeviceBox.SelectedItem;
        }
        else if (snapshot.Playback.Device is not null)
        {
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDeviceBox.SelectedItem = snapshot.Devices.FirstOrDefault(
                device => device.Id == snapshot.Playback.Device.Id);
        }

        UpdateSpotifyDeviceSelectionUi();

        IReadOnlyDictionary<string, string> spotifyErrors = _spotifyModule.LastRefreshErrors;
        if (spotifyErrors.TryGetValue("Wiedergabegeräte", out string? deviceError))
        {
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDeviceStatusText.Text = "Geräte konnten nicht geladen werden: " + deviceError;
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDeviceStatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
        }
        else
        {
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDeviceStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(174, 184, 191));
            if (snapshot.Devices.Count == 0)
            {
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDeviceStatusText.Text =
                    "Kein aktives Spotify-Gerät gefunden. Spotify auf PC oder Handy öffnen und dort kurz einen Titel starten.";
            }
        }

        if (spotifyErrors.TryGetValue("Playlists", out string? playlistError))
        {
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistStatusText.Text = "Playlists konnten nicht geladen werden: " + playlistError;
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistStatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
        }
        else
        {
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(127, 137, 145));
        }

        if (!string.IsNullOrWhiteSpace(
                _settings.Spotify.StartPlaylistUri))
        {
            SettingsPageViewHost.SpotifyPlaylistBox.SelectedItem =
                snapshot.Playlists.FirstOrDefault(
                    playlist =>
                        playlist.Uri ==
                        _settings.Spotify.StartPlaylistUri);
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistBox.SelectedItem = SettingsPageViewHost.SpotifyPlaylistBox.SelectedItem;
            _spotifyAutomationPageViewModel.SelectedPlaylist =
                SettingsPageViewHost.SpotifyPlaylistBox.SelectedItem as SpotifyPlaylist;
            DashboardPageViewHost.DashboardSpotifyPlaylistBox.SelectedItem = SettingsPageViewHost.SpotifyPlaylistBox.SelectedItem;
        }

        SpotifyPlaybackState playback = snapshot.Playback;

        int progressMs = Math.Max(0, playback.ProgressMs);
        int durationMs = Math.Max(0, playback.Track?.DurationMs ?? 0);
        _updatingSpotifyUi = true;
        try
        {
            DashboardPageViewHost.DashboardSpotifyProgressBar.Maximum = Math.Max(1, durationMs);
            DashboardPageViewHost.DashboardSpotifyProgressBar.Value = Math.Min(progressMs, Math.Max(1, durationMs));
            DashboardPageViewHost.DashboardSpotifyProgressBar.IsEnabled = playback.Track is not null && durationMs > 0;
        }
        finally
        {
            _updatingSpotifyUi = false;
        }
        DashboardPageViewHost.DashboardSpotifyProgressText.Text = TimeSpan.FromMilliseconds(progressMs).ToString(@"mm\:ss");
        DashboardPageViewHost.DashboardSpotifyDurationText.Text = TimeSpan.FromMilliseconds(durationMs).ToString(@"mm\:ss");
        DashboardPageViewHost.DashboardSpotifyShuffleButton.Content = playback.ShuffleEnabled ? "⤨ EIN" : "⤨";
        DashboardPageViewHost.DashboardSpotifyShuffleButton.ToolTip = playback.ShuffleEnabled
            ? "Zufallswiedergabe ist aktiv – klicken zum Ausschalten"
            : "Zufallswiedergabe ist aus – klicken zum Einschalten";
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyShuffleButton.Content = playback.ShuffleEnabled ? "Shuffle: Ein" : "Shuffle: Aus";
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyShuffleButton.ToolTip = playback.ShuffleEnabled
            ? "Zufallswiedergabe ist aktiv – klicken zum Ausschalten"
            : "Zufallswiedergabe ist aus – klicken zum Einschalten";
        _updatingSpotifyUi = true;
        try
        {
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyShuffleBox.IsChecked =
                playback.ShuffleEnabled;
        }
        finally
        {
            _updatingSpotifyUi = false;
        }
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlayButton.Content =
            playback.IsPlaying ? "⏸" : "▶";
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlayButton.ToolTip =
            playback.IsPlaying ? "Wiedergabe pausieren" : "Wiedergabe fortsetzen";

        DashboardPageViewHost.DashboardSpotifyRepeatButton.Content = playback.RepeatMode?.ToLowerInvariant() switch
        {
            "context" => "↻ LISTE",
            "track" => "↻ 1",
            _ => "↻"
        };
        DashboardPageViewHost.DashboardSpotifyRepeatButton.ToolTip = playback.RepeatMode?.ToLowerInvariant() switch
        {
            "context" => "Wiederholung der aktuellen Playlist – klicken für Titelwiederholung",
            "track" => "Wiederholung des aktuellen Titels – klicken zum Ausschalten",
            _ => "Wiederholung ist aus – klicken, um die Playlist zu wiederholen"
        };
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyRepeatButton.Content = playback.RepeatMode?.ToLowerInvariant() switch
        {
            "context" => "Wiederholung: Playlist",
            "track" => "Wiederholung: Titel",
            _ => "Wiederholung: Aus"
        };
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyRepeatButton.ToolTip = DashboardPageViewHost.DashboardSpotifyRepeatButton.ToolTip;

        SettingsPageViewHost.SpotifyTrackText.Text = playback.Track is null
            ? "Kein Titel"
            : playback.Track.Artist +
              " – " +
              playback.Track.Name;
        string? intelligenceTrackId = playback.Track is null ? null : $"{playback.Track.Artist}|{playback.Track.Name}|{playback.Track.Album}";
        if (!string.IsNullOrWhiteSpace(intelligenceTrackId) && !string.Equals(_lastCreatorIntelligenceTrackId, intelligenceTrackId, StringComparison.Ordinal))
        {
            _lastCreatorIntelligenceTrackId = intelligenceTrackId;
            _ = _creatorIntelligence.RecordAsync("spotify.track.changed", new
            {
                artist = playback.Track!.Artist,
                title = playback.Track.Name,
                album = playback.Track.Album,
                isPlaying = playback.IsPlaying,
                viewers = _currentLiveViewerCount,
                scene = _servicesObsCurrentScene
            });
        }

        SettingsPageViewHost.SpotifyAlbumText.Text = playback.Track is null ||
                               string.IsNullOrWhiteSpace(
                                   playback.Track.Album)
            ? "Album: -"
            : "Album: " +
              playback.Track.Album;

        SettingsPageViewHost.SpotifyPlaybackDetailText.Text = playback.Track is null
            ? "Verbunden · Pause"
            : (playback.IsPlaying
                ? "Verbunden · Spielt"
                : "Verbunden · Pause") +
              " · Gerät: " +
              (playback.Device?.Name ?? "unbekannt");

        _updatingSpotifyUi = true;

        try
        {
            int spotifyLevel = SpotifyPlaybackLevelResolver.Resolve(
                snapshot,
                _settings.Spotify.StartVolumePercent,
                _lastRequestedSpotifyVolumePercent,
                _lastRequestedSpotifyVolumeAt);
            SettingsPageViewHost.SpotifyVolumeSlider.Value =
                spotifyLevel;

            SettingsPageViewHost.SpotifyVolumeValueText.Text =
                $"{(int)Math.Round(SettingsPageViewHost.SpotifyVolumeSlider.Value)} %";
            DashboardPageViewHost.DashboardSpotifyVolumeSlider.Value = spotifyLevel;
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyVolumeSlider.Value = spotifyLevel;
            DashboardPageViewHost.DashboardSpotifyVolumeText.Text = $"{(int)Math.Round(DashboardPageViewHost.DashboardSpotifyVolumeSlider.Value)} %";
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyVolumeText.Text = $"Level {spotifyLevel}";
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyProgressBar.Maximum = Math.Max(1, durationMs);
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyProgressBar.Value = Math.Clamp(progressMs, 0, Math.Max(1, durationMs));
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyProgressBar.IsEnabled = playback.Track is not null && durationMs > 0;
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyProgressText.Text = TimeSpan.FromMilliseconds(progressMs).ToString(@"mm\:ss");
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDurationText.Text = TimeSpan.FromMilliseconds(durationMs).ToString(@"mm\:ss");
        }
        finally
        {
            _updatingSpotifyUi = false;
        }

        _ = LoadSpotifyAlbumCoverAsync(playback.Track?.AlbumImageUrl);

        if (playback.Track is not null)
        {
            _lastStableSpotifyPlayback = playback;
        }

        if (playback.IsPlaying)
        {
            _lastSpotifyPlayingAt = DateTimeOffset.UtcNow;
        }

        SpotifyPlaybackState overlayPlayback = StabilizeSpotifyOverlayPlayback(playback);
        if (IsSpotifyMusicProvider())
        {
            _ = WriteSpotifyOverlayRuntimeDataAsync(snapshot, overlayPlayback);
            _ = SynchronizeSpotifyOverlayVisibilityAsync(overlayPlayback);
        }
        RefreshDashboardServiceActionButtons();
    }

    private SpotifyPlaybackState StabilizeSpotifyOverlayPlayback(SpotifyPlaybackState playback)
    {
        // Ein leerer Spotify-Snapshot ist während einer bestehenden Verbindung kein
        // zuverlässiger Trennstatus. Die Web API liefert bei Token-Erneuerungen,
        // Gerätewechseln und einzelnen Pollfehlern kurzfristig Track=null. Würden wir
        // diesen Zustand in die JSON schreiben, löscht die Suite Titel, Cover und
        // Fortschritt und spotify.html blendet sich sofort aus.
        //
        // Solange die Verbindung durch einen früheren erfolgreichen Snapshot gehalten
        // wird, bleibt deshalb der letzte gültige Titel aktiv. Nur DisconnectSpotifyAsync
        // setzt _spotifyOverlayConnectionLatched=false und darf die JSON leeren.
        if (playback.Track is null &&
            _spotifyOverlayConnectionLatched &&
            _lastStableSpotifyPlayback?.Track is not null)
        {
            return _lastStableSpotifyPlayback with
            {
                ProgressMs = playback.ProgressMs > 0
                    ? playback.ProgressMs
                    : _lastStableSpotifyPlayback.ProgressMs
            };
        }

        return playback;
    }

    private async Task WriteSpotifyOverlayRuntimeDataAsync(SpotifySnapshot snapshot, SpotifyPlaybackState playback)
    {
        // Auch direkte Aufrufer (Lautstärke, Anzeigeoptionen usw.) dürfen einen
        // kurzfristig leeren Snapshot nicht als vollständigen Reset in die JSON schreiben.
        playback = StabilizeSpotifyOverlayPlayback(playback);

        // Der Schalter steuert ausschließlich das Schreiben der Spotify-Laufzeitdaten.
        // Sein Zustand wird in den Einstellungen gespeichert und beim Start geladen.
        if (!_settings.Spotify.OverlayEnabled)
        {
            return;
        }

        await OverlayDataWriteCoordinator.Lock.WaitAsync();

        try
        {
            // Die vorhandenen DenverJohn-v18-Overlays laden ihre Spotify-Daten
            // direkt aus <OverlayRoot>\Overlay\data\overlay-data.json. Deshalb
            // schreiben wir hier bewusst direkt in diese Datei und umgehen den
            // allgemeinen OverlayDataService, dessen gespeicherter Pfad bei älteren
            // Installationen noch auf die zweite JSON im Root zeigen kann.
            string overlayRoot = ResolveConfiguredOverlayRoot();
            if (string.IsNullOrWhiteSpace(overlayRoot))
            {
                overlayRoot = _settings.Overlay.RootPath?.Trim() ?? "";
            }

            if (string.IsNullOrWhiteSpace(overlayRoot))
            {
                throw new InvalidOperationException("Es ist kein Overlay-Ordner ausgewählt.");
            }

            overlayRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(overlayRoot));

            await DisableLegacyOverlayWriterAsync(overlayRoot);

            // Die zentrale Overlay-Datenquelle bestimmt den Laufzeitpfad.
            // Hotfix 6 leitete den Zielpfad erneut nur aus dem Overlay-Root ab und
            // konnte dadurch eine andere overlay-data.json beschreiben als die von
            // der OBS-HTML geladene Datei.
            string targetPath = ResolveActiveOverlayDataPath();

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            JsonObject rootObject;
            if (File.Exists(targetPath))
            {
                try
                {
                    string existingJson = await File.ReadAllTextAsync(targetPath);
                    rootObject = JsonNode.Parse(existingJson) as JsonObject ?? [];
                }
                catch (JsonException)
                {
                    rootObject = [];
                }
            }
            else
            {
                rootObject = [];
            }

            JsonObject spotify = rootObject["spotify"] as JsonObject ?? [];
            // Der Overlay-Verbindungsstatus wird nach einer erfolgreichen Verbindung
            // bis zu einem ausdrücklichen Trennen gehalten. Kurzlebige Poll-/Token-
            // Snapshots dürfen die Anzeige nicht sekündlich auf "nicht verbunden" setzen.
            if (snapshot.Authenticated || playback.Track is not null)
            {
                _spotifyOverlayConnectionLatched = true;
            }

            // Nur ein ausdrücklich vom Benutzer gestarteter Disconnect darf den
            // öffentlichen Overlay-Verbindungsstatus auf false setzen. Polling,
            // leere API-Antworten und Token-Erneuerungen dürfen das niemals.
            bool overlayConnected = !_spotifyExplicitDisconnectInProgress && (_spotifyOverlayConnectionLatched || snapshot.Authenticated || playback.Track is not null);
            if (overlayConnected)
            {
                _spotifyOverlayConnectionLatched = true;
            }

            bool overlayVisible = overlayConnected && _lastSpotifyOverlayMuted != true;
            ApplyMusicOverlayFields(
                spotify,
                MusicProviderIds.Spotify,
                overlayConnected,
                playback.IsPlaying,
                playback.Track?.Name ?? "",
                playback.Track?.Artist ?? "",
                playback.Track?.Album ?? "",
                playback.Track?.AlbumImageUrl ?? "",
                playback.ProgressMs,
                playback.Track?.DurationMs ?? 0,
                !overlayConnected ? "Nicht verbunden" : playback.IsPlaying ? "Spielt" : "Pause",
                overlayVisible);

            rootObject["spotify"] = spotify;
            rootObject["music"] = spotify.DeepClone();
            rootObject["updatedAt"] = DateTimeOffset.UtcNow;

            string json = rootObject.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            });

            // Direkt in dieselbe Datei schreiben. Das frühere Ersetzen über
            // eine temporäre Datei erzeugte bei einigen OBS-Browserquellen ein
            // Dateiwechsel-Ereignis, das wie kurzes Aus-/Einblenden wirkte.
            await File.WriteAllTextAsync(targetPath, json);

            string trackKey = $"{playback.Track?.Artist}|{playback.Track?.Name}|{playback.Track?.AlbumImageUrl}";
            if (!string.Equals(_lastOverlayPublishedSpotifyTrack, trackKey, StringComparison.Ordinal))
            {
                _lastOverlayPublishedSpotifyTrack = trackKey;
                await PublishOverlayRealtimeEventAsync(OverlayEventBridge.AppSpotifyTrack(
                    playback.Track?.Name ?? "",
                    playback.Track?.Artist ?? "",
                    playback.Track?.AlbumImageUrl ?? ""));
            }

            _appLogger.Write(
                AppLogLevel.Debug,
                "Spotify",
                $"Overlay-JSON aktualisiert: Pfad='{targetPath}', connected={overlayConnected}, showInOverlay={spotify["showInOverlay"]}, Titel='{playback.Track?.Name ?? ""}'.");
        }
        catch (Exception exception)
        {
            _appLogger.Write(AppLogLevel.Error, "Spotify", $"Spotify-JSON konnte nicht aktualisiert werden: {exception.Message}", exception);
        }
        finally
        {
            OverlayDataWriteCoordinator.Lock.Release();
        }
    }

    private async Task DisableLegacyOverlayWriterAsync(string overlayRoot)
    {
        if (_legacyOverlayWriterChecked)
        {
            return;
        }

        _legacyOverlayWriterChecked = true;

        try
        {
            string legacyRoot = Path.Combine(overlayRoot, "StreamingSuite");
            string legacyScript = Path.Combine(legacyRoot, "Start.ps1");
            if (!File.Exists(legacyScript))
            {
                return;
            }

            // Die alte DenverJohn-StreamingSuite schreibt periodisch in dieselbe
            // Overlay/data/overlay-data.json wie die CastingCouch. Ein
            // paralleler Betrieb erzeugt wechselnde connected-/Live-Zustände.
            // Beende ausschließlich Prozesse, deren Befehlszeile exakt auf dieses
            // Legacy-Skript verweist.
            string escapedScript = legacyScript.Replace("'", "''");
            string stopCommand =
                "$target='" + escapedScript + "'; " +
                "Get-CimInstance Win32_Process | Where-Object { " +
                "$_.CommandLine -and $_.CommandLine.IndexOf($target,[System.StringComparison]::OrdinalIgnoreCase) -ge 0 " +
                "} | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }";

            using (var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + stopCommand.Replace("\"", "\\\"") + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }))
            {
                if (process is not null)
                {
                    await process.WaitForExitAsync();
                }
            }

            // Verhindere einen versehentlichen Neustart der alten Suite. Die Dateien
            // bleiben als Sicherung erhalten und können bei Bedarf manuell
            // zurückbenannt werden.
            foreach (string? fileName in new[] { "Start.bat", "Start.vbs", "Start.ps1" })
            {
                string source = Path.Combine(legacyRoot, fileName);
                if (!File.Exists(source))
                {
                    continue;
                }

                string disabled = source + ".disabled-by-creator-control-suite";
                if (File.Exists(disabled))
                {
                    File.Delete(disabled);
                }

                File.Move(source, disabled);
            }

            string markerPath = Path.Combine(legacyRoot, "LEGACY-WRITER-DISABLED.txt");
            await File.WriteAllTextAsync(markerPath,
                "Die alte DenverJohn StreamingSuite wurde deaktiviert, weil sie parallel zur CastingCouch in Overlay\\data\\overlay-data.json geschrieben hat.\r\n" +
                "Dadurch wechselten Spotify- und Live-Status zwischen unterschiedlichen Zuständen.\r\n" +
                "Deaktiviert am: " + DateTimeOffset.Now.ToString("O"));

            _appLogger.Write(AppLogLevel.Warning, "Overlay",
                "Alter DenverJohn-Overlay-Schreiber wurde beendet und deaktiviert: " + legacyScript);
        }
        catch (Exception exception)
        {
            _appLogger.Write(AppLogLevel.Warning, "Overlay",
                "Der alte DenverJohn-Overlay-Schreiber konnte nicht automatisch deaktiviert werden: " + exception.Message,
                exception);
        }
    }
}
