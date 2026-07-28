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
            string previousDisable = _settings.StreamerBot.DisableAlertsActionName;
            string previousEnable = _settings.StreamerBot.EnableAlertsActionName;
            IReadOnlyList<StreamerBotActionOption> ordered =
                StreamerBotApplicationService.ParseActions(
                    response.RootElement);
            _streamerBotActions.Clear();
            foreach (StreamerBotActionOption option in ordered)
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

            _alertRuntimePageViewModel.SetStreamerBotGroups(
                StreamerBotApplicationService.SelectGroups(ordered));
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
        ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionsList.ItemsSource =
            StreamerBotApplicationService.FilterActions(
                _streamerBotActions,
                _streamerBotFavoriteActionIds,
                search);
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
            Dictionary<string, object?> arguments =
                StreamerBotApplicationService.ParseArguments(
                    ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionArgumentsBox.Text);
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
        try { _ = StreamerBotApplicationService.ParseArguments(ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionArgumentsBox.Text); }
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
            ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionArgumentsBox.Text =
                StreamerBotApplicationService.FormatArguments(
                    ServicesPageViewHost.StreamerBotServiceViewHost.ServicesStreamerBotActionArgumentsBox.Text);
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
