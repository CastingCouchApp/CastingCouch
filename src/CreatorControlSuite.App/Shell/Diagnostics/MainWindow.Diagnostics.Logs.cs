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
    private async Task RefreshLogsAsync()
    {
        if (_logsPaused)
        {
            return;
        }

        IReadOnlyList<AppLogEntry> entries = await _appLogger.ReadRecentAsync(1000);
        var validEntries = entries.Where(IsUsableLogEntry).ToList();
        var filtered = validEntries.Where(LogMatchesFilter).ToList();

        _visibleLogs.Clear();

        foreach (AppLogEntry? entry in filtered)
        {
            _visibleLogs.Add(entry);
        }

        await RefreshSpotifyInspectorAsync(validEntries);
    }

    private async Task RefreshSpotifyInspectorAsync(IReadOnlyList<AppLogEntry>? suppliedEntries = null)
    {
        IReadOnlyList<AppLogEntry> entries = suppliedEntries ?? await _appLogger.ReadRecentAsync(2000);
        var spotifyEntries = entries
            .Where(IsUsableLogEntry)
            .Where(entry => entry.Category.StartsWith("Spotify.", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.Timestamp)
            .ToList();

        DateTimeOffset oneMinuteAgo = DateTimeOffset.Now.AddMinutes(-1);
        SpotifyInspectorRequestsPerMinuteText.Text = spotifyEntries.Count(entry => entry.Timestamp >= oneMinuteAgo).ToString();

        IEnumerable<string> methodSummary = spotifyEntries
            .Select(ToSpotifyInspectorRow)
            .Where(row => row.Time != string.Empty)
            .GroupBy(row => string.IsNullOrWhiteSpace(row.Method) ? "–" : row.Method, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => $"{group.Key}: {group.Count()}")
            .Take(6);
        SpotifyInspectorTypeSummaryText.Text = spotifyEntries.Count == 0
            ? "Noch keine Aufrufe."
            : string.Join(" · ", methodSummary);

        AppLogEntry? latest = spotifyEntries.FirstOrDefault();
        SpotifyInspectorLastStatusText.Text = latest is null
            ? "Noch keine Anfrage"
            : GetProperty(latest, "statusCode", latest.Level.ToString());
        SpotifyInspectorRetryAfterText.Text = latest is null
            ? "–"
            : FormatRetryAfter(GetProperty(latest, "retryAfterSeconds", "none"));

        string filter = (SpotifyInspectorFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";
        IEnumerable<SpotifyApiInspectorRow> rows = spotifyEntries.Select(ToSpotifyInspectorRow);
        rows = filter switch
        {
            "GET" => rows.Where(row => string.Equals(row.Method, "GET", StringComparison.OrdinalIgnoreCase)),
            "WRITE" => rows.Where(row => row.Method is "POST" or "PUT" or "PATCH" or "DELETE"),
            "ERROR" => rows.Where(row => !int.TryParse(row.Status, out int code) || code >= 400),
            "OAUTH" => rows.Where(row => string.Equals(row.Category, "OAuth", StringComparison.OrdinalIgnoreCase)),
            _ => rows
        };

        _spotifyInspectorRows.Clear();
        foreach (SpotifyApiInspectorRow? row in rows.Take(100))
        {
            _spotifyInspectorRows.Add(row);
        }
    }

    private static SpotifyApiInspectorRow ToSpotifyInspectorRow(AppLogEntry entry)
    {
        string endpoint = GetProperty(entry, "endpoint", "");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            endpoint = InferEndpointFromMessage(entry.Message);
        }
        string operation = GetProperty(entry, "operation", "");
        string method = GetProperty(entry, "method", "");
        if (string.IsNullOrWhiteSpace(method))
        {
            method = !string.IsNullOrWhiteSpace(operation)
                ? operation
                : InferMethodFromMessage(entry.Message);
        }

        return new SpotifyApiInspectorRow(
            entry.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
            entry.Category.EndsWith("OAuth", StringComparison.OrdinalIgnoreCase) ? "OAuth" : "Web API",
            InferSpotifyRequestOrigin(entry.Category, method, endpoint),
            method,
            endpoint,
            GetProperty(entry, "statusCode", entry.Level.ToString()),
            GetProperty(entry, "durationMs", "–") is var duration && duration != "–" ? duration + " ms" : "–",
            FormatRetryAfter(GetProperty(entry, "retryAfterSeconds", "none")),
            entry.Message);
    }

    private static string InferSpotifyRequestOrigin(string category, string method, string endpoint)
    {
        if (category.EndsWith("OAuth", StringComparison.OrdinalIgnoreCase))
        {
            return "Verbindung";
        }

        if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint.Contains("/me/player", StringComparison.OrdinalIgnoreCase)
                ? "Statusabfrage"
                : "Datenabfrage";
        }

        if (endpoint.Contains("/player", StringComparison.OrdinalIgnoreCase))
        {
            return "Steuerbefehl";
        }

        return "API-Aufruf";
    }

    private static string InferMethodFromMessage(string message)
    {
        foreach (string? method in new[] { "GET", "POST", "PUT", "DELETE", "PATCH" })
        {
            if (message.Contains(method + " ", StringComparison.OrdinalIgnoreCase))
            {
                return method;
            }
        }
        return "–";
    }

    private static string InferEndpointFromMessage(string message)
    {
        int marker = message.IndexOf("/v1/", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return "–";
        }

        int end = message.IndexOf(" ->", marker, StringComparison.OrdinalIgnoreCase);
        return end > marker ? message[marker..end].Trim() : message[marker..].Trim();
    }

    private static bool IsUsableLogEntry(AppLogEntry? entry)
        => entry is not null &&
           !string.IsNullOrWhiteSpace(entry.Category) &&
           !string.IsNullOrWhiteSpace(entry.Message);

    private static string GetProperty(AppLogEntry entry, string key, string fallback)
        => entry.Properties is not null &&
           entry.Properties.TryGetValue(key, out string? value) &&
           !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;

    private static string FormatRetryAfter(string value)
        => int.TryParse(value, out int seconds) && seconds > 0
            ? $"{seconds} Sek."
            : "–";

    private void CopySelectedSpotifyInspectorEntry()
    {
        if (SpotifyInspectorGrid.SelectedItem is not SpotifyApiInspectorRow row)
        {
            return;
        }

        Clipboard.SetText($"{row.Time} | {row.Category} | {row.Origin} | {row.Method} | {row.Endpoint} | {row.Status} | {row.Duration} | Retry-After: {row.RetryAfter}\n{row.Message}");
    }

    private sealed record SpotifyApiInspectorRow(
        string Time,
        string Category,
        string Origin,
        string Method,
        string Endpoint,
        string Status,
        string Duration,
        string RetryAfter,
        string Message);

    private bool LogMatchesFilter(AppLogEntry entry)
    {
        string search = LogSearchBox.Text.Trim();

        if (!string.IsNullOrWhiteSpace(search) &&
            !entry.Message.Contains(
                search,
                StringComparison.OrdinalIgnoreCase) &&
            !entry.Category.Contains(
                search,
                StringComparison.OrdinalIgnoreCase) &&
            !(entry.Exception?.Contains(
                search,
                StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return false;
        }

        string selected =
            (LogLevelFilterBox.SelectedItem
                as System.Windows.Controls.ComboBoxItem)
                ?.Content
                ?.ToString()
            ?? "Alle";

        return selected == "Alle" ||
               string.Equals(
                   selected,
                   entry.Level.ToString(),
                   StringComparison.OrdinalIgnoreCase);
    }

    private void CopySelectedLog()
    {
        if (LogsGrid.SelectedItem is not AppLogEntry entry)
        {
            return;
        }

        Clipboard.SetText(
            $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} " +
            $"[{entry.Level}] {entry.Category}: {entry.Message}" +
            (string.IsNullOrWhiteSpace(entry.Exception)
                ? ""
                : Environment.NewLine + entry.Exception));
    }

    private async Task ExportLogsAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Textdatei (*.txt)|*.txt",
            FileName =
                "CreatorControlSuite-Logs-" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss") +
                ".txt"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await _appLogger.ExportAsync(dialog.FileName);

        MessageBox.Show(
            "Logs wurden exportiert.",
            "Creator Control Suite",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
