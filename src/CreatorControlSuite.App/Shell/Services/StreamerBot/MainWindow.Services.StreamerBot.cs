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
    private async Task ApplyStreamerBotAlertSuppressionAsync()
    {
        if (!_streamerBotClient.IsConnected)
        {
            _alertRuntimePageViewModel.SetStreamerBotStatus(
                "Streamer.bot ist nicht verbunden. Die Einstellung wird beim nächsten Verbindungsaufbau angewendet.");
            return;
        }

        bool suppress = _alertRuntimePageViewModel.Enabled &&
                        _alertRuntimePageViewModel.SuppressStreamerBotAlerts;
        await SetStreamerBotAlertsEnabledAsync(!suppress, showSuccess: false);
    }

    private void BindStreamerBotActionSelectors()
    {
        SettingsPageViewHost.SettingsStreamerBotDisableAlertsActionBox.ItemsSource = _streamerBotActions;
        SettingsPageViewHost.SettingsStreamerBotEnableAlertsActionBox.ItemsSource = _streamerBotActions;
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStreamerBotActionBox.ItemsSource = _streamerBotActions;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationStreamerBotActionBox.ItemsSource = _streamerBotActions;
    }

    private static string GetStreamerBotActionName(params object[] values)
    {
        foreach (object value in values)
        {
            if (value is System.Windows.Controls.ComboBox combo)
            {
                if (combo.SelectedItem is StreamerBotActionOption option && !string.IsNullOrWhiteSpace(option.Name))
                {
                    return option.Name;
                }

                if (!string.IsNullOrWhiteSpace(combo.Text))
                {
                    return combo.Text.Trim();
                }
            }
            else if (value is string text && !string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }
        return string.Empty;
    }

    private void SyncStreamerBotActionSelectorText()
    {
        _alertRuntimePageViewModel.SelectActions(
            _settings.StreamerBot.DisableAlertsActionId,
            _settings.StreamerBot.DisableAlertsActionName,
            _settings.StreamerBot.EnableAlertsActionId,
            _settings.StreamerBot.EnableAlertsActionName);
    }

    private async Task RefreshStreamerBotActionsAsync(bool showStatus)
    {
        if (!_streamerBotClient.IsConnected)
        {
            _alertRuntimePageViewModel.SetStreamerBotStatus(
                "Streamer.bot ist nicht verbunden.");
            return;
        }

        try
        {
            JsonDocument response = await SendStreamerBotRequestAsync(new { request = "GetActions" });
            if (!response.RootElement.TryGetProperty("actions", out JsonElement actionsElement) || actionsElement.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                throw new InvalidOperationException("Streamer.bot hat keine Aktionsliste zurückgegeben.");
            }

            string previousDisable = _settings.StreamerBot.DisableAlertsActionName;
            string previousEnable = _settings.StreamerBot.EnableAlertsActionName;
            _streamerBotActions.Clear();
            foreach (JsonElement action in actionsElement.EnumerateArray())
            {
                string id = action.TryGetProperty("id", out JsonElement idNode) ? idNode.GetString() ?? "" : "";
                string name = action.TryGetProperty("name", out JsonElement nameNode) ? nameNode.GetString() ?? "" : "";
                string group = action.TryGetProperty("group", out JsonElement groupNode) ? groupNode.GetString() ?? "Ohne Gruppe" : "Ohne Gruppe";
                bool enabled = !action.TryGetProperty("enabled", out JsonElement enabledNode) || enabledNode.GetBoolean();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _streamerBotActions.Add(new StreamerBotActionOption(id, name, group, enabled));
                }
            }

            var ordered = _streamerBotActions.OrderBy(x => x.Group).ThenBy(x => x.Name).ToList();
            _streamerBotActions.Clear();
            foreach (StreamerBotActionOption? option in ordered)
            {
                _streamerBotActions.Add(option);
            }

            ApplyStreamerBotActionFilter();

            SelectStreamerBotAction(SettingsPageViewHost.SettingsStreamerBotDisableAlertsActionBox, _settings.StreamerBot.DisableAlertsActionId, previousDisable);
            SelectStreamerBotAction(SettingsPageViewHost.SettingsStreamerBotEnableAlertsActionBox, _settings.StreamerBot.EnableAlertsActionId, previousEnable);
            _alertRuntimePageViewModel.SelectActions(
                _settings.StreamerBot.DisableAlertsActionId,
                previousDisable,
                _settings.StreamerBot.EnableAlertsActionId,
                previousEnable);
            if (WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.SelectedItem is RunOfShowStepSettings selectedRunOfShowStep)
            {
                SelectStreamerBotAction(WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStreamerBotActionBox, selectedRunOfShowStep.StreamerBotActionId, selectedRunOfShowStep.StreamerBotActionName);
            }

            var groups = ordered.Select(x => x.Group).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _alertRuntimePageViewModel.SetStreamerBotGroups(groups);
            if (showStatus)
            {
                _alertRuntimePageViewModel.SetStreamerBotStatus(
                    $"{ordered.Count} Streamer.bot-Aktionen geladen. Wähle je eine Hilfsaktion zum Deaktivieren und Aktivieren aus.");
            }
        }
        catch (Exception ex)
        {
            _alertRuntimePageViewModel.SetStreamerBotStatus(
                "Streamer.bot-Aktionen konnten nicht geladen werden: " +
                ex.Message);
        }
    }

    private void ApplyStreamerBotActionFilter()
    {
        string search = ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionSearchBox.Text?.Trim() ?? string.Empty;
        IEnumerable<StreamerBotActionOption> filtered = string.IsNullOrWhiteSpace(search)
            ? _streamerBotActions.AsEnumerable()
            : _streamerBotActions.Where(action =>
                action.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                action.Group.Contains(search, StringComparison.OrdinalIgnoreCase));
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionsList.ItemsSource = filtered
            .OrderByDescending(action => _streamerBotFavoriteActionIds.Contains(action.Id))
            .ThenBy(action => action.Group)
            .ThenBy(action => action.Name)
            .ToList();
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionResultText.Text = string.IsNullOrWhiteSpace(search)
            ? $"{_streamerBotActions.Count} Aktionen verfügbar."
            : $"{ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionsList.Items.Count} von {_streamerBotActions.Count} Aktionen gefunden.";
    }

    private void UpdateSelectedStreamerBotAction()
    {
        if (ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionsList.SelectedItem is not StreamerBotActionOption action)
        {
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotSelectedActionText.Text = "Keine Aktion ausgewählt.";
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionDetailsText.Text = "Wähle eine Aktion aus, um Details und Parameter zu sehen.";
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotFavoriteActionButton.IsEnabled = false;
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotRunActionButton.IsEnabled = false;
            return;
        }

        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotSelectedActionText.Text = $"{action.Name} · {action.Group}";
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionDetailsText.Text = $"ID: {action.Id} · Status: {(action.Enabled ? "Aktiv" : "Deaktiviert")} · Gruppe: {action.Group}";
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotFavoriteActionButton.IsEnabled = true;
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotFavoriteActionButton.Content = _streamerBotFavoriteActionIds.Contains(action.Id) ? "★ FAVORIT" : "☆ FAVORIT";
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotRunActionButton.IsEnabled = action.Enabled &&
            _streamerBotClient.IsConnected;
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionResultText.Text = action.Enabled
            ? "Bereit zur Ausführung. Optionale Parameter können als JSON übergeben werden."
            : "Diese Streamer.bot-Aktion ist deaktiviert.";
    }

    private async Task RunSelectedStreamerBotActionAsync()
    {
        if (ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionsList.SelectedItem is not StreamerBotActionOption action)
        {
            return;
        }

        int repeatCount = int.TryParse(ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotRepeatCountBox.Text, out int count) ? Math.Clamp(count, 1, 20) : 1;
        int delayMs = int.TryParse(ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotRepeatDelayBox.Text, out int delay) ? Math.Clamp(delay, 0, 10000) : 500;
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotRepeatCountBox.Text = repeatCount.ToString();
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotRepeatDelayBox.Text = delayMs.ToString();
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotRunActionButton.IsEnabled = false;
        try
        {
            for (int index = 1; index <= repeatCount; index++)
            {
                ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionResultText.Text = $"„{action.Name}“ wird ausgeführt ({index}/{repeatCount}) …";
                await ExecuteStreamerBotActionOnceAsync(action);
                if (index < repeatCount && delayMs > 0)
                {
                    await Task.Delay(delayMs);
                }
            }
        }
        finally { UpdateSelectedStreamerBotAction(); }
    }

    private async Task ExecuteStreamerBotActionOnceAsync(StreamerBotActionOption action)
    {
        try
        {
            DateTimeOffset started = DateTimeOffset.UtcNow;
            Dictionary<string, object?> arguments = ParseStreamerBotArguments(ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionArgumentsBox.Text);
            arguments["source"] = "Creator Control Suite";
            arguments["manual"] = true;
            using JsonDocument response = await SendStreamerBotRequestAsync(new
            {
                request = "DoAction",
                action = new { id = action.Id, name = action.Name },
                args = arguments
            });
            string? status = response.RootElement.TryGetProperty("status", out JsonElement node) ? node.GetString() : null;
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotLastResponseBox.Text = System.Text.Json.JsonSerializer.Serialize(
                response.RootElement,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Streamer.bot hat die Aktion nicht bestätigt.");
            }

            TimeSpan elapsed = DateTimeOffset.UtcNow - started;
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionResultText.Text = $"Aktion erfolgreich ausgeführt · {elapsed.TotalMilliseconds:0} ms";
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionResultText.Foreground = Brushes.LightGreen;
            AddStreamerBotHistory(action, true, $"{elapsed.TotalMilliseconds:0} ms", ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionArgumentsBox.Text, ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotLastResponseBox.Text);
        }
        catch (Exception exception)
        {
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionResultText.Text = "Aktion fehlgeschlagen: " + exception.Message;
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionResultText.Foreground = Brushes.IndianRed;
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotLastResponseBox.Text = exception.Message;
            AddStreamerBotHistory(action, false, exception.Message, ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionArgumentsBox.Text, exception.Message);
            throw;
        }
    }

    private void SaveSelectedStreamerBotTemplate()
    {
        if (ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionsList.SelectedItem is not StreamerBotActionOption action)
        {
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionResultText.Text = "Zum Speichern einer Vorlage zuerst eine Aktion auswählen.";
            return;
        }
        try { _ = ParseStreamerBotArguments(ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionArgumentsBox.Text); }
        catch (Exception exception) { ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionResultText.Text = "Vorlage nicht gespeichert: " + exception.Message; return; }
        string? name = ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotTemplateNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = action.Name;
        }

        StreamerBotActionTemplate? existing = _streamerBotActionTemplates.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            _streamerBotActionTemplates.Remove(existing);
        }

        var template = new StreamerBotActionTemplate(name, action.Id, action.Name, ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionArgumentsBox.Text.Trim());
        _streamerBotActionTemplates.Add(template);
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotTemplateBox.SelectedItem = template;
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionResultText.Text = $"Vorlage „{name}“ gespeichert.";
    }

    private void LoadSelectedStreamerBotTemplate()
    {
        if (ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotTemplateBox.SelectedItem is not StreamerBotActionTemplate template)
        {
            return;
        }

        StreamerBotActionOption? action = _streamerBotActions.FirstOrDefault(x => string.Equals(x.Id, template.ActionId, StringComparison.OrdinalIgnoreCase))
            ?? _streamerBotActions.FirstOrDefault(x => string.Equals(x.Name, template.ActionName, StringComparison.OrdinalIgnoreCase));
        if (action is not null)
        {
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionsList.SelectedItem = action;
        }

        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionArgumentsBox.Text = template.ArgumentsJson;
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotTemplateNameBox.Text = template.Name;
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionResultText.Text = $"Vorlage „{template.Name}“ geladen.";
    }

    private void DeleteSelectedStreamerBotTemplate()
    {
        if (ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotTemplateBox.SelectedItem is not StreamerBotActionTemplate template)
        {
            return;
        }

        _streamerBotActionTemplates.Remove(template);
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionResultText.Text = $"Vorlage „{template.Name}“ gelöscht.";
    }

    private async Task ScheduleSelectedStreamerBotActionAsync()
    {
        if (ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionsList.SelectedItem is not StreamerBotActionOption action)
        {
            return;
        }

        double minutes = double.TryParse(ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotScheduleMinutesBox.Text, out double value) ? Math.Clamp(value, 0.05, 1440) : 1;
        CancelScheduledStreamerBotAction();
        _streamerBotScheduledActionCts = new CancellationTokenSource();
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotCancelScheduleButton.IsEnabled = true;
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionResultText.Text = $"„{action.Name}“ startet in {minutes:0.##} Minute(n).";
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(minutes), _streamerBotScheduledActionCts.Token);
            await ExecuteStreamerBotActionOnceAsync(action);
        }
        catch (OperationCanceledException) { ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionResultText.Text = "Geplante Ausführung wurde abgebrochen."; }
        catch (Exception) { }
        finally
        {
            _streamerBotScheduledActionCts?.Dispose();
            _streamerBotScheduledActionCts = null;
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotCancelScheduleButton.IsEnabled = false;
        }
    }

    private void CancelScheduledStreamerBotAction()
    {
        _streamerBotScheduledActionCts?.Cancel();
    }

    private Dictionary<string, object?> ParseStreamerBotArguments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = System.Text.Json.JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            throw new InvalidOperationException("Die Parameter müssen ein JSON-Objekt sein.");
        }

        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(document.RootElement.GetRawText())
            ?? [];
    }

    private void ToggleSelectedStreamerBotFavorite()
    {
        if (ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionsList.SelectedItem is not StreamerBotActionOption action)
        {
            return;
        }

        if (!_streamerBotFavoriteActionIds.Add(action.Id))
        {
            _streamerBotFavoriteActionIds.Remove(action.Id);
        }

        ApplyStreamerBotActionFilter();
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionsList.SelectedItem = action;
        UpdateSelectedStreamerBotAction();
    }

    private void AddStreamerBotHistory(StreamerBotActionOption action, bool success, string detail, string argumentsJson, string responseJson)
    {
        _streamerBotExecutionHistory.Insert(0, new StreamerBotExecutionHistoryItem(DateTimeOffset.Now, action.Name, success, detail, argumentsJson, responseJson));
        while (_streamerBotExecutionHistory.Count > 50)
        {
            _streamerBotExecutionHistory.RemoveAt(_streamerBotExecutionHistory.Count - 1);
        }
    }

    private void FormatStreamerBotArgumentsJson()
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionArgumentsBox.Text);
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionArgumentsBox.Text = System.Text.Json.JsonSerializer.Serialize(
                document.RootElement,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionResultText.Text = "JSON wurde geprüft und formatiert.";
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionResultText.Foreground = Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionResultText.Text = "JSON ist ungültig: " + exception.Message;
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionResultText.Foreground = Brushes.IndianRed;
        }
    }

    private void ExportStreamerBotHistoryCsv()
    {
        if (_streamerBotExecutionHistory.Count == 0)
        {
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionResultText.Text = "Es sind keine Historieneinträge zum Exportieren vorhanden.";
            return;
        }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Streamer.bot-Ausführungshistorie exportieren",
            Filter = "CSV-Datei|*.csv",
            FileName = $"streamerbot-history-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        static string Csv(string? value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
        var lines = new List<string> { "Zeitpunkt;Aktion;Erfolg;Detail;Argumente;Antwort" };
        lines.AddRange(_streamerBotExecutionHistory.Select(item => string.Join(";",
            Csv(item.Timestamp.ToString("O")), Csv(item.ActionName), Csv(item.Success ? "Ja" : "Nein"),
            Csv(item.Detail), Csv(item.ArgumentsJson), Csv(item.ResponseJson))));
        System.IO.File.WriteAllLines(dialog.FileName, lines, new System.Text.UTF8Encoding(true));
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionResultText.Text = $"Historie exportiert: {dialog.FileName}";
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionResultText.Foreground = Brushes.LightGreen;
    }

    private async Task ReconnectStreamerBotAsync()
    {
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotDiagnosticText.Text = "Verbindung wird neu aufgebaut …";
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotDiagnosticText.Foreground = Brushes.Gold;
        Exception? lastError = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await DisconnectStreamerBotAsync();
                await Task.Delay(attempt * 400);
                await ConnectStreamerBotAsync();
                if (_streamerBotClient.IsConnected)
                {
                    await RefreshStreamerBotActionsAsync(true);
                    ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotDiagnosticText.Text = $"Neu verbunden · Versuch {attempt}/3 · Aktionen aktualisiert.";
                    ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotDiagnosticText.Foreground = Brushes.LightGreen;
                    return;
                }
            }
            catch (Exception exception) { lastError = exception; }
        }
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotDiagnosticText.Text = "Neuverbinden fehlgeschlagen: " + (lastError?.Message ?? "Keine WebSocket-Verbindung.");
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotDiagnosticText.Foreground = Brushes.IndianRed;
    }

    private async Task DiagnoseStreamerBotAsync()
    {
        if (!_streamerBotClient.IsConnected)
        {
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotDiagnosticText.Text = "Nicht verbunden – zuerst die WebSocket-Verbindung herstellen.";
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotDiagnosticText.Foreground = Brushes.IndianRed;
            return;
        }

        try
        {
            DateTimeOffset started = DateTimeOffset.UtcNow;
            using JsonDocument response = await SendStreamerBotRequestAsync(new { request = "GetActions" }, TimeSpan.FromSeconds(5));
            TimeSpan elapsed = DateTimeOffset.UtcNow - started;
            int actionCount = response.RootElement.TryGetProperty("actions", out JsonElement actions) && actions.ValueKind == System.Text.Json.JsonValueKind.Array
                ? actions.GetArrayLength()
                : 0;
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotDiagnosticText.Text = $"WebSocket OK · Antwort {elapsed.TotalMilliseconds:0} ms · {actionCount} Aktionen · Event-Listener {(_streamerBotEventSocket?.State == System.Net.WebSockets.WebSocketState.Open ? "aktiv" : "inaktiv")}";
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotDiagnosticText.Foreground = Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotDiagnosticText.Text = "Diagnose fehlgeschlagen: " + exception.Message;
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotDiagnosticText.Foreground = Brushes.IndianRed;
        }
    }

    private static void SelectStreamerBotAction(System.Windows.Controls.ComboBox box, string id, string name)
    {
        if (box.ItemsSource is not IEnumerable<StreamerBotActionOption> actions) { box.Text = name; return; }
        StreamerBotActionOption? selected = actions.FirstOrDefault(x => !string.IsNullOrWhiteSpace(id) && string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? actions.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (selected is not null)
        {
            box.SelectedItem = selected;
        }
        else
        {
            box.Text = name;
        }
    }

    private async Task<System.Text.Json.JsonDocument> SendStreamerBotRequestAsync(object requestBody, TimeSpan? timeout = null)
        => await _streamerBotClient.SendRequestAsync(requestBody, timeout);

    private async Task SetStreamerBotAlertsEnabledAsync(bool enabled, bool showSuccess = true)
    {
        if (!_streamerBotClient.IsConnected)
        {
            _alertRuntimePageViewModel.SetStreamerBotStatus(
                "Streamer.bot ist nicht verbunden.");
            return;
        }

        ComboBox settingsBox = enabled ? SettingsPageViewHost.SettingsStreamerBotEnableAlertsActionBox : SettingsPageViewHost.SettingsStreamerBotDisableAlertsActionBox;
        string selectedId = enabled
            ? _alertRuntimePageViewModel.EnableActionId
            : _alertRuntimePageViewModel.DisableActionId;
        string selectedName = enabled
            ? _alertRuntimePageViewModel.EnableActionName
            : _alertRuntimePageViewModel.DisableActionName;
        StreamerBotActionOption? selected = _streamerBotActions.FirstOrDefault(
            action => string.Equals(
                action.Id,
                selectedId,
                StringComparison.OrdinalIgnoreCase))
            ?? settingsBox.SelectedItem as StreamerBotActionOption;
        string actionName = selected?.Name ?? GetStreamerBotActionName(
            selectedName,
            settingsBox,
            enabled
                ? _settings.StreamerBot.EnableAlertsActionName
                : _settings.StreamerBot.DisableAlertsActionName);
        string actionId = selected?.Id ?? selectedId;
        if (string.IsNullOrWhiteSpace(actionName) && string.IsNullOrWhiteSpace(actionId))
        {
            _alertRuntimePageViewModel.SetStreamerBotStatus(
                "Bitte zuerst eine vorhandene Streamer.bot-Hilfsaktion auswählen.");
            return;
        }

        try
        {
            var action = !string.IsNullOrWhiteSpace(actionId) ? new { id = actionId, name = actionName } : new { id = "", name = actionName };
            using JsonDocument response = await SendStreamerBotRequestAsync(new
            {
                request = "DoAction",
                action,
                args = new { source = "Creator Control Suite", alertsEnabled = enabled }
            });
            string? status = response.RootElement.TryGetProperty("status", out JsonElement statusNode) ? statusNode.GetString() : null;
            if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Streamer.bot hat die Aktion nicht bestätigt.");
            }

            if (enabled)
            {
                _settings.StreamerBot.EnableAlertsActionName = actionName;
                _settings.StreamerBot.EnableAlertsActionId = actionId;
            }
            else
            {
                _settings.StreamerBot.DisableAlertsActionName = actionName;
                _settings.StreamerBot.DisableAlertsActionId = actionId;
            }
            _alertRuntimePageViewModel.SelectActions(
                _settings.StreamerBot.DisableAlertsActionId,
                _settings.StreamerBot.DisableAlertsActionName,
                _settings.StreamerBot.EnableAlertsActionId,
                _settings.StreamerBot.EnableAlertsActionName);
            _alertRuntimePageViewModel.SetStreamerBotStatus(
                showSuccess
                    ? $"Streamer.bot hat die Aktion „{actionName}“ bestätigt."
                    : enabled
                        ? "Streamer.bot-Alerts bleiben aktiv."
                        : "Suite-Alerts aktiv: Deaktivierungsaktion wurde von Streamer.bot bestätigt.");
        }
        catch (Exception ex)
        {
            _alertRuntimePageViewModel.SetStreamerBotStatus(
                "Streamer.bot-Alertsteuerung fehlgeschlagen: " + ex.Message);
        }
    }

    private async Task ConnectStreamerBotAsync()
    {
        await DisconnectStreamerBotAsync();
        try
        {
            await _streamerBotClient.ConnectAsync(_settings.StreamerBot);
            await RefreshStreamerBotActionsAsync(false);
            await StartStreamerBotEventListenerAsync();
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotStatusText.Text = _streamerBotClient.Status.Detail;
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotDiagnosticText.Text = $"WebSocket verbunden · {_streamerBotActions.Count} Aktionen geladen · Event-Listener aktiv";
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotDiagnosticText.Foreground = Brushes.LightGreen;
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
            StreamerBotDashboardStatus.Text = "VERBUNDEN";
            StreamerBotDashboardLamp.Fill =
                System.Windows.Media.Brushes.LimeGreen;
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotServicesList.ItemsSource = new[] { "WebSocket API · verbunden", "OBS · Status über Streamer.bot API verfügbar", "Twitch · Status über Streamer.bot API verfügbar", "YouTube · falls in Streamer.bot eingerichtet" };
            await ApplyStreamerBotAlertSuppressionAsync();
        }
        catch (Exception ex)
        {
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotStatusText.Text = ex.Message;
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotStatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
        }
    }

    private async Task StartStreamerBotEventListenerAsync()
    {
        _streamerBotEventCts?.Cancel();
        _streamerBotEventSocket?.Dispose();
        _streamerBotEventCts = new CancellationTokenSource();
        _streamerBotEventSocket = new System.Net.WebSockets.ClientWebSocket();

        StreamerBotConnectionInfo connection = _streamerBotClient.ResolveConnection(_settings.StreamerBot);
        if (!string.IsNullOrWhiteSpace(connection.Password))
        {
            _streamerBotEventSocket.Options.SetRequestHeader("Authorization", "Bearer " + connection.Password);
        }

        await _streamerBotEventSocket.ConnectAsync(connection.WebSocketUri, _streamerBotEventCts.Token);

        string subscribe = System.Text.Json.JsonSerializer.Serialize(new
        {
            request = "Subscribe",
            id = "ccs-events-" + Guid.NewGuid().ToString("N"),
            events = new
            {
                Twitch = new[] { "Follow", "Cheer", "Sub", "ReSub", "GiftSub", "GiftBomb", "Raid" },
                General = new[] { "Custom" }
            }
        });
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(subscribe);
        await _streamerBotEventSocket.SendAsync(bytes, System.Net.WebSockets.WebSocketMessageType.Text, true, _streamerBotEventCts.Token);
        _ = Task.Run(() => ListenForStreamerBotAlertEventsAsync(_streamerBotEventCts.Token));
    }

    private async Task ListenForStreamerBotAlertEventsAsync(CancellationToken token)
    {
        byte[] buffer = new byte[64 * 1024];
        try
        {
            while (!token.IsCancellationRequested && _streamerBotEventSocket is { State: System.Net.WebSockets.WebSocketState.Open })
            {
                using var stream = new MemoryStream();
                System.Net.WebSockets.WebSocketReceiveResult result;
                do
                {
                    result = await _streamerBotEventSocket.ReceiveAsync(buffer, token);
                    if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                    {
                        return;
                    }

                    stream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                using var document = System.Text.Json.JsonDocument.Parse(stream.ToArray());
                JsonElement root = document.RootElement;
                if (!root.TryGetProperty("event", out JsonElement eventNode))
                {
                    continue;
                }

                string source = eventNode.TryGetProperty("source", out JsonElement sourceNode) ? sourceNode.GetString() ?? "Streamer.bot" : "Streamer.bot";
                string type = eventNode.TryGetProperty("type", out JsonElement typeNode) ? typeNode.GetString() ?? "Alert" : "Alert";
                string normalized = (source + " " + type).ToLowerInvariant();
                string summary = BuildStreamerBotEventSummary(root, source, type);

                await Dispatcher.InvokeAsync(() =>
                {
                    _streamerBotLiveEvents.Insert(0, new StreamerBotLiveEventItem(DateTimeOffset.Now, source, type, summary));
                    while (_streamerBotLiveEvents.Count > 100)
                    {
                        _streamerBotLiveEvents.RemoveAt(_streamerBotLiveEvents.Count - 1);
                    }

                    ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotLiveEventStatusText.Text = $"Letztes Ereignis: {type} · {DateTime.Now:HH:mm:ss}";
                    ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotLiveEventsList.ScrollIntoView(_streamerBotLiveEvents.FirstOrDefault());
                });

                bool isKnownAlert = normalized.Contains("follow") || normalized.Contains("cheer") || normalized.Contains("sub") ||
                                   normalized.Contains("raid") || normalized.Contains("alert");
                if (!isKnownAlert)
                {
                    continue;
                }

                string id = Guid.NewGuid().ToString("N");
                _ = PulseExternalAlertAsync("Streamer.bot", id, TimeSpan.FromSeconds(8));
                await Dispatcher.InvokeAsync(() =>
                {
                    _spotifyAutomationPageViewModel.SetAlertStatus(
                        $"Streamer.bot-Alert erkannt: {type}",
                        "Warning");
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _appLogger.Write(AppLogLevel.Warning, "Streamer.bot", "Event-Listener für Alert-Ducking wurde beendet.", exception);
        }
    }


    private static string BuildStreamerBotEventSummary(System.Text.Json.JsonElement root, string source, string type)
    {
        static string? ReadString(System.Text.Json.JsonElement element, params string[] names)
        {
            foreach (string name in names)
            {
                if (element.TryGetProperty(name, out JsonElement value) && value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return value.GetString();
                }
            }
            return null;
        }

        JsonElement data = root.TryGetProperty("data", out JsonElement dataNode) && dataNode.ValueKind == System.Text.Json.JsonValueKind.Object
            ? dataNode
            : root;
        string? user = ReadString(data, "user_name", "userName", "displayName", "user", "from");
        string? message = ReadString(data, "message", "text", "input", "reason");
        string? amount = ReadString(data, "amount", "bits", "months", "viewers");
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(user))
        {
            parts.Add(user);
        }

        if (!string.IsNullOrWhiteSpace(amount))
        {
            parts.Add(amount);
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            parts.Add(message);
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : $"{source} · {type}";
    }

    private async Task DisconnectStreamerBotAsync()
    {
        _streamerBotEventCts?.Cancel();
        if (_streamerBotEventSocket is { State: System.Net.WebSockets.WebSocketState.Open })
        {
            try { await _streamerBotEventSocket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "disconnect", CancellationToken.None); } catch { }
        }
        _streamerBotEventSocket?.Dispose();
        _streamerBotEventSocket = null;
        _streamerBotEventCts?.Dispose();
        _streamerBotEventCts = null;

        await _streamerBotClient.DisconnectAsync();
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotStatusText.Text = "Nicht verbunden";
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotStatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
        StreamerBotDashboardStatus.Text = "NICHT VERBUNDEN";
        StreamerBotDashboardLamp.Fill =
            System.Windows.Media.Brushes.IndianRed;
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotServicesList.ItemsSource = null;
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionsList.ItemsSource = null;
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotDiagnosticText.Text = "Verbindung getrennt.";
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotDiagnosticText.Foreground = Brushes.Gray;
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotSelectedActionText.Text = "Keine Aktion ausgewählt.";
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotRunActionButton.IsEnabled = false;

        RefreshDashboardServiceActionButtons();
    }

    private sealed record StreamerBotExecutionHistoryItem(DateTimeOffset Timestamp, string ActionName, bool Success, string Detail, string ArgumentsJson, string ResponseJson)
    {
        public string DisplayName => $"{Timestamp:HH:mm:ss} · {(Success ? "OK" : "FEHLER")} · {ActionName} · {Detail}";
    }

    private sealed record StreamerBotActionTemplate(string Name, string ActionId, string ActionName, string ArgumentsJson)
    {
        public string DisplayName => $"{Name} · {ActionName}";
    }

    private sealed record StreamerBotLiveEventItem(DateTimeOffset Timestamp, string Source, string Type, string Summary)
    {
        public string DisplayName => $"{Timestamp:HH:mm:ss} · {Source} / {Type} · {Summary}";
    }
}
