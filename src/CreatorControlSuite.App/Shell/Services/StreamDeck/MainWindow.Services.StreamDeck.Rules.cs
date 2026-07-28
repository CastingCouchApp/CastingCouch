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
            if (!StreamDeckAutomationRuleService.IsValidWindow(window))
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
        IReadOnlyList<string> issues =
            StreamDeckAutomationRuleService.Validate(rules);
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
        var rules = allRules
            .Where(rule =>
                rule.Enabled &&
                StreamDeckAutomationRuleService.IsScheduleActive(
                    rule,
                    DateTime.Now))
            .OrderByDescending(rule => rule.Priority)
            .ToList();
        if (rules.Count == 0) { if (showConfirmation) { ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRuleStatusText.Text = "Es sind keine aktuell aktiven Automatikregeln vorhanden."; } return; }
        Dictionary<string, bool> states = GetStreamDeckRuntimeStates();
        DateTimeOffset now = DateTimeOffset.Now;
        StreamDeckAutomationRule? winner = null;
        foreach (StreamDeckAutomationRule? rule in rules)
        {
            rule.LastEvaluatedAt = now;
            bool matched = StreamDeckAutomationRuleService.IsRuleMatch(
                rule,
                states,
                DateTime.Now);
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

}
