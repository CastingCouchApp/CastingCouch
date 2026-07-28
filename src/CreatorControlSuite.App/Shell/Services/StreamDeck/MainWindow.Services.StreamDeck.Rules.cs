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

public partial class MainWindow
{
    private string StreamDeckAutomationRulesFile => Path.Combine(StreamDeckActionsDirectory, "streamdeck-automation-rules.json");
    private string StreamDeckRuleTemplatesFile => Path.Combine(StreamDeckActionsDirectory, "streamdeck-rule-templates.json");
    private string StreamDeckStableStateFile => Path.Combine(StreamDeckActionsDirectory, "streamdeck-stable-state.json");

    private sealed class StreamDeckAutomationRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Condition { get; set; } = "stream.live";
        public string Condition2 { get; set; } = string.Empty;
        public string LogicalOperator { get; set; } = "and";
        public string Profile { get; set; } = "Standard";
        public string Page { get; set; } = "Hauptseite";
        public int Priority { get; set; } = 100;
        public int DelaySeconds { get; set; }
        public int HoldSeconds { get; set; } = 10;
        public string Time { get; set; } = "20:00";
        public bool IsFallback { get; set; }
        public bool Enabled { get; set; } = true;
        public string Group { get; set; } = "Standard";
        public string ActiveDays { get; set; } = "Mo,Di,Mi,Do,Fr,Sa,So";
        public string ActiveWindow { get; set; } = "00:00-23:59";
        public DateTimeOffset? LastAppliedAt { get; set; }
        public DateTimeOffset? LastEvaluatedAt { get; set; }
        public int MatchCount { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public int ConsecutiveFailures { get; set; }
        public string LastError { get; set; } = string.Empty;
        public string DisabledReason { get; set; } = string.Empty;
    }

    private List<StreamDeckAutomationRule> LoadStreamDeckAutomationRules()
    {
        try
        {
            if (!File.Exists(StreamDeckAutomationRulesFile))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<StreamDeckAutomationRule>>(File.ReadAllText(StreamDeckAutomationRulesFile)) ?? [];
        }
        catch { return []; }
    }

    private async Task SaveStreamDeckAutomationRulesAsync(List<StreamDeckAutomationRule> rules)
    {
        Directory.CreateDirectory(StreamDeckActionsDirectory);
        await File.WriteAllTextAsync(StreamDeckAutomationRulesFile, JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void RefreshStreamDeckAutomationRules()
    {
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRulesList.Items.Clear();
        foreach (StreamDeckAutomationRule? rule in LoadStreamDeckAutomationRules().OrderByDescending(r => r.Priority))
        {
            string delay = rule.DelaySeconds > 0 ? $" · +{rule.DelaySeconds}s" : string.Empty;
            string fallback = rule.IsFallback ? " · Fallback" : string.Empty;
            string health = rule.Enabled ? $" · OK {rule.SuccessCount}/F {rule.FailureCount}" : $" · DEAKTIVIERT{(string.IsNullOrWhiteSpace(rule.DisabledReason) ? string.Empty : $": {rule.DisabledReason}")}";
            string group = string.IsNullOrWhiteSpace(rule.Group) ? "Standard" : rule.Group;
            string second = string.IsNullOrWhiteSpace(rule.Condition2) ? string.Empty : $" {rule.LogicalOperator.ToUpperInvariant()} {rule.Condition2}";
            string hold = rule.HoldSeconds > 0 ? $" · Sperre {rule.HoldSeconds}s" : string.Empty;
            string time = rule.Condition == "time.reached" ? $" · {rule.Time}" : string.Empty;
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRulesList.Items.Add(new ListBoxItem
            {
                Tag = rule.Id,
                Content = $"[{group}] P{rule.Priority} · {rule.Condition}{second}{time} → {rule.Profile} / {rule.Page}{delay}{hold}{fallback}{health}"
            });
        }
    }

    private async Task AddStreamDeckAutomationRuleAsync()
    {
        try
        {
            string condition = (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleConditionBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "stream.live";
            string condition2 = (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleCondition2Box.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
            string logicalOperator = (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleOperatorBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "and";
            string profile = string.IsNullOrWhiteSpace(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleProfileBox.Text) ? "Standard" : ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleProfileBox.Text.Trim();
            string page = string.IsNullOrWhiteSpace(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRulePageBox.Text) ? "Hauptseite" : ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRulePageBox.Text.Trim();
            if (!int.TryParse(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRulePriorityBox.Text, out int priority) || priority is < 0 or > 1000)
            {
                throw new InvalidOperationException("Die Regelpriorität muss zwischen 0 und 1000 liegen.");
            }

            if (!int.TryParse(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleDelayBox.Text, out int delay) || delay is < 0 or > 3600)
            {
                throw new InvalidOperationException("Die Verzögerung muss zwischen 0 und 3600 Sekunden liegen.");
            }

            if (!int.TryParse(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleHoldBox.Text, out int hold) || hold is < 0 or > 3600)
            {
                throw new InvalidOperationException("Die Sperrzeit muss zwischen 0 und 3600 Sekunden liegen.");
            }

            if (condition == "time.reached" && !TimeOnly.TryParse(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleTimeBox.Text.Trim(), out _))
            {
                throw new InvalidOperationException("Die Uhrzeit muss im Format HH:mm eingetragen werden.");
            }

            List<StreamDeckAutomationRule> rules = LoadStreamDeckAutomationRules();
            string group = string.IsNullOrWhiteSpace(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleGroupBox.Text) ? "Standard" : ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleGroupBox.Text.Trim();
            string days = string.IsNullOrWhiteSpace(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleDaysBox.Text) ? "Mo,Di,Mi,Do,Fr,Sa,So" : ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleDaysBox.Text.Trim();
            string window = string.IsNullOrWhiteSpace(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleWindowBox.Text) ? "00:00-23:59" : ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleWindowBox.Text.Trim();
            if (!IsValidStreamDeckRuleWindow(window))
            {
                throw new InvalidOperationException("Der Aktivitätszeitraum muss im Format HH:mm-HH:mm eingetragen werden.");
            }

            rules.Add(new StreamDeckAutomationRule { Condition = condition, Condition2 = condition2, LogicalOperator = logicalOperator, Profile = profile, Page = page, Priority = priority, DelaySeconds = delay, HoldSeconds = hold, Time = ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleTimeBox.Text.Trim(), IsFallback = ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleFallbackBox.IsChecked == true, Group = group, ActiveDays = days, ActiveWindow = window });
            await SaveStreamDeckAutomationRulesAsync(rules);
            RefreshStreamDeckAutomationRules();
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = $"Regel gespeichert: {condition} → {profile} / {page}";
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Foreground = Brushes.LightGreen;
        }
        catch (Exception ex)
        {
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = ex.Message;
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Foreground = Brushes.IndianRed;
        }
    }

    private void DeleteSelectedStreamDeckAutomationRule()
    {
        if (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRulesList.SelectedItem is not ListBoxItem item || item.Tag is not string id) { ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = "Bitte zuerst eine Regel auswählen."; return; }
        List<StreamDeckAutomationRule> rules = LoadStreamDeckAutomationRules();
        rules.RemoveAll(rule => string.Equals(rule.Id, id, StringComparison.OrdinalIgnoreCase));
        SaveStreamDeckAutomationRulesAsync(rules).GetAwaiter().GetResult();
        _streamDeckRuleFirstMatch.Remove(id);
        RefreshStreamDeckAutomationRules();
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = "Regel gelöscht.";
    }

    private bool IsStreamDeckConditionMatch(string condition, StreamDeckAutomationRule rule, Dictionary<string, bool> states)
    {
        return condition switch
        {
            "stream.live" => states.GetValueOrDefault("stream.live"),
            "stream.offline" => !states.GetValueOrDefault("stream.live"),
            "obs.connected" => states.GetValueOrDefault("obs.connected"),
            "obs.disconnected" => !states.GetValueOrDefault("obs.connected"),
            "spotify.playing" => states.GetValueOrDefault("spotify.playing"),
            "spotify.paused" => !states.GetValueOrDefault("spotify.playing"),
            "time.reached" => TimeOnly.TryParse(rule.Time, out TimeOnly target) && TimeOnly.FromDateTime(DateTime.Now).Hour == target.Hour && TimeOnly.FromDateTime(DateTime.Now).Minute == target.Minute,
            _ => false
        };
    }

    private bool IsStreamDeckRuleMatch(StreamDeckAutomationRule rule, Dictionary<string, bool> states)
    {
        bool first = IsStreamDeckConditionMatch(rule.Condition, rule, states);
        if (string.IsNullOrWhiteSpace(rule.Condition2))
        {
            return first;
        }

        bool second = IsStreamDeckConditionMatch(rule.Condition2, rule, states);
        return string.Equals(rule.LogicalOperator, "or", StringComparison.OrdinalIgnoreCase) ? first || second : first && second;
    }

    private static bool IsValidStreamDeckRuleWindow(string value)
    {
        string[] parts = value.Split('-', StringSplitOptions.TrimEntries);
        return parts.Length == 2 && TimeOnly.TryParse(parts[0], out _) && TimeOnly.TryParse(parts[1], out _);
    }

    private static bool IsStreamDeckRuleScheduleActive(StreamDeckAutomationRule rule, DateTime now)
    {
        string day = now.DayOfWeek switch { DayOfWeek.Monday => "Mo", DayOfWeek.Tuesday => "Di", DayOfWeek.Wednesday => "Mi", DayOfWeek.Thursday => "Do", DayOfWeek.Friday => "Fr", DayOfWeek.Saturday => "Sa", _ => "So" };
        string[] days = (rule.ActiveDays ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (days.Length > 0 && !days.Contains(day, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] parts = (rule.ActiveWindow ?? "00:00-23:59").Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !TimeOnly.TryParse(parts[0], out TimeOnly start) || !TimeOnly.TryParse(parts[1], out TimeOnly end))
        {
            return true;
        }

        var current = TimeOnly.FromDateTime(now);
        return start <= end ? current >= start && current <= end : current >= start || current <= end;
    }

    private void AddStreamDeckRuleHistory(string message)
    {
        _streamDeckRuleHistory.Insert(0, $"{DateTime.Now:HH:mm:ss} · {message}");
        if (_streamDeckRuleHistory.Count > 30)
        {
            _streamDeckRuleHistory.RemoveRange(30, _streamDeckRuleHistory.Count - 30);
        }

        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleHistoryBox?.Text = string.Join(Environment.NewLine, _streamDeckRuleHistory);
    }

    private void TestStreamDeckAutomationRules()
    {
        List<StreamDeckAutomationRule> rules = LoadStreamDeckAutomationRules();
        var issues = new List<string>();
        foreach (StreamDeckAutomationRule rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Profile) || string.IsNullOrWhiteSpace(rule.Page))
            {
                issues.Add($"{rule.Id}: Zielprofil oder Zielseite fehlt.");
            }

            if (rule.Priority is < 0 or > 1000)
            {
                issues.Add($"{rule.Id}: Priorität außerhalb 0–1000.");
            }

            if (rule.DelaySeconds is < 0 or > 3600 || rule.HoldSeconds is < 0 or > 3600)
            {
                issues.Add($"{rule.Id}: Verzögerung oder Sperrzeit ungültig.");
            }

            if (rule.Condition == "time.reached" && !TimeOnly.TryParse(rule.Time, out _))
            {
                issues.Add($"{rule.Id}: Uhrzeit ungültig.");
            }

            if (!IsValidStreamDeckRuleWindow(rule.ActiveWindow))
            {
                issues.Add($"{rule.Id}: Aktivitätszeitraum ungültig.");
            }

            if (string.IsNullOrWhiteSpace(rule.Group))
            {
                issues.Add($"{rule.Id}: Regelgruppe fehlt.");
            }
        }
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = issues.Count == 0 ? $"Regeltest erfolgreich: {rules.Count} Regel(n) sind formal gültig." : string.Join(Environment.NewLine, issues);
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Foreground = issues.Count == 0 ? Brushes.LightGreen : Brushes.IndianRed;
    }

    private async Task EvaluateStreamDeckAutomationRulesAsync(bool showConfirmation, bool previewOnly = false)
    {
        if (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckAutomationManualLockBox?.IsChecked == true)
        {
            if (showConfirmation)
            {
                ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = "Automatische Umschaltung ist manuell gesperrt.";
            }

            AddStreamDeckRuleHistory("Auswertung übersprungen: manuelle Sperre aktiv");
            return;
        }

        List<StreamDeckAutomationRule> allRules = LoadStreamDeckAutomationRules();
        var rules = allRules.Where(r => r.Enabled && IsStreamDeckRuleScheduleActive(r, DateTime.Now)).OrderByDescending(r => r.Priority).ToList();
        if (rules.Count == 0) { if (showConfirmation) { ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = "Es sind keine aktuell aktiven Automatikregeln vorhanden."; } return; }
        Dictionary<string, bool> states = GetStreamDeckRuntimeStates();
        DateTimeOffset now = DateTimeOffset.Now;
        StreamDeckAutomationRule? winner = null;
        foreach (StreamDeckAutomationRule? rule in rules)
        {
            rule.LastEvaluatedAt = now;
            bool matched = IsStreamDeckRuleMatch(rule, states);
            if (!matched) { _streamDeckRuleFirstMatch.Remove(rule.Id); continue; }
            rule.MatchCount++;
            if (!_streamDeckRuleFirstMatch.TryGetValue(rule.Id, out DateTimeOffset firstMatch)) { _streamDeckRuleFirstMatch[rule.Id] = now; firstMatch = now; }
            if ((now - firstMatch).TotalSeconds < rule.DelaySeconds)
            {
                continue;
            }

            winner = rule;
            break;
        }
        winner ??= rules.FirstOrDefault(r => r.IsFallback);
        if (winner is null)
        {
            await SaveStreamDeckAutomationRulesAsync(allRules);
            if (showConfirmation)
            {
                ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = "Keine Regel trifft aktuell zu.";
            }

            AddStreamDeckRuleHistory("Keine passende Regel");
            return;
        }
        StreamDeckAutomationRule? lastApplied = allRules.Where(r => r.LastAppliedAt.HasValue).MaxBy(r => r.LastAppliedAt);
        if (lastApplied?.LastAppliedAt is DateTimeOffset last && (now - last).TotalSeconds < lastApplied.HoldSeconds && !string.Equals(lastApplied.Id, winner.Id, StringComparison.OrdinalIgnoreCase))
        {
            await SaveStreamDeckAutomationRulesAsync(allRules);
            if (showConfirmation)
            {
                ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = $"Regelwechsel gesperrt: {lastApplied.Profile} / {lastApplied.Page} bleibt noch {Math.Ceiling(lastApplied.HoldSeconds - (now - last).TotalSeconds)} Sekunden aktiv.";
            }

            return;
        }
        if (previewOnly)
        {
            await SaveStreamDeckAutomationRulesAsync(allRules);
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = $"Vorschau: {winner.Profile} / {winner.Page} würde durch {winner.Condition}{(string.IsNullOrWhiteSpace(winner.Condition2) ? string.Empty : $" {winner.LogicalOperator.ToUpperInvariant()} {winner.Condition2}")} aktiviert.";
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Foreground = Brushes.LightSkyBlue;
            AddStreamDeckRuleHistory($"Vorschau: [{winner.Group}] {winner.Profile} / {winner.Page}");
            return;
        }
        try
        {
            string stateFile = StreamDeckStateFile;
            string current = File.Exists(stateFile) ? File.ReadAllText(stateFile) : string.Empty;
            if (current.Contains($"\"activeProfile\": \"{winner.Profile}\"", StringComparison.OrdinalIgnoreCase) && current.Contains($"\"activePage\": \"{winner.Page}\"", StringComparison.OrdinalIgnoreCase))
            {
                winner.ConsecutiveFailures = 0;
                winner.LastError = string.Empty;
                await SaveStreamDeckAutomationRulesAsync(allRules);
                if (showConfirmation)
                {
                    ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = $"Bereits aktiv: {winner.Profile} / {winner.Page}";
                }

                return;
            }
            if (File.Exists(stateFile))
            {
                File.Copy(stateFile, StreamDeckStableStateFile, true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
            File.WriteAllText(stateFile, JsonSerializer.Serialize(new { activeProfile = winner.Profile, activePage = winner.Page, changedAt = now, changedBy = "automation", ruleId = winner.Id }, new JsonSerializerOptions { WriteIndented = true }));
            winner.LastAppliedAt = now;
            winner.SuccessCount++;
            winner.ConsecutiveFailures = 0;
            winner.LastError = string.Empty;
            await SaveStreamDeckAutomationRulesAsync(allRules);
            string message = $"Automatisch aktiviert: {winner.Profile} / {winner.Page} ({winner.Condition}, Priorität {winner.Priority})";
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = message;
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Foreground = Brushes.LightGreen;
            AddStreamDeckRuleHistory($"Aktiviert: [{winner.Group}] {winner.Profile} / {winner.Page}");
            if (showConfirmation || ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleNotifyOnSwitchBox?.IsChecked == true)
            {
                ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = message;
            }
        }
        catch (Exception ex)
        {
            winner.FailureCount++;
            winner.ConsecutiveFailures++;
            winner.LastError = ex.Message;
            int threshold = int.TryParse(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleFailureThresholdBox?.Text, out int parsed) ? Math.Clamp(parsed, 1, 100) : 3;
            if (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleAutoDisableBox?.IsChecked == true && winner.ConsecutiveFailures >= threshold)
            {
                winner.Enabled = false;
                winner.DisabledReason = $"Automatisch nach {winner.ConsecutiveFailures} Fehlern deaktiviert";
            }
            await SaveStreamDeckAutomationRulesAsync(allRules);
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = $"Regelfehler [{winner.Group}]: {ex.Message}";
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Foreground = Brushes.IndianRed;
            AddStreamDeckRuleHistory($"FEHLER: [{winner.Group}] {ex.Message}");
            RefreshStreamDeckAutomationRules();
        }
    }

    private async Task SaveSelectedStreamDeckRuleTemplateAsync()
    {
        if (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRulesList.SelectedItem is not ListBoxItem item || item.Tag is not string id) { ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = "Bitte zuerst eine Regel auswählen."; return; }
        StreamDeckAutomationRule? rule = LoadStreamDeckAutomationRules().FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
        if (rule is null)
        {
            return;
        }

        List<StreamDeckAutomationRule> templates = LoadStreamDeckRuleTemplates();
        StreamDeckAutomationRule clone = JsonSerializer.Deserialize<StreamDeckAutomationRule>(JsonSerializer.Serialize(rule)) ?? new StreamDeckAutomationRule();
        clone.Id = Guid.NewGuid().ToString("N");
        clone.LastAppliedAt = null;
        templates.Add(clone);
        await File.WriteAllTextAsync(StreamDeckRuleTemplatesFile, JsonSerializer.Serialize(templates, new JsonSerializerOptions { WriteIndented = true }));
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = $"Regelvorlage gespeichert: [{clone.Group}] {clone.Condition} → {clone.Profile} / {clone.Page}";
    }

    private List<StreamDeckAutomationRule> LoadStreamDeckRuleTemplates()
    {
        try { return File.Exists(StreamDeckRuleTemplatesFile) ? JsonSerializer.Deserialize<List<StreamDeckAutomationRule>>(File.ReadAllText(StreamDeckRuleTemplatesFile)) ?? [] : []; }
        catch { return []; }
    }

    private async Task LoadStreamDeckRuleTemplateAsync()
    {
        StreamDeckAutomationRule? template = LoadStreamDeckRuleTemplates().LastOrDefault();
        if (template is null) { ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = "Es ist noch keine Regelvorlage gespeichert."; return; }
        List<StreamDeckAutomationRule> rules = LoadStreamDeckAutomationRules();
        template.Id = Guid.NewGuid().ToString("N"); template.LastAppliedAt = null;
        rules.Add(template); await SaveStreamDeckAutomationRulesAsync(rules); RefreshStreamDeckAutomationRules();
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = $"Letzte Regelvorlage geladen: {template.Profile} / {template.Page}";
    }

    private void ExportStreamDeckRuleSet()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Stream-Deck-Regelset (*.sdrules)|*.sdrules", FileName = "streamdeck-regelset.sdrules" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(LoadStreamDeckAutomationRules(), new JsonSerializerOptions { WriteIndented = true }));
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = $"Regelset exportiert: {dialog.FileName}";
    }

    private async Task ImportStreamDeckRuleSetAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Stream-Deck-Regelset (*.sdrules)|*.sdrules|JSON (*.json)|*.json" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            List<StreamDeckAutomationRule> imported = JsonSerializer.Deserialize<List<StreamDeckAutomationRule>>(File.ReadAllText(dialog.FileName)) ?? [];
            foreach (StreamDeckAutomationRule rule in imported) { rule.Id = Guid.NewGuid().ToString("N"); rule.LastAppliedAt = null; }
            List<StreamDeckAutomationRule> rules = LoadStreamDeckAutomationRules(); rules.AddRange(imported); await SaveStreamDeckAutomationRulesAsync(rules); RefreshStreamDeckAutomationRules();
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = $"{imported.Count} Regel(n) importiert.";
        }
        catch (Exception ex) { ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = $"Import fehlgeschlagen: {ex.Message}"; ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Foreground = Brushes.IndianRed; }
    }

    private void AnalyzeStreamDeckRuleConflicts()
    {
        var rules = LoadStreamDeckAutomationRules().Where(r => r.Enabled).ToList();
        var conflicts = new List<string>();
        for (int i = 0; i < rules.Count; i++)
        {
            for (int j = i + 1; j < rules.Count; j++)
            {
                StreamDeckAutomationRule a = rules[i]; StreamDeckAutomationRule b = rules[j];
                if (a.Priority != b.Priority || a.IsFallback || b.IsFallback)
                {
                    continue;
                }

                bool sameCondition = string.Equals(a.Condition, b.Condition, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Condition2, b.Condition2, StringComparison.OrdinalIgnoreCase) && string.Equals(a.LogicalOperator, b.LogicalOperator, StringComparison.OrdinalIgnoreCase);
                if (sameCondition && (!string.Equals(a.Profile, b.Profile, StringComparison.OrdinalIgnoreCase) || !string.Equals(a.Page, b.Page, StringComparison.OrdinalIgnoreCase)))
                {
                    conflicts.Add($"P{a.Priority}: [{a.Group}] {a.Profile}/{a.Page} kollidiert mit [{b.Group}] {b.Profile}/{b.Page}.");
                }
            }
        }

        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = conflicts.Count == 0 ? "Konfliktanalyse abgeschlossen: keine direkten Prioritätskonflikte gefunden." : string.Join(Environment.NewLine, conflicts);
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Foreground = conflicts.Count == 0 ? Brushes.LightGreen : Brushes.Orange;
    }

    private void RestoreStableStreamDeckState()
    {
        if (!File.Exists(StreamDeckStableStateFile)) { ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = "Es wurde noch kein stabiler Stream-Deck-Zustand gespeichert."; return; }
        File.Copy(StreamDeckStableStateFile, StreamDeckStateFile, true);
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = "Letztes stabiles Profil und letzte stabile Seite wurden wiederhergestellt.";
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Foreground = Brushes.LightGreen;
        AddStreamDeckRuleHistory("Stabiler Zustand manuell wiederhergestellt");
    }


    private void ShowStreamDeckRuleStatistics()
    {
        List<StreamDeckAutomationRule> rules = LoadStreamDeckAutomationRules();
        if (rules.Count == 0) { ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = "Keine Regeln für eine Statistik vorhanden."; return; }
        int enabled = rules.Count(r => r.Enabled);
        int matches = rules.Sum(r => r.MatchCount);
        int successes = rules.Sum(r => r.SuccessCount);
        int failures = rules.Sum(r => r.FailureCount);
        StreamDeckAutomationRule? mostUsed = rules.OrderByDescending(r => r.SuccessCount).FirstOrDefault();
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = $"Regelstatistik: {enabled}/{rules.Count} aktiv · Treffer {matches} · Umschaltungen {successes} · Fehler {failures}" +
            (mostUsed is null ? string.Empty : $" · Häufigste Regel: [{mostUsed.Group}] {mostUsed.Profile}/{mostUsed.Page} ({mostUsed.SuccessCount})");
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Foreground = failures == 0 ? Brushes.LightGreen : Brushes.Orange;
    }

    private async Task ResetStreamDeckRuleStatisticsAsync()
    {
        List<StreamDeckAutomationRule> rules = LoadStreamDeckAutomationRules();
        foreach (StreamDeckAutomationRule rule in rules)
        {
            rule.MatchCount = 0; rule.SuccessCount = 0; rule.FailureCount = 0; rule.ConsecutiveFailures = 0;
            rule.LastError = string.Empty; rule.LastEvaluatedAt = null;
        }
        await SaveStreamDeckAutomationRulesAsync(rules);
        RefreshStreamDeckAutomationRules();
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = "Ausführungsstatistik und Fehlerzähler wurden zurückgesetzt.";
    }

    private void ExportStreamDeckRuleDiagnostics()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Diagnosebericht (*.json)|*.json", FileName = $"streamdeck-regeldiagnose-{DateTime.Now:yyyyMMdd-HHmmss}.json" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        List<StreamDeckAutomationRule> rules = LoadStreamDeckAutomationRules();
        var report = new
        {
            generatedAt = DateTimeOffset.Now,
            suiteVersion = "6.5.0",
            automationLocked = ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckAutomationManualLockBox?.IsChecked == true,
            summary = new { total = rules.Count, enabled = rules.Count(r => r.Enabled), matches = rules.Sum(r => r.MatchCount), successes = rules.Sum(r => r.SuccessCount), failures = rules.Sum(r => r.FailureCount) },
            rules,
            recentDecisions = _streamDeckRuleHistory.TakeLast(30).ToArray()
        };
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = $"Diagnosebericht exportiert: {dialog.FileName}";
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Foreground = Brushes.LightGreen;
    }

    private async Task CreateStreamDeckActionAsync()
    {
        try
        {
            string title = string.IsNullOrWhiteSpace(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionTitleBox.Text) ? "Neue Aktion" : ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionTitleBox.Text.Trim();
            var item = ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCommandBox.SelectedItem as ComboBoxItem;
            string command = item?.Tag?.ToString() ?? "workflow.prepare";
            string parameter = ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionParameterBox.Text.Trim();
            string profile = string.IsNullOrWhiteSpace(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckProfileNameBox.Text) ? "Standard" : ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckProfileNameBox.Text.Trim();
            string page = string.IsNullOrWhiteSpace(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckPageNameBox.Text) ? "Hauptseite" : ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckPageNameBox.Text.Trim();
            string condition = (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckStateConditionBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
            string trueLabel = ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckTrueLabelBox.Text.Trim();
            string falseLabel = ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckFalseLabelBox.Text.Trim();
            bool toggleMode = ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckToggleModeBox.IsChecked == true;
            string alternateCommand = (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckAlternateCommandBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
            string alternateParameter = ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckAlternateParameterBox.Text.Trim();
            if (!int.TryParse(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckStepDelayBox.Text, out int stepDelayMs) || stepDelayMs < 0 || stepDelayMs > 10000)
            {
                throw new InvalidOperationException("Die Schrittverzögerung muss zwischen 0 und 10000 ms liegen.");
            }

            if (!int.TryParse(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRetryCountBox.Text, out int retryCount) || retryCount < 0 || retryCount > 5)
            {
                throw new InvalidOperationException("Die Wiederholungszahl muss zwischen 0 und 5 liegen.");
            }

            if (!int.TryParse(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckCooldownBox.Text, out int cooldownMs) || cooldownMs < 0 || cooldownMs > 60000)
            {
                throw new InvalidOperationException("Die Tastensperre muss zwischen 0 und 60000 ms liegen.");
            }

            if (toggleMode && string.IsNullOrWhiteSpace(condition))
            {
                throw new InvalidOperationException("Für eine Toggle-Taste muss eine Zustandsbindung ausgewählt werden.");
            }

            if (!int.TryParse(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckSlotBox.Text, out int slot) || slot < 1 || slot > 32)
            {
                throw new InvalidOperationException("Die Position muss zwischen 1 und 32 liegen.");
            }

            var steps = new List<(string Command, string Parameter)>();
            foreach (string line in ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckMultiActionBox.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split('|', 2);
                string stepCommand = parts[0].Trim();
                if (string.IsNullOrWhiteSpace(stepCommand))
                {
                    continue;
                }

                steps.Add((stepCommand, parts.Length > 1 ? parts[1].Trim() : string.Empty));
            }
            if (steps.Count == 0)
            {
                steps.Add((command, parameter));
            }

            if (steps.Count > 20)
            {
                throw new InvalidOperationException("Eine Mehrfachaktion darf höchstens 20 Schritte enthalten.");
            }

            string safeName = string.Concat(title.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).Trim();
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "Neue Aktion";
            }

            Directory.CreateDirectory(StreamDeckActionsDirectory);
            string clientPath = Path.Combine(AppContext.BaseDirectory, "CreatorControlSuite.CommandClient.exe");
            string cmdPath = Path.Combine(StreamDeckActionsDirectory, safeName + ".cmd");
            var content = new StringBuilder("@echo off\r\n");
            if (toggleMode)
            {
                string stateExpression = condition switch
                {
                    "stream.live" => "$s.stream.isLive",
                    "obs.connected" => "$s.obs.connected",
                    "spotify.playing" => "$s.spotify.isPlaying",
                    _ => "$false"
                };
                content.AppendLine($"powershell -NoProfile -ExecutionPolicy Bypass -Command \"$s=Get-Content -Raw '{StreamDeckRuntimeStateFile.Replace("'", "''")}'|ConvertFrom-Json; if({stateExpression}){{exit 0}}else{{exit 1}}\"");
                content.AppendLine("if errorlevel 1 goto stateoff");
                string alternateArgs = string.IsNullOrWhiteSpace(alternateParameter)
                    ? alternateCommand
                    : FormatStreamDeckCommandArgs(alternateCommand, alternateParameter);
                content.AppendLine($"start \"\" /wait /min \"{clientPath}\" {alternateArgs}");
                content.AppendLine("goto end");
                content.AppendLine(":stateoff");
            }
            int stepNumber = 0;
            foreach ((string Command, string Parameter) in steps)
            {
                stepNumber++;
                string args = FormatStreamDeckCommandArgs(Command, Parameter);
                string successLabel = $"step_{stepNumber}_ok";
                for (int attempt = 0; attempt <= retryCount; attempt++)
                {
                    content.AppendLine($"start \"\" /wait /min \"{clientPath}\" {args}");
                    content.AppendLine($"if not errorlevel 1 goto {successLabel}");
                }
                content.AppendLine($":{successLabel}");
                if (stepDelayMs > 0)
                {
                    content.AppendLine($"powershell -NoProfile -Command \"Start-Sleep -Milliseconds {stepDelayMs}\"");
                }
            }
            if (toggleMode)
            {
                content.AppendLine(":end");
            }

            if (cooldownMs > 0)
            {
                content.AppendLine($"powershell -NoProfile -Command \"Start-Sleep -Milliseconds {cooldownMs}\"");
            }

            await File.WriteAllTextAsync(cmdPath, content.ToString());
            var meta = new { title, command = steps[0].Command, parameter = steps[0].Parameter, profile, page, slot, steps = steps.Select(step => new { command = step.Command, parameter = step.Parameter }).ToArray(), locked = false, condition, trueLabel, falseLabel, toggleMode, alternateCommand, alternateParameter, stepDelayMs, retryCount, cooldownMs, createdAt = DateTimeOffset.Now };
            await File.WriteAllTextAsync(Path.ChangeExtension(cmdPath, ".json"), System.Text.Json.JsonSerializer.Serialize(meta, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = $"Aktionstaste erstellt: {cmdPath}";
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Foreground = new SolidColorBrush(Color.FromRgb(92, 184, 92));
            RefreshStreamDeckActionsList();
        }
        catch (Exception ex)
        {
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = ex.Message;
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Foreground = new SolidColorBrush(Color.FromRgb(220, 90, 90));
        }
    }

    private static string FormatStreamDeckCommandArgs(string command, string parameter)
    {
        if (string.IsNullOrWhiteSpace(parameter))
        {
            return command;
        }

        string key = command switch
        {
            "spotify.volume" => "volume",
            "spotify.playlist" => "uri",
            "obs.scene" => "scene",
            "obs.mute" => "input",
            "alert.test" or "alerts.test" => "type",
            _ => "value"
        };

        return $"{command} {key}=\"{parameter.Replace("\"", "\"\"")}\"";
    }

    private void OpenStreamDeckActionsFolder()
    {
        Directory.CreateDirectory(StreamDeckActionsDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", StreamDeckActionsDirectory) { UseShellExecute = true });
    }

    private void DeleteSelectedStreamDeckAction()
    {
        if (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckCreatedActionsList.SelectedItem is not ListBoxItem item || item.Tag is not string file)
        {
            return;
        }

        if (File.Exists(file))
        {
            File.Delete(file);
        }

        string json = Path.ChangeExtension(file, ".json");
        if (File.Exists(json))
        {
            File.Delete(json);
        }

        RefreshStreamDeckActionsList();
    }

    private async Task ExportStreamDeckProfileAsync()
    {
        try
        {
            StreamDeckProfilePackage package =
                await _streamDeckModule.BuildDefaultProfileAsync();

            SettingsPageViewHost.StreamDeckStatusText.Text =
                "Profil exportiert: " + package.Path;

            SettingsPageViewHost.StreamDeckStatusText.Foreground =
                System.Windows.Media.Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            SettingsPageViewHost.StreamDeckStatusText.Text = exception.Message;
            SettingsPageViewHost.StreamDeckStatusText.Foreground =
                System.Windows.Media.Brushes.IndianRed;
        }
    }
}
