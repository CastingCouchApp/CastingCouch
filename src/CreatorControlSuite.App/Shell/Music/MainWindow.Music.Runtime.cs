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
    private bool IsSpotifyMusicProvider() =>
        string.Equals(
            MusicProviderIds.Normalize(_settings.MusicPlayer?.ProviderId),
            MusicProviderIds.Spotify,
            StringComparison.OrdinalIgnoreCase);

    private bool IsYouTubeMusicProvider() =>
        string.Equals(
            MusicProviderIds.Normalize(_settings.MusicPlayer?.ProviderId),
            MusicProviderIds.YouTubeMusic,
            StringComparison.OrdinalIgnoreCase);

    private bool GetActiveMusicConnected()
    {
        if (IsYouTubeMusicProvider())
        {
            return _youTubeMusicModule.IsBridgeRunning;
        }

        return _spotifyModule.GetSnapshot().Authenticated;
    }

    private string GetSelectedMusicPlayerProviderId()
    {
        if (SettingsPageViewHost.MusicProviderYouTubeMusicRadio.IsChecked == true)
        {
            return MusicProviderIds.YouTubeMusic;
        }

        return MusicProviderIds.Spotify;
    }

    private void SelectMusicPlayerProviderRadio(string? providerId)
    {
        string normalized = MusicProviderIds.Normalize(providerId);
        bool isYouTube = string.Equals(normalized, MusicProviderIds.YouTubeMusic, StringComparison.OrdinalIgnoreCase);
        SettingsPageViewHost.MusicProviderYouTubeMusicRadio.IsChecked = isYouTube;
        SettingsPageViewHost.MusicProviderSpotifyRadio.IsChecked = !isYouTube;
    }

    private void UpdateMusicPlayerSettingsVisibility()
    {
        string providerId = GetSelectedMusicPlayerProviderId();
        bool isYouTube = string.Equals(providerId, MusicProviderIds.YouTubeMusic, StringComparison.OrdinalIgnoreCase);
        SettingsPageViewHost.SpotifyMusicSettingsPanel.Visibility = isYouTube ? Visibility.Collapsed : Visibility.Visible;
        SettingsPageViewHost.YouTubeMusicSettingsPanel.Visibility = isYouTube ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyMusicProviderUiState()
    {
        string providerId = MusicProviderIds.Normalize(_settings.MusicPlayer?.ProviderId);
        string displayName = MusicProviderIds.DisplayName(providerId);
        bool isSpotify = string.Equals(providerId, MusicProviderIds.Spotify, StringComparison.OrdinalIgnoreCase);

        DashboardPageViewHost.DashboardMusicModuleProvider.Text = displayName;
        DashboardTopMusicProviderText.Text = displayName.ToUpperInvariant();
        DashboardPageViewHost.DashboardQuickMusicTitle.Text = displayName;
        DashboardPageViewHost.DashboardQuickMusicDetail.Text = isSpotify
            ? "Player & Playlists"
            : "Bookmarklet-Bridge";
        DashboardPageViewHost.DashboardQuickStartSpotifyButton.Visibility = isSpotify
            ? Visibility.Visible
            : Visibility.Collapsed;
        ServicesPageViewHost.SpotifyServiceViewHost.ServicesSpotifyInactiveBanner.Visibility = isSpotify
            ? Visibility.Collapsed
            : Visibility.Visible;
        ServicesSpotifyButton.Content = isSpotify ? "●   Spotify" : "●   Spotify (inaktiv)";
        MusicPlayerPageViewHost.ApplyProvider(displayName, isSpotify);
        // YouTube Music liefert praktisch kein Album – Zeile ausblenden.
        Visibility albumVisibility = isSpotify ? Visibility.Visible : Visibility.Collapsed;
        DashboardTopMusicAlbumText.Visibility = albumVisibility;
    }

    private async Task ExecuteMusicCommandAsync(Func<Task> action)
    {
        try
        {
            await action();
            await Task.Delay(350);
            if (IsSpotifyMusicProvider() && _spotifyModule.GetSnapshot().Authenticated)
            {
                await _spotifyModule.RefreshPlaybackAsync();
            }

            await RefreshMusicPlayerUiAsync();
            if (IsSpotifyMusicProvider())
            {
                RefreshSpotifyUi();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Music Player", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private WorkflowPageActions CreateWorkflowPageActions()
        => new(
            PrepareStreamWithConfiguredServicesAsync,
            () => ExecuteWorkflowAsync(() => _workflowModule.Service.StartCountdownAsync()),
            () => ExecuteWorkflowAsync(() => _workflowModule.Service.StopCountdownAsync()),
            () => ExecuteWorkflowAsync(() => _workflowModule.Service.GoLiveAsync()),
            () => ExecuteWorkflowAsync(() => _workflowModule.Service.PauseAsync()),
            () => ExecuteWorkflowAsync(() => _workflowModule.Service.ResumeAsync()),
            async () =>
            {
                await ExecuteWorkflowAsync(() => _workflowModule.Service.EndAsync());
                await ResetTimedAutomationsAtStreamEndAsync();
            });

    private MusicPlayerPageActions CreateMusicPlayerPageActions()
        => new(
            () => ExecuteMusicCommandAsync(
                () => _musicPlayerRouter.PreviousAsync()),
            () => ExecuteMusicCommandAsync(
                () => _musicPlayerRouter.PlayPauseAsync()),
            () => ExecuteMusicCommandAsync(
                () => _musicPlayerRouter.NextAsync()),
            () => ExecuteMusicCommandAsync(
                () => _musicPlayerRouter.ConnectActiveAsync()),
            () => ExecuteMusicCommandAsync(
                () => _musicPlayerRouter.DisconnectActiveAsync()),
            () => _youTubeMusicModule.GetBookmarkletAsync(),
            OpenYouTubeMusicBookmarkletInstallPageAsync,
            GetYouTubeMusicBookmarkletDragDataAsync,
            () =>
            {
                NavigateToServicesTab(0, ServicesSpotifyButton);
                return Task.CompletedTask;
            },
            SeekMusicPlayerAsync,
            SetMusicPlayerVolumeAsync);

    private async Task SeekMusicPlayerAsync(double progressRatio)
    {
        if (!_musicPlayerRouter.ActivePlayer.SupportsSeek)
        {
            return;
        }

        NowPlayingSnapshot snapshot =
            await _musicPlayerRouter.GetSnapshotAsync();
        if (snapshot.DurationMs <= 0)
        {
            return;
        }

        int target = (int)(
            Math.Clamp(progressRatio, 0, 1) *
            snapshot.DurationMs);
        await ExecuteMusicCommandAsync(
            () => _musicPlayerRouter.SeekAsync(target));
    }

    private Task SetMusicPlayerVolumeAsync(int volume)
    {
        if (_updatingMusicPlayerUi ||
            !_musicPlayerRouter.ActivePlayer.SupportsVolume ||
            !_settingsUiLoaded)
        {
            return Task.CompletedTask;
        }

        return ExecuteMusicCommandAsync(
            () => _musicPlayerRouter.SetVolumeAsync(
                Math.Clamp(volume, 0, 100)));
    }

    private bool _updatingMusicPlayerUi;

    private void UpdateMusicTitleMarquees()
    {
        UpdateTextMarquee(
            DashboardTopMusicTitleText,
            DashboardTopMusicTitleViewport,
            DashboardTopMusicTitleTranslate);
    }

    private static void UpdateTextMarquee(
        TextBlock textBlock,
        FrameworkElement viewport,
        TranslateTransform translate)
    {
        translate.BeginAnimation(TranslateTransform.XProperty, null);
        translate.X = 0;

        if (viewport.ActualWidth <= 0 || string.IsNullOrWhiteSpace(textBlock.Text))
        {
            return;
        }

        textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double overflow = textBlock.DesiredSize.Width - viewport.ActualWidth;
        if (overflow <= 2)
        {
            return;
        }

        double pixelsPerSecond = 28.0;
        double scrollSeconds = Math.Max(3.0, overflow / pixelsPerSecond);
        var animation = new DoubleAnimation
        {
            From = 0,
            To = -overflow,
            Duration = TimeSpan.FromSeconds(scrollSeconds),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromSeconds(1.25),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        translate.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    private async Task<MusicBookmarkletDragData>
        GetYouTubeMusicBookmarkletDragDataAsync()
    {
        if (!_youTubeMusicModule.IsBridgeRunning)
        {
            await _musicPlayerRouter.ConnectActiveAsync();
            await RefreshMusicPlayerUiAsync();
        }

        return new MusicBookmarkletDragData(
            await _youTubeMusicModule.GetBookmarkletAsync(),
            _youTubeMusicModule.GetBookmarkletDisplayName());
    }

    private async Task OpenYouTubeMusicBookmarkletInstallPageAsync()
    {
        if (!_youTubeMusicModule.IsBridgeRunning)
        {
            await _musicPlayerRouter.ConnectActiveAsync();
            await RefreshMusicPlayerUiAsync();
        }

        string url =
            await _youTubeMusicModule.GetBookmarkletInstallPageUrlAsync();
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private async Task RefreshMusicPlayerUiAsync()
    {
        ApplyMusicProviderUiState();
        MusicPlayerUiState uiState = await _musicPlayerUiPresenter.GetStateAsync();
        string trackLabel = uiState.TrackLabel;
        var snapshot = new NowPlayingSnapshot(
            uiState.ProviderId,
            uiState.Connected,
            uiState.IsPlaying,
            uiState.Title,
            uiState.Artist,
            uiState.Album,
            uiState.CoverUrl ?? "",
            uiState.PositionMs,
            uiState.DurationMs,
            uiState.VolumePercent,
            uiState.StatusText);

        _updatingMusicPlayerUi = true;
        try
        {
            DashboardTopMusicTitleText.Text = string.IsNullOrWhiteSpace(uiState.Title) ? "Kein Titel" : uiState.Title;
            DashboardTopMusicArtistText.Text = string.IsNullOrWhiteSpace(uiState.Artist) ? "-" : uiState.Artist;
            bool showAlbum = !IsYouTubeMusicProvider();
            DashboardTopMusicAlbumText.Visibility = showAlbum ? Visibility.Visible : Visibility.Collapsed;
            if (showAlbum)
            {
                DashboardTopMusicAlbumText.Text = string.IsNullOrWhiteSpace(uiState.Album) ? "Album: -" : "Album: " + uiState.Album;
            }
            DashboardTopMusicStatusText.Text = uiState.StatusText;
            DashboardTopMusicPlayPauseButton.Content = uiState.IsPlaying ? "Ⅱ" : "▶";
            MusicPlayerPageViewHost.ApplyState(uiState, showAlbum);
            DashboardPageViewHost.DashboardMusicNowPlayingText.Text = trackLabel;
            DashboardPageViewHost.DashboardMusicStatusText.Text = uiState.StatusText;
            _ = Dispatcher.BeginInvoke(UpdateMusicTitleMarquees, System.Windows.Threading.DispatcherPriority.Loaded);

            if (!IsSpotifyMusicProvider())
            {
                SpotifyDashboardStatus.Text = uiState.Connected || _youTubeMusicModule.IsBridgeRunning
                    ? "VERBUNDEN"
                    : "NICHT VERBUNDEN";
                SpotifyDashboardLamp.Fill = uiState.Connected || _youTubeMusicModule.IsBridgeRunning
                    ? System.Windows.Media.Brushes.LimeGreen
                    : System.Windows.Media.Brushes.IndianRed;
            }

            if (uiState.VolumePercent is int volume)
            {
                DashboardTopMusicVolumeSlider.Value = volume;
                DashboardTopMusicVolumeText.Text = $"{volume} %";
            }
        }
        finally
        {
            _updatingMusicPlayerUi = false;
        }

        await LoadMusicCoverAsync(snapshot.CoverUrl);

        if (IsYouTubeMusicProvider())
        {
            try
            {
                MusicPlayerPageViewHost.UpdateBookmarklet(
                    await _youTubeMusicModule.GetBookmarkletAsync(),
                    _youTubeMusicModule.GetBookmarkletDisplayName(),
                    _youTubeMusicModule.IsBridgeRunning
                        ? snapshot.StatusText
                        : "Bridge gestoppt");
            }
            catch
            {
                MusicPlayerPageViewHost.UpdateBookmarklet(
                    "",
                    _youTubeMusicModule.GetBookmarkletDisplayName(),
                    "Bridge gestoppt");
            }
        }

        // Overlay-Now-Playing: YouTube Music über den generischen Writer.
        // Spotify bleibt beim dedizierten WriteSpotifyOverlayRuntimeDataAsync (Mute/Latch).
        if (!IsSpotifyMusicProvider())
        {
            try
            {
                await WriteMusicOverlayRuntimeDataAsync(snapshot);
            }
            catch (Exception exception)
            {
                _appLogger.Write(
                    AppLogLevel.Debug,
                    "Music",
                    "Music-Overlay-Refresh übersprungen: " + exception.Message,
                    exception);
            }
        }

        RefreshDashboardServiceActionButtons();
    }

    private async Task LoadMusicCoverAsync(string? coverUrl)
    {
        if (string.IsNullOrWhiteSpace(coverUrl))
        {
            DashboardTopMusicCoverImage.Source = null;
            MusicPlayerPageViewHost.SetCover(null);
            return;
        }

        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            byte[] bytes = await client.GetByteArrayAsync(coverUrl);
            var image = new System.Windows.Media.Imaging.BitmapImage();
            using var stream = new MemoryStream(bytes);
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            DashboardTopMusicCoverImage.Source = image;
            MusicPlayerPageViewHost.SetCover(image);
        }
        catch
        {
            // Cover optional
        }
    }

    private async Task WriteMusicOverlayRuntimeDataAsync(NowPlayingSnapshot snapshot)
    {
        if (!_settings.Spotify.OverlayEnabled)
        {
            return;
        }

        string targetPath;
        try
        {
            targetPath = ResolveActiveOverlayDataPath();
        }
        catch (InvalidOperationException)
        {
            // Ohne konfigurierten Overlay-Ordner darf der Live-Refresh nicht crashen.
            return;
        }

        await OverlayDataWriteCoordinator.Lock.WaitAsync();
        try
        {
            string? directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

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
            bool connected = snapshot.Connected;
            if (connected)
            {
                _spotifyOverlayConnectionLatched = true;
            }

            string provider = MusicProviderIds.Normalize(snapshot.ProviderId);
            ApplyMusicOverlayFields(
                spotify,
                provider,
                connected,
                snapshot.IsPlaying,
                snapshot.Title,
                snapshot.Artist,
                snapshot.Album,
                snapshot.CoverUrl,
                snapshot.ProgressMs,
                snapshot.DurationMs,
                snapshot.StatusText,
                showInOverlay: connected && _lastSpotifyOverlayMuted != true);

            rootObject["spotify"] = spotify;
            rootObject["music"] = spotify.DeepClone();
            rootObject["updatedAt"] = DateTimeOffset.UtcNow;

            string json = rootObject.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(targetPath, json);

            string trackKey = $"{provider}|{snapshot.Artist}|{snapshot.Title}|{snapshot.CoverUrl}";
            if (!string.Equals(_lastOverlayPublishedSpotifyTrack, trackKey, StringComparison.Ordinal))
            {
                _lastOverlayPublishedSpotifyTrack = trackKey;
                await PublishOverlayRealtimeEventAsync(OverlayEventBridge.AppMusicTrack(
                    provider,
                    snapshot.Title,
                    snapshot.Artist,
                    snapshot.CoverUrl));
            }
        }
        catch (Exception exception)
        {
            _appLogger.Write(
                AppLogLevel.Warning,
                "Music",
                "Music-Overlay-Daten konnten nicht geschrieben werden: " + exception.Message,
                exception);
        }
        finally
        {
            OverlayDataWriteCoordinator.Lock.Release();
        }
    }

    private void ApplyMusicOverlayFields(
        JsonObject target,
        string provider,
        bool connected,
        bool isPlaying,
        string title,
        string artist,
        string album,
        string coverUrl,
        int progressMs,
        int durationMs,
        string statusText,
        bool showInOverlay)
    {
        target["provider"] = provider;
        target["providerDisplayName"] = MusicProviderIds.DisplayName(provider);
        target["connected"] = connected;
        target["isPlaying"] = isPlaying;
        target["title"] = title ?? "";
        target["artist"] = artist ?? "";
        target["album"] = album ?? "";
        target["coverUrl"] = coverUrl ?? "";
        target["cover"] = coverUrl ?? "";
        target["showInOverlay"] = showInOverlay;
        target["visible"] = showInOverlay;
        target["showTitle"] = true;
        target["showArtist"] = true;
        target["showAlbumCover"] = true;
        target["showProgress"] = true;
        target["hideWhenPaused"] = _settings.Spotify.OverlayHideWhenPaused;
        target["hideWhenMuted"] = _settings.Spotify.OverlayHideWhenMuted;
        target["progressMs"] = Math.Max(0, progressMs);
        target["durationMs"] = Math.Max(0, durationMs);
        target["statusText"] = string.IsNullOrWhiteSpace(statusText)
            ? (!connected ? "Nicht verbunden" : isPlaying ? "Spielt" : "Pause")
            : statusText;
    }
}
