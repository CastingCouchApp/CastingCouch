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
    private string GetStreamHistoryDirectory()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CreatorControlSuite",
            "StreamHistory");
        Directory.CreateDirectory(root);
        return root;
    }

    private string GetStreamHistoryFilePath() =>
        Path.Combine(GetStreamHistoryDirectory(), "history.jsonl");

    private async Task SaveCurrentStreamHistoryAsync()
    {
        StreamSessionStats stats = _workflowModule.Service.SessionStats;
        DateTimeOffset endedAt = DateTimeOffset.Now;
        DateTimeOffset startedAt = ResolveLiveStreamStartedAt() ?? endedAt;
        var item = new
        {
            StartedAt = startedAt,
            EndedAt = endedAt,
            DurationSeconds = Math.Max(0, (long)(endedAt - startedAt).TotalSeconds),
            stats.PeakViewers,
            stats.AverageViewers,
            stats.FollowersGained,
            stats.ChatMessages,
            stats.AlertsPlayed,
            stats.NewSubscriptions,
            stats.GiftSubscriptions,
            stats.BitsCheered,
            stats.IncomingRaids,
            RaidEnabled = _settings.Twitch.RaidOnStreamEnd,
            RaidTarget = _settings.Twitch.SelectedRaidChannel,
            Category = DashboardPageViewHost.DashboardTwitchCategoryResultsBox.SelectedItem?.ToString() ?? DashboardPageViewHost.DashboardTwitchCategorySearchBox.Text,
            Title = DashboardPageViewHost.DashboardTwitchTitleBox.Text
        };

        string line = System.Text.Json.JsonSerializer.Serialize(item);
        await File.AppendAllTextAsync(GetStreamHistoryFilePath(), line + Environment.NewLine);
        await LoadTwitchProfessionalHistoryAsync();
        await _creatorIntelligence.CompleteSessionAsync(endedAt);
        await RefreshCreatorIntelligenceAsync();
        _streamSessionStartedAt = null;
        _twitchStreamStartedAt = null;
    }

    private async Task LoadStreamHistoryAsync()
    {
        _streamHistoryItems.Clear();
        string path = GetStreamHistoryFilePath();
        if (!File.Exists(path))
        {
            _streamHistoryItems.Add("Noch keine abgeschlossenen Streams gespeichert.");
            return;
        }

        string[] lines = await File.ReadAllLinesAsync(path);
        foreach (string? line in lines.Reverse().Take(50))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(line);
                JsonElement root = doc.RootElement;
                DateTimeOffset started = root.GetProperty("StartedAt").GetDateTimeOffset().ToLocalTime();
                var duration = TimeSpan.FromSeconds(root.GetProperty("DurationSeconds").GetInt64());
                int peak = root.GetProperty("PeakViewers").GetInt32();
                double avg = root.GetProperty("AverageViewers").GetDouble();
                int followers = root.GetProperty("FollowersGained").GetInt32();
                _streamHistoryItems.Add(
                    $"{started:dd.MM.yyyy HH:mm} · {duration:hh\\:mm\\:ss} · Peak {peak} · Ø {avg:0.0} · +{followers} Follower");
            }
            catch
            {
                // Ignore malformed legacy lines and continue loading valid history entries.
            }
        }
    }

    private async Task CopyLatestTwitchProfessionalSummaryAsync()
    {
        string path = GetStreamHistoryFilePath();
        if (!File.Exists(path))
        {
            MessageBox.Show("Es ist noch kein abgeschlossener Stream gespeichert.", "Twitch Professional", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (string? line in (await File.ReadAllLinesAsync(path)).Reverse())
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                DateTimeOffset startedAt = root.GetProperty("StartedAt").GetDateTimeOffset().ToLocalTime();
                long durationSeconds = root.TryGetProperty("DurationSeconds", out JsonElement duration) ? duration.GetInt64() : 0;
                int peak = root.TryGetProperty("PeakViewers", out JsonElement peakElement) ? peakElement.GetInt32() : 0;
                double average = root.TryGetProperty("AverageViewers", out JsonElement averageElement) ? averageElement.GetDouble() : 0;
                int followers = root.TryGetProperty("FollowersGained", out JsonElement followerElement) ? followerElement.GetInt32() : 0;
                int chat = root.TryGetProperty("ChatMessages", out JsonElement chatElement) ? chatElement.GetInt32() : 0;
                string category = root.TryGetProperty("Category", out JsonElement categoryElement) ? categoryElement.GetString() ?? "-" : "-";
                string title = root.TryGetProperty("Title", out JsonElement titleElement) ? titleElement.GetString() ?? "-" : "-";
                string summary = $"Stream-Zusammenfassung vom {startedAt:dd.MM.yyyy}\n" +
                              $"Titel: {title}\nKategorie: {category}\n" +
                              $"Dauer: {TimeSpan.FromSeconds(Math.Max(0, durationSeconds)):hh\\:mm\\:ss}\n" +
                              $"Peak: {peak} Zuschauer | Durchschnitt: {average:0.0}\n" +
                              $"Neue Follower: {followers} | Chatnachrichten: {chat}";
                Clipboard.SetText(summary);
                AddDashboardNotification("Stream-Zusammenfassung wurde in die Zwischenablage kopiert.", "Info");
                return;
            }
            catch
            {
                // Ungültige Historienzeilen werden übersprungen.
            }
        }
    }

    private async Task CreateTwitchProfessionalReportAsync()
    {
        string path = GetStreamHistoryFilePath();
        if (!File.Exists(path))
        {
            MessageBox.Show("Für einen Stream-Report werden abgeschlossene Streams benötigt.", "Twitch Professional", MessageBoxButton.OK, MessageBoxImage.Information);
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
            catch { }
        }

        if (rows.Count == 0)
        {
            MessageBox.Show("Es wurden keine gültigen Stream-Sessions gefunden.", "Twitch Professional", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        static string H(string value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
        var ordered = rows.OrderByDescending(x => x.StartedAt).ToList();
        var recent = ordered.Take(5).ToList();
        double totalHours = rows.Sum(x => x.DurationSeconds) / 3600d;
        var bestCategory = rows.Where(x => !string.IsNullOrWhiteSpace(x.Category) && x.Category != "-")
            .GroupBy(x => x.Category)
            .Select(group => new { Name = group.Key, Average = group.Average(x => x.Average) })
            .OrderByDescending(x => x.Average)
            .FirstOrDefault();
        string tableRows = string.Join(Environment.NewLine, ordered.Take(50).Select(row =>
            $"<tr><td>{row.StartedAt.ToLocalTime():dd.MM.yyyy HH:mm}</td><td>{H(row.Title)}</td><td>{H(row.Category)}</td><td>{TimeSpan.FromSeconds(Math.Max(0, row.DurationSeconds)):hh\\:mm\\:ss}</td><td>{row.Peak}</td><td>{row.Average:0.0}</td><td>{row.Followers}</td><td>{row.Chat}</td></tr>"));
        string html = $$"""<!doctype html><html lang="de"><head><meta charset="utf-8"><title>Twitch Stream-Report</title><style>body{font-family:Segoe UI,Arial;background:#0b1014;color:#eef3f6;margin:32px}h1,h2{color:#fff}.cards{display:flex;flex-wrap:wrap;gap:12px}.card{background:#151d23;border:1px solid #2a3740;border-radius:10px;padding:16px;min-width:160px}.value{font-size:26px;font-weight:700;margin-top:5px}table{width:100%;border-collapse:collapse;margin-top:16px;background:#11181d}th,td{border-bottom:1px solid #2a3740;padding:10px;text-align:left}th{background:#192229}.muted{color:#aeb8bf}</style></head><body><h1>CastingCouch – Twitch Stream-Report</h1><p class="muted">Erstellt am {{DateTime.Now:dd.MM.yyyy HH:mm}}</p><div class="cards"><div class="card">Streams<div class="value">{{rows.Count}}</div></div><div class="card">Rekord-Peak<div class="value">{{rows.Max(x => x.Peak)}}</div></div><div class="card">Bestes Ø<div class="value">{{rows.Max(x => x.Average):0.0}}</div></div><div class="card">Livezeit<div class="value">{{StreamStatisticsApplicationService.FormatDuration(rows.Sum(x => x.DurationSeconds))}}</div></div><div class="card">Follower<div class="value">{{rows.Sum(x => x.Followers)}}</div></div><div class="card">Chat / Std.<div class="value">{{(totalHours <= 0 ? 0 : rows.Sum(x => x.Chat) / totalHours):0.0}}</div></div></div><h2>Auswertung</h2><p>Die letzten {{recent.Count}} Streams erreichten durchschnittlich {{recent.Average(x => x.Average):0.0}} Zuschauer bei einem mittleren Peak von {{recent.Average(x => x.Peak):0.0}}. Beste Kategorie nach Zuschauerdurchschnitt: <strong>{{H(bestCategory?.Name ?? "-")}}</strong>.</p><h2>Letzte Streams</h2><table><thead><tr><th>Start</th><th>Titel</th><th>Kategorie</th><th>Dauer</th><th>Peak</th><th>Ø</th><th>Follower</th><th>Chat</th></tr></thead><tbody>{{tableRows}}</tbody></table></body></html>""";
        string reportPath = Path.Combine(GetStreamHistoryDirectory(), $"twitch-stream-report-{DateTime.Now:yyyyMMdd-HHmmss}.html");
        await File.WriteAllTextAsync(reportPath, html, new System.Text.UTF8Encoding(true));
        Process.Start(new ProcessStartInfo(reportPath) { UseShellExecute = true });
    }

    private async Task ExportTwitchProfessionalHistoryCsvAsync()
    {
        string path = GetStreamHistoryFilePath();
        if (!File.Exists(path))
        {
            return;
        }

        string csvPath = Path.Combine(GetStreamHistoryDirectory(), $"twitch-history-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        var lines = new List<string> { "StartedAt;EndedAt;DurationSeconds;PeakViewers;AverageViewers;FollowersGained;ChatMessages;Category;Title" };
        foreach (string line in await File.ReadAllLinesAsync(path))
        {
            try
            {
                using var doc = JsonDocument.Parse(line); JsonElement r = doc.RootElement;
                string V(string n) => r.TryGetProperty(n, out JsonElement v) ? v.ToString().Replace(";", ",").Replace("\r", " ").Replace("\n", " ") : string.Empty;
                lines.Add(string.Join(";", new[] { V("StartedAt"), V("EndedAt"), V("DurationSeconds"), V("PeakViewers"), V("AverageViewers"), V("FollowersGained"), V("ChatMessages"), V("Category"), V("Title") }));
            }
            catch { }
        }
        await File.WriteAllLinesAsync(csvPath, lines, new System.Text.UTF8Encoding(true));
        Process.Start(new ProcessStartInfo(csvPath) { UseShellExecute = true });
    }

    private void OpenStreamHistoryFolder()
    {
        string folder = GetStreamHistoryDirectory();
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }
}
