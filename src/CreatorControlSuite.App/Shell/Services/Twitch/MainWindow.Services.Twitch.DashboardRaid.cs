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
        => TwitchDashboardApplicationService.FormatRaidLiveDuration(duration);

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
        List<string> channels =
        [
            .. TwitchDashboardApplicationService.NormalizeRaidChannels(
                _settings.Twitch.RaidChannels)
        ];

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
        if (string.IsNullOrWhiteSpace(channel.Trim().TrimStart('@')))
        {
            return;
        }

        IReadOnlyList<string> channels =
            TwitchDashboardApplicationService.RememberRaidChannel(
                _settings.Twitch.RaidChannels,
                channel);

        _settings.Twitch.RaidChannels = [.. channels];
        _settings.Twitch.SelectedRaidChannel = channels[0];
        RefreshRaidChannelSelectors();
    }

    private async Task<IReadOnlyList<TwitchChannelSuggestion>> SuggestRaidTargetsAsync(
        string query,
        CancellationToken cancellationToken)
    {
        query = query.Trim().TrimStart('@');

        IReadOnlyList<string> recentLogins =
            TwitchDashboardApplicationService.NormalizeRaidChannels(
                _settings.Twitch.RaidChannels)
            .Where(channel =>
                TwitchDashboardApplicationService.MatchesRaidQuery(
                    channel,
                    channel,
                    query))
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
            IReadOnlyList<string> loginsToCheck =
                TwitchDashboardApplicationService.BuildRaidStatusProbeLogins(
                    recentLogins,
                    followed,
                    query);
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

        return TwitchDashboardApplicationService.BuildRaidSuggestions(
            recentLogins,
            followed,
            followedLive,
            searched,
            liveByLogin,
            query);
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
        RaidActionState state =
            TwitchDashboardApplicationService.ProjectRaidActions(
                !string.IsNullOrWhiteSpace(
                    _settings.Twitch.SelectedRaidChannel),
                _raidTargetIsOnline,
                _raidCountdownActive,
                _awaitingManualRaid,
                _streamEndFlowActive);
        DashboardPageViewHost.DashboardStartRaidButton.IsEnabled =
            state.ShowStartRaid;
        DashboardPageViewHost.DashboardStartRaidButton.Visibility =
            state.ShowStartRaid ? Visibility.Visible : Visibility.Collapsed;
        DashboardPageViewHost.DashboardCancelRaidButton.IsEnabled =
            state.CanCancelRaid;

        _activeStreamEndDialog?.SetRaidReady(state.ShowStartRaid);
        _activeStreamEndDialog?.SetCancelRaidEnabled(state.CanCancelRaid);
        if (state.ShowManualRaidActions)
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
