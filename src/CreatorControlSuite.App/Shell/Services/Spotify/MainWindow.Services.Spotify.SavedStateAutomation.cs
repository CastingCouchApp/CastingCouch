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
    private int GetSpotifySavedStateMaxAgeMinutes()
    {
        return int.TryParse(WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateMaxAgeBox.Text, out int minutes)
            ? Math.Clamp(minutes, 1, 10080)
            : 180;
    }


    private int GetSpotifySavedStateCleanupIntervalMinutes()
    {
        return int.TryParse(WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateCleanupIntervalMinutesBox.Text, out int minutes)
            ? Math.Clamp(minutes, 1, 1440)
            : 15;
    }

    private void UpdateSpotifySavedStateCleanupTimer()
    {
        if (WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateCleanupIntervalBox is null || WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateCleanupIntervalMinutesBox is null)
        {
            return;
        }

        _spotifySavedStateCleanupTimer.Stop();
        int minutes = GetSpotifySavedStateCleanupIntervalMinutes();
        _spotifySavedStateCleanupTimer.Interval = TimeSpan.FromMinutes(minutes);

        if (WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateCleanupIntervalBox.IsChecked == true)
        {
            _spotifySavedStateCleanupTimer.Start();
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStatesOverviewStatusText.Text = $"Automatische Bereinigung ist aktiv · alle {minutes} Minuten.";
        }
    }

    private void DiscardExpiredSpotifySavedStates(string reason, bool onlyLogWhenRemoved = false)
    {
        _spotifySavedStateStore.TtlMinutes = GetSpotifySavedStateMaxAgeMinutes();
        _spotifySavedStateStore.DiscardExpired(out IReadOnlyList<string> expiredGroups);

        if (expiredGroups.Count > 0)
        {
            _spotifySavedStateCleanupCount += expiredGroups.Count;
            AddSpotifySavedStateHistory($"Bereinigung ({reason}): {expiredGroups.Count} entfernt");
            AddTimedAutomationDiagnostic($"Spotify ({reason}): {expiredGroups.Count} abgelaufene Zustände verworfen ({string.Join(", ", expiredGroups)}).");
        }
        else if (!onlyLogWhenRemoved)
        {
            AddTimedAutomationDiagnostic($"Spotify ({reason}): Keine abgelaufenen gespeicherten Zustände gefunden.");
        }

        RefreshSpotifySavedStateStatus();
        RefreshSpotifySavedStatesOverview();
    }

    private async Task RestoreSpotifySavedStateNowAsync()
    {
        string group = GetSpotifyAutomationEditorGroup();
        int fadeSeconds = int.TryParse(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyFadeBox.Text, out int fade)
            ? Math.Clamp(fade, 0, 300)
            : 0;
        var restoreRule = new TimedAutomationRuleSettings { SpotifyFadeSeconds = fadeSeconds };

        CancellationTokenSource restoreCts;
        lock (_spotifyAutomationSync)
        {
            _spotifyAutomationCts?.Cancel();
            _spotifyAutomationCts?.Dispose();
            _spotifyAutomationCts = new CancellationTokenSource();
            _activeSpotifyAutomationPriority = int.MaxValue;
            _activeSpotifyAutomationGroup = group;
            _activeSpotifyAutomationExclusive = true;
            restoreCts = _spotifyAutomationCts;
        }

        try
        {
            await RestoreSpotifyAutomationStateAsync(group, restoreRule, restoreCts.Token);
            RefreshSpotifySavedStateStatus();
        }
        catch (OperationCanceledException)
        {
            AddTimedAutomationDiagnostic($"Spotify-Gruppe '{group}': Manuelle Wiederherstellung abgebrochen.");
        }
        catch (Exception ex)
        {
            AddTimedAutomationDiagnostic($"Spotify-Gruppe '{group}': Manuelle Wiederherstellung fehlgeschlagen: {ex.Message}");
            WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifySavedStateText.Text = ex.Message;
        }
        finally
        {
            lock (_spotifyAutomationSync)
            {
                if (ReferenceEquals(_spotifyAutomationCts, restoreCts))
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

    private void DiscardSpotifySavedState()
    {
        string group = GetSpotifyAutomationEditorGroup();
        bool removed;
        lock (_spotifyAutomationSync)
        {
            removed = _spotifySavedStateStore.Remove(group);
        }

        AddTimedAutomationDiagnostic(removed
            ? $"Spotify-Gruppe '{group}': Gespeicherter Zustand wurde verworfen."
            : $"Spotify-Gruppe '{group}': Es war kein gespeicherter Zustand vorhanden.");
        if (removed)
        {
            _spotifySavedStateDiscardCount++;
            AddSpotifySavedStateHistory($"{group}: Zustand verworfen");
        }
        RefreshSpotifySavedStateStatus();
        RefreshSpotifySavedStatesOverview();
    }

    private async Task SaveSpotifyAutomationStateAsync(string group, CancellationToken cancellationToken)
    {
        if (WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateCleanupOnSaveBox.IsChecked == true)
        {
            DiscardExpiredSpotifySavedStates("vor neuem Speichern", onlyLogWhenRemoved: true);
        }

        await _spotifyModule.RefreshPlaybackAsync(cancellationToken);
        SpotifyPlaybackState playback = _spotifyModule.GetSnapshot().Playback;
        if (!playback.HasPlayback || playback.Track is null)
        {
            AddTimedAutomationDiagnostic($"Spotify-Gruppe '{group}': Kein aktiver Wiedergabezustand zum Sichern vorhanden.");
            return;
        }

        var state = new SpotifyAutomationSavedState(
            playback.ContextUri ?? "",
            playback.Track,
            Math.Max(0, playback.ProgressMs),
            Math.Clamp(playback.Device?.VolumePercent ?? 0, 0, 100),
            playback.ShuffleEnabled,
            string.IsNullOrWhiteSpace(playback.RepeatMode) ? "off" : playback.RepeatMode,
            playback.IsPlaying,
            DateTimeOffset.UtcNow);
        lock (_spotifyAutomationSync)
        {
            _spotifySavedStateStore.Set(group, state);
        }

        _spotifySavedStateSaveCount++;
        AddSpotifySavedStateHistory($"{group}: '{playback.Track.Name}' gespeichert");
        AddTimedAutomationDiagnostic($"Spotify-Gruppe '{group}': Wiedergabe '{playback.Track.Name}' gesichert.");
        Dispatcher.Invoke(() =>
        {
            RefreshSpotifySavedStateStatus();
            RefreshSpotifySavedStatesOverview();
        });
    }

    private async Task RestoreSpotifyAutomationStateAsync(string group, TimedAutomationRuleSettings rule, CancellationToken cancellationToken)
    {
        SpotifyAutomationSavedState? state;
        _spotifySavedStateStore.TryGet(group, out state);

        if (state is null)
        {
            throw new InvalidOperationException($"Für die Spotify-Gruppe '{group}' wurde noch kein vorheriger Wiedergabezustand gesichert.");
        }

        if (rule.SpotifyFadeSeconds > 0)
        {
            await _spotifyModule.SetVolumeImmediateAsync(0, cancellationToken);
        }

        await _spotifyModule.SetRepeatAsync(state.RepeatMode, cancellationToken);

        if (!string.IsNullOrWhiteSpace(state.ContextUri))
        {
            await _spotifyModule.StartPlaylistAsync(
                state.ContextUri,
                applyConfiguredStartVolume: false,
                shuffleOverride: state.ShuffleEnabled,
                offsetTrackUri: state.Track?.Uri,
                cancellationToken: cancellationToken);
        }
        else if (state.Track is not null)
        {
            await _spotifyModule.PlayTrackAsync(state.Track, cancellationToken);
            await _spotifyModule.SetShuffleAsync(state.ShuffleEnabled, cancellationToken);
        }

        if (state.ProgressMs > 0)
        {
            // Kurz warten, bis Spotify den Kontext/Track aktiviert hat, bevor Seek greift.
            await Task.Delay(350, cancellationToken);
            await _spotifyModule.SeekImmediateAsync(state.ProgressMs, cancellationToken);
        }

        var restoreVolumeRule = new TimedAutomationRuleSettings
        {
            SpotifyVolumePercent = state.VolumePercent,
            SpotifyFadeSeconds = rule.SpotifyFadeSeconds
        };
        await ApplySpotifyAutomationVolumeAsync(restoreVolumeRule, cancellationToken);
        if (!state.WasPlaying)
        {
            await _spotifyModule.PauseAsync(cancellationToken);
        }

        lock (_spotifyAutomationSync)
        {
            _spotifySavedStateStore.Remove(group);
        }

        _spotifySavedStateRestoreCount++;
        AddSpotifySavedStateHistory($"{group}: Wiedergabe wiederhergestellt");
        AddTimedAutomationDiagnostic($"Spotify-Gruppe '{group}': Vorherige Wiedergabe wiederhergestellt.");
        Dispatcher.Invoke(() =>
        {
            RefreshSpotifySavedStateStatus();
            RefreshSpotifySavedStatesOverview();
        });
    }

    private async Task ApplySpotifyAutomationVolumeAsync(TimedAutomationRuleSettings rule, CancellationToken cancellationToken)
    {
        int target = Math.Clamp(rule.SpotifyVolumePercent, 0, 100);
        if (rule.SpotifyFadeSeconds <= 0)
        {
            await _spotifyModule.SetVolumeImmediateAsync(target, cancellationToken);
            return;
        }

        await _spotifyModule.RefreshPlaybackAsync(cancellationToken);
        int current = _spotifyModule.GetSnapshot().Playback.Device?.VolumePercent ?? 0;
        int steps = Math.Max(1, Math.Min(rule.SpotifyFadeSeconds * 4, 120));
        var delay = TimeSpan.FromMilliseconds(rule.SpotifyFadeSeconds * 1000d / steps);
        for (int step = 1; step <= steps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int volume = (int)Math.Round(current + ((target - current) * (step / (double)steps)));
            await _spotifyModule.SetVolumeImmediateAsync(Math.Clamp(volume, 0, 100), cancellationToken);
            if (step < steps)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }
    }
}
