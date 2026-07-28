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
    private async Task AuthorizeTwitchAsync()
    {
        try
        {
            await SaveSettingsAsync();

            SettingsPageViewHost.TwitchConnectionStatusText.Text =
                "Gerätecode wird angefordert ...";

            TwitchDeviceCode deviceCode =
                await _twitchModule.StartAuthorizationAsync();

            Clipboard.SetText(deviceCode.UserCode);

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = deviceCode.VerificationUri,
                    UseShellExecute = true
                });

            MessageBoxResult result = MessageBox.Show(
                "Twitch wurde im Browser geöffnet.\n\n" +
                "Code: " + deviceCode.UserCode + "\n\n" +
                "Der Code wurde in die Zwischenablage kopiert.\n" +
                "Nach der Bestätigung auf Twitch hier auf OK klicken. " +
                "Die Suite wartet danach automatisch auf den Token.",
                "Twitch autorisieren",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.OK)
            {
                SettingsPageViewHost.TwitchConnectionStatusText.Text =
                    "Autorisierung abgebrochen.";

                return;
            }

            var progress = new Progress<string>(
                text => SettingsPageViewHost.TwitchConnectionStatusText.Text = text);

            await _twitchModule.CompleteAuthorizationAsync(
                deviceCode,
                progress);

            RefreshTwitchUi();
        }
        catch (Exception exception)
        {
            SettingsPageViewHost.TwitchConnectionStatusText.Text = exception.Message;
            SettingsPageViewHost.TwitchConnectionStatusText.Foreground =
                System.Windows.Media.Brushes.IndianRed;

            MessageBox.Show(
                exception.Message,
                "Twitch-Autorisierung fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ConnectTwitchAsync(
        bool showErrorDialog = true)
    {
        try
        {
            SettingsPageViewHost.TwitchConnectionStatusText.Text =
                "Twitch wird verbunden ...";

            await _twitchModule.ConnectAsync(CancellationToken.None);

            RefreshTwitchUi();
            await RefreshTwitchUsersAsync(force: true);
            await RefreshLiveViewerSampleAsync();
            await RefreshTwitchFollowerCountAsync();
            await RefreshChatEmoteCatalogFromSettingsAsync();
        }
        catch (Exception exception)
        {
            SettingsPageViewHost.TwitchConnectionStatusText.Text = exception.Message;
            SettingsPageViewHost.TwitchConnectionStatusText.Foreground =
                System.Windows.Media.Brushes.IndianRed;

            if (showErrorDialog)
            {
                MessageBox.Show(
                    exception.Message,
                    "Twitch-Verbindung fehlgeschlagen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private async Task DisconnectTwitchAsync()
    {
        await _twitchModule.DisconnectAsync(CancellationToken.None);

        TwitchDashboardStatus.Text = "NICHT VERBUNDEN";
        SettingsPageViewHost.TwitchConnectionStatusText.Text =
            "Nicht verbunden";
        SettingsPageViewHost.TwitchConnectionStatusText.Foreground =
            System.Windows.Media.Brushes.Gray;

        RefreshDashboardServiceActionButtons();
    }

    private async Task SearchTwitchCategoriesAsync(System.Windows.Controls.TextBox searchBox, System.Windows.Controls.ComboBox resultsBox)
    {
        try
        {
            string query = searchBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                return;
            }

            resultsBox.ItemsSource = await _twitchModule.SearchCategoriesAsync(query);
            resultsBox.IsDropDownOpen = true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Kategoriesuche fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task SaveTwitchChannelAsync(System.Windows.Controls.TextBox titleBox, System.Windows.Controls.ComboBox categoryBox)
    {
        try
        {
            var category = categoryBox.SelectedItem as TwitchCategory;
            await _twitchModule.UpdateChannelAsync(titleBox.Text.Trim(), category?.Id);
            RefreshTwitchUi();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Twitch-Kanal konnte nicht aktualisiert werden", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task OpenTwitchChannelEditorAsync()
    {
        TwitchConnectionSnapshot snapshot = _twitchModule.GetSnapshot();
        var editor = new TwitchChannelEditorWindow(
            snapshot.ChannelTitle,
            snapshot.CategoryName,
            _settings.Twitch.LiveNotificationText,
            query => _twitchModule.SearchCategoriesAsync(query),
            async (title, categoryId, liveNotification) =>
            {
                await _twitchModule.UpdateChannelAsync(title, categoryId);
                _settings.Twitch.LiveNotificationText = liveNotification;
                await _settingsStore.SaveAsync(_settings);
            })
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
        {
            TwitchConnectionSnapshot updated = _twitchModule.GetSnapshot();
            DashboardPageViewHost.DashboardTwitchTitleBox.Text = updated.ChannelTitle;
            DashboardPageViewHost.DashboardTwitchCategorySearchBox.Text = updated.CategoryName;
            RefreshTwitchUi();
            AddDashboardNotification("Twitch-Kanaldaten wurden aktualisiert.", "Info");
        }
    }

    private async Task SearchTwitchCategoriesAsync()
    {
        try
        {
            string query =
                SettingsPageViewHost.TwitchCategorySearchBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(query))
            {
                return;
            }

            SettingsPageViewHost.TwitchCategoryResultsBox.ItemsSource =
                await _twitchModule.SearchCategoriesAsync(query);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Kategoriesuche fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task SaveTwitchChannelAsync()
    {
        try
        {
            var category =
                SettingsPageViewHost.TwitchCategoryResultsBox.SelectedItem
                    as TwitchCategory;

            await _twitchModule.UpdateChannelAsync(
                SettingsPageViewHost.TwitchTitleBox.Text.Trim(),
                category?.Id);

            RefreshTwitchUi();

            MessageBox.Show(
                "Streamtitel und Kategorie wurden gespeichert.",
                "Twitch",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Twitch-Kanal konnte nicht aktualisiert werden",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task RunStartupStepSafelyAsync(string stepName, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            _appLogger.Write(
                AppLogLevel.Error,
                "Startup",
                $"Startschritt '{stepName}' ist fehlgeschlagen. Die Suite wird im eingeschränkten Modus fortgesetzt.",
                exception);
        }
    }

    private void SelectDashboardStatisticInUi()
    {
        string metric = string.IsNullOrWhiteSpace(_settings.Dashboard.DashboardStatistic)
            ? "ViewerCount"
            : _settings.Dashboard.DashboardStatistic;
        _statisticsPageViewModel.LoadMetric(metric);
        UpdateDashboardSelectedStatistic();
    }

    private void UpdateDashboardSelectedStatistic()
    {
        StreamSessionStats stats = _workflowModule.Service.SessionStats;
        string metric = _settings.Dashboard.DashboardStatistic ?? "ViewerCount";
        (DashboardPageViewHost.DashboardSelectedStatisticLabel.Text, DashboardPageViewHost.DashboardSelectedStatisticValue.Text) = metric switch
        {
            "FollowerCount" => ("FOLLOWERZAHL", _currentFollowerCount.ToString()),
            "SubscriberCount" => ("SUB-ANZAHL", _currentActiveSubscriptionCount.ToString()),
            "NewFollowers" => ("NEUE FOLLOWER", stats.FollowersGained.ToString()),
            "NewSubscribers" => ("NEUE SUBS", stats.NewSubscriptions.ToString()),
            _ => ("ZUSCHAUERZAHL", _currentLiveViewerCount.ToString())
        };
    }

    private void UpdateStreamLivePulse(bool isLive)
    {
        StreamDashboardStatus.Foreground = isLive
            ? System.Windows.Media.Brushes.LimeGreen
            : System.Windows.Media.Brushes.IndianRed;
        StreamDashboardStatus.BeginAnimation(UIElement.OpacityProperty, null);
        StreamDashboardStatus.Opacity = 1;
        if (!isLive)
        {
            return;
        }

        var pulse = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 1.0,
            To = 0.35,
            Duration = TimeSpan.FromSeconds(1.2),
            AutoReverse = true,
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
        };
        StreamDashboardStatus.BeginAnimation(UIElement.OpacityProperty, pulse);
    }

    private static void CopySelectedModerationUser(ListBox list, TextBox target)
    {
        if (list.SelectedItem is not null)
        {
            target.Text = list.SelectedItem.ToString()?.TrimStart('@') ?? string.Empty;
        }
    }

    private async Task ModerateTwitchUserAsync(string userName, bool ban, string? durationMinutesText, string? reason)
    {
        string cleanName = (userName ?? string.Empty).Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            MessageBox.Show("Bitte zuerst einen Twitch-User auswählen oder eingeben.", "Twitch-Moderation", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int? durationSeconds = null;
        if (!ban)
        {
            if (!int.TryParse(durationMinutesText, out int minutes) || minutes < 1)
            {
                MessageBox.Show("Bitte eine Timeout-Dauer von mindestens einer Minute eingeben.", "Twitch-Moderation", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            durationSeconds = Math.Clamp(minutes * 60, 1, 1_209_600);
        }

        try
        {
            await _twitchModule.ModerateUserAsync(cleanName, durationSeconds, reason);
            string resultText = ban
                ? $"{cleanName} wurde gebannt."
                : $"{cleanName} erhielt einen Timeout von {durationSeconds / 60} Minuten.";
            AddDashboardNotification(resultText, "Info");
            await AddTwitchModerationLogAsync(ban ? "BAN" : "TIMEOUT", cleanName, reason, resultText);
        }
        catch (Exception exception)
        {
            await AddTwitchModerationLogAsync(ban ? "BAN FEHLER" : "TIMEOUT FEHLER", cleanName, reason, exception.Message);
            MessageBox.Show(exception.Message, "Twitch-Moderation fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task UnbanTwitchUserAsync(string userName)
    {
        string cleanName = (userName ?? string.Empty).Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            MessageBox.Show("Bitte zuerst einen Twitch-User auswählen oder eingeben.", "Twitch-Moderation", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            await _twitchModule.UnbanUserAsync(cleanName);
            string resultText = $"Ban oder Timeout für {cleanName} wurde aufgehoben.";
            AddDashboardNotification(resultText, "Info");
            await AddTwitchModerationLogAsync("AUFHEBEN", cleanName, null, resultText);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Twitch-Moderation fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string GetTwitchModerationLogPath()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CreatorControlSuite", "Logs");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "twitch-moderation.log");
    }

    private async Task AddTwitchModerationLogAsync(string action, string userName, string? reason, string result)
    {
        string line = $"{DateTimeOffset.Now:dd.MM.yyyy HH:mm:ss} · {action} · @{userName}" +
                   (string.IsNullOrWhiteSpace(reason) ? string.Empty : $" · Grund: {reason.Trim()}") +
                   $" · {result}";
        _twitchModerationLogItems.Insert(0, line);
        while (_twitchModerationLogItems.Count > 100)
        {
            _twitchModerationLogItems.RemoveAt(_twitchModerationLogItems.Count - 1);
        }

        await File.AppendAllTextAsync(GetTwitchModerationLogPath(), line + Environment.NewLine, new System.Text.UTF8Encoding(true));
    }

    private async Task ExportTwitchModerationLogAsync()
    {
        string source = GetTwitchModerationLogPath();
        if (!File.Exists(source))
        {
            MessageBox.Show("Es sind noch keine Moderationsaktionen gespeichert.", "Twitch-Moderation", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        string target = Path.Combine(Path.GetDirectoryName(source)!, $"twitch-moderation-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        await Task.Run(() => File.Copy(source, target, true));
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    private async Task SendTwitchChatAsync()
    {
        string message = SettingsPageViewHost.TwitchChatMessageBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            await _twitchModule.SendChatMessageAsync(message);

            SettingsPageViewHost.TwitchChatMessageBox.Clear();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Chatnachricht konnte nicht gesendet werden",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ScrollTwitchChatToLatest()
    {
        if (_twitchChatItems.Count == 0)
        {
            return;
        }

        string latest = _twitchChatItems[^1];
        SettingsPageViewHost.TwitchChatList.ScrollIntoView(latest);
        DashboardPageViewHost.DashboardTwitchChatList.ScrollIntoView(latest);
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchChatList.ScrollIntoView(latest);
    }

    private async Task LoadTwitchProfessionalHistoryAsync()
    {
        _twitchProfessionalHistoryItems.Clear();
        string path = GetStreamHistoryFilePath();
        if (!File.Exists(path))
        {
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalTotalStreamsText.Text = "0";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalRecordPeakText.Text = "0";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalRecordAverageText.Text = "0,0";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalTotalDurationText.Text = "00:00";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalTotalFollowersText.Text = "0";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalPeakTrendText.Text = "-";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalAverageTrendText.Text = "-";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalChatRateText.Text = "0";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalBestCategoryText.Text = "-";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalEngagementRateText.Text = "0";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalFollowerRateText.Text = "0";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalConsistencyText.Text = "-";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalSummaryText.Text = "Noch keine Trenddaten verfügbar.";
            _twitchProfessionalHistoryItems.Add("Noch keine abgeschlossenen Streams gespeichert.");
            return;
        }

        var rows = new List<(DateTimeOffset StartedAt, long DurationSeconds, int Peak, double Average, int Followers, int Chat, int Events, string Category, string Title)>();
        foreach (string line in await File.ReadAllLinesAsync(path))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                rows.Add((
                    root.GetProperty("StartedAt").GetDateTimeOffset(),
                    root.TryGetProperty("DurationSeconds", out JsonElement duration) ? duration.GetInt64() : 0,
                    root.TryGetProperty("PeakViewers", out JsonElement peak) ? peak.GetInt32() : 0,
                    root.TryGetProperty("AverageViewers", out JsonElement average) ? average.GetDouble() : 0,
                    root.TryGetProperty("FollowersGained", out JsonElement followers) ? followers.GetInt32() : 0,
                    root.TryGetProperty("ChatMessages", out JsonElement chat) ? chat.GetInt32() : 0,
                    root.TryGetProperty("AlertsPlayed", out JsonElement eventsCount) ? eventsCount.GetInt32() : 0,
                    root.TryGetProperty("Category", out JsonElement category) ? category.GetString() ?? "-" : "-",
                    root.TryGetProperty("Title", out JsonElement title) ? title.GetString() ?? "-" : "-"));
            }
            catch
            {
                // Ungültige oder ältere Zeilen werden übersprungen.
            }
        }

        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalTotalStreamsText.Text = rows.Count.ToString();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalRecordPeakText.Text = rows.Count == 0 ? "0" : rows.Max(x => x.Peak).ToString();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalRecordAverageText.Text = rows.Count == 0 ? "0,0" : rows.Max(x => x.Average).ToString("0.0");
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalTotalDurationText.Text =
            StreamStatisticsApplicationService.FormatDuration(
                rows.Sum(x => x.DurationSeconds));
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalTotalFollowersText.Text = rows.Sum(x => x.Followers).ToString();

        var recent = rows.OrderBy(x => x.StartedAt).TakeLast(10).ToList();
        if (recent.Count >= 2)
        {
            int split = Math.Max(1, recent.Count / 2);
            double earlier = recent.Take(split).Average(x => x.Average);
            double later = recent.Skip(split).Average(x => x.Average);
            double delta = later - earlier;
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalViewerTrendText.Text = $"Zuschauertrend: {(delta >= 0 ? "+" : string.Empty)}{delta:0.0} Ø Zuschauer";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalFollowerTrendText.Text = $"Followertrend: {recent.Average(x => x.Followers):0.0} pro Stream";
        }
        else
        {
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalViewerTrendText.Text = "Zuschauertrend: Noch nicht genügend Daten";
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalFollowerTrendText.Text = "Followertrend: Noch nicht genügend Daten";
        }
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalCategoryTrendText.Text = "Häufigste Kategorie: " + (rows.Where(x => !string.IsNullOrWhiteSpace(x.Category) && x.Category != "-").GroupBy(x => x.Category).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key ?? "-");
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalDurationTrendText.Text =
            "Ø Streamdauer: " +
            StreamStatisticsApplicationService.FormatDuration(
                rows.Count == 0
                    ? 0
                    : (long)rows.Average(x => x.DurationSeconds));

        var ordered = rows.OrderByDescending(x => x.StartedAt).ToList();
        var latestFive = ordered.Take(5).ToList();
        var previousFive = ordered.Skip(5).Take(5).ToList();
        static string PercentTrend(double current, double previous) => previous <= 0 ? "-" : $"{(current - previous) / previous * 100:+0.0;-0.0;0.0}%";
        double latestPeak = latestFive.Count == 0 ? 0 : latestFive.Average(x => x.Peak);
        double previousPeak = previousFive.Count == 0 ? 0 : previousFive.Average(x => x.Peak);
        double latestAverage = latestFive.Count == 0 ? 0 : latestFive.Average(x => x.Average);
        double previousAverage = previousFive.Count == 0 ? 0 : previousFive.Average(x => x.Average);
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalPeakTrendText.Text = PercentTrend(latestPeak, previousPeak);
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalAverageTrendText.Text = PercentTrend(latestAverage, previousAverage);
        double totalHours = rows.Sum(x => x.DurationSeconds) / 3600d;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalChatRateText.Text = totalHours <= 0 ? "0" : (rows.Sum(x => x.Chat) / totalHours).ToString("0.0");
        var bestCategory = rows.Where(x => !string.IsNullOrWhiteSpace(x.Category) && x.Category != "-")
            .GroupBy(x => x.Category).Select(g => new { Name = g.Key, Average = g.Average(x => x.Average) })
            .OrderByDescending(x => x.Average).FirstOrDefault();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalBestCategoryText.Text = bestCategory?.Name ?? "-";
        int totalEngagement = rows.Sum(x => x.Chat + x.Events);
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalEngagementRateText.Text = totalHours <= 0 ? "0" : (totalEngagement / totalHours).ToString("0.0");
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalFollowerRateText.Text = totalHours <= 0 ? "0" : (rows.Sum(x => x.Followers) / totalHours).ToString("0.00");
        var recentAverages = latestFive.Select(x => x.Average).ToList();
        if (recentAverages.Count < 2 || recentAverages.Average() <= 0)
        {
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalConsistencyText.Text = "-";
        }
        else
        {
            double mean = recentAverages.Average();
            double variance = recentAverages.Sum(value => Math.Pow(value - mean, 2)) / recentAverages.Count;
            double coefficient = Math.Sqrt(variance) / mean;
            ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalConsistencyText.Text = coefficient switch
            {
                <= 0.15 => "Sehr stabil",
                <= 0.30 => "Stabil",
                <= 0.50 => "Schwankend",
                _ => "Stark schwankend"
            };
        }
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalSummaryText.Text = rows.Count == 0 ? "Noch keine Trenddaten verfügbar." :
            $"Letzte {latestFive.Count} Streams: Ø {latestAverage:0.0} Zuschauer, mittlerer Peak {latestPeak:0.0}. Insgesamt {rows.Sum(x => x.Chat)} Chatnachrichten und {rows.Sum(x => x.Followers)} neue Follower.";

        foreach ((DateTimeOffset StartedAt, long DurationSeconds, int Peak, double Average, int Followers, int Chat, int Events, string Category, string Title) row in ordered.Take(20))
        {
            DateTimeOffset local = row.StartedAt.ToLocalTime();
            var duration = TimeSpan.FromSeconds(Math.Max(0, row.DurationSeconds));
            _twitchProfessionalHistoryItems.Add(
                $"{local:dd.MM.yyyy HH:mm} · {duration:hh\\:mm\\:ss} · Peak {row.Peak} · Ø {row.Average:0.0} · +{row.Followers} Follower · {row.Category}");
        }

        if (_twitchProfessionalHistoryItems.Count == 0)
        {
            _twitchProfessionalHistoryItems.Add("Noch keine gültigen Stream-Sessions vorhanden.");
        }
    }

    private void RefreshTwitchProfessionalUi(TwitchRaidTargetStatus? liveStatus = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => RefreshTwitchProfessionalUi(liveStatus));
            return;
        }

        TwitchConnectionSnapshot snapshot = _twitchModule.GetSnapshot();
        StreamSessionStats stats = _workflowModule.Service.SessionStats;
        bool live = liveStatus is not null
            ? liveStatus.IsOnline
            : _twitchStreamStartedAt.HasValue || _lastObsStreamActive;
        DateTimeOffset? startedAt = liveStatus?.StartedAt
            ?? ResolveLiveStreamStartedAt();
        TimeSpan duration = startedAt.HasValue
            ? DateTimeOffset.Now - startedAt.Value
            : TimeSpan.Zero;

        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalLiveText.Text = live ? "LIVE" : "OFFLINE";
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalLiveText.Foreground = live
            ? System.Windows.Media.Brushes.LightGreen
            : System.Windows.Media.Brushes.Gray;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalViewerText.Text = _currentLiveViewerCount.ToString();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalPeakText.Text = stats.PeakViewers.ToString();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalAverageText.Text = stats.AverageViewers.ToString("0.0");
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalDurationText.Text = duration.ToString(@"hh\:mm\:ss");
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalChatText.Text = _twitchSessionChatMessages.ToString();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalUniqueChattersText.Text = _twitchSessionUniqueChatters.Count.ToString();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalEventsText.Text = _twitchSessionEvents.ToString();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalFollowersText.Text = stats.FollowersGained.ToString();
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalCategoryText.Text = string.IsNullOrWhiteSpace(liveStatus?.GameName)
            ? (string.IsNullOrWhiteSpace(snapshot.CategoryName) ? "-" : snapshot.CategoryName)
            : liveStatus.GameName;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchProfessionalTitleText.Text = string.IsNullOrWhiteSpace(liveStatus?.StreamTitle)
            ? (string.IsNullOrWhiteSpace(snapshot.ChannelTitle) ? "-" : snapshot.ChannelTitle)
            : liveStatus.StreamTitle;
    }

    private void RefreshTwitchUi()
    {
        TwitchConnectionSnapshot snapshot = _twitchModule.GetSnapshot();

        TwitchDashboardStatus.Text = snapshot.Authenticated
            ? "VERBUNDEN"
            : "NICHT VERBUNDEN";

        TwitchDashboardLamp.Fill = snapshot.Authenticated
            ? System.Windows.Media.Brushes.LimeGreen
            : System.Windows.Media.Brushes.IndianRed;

        SettingsPageViewHost.TwitchConnectionStatusText.Text = snapshot.Authenticated
            ? $"Verbunden als {snapshot.Login} · " +
              $"EventSub: {(snapshot.EventSubConnected ? "aktiv" : "offline")}"
            : "Nicht verbunden";

        SettingsPageViewHost.TwitchConnectionStatusText.Foreground = snapshot.Authenticated
            ? System.Windows.Media.Brushes.LightGreen
            : System.Windows.Media.Brushes.Gray;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchStatusText.Text = SettingsPageViewHost.TwitchConnectionStatusText.Text;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchStatusText.Foreground = SettingsPageViewHost.TwitchConnectionStatusText.Foreground;

        SettingsPageViewHost.TwitchTitleBox.Text = snapshot.ChannelTitle;
        SettingsPageViewHost.TwitchCategorySearchBox.Text = snapshot.CategoryName;
        DashboardPageViewHost.DashboardTwitchTitleBox.Text = snapshot.ChannelTitle;
        DashboardPageViewHost.DashboardTwitchCategorySearchBox.Text = snapshot.CategoryName;
        DashboardPageViewHost.DashboardTwitchChannelTitleText.Text = string.IsNullOrWhiteSpace(snapshot.ChannelTitle)
            ? "Kein Streamtitel gesetzt"
            : snapshot.ChannelTitle;
        string notification = string.IsNullOrWhiteSpace(_settings.Twitch.LiveNotificationText)
            ? "Live-Benachrichtigung nicht gesetzt"
            : _settings.Twitch.LiveNotificationText;
        DashboardPageViewHost.DashboardTwitchChannelDetailsText.Text =
            $"{(string.IsNullOrWhiteSpace(snapshot.CategoryName) ? "Keine Kategorie" : snapshot.CategoryName)} · {notification}";
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchTitleBox.Text = snapshot.ChannelTitle;
        ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchCategorySearchBox.Text = snapshot.CategoryName;
        RefreshDashboardServiceActionButtons();
        _ = RefreshTwitchWebChatViewsAsync(forceReload: false);
    }

    private static string GetTwitchRoleLabel(
        TwitchChatMessage message)
    {
        if (string.Equals(
                message.ChatterUserId,
                message.BroadcasterUserId,
                StringComparison.Ordinal))
        {
            return "[STREAMER] ";
        }

        if (message.Badges.Any(
                badge =>
                    string.Equals(
                        badge.SetId,
                        "moderator",
                        StringComparison.OrdinalIgnoreCase)))
        {
            return "[MOD] ";
        }

        if (message.Badges.Any(
                badge =>
                    string.Equals(
                        badge.SetId,
                        "vip",
                        StringComparison.OrdinalIgnoreCase)))
        {
            return "[VIP] ";
        }

        if (message.Badges.Any(
                badge =>
                    string.Equals(
                        badge.SetId,
                        "subscriber",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        badge.SetId,
                        "founder",
                        StringComparison.OrdinalIgnoreCase)))
        {
            return "[SUB] ";
        }

        return "";
    }

    private void UpdateDashboardTwitchUser(
        TwitchChatMessage message,
        string role)
    {
        string userId = string.IsNullOrWhiteSpace(message.ChatterUserId)
            ? message.ChatterLogin
            : message.ChatterUserId;
        string userName = string.IsNullOrWhiteSpace(message.ChatterName)
            ? message.ChatterLogin
            : message.ChatterName;

        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(userName))
        {
            return;
        }

        string display = role + userName;

        if (_twitchUserDisplayById.TryGetValue(userId, out string? previous))
        {
            int index = _twitchUserItems.IndexOf(previous);

            if (index >= 0)
            {
                _twitchUserItems[index] = display;
            }
        }
        else if (!_twitchUserItems.Any(item =>
                     string.Equals(
                         GetTwitchUserNameFromDisplay(item),
                         userName,
                         StringComparison.OrdinalIgnoreCase)))
        {
            _twitchUserItems.Add(display);
        }

        _twitchUserDisplayById[userId] = display;

        while (_twitchUserItems.Count > 1000)
        {
            _twitchUserItems.RemoveAt(0);
        }
    }

    private static string GetTwitchUserNameFromDisplay(string display)
    {
        foreach (string? prefix in new[]
                 {
                     "[STREAMER] ",
                     "[MOD] ",
                     "[VIP] ",
                     "[SUB] "
                 })
        {
            if (display.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return display[prefix.Length..];
            }
        }

        return display;
    }

    private static int GetTwitchEventCount(TwitchEvent twitchEvent)
    {
        static int Parse(
            IReadOnlyDictionary<string, string> data,
            params string[] keys)
        {
            foreach (string key in keys)
            {
                if (data.TryGetValue(key, out string? value) &&
                    int.TryParse(value, out int parsed))
                {
                    return Math.Max(1, parsed);
                }
            }

            return 1;
        }

        return twitchEvent.Type switch
        {
            "channel.subscription.gift" =>
                Parse(twitchEvent.Data, "total", "count", "amount"),
            "channel.cheer" =>
                Parse(twitchEvent.Data, "bits"),
            _ => 1
        };
    }

    private static void AddLimitedItem(
        ObservableCollection<string> collection,
        string value,
        int limit)
    {
        collection.Add(value);

        while (collection.Count > limit)
        {
            collection.RemoveAt(0);
        }
    }
}
