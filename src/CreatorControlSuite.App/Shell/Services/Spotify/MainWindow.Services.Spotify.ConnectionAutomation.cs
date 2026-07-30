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
    private async Task AuthorizeSpotifyAsync()
    {
        try
        {
            await SaveSettingsAsync();

            SettingsPageViewHost.SpotifyConnectionStatusText.Text =
                "Spotify-Autorisierung wird geöffnet ...";
            SettingsPageViewHost.SpotifyConnectionStatusText.Foreground =
                System.Windows.Media.Brushes.Goldenrod;

            await _spotifyModule.AuthorizeAsync();

            RefreshSpotifyUi();
        }
        catch (SpotifyRateLimitException exception)
        {
            BeginSpotifyRateLimitCooldown(exception.RetryAfter);
        }
        catch (Exception exception)
        {
            SettingsPageViewHost.SpotifyConnectionStatusText.Text = exception.Message;
            SettingsPageViewHost.SpotifyConnectionStatusText.Foreground =
                System.Windows.Media.Brushes.IndianRed;

            MessageBox.Show(
                exception.Message,
                "Spotify-Autorisierung fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ConnectSpotifyAsync(
        bool showErrorDialog = true)
    {
        try
        {
            SettingsPageViewHost.SpotifyConnectionStatusText.Text =
                "Spotify wird verbunden ...";
            SettingsPageViewHost.SpotifyConnectionStatusText.Foreground =
                System.Windows.Media.Brushes.Goldenrod;

            await _spotifyModule.ConnectAsync(CancellationToken.None);
            _spotifyOverlayConnectionLatched = true;
            _lastSpotifyOverlayMuted = null;

            RefreshSpotifyUi();
        }
        catch (SpotifyRateLimitException exception)
        {
            BeginSpotifyRateLimitCooldown(exception.RetryAfter);
        }
        catch (Exception exception)
        {
            SettingsPageViewHost.SpotifyConnectionStatusText.Text = exception.Message;
            SettingsPageViewHost.SpotifyConnectionStatusText.Foreground =
                System.Windows.Media.Brushes.IndianRed;

            if (showErrorDialog)
            {
                MessageBox.Show(
                    exception.Message,
                    "Spotify-Verbindung fehlgeschlagen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private async Task DisconnectSpotifyAsync()
    {
        _spotifyExplicitDisconnectInProgress = true;
        await _spotifyModule.DisconnectAsync(CancellationToken.None);
        _spotifyOverlayConnectionLatched = false;
        _lastStableSpotifyPlayback = null;
        _lastSpotifyOverlayMuted = null;

        try
        {
            await UpdateActiveOverlayJsonAsync(root =>
            {
                JsonObject spotify = root["spotify"] as JsonObject ?? [];
                spotify["connected"] = false;
                spotify["isPlaying"] = false;
                spotify["showInOverlay"] = false;
                spotify["visible"] = false;
                root["spotify"] = spotify;
            });
        }
        catch (Exception exception)
        {
            _appLogger.Write(AppLogLevel.Debug, "Spotify", "Spotify-Trennstatus konnte nicht in die Overlay-JSON geschrieben werden: " + exception.Message);
        }

        SpotifyDashboardStatus.Text = "NICHT VERBUNDEN";
        SettingsPageViewHost.SpotifyConnectionStatusText.Text = "Nicht verbunden";
        SettingsPageViewHost.SpotifyConnectionStatusText.Foreground =
            System.Windows.Media.Brushes.Gray;
        SettingsPageViewHost.SpotifyDeviceBox.ItemsSource = null;
        SettingsPageViewHost.SpotifyPlaylistBox.ItemsSource = null;
        SettingsPageViewHost.SpotifyTrackText.Text = "Kein Titel";
        SettingsPageViewHost.SpotifyPlaybackDetailText.Text =
            "Playerstatus unbekannt";

        RefreshDashboardServiceActionButtons();
    }


    private Task ApplyCombinedAlertDuckingAsync()
    {
        int externalCount = _externalAlertActivity.ActiveCount;
        bool isRunning = _suiteAlertRunning || externalCount > 0;
        int pending = _suiteAlertQueueLength + Math.Max(0, externalCount - (isRunning ? 1 : 0));
        string detail = externalCount > 0 ? $"Streamer.bot/externe Alerts aktiv: {externalCount}" : "Suite-Alertstatus";
        return HandleSpotifyAlertMuteAsync(new AlertPlaybackState(isRunning, null, pending, isRunning ? DateTimeOffset.Now : null, detail));
    }

    private async Task HandleSpotifyAlertMuteAsync(AlertPlaybackState state)
    {
        await _spotifyAlertMuteGate.WaitAsync();
        try
        {
            if (!_settings.Spotify.SmartAutomationEnabled ||
                !_settings.Spotify.MuteDuringAlerts ||
                string.Equals(_settings.Spotify.AlertDuckingMode, "None", StringComparison.OrdinalIgnoreCase))
            {
                if (!state.IsRunning && _spotifyAlertMuteActive)
                {
                    await RestoreSpotifyVolumeAfterAlertAsync();
                }
                return;
            }

            if (state.IsRunning)
            {
                if (_spotifyAlertMuteActive)
                {
                    return;
                }

                SpotifySnapshot snapshot = _spotifyModule.GetSnapshot();
                SpotifyPlaybackState playback = snapshot.Playback;
                if (!snapshot.Authenticated || !playback.IsPlaying || playback.Device is null)
                {
                    Dispatcher.Invoke(() =>
                        _spotifyAutomationPageViewModel.SetAlertStatus(
                            "Kein laufender Spotify-Titel – keine Lautstärkeabsenkung nötig."));
                    return;
                }

                _spotifyVolumeBeforeAlert = Math.Clamp(playback.Device.VolumePercent, 0, 100);
                _spotifyWasPlayingBeforeAlert = playback.IsPlaying;
                _spotifyAlertMuteActive = true;

                int alertVolume = Math.Clamp(_settings.Spotify.AlertMuteVolumePercent, 0, 100);
                await FadeSpotifyVolumeAsync(
                    _spotifyVolumeBeforeAlert.Value,
                    alertVolume,
                    _settings.Spotify.FadeDuringAlerts ? _settings.Spotify.AlertFadeOutMilliseconds : 0);

                Dispatcher.Invoke(() =>
                {
                    _spotifyAutomationPageViewModel.SetAlertStatus(
                        $"Alert läuft: Spotify {_spotifyVolumeBeforeAlert}% → {alertVolume}%",
                        "Warning");
                });
                return;
            }

            // Bei mehreren Alerts bleibt die Musik abgesenkt, bis auch die Queue leer ist.
            if (state.QueueLength > 0 || !_spotifyAlertMuteActive)
            {
                return;
            }

            await RestoreSpotifyVolumeAfterAlertAsync();
        }
        catch (Exception ex)
        {
            _appLogger.Write(AppLogLevel.Warning, "Spotify", "Spotify konnte für den Alert nicht automatisch geregelt werden.", ex);
            Dispatcher.Invoke(() =>
            {
                _spotifyAutomationPageViewModel.SetAlertStatus(
                    "Spotify-Alert-Ducking fehlgeschlagen: " + ex.Message,
                    "Error");
            });
        }
        finally
        {
            _spotifyAlertMuteGate.Release();
        }
    }

    private async Task RestoreSpotifyVolumeAfterAlertAsync()
    {
        int? restoreVolume = _spotifyVolumeBeforeAlert;
        bool shouldRestore = _spotifyWasPlayingBeforeAlert && restoreVolume.HasValue;

        _spotifyAlertMuteActive = false;
        _spotifyVolumeBeforeAlert = null;
        _spotifyWasPlayingBeforeAlert = false;

        if (!shouldRestore)
        {
            return;
        }

        int currentVolume = Math.Clamp(_spotifyModule.GetSnapshot().Playback.Device?.VolumePercent ?? 0, 0, 100);
        await FadeSpotifyVolumeAsync(
            currentVolume,
            restoreVolume!.Value,
            _settings.Spotify.FadeDuringAlerts ? _settings.Spotify.AlertFadeInMilliseconds : 0);

        Dispatcher.Invoke(() =>
        {
            _spotifyAutomationPageViewModel.SetAlertStatus(
                $"Alert beendet: Spotify auf {restoreVolume.Value}% zurückgestellt.",
                "Success");
        });
    }

    private static void SelectMillisecondsComboItem(ComboBox comboBox, int milliseconds)
    {
        if (comboBox is null)
        {
            return;
        }

        foreach (ComboBoxItem item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (int.TryParse(item.Tag?.ToString(), out int value) && value == milliseconds)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
        comboBox.SelectedIndex = 2;
    }

    private static int GetMillisecondsComboValue(ComboBox comboBox, int fallback)
    {
        return comboBox?.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int value)
            ? value
            : fallback;
    }

    private async Task FadeSpotifyVolumeAsync(int fromVolume, int toVolume, int durationMilliseconds)
    {
        fromVolume = Math.Clamp(fromVolume, 0, 100);
        toVolume = Math.Clamp(toVolume, 0, 100);
        durationMilliseconds = Math.Clamp(durationMilliseconds, 0, 5000);
        if (durationMilliseconds == 0 || fromVolume == toVolume)
        {
            await SetSpotifyVolumeTrackedAsync(toVolume);
            return;
        }

        int steps = Math.Clamp(durationMilliseconds / 100, 2, 10);
        int delay = Math.Max(50, durationMilliseconds / steps);
        for (int step = 1; step <= steps; step++)
        {
            int volume = (int)Math.Round(fromVolume + ((toVolume - fromVolume) * (step / (double)steps)));
            await SetSpotifyVolumeTrackedAsync(Math.Clamp(volume, 0, 100));
            if (step < steps)
            {
                await Task.Delay(delay);
            }
        }
    }

    private async Task QueueSpotifyVolumeUpdateAsync(int? explicitVolume = null)
    {
        SettingsPageViewHost.SpotifyVolumeValueText.Text =
            $"{(int)Math.Round(SettingsPageViewHost.SpotifyVolumeSlider.Value)} %";

        if (_updatingSpotifyUi)
        {
            return;
        }

        _spotifyVolumeChangeCts?.Cancel();
        _spotifyVolumeChangeCts?.Dispose();

        _spotifyVolumeChangeCts =
            new CancellationTokenSource();

        CancellationToken cancellationToken =
            _spotifyVolumeChangeCts.Token;

        try
        {
            int volume = explicitVolume ??
                (int)Math.Round(
                    SettingsPageViewHost.SpotifyVolumeSlider.Value);

            volume = Math.Clamp(volume, 0, 100);
            _lastRequestedSpotifyVolumePercent = volume;
            _lastRequestedSpotifyVolumeAt = DateTimeOffset.UtcNow;

            // Sliderbewegungen sollen ohne zusätzliche Verzögerung hörbar werden.
            await _spotifyModule.SetVolumeImmediateAsync(
                volume,
                cancellationToken);

            await ApplySpotifyOverlayMuteStateAsync(volume <= 0);
            await WriteSpotifyOverlayRuntimeDataAsync(_spotifyModule.GetSnapshot(), _spotifyModule.GetSnapshot().Playback);
        }
        catch (OperationCanceledException)
        {
            // A newer slider position superseded this update.
        }
        catch (Exception exception)
        {
            SettingsPageViewHost.SpotifyPlaybackDetailText.Text =
                "Lautstärke konnte nicht gesetzt werden: " +
                exception.Message;

            SettingsPageViewHost.SpotifyPlaybackDetailText.Foreground =
                System.Windows.Media.Brushes.IndianRed;
        }
    }


    private void RefreshSpotifyStatisticsUi()
    {
        SpotifyListeningStatisticsSnapshot statistics = _spotifyListeningStatistics.GetSnapshot();
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyStatisticsSummaryText.Text = $"{statistics.TotalPlays} erkannte Titelstarts · {statistics.TotalListeningTime:hh\\:mm\\:ss} Wiedergabezeit";
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyTopTracksList.ItemsSource = statistics.TopTracks;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyTopArtistsList.ItemsSource = statistics.TopArtists;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyStatisticsEmptyText.Visibility = statistics.TotalPlays == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task RefreshSpotifyAsync()
    {
        try
        {
            await _spotifyModule.RefreshAsync();
            RefreshSpotifyUi();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Spotify konnte nicht aktualisiert werden",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task StartSpotifyPlaylistAsync()
    {
        await ExecuteSpotifyAsync(
            () => _spotifyModule.StartConfiguredPlaylistAsync());
    }

    private async Task TestSpotifyFadeAsync()
    {
        int seconds = int.Parse(
            SettingsPageViewHost.SpotifyFadeOutSecondsBox.Text.Trim());

        await ExecuteSpotifyAsync(
            () => _spotifyModule.FadeToAsync(
                targetVolumePercent: 0,
                duration: TimeSpan.FromSeconds(seconds),
                pauseAtEnd:
                    SettingsPageViewHost.SpotifyPauseAfterFadeBox.IsChecked == true));
    }

    private async Task ExecuteSpotifyAsync(
        Func<Task> action)
    {
        if (DateTimeOffset.Now < _spotifyRateLimitUntil)
        {
            UpdateSpotifyRateLimitStatus();
            return;
        }

        try
        {
            await action();
            await Task.Delay(500);
            await _spotifyModule.RefreshPlaybackAsync();
            await _spotifyModule.RefreshLibraryIfStaleAsync();
            ClearSpotifyRateLimitStatus();
            RefreshSpotifyUi();
        }
        catch (SpotifyRateLimitException exception)
        {
            BeginSpotifyRateLimitCooldown(exception.RetryAfter);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Spotify-Aktion fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void BeginSpotifyRateLimitCooldown(TimeSpan retryAfter)
    {
        TimeSpan effectiveDelay = retryAfter <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(5)
            : retryAfter;

        _spotifyRateLimitUntil = DateTimeOffset.Now.Add(effectiveDelay);
        UpdateSpotifyRateLimitStatus();

        if (DateTimeOffset.Now - _lastSpotifyRateLimitNotice > TimeSpan.FromMinutes(1))
        {
            _lastSpotifyRateLimitNotice = DateTimeOffset.Now;
            AddDashboardNotification(
                $"Spotify API-Limit erreicht. Steuerung wird für etwa {Math.Ceiling(effectiveDelay.TotalSeconds):0} Sekunden pausiert.",
                "Warnung");
        }

        _spotifyRateLimitResetCts?.Cancel();
        _spotifyRateLimitResetCts?.Dispose();
        _spotifyRateLimitResetCts = new CancellationTokenSource();
        _ = ResetSpotifyRateLimitAfterDelayAsync(effectiveDelay, _spotifyRateLimitResetCts.Token);
    }

    private async Task ResetSpotifyRateLimitAfterDelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        try
        {
            while (DateTimeOffset.Now < _spotifyRateLimitUntil)
            {
                await Dispatcher.InvokeAsync(UpdateSpotifyRateLimitStatus);
                TimeSpan remaining = _spotifyRateLimitUntil - DateTimeOffset.Now;
                await Task.Delay(
                    remaining > TimeSpan.FromSeconds(1)
                        ? TimeSpan.FromSeconds(1)
                        : remaining,
                    cancellationToken);
            }

            await Dispatcher.InvokeAsync(ClearSpotifyRateLimitStatus);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void UpdateSpotifyRateLimitStatus()
    {
        int remaining = Math.Max(1, (int)Math.Ceiling((_spotifyRateLimitUntil - DateTimeOffset.Now).TotalSeconds));
        string message = $"Spotify-Limit erreicht – Steuerung in etwa {remaining} Sek. wieder verfügbar.";

        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyNowPlayingText.Text = message;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyNowPlayingText.Foreground = System.Windows.Media.Brushes.Orange;

        SettingsPageViewHost.SpotifyConnectionStatusText.Text = message;
        SettingsPageViewHost.SpotifyConnectionStatusText.Foreground = System.Windows.Media.Brushes.Orange;
    }

    private void ClearSpotifyRateLimitStatus()
    {
        _spotifyRateLimitUntil = DateTimeOffset.MinValue;
        RefreshSpotifyUi();
    }
}
