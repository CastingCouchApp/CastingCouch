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
    private void ApplyTwitchEndFieldsToSettings()
    {
        _settings.Twitch.RaidOnStreamEnd = ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidEnabledBox.IsChecked == true;
        _settings.Twitch.RaidCountdownSeconds = int.TryParse(ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidCountdownSecondsBox.Text, out int raidSeconds)
            ? Math.Clamp(raidSeconds, 5, 300)
            : 90;
        _settings.Twitch.RaidStartTimeoutSeconds = int.TryParse(
                ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidStartTimeoutSecondsBox.Text,
                out int raidStartTimeout)
            ? RaidStartPolicy.ClampTimeoutSeconds(raidStartTimeout)
            : RaidStartPolicy.DefaultTimeoutSeconds;
        _settings.Twitch.StopStreamAfterRaid = ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchStopStreamAfterRaidBox.IsChecked != false;
        _settings.Twitch.StopSpotifyAfterRaid = ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchStopSpotifyAfterRaidBox.IsChecked != false;
        _settings.Twitch.RaidChannels = [.. ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidChannelsBox.Text
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        if (ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchRaidTargetBox.SelectedItem is string raidTarget)
        {
            _settings.Twitch.SelectedRaidChannel = raidTarget;
        }
        else if (!_settings.Twitch.RaidChannels.Contains(_settings.Twitch.SelectedRaidChannel, StringComparer.OrdinalIgnoreCase))
        {
            _settings.Twitch.SelectedRaidChannel = _settings.Twitch.RaidChannels.FirstOrDefault() ?? "";
        }
        _settings.Workflow.EndSceneSeconds = int.TryParse(ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchEndSceneSecondsBox.Text, out int seconds) ? Math.Max(0, seconds) : 60;
        _settings.Twitch.EndSceneDurationSeconds = _settings.Workflow.EndSceneSeconds;

        // Die Endszene verwendet dasselbe zentrale Follower-Ziel wie das Goal-Overlay.
        // Den ViewModel-Wert hier nicht zurücksetzen: Er wird direkt danach gespeichert.
    }

    private async Task RefreshTwitchRewardsAsync()
    {
        try
        {
            IReadOnlyList<TwitchChannelPointReward> rewards = await _twitchModule.GetCustomRewardsAsync();
            ServicesPageViewHost.TwitchServiceViewHost.ServicesRewardsList.ItemsSource = rewards.Select(reward => $"{reward.Title} · {reward.Cost:N0} Punkte").ToList();
        }
        catch (Exception exception)
        {
            ServicesPageViewHost.TwitchServiceViewHost.ServicesRewardsList.ItemsSource = new[] { "Fehler: " + exception.Message };
        }
    }

    private async Task CreateTwitchRewardAsync()
    {
        try
        {
            string title = ServicesPageViewHost.TwitchServiceViewHost.ServicesRewardTitleBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                throw new InvalidOperationException("Bitte einen Titel für die Belohnung eingeben.");
            }

            if (!int.TryParse(ServicesPageViewHost.TwitchServiceViewHost.ServicesRewardCostBox.Text, out int cost) || cost < 1)
            {
                throw new InvalidOperationException("Die Punktekosten müssen mindestens 1 betragen.");
            }

            TwitchChannelPointReward reward = await _twitchModule.CreateCustomRewardAsync(title, cost, ServicesPageViewHost.TwitchServiceViewHost.ServicesRewardPromptBox.Text);
            ServicesPageViewHost.TwitchServiceViewHost.ServicesRewardTitleBox.Clear();
            ServicesPageViewHost.TwitchServiceViewHost.ServicesRewardPromptBox.Clear();
            await RefreshTwitchRewardsAsync();
            ServicesPageViewHost.TwitchServiceViewHost.ServicesRewardsList.ToolTip = $"Belohnung '{reward.Title}' wurde erstellt.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Twitch Channel Points", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task CreateTwitchPollAsync()
    {
        try
        {
            var choices = ServicesPageViewHost.TwitchServiceViewHost.ServicesPollChoicesBox.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (string.IsNullOrWhiteSpace(ServicesPageViewHost.TwitchServiceViewHost.ServicesPollTitleBox.Text))
            {
                throw new InvalidOperationException("Bitte eine Umfragefrage eingeben.");
            }

            if (choices.Count < 2 || choices.Count > 5)
            {
                throw new InvalidOperationException("Eine Umfrage benötigt zwei bis fünf Antworten.");
            }

            int duration = int.TryParse(ServicesPageViewHost.TwitchServiceViewHost.ServicesPollDurationBox.Text, out int parsed) ? Math.Clamp(parsed, 15, 1800) : 60;
            TwitchPoll poll = await _twitchModule.CreatePollAsync(ServicesPageViewHost.TwitchServiceViewHost.ServicesPollTitleBox.Text, choices, duration);
            _activeTwitchPollId = poll.Id;
            ServicesPageViewHost.TwitchServiceViewHost.ServicesPollStatusText.Text = $"Aktiv: {poll.Title} · {duration} Sekunden";
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Twitch-Umfrage", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task EndTwitchPollAsync(string status)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_activeTwitchPollId))
            {
                throw new InvalidOperationException("Es ist keine in dieser Sitzung gestartete Umfrage vorhanden.");
            }

            TwitchPoll poll = await _twitchModule.EndPollAsync(_activeTwitchPollId, status);
            ServicesPageViewHost.TwitchServiceViewHost.ServicesPollStatusText.Text = $"{poll.Status}: {poll.Title}";
            if (!status.Equals("TERMINATED", StringComparison.OrdinalIgnoreCase))
            {
                _activeTwitchPollId = null;
            }
        }
        catch (Exception ex) { ShowError("Umfrage konnte nicht aktualisiert werden", ex); }
    }

    private async Task EndTwitchPredictionAsync(string status)
    {
        try
        {
            if (_activeTwitchPrediction is null)
            {
                throw new InvalidOperationException("Es ist keine in dieser Sitzung gestartete Vorhersage vorhanden.");
            }

            _activeTwitchPrediction = await _twitchModule.EndPredictionAsync(_activeTwitchPrediction.Id, status, null);
            ServicesPageViewHost.TwitchServiceViewHost.ServicesPredictionWinnerBox.ItemsSource = _activeTwitchPrediction.Outcomes;
            ServicesPageViewHost.TwitchServiceViewHost.ServicesPredictionStatusText.Text = $"{_activeTwitchPrediction.Status}: {_activeTwitchPrediction.Title}";
        }
        catch (Exception ex) { ShowError("Vorhersage konnte nicht aktualisiert werden", ex); }
    }

    private async Task ResolveTwitchPredictionAsync()
    {
        try
        {
            if (_activeTwitchPrediction is null)
            {
                throw new InvalidOperationException("Es ist keine in dieser Sitzung gestartete Vorhersage vorhanden.");
            }

            if (ServicesPageViewHost.TwitchServiceViewHost.ServicesPredictionWinnerBox.SelectedItem is not TwitchPredictionOutcome winner)
            {
                throw new InvalidOperationException("Bitte das Gewinnergebnis auswählen.");
            }

            _activeTwitchPrediction = await _twitchModule.EndPredictionAsync(_activeTwitchPrediction.Id, "RESOLVED", winner.Id);
            ServicesPageViewHost.TwitchServiceViewHost.ServicesPredictionStatusText.Text = $"Aufgelöst: {winner.Title}";
        }
        catch (Exception ex) { ShowError("Vorhersage konnte nicht aufgelöst werden", ex); }
    }

    private async Task RefreshTwitchRedemptionsAsync()
    {
        try
        {
            if (ServicesPageViewHost.TwitchServiceViewHost.ServicesRewardsList.SelectedItem is not TwitchChannelPointReward reward)
            {
                throw new InvalidOperationException("Bitte zuerst eine Channel-Point-Belohnung auswählen.");
            }

            IReadOnlyList<TwitchRewardRedemption> redemptions = await _twitchModule.GetRewardRedemptionsAsync(reward.Id);
            _twitchRedemptionItems.Clear();
            foreach (TwitchRewardRedemption redemption in redemptions)
            {
                _twitchRedemptionItems.Add(new TwitchRewardRedemptionItem(redemption));
            }
        }
        catch (Exception ex) { ShowError("Einlösungen konnten nicht geladen werden", ex); }
    }

    private async Task UpdateSelectedTwitchRedemptionAsync(string status)
    {
        try
        {
            if (ServicesPageViewHost.TwitchServiceViewHost.ServicesRedemptionsList.SelectedItem is not TwitchRewardRedemptionItem selected)
            {
                throw new InvalidOperationException("Bitte eine offene Einlösung auswählen.");
            }

            await _twitchModule.UpdateRewardRedemptionStatusAsync(selected.Redemption.RewardId, selected.Redemption.Id, status);
            _twitchRedemptionItems.Remove(selected);
        }
        catch (Exception ex) { ShowError("Einlösung konnte nicht aktualisiert werden", ex); }
    }

    private async Task CreateTwitchPredictionAsync()
    {
        try
        {
            var outcomes = ServicesPageViewHost.TwitchServiceViewHost.ServicesPredictionOutcomesBox.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (string.IsNullOrWhiteSpace(ServicesPageViewHost.TwitchServiceViewHost.ServicesPredictionTitleBox.Text))
            {
                throw new InvalidOperationException("Bitte eine Vorhersagefrage eingeben.");
            }

            if (outcomes.Count < 2 || outcomes.Count > 10)
            {
                throw new InvalidOperationException("Eine Vorhersage benötigt zwei bis zehn Ergebnisse.");
            }

            int window = int.TryParse(ServicesPageViewHost.TwitchServiceViewHost.ServicesPredictionWindowBox.Text, out int parsed) ? Math.Clamp(parsed, 30, 1800) : 120;
            TwitchPrediction prediction = await _twitchModule.CreatePredictionAsync(ServicesPageViewHost.TwitchServiceViewHost.ServicesPredictionTitleBox.Text, outcomes, window);
            _activeTwitchPrediction = prediction;
            ServicesPageViewHost.TwitchServiceViewHost.ServicesPredictionWinnerBox.ItemsSource = prediction.Outcomes;
            ServicesPageViewHost.TwitchServiceViewHost.ServicesPredictionStatusText.Text = $"Aktiv: {prediction.Title} · {window} Sekunden";
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Twitch-Vorhersage", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task SaveTwitchEndSettingsAsync()
    {
        ApplyTwitchEndFieldsToSettings();
        RefreshRaidChannelSelectors();
        DashboardPageViewHost.DashboardRaidEnabledBox.IsChecked = _settings.Twitch.RaidOnStreamEnd;
        UpdateDashboardRaidControlsVisibility();

        // Speichert das Follower-Ziel und schreibt es zugleich in die aktive overlay-data.json.
        await SaveTwitchGoalsAsync();
        await SaveTwitchChannelAsync(
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchTitleBox,
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchCategoryResultsBox);
    }

    private async Task SaveTwitchGoalsAsync()
    {
        _twitchGoalsPageViewModel.ApplyTo(
            _settings.Obs,
            _settings.Twitch);
        await _settingsStore.SaveAsync(_settings);
        await _overlayModule.Service.UpdateAsync(data =>
        {
            data.Twitch.FollowerGoalState = ToOverlayGoal(_settings.Twitch.FollowerGoal);
            data.Twitch.SubGoalState = ToOverlayGoal(_settings.Twitch.SubGoal);
            data.Twitch.DonationGoalState = ToOverlayGoal(_settings.Twitch.DonationGoal);
        });
        await UpdateActiveOverlayJsonAsync(root =>
        {
            JsonObject twitch = root["twitch"] as JsonObject ?? [];
            twitch["followers"] = _currentFollowerCount;
            twitch["followerGoal"] = _settings.Twitch.FollowerGoal.Target;
            JsonObject followerGoal = twitch["followerGoalState"] as JsonObject ?? [];
            followerGoal["title"] = _settings.Twitch.FollowerGoal.Title;
            followerGoal["current"] = _currentFollowerCount > 0 ? _currentFollowerCount : _settings.Twitch.FollowerGoal.Current;
            followerGoal["target"] = _settings.Twitch.FollowerGoal.Target;
            followerGoal["fontFace"] = _settings.Twitch.FollowerGoal.FontFace;
            followerGoal["fontSize"] = _settings.Twitch.FollowerGoal.FontSize;
            twitch["followerGoalState"] = followerGoal;
            JsonObject subGoal = twitch["subGoalState"] as JsonObject ?? [];
            subGoal["title"] = _settings.Twitch.SubGoal.Title;
            subGoal["current"] = _settings.Twitch.SubGoal.Current;
            subGoal["target"] = _settings.Twitch.SubGoal.Target;
            subGoal["fontFace"] = _settings.Twitch.SubGoal.FontFace;
            subGoal["fontSize"] = _settings.Twitch.SubGoal.FontSize;
            twitch["subGoalState"] = subGoal;
            JsonObject donationGoal = twitch["donationGoalState"] as JsonObject ?? [];
            donationGoal["title"] = _settings.Twitch.DonationGoal.Title;
            donationGoal["reason"] = _settings.Twitch.DonationGoal.Reason;
            donationGoal["current"] = _settings.Twitch.DonationGoal.Current;
            donationGoal["target"] = _settings.Twitch.DonationGoal.Target;
            donationGoal["currency"] = _settings.Twitch.DonationGoal.Currency;
            donationGoal["fontFace"] = _settings.Twitch.DonationGoal.FontFace;
            donationGoal["fontSize"] = _settings.Twitch.DonationGoal.FontSize;
            twitch["donationGoalState"] = donationGoal;
            root["twitch"] = twitch;
        });
        await SynchronizeGoalBarsAsync();
    }

    private async Task SynchronizeGoalBarsAsync()
    {
        var follower = new OverlayGoalPreset(
            _settings.Twitch.FollowerGoal.Title,
            _settings.Twitch.FollowerGoal.Target);
        var subscriptions = new OverlayGoalPreset(
            _settings.Twitch.SubGoal.Title,
            _settings.Twitch.SubGoal.Target);
        string donationLabel = string.IsNullOrWhiteSpace(_settings.Twitch.DonationGoal.Reason)
            ? _settings.Twitch.DonationGoal.Title
            : $"{_settings.Twitch.DonationGoal.Title} · {_settings.Twitch.DonationGoal.Reason}";
        var donations = new OverlayGoalPreset(
            donationLabel,
            _settings.Twitch.DonationGoal.Target);

        foreach (string instanceId in _overlayModule.LayoutStore.ListInstanceIds())
        {
            OverlayLayout layout = await _overlayModule.LayoutStore.LoadAsync(instanceId);
            if (!OverlayGoalLayoutUpdater.Apply(
                    layout,
                    follower,
                    subscriptions,
                    donations))
            {
                continue;
            }

            await _overlayModule.LayoutStore.SaveAsync(instanceId, layout);
            await PublishOverlayRealtimeEventAsync(
                OverlayEventBridge.AppOverlayLayout(instanceId, layout));
        }
    }

    private static CreatorControlSuite.Modules.Overlay.Models.OverlayGoalState ToOverlayGoal(TwitchGoalSettings goal) => new()
    {
        Title = goal.Title,
        Reason = goal.Reason,
        Current = goal.Current,
        Target = goal.Target,
        FontFace = goal.FontFace,
        FontSize = goal.FontSize,
        Currency = goal.Currency
    };

    private void NormalizeTwitchChattersRefreshSettings()
    {
        _settings.Twitch ??= new TwitchSettings();
        _settings.Twitch.ChattersRefreshSecondsLow = Math.Clamp(
            _settings.Twitch.ChattersRefreshSecondsLow <= 0 ? 10 : _settings.Twitch.ChattersRefreshSecondsLow,
            5,
            120);
        _settings.Twitch.ChattersRefreshSecondsHigh = Math.Clamp(
            _settings.Twitch.ChattersRefreshSecondsHigh <= 0 ? 60 : _settings.Twitch.ChattersRefreshSecondsHigh,
            15,
            600);
        _settings.Twitch.ChattersRefreshViewerThreshold = Math.Clamp(
            _settings.Twitch.ChattersRefreshViewerThreshold <= 0 ? 50 : _settings.Twitch.ChattersRefreshViewerThreshold,
            1,
            10000);

        if (_settings.Twitch.ChattersRefreshSecondsHigh < _settings.Twitch.ChattersRefreshSecondsLow)
        {
            _settings.Twitch.ChattersRefreshSecondsHigh = _settings.Twitch.ChattersRefreshSecondsLow;
        }
    }

    private void ApplyTwitchUsersRefreshInterval()
    {
        NormalizeTwitchChattersRefreshSettings();
        int seconds = _currentLiveViewerCount >= _settings.Twitch.ChattersRefreshViewerThreshold
            ? _settings.Twitch.ChattersRefreshSecondsHigh
            : _settings.Twitch.ChattersRefreshSecondsLow;
        TimeSpan interval = TimeSpan.FromSeconds(seconds);
        if (_twitchUsersRefreshTimer.Interval != interval)
        {
            _twitchUsersRefreshTimer.Interval = interval;
        }
    }

    private async Task RefreshTwitchUsersAsync(bool force = false)
    {
        if (_twitchUsersRefreshRunning)
        {
            return;
        }

        NormalizeTwitchChattersRefreshSettings();
        int requiredSeconds = _currentLiveViewerCount >= _settings.Twitch.ChattersRefreshViewerThreshold
            ? _settings.Twitch.ChattersRefreshSecondsHigh
            : _settings.Twitch.ChattersRefreshSecondsLow;

        if (!force &&
            _lastTwitchUsersRefreshUtc != DateTimeOffset.MinValue &&
            DateTimeOffset.UtcNow - _lastTwitchUsersRefreshUtc < TimeSpan.FromSeconds(requiredSeconds))
        {
            return;
        }

        _twitchUsersRefreshRunning = true;

        try
        {
            IReadOnlyList<string> users = await _twitchModule.GetChattersAsync();
            await Dispatcher.InvokeAsync(() =>
            {
                var merged = users
                    .Where(user => !string.IsNullOrWhiteSpace(user))
                    .Concat(_twitchUserDisplayById.Values)
                    .GroupBy(
                        GetTwitchUserNameFromDisplay,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group =>
                        group.FirstOrDefault(item => item.StartsWith("[", StringComparison.Ordinal))
                        ?? group.First())
                    .OrderBy(
                        GetTwitchUserNameFromDisplay,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();

                _twitchUserItems.Clear();
                foreach (string? user in merged)
                {
                    _twitchUserItems.Add(user);
                }

                DashboardPageViewHost.DashboardTwitchUsersHeaderText.Text =
                    $"TWITCH · USER ({_twitchUserItems.Count})";
                ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchUsersHeaderText.Text =
                    $"User ({_twitchUserItems.Count})";
                RefreshCommunityUi();
            });
            _lastTwitchUsersRefreshUtc = DateTimeOffset.UtcNow;
        }
        catch (Exception exception)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                DashboardPageViewHost.DashboardTwitchUsersHeaderText.Text =
                    $"TWITCH · USER ({_twitchUserItems.Count})";
                ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchUsersHeaderText.Text =
                    $"User ({_twitchUserItems.Count}) · Aktualisierung fehlgeschlagen";
                ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchUsersHeaderText.ToolTip = exception.Message;
            });

            // Die User-Liste ist optional. Chat und EventSub laufen bei einem
            // vorübergehenden API- oder Berechtigungsfehler weiter.
        }
        finally
        {
            _twitchUsersRefreshRunning = false;
        }
    }
}
