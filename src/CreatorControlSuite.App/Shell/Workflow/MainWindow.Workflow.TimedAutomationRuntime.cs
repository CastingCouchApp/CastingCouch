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
            foreach (TimedAutomationRuleSettings? rule in _settings.Workflow.TimedAutomations.Where(x => x.Enabled).OrderByDescending(x => x.Priority).ThenBy(x => x.Name).ToList())
            {
                if (rule.OncePerStream && _executedTimedAutomationRuleIds.Contains(rule.Id))
                {
                    continue;
                }

                if (!TimedAutomationSchedule.IsTriggerDue(rule, now, _streamSessionStartedAt, _automationSceneActivatedAt, _automationCurrentScene))
                {
                    continue;
                }

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

        if (!string.IsNullOrWhiteSpace(rule.DependencyRuleId))
        {
            TimedAutomationRuleSettings? dependency = _settings.Workflow.TimedAutomations.FirstOrDefault(x => string.Equals(x.Id, rule.DependencyRuleId, StringComparison.OrdinalIgnoreCase));
            if (dependency is null || !string.Equals(dependency.LastRunStatus, rule.DependencyRequiredStatus, StringComparison.OrdinalIgnoreCase))
            {
                rule.SkippedRuns++;
                rule.LastRunAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
                rule.LastRunStatus = "Abhängigkeit nicht erfüllt";
                AddTimedAutomationDiagnostic($"Übersprungen: '{rule.Name}' – erforderliche Vorgängerregel war nicht erfolgreich.");
                await _settingsStore.SaveAsync(_settings);
                return;
            }
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

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(rule.TimeoutSeconds, 1, 86400)));
        lock (_timedAutomationRunSync)
        {
            if (!string.Equals(rule.ExecutionMode, "Parallel", StringComparison.OrdinalIgnoreCase))
            {
                _activeTimedAutomationRuns[rule.Id] = timeoutCts;
            }

            UpdateTimedAutomationRuntimeStatus();
        }

        Exception? finalError = null;
        int maxAttempts = Math.Clamp(rule.RetryCount, 0, 20) + 1;
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
                    if (rule.RetryDelaySeconds > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(rule.RetryDelaySeconds), timeoutCts.Token);
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
        var steps = _settings.Workflow.TimedAutomations
            .Where(x => x.Enabled && string.Equals(x.WorkflowGroup, starter.WorkflowGroup, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.WorkflowOrder)
            .ThenByDescending(x => x.Priority)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(step.TimeoutSeconds, 1, 86400)));
                Exception? lastError = null;
                int attempts = Math.Clamp(step.RetryCount, 0, 20) + 1;
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
                        if (attempt < attempts && step.RetryDelaySeconds > 0)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(step.RetryDelaySeconds), timeout.Token);
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

    private async Task ExecuteTimedAutomationActionAsync(TimedAutomationRuleSettings rule, CancellationToken cancellationToken)
    {
        if (string.Equals(rule.ActionType, "SwitchScene", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(rule.TargetScene))
            {
                throw new InvalidOperationException("Keine Zielszene gewählt.");
            }

            if (!string.IsNullOrWhiteSpace(rule.TransitionName))
            {
                await _obsClient.SetCurrentSceneTransitionAsync(rule.TransitionName, cancellationToken);
                await _obsClient.SetCurrentSceneTransitionDurationAsync(rule.TransitionDurationMilliseconds, cancellationToken);
            }
            await _obsClient.SetCurrentProgramSceneAsync(rule.TargetScene, cancellationToken);
        }
        else if (string.Equals(rule.ActionType, "SetSourceVisibility", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(rule.ObsScene) || string.IsNullOrWhiteSpace(rule.ObsSource))
            {
                throw new InvalidOperationException("Szene und Quelle müssen gewählt sein.");
            }

            await _obsClient.SetSceneItemEnabledAsync(rule.ObsScene, rule.ObsSource, rule.SourceVisible, cancellationToken);
        }
        else if (string.Equals(rule.ActionType, "SetInputMute", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(rule.ObsInput))
            {
                throw new InvalidOperationException("Keine OBS-Audioquelle gewählt.");
            }

            await _obsClient.SetInputMuteAsync(rule.ObsInput, rule.InputMuted, cancellationToken);
        }
        else if (string.Equals(rule.ActionType, "StartObsStream", StringComparison.OrdinalIgnoreCase))
        {
            await _obsClient.StartStreamAsync(cancellationToken);
        }
        else if (string.Equals(rule.ActionType, "StopObsStream", StringComparison.OrdinalIgnoreCase))
        {
            await _obsClient.StopStreamAsync(cancellationToken);
        }
        else if (string.Equals(rule.ActionType, "StreamerBotAction", StringComparison.OrdinalIgnoreCase))
        {
            if (!_streamerBotClient.IsConnected)
            {
                throw new InvalidOperationException("Streamer.bot ist nicht verbunden.");
            }

            if (string.IsNullOrWhiteSpace(rule.StreamerBotActionId) && string.IsNullOrWhiteSpace(rule.StreamerBotActionName))
            {
                throw new InvalidOperationException("Keine Streamer.bot-Aktion gewählt.");
            }

            using JsonDocument response = await SendStreamerBotRequestAsync(new
            {
                request = "DoAction",
                action = new { id = rule.StreamerBotActionId, name = rule.StreamerBotActionName },
                args = new { source = "Creator Control Suite", automationRule = rule.Name }
            });
            string? status = response.RootElement.TryGetProperty("status", out JsonElement statusNode) ? statusNode.GetString() : null;
            if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Streamer.bot hat die Aktion nicht bestätigt.");
            }
        }
        else if (string.Equals(rule.ActionType, "OverlayCountdown", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(rule.OverlayCountdownAction, "Stop", StringComparison.OrdinalIgnoreCase))
            {
                await _workflowModule.Service.StopCountdownAsync(cancellationToken);
            }
            else
            {
                PersistDashboardCountdownSettings();
                int duration = rule.OverlayCountdownSeconds > 0
                    ? rule.OverlayCountdownSeconds
                    : Math.Max(0, _settings.Workflow.StartCountdownSeconds);
                // Countdown läuft asynchron; die Automation soll nicht die volle Dauer blockieren.
                _ = Task.Run(
                    () => duration > 0
                        ? _workflowModule.Service.StartCountdownAsync(duration)
                        : _workflowModule.Service.StartCountdownAsync(),
                    CancellationToken.None);
            }
        }

        if (!string.Equals(rule.SpotifyAction, "None", StringComparison.OrdinalIgnoreCase))
        {
            CancellationTokenSource spotifyRunCts;
            lock (_spotifyAutomationSync)
            {
                string incomingGroup = string.IsNullOrWhiteSpace(rule.SpotifyAutomationGroup) ? "Standard" : rule.SpotifyAutomationGroup.Trim();
                if (_spotifyAutomationCts is not null)
                {
                    bool sameGroup = string.Equals(incomingGroup, _activeSpotifyAutomationGroup, StringComparison.OrdinalIgnoreCase);
                    bool blockedByExclusiveGroup = !sameGroup && (_activeSpotifyAutomationExclusive || rule.SpotifyExclusiveGroup);
                    if (rule.SpotifyPriority < _activeSpotifyAutomationPriority ||
                        (blockedByExclusiveGroup && rule.SpotifyPriority <= _activeSpotifyAutomationPriority))
                    {
                        string reason = blockedByExclusiveGroup
                            ? $"Gruppe '{incomingGroup}' ist durch die aktive Gruppe '{_activeSpotifyAutomationGroup}' gesperrt"
                            : $"Priorität {rule.SpotifyPriority} ist niedriger als aktive Priorität {_activeSpotifyAutomationPriority}";
                        AddTimedAutomationDiagnostic($"Spotify-Aktion '{rule.Name}' übersprungen: {reason}.");
                        return;
                    }
                }

                _spotifyAutomationCts?.Cancel();
                _spotifyAutomationCts?.Dispose();
                _spotifyAutomationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _activeSpotifyAutomationPriority = rule.SpotifyPriority;
                _activeSpotifyAutomationGroup = string.IsNullOrWhiteSpace(rule.SpotifyAutomationGroup) ? "Standard" : rule.SpotifyAutomationGroup.Trim();
                _activeSpotifyAutomationExclusive = rule.SpotifyExclusiveGroup;
                spotifyRunCts = _spotifyAutomationCts;
            }

            try
            {
                CancellationToken spotifyToken = spotifyRunCts.Token;
                if (rule.SpotifyActionDelaySeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(rule.SpotifyActionDelaySeconds), spotifyToken);
                }

                string spotifyGroup = string.IsNullOrWhiteSpace(rule.SpotifyAutomationGroup) ? "Standard" : rule.SpotifyAutomationGroup.Trim();
                if (rule.SpotifySavePreviousState && !string.Equals(rule.SpotifyAction, "RestorePrevious", StringComparison.OrdinalIgnoreCase))
                {
                    await SaveSpotifyAutomationStateAsync(spotifyGroup, spotifyToken);
                }

                if (string.Equals(rule.SpotifyAction, "RestorePrevious", StringComparison.OrdinalIgnoreCase))
                {
                    await RestoreSpotifyAutomationStateAsync(spotifyGroup, rule, spotifyToken);
                }
                else if (string.Equals(rule.SpotifyAction, "Pause", StringComparison.OrdinalIgnoreCase))
                {
                    await _spotifyModule.PauseAsync(spotifyToken);
                }
                else if (string.Equals(rule.SpotifyAction, "Resume", StringComparison.OrdinalIgnoreCase))
                {
                    await _spotifyModule.ResumeAsync(spotifyToken);
                    await ApplySpotifyAutomationVolumeAsync(rule, spotifyToken);
                }
                else if (string.Equals(rule.SpotifyAction, "SetVolume", StringComparison.OrdinalIgnoreCase))
                {
                    await ApplySpotifyAutomationVolumeAsync(rule, spotifyToken);
                }
                else if (string.Equals(rule.SpotifyAction, "StartPlaylist", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(rule.SpotifyPlaylistUri))
                    {
                        throw new InvalidOperationException("Für die Spotify-Automation wurde keine Playlist-URI eingetragen.");
                    }

                    if (rule.SpotifyFadeSeconds > 0)
                    {
                        await _spotifyModule.SetVolumeImmediateAsync(0, spotifyToken);
                    }

                    await _spotifyModule.StartPlaylistAsync(
                        rule.SpotifyPlaylistUri,
                        applyConfiguredStartVolume: false,
                        shuffleOverride: rule.SpotifyPlaylistShuffle,
                        cancellationToken: spotifyToken);
                    await ApplySpotifyAutomationVolumeAsync(rule, spotifyToken);
                }

                if (rule.SpotifyAutoRestorePreviousState &&
                    !string.Equals(rule.SpotifyAction, "RestorePrevious", StringComparison.OrdinalIgnoreCase))
                {
                    _spotifySavedStateStore.TtlMinutes = GetSpotifySavedStateMaxAgeMinutes();
                    bool hasSavedState = _spotifySavedStateStore.ContainsKey(spotifyGroup);

                    if (!hasSavedState)
                    {
                        AddTimedAutomationDiagnostic($"Spotify-Gruppe '{spotifyGroup}': Automatische Rückkehr übersprungen, weil kein Zustand gesichert wurde.");
                    }
                    else
                    {
                        int restoreDelay = Math.Clamp(rule.SpotifyAutoRestoreDelaySeconds, 1, 86400);
                        string expectedScene = _automationCurrentScene;
                        await _spotifyModule.RefreshPlaybackAsync(spotifyToken);
                        SpotifyPlaybackState expectedPlayback = _spotifyModule.GetSnapshot().Playback;
                        string expectedTrackUri = expectedPlayback.Track?.Uri ?? "";
                        string expectedContextUri = expectedPlayback.ContextUri ?? "";
                        AddTimedAutomationDiagnostic($"Spotify-Gruppe '{spotifyGroup}': Automatische Rückkehr in {restoreDelay} Sekunden vorgemerkt.");
                        await Task.Delay(TimeSpan.FromSeconds(restoreDelay), spotifyToken);

                        if (rule.SpotifyAutoRestoreRequireSameScene && !string.Equals(expectedScene, _automationCurrentScene, StringComparison.OrdinalIgnoreCase))
                        {
                            AddTimedAutomationDiagnostic($"Spotify-Gruppe '{spotifyGroup}': Automatische Rückkehr verworfen, weil die OBS-Szene von '{expectedScene}' zu '{_automationCurrentScene}' gewechselt wurde.");
                            return;
                        }
                        if (rule.SpotifyAutoRestoreRequireSameGroup)
                        {
                            lock (_spotifyAutomationSync)
                            {
                                if (!ReferenceEquals(_spotifyAutomationCts, spotifyRunCts) || !string.Equals(_activeSpotifyAutomationGroup, spotifyGroup, StringComparison.OrdinalIgnoreCase))
                                {
                                    AddTimedAutomationDiagnostic($"Spotify-Gruppe '{spotifyGroup}': Automatische Rückkehr verworfen, weil die Gruppe nicht mehr aktiv ist.");
                                    return;
                                }
                            }
                        }
                        if (rule.SpotifyAutoRestoreRequireUnchangedPlayback)
                        {
                            await _spotifyModule.RefreshPlaybackAsync(spotifyToken);
                            SpotifyPlaybackState currentPlayback = _spotifyModule.GetSnapshot().Playback;
                            string currentTrackUri = currentPlayback.Track?.Uri ?? "";
                            string currentContextUri = currentPlayback.ContextUri ?? "";
                            if (!string.Equals(expectedTrackUri, currentTrackUri, StringComparison.OrdinalIgnoreCase) ||
                                !string.Equals(expectedContextUri, currentContextUri, StringComparison.OrdinalIgnoreCase))
                            {
                                AddTimedAutomationDiagnostic($"Spotify-Gruppe '{spotifyGroup}': Automatische Rückkehr verworfen, weil die Wiedergabe zwischenzeitlich geändert wurde.");
                                return;
                            }
                        }
                        await RestoreSpotifyAutomationStateAsync(spotifyGroup, rule, spotifyToken);
                    }
                }
            }
            finally
            {
                lock (_spotifyAutomationSync)
                {
                    if (ReferenceEquals(_spotifyAutomationCts, spotifyRunCts))
                    {
                        _spotifyAutomationCts.Dispose();
                        _spotifyAutomationCts = null;
                        _activeSpotifyAutomationPriority = int.MinValue;
                        _activeSpotifyAutomationGroup = "";
                        _activeSpotifyAutomationExclusive = false;
                    }
                }
            }
        }
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
        var ids = _timedAutomationRules.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int issues = 0;
        foreach (TimedAutomationRuleSettings rule in _timedAutomationRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Name)) { AddTimedAutomationDiagnostic("Hinweis: Eine Regel hat keinen Namen."); issues++; }
            if ((rule.TriggerType is "SceneElapsed" or "SceneActivated") && string.IsNullOrWhiteSpace(rule.TriggerScene)) { AddTimedAutomationDiagnostic($"Fehlt: Ausgangsszene bei '{rule.Name}'."); issues++; }
            if ((rule.TriggerType is "DailySchedule" or "WeeklySchedule" or "OneTimeSchedule") && !TimeOnly.TryParse(rule.ScheduleTime, out _)) { AddTimedAutomationDiagnostic($"Ungültige Uhrzeit bei '{rule.Name}'."); issues++; }
            if (rule.TriggerType == "WeeklySchedule" && string.IsNullOrWhiteSpace(rule.ScheduleDays)) { AddTimedAutomationDiagnostic($"Keine Wochentage bei '{rule.Name}'."); issues++; }
            if (rule.TriggerType == "OneTimeSchedule" && !DateOnly.TryParse(rule.ScheduleDate, out _)) { AddTimedAutomationDiagnostic($"Ungültiges einmaliges Datum bei '{rule.Name}'."); issues++; }
            if (DateOnly.TryParse(rule.ActiveFromDate, out DateOnly fromDate) && DateOnly.TryParse(rule.ActiveUntilDate, out DateOnly untilDate) && fromDate > untilDate) { AddTimedAutomationDiagnostic($"Aktivzeitraum ist umgekehrt bei '{rule.Name}'."); issues++; }
            foreach (string value in (rule.ExcludedDates ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!DateOnly.TryParse(value, out _)) { AddTimedAutomationDiagnostic($"Ungültiger Ausnahmetag '{value}' bei '{rule.Name}'."); issues++; }
            }

            foreach (string range in (rule.BlackoutRanges ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) { string[] bounds = range.Split("..", StringSplitOptions.TrimEntries); if (bounds.Length != 2 || !DateOnly.TryParse(bounds[0], out DateOnly blackoutStart) || !DateOnly.TryParse(bounds[1], out DateOnly blackoutEnd) || blackoutStart > blackoutEnd) { AddTimedAutomationDiagnostic($"Ungültiger Sperrzeitraum '{range}' bei '{rule.Name}'."); issues++; } }
            if (rule.ActionType == "SwitchScene" && string.IsNullOrWhiteSpace(rule.TargetScene)) { AddTimedAutomationDiagnostic($"Fehlt: Zielszene bei '{rule.Name}'."); issues++; }
            if (rule.ActionType == "SetSourceVisibility" && (string.IsNullOrWhiteSpace(rule.ObsScene) || string.IsNullOrWhiteSpace(rule.ObsSource))) { AddTimedAutomationDiagnostic($"Fehlt: Szene/Quelle bei '{rule.Name}'."); issues++; }
            if (rule.ActionType == "SetInputMute" && string.IsNullOrWhiteSpace(rule.ObsInput)) { AddTimedAutomationDiagnostic($"Fehlt: Audioquelle bei '{rule.Name}'."); issues++; }
            if (rule.ConditionType == "CurrentScene" && string.IsNullOrWhiteSpace(rule.ConditionValue)) { AddTimedAutomationDiagnostic($"Fehlt: Szenenname in Bedingung bei '{rule.Name}'."); issues++; }
            if (!string.IsNullOrWhiteSpace(rule.NextRuleId) && !ids.Contains(rule.NextRuleId)) { AddTimedAutomationDiagnostic($"Ungültige Folgeregel bei '{rule.Name}'."); issues++; }
            if (!string.IsNullOrWhiteSpace(rule.DependencyRuleId) && !ids.Contains(rule.DependencyRuleId)) { AddTimedAutomationDiagnostic($"Ungültige Abhängigkeitsregel bei '{rule.Name}'."); issues++; }
            if (!string.IsNullOrWhiteSpace(rule.FailureRuleId) && !ids.Contains(rule.FailureRuleId)) { AddTimedAutomationDiagnostic($"Ungültige Ersatzregel bei '{rule.Name}'."); issues++; }
            if (!string.IsNullOrWhiteSpace(rule.RollbackRuleId) && !ids.Contains(rule.RollbackRuleId)) { AddTimedAutomationDiagnostic($"Ungültige Rückabwicklungsregel bei '{rule.Name}'."); issues++; }
            if (rule.StartWorkflowGroup && string.IsNullOrWhiteSpace(rule.WorkflowGroup)) { AddTimedAutomationDiagnostic($"Workflow-Start ohne Gruppenname bei '{rule.Name}'."); issues++; }
            if (string.Equals(rule.DependencyRuleId, rule.Id, StringComparison.OrdinalIgnoreCase)) { AddTimedAutomationDiagnostic($"Selbstabhängigkeit bei '{rule.Name}'."); issues++; }
            if (string.Equals(rule.FailureRuleId, rule.Id, StringComparison.OrdinalIgnoreCase)) { AddTimedAutomationDiagnostic($"Ersatzregel verweist auf sich selbst bei '{rule.Name}'."); issues++; }
            if (string.Equals(rule.RollbackRuleId, rule.Id, StringComparison.OrdinalIgnoreCase)) { AddTimedAutomationDiagnostic($"Rückabwicklungsregel verweist auf sich selbst bei '{rule.Name}'."); issues++; }
        }
        foreach (TimedAutomationRuleSettings rule in _timedAutomationRules)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            TimedAutomationRuleSettings? current = rule;
            while (!string.IsNullOrWhiteSpace(current.NextRuleId))
            {
                if (!seen.Add(current.Id)) { AddTimedAutomationDiagnostic($"Schleife erkannt, beginnend bei '{rule.Name}'."); issues++; break; }
                current = _timedAutomationRules.FirstOrDefault(x => string.Equals(x.Id, current.NextRuleId, StringComparison.OrdinalIgnoreCase))!;
                if (current is null)
                {
                    break;
                }
            }
        }
        if (issues == 0)
        {
            AddTimedAutomationDiagnostic($"Prüfung abgeschlossen: {_timedAutomationRules.Count} Regeln, keine Fehler gefunden.");
        }

        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationTestStatusText.Text = issues == 0 ? "Alle Regeln sind gültig." : $"Regelprüfung: {issues} Hinweis(e).";
    }

    private void CancelPendingSceneAutomationExecutions()
    {
        // Szenen-Timer werden über den Aktivierungszeitpunkt neu gestartet.
    }

    private async Task ResetTimedAutomationsAtStreamEndAsync()
    {
        _executedTimedAutomationRuleIds.Clear();
        foreach (TimedAutomationRuleSettings? rule in _settings.Workflow.TimedAutomations.Where(x => x.Enabled && x.ResetSourceAtStreamEnd))
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
