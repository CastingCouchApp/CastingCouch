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
    private async Task LoadSpotifyAlbumCoverAsync(string? imageUrl)
    {
        if (string.Equals(_lastSpotifyAlbumCoverUrl, imageUrl, StringComparison.Ordinal))
        {
            return;
        }

        _lastSpotifyAlbumCoverUrl = imageUrl;
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            SettingsPageViewHost.SpotifyAlbumCoverImage.Source = null;
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyAlbumCoverImage.Source = null;
            DashboardPageViewHost.DashboardSpotifyAlbumCoverImage.Source = null;
            return;
        }
        try
        {
            byte[] bytes = await AlbumCoverHttpClient.GetByteArrayAsync(imageUrl);
            await Dispatcher.InvokeAsync(() =>
            {
                using var stream = new System.IO.MemoryStream(bytes);
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                SettingsPageViewHost.SpotifyAlbumCoverImage.Source = image;
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyAlbumCoverImage.Source = image;
                DashboardPageViewHost.DashboardSpotifyAlbumCoverImage.Source = image;
            });
        }
        catch
        {
            await Dispatcher.InvokeAsync(() =>
            {
                SettingsPageViewHost.SpotifyAlbumCoverImage.Source = null;
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyAlbumCoverImage.Source = null;
                DashboardPageViewHost.DashboardSpotifyAlbumCoverImage.Source = null;
            });
        }
    }

    private async Task SearchSpotifyTracksAsync()
    {
        string query = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyTrackSearchBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(query))
        {
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyTrackSearchResultsList.ItemsSource = null;
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyTrackSearchStatusText.Text = "Bitte einen Suchbegriff eingeben.";
            return;
        }

        await ExecuteUiActionAsync(
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyTrackSearchButton,
            "Spotify-Titel suchen",
            async () =>
            {
                IReadOnlyList<SpotifyTrack> tracks = await _spotifyModule.SearchTracksAsync(query);
                var items = tracks.Select(track => new SpotifyTrackSearchItem(track)).ToList();
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyTrackSearchResultsList.ItemsSource = items;
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyTrackSearchStatusText.Text = items.Count == 0
                    ? "Keine Titel gefunden."
                    : $"{items.Count} Titel gefunden.";
            });
    }

    private sealed record SpotifyTrackSearchItem(SpotifyTrack Track)
    {
        public string DisplayText => $"{Track.Artist} – {Track.Name} ({Track.Album})";
    }

    private sealed record SpotifyPlaylistTrackItem(SpotifyTrack Track)
    {
        public string DisplayText => $"{Track.Artist} – {Track.Name} ({Track.Album})";
    }

    private sealed record SpotifyQueueItem(SpotifyTrack Track, int Position)
    {
        public string DisplayText => $"{Position}. {Track.Artist} – {Track.Name} ({Track.Album})";
    }

    private sealed record SpotifySavedTrackItem(SpotifyTrack Track)
    {
        public string DisplayText => $"{Track.Artist} – {Track.Name} · {Track.Album}";
    }

    private sealed record SpotifyHistoryItem(SpotifyRecentlyPlayedItem Item)
    {
        public string DisplayText =>
            $"{Item.PlayedAt.ToLocalTime():dd.MM. HH:mm} · {Item.Track.Artist} – {Item.Track.Name}";
    }

    private async Task StartSpotifyPlaylistAndRememberAsync(SpotifyPlaylist playlist)
    {
        await ExecuteSpotifyAsync(() => _spotifyModule.StartPlaylistAsync(playlist.Uri));

        _settings.Spotify.RecentPlaylistUris.RemoveAll(uri =>
            string.Equals(uri, playlist.Uri, StringComparison.OrdinalIgnoreCase));
        _settings.Spotify.RecentPlaylistUris.Insert(0, playlist.Uri);
        if (_settings.Spotify.RecentPlaylistUris.Count > 5)
        {
            _settings.Spotify.RecentPlaylistUris.RemoveRange(
                5,
                _settings.Spotify.RecentPlaylistUris.Count - 5);
        }

        await _settingsStore.SaveAsync(_settings);
        RefreshSpotifyQuickPlaylists();
    }

    private async Task StartSpotifyQuickPlaylistAsync(SpotifyPlaylist? playlist)
    {
        if (playlist is null)
        {
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistStatusText.Text = "Bitte zuerst eine Favoriten- oder zuletzt verwendete Playlist auswählen.";
            return;
        }

        await StartSpotifyPlaylistAndRememberAsync(playlist);
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistStatusText.Text = $"Gestartet: {playlist.Name}";
    }

    private async Task ToggleSelectedSpotifyPlaylistFavoriteAsync()
    {
        if (ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistBox.SelectedItem is not SpotifyPlaylist playlist)
        {
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistStatusText.Text = "Bitte zuerst eine Playlist auswählen.";
            return;
        }

        string? existing = _settings.Spotify.FavoritePlaylistUris.FirstOrDefault(uri =>
            string.Equals(uri, playlist.Uri, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            _settings.Spotify.FavoritePlaylistUris.Add(playlist.Uri);
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistStatusText.Text = $"Favorit hinzugefügt: {playlist.Name}";
        }
        else
        {
            _settings.Spotify.FavoritePlaylistUris.Remove(existing);
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistStatusText.Text = $"Favorit entfernt: {playlist.Name}";
        }

        await _settingsStore.SaveAsync(_settings);
        UpdateSpotifyFavoriteButton();
        RefreshSpotifyQuickPlaylists();
    }

    private void UpdateSpotifyFavoriteButton()
    {
        if (ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistBox.SelectedItem is not SpotifyPlaylist playlist)
        {
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyToggleFavoritePlaylistButton.Content = "☆ FAVORIT";
            return;
        }

        bool isFavorite = _settings.Spotify.FavoritePlaylistUris.Any(uri =>
            string.Equals(uri, playlist.Uri, StringComparison.OrdinalIgnoreCase));
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyToggleFavoritePlaylistButton.Content = isFavorite
            ? "★ FAVORIT ENTFERNEN"
            : "☆ ALS FAVORIT";
    }

    private void RefreshSpotifyQuickPlaylists()
    {
        IReadOnlyList<SpotifyPlaylist> playlists = _spotifyModule.GetSnapshot().Playlists;
        var byUri = playlists
            .Where(playlist => !string.IsNullOrWhiteSpace(playlist.Uri))
            .GroupBy(playlist => playlist.Uri, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> orderedUris = _settings.Spotify.FavoritePlaylistUris
            .Concat(_settings.Spotify.RecentPlaylistUris)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var quickPlaylists = orderedUris
            .Where(byUri.ContainsKey)
            .Select(uri => byUri[uri])
            .ToList();

        string? selectedUri = (ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyQuickPlaylistBox.SelectedItem as SpotifyPlaylist)?.Uri
                          ?? (DashboardPageViewHost.DashboardSpotifyQuickPlaylistBox.SelectedItem as SpotifyPlaylist)?.Uri;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyQuickPlaylistBox.ItemsSource = quickPlaylists;
        DashboardPageViewHost.DashboardSpotifyQuickPlaylistBox.ItemsSource = quickPlaylists;

        SpotifyPlaylist? selected = !string.IsNullOrWhiteSpace(selectedUri)
            ? quickPlaylists.FirstOrDefault(playlist =>
                string.Equals(playlist.Uri, selectedUri, StringComparison.OrdinalIgnoreCase))
            : quickPlaylists.FirstOrDefault();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyQuickPlaylistBox.SelectedItem = selected;
        DashboardPageViewHost.DashboardSpotifyQuickPlaylistBox.SelectedItem = selected;
    }

    private void ApplySpotifyPlaylistFilter()
    {
        IReadOnlyList<SpotifyPlaylist> playlists = _spotifyModule.GetSnapshot().Playlists;
        string filter = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistFilterBox.Text?.Trim() ?? "";
        IReadOnlyList<SpotifyPlaylist> filtered = string.IsNullOrWhiteSpace(filter)
            ? playlists
            : [.. playlists.Where(playlist =>
                    playlist.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    playlist.OwnerName.Contains(filter, StringComparison.OrdinalIgnoreCase))];

        string? selectedUri = (ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistBox.SelectedItem as SpotifyPlaylist)?.Uri;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistBox.ItemsSource = filtered;
        if (!string.IsNullOrWhiteSpace(selectedUri))
        {
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistBox.SelectedItem = filtered.FirstOrDefault(p => p.Uri == selectedUri);
        }
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistStatusText.Text = $"{filtered.Count} von {playlists.Count} Playlists";
    }

    private async Task LoadSelectedSpotifyPlaylistTracksAsync()
    {
        if (ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistBox.SelectedItem is not SpotifyPlaylist playlist)
        {
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistStatusText.Text = "Bitte zuerst eine Playlist auswählen.";
            return;
        }

        await ExecuteUiActionAsync(
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyLoadPlaylistTracksButton,
            "Spotify-Playlisttitel laden",
            async () =>
            {
                IReadOnlyList<SpotifyTrack> tracks = await _spotifyModule.GetPlaylistTracksAsync(playlist);
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistTracksList.ItemsSource = tracks
                    .Select(track => new SpotifyPlaylistTrackItem(track))
                    .ToList();
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistStatusText.Text = tracks.Count == 0
                    ? "Die Playlist enthält keine verfügbaren Titel."
                    : $"{tracks.Count} Titel geladen.";
            });
    }

    private async Task ExecuteSelectedSpotifyPlaylistTrackAsync(bool playImmediately)
    {
        if (ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistTracksList.SelectedItem is not SpotifyPlaylistTrackItem selected)
        {
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistStatusText.Text = "Bitte zuerst einen Playlist-Titel auswählen.";
            return;
        }

        Button button = playImmediately
            ? ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlayPlaylistTrackButton
            : ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyQueuePlaylistTrackButton;
        await ExecuteUiActionAsync(
            button,
            playImmediately ? "Spotify-Titel abspielen" : "Spotify-Titel vormerken",
            async () =>
            {
                if (playImmediately)
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.PlayTrackAsync(selected.Track));
                    ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistStatusText.Text =
                        $"Wiedergabe gestartet: {selected.Track.Artist} – {selected.Track.Name}";
                }
                else
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.AddToQueueAsync(selected.Track));
                    ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistStatusText.Text =
                        $"Zur Warteschlange hinzugefügt: {selected.Track.Artist} – {selected.Track.Name}";
                }
                RefreshSpotifyUi();
            });
    }

    private void UpdateSpotifyDeviceSelectionUi()
    {
        if (ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDeviceBox.SelectedItem is not SpotifyDevice device)
        {
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDeviceStatusText.Text = "Kein Gerät ausgewählt.";
            return;
        }

        string active = device.IsActive ? "aktiv" : "inaktiv";
        string volume = device.SupportsVolume ? $" · Lautstärke {device.VolumePercent} %" : " · Lautstärke nicht steuerbar";
        string restricted = device.IsRestricted ? " · eingeschränkt" : string.Empty;
        string preferred = string.Equals(
            _settings.Spotify.PreferredDeviceId,
            device.Id,
            StringComparison.Ordinal)
            ? " · Standardgerät"
            : string.Empty;

        string automatic = _settings.Spotify.AutoTransferToPreferredDevice
            ? " · automatische Übernahme aktiv"
            : string.Empty;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDeviceStatusText.Text =
            $"{device.Type} · {active}{volume}{restricted}{preferred}{automatic}";
    }


    private async Task SaveSpotifyDeviceBehaviorAsync()
    {
        _settings.Spotify.AutoTransferToPreferredDevice = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyAutoTransferPreferredBox.IsChecked == true;
        _settings.Spotify.UseActiveDeviceWhenPreferredUnavailable = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyUseActiveFallbackBox.IsChecked == true;
        _settings.Spotify.SmartAutomationEnabled = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifySmartAutomationBox.IsChecked == true;
        _settings.Spotify.HealthMonitorEnabled = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyHealthMonitorBox.IsChecked == true;
        _settings.Spotify.AutoRecoverPlayback = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyAutoRecoverBox.IsChecked == true;
        await _settingsStore.SaveAsync(_settings);
        UpdateSpotifyDeviceSelectionUi();
    }

    private async Task ActivatePreferredSpotifyDeviceAsync()
    {
        await ExecuteUiActionAsync(
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyActivatePreferredDeviceButton,
            "Spotify-Standardgerät aktivieren",
            async () =>
            {
                SpotifyDevice? device = null;
                await ExecuteSpotifyAsync(async () =>
                {
                    device = await _spotifyModule.ActivatePreferredDeviceAsync(play: false);
                });
                if (device is null)
                {
                    return;
                }

                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDeviceBox.SelectedItem = device;
                SettingsPageViewHost.SpotifyDeviceBox.SelectedItem = device;
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDeviceStatusText.Text = $"{device.Name} wurde als Wiedergabegerät aktiviert.";
                RefreshSpotifyUi();
            });
    }

    private async Task TransferSelectedSpotifyDeviceAsync(bool play)
    {
        if (ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDeviceBox.SelectedItem is not SpotifyDevice device)
        {
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDeviceStatusText.Text = "Bitte zuerst ein Spotify-Gerät auswählen.";
            return;
        }

        await ExecuteUiActionAsync(
            play ? ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyTransferAndPlayDeviceButton : ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyTransferDeviceButton,
            play ? "Spotify-Wiedergabe übertragen und starten" : "Spotify-Wiedergabe übertragen",
            async () =>
            {
                await ExecuteSpotifyAsync(() => _spotifyModule.TransferPlaybackAsync(device.Id, play));
                _settings.Spotify.PreferredDeviceId = device.Id;
                await _settingsStore.SaveAsync(_settings);
                await RefreshSpotifyAsync();
            });
    }

    private async Task SaveSelectedSpotifyDeviceAsync()
    {
        if (ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDeviceBox.SelectedItem is not SpotifyDevice device)
        {
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDeviceStatusText.Text = "Bitte zuerst ein Spotify-Gerät auswählen.";
            return;
        }

        _settings.Spotify.PreferredDeviceId = device.Id;
        await _settingsStore.SaveAsync(_settings);
        SettingsPageViewHost.SpotifyDeviceBox.SelectedItem = device;
        UpdateSpotifyDeviceSelectionUi();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDeviceStatusText.Text += " · gespeichert";
    }

    private async Task SaveSpotifySmartAutomationSettingsAsync()
    {
        _settings.Spotify.SmartAutomationEnabled = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifySmartAutomationBox.IsChecked == true;
        _settings.Spotify.HealthMonitorEnabled = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyHealthMonitorBox.IsChecked == true;
        _settings.Spotify.AutoRecoverPlayback = ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyAutoRecoverBox.IsChecked == true;
        await _settingsStore.SaveAsync(_settings);
        RefreshSpotifyAutomationLogUi();
    }

    private async Task CreateDefaultSpotifyAutomationRulesAsync()
    {
        var rules = new List<SpotifyAutomationRuleSettings>();
        if (!string.IsNullOrWhiteSpace(_settings.Obs.StartScene))
        {
            rules.Add(new() { Name = "Startszene-Musik", TriggerValue = _settings.Obs.StartScene, ActionType = "StartPlaylist", PlaylistUri = _settings.Spotify.StartPlaylistUri, Shuffle = _settings.Spotify.ShuffleSelectedPlaylist });
        }

        if (!string.IsNullOrWhiteSpace(_settings.Obs.LiveScene))
        {
            rules.Add(new() { Name = "Live-Szene fortsetzen", TriggerValue = _settings.Obs.LiveScene, ActionType = "Resume" });
        }

        if (!string.IsNullOrWhiteSpace(_settings.Obs.EndScene))
        {
            rules.Add(new() { Name = "Endszene-Musik", TriggerValue = _settings.Obs.EndScene, ActionType = "StartPlaylist", PlaylistUri = _settings.Spotify.StartPlaylistUri, Shuffle = true });
        }

        _settings.Spotify.AutomationRules = rules;
        await _settingsStore.SaveAsync(_settings);
        _spotifyAutomationLog.Add("Regeln", $"{rules.Count} Standardregeln aus den OBS-Szenen erstellt.");
        RefreshSpotifyAutomationLogUi();
    }

    private async Task EditSpotifySceneMusicAsync()
    {
        IReadOnlyList<string> scenes = _servicesObsScenes
            .Select(scene => scene.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        if (scenes.Count == 0)
        {
            MessageBox.Show(
                this,
                "Es wurden noch keine OBS-Szenen geladen. Bitte zuerst OBS verbinden.",
                "Spotify-Szenenmusik",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var window = new SpotifySceneMusicWindow(
            SpotifySceneMusicRuleService.CreateRows(scenes, _settings.Spotify.AutomationRules),
            _spotifyModule.GetSnapshot().Playlists)
        {
            Owner = this
        };
        if (window.ShowDialog() != true)
        {
            return;
        }

        _settings.Spotify.AutomationRules =
            SpotifySceneMusicRuleService.ApplyRows(_settings.Spotify.AutomationRules, window.Rows).ToList();
        _settings.Spotify.SmartAutomationEnabled = true;
        await _settingsStore.SaveAsync(_settings);
        RefreshSpotifyAutomationUi(_spotifyModule.GetSnapshot());
    }

    private async Task ExecuteSpotifySceneAutomationAsync(string sceneName, bool force = false)
    {
        if ((!_settings.Spotify.SmartAutomationEnabled && !force) || string.IsNullOrWhiteSpace(sceneName))
        {
            return;
        }

        if (!await _spotifyAutomationLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            var rules = _settings.Spotify.AutomationRules
                .Where(r => r.Enabled && string.Equals(r.TriggerType, "ObsSceneChanged", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.TriggerValue, sceneName, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (SpotifyAutomationRuleSettings? rule in rules)
            {
                try
                {
                    if (rule.DelaySeconds > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(rule.DelaySeconds));
                    }

                    bool isConfiguredLiveScene = string.Equals(
                        sceneName,
                        string.IsNullOrWhiteSpace(_settings.Obs.LiveScene) ? "Game" : _settings.Obs.LiveScene.Trim(),
                        StringComparison.OrdinalIgnoreCase);
                    if (isConfiguredLiveScene &&
                        string.Equals(rule.ActionType, "Pause", StringComparison.OrdinalIgnoreCase) &&
                        _settings.Spotify.SetVolumeOnLiveTransition &&
                        !_settings.Spotify.MuteOnLiveTransition)
                    {
                        int liveVolume = Math.Clamp(_settings.Spotify.LiveVolumePercent, 0, 100);
                        await _spotifyModule.SetVolumeImmediateAsync(liveVolume);
                        _spotifyAutomationLog.Add(rule.Name,
                            $"Live-Lautstärke gesetzt: {liveVolume} % (veraltete Pause-Regel übersprungen).");
                        continue;
                    }

                    switch (rule.ActionType)
                    {
                        case "StartPlaylist":
                            if (string.IsNullOrWhiteSpace(rule.PlaylistUri))
                            {
                                throw new InvalidOperationException("Keine Playlist in der Regel hinterlegt.");
                            }

                            await _spotifyModule.StartPlaylistAsync(
                                rule.PlaylistUri,
                                applyConfiguredStartVolume: false,
                                shuffleOverride: rule.Shuffle);
                            break;
                        case "Pause": await _spotifyModule.PauseAsync(); break;
                        case "SetVolume": await _spotifyModule.SetVolumeImmediateAsync(Math.Clamp(rule.VolumePercent, 0, 100)); break;
                        default: await _spotifyModule.ResumeAsync(); break;
                    }
                    if (rule.ActionType is "StartPlaylist" or "Resume")
                    {
                        int targetVolume = Math.Clamp(rule.VolumePercent, 0, 100);
                        if (rule.FadeEnabled && rule.FadeMilliseconds > 0)
                        {
                            await _spotifyModule.FadeToAsync(
                                targetVolume,
                                TimeSpan.FromMilliseconds(Math.Clamp(rule.FadeMilliseconds, 0, 60_000)),
                                pauseAtEnd: false);
                        }
                        else
                        {
                            await _spotifyModule.SetVolumeImmediateAsync(targetVolume);
                        }
                    }
                    _spotifyAutomationLog.Add(rule.Name, $"Aktion {rule.ActionType} für Szene '{sceneName}' ausgeführt.");
                }
                catch (Exception ex)
                {
                    _spotifyAutomationLog.Add(rule.Name, ex.Message, false);
                }
            }
        }
        finally
        {
            _spotifyAutomationLock.Release();
            RefreshSpotifyAutomationLogUi();
        }
    }

    private async Task RunSpotifyHealthMonitorAsync(SpotifySnapshot snapshot)
    {
        if (!_settings.Spotify.HealthMonitorEnabled || !snapshot.Authenticated)
        {
            return;
        }

        string status = snapshot.Playback.Device is null ? "Kein aktives Gerät" : snapshot.Playback.Device.IsRestricted ? "Gerät nicht steuerbar" : snapshot.Playback.IsPlaying ? "Wiedergabe aktiv" : "Bereit / pausiert";
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyHealthStatusText.Text = status;
        if (!_settings.Spotify.AutoRecoverPlayback || snapshot.Playback.Device is not null || DateTimeOffset.UtcNow - _lastSpotifyHealthRecoveryAt < TimeSpan.FromMinutes(2))
        {
            return;
        }

        _lastSpotifyHealthRecoveryAt = DateTimeOffset.UtcNow;
        try
        {
            SpotifyDevice device = await _spotifyModule.ActivatePreferredDeviceAsync(play: false);
            _spotifyAutomationLog.Add("Health Monitor", $"Wiedergabegerät '{device.Name}' automatisch wieder aktiviert.");
        }
        catch (Exception ex)
        {
            _spotifyAutomationLog.Add("Health Monitor", "Automatische Gerätewiederherstellung fehlgeschlagen: " + ex.Message, false);
        }
        RefreshSpotifyAutomationLogUi();
    }

    private void RefreshSpotifyAutomationUi(SpotifySnapshot snapshot)
    {
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyAutomationStatusText.Text = _settings.Spotify.SmartAutomationEnabled
            ? $"Aktiv · {_settings.Spotify.AutomationRules.Count(r => r.Enabled)} Regeln"
            : "Deaktiviert";
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyAutomationRulesList.ItemsSource = _settings.Spotify.AutomationRules.Select(r => $"{(r.Enabled ? "✓" : "–")} {r.Name}: {r.TriggerValue} → {r.ActionType}").ToList();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyHealthStatusText.Text = !snapshot.Authenticated ? "Spotify nicht verbunden" : snapshot.Playback.Device is null ? "Kein aktives Gerät" : snapshot.Playback.Device.IsRestricted ? "Gerät nicht fernsteuerbar" : "Verbindung gesund";
        RefreshSpotifyAutomationLogUi();
    }
}
