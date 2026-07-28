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
    private string GetSpotifyAutomationEditorGroup()
    {
        return string.IsNullOrWhiteSpace(WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyGroupBox.Text)
            ? "Standard"
            : WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyGroupBox.Text.Trim();
    }

    private void RefreshSpotifySavedStateStatus()
    {
        string group = GetSpotifyAutomationEditorGroup();
        _spotifySavedStateStore.TtlMinutes = GetSpotifySavedStateMaxAgeMinutes();
        SpotifyAutomationSavedState? state;
        _spotifySavedStateStore.TryGet(group, out state);

        if (state is null)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifySavedStateText.Text = $"Für die Gruppe '{group}' ist kein Zustand gespeichert.";
            return;
        }

        string title = state.Track?.Name ?? "Unbekannter Titel";
        string artist = state.Track?.Artist ?? "Unbekannter Interpret";
        var position = TimeSpan.FromMilliseconds(Math.Max(0, state.ProgressMs));
        string playbackState = state.WasPlaying ? "lief" : "war pausiert";
        TimeSpan age = DateTimeOffset.UtcNow - state.SavedAtUtc;
        string expiry = _spotifySavedStateStore.IsExpired(state) ? " · ABGELAUFEN" : "";
        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifySavedStateText.Text =
            $"Gruppe '{group}': {title} – {artist} bei {position:mm\\:ss}, Lautstärke {state.VolumePercent} %, " +
            $"Shuffle {(state.ShuffleEnabled ? "an" : "aus")}, Wiederholung {state.RepeatMode}, {playbackState}. " +
            $"Gesichert vor {SpotifySavedStateStore.FormatAge(age)}{expiry}.";
    }

    private void RefreshSpotifySavedStatesOverview()
    {
        _spotifySavedStateStore.TtlMinutes = GetSpotifySavedStateMaxAgeMinutes();
        List<SpotifySavedStateOverviewItem> items = [.. _spotifySavedStateStore.Snapshot()
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry =>
            {
                SpotifyAutomationSavedState state = entry.Value;
                string title = state.Track?.Name ?? "Unbekannter Titel";
                string artist = state.Track?.Artist ?? "Unbekannter Interpret";
                var position = TimeSpan.FromMilliseconds(Math.Max(0, state.ProgressMs));
                string playbackState = state.WasPlaying ? "lief" : "pausiert";
                TimeSpan age = DateTimeOffset.UtcNow - state.SavedAtUtc;
                bool expired = _spotifySavedStateStore.IsExpired(state);
                string prefix = expired ? "[ABGELAUFEN] " : "";
                string summary = $"{prefix}{entry.Key} · {title} – {artist} · {position:mm\\:ss} · {state.VolumePercent} % · {playbackState} · vor {SpotifySavedStateStore.FormatAge(age)}";
                return new SpotifySavedStateOverviewItem(entry.Key, summary, expired);
            })];

        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStatesOverviewList.ItemsSource = items;
        int expiredCount = items.Count(item => item.IsExpired);
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStatesOverviewStatusText.Text = items.Count == 0
            ? "Es ist aktuell kein Spotify-Zustand gespeichert."
            : expiredCount == 0
                ? $"{items.Count} gespeicherte Spotify-Zustände gefunden."
                : $"{items.Count} gespeicherte Spotify-Zustände gefunden · {expiredCount} abgelaufen.";
    }

    private void UpdateSpotifySavedStatesOverviewSelection()
    {
        if (WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStatesOverviewList.SelectedItem is not SpotifySavedStateOverviewItem item)
        {
            return;
        }

        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStatesOverviewStatusText.Text = item.IsExpired
            ? $"Ausgewählt: Gruppe '{item.Group}' · Zustand ist abgelaufen, kann aber weiterhin manuell wiederhergestellt werden."
            : $"Ausgewählt: Gruppe '{item.Group}'.";
    }

    private async Task RestoreSelectedSpotifySavedStateAsync()
    {
        if (WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStatesOverviewList.SelectedItem is not SpotifySavedStateOverviewItem item)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStatesOverviewStatusText.Text = "Bitte zuerst einen gespeicherten Zustand auswählen.";
            return;
        }

        WorkflowPageViewHost.TimedAutomationViewHost.TimedAutomationSpotifyGroupBox.Text = item.Group;
        await RestoreSpotifySavedStateNowAsync();
        RefreshSpotifySavedStatesOverview();
    }

    private void DiscardSelectedSpotifySavedState()
    {
        if (WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStatesOverviewList.SelectedItem is not SpotifySavedStateOverviewItem item)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStatesOverviewStatusText.Text = "Bitte zuerst einen gespeicherten Zustand auswählen.";
            return;
        }

        bool removed = _spotifySavedStateStore.Remove(item.Group);

        AddTimedAutomationDiagnostic(removed
            ? $"Spotify-Gruppe '{item.Group}': Gespeicherter Zustand wurde über die Übersicht verworfen."
            : $"Spotify-Gruppe '{item.Group}': Zustand war bereits nicht mehr vorhanden.");
        if (removed)
        {
            _spotifySavedStateDiscardCount++;
            AddSpotifySavedStateHistory($"{item.Group}: Zustand über Übersicht verworfen");
        }
        RefreshSpotifySavedStateStatus();
        RefreshSpotifySavedStatesOverview();
    }

    private void DiscardAllSpotifySavedStates()
    {
        int count = _spotifySavedStateStore.Count;
        _spotifySavedStateStore.Clear();

        AddTimedAutomationDiagnostic(count == 0
            ? "Spotify: Es waren keine gespeicherten Zustände vorhanden."
            : $"Spotify: {count} gespeicherte Zustände wurden verworfen.");
        if (count > 0)
        {
            _spotifySavedStateDiscardCount += count;
            AddSpotifySavedStateHistory($"Alle Zustände verworfen ({count})");
        }
        RefreshSpotifySavedStateStatus();
        RefreshSpotifySavedStatesOverview();
    }


    private void LoadSpotifySavedStateHistoryPersistence()
    {
        _loadingSpotifySavedStateHistoryPersistence = true;
        try
        {
            if (!File.Exists(SpotifySavedStateHistoryPersistencePath))
            {
                return;
            }

            SpotifySavedStateHistoryPersistence? state = JsonSerializer.Deserialize<SpotifySavedStateHistoryPersistence>(
                File.ReadAllText(SpotifySavedStateHistoryPersistencePath));
            if (state is null || state.FormatVersion != 1 || state.Entries is null)
            {
                return;
            }

            _spotifySavedStateHistory.Clear();
            _spotifySavedStateHistoryFavorites.Clear();
            _spotifySavedStateHistoryNotes.Clear();
            foreach (string? entry in state.Entries.Where(entry => !string.IsNullOrWhiteSpace(entry)).Take(100))
            {
                _spotifySavedStateHistory.Add(entry);
            }

            foreach (string entry in state.FavoriteEntries ?? [])
            {
                if (_spotifySavedStateHistory.Contains(entry))
                {
                    _spotifySavedStateHistoryFavorites.Add(entry);
                }
            }

            foreach (KeyValuePair<string, string> pair in state.Notes ?? [])
            {
                if (_spotifySavedStateHistory.Contains(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                {
                    _spotifySavedStateHistoryNotes[pair.Key] = pair.Value;
                }
            }

            _spotifySavedStateSaveCount = Math.Max(0, state.SavedCount);
            _spotifySavedStateRestoreCount = Math.Max(0, state.RestoredCount);
            _spotifySavedStateDiscardCount = Math.Max(0, state.DiscardedCount);
            _spotifySavedStateCleanupCount = Math.Max(0, state.CleanupCount);
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistorySearchBox.Text = state.SearchText ?? "";
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryActionFilterBox.SelectedIndex = Math.Clamp(state.ActionFilterIndex, 0, Math.Max(0, WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryActionFilterBox.Items.Count - 1));
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistorySortBox.SelectedIndex = Math.Clamp(state.SortIndex, 0, Math.Max(0, WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistorySortBox.Items.Count - 1));
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryFavoritesOnlyBox.IsChecked = state.FavoritesOnly;
            AddTimedAutomationDiagnostic($"Spotify: {_spotifySavedStateHistory.Count} gespeicherte Verlaufseinträge aus der lokalen Sitzungshistorie geladen.");
        }
        catch (Exception exception)
        {
            AddTimedAutomationDiagnostic("Spotify: Lokale Sitzungshistorie konnte nicht geladen werden: " + exception.Message);
        }
        finally
        {
            _loadingSpotifySavedStateHistoryPersistence = false;
        }
    }

    private void SaveSpotifySavedStateHistoryPersistence()
    {
        if (_loadingSpotifySavedStateHistoryPersistence)
        {
            return;
        }

        try
        {
            string? directory = Path.GetDirectoryName(SpotifySavedStateHistoryPersistencePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var state = new SpotifySavedStateHistoryPersistence(
                1,
                _spotifySavedStateSaveCount,
                _spotifySavedStateRestoreCount,
                _spotifySavedStateDiscardCount,
                _spotifySavedStateCleanupCount,
                [.. _spotifySavedStateHistory],
                [.. _spotifySavedStateHistoryFavorites],
                new Dictionary<string, string>(_spotifySavedStateHistoryNotes),
                WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistorySearchBox?.Text ?? "",
                WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryActionFilterBox?.SelectedIndex ?? 0,
                WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistorySortBox?.SelectedIndex ?? 0,
                WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryFavoritesOnlyBox?.IsChecked == true);
            if (File.Exists(SpotifySavedStateHistoryPersistencePath) &&
                DateTimeOffset.UtcNow - _lastSpotifySavedStateHistoryBackupUtc >= TimeSpan.FromMinutes(30))
            {
                CreateSpotifySavedStateHistoryBackup(manual: false);
            }
            string temporaryPath = SpotifySavedStateHistoryPersistencePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, SpotifySavedStateHistoryPersistencePath, true);
        }
        catch (Exception exception)
        {
            AddTimedAutomationDiagnostic("Spotify: Lokale Sitzungshistorie konnte nicht gespeichert werden: " + exception.Message);
        }
    }
}
