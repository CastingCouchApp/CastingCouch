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
    private void RestoreSelectedSpotifySavedStateHistoryParts()
    {
        if (WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryBackupsList?.SelectedItem is not SpotifySavedStateHistoryBackupItem backup)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Bitte zuerst einen Wiederherstellungspunkt auswählen.";
            return;
        }

        bool restoreEntries = WorkflowPageViewHost.TimedAutomationViewHost.RestoreSpotifyHistoryEntriesBox.IsChecked == true;
        bool restoreFavorites = WorkflowPageViewHost.TimedAutomationViewHost.RestoreSpotifyHistoryFavoritesBox.IsChecked == true;
        bool restoreNotes = WorkflowPageViewHost.TimedAutomationViewHost.RestoreSpotifyHistoryNotesBox.IsChecked == true;
        bool restoreCounters = WorkflowPageViewHost.TimedAutomationViewHost.RestoreSpotifyHistoryCountersBox.IsChecked == true;
        bool restoreFilters = WorkflowPageViewHost.TimedAutomationViewHost.RestoreSpotifyHistoryFiltersBox.IsChecked == true;
        if (!restoreEntries && !restoreFavorites && !restoreNotes && !restoreCounters && !restoreFilters)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Bitte mindestens einen Bereich für die Wiederherstellung auswählen.";
            return;
        }

        var selectedAreas = new List<string>();
        if (restoreEntries)
        {
            selectedAreas.Add(WorkflowPageViewHost.TimedAutomationViewHost.MergeSpotifyHistoryEntriesBox.IsChecked == true ? "Verlauf (zusammenführen)" : "Verlauf (ersetzen)");
        }

        if (restoreFavorites)
        {
            selectedAreas.Add("Favoriten");
        }

        if (restoreNotes)
        {
            selectedAreas.Add("Notizen");
        }

        if (restoreCounters)
        {
            selectedAreas.Add("Statistikzähler");
        }

        if (restoreFilters)
        {
            selectedAreas.Add("Filter und Sortierung");
        }

        MessageBoxResult result = MessageBox.Show(
            $"Folgende Bereiche aus der Sicherung vom {backup.LastWriteTime:dd.MM.yyyy HH:mm:ss} laden?\n\n• {string.Join("\n• ", selectedAreas)}\n\nDer aktuelle Zustand wird vorher automatisch gesichert.",
            "Spotify-Verlauf selektiv wiederherstellen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            SpotifySavedStateHistoryPersistence? state = JsonSerializer.Deserialize<SpotifySavedStateHistoryPersistence>(File.ReadAllText(backup.FullPath));
            if (state is null || state.FormatVersion != 1 || state.Entries is null)
            {
                throw new InvalidDataException("Die Sicherungsdatei enthält kein unterstütztes Verlaufsformat.");
            }

            CreateSpotifySavedStateHistoryBackup(manual: false);
            _loadingSpotifySavedStateHistoryPersistence = true;
            try
            {
                if (restoreEntries)
                {
                    if (WorkflowPageViewHost.TimedAutomationViewHost.MergeSpotifyHistoryEntriesBox.IsChecked == true)
                    {
                        var merged = state.Entries
                            .Concat(_spotifySavedStateHistory)
                            .Distinct(StringComparer.Ordinal)
                            .Take(100)
                            .ToList();
                        _spotifySavedStateHistory.Clear();
                        foreach (string? entry in merged)
                        {
                            _spotifySavedStateHistory.Add(entry);
                        }
                    }
                    else
                    {
                        _spotifySavedStateHistory.Clear();
                        foreach (string? entry in state.Entries.Take(100))
                        {
                            _spotifySavedStateHistory.Add(entry);
                        }
                    }
                }

                if (restoreFavorites)
                {
                    _spotifySavedStateHistoryFavorites.Clear();
                    foreach (string entry in state.FavoriteEntries ?? [])
                    {
                        if (_spotifySavedStateHistory.Contains(entry))
                        {
                            _spotifySavedStateHistoryFavorites.Add(entry);
                        }
                    }
                }

                if (restoreNotes)
                {
                    _spotifySavedStateHistoryNotes.Clear();
                    foreach (KeyValuePair<string, string> pair in state.Notes ?? [])
                    {
                        if (_spotifySavedStateHistory.Contains(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                        {
                            _spotifySavedStateHistoryNotes[pair.Key] = pair.Value;
                        }
                    }
                }

                if (restoreCounters)
                {
                    _spotifySavedStateSaveCount = state.SavedCount;
                    _spotifySavedStateRestoreCount = state.RestoredCount;
                    _spotifySavedStateDiscardCount = state.DiscardedCount;
                    _spotifySavedStateCleanupCount = state.CleanupCount;
                }

                if (restoreFilters)
                {
                    WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistorySearchBox.Text = state.SearchText ?? "";
                    WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryActionFilterBox.SelectedIndex = Math.Max(0, Math.Min(state.ActionFilterIndex, WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryActionFilterBox.Items.Count - 1));
                    WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistorySortBox.SelectedIndex = Math.Max(0, Math.Min(state.SortIndex, WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistorySortBox.Items.Count - 1));
                    WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryFavoritesOnlyBox.IsChecked = state.FavoritesOnly;
                }
            }
            finally
            {
                _loadingSpotifySavedStateHistoryPersistence = false;
            }

            ApplySpotifySavedStateHistorySort();
            RefreshSpotifySavedStateHistoryFilter();
            RefreshSpotifySavedStateStatistics();
            UpdateSpotifySavedStateHistoryDetail();
            SaveSpotifySavedStateHistoryPersistence();
            UpdateSpotifySavedStateHistoryBackupPreview(showStatus: false);
            AddTimedAutomationDiagnostic($"Spotify: Bereiche aus Verlaufssicherung selektiv wiederhergestellt: {string.Join(", ", selectedAreas)}.");
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = $"Selektiv wiederhergestellt: {string.Join(", ", selectedAreas)}.";
        }
        catch (Exception exception)
        {
            AddTimedAutomationDiagnostic("Spotify: Selektive Wiederherstellung fehlgeschlagen: " + exception.Message);
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Selektive Wiederherstellung fehlgeschlagen: " + exception.Message;
        }
    }

    private void DeleteSelectedSpotifySavedStateHistoryBackup()
    {
        if (WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryBackupsList?.SelectedItem is not SpotifySavedStateHistoryBackupItem backup)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Bitte zuerst eine Sicherung zum Löschen auswählen.";
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            $"Die Sicherung vom {backup.LastWriteTime:dd.MM.yyyy HH:mm:ss} endgültig löschen?",
            "Spotify-Sicherung löschen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            File.Delete(backup.FullPath);
            RefreshSpotifySavedStateHistoryBackups();
            AddTimedAutomationDiagnostic($"Spotify: Verlaufssicherung vom {backup.LastWriteTime:dd.MM.yyyy HH:mm:ss} gelöscht.");
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Ausgewählter Wiederherstellungspunkt wurde gelöscht.";
        }
        catch (Exception exception)
        {
            AddTimedAutomationDiagnostic("Spotify: Verlaufssicherung konnte nicht gelöscht werden: " + exception.Message);
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Sicherung konnte nicht gelöscht werden: " + exception.Message;
        }
    }

    private void OpenSpotifySavedStateHistoryBackupFolder()
    {
        try
        {
            Directory.CreateDirectory(SpotifySavedStateHistoryBackupDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = SpotifySavedStateHistoryBackupDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            AddTimedAutomationDiagnostic("Spotify: Sicherungsordner konnte nicht geöffnet werden: " + exception.Message);
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Sicherungsordner konnte nicht geöffnet werden: " + exception.Message;
        }
    }
}
