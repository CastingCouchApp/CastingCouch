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
    private void SetWorkflowVisualStage(string stage, string summary)
    {
        Brush inactive = _themeService.GetBrush("BorderBrush")
            ?? new SolidColorBrush(Color.FromRgb(51, 55, 59));
        Brush complete = _themeService.GetBrush("SuccessBrush")
            ?? new SolidColorBrush(Color.FromRgb(45, 125, 70));
        Brush active = _themeService.GetBrush("AccentBrush")
            ?? new SolidColorBrush(Color.FromRgb(112, 70, 190));

        DashboardPageViewHost.WorkflowPrepareNode.Background = inactive;
        DashboardPageViewHost.WorkflowReadyNode.Background = inactive;
        DashboardPageViewHost.WorkflowStartNode.Background = inactive;
        DashboardPageViewHost.WorkflowLiveNode.Background = inactive;
        DashboardPageViewHost.WorkflowEndNode.Background = inactive;
        DashboardPageViewHost.WorkflowRaidNode.Background = inactive;

        switch (stage)
        {
            case "Ready":
                DashboardPageViewHost.WorkflowPrepareNode.Background = complete;
                DashboardPageViewHost.WorkflowReadyNode.Background = active;
                break;
            case "Start":
                DashboardPageViewHost.WorkflowPrepareNode.Background = complete;
                DashboardPageViewHost.WorkflowReadyNode.Background = complete;
                DashboardPageViewHost.WorkflowStartNode.Background = active;
                break;
            case "Live":
                DashboardPageViewHost.WorkflowPrepareNode.Background = complete;
                DashboardPageViewHost.WorkflowReadyNode.Background = complete;
                DashboardPageViewHost.WorkflowStartNode.Background = complete;
                DashboardPageViewHost.WorkflowLiveNode.Background = active;
                break;
            case "End":
                DashboardPageViewHost.WorkflowPrepareNode.Background = complete;
                DashboardPageViewHost.WorkflowReadyNode.Background = complete;
                DashboardPageViewHost.WorkflowStartNode.Background = complete;
                DashboardPageViewHost.WorkflowLiveNode.Background = complete;
                DashboardPageViewHost.WorkflowEndNode.Background = active;
                break;
            case "Raid":
                DashboardPageViewHost.WorkflowPrepareNode.Background = complete;
                DashboardPageViewHost.WorkflowReadyNode.Background = complete;
                DashboardPageViewHost.WorkflowStartNode.Background = complete;
                DashboardPageViewHost.WorkflowLiveNode.Background = complete;
                DashboardPageViewHost.WorkflowEndNode.Background = complete;
                DashboardPageViewHost.WorkflowRaidNode.Background = active;
                break;
            default:
                DashboardPageViewHost.WorkflowPrepareNode.Background = active;
                break;
        }

        DashboardPageViewHost.DashboardCommandCenterSummaryText.Text = summary;
    }

    private async Task EvaluateTimedAutomationRulesAsync()
    {
        if (_timedAutomationEvaluationRunning || !_obsClient.IsConnected)
        {
            return;
        }

        _timedAutomationEvaluationRunning = true;
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            IReadOnlyList<TimedAutomationRuleSettings> dueRules =
                TimedAutomationRuleService.SelectDueRules(
                    _settings.Workflow.TimedAutomations,
                    _executedTimedAutomationRuleIds,
                    now,
                    _streamSessionStartedAt,
                    _automationSceneActivatedAt,
                    _automationCurrentScene);
            foreach (TimedAutomationRuleSettings rule in dueRules)
            {
                await StartTimedAutomationRuleAsync(rule, simulate: false);
                if (rule.OncePerStream)
                {
                    _executedTimedAutomationRuleIds.Add(rule.Id);
                }
            }
        }
        finally { _timedAutomationEvaluationRunning = false; }
    }


    private static bool IsScheduledAutomationDue(TimedAutomationRuleSettings rule, DateTime localNow)
        => TimedAutomationSchedule.IsDue(rule, localNow);

    private static string DescribeNextScheduledRun(TimedAutomationRuleSettings rule)
    {
        if (rule.TriggerType is not ("DailySchedule" or "WeeklySchedule" or "OneTimeSchedule"))
        {
            return "nicht zeitplanbasiert";
        }

        string next = TimedAutomationSchedule.DescribeNextRun(rule, DateTime.Now);
        return next switch
        {
            "Ungültige Uhrzeit" => "ungültige Uhrzeit",
            "Kein nächster Lauf" => "kein Termin im gültigen Zeitraum",
            _ => next
        };
    }

    private async Task StartTimedAutomationRuleAsync(TimedAutomationRuleSettings rule, bool simulate)
    {
        if (rule.StartWorkflowGroup && !string.IsNullOrWhiteSpace(rule.WorkflowGroup))
        {
            await ExecuteTimedAutomationWorkflowAsync(rule, simulate);
            return;
        }

        TimedAutomationDependencyDecision dependency =
            TimedAutomationRuntimeService.EvaluateDependency(
                rule,
                _settings.Workflow.TimedAutomations);
        if (!dependency.CanRun)
        {
            rule.SkippedRuns++;
            rule.LastRunAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
            rule.LastRunStatus = dependency.Status;
            AddTimedAutomationDiagnostic($"Übersprungen: '{rule.Name}' – erforderliche Vorgängerregel war nicht erfolgreich.");
            await _settingsStore.SaveAsync(_settings);
            return;
        }

        CancellationTokenSource? previous = null;
        lock (_timedAutomationRunSync)
        {
            if (_activeTimedAutomationRuns.TryGetValue(rule.Id, out previous))
            {
                if (string.Equals(rule.ExecutionMode, "SkipIfRunning", StringComparison.OrdinalIgnoreCase))
                {
                    rule.SkippedRuns++;
                    rule.LastRunAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
                    rule.LastRunStatus = "Übersprungen";
                    AddTimedAutomationDiagnostic($"Übersprungen: '{rule.Name}' läuft bereits.");
                    _ = _settingsStore.SaveAsync(_settings);
                    return;
                }
                if (string.Equals(rule.ExecutionMode, "Restart", StringComparison.OrdinalIgnoreCase))
                {
                    previous.Cancel();
                }
            }
        }

        TimedAutomationExecutionPolicy policy =
            TimedAutomationRuntimeService.ResolveExecutionPolicy(rule);
        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(policy.TimeoutSeconds));
        lock (_timedAutomationRunSync)
        {
            if (!string.Equals(rule.ExecutionMode, "Parallel", StringComparison.OrdinalIgnoreCase))
            {
                _activeTimedAutomationRuns[rule.Id] = timeoutCts;
            }

            UpdateTimedAutomationRuntimeStatus();
        }

        Exception? finalError = null;
        int maxAttempts = policy.MaxAttempts;
        try
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    if (attempt > 1)
                    {
                        AddTimedAutomationDiagnostic($"Wiederholungsversuch {attempt}/{maxAttempts}: '{rule.Name}'.");
                    }

                    await ExecuteTimedAutomationRuleAsync(rule, timeoutCts.Token, simulate: simulate);
                    finalError = null;
                    break;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    finalError = ex;
                    if (attempt >= maxAttempts)
                    {
                        break;
                    }

                    AddTimedAutomationDiagnostic($"Fehler bei '{rule.Name}': {ex.Message} – neuer Versuch in {rule.RetryDelaySeconds} Sekunden.");
                    if (policy.RetryDelaySeconds > 0)
                    {
                        await Task.Delay(
                            TimeSpan.FromSeconds(policy.RetryDelaySeconds),
                            timeoutCts.Token);
                    }
                }
            }

            if (finalError is not null)
            {
                throw finalError;
            }

            if (!simulate)
            {
                rule.SuccessfulRuns++;
                rule.LastRunStatus = "Erfolgreich";
                rule.LastRunAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
                if (rule.TriggerType is "DailySchedule" or "WeeklySchedule" or "OneTimeSchedule")
                {
                    rule.LastScheduledRunDate = DateTime.Now.ToString("yyyy-MM-dd");
                }

                await _settingsStore.SaveAsync(_settings);
            }
        }
        catch (OperationCanceledException)
        {
            rule.FailedRuns++;
            rule.LastRunStatus = "Abgebrochen/Timeout";
            rule.LastRunAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
            AddTimedAutomationDiagnostic($"Abgebrochen/Timeout: '{rule.Name}'.");
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception ex)
        {
            rule.FailedRuns++;
            rule.LastRunStatus = "Fehler";
            rule.LastRunAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
            AddTimedAutomationDiagnostic($"Endgültig fehlgeschlagen: '{rule.Name}' – {ex.Message}");
            await _settingsStore.SaveAsync(_settings);

            if (!string.IsNullOrWhiteSpace(rule.FailureRuleId))
            {
                TimedAutomationRuleSettings? fallback = _settings.Workflow.TimedAutomations.FirstOrDefault(x => string.Equals(x.Id, rule.FailureRuleId, StringComparison.OrdinalIgnoreCase));
                if (fallback is not null)
                {
                    AddTimedAutomationDiagnostic($"Ersatzregel: '{rule.Name}' → '{fallback.Name}'.");
                    await StartTimedAutomationRuleAsync(fallback, simulate);
                }
            }
        }
        finally
        {
            lock (_timedAutomationRunSync)
            {
                if (_activeTimedAutomationRuns.TryGetValue(rule.Id, out CancellationTokenSource? current) && ReferenceEquals(current, timeoutCts))
                {
                    _activeTimedAutomationRuns.Remove(rule.Id);
                }

                UpdateTimedAutomationRuntimeStatus();
            }
        }
    }

    private async Task ExecuteTimedAutomationWorkflowAsync(TimedAutomationRuleSettings starter, bool simulate)
    {
        IReadOnlyList<TimedAutomationRuleSettings> steps =
            TimedAutomationRuleService.SelectWorkflowSteps(
                _settings.Workflow.TimedAutomations,
                starter.WorkflowGroup);
        if (steps.Count == 0)
        {
            AddTimedAutomationDiagnostic($"Ablaufgruppe '{starter.WorkflowGroup}' enthält keine aktiven Schritte.");
            return;
        }

        string runId = Guid.NewGuid().ToString("N")[..8];
        var completed = new List<TimedAutomationRuleSettings>();
        AddTimedAutomationDiagnostic($"Workflow {runId} gestartet: '{starter.WorkflowGroup}' mit {steps.Count} Schritt(en).");
        foreach (TimedAutomationRuleSettings? step in steps)
        {
            try
            {
                TimedAutomationExecutionPolicy policy =
                    TimedAutomationRuntimeService.ResolveExecutionPolicy(step);
                using var timeout = new CancellationTokenSource(
                    TimeSpan.FromSeconds(policy.TimeoutSeconds));
                Exception? lastError = null;
                int attempts = policy.MaxAttempts;
                for (int attempt = 1; attempt <= attempts; attempt++)
                {
                    try
                    {
                        await ExecuteTimedAutomationRuleAsync(step, timeout.Token, simulate: simulate);
                        lastError = null;
                        break;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        lastError = ex;
                        if (attempt < attempts &&
                            policy.RetryDelaySeconds > 0)
                        {
                            await Task.Delay(
                                TimeSpan.FromSeconds(
                                    policy.RetryDelaySeconds),
                                timeout.Token);
                        }
                    }
                }
                if (lastError is not null)
                {
                    throw lastError;
                }

                completed.Add(step);
                AddTimedAutomationDiagnostic($"Workflow {runId}: Schritt '{step.Name}' abgeschlossen.");
            }
            catch (Exception ex)
            {
                AddTimedAutomationDiagnostic($"Workflow {runId}: Schritt '{step.Name}' fehlgeschlagen – {ex.Message}");
                if (string.Equals(starter.WorkflowFailureMode, "Continue", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(starter.WorkflowFailureMode, "Rollback", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (TimedAutomationRuleSettings? done in completed.AsEnumerable().Reverse())
                    {
                        TimedAutomationRuleSettings? rollback = _settings.Workflow.TimedAutomations.FirstOrDefault(x => string.Equals(x.Id, done.RollbackRuleId, StringComparison.OrdinalIgnoreCase));
                        if (rollback is null)
                        {
                            continue;
                        }

                        try
                        {
                            AddTimedAutomationDiagnostic($"Workflow {runId}: Rückabwicklung '{done.Name}' → '{rollback.Name}'.");
                            await ExecuteTimedAutomationRuleAsync(rollback, CancellationToken.None, simulate: simulate);
                        }
                        catch (Exception rollbackError)
                        {
                            AddTimedAutomationDiagnostic($"Workflow {runId}: Rückabwicklung '{rollback.Name}' fehlgeschlagen – {rollbackError.Message}");
                        }
                    }
                }
                starter.FailedRuns++;
                starter.LastRunAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
                starter.LastRunStatus = "Workflow fehlgeschlagen";
                await _settingsStore.SaveAsync(_settings);
                return;
            }
        }
        if (!simulate)
        {
            starter.SuccessfulRuns++;
            starter.LastRunAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
            starter.LastRunStatus = "Workflow erfolgreich";
            await _settingsStore.SaveAsync(_settings);
        }
        AddTimedAutomationDiagnostic($"Workflow {runId} abgeschlossen: '{starter.WorkflowGroup}'.");
    }

    private void StopAllTimedAutomations()
    {
        List<CancellationTokenSource> running;
        lock (_timedAutomationRunSync)
        {
            running = [.. _activeTimedAutomationRuns.Values.Distinct()];
        }

        foreach (CancellationTokenSource cts in running)
        {
            cts.Cancel();
        }

        AddTimedAutomationDiagnostic($"Abbruch angefordert: {running.Count} laufende Automation(en).");
    }

    private void UpdateTimedAutomationRuntimeStatus()
    {
        if (WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationRuntimeStatusText is null)
        {
            return;
        }

        int count = _activeTimedAutomationRuns.Count;
        Dispatcher.InvokeAsync(() => WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationRuntimeStatusText.Text = count == 0 ? "Keine laufende Automation." : $"{count} Automation(en) laufen.");
    }

    private async Task ExecuteTimedAutomationRuleAsync(TimedAutomationRuleSettings rule, CancellationToken cancellationToken, HashSet<string>? chain = null, bool simulate = false)
    {
        chain ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!chain.Add(rule.Id))
        {
            AddTimedAutomationDiagnostic($"Kette abgebrochen: Schleife bei '{rule.Name}' erkannt.");
            return;
        }

        if (!await EvaluateTimedAutomationConditionAsync(rule, cancellationToken))
        {
            AddTimedAutomationDiagnostic($"Übersprungen: '{rule.Name}' – Bedingung nicht erfüllt.");
            return;
        }

        Exception? executionError = null;
        try
        {
            if (simulate)
            {
                AddTimedAutomationDiagnostic($"Simulation: '{rule.Name}' → {DescribeTimedAutomationAction(rule)}");
            }
            else
            {
                await ExecuteTimedAutomationActionAsync(rule, cancellationToken);
                AddTimedAutomationDiagnostic($"Ausgeführt: '{rule.Name}'.");
            }
            _appLogger.Write(AppLogLevel.Information, "Automation", $"Regel ausgeführt: {rule.Name}");
        }
        catch (Exception ex)
        {
            executionError = ex;
            AddTimedAutomationDiagnostic($"Fehler: '{rule.Name}' – {ex.Message}");
            _appLogger.Write(AppLogLevel.Error, "Automation", $"Regel fehlgeschlagen ({rule.Name}): {ex.Message}");
            if (!rule.ContinueChainOnError)
            {
                throw;
            }
        }

        if (!string.IsNullOrWhiteSpace(rule.NextRuleId) && (executionError is null || rule.ContinueChainOnError))
        {
            TimedAutomationRuleSettings? next = _settings.Workflow.TimedAutomations.FirstOrDefault(x => string.Equals(x.Id, rule.NextRuleId, StringComparison.OrdinalIgnoreCase));
            if (next is null)
            {
                AddTimedAutomationDiagnostic($"Kette unvollständig: Folgeregel für '{rule.Name}' wurde nicht gefunden.");
                return;
            }
            if (rule.NextRuleDelaySeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(rule.NextRuleDelaySeconds), cancellationToken);
            }

            AddTimedAutomationDiagnostic($"Kette: '{rule.Name}' → '{next.Name}'.");
            await ExecuteTimedAutomationRuleAsync(next, cancellationToken, chain, simulate);
        }
    }

    private async Task<bool> EvaluateTimedAutomationConditionAsync(TimedAutomationRuleSettings rule, CancellationToken cancellationToken)
    {
        bool result = rule.ConditionType switch
        {
            "ObsConnected" => _obsClient.IsConnected,
            "StreamerBotConnected" => _streamerBotClient.IsConnected,
            "StreamActive" => _streamSessionStartedAt.HasValue,
            "CurrentScene" => _obsClient.IsConnected && string.Equals(await _obsClient.GetCurrentProgramSceneAsync(cancellationToken), rule.ConditionValue, StringComparison.OrdinalIgnoreCase),
            _ => true
        };
        return rule.ConditionNegated ? !result : result;
    }

    private void AddTimedAutomationDiagnostic(string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss}  {message}";
        _timedAutomationDiagnostics.Insert(0, line);
        while (_timedAutomationDiagnostics.Count > 100)
        {
            _timedAutomationDiagnostics.RemoveAt(_timedAutomationDiagnostics.Count - 1);
        }
    }

    private void ValidateTimedAutomationRules()
    {
        _timedAutomationDiagnostics.Clear();
        IReadOnlyList<string> issues =
            TimedAutomationRuleService.Validate(_timedAutomationRules);
        foreach (string issue in issues)
        {
            AddTimedAutomationDiagnostic(issue);
        }

        if (issues.Count == 0)
        {
            AddTimedAutomationDiagnostic($"Prüfung abgeschlossen: {_timedAutomationRules.Count} Regeln, keine Fehler gefunden.");
        }

        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTestStatusText.Text = issues.Count == 0 ? "Alle Regeln sind gültig." : $"Regelprüfung: {issues.Count} Hinweis(e).";
    }

    private void CancelPendingSceneAutomationExecutions()
    {
        // Szenen-Timer werden über den Aktivierungszeitpunkt neu gestartet.
    }

    private async Task ResetTimedAutomationsAtStreamEndAsync()
    {
        _executedTimedAutomationRuleIds.Clear();
        foreach (TimedAutomationRuleSettings rule in
                 TimedAutomationRuntimeService.SelectStreamEndResetRules(
                     _settings.Workflow.TimedAutomations))
        {
            if (!_obsClient.IsConnected || string.IsNullOrWhiteSpace(rule.ObsScene) || string.IsNullOrWhiteSpace(rule.ObsSource))
            {
                continue;
            }

            try { await _obsClient.SetSceneItemEnabledAsync(rule.ObsScene, rule.ObsSource, rule.ResetSourceVisible); }
            catch (Exception ex) { _appLogger.Write(AppLogLevel.Warning, "Automation", $"Rücksetzen fehlgeschlagen ({rule.Name}): {ex.Message}"); }
        }
    }

    private sealed class TimedAutomationExportPackage
    {
        public string Format { get; set; } = "CreatorControlSuite.Automation";
        public int Version { get; set; } = 1;
        public DateTimeOffset ExportedAt { get; set; }
        public List<TimedAutomationRuleSettings> Rules { get; set; } = [];
    }
}
