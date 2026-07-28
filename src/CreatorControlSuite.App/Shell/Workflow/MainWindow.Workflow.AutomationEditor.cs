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
    private void RefreshTimedAutomationRules()
    {
        if (WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationRulesList is null)
        {
            return;
        }

        _timedAutomationRules.Clear();
        foreach (TimedAutomationRuleSettings rule in _settings.Workflow.TimedAutomations)
        {
            _timedAutomationRules.Add(rule);
        }

        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationNextRuleBox?.ItemsSource = _timedAutomationRules.ToList();

        if (_timedAutomationRules.Count > 0 && WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationRulesList.SelectedItem is null)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationRulesList.SelectedIndex = 0;
        }
    }

    private static string ComboTag(ComboBox box, string fallback)
        => (box.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private async Task ExportTimedAutomationsAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Automatisierungsregeln exportieren",
            Filter = "Creator-Control-Automationen (*.ccsautomation.json)|*.ccsautomation.json|JSON (*.json)|*.json",
            FileName = $"CreatorControlSuite-Automationen-{DateTime.Now:yyyyMMdd-HHmm}.ccsautomation.json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var package = new TimedAutomationExportPackage
        {
            ExportedAt = DateTimeOffset.Now,
            Rules = [.. _timedAutomationRules.Select(CloneTimedAutomationRule)]
        };
        await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(package, new JsonSerializerOptions { WriteIndented = true }));
        AddTimedAutomationDiagnostic($"Exportiert: {package.Rules.Count} Regeln nach {Path.GetFileName(dialog.FileName)}.");
    }

    private async Task ImportTimedAutomationsAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Automatisierungsregeln importieren",
            Filter = "Creator-Control-Automationen (*.ccsautomation.json;*.json)|*.ccsautomation.json;*.json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            TimedAutomationExportPackage? package = JsonSerializer.Deserialize<TimedAutomationExportPackage>(await File.ReadAllTextAsync(dialog.FileName), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (package?.Rules is null || package.Rules.Count == 0)
            {
                throw new InvalidDataException("Die Datei enthält keine Regeln.");
            }

            var idMap = package.Rules.ToDictionary(x => x.Id, _ => Guid.NewGuid().ToString("N"), StringComparer.OrdinalIgnoreCase);
            foreach (TimedAutomationRuleSettings imported in package.Rules)
            {
                TimedAutomationRuleSettings clone = CloneTimedAutomationRule(imported);
                clone.Id = idMap[imported.Id];
                clone.Name = EnsureUniqueAutomationName(clone.Name);
                clone.NextRuleId = !string.IsNullOrWhiteSpace(imported.NextRuleId) && idMap.TryGetValue(imported.NextRuleId, out string? nextId) ? nextId : "";
                clone.DependencyRuleId = !string.IsNullOrWhiteSpace(imported.DependencyRuleId) && idMap.TryGetValue(imported.DependencyRuleId, out string? dependencyId) ? dependencyId : "";
                clone.FailureRuleId = !string.IsNullOrWhiteSpace(imported.FailureRuleId) && idMap.TryGetValue(imported.FailureRuleId, out string? failureId) ? failureId : "";
                clone.RollbackRuleId = !string.IsNullOrWhiteSpace(imported.RollbackRuleId) && idMap.TryGetValue(imported.RollbackRuleId, out string? rollbackId) ? rollbackId : "";
                _settings.Workflow.TimedAutomations.Add(clone);
            }
            await _settingsStore.SaveAsync(_settings);
            RefreshTimedAutomationRules();
            AddTimedAutomationDiagnostic($"Importiert: {package.Rules.Count} Regeln aus {Path.GetFileName(dialog.FileName)}.");
            ValidateTimedAutomationRules();
        }
        catch (Exception ex)
        {
            AddTimedAutomationDiagnostic($"Import fehlgeschlagen: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Import fehlgeschlagen", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task AddTimedAutomationTemplateAsync()
    {
        MessageBoxResult result = MessageBox.Show(this,
            "Vorlage '10-Minuten-Streamstart' anlegen?\n\nSie erstellt verkettete Regeln: Streamstart (Spotify + Overlay-Countdown), nach 5 Minuten Intro-Quelle ausblenden und nach 10 Minuten auf die Game-Szene wechseln.",
            "Automationsvorlage", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var start = new TimedAutomationRuleSettings { Name = EnsureUniqueAutomationName("Streamstart – Initialisierung"), TriggerType = "StreamStarted", DelaySeconds = 0, ActionType = "SpotifyOnly", SpotifyAction = "Resume", OncePerStream = true };
        var countdown = new TimedAutomationRuleSettings
        {
            Name = EnsureUniqueAutomationName("Streamstart – Overlay-Countdown"),
            TriggerType = "StreamStarted",
            DelaySeconds = 0,
            ActionType = "OverlayCountdown",
            OverlayCountdownAction = "Start",
            OverlayCountdownSeconds = 0,
            OncePerStream = true
        };
        var intro = new TimedAutomationRuleSettings { Name = EnsureUniqueAutomationName("Streamstart – Intro ausblenden"), TriggerType = "StreamElapsed", DelaySeconds = 300, ActionType = "SetSourceVisibility", ObsScene = "Start", ObsSource = "Intro", SourceVisible = false, OncePerStream = true };
        var game = new TimedAutomationRuleSettings { Name = EnsureUniqueAutomationName("Startszene – nach 10 Minuten zu Game"), TriggerType = "SceneElapsed", TriggerScene = string.IsNullOrWhiteSpace(_settings.Obs.StartScene) ? "Start" : _settings.Obs.StartScene, DelaySeconds = 600, ActionType = "SwitchScene", TargetScene = string.IsNullOrWhiteSpace(_settings.Obs.LiveScene) ? "Game" : _settings.Obs.LiveScene, OncePerStream = true };
        _settings.Workflow.TimedAutomations.Add(start);
        _settings.Workflow.TimedAutomations.Add(countdown);
        _settings.Workflow.TimedAutomations.Add(intro);
        _settings.Workflow.TimedAutomations.Add(game);
        await _settingsStore.SaveAsync(_settings);
        RefreshTimedAutomationRules();
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationRulesList.SelectedItem = start;
        AddTimedAutomationDiagnostic("Vorlage angelegt: 10-Minuten-Streamstart. Szenen- und Quellnamen bitte prüfen.");
        ValidateTimedAutomationRules();
    }

    private string EnsureUniqueAutomationName(string baseName)
    {
        string name = string.IsNullOrWhiteSpace(baseName) ? "Importierte Automatisierung" : baseName.Trim();
        var existing = _settings.Workflow.TimedAutomations.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(name))
        {
            return name;
        }

        for (int i = 2; ; i++)
        {
            if (!existing.Contains($"{name} ({i})"))
            {
                return $"{name} ({i})";
            }
        }
    }

    private static TimedAutomationRuleSettings CloneTimedAutomationRule(TimedAutomationRuleSettings rule)
        => JsonSerializer.Deserialize<TimedAutomationRuleSettings>(JsonSerializer.Serialize(rule)) ?? new TimedAutomationRuleSettings();

    private static string DescribeTimedAutomationAction(TimedAutomationRuleSettings rule)
    {
        string action = rule.ActionType switch
        {
            "SwitchScene" => $"Szene '{rule.TargetScene}' aktivieren",
            "SetSourceVisibility" => $"Quelle '{rule.ObsSource}' in '{rule.ObsScene}' {(rule.SourceVisible ? "einblenden" : "ausblenden")}",
            "SetInputMute" => $"Audioquelle '{rule.ObsInput}' {(rule.InputMuted ? "muten" : "aktivieren")}",
            "StartObsStream" => "OBS-Stream starten",
            "StopObsStream" => "OBS-Stream stoppen",
            "StreamerBotAction" => $"Streamer.bot-Aktion '{rule.StreamerBotActionName}' ausführen",
            "OverlayCountdown" => string.Equals(rule.OverlayCountdownAction, "Stop", StringComparison.OrdinalIgnoreCase)
                ? "Overlay-Countdown stoppen"
                : $"Overlay-Countdown starten{(rule.OverlayCountdownSeconds > 0 ? $" ({rule.OverlayCountdownSeconds}s)" : "")}",
            _ => "keine OBS-Aktion"
        };
        if (!string.Equals(rule.SpotifyAction, "None", StringComparison.OrdinalIgnoreCase))
        {
            action += $", Spotify: {rule.SpotifyAction}";
        }

        return action;
    }

    private void CreateNewTimedAutomationRule()
    {
        var rule = new TimedAutomationRuleSettings();
        _settings.Workflow.TimedAutomations.Add(rule);
        _timedAutomationRules.Add(rule);
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationRulesList.SelectedItem = rule;
    }

    private void LoadSelectedTimedAutomationRule()
    {
        if (WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationRulesList.SelectedItem is not TimedAutomationRuleSettings rule)
        {
            return;
        }

        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationEnabledBox.IsChecked = rule.Enabled;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationNameBox.Text = rule.Name;
        SelectComboByTag(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTriggerTypeBox, rule.TriggerType);
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTriggerSceneBox.Text = rule.TriggerScene;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationDelayBox.Text = rule.DelaySeconds.ToString();
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationScheduleTimeBox.Text = rule.ScheduleTime;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationScheduleDaysBox.Text = rule.ScheduleDays;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationScheduleDateBox.Text = rule.ScheduleDate;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationActiveFromBox.Text = rule.ActiveFromDate;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationActiveUntilBox.Text = rule.ActiveUntilDate;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationExcludedDatesBox.Text = rule.ExcludedDates;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationBlackoutRangesBox.Text = rule.BlackoutRanges;
        SelectComboByTag(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationMissedRunBehaviorBox, rule.MissedRunBehavior);
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationCatchUpGraceBox.Text = rule.CatchUpGraceMinutes.ToString();
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationNextRunText.Text = $"Nächster geplanter Lauf: {DescribeNextScheduledRun(rule)}";
        SelectComboByTag(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationActionTypeBox, rule.ActionType);
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTargetSceneBox.Text = rule.TargetScene;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTransitionBox.Text = rule.TransitionName;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTransitionDurationBox.Text = rule.TransitionDurationMilliseconds.ToString();
        SelectComboByTag(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationOverlayCountdownActionBox, rule.OverlayCountdownAction);
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationOverlayCountdownSecondsBox.Text = Math.Max(0, rule.OverlayCountdownSeconds).ToString();
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSourceSceneBox.Text = rule.ObsScene;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSourceBox.Text = rule.ObsSource;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSourceVisibleBox.IsChecked = rule.SourceVisible;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationResetSourceBox.IsChecked = rule.ResetSourceAtStreamEnd;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationResetVisibleBox.IsChecked = rule.ResetSourceVisible;
        SelectComboByTag(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyActionBox, rule.SpotifyAction);
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyVolumeBox.Text = rule.SpotifyVolumePercent.ToString();
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyPlaylistUriBox.Text = rule.SpotifyPlaylistUri;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyPlaylistShuffleBox.IsChecked = rule.SpotifyPlaylistShuffle;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyDelayBox.Text = rule.SpotifyActionDelaySeconds.ToString();
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyFadeBox.Text = rule.SpotifyFadeSeconds.ToString();
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyPriorityBox.Text = rule.SpotifyPriority.ToString();
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyGroupBox.Text = string.IsNullOrWhiteSpace(rule.SpotifyAutomationGroup) ? "Standard" : rule.SpotifyAutomationGroup;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyExclusiveGroupBox.IsChecked = rule.SpotifyExclusiveGroup;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifySavePreviousBox.IsChecked = rule.SpotifySavePreviousState;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyAutoRestoreBox.IsChecked = rule.SpotifyAutoRestorePreviousState;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyAutoRestoreDelayBox.Text = rule.SpotifyAutoRestoreDelaySeconds.ToString();
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyAutoRestoreSameSceneBox.IsChecked = rule.SpotifyAutoRestoreRequireSameScene;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyAutoRestoreSameGroupBox.IsChecked = rule.SpotifyAutoRestoreRequireSameGroup;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyAutoRestoreUnchangedPlaybackBox.IsChecked = rule.SpotifyAutoRestoreRequireUnchangedPlayback;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationOncePerStreamBox.IsChecked = rule.OncePerStream;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationInputBox.Text = rule.ObsInput;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationInputMutedBox.IsChecked = rule.InputMuted;
        SelectStreamerBotAction(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationStreamerBotActionBox, rule.StreamerBotActionId, rule.StreamerBotActionName);
        SelectComboByTag(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationConditionTypeBox, rule.ConditionType);
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationConditionValueBox.Text = rule.ConditionValue;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationConditionNegatedBox.IsChecked = rule.ConditionNegated;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationNextRuleBox.ItemsSource = _timedAutomationRules.Where(x => !string.Equals(x.Id, rule.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationNextRuleBox.SelectedItem = _timedAutomationRules.FirstOrDefault(x => string.Equals(x.Id, rule.NextRuleId, StringComparison.OrdinalIgnoreCase));
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationNextRuleDelayBox.Text = rule.NextRuleDelaySeconds.ToString();
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationContinueChainOnErrorBox.IsChecked = rule.ContinueChainOnError;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationPriorityBox.Text = rule.Priority.ToString();
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTimeoutBox.Text = rule.TimeoutSeconds.ToString();
        SelectComboByTag(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationExecutionModeBox, rule.ExecutionMode);
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationDependencyRuleBox.ItemsSource = _timedAutomationRules.Where(x => !string.Equals(x.Id, rule.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationDependencyRuleBox.SelectedItem = _timedAutomationRules.FirstOrDefault(x => string.Equals(x.Id, rule.DependencyRuleId, StringComparison.OrdinalIgnoreCase));
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationRetryCountBox.Text = rule.RetryCount.ToString();
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationRetryDelayBox.Text = rule.RetryDelaySeconds.ToString();
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationFailureRuleBox.ItemsSource = _timedAutomationRules.Where(x => !string.Equals(x.Id, rule.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationFailureRuleBox.SelectedItem = _timedAutomationRules.FirstOrDefault(x => string.Equals(x.Id, rule.FailureRuleId, StringComparison.OrdinalIgnoreCase));
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationWorkflowGroupBox.Text = rule.WorkflowGroup;
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationWorkflowOrderBox.Text = rule.WorkflowOrder.ToString();
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationStartWorkflowBox.IsChecked = rule.StartWorkflowGroup;
        SelectComboByTag(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationWorkflowFailureModeBox, rule.WorkflowFailureMode);
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationRollbackRuleBox.ItemsSource = _timedAutomationRules.Where(x => !string.Equals(x.Id, rule.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationRollbackRuleBox.SelectedItem = _timedAutomationRules.FirstOrDefault(x => string.Equals(x.Id, rule.RollbackRuleId, StringComparison.OrdinalIgnoreCase));
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationHistoryText.Text = $"Letzter Lauf: {(string.IsNullOrWhiteSpace(rule.LastRunAt) ? "Noch nie" : rule.LastRunAt)} | Status: {rule.LastRunStatus} | Erfolgreich: {rule.SuccessfulRuns} | Fehler: {rule.FailedRuns} | Übersprungen: {rule.SkippedRuns}";
    }

    private static void SelectComboByTag(ComboBox box, string tag)
    {
        foreach (ComboBoxItem item in box.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase)) { box.SelectedItem = item; return; }
        }
    }

    private TimedAutomationRuleSettings ReadTimedAutomationEditor(TimedAutomationRuleSettings? target = null)
    {
        TimedAutomationRuleSettings rule = target ?? new TimedAutomationRuleSettings();
        rule.Enabled = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationEnabledBox.IsChecked == true;
        rule.Name = string.IsNullOrWhiteSpace(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationNameBox.Text) ? "Neue Automatisierung" : WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationNameBox.Text.Trim();
        rule.TriggerType = ComboTag(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTriggerTypeBox, "StreamElapsed");
        rule.TriggerScene = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTriggerSceneBox.Text.Trim();
        rule.DelaySeconds = int.TryParse(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationDelayBox.Text, out int delay) ? Math.Max(0, delay) : 10;
        rule.ScheduleTime = TimeOnly.TryParse(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationScheduleTimeBox.Text, out TimeOnly scheduleTime) ? scheduleTime.ToString("HH:mm") : "20:00";
        rule.ScheduleDays = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationScheduleDaysBox.Text.Trim();
        rule.ScheduleDate = DateOnly.TryParse(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationScheduleDateBox.Text, out DateOnly scheduleDate) ? scheduleDate.ToString("yyyy-MM-dd") : "";
        rule.ActiveFromDate = DateOnly.TryParse(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationActiveFromBox.Text, out DateOnly activeFrom) ? activeFrom.ToString("yyyy-MM-dd") : "";
        rule.ActiveUntilDate = DateOnly.TryParse(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationActiveUntilBox.Text, out DateOnly activeUntil) ? activeUntil.ToString("yyyy-MM-dd") : "";
        rule.ExcludedDates = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationExcludedDatesBox.Text.Trim();
        rule.BlackoutRanges = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationBlackoutRangesBox.Text.Trim();
        rule.MissedRunBehavior = ComboTag(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationMissedRunBehaviorBox, "SameDay");
        rule.CatchUpGraceMinutes = int.TryParse(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationCatchUpGraceBox.Text, out int graceMinutes) ? Math.Clamp(graceMinutes, 0, 1440) : 30;
        rule.ActionType = ComboTag(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationActionTypeBox, "SwitchScene");
        rule.TargetScene = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTargetSceneBox.Text.Trim();
        rule.TransitionName = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTransitionBox.Text.Trim();
        rule.TransitionDurationMilliseconds = int.TryParse(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTransitionDurationBox.Text, out int transitionMs) ? Math.Clamp(transitionMs, 50, 20000) : 1000;
        rule.OverlayCountdownAction = ComboTag(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationOverlayCountdownActionBox, "Start");
        rule.OverlayCountdownSeconds = int.TryParse(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationOverlayCountdownSecondsBox.Text, out int countdownSeconds)
            ? Math.Max(0, countdownSeconds)
            : 0;
        rule.ObsScene = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSourceSceneBox.Text.Trim();
        rule.ObsSource = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSourceBox.Text.Trim();
        rule.SourceVisible = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSourceVisibleBox.IsChecked == true;
        rule.ResetSourceAtStreamEnd = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationResetSourceBox.IsChecked == true;
        rule.ResetSourceVisible = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationResetVisibleBox.IsChecked == true;
        rule.SpotifyAction = ComboTag(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyActionBox, "None");
        rule.SpotifyVolumePercent = int.TryParse(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyVolumeBox.Text, out int volume) ? Math.Clamp(volume, 0, 100) : 35;
        rule.SpotifyPlaylistUri = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyPlaylistUriBox.Text.Trim();
        rule.SpotifyPlaylistShuffle = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyPlaylistShuffleBox.IsChecked == true;
        rule.SpotifyActionDelaySeconds = int.TryParse(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyDelayBox.Text, out int spotifyDelay) ? Math.Clamp(spotifyDelay, 0, 3600) : 0;
        rule.SpotifyFadeSeconds = int.TryParse(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyFadeBox.Text, out int spotifyFade) ? Math.Clamp(spotifyFade, 0, 120) : 0;
        rule.SpotifyPriority = int.TryParse(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyPriorityBox.Text, out int spotifyPriority) ? Math.Clamp(spotifyPriority, -1000, 1000) : 0;
        rule.SpotifyAutomationGroup = string.IsNullOrWhiteSpace(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyGroupBox.Text) ? "Standard" : WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyGroupBox.Text.Trim();
        rule.SpotifyExclusiveGroup = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyExclusiveGroupBox.IsChecked == true;
        rule.SpotifySavePreviousState = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifySavePreviousBox.IsChecked == true;
        rule.SpotifyAutoRestorePreviousState = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyAutoRestoreBox.IsChecked == true;
        rule.SpotifyAutoRestoreDelaySeconds = int.TryParse(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyAutoRestoreDelayBox.Text, out int spotifyAutoRestoreDelay) ? Math.Clamp(spotifyAutoRestoreDelay, 1, 86400) : 30;
        rule.SpotifyAutoRestoreRequireSameScene = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyAutoRestoreSameSceneBox.IsChecked == true;
        rule.SpotifyAutoRestoreRequireSameGroup = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyAutoRestoreSameGroupBox.IsChecked == true;
        rule.SpotifyAutoRestoreRequireUnchangedPlayback = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyAutoRestoreUnchangedPlaybackBox.IsChecked == true;
        rule.OncePerStream = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationOncePerStreamBox.IsChecked == true;
        rule.ObsInput = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationInputBox.Text.Trim();
        rule.InputMuted = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationInputMutedBox.IsChecked == true;
        var timedAction = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationStreamerBotActionBox.SelectedItem as StreamerBotActionOption;
        rule.StreamerBotActionId = timedAction?.Id ?? "";
        rule.StreamerBotActionName = timedAction?.Name ?? WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationStreamerBotActionBox.Text.Trim();
        rule.ConditionType = ComboTag(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationConditionTypeBox, "None");
        rule.ConditionValue = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationConditionValueBox.Text.Trim();
        rule.ConditionNegated = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationConditionNegatedBox.IsChecked == true;
        rule.NextRuleId = (WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationNextRuleBox.SelectedItem as TimedAutomationRuleSettings)?.Id ?? "";
        rule.NextRuleDelaySeconds = int.TryParse(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationNextRuleDelayBox.Text, out int nextDelay) ? Math.Clamp(nextDelay, 0, 86400) : 0;
        rule.ContinueChainOnError = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationContinueChainOnErrorBox.IsChecked == true;
        rule.Priority = int.TryParse(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationPriorityBox.Text, out int priority) ? Math.Clamp(priority, -1000, 1000) : 0;
        rule.TimeoutSeconds = int.TryParse(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTimeoutBox.Text, out int timeout) ? Math.Clamp(timeout, 1, 86400) : 60;
        rule.ExecutionMode = ComboTag(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationExecutionModeBox, "SkipIfRunning");
        rule.DependencyRuleId = (WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationDependencyRuleBox.SelectedItem as TimedAutomationRuleSettings)?.Id ?? "";
        rule.DependencyRequiredStatus = "Erfolgreich";
        rule.RetryCount = int.TryParse(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationRetryCountBox.Text, out int retryCount) ? Math.Clamp(retryCount, 0, 20) : 0;
        rule.RetryDelaySeconds = int.TryParse(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationRetryDelayBox.Text, out int retryDelay) ? Math.Clamp(retryDelay, 0, 3600) : 5;
        rule.FailureRuleId = (WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationFailureRuleBox.SelectedItem as TimedAutomationRuleSettings)?.Id ?? "";
        rule.WorkflowGroup = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationWorkflowGroupBox.Text.Trim();
        rule.WorkflowOrder = int.TryParse(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationWorkflowOrderBox.Text, out int workflowOrder) ? Math.Clamp(workflowOrder, -1000, 1000) : 0;
        rule.StartWorkflowGroup = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationStartWorkflowBox.IsChecked == true;
        rule.WorkflowFailureMode = ComboTag(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationWorkflowFailureModeBox, "Stop");
        rule.RollbackRuleId = (WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationRollbackRuleBox.SelectedItem as TimedAutomationRuleSettings)?.Id ?? "";
        return rule;
    }

    private async Task SaveTimedAutomationRuleAsync()
    {
        var selected = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationRulesList.SelectedItem as TimedAutomationRuleSettings;
        if (selected is null) { CreateNewTimedAutomationRule(); selected = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationRulesList.SelectedItem as TimedAutomationRuleSettings; }
        if (selected is null)
        {
            return;
        }

        ReadTimedAutomationEditor(selected);
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationRulesList.Items.Refresh();
        _settings.Workflow.TimedAutomations = [.. _timedAutomationRules.Select(rule => rule)];
        await _settingsStore.SaveAsync(_settings);
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTestStatusText.Text = "Regel gespeichert.";
    }

    private async Task DeleteSelectedTimedAutomationRuleAsync()
    {
        if (WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationRulesList.SelectedItem is not TimedAutomationRuleSettings rule)
        {
            return;
        }

        _settings.Workflow.TimedAutomations.Remove(rule); _timedAutomationRules.Remove(rule);
        await _settingsStore.SaveAsync(_settings);
    }

    private async Task RefreshTimedAutomationObsListsAsync(bool force = true)
    {
        if (_timedAutomationObsRefreshRunning)
        {
            return;
        }

        if (!force && DateTimeOffset.UtcNow - _lastTimedAutomationObsRefresh < TimeSpan.FromSeconds(3))
        {
            return;
        }

        if (!_obsClient.IsConnected)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTestStatusText.Text = "OBS ist nicht verbunden.";
            return;
        }

        _timedAutomationObsRefreshRunning = true;
        try
        {
            string previousTrigger = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTriggerSceneBox.Text;
            string previousTarget = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTargetSceneBox.Text;
            string previousSourceScene = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSourceSceneBox.Text;
            string previousTransition = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTransitionBox.Text;

            var scenes = (await _obsClient.GetSceneListAsync())
                .Select(x => x.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var transitions = (await _obsClient.GetSceneTransitionListAsync())
                .Select(x => x.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTriggerSceneBox.ItemsSource = scenes;
            WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTargetSceneBox.ItemsSource = scenes;
            WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSourceSceneBox.ItemsSource = scenes;
            WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTransitionBox.ItemsSource = transitions;

            WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTriggerSceneBox.Text = previousTrigger;
            WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTargetSceneBox.Text = previousTarget;
            WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSourceSceneBox.Text = previousSourceScene;
            WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTransitionBox.Text = previousTransition;

            var inputs = (await _obsClient.GetInputListAsync())
                .Select(x => x.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            string previousInput = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationInputBox.Text;
            WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationInputBox.ItemsSource = inputs;
            WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationInputBox.Text = previousInput;
            await RefreshStreamerBotActionsAsync(false);
            WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTestStatusText.Text = $"{scenes.Count} Szenen, {transitions.Count} Übergänge und {inputs.Count} Eingaben aus OBS geladen.";
            await RefreshTimedAutomationSourceListAsync();
            _lastTimedAutomationObsRefresh = DateTimeOffset.UtcNow;
        }
        finally
        {
            _timedAutomationObsRefreshRunning = false;
        }
    }

    private async Task RefreshTimedAutomationSourceListAsync()
    {
        string? sceneName = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSourceSceneBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            sceneName = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSourceSceneBox.Text?.Trim();
        }
        if (!_obsClient.IsConnected || string.IsNullOrWhiteSpace(sceneName))
        {
            WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSourceBox.ItemsSource = Array.Empty<string>();
            return;
        }

        string previousSource = WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSourceBox.Text;
        try
        {
            var sources = (await _obsClient.GetSceneItemListAsync(sceneName))
                .Select(x => x.SourceName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSourceBox.ItemsSource = sources;
            WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSourceBox.Text = previousSource;
            WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTestStatusText.Text = $"{sources.Count} Quellen aus Szene ‘{sceneName}’ geladen.";
        }
        catch (Exception ex)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSourceBox.ItemsSource = Array.Empty<string>();
            WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTestStatusText.Text = "Quellen konnten nicht geladen werden: " + ex.Message;
        }
    }

    private async Task TestSelectedTimedAutomationRuleAsync()
    {
        TimedAutomationRuleSettings rule = ReadTimedAutomationEditor(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationRulesList.SelectedItem as TimedAutomationRuleSettings);
        if (!_obsClient.IsConnected) { WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTestStatusText.Text = "OBS verbinden, bevor der Test gestartet wird."; return; }
        _timedAutomationTestCts?.Cancel(); _timedAutomationTestCts = new CancellationTokenSource();
        int seconds = int.TryParse(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTestSecondsBox.Text, out int value) ? Math.Clamp(value, 0, 60) : 3;
        try
        {
            WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTestStatusText.Text = $"Test läuft · Aktion in {seconds} Sekunde(n). Der Stream bleibt aus.";
            await Task.Delay(TimeSpan.FromSeconds(seconds), _timedAutomationTestCts.Token);
            await ExecuteTimedAutomationRuleAsync(rule, _timedAutomationTestCts.Token, simulate: WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSimulationBox.IsChecked == true);
            WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTestStatusText.Text = "Test erfolgreich in OBS ausgeführt.";
        }
        catch (OperationCanceledException) { WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTestStatusText.Text = "Test abgebrochen."; }
        catch (Exception ex) { WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTestStatusText.Text = "Test fehlgeschlagen: " + ex.Message; }
    }

    private async Task RunShortStreamTestAsync()
    {
        _timedAutomationTestCts?.Cancel();
        _timedAutomationTestCts = new CancellationTokenSource();
        CancellationToken token = _timedAutomationTestCts.Token;
        WorkflowPageViewHost.ShortStreamTestViewHost.ShortStreamTestResultsList.Items.Clear();
        WorkflowPageViewHost.ShortStreamTestViewHost.StartShortStreamTestButton.IsEnabled = false;
        WorkflowPageViewHost.ShortStreamTestViewHost.SetStatus(
            "Kurztest läuft. OBS-Streaming bleibt ausgeschaltet.");

        async Task AddResultAsync(string name, Func<Task> test)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await test();
                WorkflowPageViewHost.ShortStreamTestViewHost.ShortStreamTestResultsList.Items.Add($"✓ {name}");
            }
            catch (Exception ex)
            {
                WorkflowPageViewHost.ShortStreamTestViewHost.ShortStreamTestResultsList.Items.Add($"✗ {name}: {ex.Message}");
            }
        }

        try
        {
            if (WorkflowPageViewHost.ShortStreamTestViewHost.ShortTestObsBox.IsChecked == true)
            {
                await AddResultAsync("OBS-Verbindung und Szenen", async () =>
                {
                    if (!_obsClient.IsConnected)
                    {
                        throw new InvalidOperationException("OBS ist nicht verbunden.");
                    }

                    IReadOnlyList<ObsSceneInfo> scenes = await _obsClient.GetSceneListAsync(token);
                    IReadOnlyList<ObsTransitionInfo> transitions = await _obsClient.GetSceneTransitionListAsync(token);
                    if (scenes.Count == 0)
                    {
                        throw new InvalidOperationException("Keine OBS-Szenen gefunden.");
                    }

                    WorkflowPageViewHost.ShortStreamTestViewHost.ShortStreamTestResultsList.Items.Add($"  {scenes.Count} Szenen · {transitions.Count} Übergänge");
                });
            }

            if (WorkflowPageViewHost.ShortStreamTestViewHost.ShortTestSpotifyBox.IsChecked == true)
            {
                await AddResultAsync("Spotify", async () =>
                {
                    if (!_spotifyModule.GetSnapshot().Authenticated)
                    {
                        throw new InvalidOperationException("Spotify ist nicht verbunden.");
                    }

                    await RefreshSpotifyInspectorAsync();
                });
            }

            if (WorkflowPageViewHost.ShortStreamTestViewHost.ShortTestStreamerBotBox.IsChecked == true)
            {
                await AddResultAsync("Streamer.bot", () =>
                {
                    if (!_streamerBotClient.IsConnected)
                    {
                        throw new InvalidOperationException("Streamer.bot ist nicht verbunden.");
                    }

                    return Task.CompletedTask;
                });
            }

            if (WorkflowPageViewHost.ShortStreamTestViewHost.ShortTestOverlayBox.IsChecked == true)
            {
                await AddResultAsync("Overlay", () =>
                {
                    string path = ResolveActiveOverlayDataPath();
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    {
                        throw new InvalidOperationException("overlay-data.json wurde nicht gefunden.");
                    }

                    return Task.CompletedTask;
                });
            }

            if (WorkflowPageViewHost.ShortStreamTestViewHost.ShortTestAlertBox.IsChecked == true)
            {
                await AddResultAsync("Suite-Alert", async () => await TestAlertInObsAsync());
            }

            if (WorkflowPageViewHost.ShortStreamTestViewHost.ShortTestAutomationBox.IsChecked == true)
            {
                await AddResultAsync("Automatisierungsregel", async () =>
                {
                    if (!_obsClient.IsConnected)
                    {
                        throw new InvalidOperationException("OBS ist nicht verbunden.");
                    }

                    TimedAutomationRuleSettings rule = ReadTimedAutomationEditor(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationRulesList.SelectedItem as TimedAutomationRuleSettings);
                    await ExecuteTimedAutomationRuleAsync(rule, token);
                });
            }

            WorkflowPageViewHost.ShortStreamTestViewHost.SetStatus(
                "Kurztest abgeschlossen. Der Stream wurde nicht gestartet.");
        }
        catch (OperationCanceledException)
        {
            WorkflowPageViewHost.ShortStreamTestViewHost.SetStatus(
                "Kurztest abgebrochen.");
        }
        finally
        {
            WorkflowPageViewHost.ShortStreamTestViewHost.StartShortStreamTestButton.IsEnabled = true;
        }
    }
}
