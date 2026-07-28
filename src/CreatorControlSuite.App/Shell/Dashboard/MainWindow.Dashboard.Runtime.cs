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
    private async Task RunDashboardPreflightAsync()
    {
        _dashboardPreflightItems.Clear();
        AddDashboardNotification($"Preflight gestartet.", "Info");

        void AddCheck(bool ok, string text)
        {
            _dashboardPreflightItems.Add($"{(ok ? "✓" : "⚠")} {text}");
        }

        AddCheck(_obsClient.IsConnected, "OBS WebSocket verbunden");
        AddCheck(_twitchModule.GetSnapshot().Authenticated, "Twitch verbunden");
        AddCheck(_spotifyModule.GetSnapshot().Authenticated, "Spotify verbunden");
        AddCheck(_streamerBotClient.IsConnected, "Streamer.bot verbunden");
        AddCheck(!string.IsNullOrWhiteSpace(_settings.Obs.StartScene), $"Startszene: {_settings.Obs.StartScene}");
        AddCheck(!string.IsNullOrWhiteSpace(_settings.Obs.LiveScene), $"Live-Szene: {_settings.Obs.LiveScene}");
        AddCheck(!string.IsNullOrWhiteSpace(DashboardPageViewHost.DashboardTwitchTitleBox.Text), "Streamtitel gesetzt");
        AddCheck(DashboardPageViewHost.DashboardTwitchCategoryResultsBox.SelectedItem is not null || !string.IsNullOrWhiteSpace(DashboardPageViewHost.DashboardTwitchCategorySearchBox.Text), "Twitch-Kategorie gewählt oder suchbar");
        AddCheck(!_settings.Workflow.AutoStartSpotifyPlaylist || !string.IsNullOrWhiteSpace(_settings.Spotify.StartPlaylistUri), "Spotify-Startplaylist konfiguriert");
        AddCheck(!_settings.Twitch.RaidOnStreamEnd || !string.IsNullOrWhiteSpace(_settings.Twitch.SelectedRaidChannel), "Raid-Ziel für Streamende gesetzt");

        int warningCount = _dashboardPreflightItems.Count(x => x.StartsWith("⚠", StringComparison.Ordinal));
        DashboardPageViewHost.DashboardWorkflowStageText.Text = warningCount == 0
            ? "BEREIT → START → LIVE → ENDE → RAID"
            : $"VORBEREITEN · {warningCount} Punkt(e) prüfen";

        AddDashboardNotification(
            warningCount == 0
                ? "Preflight erfolgreich: Stream ist bereit."
                : $"Preflight: {warningCount} Punkt(e) benötigen Aufmerksamkeit.",
            warningCount == 0 ? "Info" : "Warnung");

        await Task.CompletedTask;
    }

    private async Task SwitchDashboardConfiguredSceneAsync(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            AddDashboardNotification($"Kein Szenenname konfiguriert.", "Info");
            return;
        }

        await SwitchDashboardSceneByNameAsync(sceneName);
    }


    private async Task ApplyDashboardProfileAndPrepareAsync()
    {
        if (DashboardPageViewHost.DashboardProfileBox.SelectedItem is not ProfileSummary summary)
        {
            AddDashboardNotification($"Kein Stream-Profil ausgewählt.", "Info");
            return;
        }

        try
        {
            DashboardPageViewHost.DashboardWorkflowStageText.Text = $"PROFIL LADEN · {summary.Name}";
            await _profileService.ApplyAsync(summary.Id);
            await LoadSettingsAsync();

            AddDashboardNotification($"Profil „{summary.Name}“ wurde angewendet.", "Info");

            DashboardPageViewHost.DashboardWorkflowStageText.Text = $"PROFIL {summary.Name} · STREAM VORBEREITEN";
            await PrepareStreamAsync();
        }
        catch (Exception exception)
        {
            DashboardPageViewHost.DashboardWorkflowStageText.Text = "PROFIL FEHLER";
            AddDashboardNotification($"Profil konnte nicht angewendet werden: {exception.Message}", "Fehler");
        }
    }

    private sealed class DashboardNotificationEntry
    {
        public DateTimeOffset Timestamp { get; set; }
        public string Severity { get; set; } = "Info";
        public string Message { get; set; } = "";
        public bool IsRead { get; set; }
    }

    private string GetDashboardNotificationFilePath()
    {
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CreatorControlSuite");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "notifications.json");
    }

    private void AddDashboardNotification(string message, string severity = "Info")
    {
        string normalizedSeverity = severity switch
        {
            "Error" => "Fehler",
            "Warning" => "Warnung",
            "Fehler" => "Fehler",
            "Warnung" => "Warnung",
            _ => "Info"
        };

        _dashboardNotifications.Add(new DashboardNotificationEntry
        {
            Timestamp = DateTimeOffset.Now,
            Severity = normalizedSeverity,
            Message = message,
            IsRead = false
        });

        if (_dashboardNotifications.Count > 250)
        {
            _dashboardNotifications.RemoveRange(0, _dashboardNotifications.Count - 250);
        }

        RefreshDashboardNotificationView();
        _ = SaveDashboardNotificationsAsync();
    }

    private void RefreshDashboardNotificationView()
    {
        if (DashboardPageViewHost.DashboardNotificationList is null || DashboardPageViewHost.DashboardNotificationFilterBox is null)
        {
            return;
        }

        string selectedFilter = (DashboardPageViewHost.DashboardNotificationFilterBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString()
            ?? "Alle";

        IEnumerable<DashboardNotificationEntry> query = _dashboardNotifications;
        query = selectedFilter switch
        {
            "Info" => query.Where(item => item.Severity == "Info"),
            "Warnungen" => query.Where(item => item.Severity == "Warnung"),
            "Fehler" => query.Where(item => item.Severity == "Fehler"),
            _ => query
        };

        _dashboardNotificationItems.Clear();
        foreach (DashboardNotificationEntry? item in query.OrderByDescending(item => item.Timestamp).Take(100))
        {
            string icon = item.Severity switch
            {
                "Fehler" => "✕",
                "Warnung" => "⚠",
                _ => "ℹ"
            };
            string unread = item.IsRead ? "" : " •";
            _dashboardNotificationItems.Add(
                $"{icon} {item.Timestamp:HH:mm:ss} · {item.Message}{unread}");
        }

        int unreadCount = _dashboardNotifications.Count(item => !item.IsRead);
        DashboardPageViewHost.DashboardNotificationCountText.Text = unreadCount == 0
            ? $"{_dashboardNotifications.Count} Meldungen"
            : $"{unreadCount} ungelesen";
    }

    private async Task LoadDashboardNotificationsAsync()
    {
        _dashboardNotifications.Clear();
        string path = GetDashboardNotificationFilePath();
        if (File.Exists(path))
        {
            try
            {
                string json = await File.ReadAllTextAsync(path);
                List<DashboardNotificationEntry>? items = System.Text.Json.JsonSerializer.Deserialize<List<DashboardNotificationEntry>>(json);
                if (items is not null)
                {
                    _dashboardNotifications.AddRange(items.TakeLast(250));
                }
            }
            catch
            {
                // A corrupt notification cache must never prevent application startup.
            }
        }

        RefreshDashboardNotificationView();
    }

    private async Task SaveDashboardNotificationsAsync()
    {
        try
        {
            string json = System.Text.Json.JsonSerializer.Serialize(
                _dashboardNotifications.TakeLast(250),
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(GetDashboardNotificationFilePath(), json);
        }
        catch
        {
            // Notifications are non-critical and must not interrupt streaming workflows.
        }
    }
}
