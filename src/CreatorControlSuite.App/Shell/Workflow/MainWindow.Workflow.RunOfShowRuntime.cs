#nullable enable

using System.Text.Json;
using CreatorControlSuite.App.Services;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Logging;

namespace CreatorControlSuite.App.Shell;

public partial class MainWindow
{
    private async Task ExecuteSelectedRunOfShowStepAsync()
    {
        if (WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.SelectedItem is not RunOfShowStepSettings step)
        {
            return;
        }

        ReadRunOfShowEditor(step);
        await ExecuteRunOfShowStepAsync(step);
        _runOfShowCurrentIndex = _runOfShowSteps.IndexOf(step);
        UpdateRunOfShowStatus();
    }

    private async Task ExecuteNextRunOfShowStepAsync()
    {
        if (_runOfShowSteps.Count == 0)
        {
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text =
                "Noch keine Regieschritte vorhanden.";
            return;
        }

        int nextIndex =
            RunOfShowPlanService.ProjectRuntime(
                _runOfShowSteps,
                _runOfShowCurrentIndex).NextEnabledIndex;
        if (nextIndex < 0)
        {
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text =
                "Regieplan ist beendet.";
            return;
        }

        RunOfShowStepSettings step = _runOfShowSteps[nextIndex];
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.SelectedItem =
            step;
        await ExecuteRunOfShowStepAsync(step);
        _runOfShowCurrentIndex = nextIndex;
        UpdateRunOfShowStatus();
    }

    private async Task ExecuteRunOfShowStepAsync(RunOfShowStepSettings step)
    {
        try
        {
            string? executionWarning = null;
            if (!_obsClient.IsConnected)
            {
                throw new InvalidOperationException("OBS ist nicht verbunden.");
            }

            if (string.IsNullOrWhiteSpace(step.ObsScene))
            {
                throw new InvalidOperationException(
                    "Keine OBS-Szene ausgewählt.");
            }

            if (!string.IsNullOrWhiteSpace(step.TransitionName))
            {
                await _obsClient.SetCurrentSceneTransitionAsync(
                    step.TransitionName);
                await _obsClient.SetCurrentSceneTransitionDurationAsync(
                    step.TransitionDurationMilliseconds);
            }

            await _obsClient.SetCurrentProgramSceneAsync(step.ObsScene);
            await ExecuteRunOfShowSpotifyActionAsync(step);
            executionWarning =
                await ExecuteRunOfShowTwitchUpdateAsync(
                    step,
                    executionWarning);
            executionWarning =
                await ExecuteRunOfShowStreamerBotActionAsync(
                    step,
                    executionWarning);
            _appLogger.Write(
                AppLogLevel.Information,
                "RunOfShow",
                $"Regieschritt ausgeführt: {step.Name}");
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text =
                executionWarning is null
                    ? $"Ausgeführt: {step.Name}"
                    : $"Ausgeführt mit Warnung: {step.Name} – {executionWarning}";
        }
        catch (Exception ex)
        {
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text =
                "Regieschritt fehlgeschlagen: " + ex.Message;
            _appLogger.Write(
                AppLogLevel.Error,
                "RunOfShow",
                WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text);
        }
    }

    private async Task ExecuteRunOfShowSpotifyActionAsync(
        RunOfShowStepSettings step)
    {
        if (string.Equals(
                step.SpotifyAction,
                "Pause",
                StringComparison.OrdinalIgnoreCase))
        {
            await _spotifyModule.PauseAsync();
        }
        else if (string.Equals(
                     step.SpotifyAction,
                     "Resume",
                     StringComparison.OrdinalIgnoreCase))
        {
            await _spotifyModule.ResumeAsync();
        }
        else if (string.Equals(
                     step.SpotifyAction,
                     "SetVolume",
                     StringComparison.OrdinalIgnoreCase))
        {
            await _spotifyModule.SetVolumeImmediateAsync(
                step.SpotifyVolumePercent);
        }
    }

    private async Task<string?> ExecuteRunOfShowTwitchUpdateAsync(
        RunOfShowStepSettings step,
        string? executionWarning)
    {
        if (!step.UpdateTwitchChannel)
        {
            return executionWarning;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(step.TwitchTitle) &&
                string.IsNullOrWhiteSpace(step.TwitchCategoryId))
            {
                throw new InvalidOperationException(
                    "Für die Twitch-Aktualisierung ist weder ein Titel " +
                    "noch eine Kategorie eingetragen.");
            }

            await _twitchModule.UpdateChannelAsync(
                string.IsNullOrWhiteSpace(step.TwitchTitle)
                    ? null
                    : step.TwitchTitle,
                string.IsNullOrWhiteSpace(step.TwitchCategoryId)
                    ? null
                    : step.TwitchCategoryId);
            _appLogger.Write(
                AppLogLevel.Information,
                "RunOfShow.Twitch",
                $"{step.Name}: Twitch-Kanal aktualisiert.");
            return executionWarning;
        }
        catch (Exception exception)
        {
            _appLogger.Write(
                AppLogLevel.Error,
                "RunOfShow.Twitch",
                $"{step.Name}: {exception.Message}");
            if (!step.ContinueOnTwitchError)
            {
                throw;
            }

            return string.IsNullOrWhiteSpace(executionWarning)
                ? "Twitch: " + exception.Message
                : executionWarning + " | Twitch: " + exception.Message;
        }
    }

    private async Task<string?> ExecuteRunOfShowStreamerBotActionAsync(
        RunOfShowStepSettings step,
        string? executionWarning)
    {
        if (string.IsNullOrWhiteSpace(step.StreamerBotActionId) &&
            string.IsNullOrWhiteSpace(step.StreamerBotActionName))
        {
            return executionWarning;
        }

        if (step.ActionDelayMilliseconds > 0)
        {
            await Task.Delay(step.ActionDelayMilliseconds);
        }

        try
        {
            if (!_streamerBotClient.IsConnected)
            {
                throw new InvalidOperationException(
                    "Streamer.bot ist nicht verbunden.");
            }

            var action = new
            {
                id = step.StreamerBotActionId,
                name = step.StreamerBotActionName
            };
            using JsonDocument response =
                await SendStreamerBotRequestAsync(new
                {
                    request = "DoAction",
                    action,
                    args = new
                    {
                        source = "CastingCouch",
                        runOfShowStep = step.Name
                    }
                });
            string? status =
                response.RootElement.TryGetProperty(
                    "status",
                    out JsonElement statusNode)
                    ? statusNode.GetString()
                    : null;
            if (!string.Equals(
                    status,
                    "ok",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Streamer.bot hat die Regieaktion nicht bestätigt.");
            }

            return executionWarning;
        }
        catch (Exception exception)
        {
            _appLogger.Write(
                AppLogLevel.Error,
                "RunOfShow.StreamerBot",
                $"{step.Name}: {exception.Message}");
            if (!step.ContinueOnActionError)
            {
                throw;
            }

            return exception.Message;
        }
    }

    private async Task StartAutomaticRunOfShowAsync()
    {
        if (_runOfShowAutoCts is not null)
        {
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text =
                "Der automatische Regieplan läuft bereits.";
            return;
        }

        if (_runOfShowSteps.All(step => !step.Enabled))
        {
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text =
                "Es ist kein aktiver Regieschritt vorhanden.";
            return;
        }

        _runOfShowAutoCts = new CancellationTokenSource();
        CancellationToken token = _runOfShowAutoCts.Token;
        WorkflowPageViewHost.RunOfShowViewHost.StartAutomaticRunOfShowButton.IsEnabled = false;
        WorkflowPageViewHost.RunOfShowViewHost.StopAutomaticRunOfShowButton.IsEnabled = true;
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text =
            "Automatischer Regieplan gestartet.";

        try
        {
            while (!token.IsCancellationRequested)
            {
                int nextIndex =
                    RunOfShowPlanService.ProjectRuntime(
                        _runOfShowSteps,
                        _runOfShowCurrentIndex).NextEnabledIndex;
                if (nextIndex < 0)
                {
                    WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text =
                        "Automatischer Regieplan beendet.";
                    break;
                }

                RunOfShowStepSettings step = _runOfShowSteps[nextIndex];
                WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.SelectedItem =
                    step;
                WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStepsList.ScrollIntoView(
                    step);
                await ExecuteRunOfShowStepAsync(step);
                _runOfShowCurrentIndex = nextIndex;
                UpdateRunOfShowStatus();

                if (!step.AutoAdvance)
                {
                    WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text =
                        $"Automatik wartet nach: {step.Name}. Nächsten Schritt " +
                        "manuell starten oder Automatik erneut starten.";
                    break;
                }

                int delaySeconds = Math.Clamp(
                    step.AutoAdvanceDelaySeconds,
                    1,
                    86_400);
                for (int remaining = delaySeconds;
                     remaining > 0;
                     remaining--)
                {
                    token.ThrowIfCancellationRequested();
                    WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text =
                        $"{step.Name} ausgeführt. Nächster Schritt in " +
                        $"{remaining} s.";
                    await Task.Delay(TimeSpan.FromSeconds(1), token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text =
                "Automatischer Regieplan gestoppt.";
        }
        catch (Exception ex)
        {
            WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text =
                "Automatischer Regieplan fehlgeschlagen: " + ex.Message;
            _appLogger.Write(
                AppLogLevel.Error,
                "RunOfShow.Auto",
                WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText.Text);
        }
        finally
        {
            _runOfShowAutoCts?.Dispose();
            _runOfShowAutoCts = null;
            WorkflowPageViewHost.RunOfShowViewHost.StartAutomaticRunOfShowButton.IsEnabled = true;
            WorkflowPageViewHost.RunOfShowViewHost.StopAutomaticRunOfShowButton.IsEnabled = false;
        }
    }

    private void StopAutomaticRunOfShow() => _runOfShowAutoCts?.Cancel();

    private void ResetRunOfShow()
    {
        StopAutomaticRunOfShow();
        _runOfShowCurrentIndex = -1;
        UpdateRunOfShowStatus();
    }

    private void UpdateRunOfShowStatus()
    {
        if (WorkflowPageViewHost.RunOfShowViewHost.RunOfShowStatusText is null)
        {
            return;
        }

        RunOfShowRuntimeProjection projection =
            RunOfShowPlanService.ProjectRuntime(
                _runOfShowSteps,
                _runOfShowCurrentIndex);
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowCurrentText.Text =
            projection.CurrentName;
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowNextText.Text =
            projection.NextName;
        WorkflowPageViewHost.RunOfShowViewHost.RunOfShowProgressText.Text =
            projection.Progress;
    }
}
