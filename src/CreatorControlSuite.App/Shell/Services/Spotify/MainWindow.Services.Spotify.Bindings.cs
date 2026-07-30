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
    private void InitializeSpotifyBindings()
    {
        SettingsPageViewHost.AuthorizeSpotifyButton.Click += async (_, _) =>
            await AuthorizeSpotifyAsync();

        SettingsPageViewHost.ConnectSpotifyButton.Click += async (_, _) =>
            await ConnectSpotifyAsync();

        SettingsPageViewHost.DisconnectSpotifyButton.Click += async (_, _) =>
            await DisconnectSpotifyAsync();

        SettingsPageViewHost.RefreshSpotifyButton.Click += async (_, _) =>
            await RefreshSpotifyAsync();

        SettingsPageViewHost.StartSpotifyPlaylistButton.Click += async (_, _) =>
            await StartSpotifyPlaylistAsync();

        SettingsPageViewHost.SpotifyPlayButton.Click += async (_, _) =>
            await ExecuteSpotifyAsync(
                () => _spotifyModule.ResumeAsync());

        SettingsPageViewHost.SpotifyPauseButton.Click += async (_, _) =>
            await ExecuteSpotifyAsync(
                () => _spotifyModule.PauseAsync());

        SettingsPageViewHost.SpotifyPreviousButton.Click += async (_, _) =>
            await ExecuteSpotifyAsync(
                () => _spotifyModule.PreviousAsync());

        SettingsPageViewHost.SpotifyNextButton.Click += async (_, _) =>
            await ExecuteSpotifyAsync(
                () => _spotifyModule.NextAsync());

        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyLaunchButton.Click += (_, _) => LaunchConfiguredExecutable(_settings.Spotify.ExecutablePath, "Spotify");
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPreviousButton.Click += async (_, _) =>
            await ExecuteSpotifyAsync(() => _spotifyModule.PreviousAsync());
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlayButton.Click += async (_, _) =>
            await ExecuteSpotifyAsync(() => _spotifyModule.ResumeAsync());
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPauseButton.Click += async (_, _) =>
            await ExecuteSpotifyAsync(() => _spotifyModule.PauseAsync());
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyNextButton.Click += async (_, _) =>
            await ExecuteSpotifyAsync(() => _spotifyModule.NextAsync());
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyShuffleButton.Click += async (_, _) =>
            await ExecuteSpotifyAsync(async () =>
            {
                bool enabled = !_spotifyModule.GetSnapshot().Playback.ShuffleEnabled;
                await _spotifyModule.SetShuffleAsync(enabled);
                await RefreshSpotifyAsync();
            });
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyRepeatButton.Click += async (_, _) =>
            await ExecuteSpotifyAsync(async () =>
            {
                string current = _spotifyModule.GetSnapshot().Playback.RepeatMode;
                string next = current?.ToLowerInvariant() switch
                {
                    "off" => "context",
                    "context" => "track",
                    _ => "off"
                };
                await _spotifyModule.SetRepeatAsync(next);
                await RefreshSpotifyAsync();
            });
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyRefreshButton.Click += async (_, _) =>
            await RefreshSpotifyAsync();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyProgressBar.PreviewMouseLeftButtonUp += async (_, _) =>
        {
            if (_updatingSpotifyUi || !ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyProgressBar.IsEnabled)
            {
                return;
            }

            int targetMs = (int)Math.Round(ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyProgressBar.Value);
            ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyProgressBar.IsEnabled = false;
            try
            {
                await ExecuteSpotifyAsync(() => _spotifyModule.SeekAsync(targetMs));
                await RefreshSpotifyAsync();
            }
            finally
            {
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyProgressBar.IsEnabled = true;
            }
        };
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyRefreshQueueButton.Click += async (_, _) =>
            await ExecuteUiActionAsync(
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyRefreshQueueButton,
                "Spotify-Warteschlange aktualisieren",
                async () =>
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.RefreshQueueAsync());
                    RefreshSpotifyUi();
                });
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlayQueueItemButton.Click += async (_, _) =>
        {
            if (ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyQueueList.SelectedItem is not SpotifyQueueItem selected)
            {
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyQueueStatusText.Text = "Bitte zuerst einen Titel aus der Warteschlange auswählen.";
                return;
            }

            await ExecuteUiActionAsync(
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlayQueueItemButton,
                "Spotify-Warteschlangentitel abspielen",
                async () =>
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.PlayTrackAsync(selected.Track));
                    await _spotifyModule.RefreshQueueAsync();
                    await _spotifyModule.RefreshRecentlyPlayedAsync();
                    ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyQueueStatusText.Text =
                        $"Wiedergabe gestartet: {selected.Track.Artist} – {selected.Track.Name}";
                    RefreshSpotifyUi();
                });
        };
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifySkipCurrentButton.Click += async (_, _) =>
            await ExecuteUiActionAsync(
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifySkipCurrentButton,
                "Spotify-Titel überspringen",
                async () =>
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.NextAsync());
                    await Task.Delay(350);
                    await _spotifyModule.RefreshQueueAsync();
                    await _spotifyModule.RefreshRecentlyPlayedAsync();
                    ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyQueueStatusText.Text = "Der aktuelle Titel wurde übersprungen.";
                    RefreshSpotifyUi();
                });
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyTrackSearchButton.Click += async (_, _) =>
            await SearchSpotifyTracksAsync();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyTrackSearchBox.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                await SearchSpotifyTracksAsync();
            }
        };
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistFilterBox.TextChanged += (_, _) =>
            ApplySpotifyPlaylistFilter();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyLoadPlaylistTracksButton.Click += async (_, _) =>
            await LoadSelectedSpotifyPlaylistTracksAsync();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyToggleFavoritePlaylistButton.Click += async (_, _) =>
            await ToggleSelectedSpotifyPlaylistFavoriteAsync();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyStartQuickPlaylistButton.Click += async (_, _) =>
            await StartSpotifyQuickPlaylistAsync(ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyQuickPlaylistBox.SelectedItem as SpotifyPlaylist);
        DashboardPageViewHost.DashboardSpotifyStartQuickPlaylistButton.Click += async (_, _) =>
            await StartSpotifyQuickPlaylistAsync(DashboardPageViewHost.DashboardSpotifyQuickPlaylistBox.SelectedItem as SpotifyPlaylist);
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistBox.SelectionChanged += (_, _) =>
            UpdateSpotifyFavoriteButton();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlayPlaylistTrackButton.Click += async (_, _) =>
            await ExecuteSelectedSpotifyPlaylistTrackAsync(playImmediately: true);
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyQueuePlaylistTrackButton.Click += async (_, _) =>
            await ExecuteSelectedSpotifyPlaylistTrackAsync(playImmediately: false);

        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaySelectedSearchResultButton.Click += async (_, _) =>
        {
            if (ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyTrackSearchResultsList.SelectedItem is not SpotifyTrackSearchItem selected)
            {
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyTrackSearchStatusText.Text = "Bitte zuerst einen Titel auswählen.";
                return;
            }

            await ExecuteUiActionAsync(
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaySelectedSearchResultButton,
                "Spotify-Titel abspielen",
                async () =>
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.PlayTrackAsync(selected.Track));
                    ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyTrackSearchStatusText.Text =
                        $"Wiedergabe gestartet: {selected.Track.Artist} – {selected.Track.Name}";
                    await RefreshSpotifyAsync();
                });
        };

        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyAddSelectedToQueueButton.Click += async (_, _) =>
        {
            if (ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyTrackSearchResultsList.SelectedItem is not SpotifyTrackSearchItem selected)
            {
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyTrackSearchStatusText.Text = "Bitte zuerst einen Titel auswählen.";
                return;
            }

            await ExecuteUiActionAsync(
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyAddSelectedToQueueButton,
                "Spotify-Titel zur Warteschlange hinzufügen",
                async () =>
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.AddToQueueAsync(selected.Track));
                    await _spotifyModule.RefreshQueueAsync();
                    ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyTrackSearchStatusText.Text =
                        $"Hinzugefügt: {selected.Track.Artist} – {selected.Track.Name}";
                    RefreshSpotifyUi();
                });
        };
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyRefreshHistoryButton.Click += async (_, _) =>
            await ExecuteUiActionAsync(
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyRefreshHistoryButton,
                "Spotify-Verlauf aktualisieren",
                async () =>
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.RefreshRecentlyPlayedAsync());
                    RefreshSpotifyUi();
                    ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyHistoryStatusText.Text = "Verlauf aktualisiert.";
                });
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlayHistoryButton.Click += async (_, _) =>
        {
            if (ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyHistoryList.SelectedItem is not SpotifyHistoryItem selected)
            {
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyHistoryStatusText.Text = "Bitte zuerst einen Titel auswählen.";
                return;
            }

            await ExecuteUiActionAsync(
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlayHistoryButton,
                "Spotify-Titel abspielen",
                async () =>
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.PlayTrackAsync(selected.Item.Track));
                    ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyHistoryStatusText.Text =
                        $"Wird abgespielt: {selected.Item.Track.Artist} – {selected.Item.Track.Name}";
                    RefreshSpotifyUi();
                });
        };
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyQueueHistoryButton.Click += async (_, _) =>
        {
            if (ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyHistoryList.SelectedItem is not SpotifyHistoryItem selected)
            {
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyHistoryStatusText.Text = "Bitte zuerst einen Titel auswählen.";
                return;
            }

            await ExecuteUiActionAsync(
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyQueueHistoryButton,
                "Spotify-Titel zur Warteschlange hinzufügen",
                async () =>
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.AddToQueueAsync(selected.Item.Track));
                    await _spotifyModule.RefreshQueueAsync();
                    ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyHistoryStatusText.Text =
                        $"Hinzugefügt: {selected.Item.Track.Artist} – {selected.Item.Track.Name}";
                    RefreshSpotifyUi();
                });
        };
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyResetStatisticsButton.Click += (_, _) =>
        {
            if (MessageBox.Show("Spotify-Statistik wirklich zurücksetzen?", "Spotify-Statistik", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            _spotifyListeningStatistics.Reset();
            RefreshSpotifyStatisticsUi();
        };

        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyOpenStatisticsButton.Click += (_, _) =>
            new SpotifyStatisticsWindow(_spotifyListeningStatistics) { Owner = this }.ShowDialog();

        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyRefreshSavedTracksButton.Click += async (_, _) =>
            await ExecuteUiActionAsync(
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyRefreshSavedTracksButton,
                "Spotify-Favoriten aktualisieren",
                async () =>
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.RefreshSavedTracksAsync());
                    RefreshSpotifyUi();
                    ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifySavedTracksStatusText.Text = "Gespeicherte Titel aktualisiert.";
                });
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaySavedTrackButton.Click += async (_, _) =>
        {
            if (ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifySavedTracksList.SelectedItem is not SpotifySavedTrackItem selected)
            {
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifySavedTracksStatusText.Text = "Bitte zuerst einen gespeicherten Titel auswählen.";
                return;
            }

            await ExecuteUiActionAsync(
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaySavedTrackButton,
                "Gespeicherten Spotify-Titel abspielen",
                async () =>
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.PlayTrackAsync(selected.Track));
                    ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifySavedTracksStatusText.Text =
                        $"Wird abgespielt: {selected.Track.Artist} – {selected.Track.Name}";
                    await RefreshSpotifyAsync();
                });
        };
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyRemoveSavedTrackButton.Click += async (_, _) =>
        {
            if (ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifySavedTracksList.SelectedItem is not SpotifySavedTrackItem selected)
            {
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifySavedTracksStatusText.Text = "Bitte zuerst einen gespeicherten Titel auswählen.";
                return;
            }

            await ExecuteUiActionAsync(
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyRemoveSavedTrackButton,
                "Spotify-Titel aus Favoriten entfernen",
                async () =>
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.SetTrackSavedAsync(selected.Track, false));
                    ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifySavedTracksStatusText.Text =
                        $"Aus Favoriten entfernt: {selected.Track.Artist} – {selected.Track.Name}";
                    RefreshSpotifyUi();
                });
        };
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyToggleCurrentSavedButton.Click += async (_, _) =>
        {
            SpotifyTrack? track = _spotifyModule.GetSnapshot().Playback.Track;
            if (track is null)
            {
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifySavedTracksStatusText.Text = "Aktuell läuft kein Spotify-Titel.";
                return;
            }

            await ExecuteUiActionAsync(
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyToggleCurrentSavedButton,
                "Spotify-Gefällt-mir-Status ändern",
                async () =>
                {
                    bool isSaved = await _spotifyModule.IsTrackSavedAsync(track);
                    await _spotifyModule.SetTrackSavedAsync(track, !isSaved);
                    ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifySavedTracksStatusText.Text = !isSaved
                        ? $"Zu Favoriten hinzugefügt: {track.Artist} – {track.Name}"
                        : $"Aus Favoriten entfernt: {track.Artist} – {track.Name}";
                    RefreshSpotifyUi();
                });
        };

        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyStartPlaylistButton.Click += async (_, _) =>
        {
            if (ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyPlaylistBox.SelectedItem is SpotifyPlaylist playlist)
            {
                await StartSpotifyPlaylistAndRememberAsync(playlist);
            }
        };
        DashboardPageViewHost.DashboardSpotifyStartPlaylistButton.Click += async (_, _) =>
        {
            if (DashboardPageViewHost.DashboardSpotifyPlaylistBox.SelectedItem is SpotifyPlaylist playlist)
            {
                await StartSpotifyPlaylistAndRememberAsync(playlist);
            }
        };
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyDeviceBox.SelectionChanged += (_, _) =>
            UpdateSpotifyDeviceSelectionUi();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyRefreshDevicesButton.Click += async (_, _) =>
            await ExecuteUiActionAsync(
                ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyRefreshDevicesButton,
                "Spotify-Geräte aktualisieren",
                async () =>
                {
                    await ExecuteSpotifyAsync(() => _spotifyModule.RefreshDevicesAsync());
                    RefreshSpotifyUi();
                });
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyTransferDeviceButton.Click += async (_, _) =>
            await TransferSelectedSpotifyDeviceAsync(play: false);
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyTransferAndPlayDeviceButton.Click += async (_, _) =>
            await TransferSelectedSpotifyDeviceAsync(play: true);
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifySetPreferredDeviceButton.Click += async (_, _) =>
            await SaveSelectedSpotifyDeviceAsync();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyActivatePreferredDeviceButton.Click += async (_, _) =>
            await ActivatePreferredSpotifyDeviceAsync();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyAutoTransferPreferredBox.Checked += async (_, _) => await SaveSpotifyDeviceBehaviorAsync();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyAutoTransferPreferredBox.Unchecked += async (_, _) => await SaveSpotifyDeviceBehaviorAsync();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyUseActiveFallbackBox.Checked += async (_, _) => await SaveSpotifyDeviceBehaviorAsync();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyUseActiveFallbackBox.Unchecked += async (_, _) => await SaveSpotifyDeviceBehaviorAsync();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifySmartAutomationBox.Checked += async (_, _) => await SaveSpotifySmartAutomationSettingsAsync();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifySmartAutomationBox.Unchecked += async (_, _) => await SaveSpotifySmartAutomationSettingsAsync();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyHealthMonitorBox.Checked += async (_, _) => await SaveSpotifySmartAutomationSettingsAsync();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyHealthMonitorBox.Unchecked += async (_, _) => await SaveSpotifySmartAutomationSettingsAsync();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyAutoRecoverBox.Checked += async (_, _) => await SaveSpotifySmartAutomationSettingsAsync();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyAutoRecoverBox.Unchecked += async (_, _) => await SaveSpotifySmartAutomationSettingsAsync();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyCreateDefaultRulesButton.Click += async (_, _) => await CreateDefaultSpotifyAutomationRulesAsync();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyEditSceneMusicButton.Click += async (_, _) => await EditSpotifySceneMusicAsync();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyTestAutomationButton.Click += async (_, _) => await ExecuteSpotifySceneAutomationAsync(_automationCurrentScene, force: true);
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyClearAutomationLogButton.Click += (_, _) => { _spotifyAutomationLog.Clear(); RefreshSpotifyAutomationLogUi(); };
    }
}
