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
    private void OpenDashboardTwitchChat()
    {
        string? channel = ResolveTwitchChatChannel();
        if (string.IsNullOrWhiteSpace(channel))
        {
            AddDashboardNotification(
                "Kein Twitch-Kanal für den Chat konfiguriert.",
                "Warnung");
            return;
        }

        OpenConfiguredTarget(
            TwitchWebViewProfile.BuildPopoutChatUrl(channel),
            "Twitch Chat");
    }

    private string? ResolveTwitchChatChannel()
    {
        TwitchConnectionSnapshot twitchSnapshot = _twitchModule.GetSnapshot();
        string channel = twitchSnapshot.ChannelLogin;

        if (string.IsNullOrWhiteSpace(channel))
        {
            channel = !string.IsNullOrWhiteSpace(twitchSnapshot.ChannelName)
                ? twitchSnapshot.ChannelName
                : _settings.Twitch.ChannelName;
        }

        return string.IsNullOrWhiteSpace(channel) ? null : channel.Trim();
    }

    private async Task OnTwitchChatUiModeChangedAsync()
    {
        if (_loadingSettingsIntoUi)
        {
            return;
        }

        _settings.Twitch.ChatUiMode = SettingsPageViewHost.TwitchChatUiEmbeddedWebRadio.IsChecked == true
            ? TwitchChatUiMode.EmbeddedWeb
            : TwitchChatUiMode.BuiltIn;
        await ApplyTwitchChatUiModeAsync();
    }

    private void OpenTwitchWebLoginWindow()
    {
        var window = new TwitchWebLoginWindow
        {
            Owner = this,
        };
        window.ShowDialog();
        _ = RefreshTwitchWebChatViewsAsync(forceReload: true);
    }

    private async Task ApplyTwitchChatUiModeAsync()
    {
        bool web = _settings.Twitch.ChatUiMode == TwitchChatUiMode.EmbeddedWeb;

        DashboardPageViewHost.DashboardTwitchChatList.Visibility = web ? Visibility.Collapsed : Visibility.Visible;
        DashboardPageViewHost.DashboardTwitchWebChat.Visibility = web ? Visibility.Visible : Visibility.Collapsed;
        DashboardPageViewHost.DashboardTwitchChatHeader.Visibility = web ? Visibility.Collapsed : Visibility.Visible;
        DashboardPageViewHost.DashboardTwitchChatControls.Visibility = web ? Visibility.Collapsed : Visibility.Visible;
        DashboardPageViewHost.DashboardTwitchChatHeaderRow.Height = web ? new GridLength(0) : GridLength.Auto;
        DashboardPageViewHost.DashboardTwitchChatControlsRow.Height = web ? new GridLength(0) : GridLength.Auto;
        DashboardPageViewHost.DashboardTwitchChatContentHost.Margin = web ? new Thickness(0) : new Thickness(0, 8, 0, 8);
        DashboardPageViewHost.DashboardTwitchChatModule.Padding = web ? new Thickness(0) : new Thickness(10);

        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchChatList.Visibility = web ? Visibility.Collapsed : Visibility.Visible;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchWebChat.Visibility = web ? Visibility.Visible : Visibility.Collapsed;

        SettingsPageViewHost.TwitchBuiltInChatPanel.Visibility = web ? Visibility.Collapsed : Visibility.Visible;
        SettingsPageViewHost.TwitchWebChatSettingsHint.Visibility = web ? Visibility.Visible : Visibility.Collapsed;

        if (!web)
        {
            return;
        }

        await RefreshTwitchWebChatViewsAsync(forceReload: false);
    }

    private async Task RefreshTwitchWebChatViewsAsync(bool forceReload)
    {
        if (_settings.Twitch.ChatUiMode != TwitchChatUiMode.EmbeddedWeb)
        {
            return;
        }

        string? channel = ResolveTwitchChatChannel();
        if (string.IsNullOrWhiteSpace(channel))
        {
            return;
        }

        string url = TwitchWebViewProfile.BuildPopoutChatUrl(channel);
        if (!forceReload &&
            string.Equals(_lastTwitchWebChatUrl, url, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            await EnsureAndNavigateTwitchWebChatAsync(DashboardPageViewHost.DashboardTwitchWebChat, url, forceReload);
            await EnsureAndNavigateTwitchWebChatAsync(ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchWebChat, url, forceReload);
            _lastTwitchWebChatUrl = url;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            AddDashboardNotification(
                "WebView2 Runtime fehlt. Bitte Evergreen Runtime installieren oder den Systembrowser nutzen.",
                "Warnung");
            MessageBoxResult result = MessageBox.Show(
                this,
                "Die Microsoft Edge WebView2 Runtime ist nicht installiert.\n\nInstaller jetzt im Browser öffnen?",
                "WebView2 Runtime fehlt",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                OpenConfiguredTarget(TwitchWebViewProfile.RuntimeInstallerUrl, "WebView2 Runtime");
            }
        }
        catch (Exception ex)
        {
            AddDashboardNotification("Web-Chat konnte nicht geladen werden: " + ex.Message, "Warnung");
        }
    }

    private static async Task EnsureAndNavigateTwitchWebChatAsync(WebView2 webView, string url, bool forceReload = false)
    {
        await TwitchWebViewProfile.EnsureAsync(webView);
        if (!forceReload &&
            webView.Source?.AbsoluteUri is string current &&
            string.Equals(current, url, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (forceReload && webView.CoreWebView2 is not null)
        {
            webView.CoreWebView2.Navigate(url);
            return;
        }

        webView.Source = new Uri(url);
    }


    private async Task RefreshTwitchGoalsAsync()
    {
        if (!_twitchModule.GetSnapshot().Authenticated)
        {
            _currentActiveSubscriptionCount = 0;
            DashboardChatAlertsText.Text = "0";
            return;
        }

        try
        {
            Task<int> followerTask = _twitchModule.GetFollowerCountAsync();
            Task<int> subscriptionTask = _twitchModule.GetActiveSubscriptionCountAsync();

            await Task.WhenAll(followerTask, subscriptionTask);

            _currentFollowerCount = Math.Max(0, followerTask.Result);
            _currentActiveSubscriptionCount =
                Math.Max(0, subscriptionTask.Result);

            _settings.Twitch.FollowerGoal.Current =
                _currentFollowerCount;
            _settings.Twitch.SubGoal.Current =
                _currentActiveSubscriptionCount;

            _twitchGoalsPageViewModel.UpdateLiveCounts(
                _currentFollowerCount,
                _currentActiveSubscriptionCount);

            DashboardFollowerTotalText.Text =
                $"Gesamt: {_currentFollowerCount}";
            DashboardHeroFollowerText.Text = _currentFollowerCount.ToString();
            DashboardChatAlertsText.Text = _currentActiveSubscriptionCount.ToString();
            DashboardTwitchGoalsText.Text =
                $"Follower-Ziel: {_currentFollowerCount:0} / {_settings.Twitch.FollowerGoal.Target:0} · " +
                $"Sub-Ziel: {_currentActiveSubscriptionCount:0} / {_settings.Twitch.SubGoal.Target:0}";

            await _overlayModule.Service.UpdateAsync(data =>
            {
                data.Twitch.Followers = _currentFollowerCount;
                data.Twitch.FollowerGoalState.Current =
                    _currentFollowerCount;
                data.Twitch.FollowerGoalState.Target =
                    _settings.Twitch.FollowerGoal.Target;
                data.Twitch.FollowerGoalState.Title =
                    _settings.Twitch.FollowerGoal.Title;
                data.Twitch.FollowerGoalState.FontFace =
                    _settings.Twitch.FollowerGoal.FontFace;
                data.Twitch.FollowerGoalState.FontSize =
                    _settings.Twitch.FollowerGoal.FontSize;

                data.Twitch.SubGoalState.Current =
                    _currentActiveSubscriptionCount;
                data.Twitch.SubGoalState.Target =
                    _settings.Twitch.SubGoal.Target;
                data.Twitch.SubGoalState.Title =
                    _settings.Twitch.SubGoal.Title;
                data.Twitch.SubGoalState.FontFace =
                    _settings.Twitch.SubGoal.FontFace;
                data.Twitch.SubGoalState.FontSize =
                    _settings.Twitch.SubGoal.FontSize;
            });
        }
        catch (Exception exception)
        {
            _appLogger.Write(
                AppLogLevel.Warning,
                "Twitch",
                "Twitch-Ziele konnten nicht automatisch aktualisiert werden.",
                exception);
        }
    }

    private async Task RefreshTwitchFollowerCountAsync(
        bool initializeStreamBaseline = false)
    {
        if (!_twitchModule.GetSnapshot().Authenticated)
        {
            return;
        }

        try
        {
            int followerCount =
                await _twitchModule.GetFollowerCountAsync();

            _currentFollowerCount = Math.Max(0, followerCount);

            if (initializeStreamBaseline)
            {
                _streamFollowerBaseline = _currentFollowerCount;
                _twitchSessionChatMessages = 0;
                _twitchSessionEvents = 0;
                _twitchSessionUniqueChatters.Clear();
                _twitchSessionObservedAt = DateTimeOffset.Now;
                RefreshTwitchProfessionalUi();
            }

            int baseline = _streamFollowerBaseline > 0
                ? _streamFollowerBaseline
                : _currentFollowerCount;

            await _workflowModule.Service.SetFollowerCountsAsync(
                baseline,
                _currentFollowerCount);

            DashboardFollowerTotalText.Text =
                $"Gesamt: {_currentFollowerCount}";
            DashboardFollowersGainedText.Text =
                Math.Max(
                        0,
                        _currentFollowerCount - baseline)
                    .ToString();

            await _overlayModule.Service.UpdateAsync(data =>
            {
                data.Twitch.Followers = _currentFollowerCount;
            });
            await UpdateActiveOverlayJsonAsync(root =>
            {
                JsonObject twitch = root["twitch"] as JsonObject ?? [];
                twitch["followers"] = _currentFollowerCount;
                twitch["followerGoal"] = _settings.Twitch.FollowerGoal.Target;
                JsonObject goal = twitch["followerGoalState"] as JsonObject ?? [];
                goal["title"] = _settings.Twitch.FollowerGoal.Title;
                goal["current"] = _currentFollowerCount;
                goal["target"] = _settings.Twitch.FollowerGoal.Target;
                goal["fontFace"] = _settings.Twitch.FollowerGoal.FontFace;
                goal["fontSize"] = _settings.Twitch.FollowerGoal.FontSize;
                twitch["followerGoalState"] = goal;
                root["twitch"] = twitch;
            });
            DashboardTwitchGoalsText.Text =
                $"Follower-Ziel: {_currentFollowerCount:0} / {_settings.Twitch.FollowerGoal.Target:0} · " +
                $"Sub-Ziel: {_currentActiveSubscriptionCount:0} / {_settings.Twitch.SubGoal.Target:0}";
        }
        catch (Exception exception)
        {
            _appLogger.Write(
                AppLogLevel.Warning,
                "Twitch",
                "Followerzahl konnte nicht aktualisiert werden.",
                exception);
        }
    }

    private async Task RefreshLiveViewerSampleAsync()
    {
        if (_liveViewerSampleRunning)
        {
            return;
        }

        TwitchConnectionSnapshot twitchSnapshot = _twitchModule.GetSnapshot();
        if (!twitchSnapshot.Authenticated)
        {
            _twitchStreamStartedAt = null;
            _currentLiveViewerCount = 0;
            DashboardHeroViewerText.Text = "0";
            AddDashboardViewerTrendSample(0);
            RefreshTwitchProfessionalUi();
            ApplyTwitchUsersRefreshInterval();
            return;
        }

        // Helix' `login` parameter expects the canonical login, not the
        // user-facing display name (which may contain different casing or
        // localized characters).
        string channel = !string.IsNullOrWhiteSpace(twitchSnapshot.ChannelLogin)
            ? twitchSnapshot.ChannelLogin
            : twitchSnapshot.Login;

        if (string.IsNullOrWhiteSpace(channel))
        {
            return;
        }

        _liveViewerSampleRunning = true;

        try
        {
            TwitchRaidTargetStatus? status = await _twitchModule.GetRaidTargetStatusAsync(channel);

            if (status is null || !status.IsOnline)
            {
                _twitchStreamStartedAt = null;
                _currentLiveViewerCount = 0;
                DashboardHeroViewerText.Text = "0";
                AddDashboardViewerTrendSample(0);
                RefreshTwitchProfessionalUi();
                RefreshWorkflowUi(_workflowModule.Service.State);
                ApplyTwitchUsersRefreshInterval();
                return;
            }

            // Helix streams.started_at ist die Zuschauer-sichtbare Live-Dauer auf Twitch.
            // Lokale OBS-/Workflow-Zeiten dienen nur als Fallback, bis Twitch den Stream meldet.
            ApplyTwitchLiveStreamStartedAt(status.StartedAt);

            _currentLiveViewerCount = Math.Max(0, status.ViewerCount);
            ApplyTwitchUsersRefreshInterval();
            await _creatorIntelligence.RecordAsync("twitch.viewer.sample", new { viewers = _currentLiveViewerCount, scene = _servicesObsCurrentScene, category = status.GameName, title = status.StreamTitle });
            RefreshTwitchProfessionalUi(status);

            await _workflowModule.Service.AddViewerSampleAsync(
                _currentLiveViewerCount);

            RefreshWorkflowUi(_workflowModule.Service.State);

            await _overlayModule.Service.UpdateAsync(data =>
            {
                data.Stream.ViewerCount = _currentLiveViewerCount;
            });
            DateTimeOffset? liveStartedAt = ResolveLiveStreamStartedAt();
            await UpdateActiveOverlayJsonAsync(root =>
            {
                JsonObject stream = root["stream"] as JsonObject ?? [];
                stream["viewerCount"] = _currentLiveViewerCount;
                stream["isLive"] = true;
                stream["startedAt"] = liveStartedAt;
                stream["elapsedSeconds"] = liveStartedAt.HasValue
                    ? Math.Max(0, (long)(DateTimeOffset.Now - liveStartedAt.Value).TotalSeconds)
                    : 0;
                root["stream"] = stream;
            });
        }
        catch (Exception exception)
        {
            _appLogger.Write(
                AppLogLevel.Warning,
                "Twitch",
                "Aktuelle Zuschauerzahl konnte nicht aktualisiert werden.",
                exception);
        }
        finally
        {
            _liveViewerSampleRunning = false;
        }
    }

    private async Task RefreshRaidTargetStatusAsync(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            _raidTargetIsOnline = false;
            SetRaidTargetStatusText("Kein Ziel ausgewählt");
            DashboardPageViewHost.DashboardRaidAssistantText.Text = "Kein Raid-Ziel ausgewählt.";
            DashboardPageViewHost.DashboardRaidLiveDurationText.Text = "Live-Dauer: -";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidLiveDurationText.Text = "Live-Dauer: -";
            DashboardPageViewHost.DashboardRaidProfileImage.Source = null;
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidProfileImage.Source = null;
            UpdateDashboardRaidActionButtons();
            return;
        }

        try
        {
            TwitchRaidTargetStatus? status = await _twitchModule.GetRaidTargetStatusAsync(channel.Trim());
            if (status is null)
            {
                _raidTargetIsOnline = false;
                SetRaidTargetStatusText($"{channel}: Kanal nicht gefunden");
                UpdateDashboardRaidActionButtons();
                return;
            }

            _raidTargetIsOnline = status.IsOnline;
            TimeSpan liveDuration = status.IsOnline && status.StartedAt is not null
                ? DateTimeOffset.Now - status.StartedAt.Value
                : TimeSpan.Zero;

            string text = status.IsOnline
                ? $"{status.DisplayName} ist ONLINE · {status.ViewerCount} Zuschauer · {status.GameName}" +
                  (string.IsNullOrWhiteSpace(status.StreamTitle) ? "" : $" · {status.StreamTitle}")
                : $"{status.DisplayName} ist OFFLINE";

            SetRaidTargetStatusText(text);
            DashboardPageViewHost.DashboardRaidAssistantText.Text = text;
            DashboardPageViewHost.DashboardRaidLiveDurationText.Text = status.IsOnline
                ? $"Live seit {FormatRaidLiveDuration(liveDuration)}"
                : "Live-Dauer: -";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidLiveDurationText.Text = DashboardPageViewHost.DashboardRaidLiveDurationText.Text;
            await LoadRaidProfileImageAsync(status.ProfileImageUrl);
            UpdateDashboardRaidActionButtons();
        }
        catch (Exception ex)
        {
            _raidTargetIsOnline = false;
            SetRaidTargetStatusText($"Status nicht verfügbar: {ex.Message}");
            UpdateDashboardRaidActionButtons();
        }
    }

    private async Task LoadRaidProfileImageAsync(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            DashboardPageViewHost.DashboardRaidProfileImage.Source = null;
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidProfileImage.Source = null;
            return;
        }

        try
        {
            byte[] bytes = await RaidProfileHttpClient.GetByteArrayAsync(imageUrl);
            await Dispatcher.InvokeAsync(() =>
            {
                using var stream = new MemoryStream(bytes);
                var image = new System.Windows.Media.Imaging.BitmapImage();
                image.BeginInit();
                image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                DashboardPageViewHost.DashboardRaidProfileImage.Source = image;
                ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidProfileImage.Source = image;
            });
        }
        catch
        {
            DashboardPageViewHost.DashboardRaidProfileImage.Source = null;
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidProfileImage.Source = null;
        }
    }

    private static string FormatRaidLiveDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}:{duration.Minutes:00} Std.";
        }

        return $"{Math.Max(0, duration.Minutes)} Min.";
    }

    private void SetRaidTargetStatusText(string text)
    {
        DashboardPageViewHost.DashboardRaidTargetStatusText.Text = text;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidTargetStatusText.Text = text;
        DashboardPageViewHost.DashboardStreamEndTargetText.Text = text;
        _activeStreamEndDialog?.SetRaidTargetStatus(text);
    }

    private void OpenSelectedRaidChannel()
    {
        string channel = DashboardPageViewHost.DashboardRaidChannelBox.SelectedItem as string
                      ?? ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidTargetBox.SelectedItem as string
                      ?? _settings.Twitch.SelectedRaidChannel;

        if (string.IsNullOrWhiteSpace(channel))
        {
            MessageBox.Show("Bitte zuerst ein Raid-Ziel auswählen.", "Twitch");
            return;
        }

        string url = "https://www.twitch.tv/" + Uri.EscapeDataString(channel.Trim().TrimStart('@'));
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void RefreshRaidChannelSelectors()
    {
        var channels = _settings.Twitch.RaidChannels
            .Select(channel => channel.Trim().TrimStart('@'))
            .Where(channel => !string.IsNullOrWhiteSpace(channel))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _settings.Twitch.RaidChannels = channels;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidChannelsBox.Text = string.Join(Environment.NewLine, channels);
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidChannelsList.ItemsSource = channels;
        DashboardPageViewHost.DashboardRaidChannelBox.ItemsSource = channels;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidTargetBox.ItemsSource = channels;
        DashboardPageViewHost.DashboardRaidChannelBox.SelectedItem = channels.FirstOrDefault(channel => string.Equals(channel, _settings.Twitch.SelectedRaidChannel, StringComparison.OrdinalIgnoreCase));
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidTargetBox.SelectedItem = DashboardPageViewHost.DashboardRaidChannelBox.SelectedItem;
    }

    private void RememberRaidChannel(string channel)
    {
        channel = channel.Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(channel))
        {
            return;
        }

        _settings.Twitch.RaidChannels.RemoveAll(item =>
            string.Equals(item, channel, StringComparison.OrdinalIgnoreCase));
        _settings.Twitch.RaidChannels.Insert(0, channel);
        if (_settings.Twitch.RaidChannels.Count > 40)
        {
            _settings.Twitch.RaidChannels.RemoveRange(40, _settings.Twitch.RaidChannels.Count - 40);
        }

        _settings.Twitch.SelectedRaidChannel = channel;
        RefreshRaidChannelSelectors();
    }

    private async Task<IReadOnlyList<TwitchChannelSuggestion>> SuggestRaidTargetsAsync(
        string query,
        CancellationToken cancellationToken)
    {
        query = query.Trim().TrimStart('@');

        var recentLogins = _settings.Twitch.RaidChannels
            .Select(channel => channel.Trim().TrimStart('@'))
            .Where(channel => !string.IsNullOrWhiteSpace(channel))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(channel => MatchesRaidQuery(channel, channel, query))
            .ToList();

        IReadOnlyList<TwitchChannelSuggestion> followed = [];
        IReadOnlyList<TwitchChannelSuggestion> followedLive = [];
        IReadOnlyList<TwitchChannelSuggestion> searched = [];
        var liveByLogin = new Dictionary<string, TwitchChannelSuggestion>(StringComparer.OrdinalIgnoreCase);

        try
        {
            followed = await GetFollowedRaidTargetsCachedAsync(cancellationToken);
        }
        catch
        {
            // Scope fehlt oder Twitch offline – lokale Vorschläge reichen.
        }

        try
        {
            followedLive = await GetFollowedLiveRaidTargetsCachedAsync(cancellationToken);
        }
        catch
        {
            // Optional
        }

        if (query.Length >= 2)
        {
            try
            {
                searched = await _twitchModule.SearchChannelsAsync(query, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Suche optional
            }
        }

        try
        {
            var loginsToCheck = recentLogins
                .Concat(followed
                    .Where(item => MatchesRaidQuery(item.Login, item.DisplayName, query))
                    .Select(item => item.Login)
                    .Take(80))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (loginsToCheck.Count > 0)
            {
                foreach (KeyValuePair<string, TwitchChannelSuggestion> pair in await _twitchModule.GetLiveChannelsByLoginsAsync(loginsToCheck, cancellationToken))
                {
                    liveByLogin[pair.Key] = pair.Value;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Live-Status optional
        }

        foreach (TwitchChannelSuggestion? live in followedLive.Concat(searched.Where(item => item.IsLive)))
        {
            liveByLogin.TryAdd(live.Login, live with { IsLive = true, SourceLabel = "Live" });
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<TwitchChannelSuggestion>(25);

        // 1) Bereits geraidet – ganz oben; darin Live vor Offline
        var recentSuggestions = recentLogins
            .Select(login =>
            {
                bool isLive = liveByLogin.TryGetValue(login, out TwitchChannelSuggestion? liveInfo);
                string display = isLive ? liveInfo!.DisplayName : login;
                return new TwitchChannelSuggestion(login, display, isLive, "Zuletzt");
            })
            .OrderByDescending(item => item.IsLive)
            .ToList();

        foreach (TwitchChannelSuggestion? item in recentSuggestions)
        {
            if (!seen.Add(item.Login))
            {
                continue;
            }

            results.Add(item);
        }

        // 2) Weitere Live-Kanäle (Follows + Suche)
        foreach (TwitchChannelSuggestion? item in followedLive
                     .Concat(searched.Where(x => x.IsLive))
                     .Concat(liveByLogin.Values)
                     .Where(item => MatchesRaidQuery(item.Login, item.DisplayName, query)))
        {
            if (!seen.Add(item.Login))
            {
                continue;
            }

            results.Add(item with { IsLive = true, SourceLabel = "Live" });
            if (results.Count >= 25)
            {
                return results;
            }
        }

        // 3) Offline: gefolgte, dann Suche
        foreach (TwitchChannelSuggestion? item in followed
                     .Where(item => MatchesRaidQuery(item.Login, item.DisplayName, query))
                     .Concat(searched.Where(item => !item.IsLive)))
        {
            if (!seen.Add(item.Login))
            {
                continue;
            }

            results.Add(item with { IsLive = false });
            if (results.Count >= 25)
            {
                break;
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<TwitchChannelSuggestion>> GetFollowedRaidTargetsCachedAsync(
        CancellationToken cancellationToken)
    {
        if (_followedRaidTargetCache is not null &&
            DateTimeOffset.UtcNow - _followedRaidTargetCacheAt < TimeSpan.FromMinutes(10))
        {
            return _followedRaidTargetCache;
        }

        IReadOnlyList<TwitchChannelSuggestion> followed = await _twitchModule.GetFollowedChannelsAsync(cancellationToken);
        _followedRaidTargetCache = followed;
        _followedRaidTargetCacheAt = DateTimeOffset.UtcNow;
        return followed;
    }

    private async Task<IReadOnlyList<TwitchChannelSuggestion>> GetFollowedLiveRaidTargetsCachedAsync(
        CancellationToken cancellationToken)
    {
        if (_followedLiveRaidTargetCache is not null &&
            DateTimeOffset.UtcNow - _followedLiveRaidTargetCacheAt < TimeSpan.FromMinutes(2))
        {
            return _followedLiveRaidTargetCache;
        }

        IReadOnlyList<TwitchChannelSuggestion> live = await _twitchModule.GetFollowedLiveStreamsAsync(cancellationToken);
        _followedLiveRaidTargetCache = live;
        _followedLiveRaidTargetCacheAt = DateTimeOffset.UtcNow;
        return live;
    }

    private static bool MatchesRaidQuery(string login, string displayName, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return login.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               displayName.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void OnStreamEndRaidTargetChanged(string channel)
    {
        _raidTargetSuggestStatusCts?.Cancel();
        _raidTargetSuggestStatusCts?.Dispose();
        _raidTargetSuggestStatusCts = new CancellationTokenSource();
        CancellationToken token = _raidTargetSuggestStatusCts.Token;
        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await Task.Delay(350, token);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                await RefreshRaidTargetStatusAsync(channel);
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private void UpdateDashboardRaidControlsVisibility()
    {
        Visibility visibility = _settings.Twitch.RaidOnStreamEnd
            ? Visibility.Visible
            : Visibility.Collapsed;
        DashboardPageViewHost.DashboardRaidSelectionPanel.Visibility = visibility;
        DashboardPageViewHost.DashboardOpenRaidChannelButton.Visibility = visibility;
        UpdateDashboardStreamEndModuleVisibility();
        UpdateDashboardRaidActionButtons();
    }

    private void UpdateDashboardStreamEndModuleVisibility()
    {
        DashboardPageViewHost.DashboardStreamEndModule.Visibility = Visibility.Collapsed;
        DashboardPageViewHost.DashboardPlanStreamEndButton.Visibility = _plannedStreamEndActive ? Visibility.Collapsed : Visibility.Visible;
        DashboardPageViewHost.DashboardCancelPlannedStreamEndButton.Visibility = _plannedStreamEndActive ? Visibility.Visible : Visibility.Collapsed;
        DashboardPageViewHost.DashboardSkipRaidAndStopButton.Visibility = _awaitingManualRaid || _streamEndFlowActive
            ? Visibility.Visible
            : Visibility.Collapsed;

        _activeStreamEndDialog?.ShowRaidActions(_awaitingManualRaid && !_raidCountdownActive);
    }

    private void UpdateDashboardRaidActionButtons()
    {
        bool hasTarget = !string.IsNullOrWhiteSpace(_settings.Twitch.SelectedRaidChannel);
        bool canSkipCountdown = _raidCountdownActive;
        bool raidReady = hasTarget && _raidTargetIsOnline && !_raidCountdownActive
            && (_awaitingManualRaid || _streamEndFlowActive);
        bool showJetztRaiden = raidReady || canSkipCountdown;
        DashboardPageViewHost.DashboardStartRaidButton.IsEnabled = showJetztRaiden;
        DashboardPageViewHost.DashboardStartRaidButton.Visibility = showJetztRaiden ? Visibility.Visible : Visibility.Collapsed;
        DashboardPageViewHost.DashboardCancelRaidButton.IsEnabled = _raidCountdownActive;

        _activeStreamEndDialog?.SetRaidReady(showJetztRaiden);
        _activeStreamEndDialog?.SetCancelRaidEnabled(_raidCountdownActive);
        if (_awaitingManualRaid && !_raidCountdownActive)
        {
            _activeStreamEndDialog?.ShowRaidActions(true);
        }
    }

    private async Task AddRaidChannelAsync()
    {
        string channel = ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchNewRaidChannelBox.Text.Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(channel))
        {
            return;
        }

        RememberRaidChannel(channel);
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchNewRaidChannelBox.Clear();
        await _settingsStore.SaveAsync(_settings);
        await RefreshRaidTargetStatusAsync(channel);
    }

    private async Task RemoveSelectedRaidChannelAsync()
    {
        if (ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidChannelsList.SelectedItem is not string channel)
        {
            return;
        }

        _settings.Twitch.RaidChannels.RemoveAll(item => string.Equals(item, channel, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(_settings.Twitch.SelectedRaidChannel, channel, StringComparison.OrdinalIgnoreCase))
        {
            _settings.Twitch.SelectedRaidChannel = _settings.Twitch.RaidChannels.FirstOrDefault() ?? "";
        }

        RefreshRaidChannelSelectors();
        await _settingsStore.SaveAsync(_settings);
        await RefreshRaidTargetStatusAsync(_settings.Twitch.SelectedRaidChannel);
    }
}
