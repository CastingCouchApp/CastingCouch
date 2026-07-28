#nullable enable

using System.Text.Json;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Modules.Spotify.Models;

namespace CreatorControlSuite.App.Shell;

public partial class MainWindow
{
    private async Task ExecuteTimedAutomationActionAsync(
        TimedAutomationRuleSettings rule,
        CancellationToken cancellationToken)
    {
        if (string.Equals(
                rule.ActionType,
                "SwitchScene",
                StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(rule.TargetScene))
            {
                throw new InvalidOperationException(
                    "Keine Zielszene gewählt.");
            }

            if (!string.IsNullOrWhiteSpace(rule.TransitionName))
            {
                await _obsClient.SetCurrentSceneTransitionAsync(
                    rule.TransitionName,
                    cancellationToken);
                await _obsClient.SetCurrentSceneTransitionDurationAsync(
                    rule.TransitionDurationMilliseconds,
                    cancellationToken);
            }

            await _obsClient.SetCurrentProgramSceneAsync(
                rule.TargetScene,
                cancellationToken);
        }
        else if (string.Equals(
                     rule.ActionType,
                     "SetSourceVisibility",
                     StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(rule.ObsScene) ||
                string.IsNullOrWhiteSpace(rule.ObsSource))
            {
                throw new InvalidOperationException(
                    "Szene und Quelle müssen gewählt sein.");
            }

            await _obsClient.SetSceneItemEnabledAsync(
                rule.ObsScene,
                rule.ObsSource,
                rule.SourceVisible,
                cancellationToken);
        }
        else if (string.Equals(
                     rule.ActionType,
                     "SetInputMute",
                     StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(rule.ObsInput))
            {
                throw new InvalidOperationException(
                    "Keine OBS-Audioquelle gewählt.");
            }

            await _obsClient.SetInputMuteAsync(
                rule.ObsInput,
                rule.InputMuted,
                cancellationToken);
        }
        else if (string.Equals(
                     rule.ActionType,
                     "StartObsStream",
                     StringComparison.OrdinalIgnoreCase))
        {
            await _obsClient.StartStreamAsync(cancellationToken);
        }
        else if (string.Equals(
                     rule.ActionType,
                     "StopObsStream",
                     StringComparison.OrdinalIgnoreCase))
        {
            await _obsClient.StopStreamAsync(cancellationToken);
        }
        else if (string.Equals(
                     rule.ActionType,
                     "StreamerBotAction",
                     StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteTimedAutomationStreamerBotActionAsync(rule);
        }
        else if (string.Equals(
                     rule.ActionType,
                     "OverlayCountdown",
                     StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(
                    rule.OverlayCountdownAction,
                    "Stop",
                    StringComparison.OrdinalIgnoreCase))
            {
                await _workflowModule.Service.StopCountdownAsync(
                    cancellationToken);
            }
            else
            {
                PersistDashboardCountdownSettings();
                int duration = rule.OverlayCountdownSeconds > 0
                    ? rule.OverlayCountdownSeconds
                    : Math.Max(
                        0,
                        _settings.Workflow.StartCountdownSeconds);
                _ = Task.Run(
                    () => duration > 0
                        ? _workflowModule.Service.StartCountdownAsync(duration)
                        : _workflowModule.Service.StartCountdownAsync(),
                    CancellationToken.None);
            }
        }

        if (!string.Equals(
                rule.SpotifyAction,
                "None",
                StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteTimedAutomationSpotifyActionAsync(
                rule,
                cancellationToken);
        }
    }

    private async Task ExecuteTimedAutomationStreamerBotActionAsync(
        TimedAutomationRuleSettings rule)
    {
        if (!_streamerBotClient.IsConnected)
        {
            throw new InvalidOperationException(
                "Streamer.bot ist nicht verbunden.");
        }

        if (string.IsNullOrWhiteSpace(rule.StreamerBotActionId) &&
            string.IsNullOrWhiteSpace(rule.StreamerBotActionName))
        {
            throw new InvalidOperationException(
                "Keine Streamer.bot-Aktion gewählt.");
        }

        using JsonDocument response =
            await SendStreamerBotRequestAsync(new
            {
                request = "DoAction",
                action = new
                {
                    id = rule.StreamerBotActionId,
                    name = rule.StreamerBotActionName
                },
                args = new
                {
                    source = "CastingCouch",
                    automationRule = rule.Name
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
                "Streamer.bot hat die Aktion nicht bestätigt.");
        }
    }

    private async Task ExecuteTimedAutomationSpotifyActionAsync(
        TimedAutomationRuleSettings rule,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource spotifyRunCts;
        string spotifyGroup = string.IsNullOrWhiteSpace(
            rule.SpotifyAutomationGroup)
            ? "Standard"
            : rule.SpotifyAutomationGroup.Trim();
        lock (_spotifyAutomationSync)
        {
            if (_spotifyAutomationCts is not null)
            {
                bool sameGroup = string.Equals(
                    spotifyGroup,
                    _activeSpotifyAutomationGroup,
                    StringComparison.OrdinalIgnoreCase);
                bool blockedByExclusiveGroup =
                    !sameGroup &&
                    (_activeSpotifyAutomationExclusive ||
                     rule.SpotifyExclusiveGroup);
                if (rule.SpotifyPriority < _activeSpotifyAutomationPriority ||
                    (blockedByExclusiveGroup &&
                     rule.SpotifyPriority <= _activeSpotifyAutomationPriority))
                {
                    string reason = blockedByExclusiveGroup
                        ? $"Gruppe '{spotifyGroup}' ist durch die aktive " +
                          $"Gruppe '{_activeSpotifyAutomationGroup}' gesperrt"
                        : $"Priorität {rule.SpotifyPriority} ist niedriger " +
                          $"als aktive Priorität " +
                          $"{_activeSpotifyAutomationPriority}";
                    AddTimedAutomationDiagnostic(
                        $"Spotify-Aktion '{rule.Name}' übersprungen: " +
                        $"{reason}.");
                    return;
                }
            }

            _spotifyAutomationCts?.Cancel();
            _spotifyAutomationCts?.Dispose();
            _spotifyAutomationCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            _activeSpotifyAutomationPriority = rule.SpotifyPriority;
            _activeSpotifyAutomationGroup = spotifyGroup;
            _activeSpotifyAutomationExclusive = rule.SpotifyExclusiveGroup;
            spotifyRunCts = _spotifyAutomationCts;
        }

        try
        {
            await ExecuteTimedAutomationSpotifyCoreAsync(
                rule,
                spotifyGroup,
                spotifyRunCts);
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

    private async Task ExecuteTimedAutomationSpotifyCoreAsync(
        TimedAutomationRuleSettings rule,
        string spotifyGroup,
        CancellationTokenSource spotifyRunCts)
    {
        CancellationToken spotifyToken = spotifyRunCts.Token;
        if (rule.SpotifyActionDelaySeconds > 0)
        {
            await Task.Delay(
                TimeSpan.FromSeconds(rule.SpotifyActionDelaySeconds),
                spotifyToken);
        }

        if (rule.SpotifySavePreviousState &&
            !string.Equals(
                rule.SpotifyAction,
                "RestorePrevious",
                StringComparison.OrdinalIgnoreCase))
        {
            await SaveSpotifyAutomationStateAsync(
                spotifyGroup,
                spotifyToken);
        }

        if (string.Equals(
                rule.SpotifyAction,
                "RestorePrevious",
                StringComparison.OrdinalIgnoreCase))
        {
            await RestoreSpotifyAutomationStateAsync(
                spotifyGroup,
                rule,
                spotifyToken);
        }
        else if (string.Equals(
                     rule.SpotifyAction,
                     "Pause",
                     StringComparison.OrdinalIgnoreCase))
        {
            await _spotifyModule.PauseAsync(spotifyToken);
        }
        else if (string.Equals(
                     rule.SpotifyAction,
                     "Resume",
                     StringComparison.OrdinalIgnoreCase))
        {
            await _spotifyModule.ResumeAsync(spotifyToken);
            await ApplySpotifyAutomationVolumeAsync(rule, spotifyToken);
        }
        else if (string.Equals(
                     rule.SpotifyAction,
                     "SetVolume",
                     StringComparison.OrdinalIgnoreCase))
        {
            await ApplySpotifyAutomationVolumeAsync(rule, spotifyToken);
        }
        else if (string.Equals(
                     rule.SpotifyAction,
                     "StartPlaylist",
                     StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(rule.SpotifyPlaylistUri))
            {
                throw new InvalidOperationException(
                    "Für die Spotify-Automation wurde keine Playlist-URI " +
                    "eingetragen.");
            }

            if (rule.SpotifyFadeSeconds > 0)
            {
                await _spotifyModule.SetVolumeImmediateAsync(
                    0,
                    spotifyToken);
            }

            await _spotifyModule.StartPlaylistAsync(
                rule.SpotifyPlaylistUri,
                applyConfiguredStartVolume: false,
                shuffleOverride: rule.SpotifyPlaylistShuffle,
                cancellationToken: spotifyToken);
            await ApplySpotifyAutomationVolumeAsync(rule, spotifyToken);
        }

        await RestoreTimedAutomationSpotifyStateWhenDueAsync(
            rule,
            spotifyGroup,
            spotifyRunCts,
            spotifyToken);
    }

    private async Task RestoreTimedAutomationSpotifyStateWhenDueAsync(
        TimedAutomationRuleSettings rule,
        string spotifyGroup,
        CancellationTokenSource spotifyRunCts,
        CancellationToken spotifyToken)
    {
        if (!rule.SpotifyAutoRestorePreviousState ||
            string.Equals(
                rule.SpotifyAction,
                "RestorePrevious",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _spotifySavedStateStore.TtlMinutes =
            GetSpotifySavedStateMaxAgeMinutes();
        if (!_spotifySavedStateStore.ContainsKey(spotifyGroup))
        {
            AddTimedAutomationDiagnostic(
                $"Spotify-Gruppe '{spotifyGroup}': Automatische Rückkehr " +
                "übersprungen, weil kein Zustand gesichert wurde.");
            return;
        }

        int restoreDelay = Math.Clamp(
            rule.SpotifyAutoRestoreDelaySeconds,
            1,
            86_400);
        string expectedScene = _automationCurrentScene;
        await _spotifyModule.RefreshPlaybackAsync(spotifyToken);
        SpotifyPlaybackState expectedPlayback =
            _spotifyModule.GetSnapshot().Playback;
        string expectedTrackUri = expectedPlayback.Track?.Uri ?? "";
        string expectedContextUri = expectedPlayback.ContextUri ?? "";
        AddTimedAutomationDiagnostic(
            $"Spotify-Gruppe '{spotifyGroup}': Automatische Rückkehr in " +
            $"{restoreDelay} Sekunden vorgemerkt.");
        await Task.Delay(
            TimeSpan.FromSeconds(restoreDelay),
            spotifyToken);

        if (rule.SpotifyAutoRestoreRequireSameScene &&
            !string.Equals(
                expectedScene,
                _automationCurrentScene,
                StringComparison.OrdinalIgnoreCase))
        {
            AddTimedAutomationDiagnostic(
                $"Spotify-Gruppe '{spotifyGroup}': Automatische Rückkehr " +
                $"verworfen, weil die OBS-Szene von '{expectedScene}' zu " +
                $"'{_automationCurrentScene}' gewechselt wurde.");
            return;
        }

        if (rule.SpotifyAutoRestoreRequireSameGroup)
        {
            lock (_spotifyAutomationSync)
            {
                if (!ReferenceEquals(
                        _spotifyAutomationCts,
                        spotifyRunCts) ||
                    !string.Equals(
                        _activeSpotifyAutomationGroup,
                        spotifyGroup,
                        StringComparison.OrdinalIgnoreCase))
                {
                    AddTimedAutomationDiagnostic(
                        $"Spotify-Gruppe '{spotifyGroup}': Automatische " +
                        "Rückkehr verworfen, weil die Gruppe nicht mehr " +
                        "aktiv ist.");
                    return;
                }
            }
        }

        if (rule.SpotifyAutoRestoreRequireUnchangedPlayback)
        {
            await _spotifyModule.RefreshPlaybackAsync(spotifyToken);
            SpotifyPlaybackState currentPlayback =
                _spotifyModule.GetSnapshot().Playback;
            string currentTrackUri = currentPlayback.Track?.Uri ?? "";
            string currentContextUri = currentPlayback.ContextUri ?? "";
            if (!string.Equals(
                    expectedTrackUri,
                    currentTrackUri,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    expectedContextUri,
                    currentContextUri,
                    StringComparison.OrdinalIgnoreCase))
            {
                AddTimedAutomationDiagnostic(
                    $"Spotify-Gruppe '{spotifyGroup}': Automatische " +
                    "Rückkehr verworfen, weil die Wiedergabe " +
                    "zwischenzeitlich geändert wurde.");
                return;
            }
        }

        await RestoreSpotifyAutomationStateAsync(
            spotifyGroup,
            rule,
            spotifyToken);
    }
}
