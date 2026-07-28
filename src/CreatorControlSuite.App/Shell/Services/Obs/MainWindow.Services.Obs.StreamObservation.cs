#nullable enable

using System.Windows;
using CreatorControlSuite.App.Services;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Logging;
using CreatorControlSuite.Modules.OBS.Models;
using CreatorControlSuite.Modules.Workflow.Models;

namespace CreatorControlSuite.App.Shell;

public partial class MainWindow
{
    private static DateTimeOffset ResolveObservedObsStreamStartedAt(
        string? outputTimecode) =>
        ObsDashboardApplicationService.ResolveObservedStreamStartedAt(
            outputTimecode,
            DateTimeOffset.Now);

    private DateTimeOffset? ResolveLiveStreamStartedAt() =>
        ObsDashboardApplicationService.ResolveLiveStartedAt(
            _twitchStreamStartedAt,
            _streamSessionStartedAt,
            _twitchSessionObservedAt);

    private void ApplyTwitchLiveStreamStartedAt(DateTimeOffset? startedAt)
    {
        if (startedAt is null)
        {
            return;
        }

        _twitchStreamStartedAt = startedAt;
        _streamSessionStartedAt = startedAt;
        StreamSessionStats stats = _workflowModule.Service.SessionStats;
        if (stats.EndedAt is null)
        {
            stats.StartedAt = startedAt;
        }
    }

    private async Task HandleObservedStreamStartAsync()
    {
        if (!_spotifyStartPlaylistTriggeredForCurrentStream)
        {
            try
            {
                await StartConfiguredSpotifyPlaylistAtStreamStartAsync();
            }
            catch (Exception exception)
            {
                _appLogger.Write(
                    AppLogLevel.Warning,
                    "Spotify.StartPlaylist",
                    "Ausgewählte Startplaylist konnte beim erkannten " +
                    "Streamstart nicht gestartet werden: " +
                    exception.Message,
                    exception);
                AddDashboardNotification(
                    "Spotify-Startplaylist konnte nicht gestartet werden: " +
                    exception.Message,
                    "Warnung");
            }
        }

        _ = StartLegacyStreamAutomationSafeAsync();
    }

    private async Task StartLegacyStreamAutomationSafeAsync()
    {
        try
        {
            await StartLegacyStreamAutomationAsync();
        }
        catch (Exception exception)
        {
            _appLogger.Write(
                AppLogLevel.Warning,
                "StreamStart",
                "Streamstart-Automation konnte nicht vollständig gestartet " +
                "werden: " + exception.Message,
                exception);
        }
    }

    private async Task<bool> GetTrackedObsInputMuteAsync(
        IReadOnlyList<ObsInputInfo> inputs,
        string configuredSource,
        IReadOnlyList<string> preferredExactNames,
        IReadOnlyList<string> fallbackNameParts)
    {
        if (!_obsClient.IsConnected || inputs.Count == 0)
        {
            return false;
        }

        ObsInputInfo? input =
            ObsDashboardApplicationService.SelectTrackedInput(
                inputs,
                configuredSource,
                preferredExactNames,
                fallbackNameParts);
        if (input is null)
        {
            return false;
        }

        try
        {
            ObsInputAudioState state =
                await _obsClient.GetInputAudioStateAsync(input.Name);
            return state.Muted;
        }
        catch (Exception exception)
        {
            _appLogger.Write(
                AppLogLevel.Warning,
                "OBS.MuteState",
                $"Mute-Status für OBS-Quelle '{input.Name}' konnte nicht " +
                $"gelesen werden: {exception.Message}",
                exception);
            return false;
        }
    }

    private async Task RefreshDashboardObsScenePreviewAsync(
        string? sceneName = null)
    {
        try
        {
            if (!_obsClient.IsConnected)
            {
                DashboardPageViewHost.DashboardObsScenePreviewImage.Source =
                    null;
                DashboardPageViewHost.DashboardObsScenePreviewPlaceholder
                    .Visibility = Visibility.Visible;
                return;
            }

            sceneName ??= await _obsClient.GetCurrentProgramSceneAsync();
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return;
            }

            double previewWidth = GetDashboardObsScenePreviewWidth(
                _settings.Dashboard.ObsScenePreviewSize);
            byte[] bytes = await _obsClient.GetSourceScreenshotAsync(
                sceneName,
                (int)Math.Clamp(previewWidth, 160, 1920),
                imageHeight: null);
            if (bytes.Length == 0)
            {
                DashboardPageViewHost.DashboardObsScenePreviewImage.Source =
                    null;
                DashboardPageViewHost.DashboardObsScenePreviewPlaceholder
                    .Visibility = Visibility.Visible;
                return;
            }

            using var stream = new MemoryStream(bytes);
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption =
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            if (bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
            {
                _dashboardObsPreviewAspect =
                    bitmap.PixelWidth / (double)bitmap.PixelHeight;
                ApplyDashboardObsScenePreviewSize();
            }

            DashboardPageViewHost.DashboardObsScenePreviewImage.Source =
                bitmap;
            DashboardPageViewHost.DashboardObsScenePreviewPlaceholder
                .Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            DashboardPageViewHost.DashboardObsScenePreviewImage.Source = null;
            DashboardPageViewHost.DashboardObsScenePreviewPlaceholder
                .Visibility = Visibility.Visible;
            _appLogger.Write(
                AppLogLevel.Warning,
                "OBS",
                "OBS-Szenenvorschau konnte nicht geladen werden.",
                exception);
        }
    }

    private async Task SwitchObsSceneAsync()
    {
        if (SettingsPageViewHost.ObsScenesList.SelectedItem is not
            ObsSceneInfo scene)
        {
            MessageBox.Show(
                "Bitte zuerst eine Szene auswählen.",
                "OBS",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        await _obsClient.SetCurrentProgramSceneAsync(scene.Name);
        await RefreshObsAsync();
    }

    private async Task ToggleDashboardHeaderStreamAsync()
    {
        try
        {
            if (!_obsClient.IsConnected)
            {
                AddDashboardNotification(
                    "OBS ist nicht verbunden.",
                    "Warnung");
                return;
            }

            ObsSnapshot snapshot = await _obsClient.GetSnapshotAsync();
            if (snapshot.Stream?.OutputActive == true)
            {
                await StopObsStreamAsync();
            }
            else
            {
                await StartObsStreamAsync();
            }

            await RefreshObsAsync();
        }
        catch (Exception exception)
        {
            AddDashboardNotification(
                "Stream-Aktion fehlgeschlagen: " + exception.Message,
                "Fehler");
        }
    }

    private async Task StartConfiguredSpotifyPlaylistAtStreamStartAsync()
    {
        AppSettings persisted =
            await _settingsStore.LoadAsync(CancellationToken.None);
        if (!persisted.Workflow.AutoStartSpotifyPlaylist)
        {
            _appLogger.Write(
                AppLogLevel.Information,
                "Spotify.StartPlaylist",
                "Automatischer Playliststart ist in den gespeicherten " +
                "Einstellungen deaktiviert.");
            return;
        }

        string playlistUri =
            persisted.Spotify.StartPlaylistUri?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(playlistUri))
        {
            throw new InvalidOperationException(
                "Für den Streamstart ist keine dauerhaft gespeicherte " +
                "Spotify-Playlist ausgewählt.");
        }

        if (!_spotifyModule.GetSnapshot().Authenticated)
        {
            await _spotifyModule.ConnectAsync(CancellationToken.None);
        }

        Exception? firstFailure = null;
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                await _spotifyModule.StartPlaylistAsync(
                    playlistUri,
                    startVolumePercent:
                        persisted.Spotify.StartVolumePercent,
                    cancellationToken: CancellationToken.None);
                _spotifyStartPlaylistTriggeredForCurrentStream = true;
                AddDashboardNotification(
                    "Spotify-Startplaylist wurde gestartet.",
                    "Info");
                _appLogger.Write(
                    AppLogLevel.Information,
                    "Spotify.StartPlaylist",
                    "Gespeicherte Startplaylist wurde gestartet: " +
                    playlistUri);
                return;
            }
            catch (Exception exception) when (attempt == 1)
            {
                firstFailure = exception;
                _appLogger.Write(
                    AppLogLevel.Warning,
                    "Spotify.StartPlaylist",
                    "Erster Startversuch fehlgeschlagen; erneuter Versuch " +
                    "in 2 Sekunden: " + exception.Message,
                    exception);
                await Task.Delay(TimeSpan.FromSeconds(2));
                if (!_spotifyModule.GetSnapshot().Authenticated)
                {
                    await _spotifyModule.ConnectAsync(CancellationToken.None);
                }
            }
        }

        throw new InvalidOperationException(
            "Spotify konnte die gespeicherte Startplaylist auch nach dem " +
            "Wiederholungsversuch nicht starten.",
            firstFailure);
    }
}
