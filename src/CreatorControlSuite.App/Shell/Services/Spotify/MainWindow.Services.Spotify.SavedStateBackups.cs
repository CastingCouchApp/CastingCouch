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
    private void CreateSpotifySavedStateHistoryBackup(bool manual)
    {
        try
        {
            if (!File.Exists(SpotifySavedStateHistoryPersistencePath))
            {
                if (manual)
                {
                    WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Es ist noch keine lokale Verlaufshistorie vorhanden, die gesichert werden kann.";
                }

                return;
            }

            Directory.CreateDirectory(SpotifySavedStateHistoryBackupDirectory);
            string backupPath = Path.Combine(
                SpotifySavedStateHistoryBackupDirectory,
                $"spotify-saved-state-history-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(SpotifySavedStateHistoryPersistencePath, backupPath, overwrite: false);
            _lastSpotifySavedStateHistoryBackupUtc = DateTimeOffset.UtcNow;

            foreach (FileInfo? oldBackup in new DirectoryInfo(SpotifySavedStateHistoryBackupDirectory)
                         .GetFiles("spotify-saved-state-history-*.json")
                         .OrderByDescending(file => file.CreationTimeUtc)
                         .Skip(10))
            {
                oldBackup.Delete();
            }

            AddTimedAutomationDiagnostic(manual
                ? "Spotify: Manueller Wiederherstellungspunkt für den Zustandsverlauf erstellt."
                : "Spotify: Automatischer Wiederherstellungspunkt für den Zustandsverlauf erstellt.");
            RefreshSpotifySavedStateHistoryBackups();
            if (manual)
            {
                WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Wiederherstellungspunkt wurde erstellt. Es werden höchstens 10 Sicherungen aufbewahrt.";
            }
        }
        catch (Exception exception)
        {
            AddTimedAutomationDiagnostic("Spotify: Verlaufssicherung konnte nicht erstellt werden: " + exception.Message);
            if (manual)
            {
                WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Sicherung fehlgeschlagen: " + exception.Message;
            }
        }
    }

    private void RefreshSpotifySavedStateHistoryBackups()
    {
        string? selectedPath = (WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryBackupsList?.SelectedItem as SpotifySavedStateHistoryBackupItem)?.FullPath;
        _spotifySavedStateHistoryBackups.Clear();
        if (Directory.Exists(SpotifySavedStateHistoryBackupDirectory))
        {
            foreach (FileInfo? file in new DirectoryInfo(SpotifySavedStateHistoryBackupDirectory)
                         .GetFiles("spotify-saved-state-history-*.json")
                         .OrderByDescending(file => file.LastWriteTimeUtc))
            {
                _spotifySavedStateHistoryBackups.Add(new SpotifySavedStateHistoryBackupItem(
                    file.FullName,
                    $"{file.LastWriteTime:dd.MM.yyyy HH:mm:ss} · {file.Length / 1024.0:0.0} KB",
                    file.LastWriteTime,
                    file.Length));
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedPath) && WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryBackupsList is not null)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryBackupsList.SelectedItem = _spotifySavedStateHistoryBackups.FirstOrDefault(item => item.FullPath == selectedPath);
        }

        UpdateSpotifySavedStateHistoryBackupDetail();
        UpdateSpotifySavedStateHistoryBackupPreview(showStatus: false);
    }

    private void UpdateSpotifySavedStateHistoryBackupDetail()
    {
        if (WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryBackupDetailText is null)
        {
            return;
        }

        if (WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryBackupsList?.SelectedItem is not SpotifySavedStateHistoryBackupItem backup)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryBackupDetailText.Text = _spotifySavedStateHistoryBackups.Count == 0
                ? "Es sind noch keine Wiederherstellungspunkte vorhanden."
                : $"{_spotifySavedStateHistoryBackups.Count} Sicherungen vorhanden. Bitte eine Sicherung auswählen.";
            return;
        }

        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryBackupDetailText.Text =
            $"Ausgewählt: {backup.LastWriteTime:dd.MM.yyyy HH:mm:ss} · {backup.SizeBytes / 1024.0:0.0} KB\n{backup.FullPath}";
    }

    private void UpdateSpotifySavedStateHistoryBackupPreview(bool showStatus)
    {
        if (WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryBackupPreviewText is null)
        {
            return;
        }

        if (WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryBackupsList?.SelectedItem is not SpotifySavedStateHistoryBackupItem backup)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryBackupPreviewText.Text =
                "Sicherung auswählen, um Inhalt und Unterschiede zum aktuellen Verlauf anzuzeigen.";
            _spotifySavedStateHistoryBackupDifferences.Clear();
            if (showStatus)
            {
                WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Bitte zuerst einen Wiederherstellungspunkt auswählen.";
            }

            return;
        }

        try
        {
            SpotifySavedStateHistoryPersistence? state = JsonSerializer.Deserialize<SpotifySavedStateHistoryPersistence>(File.ReadAllText(backup.FullPath));
            if (state is null || state.FormatVersion != 1 || state.Entries is null)
            {
                throw new InvalidDataException("Die Sicherungsdatei enthält kein unterstütztes Verlaufsformat.");
            }

            var backupEntries = new HashSet<string>(state.Entries, StringComparer.Ordinal);
            var currentEntries = new HashSet<string>(_spotifySavedStateHistory, StringComparer.Ordinal);
            var addedEntries = backupEntries.Except(currentEntries).OrderBy(entry => entry, StringComparer.CurrentCultureIgnoreCase).ToList();
            var replacedEntries = currentEntries.Except(backupEntries).OrderBy(entry => entry, StringComparer.CurrentCultureIgnoreCase).ToList();
            var unchangedEntries = backupEntries.Intersect(currentEntries).OrderBy(entry => entry, StringComparer.CurrentCultureIgnoreCase).ToList();
            int onlyInBackup = addedEntries.Count;
            int onlyCurrent = replacedEntries.Count;
            int common = unchangedEntries.Count;
            int backupFavorites = state.FavoriteEntries?.Count ?? 0;
            int backupNotes = state.Notes?.Count(pair => !string.IsNullOrWhiteSpace(pair.Value)) ?? 0;
            int currentNotes = _spotifySavedStateHistoryNotes.Count(pair => !string.IsNullOrWhiteSpace(pair.Value));

            _spotifySavedStateHistoryBackupDifferences.Clear();
            foreach (string? entry in addedEntries.Take(50))
            {
                _spotifySavedStateHistoryBackupDifferences.Add("+ HINZUKOMMEND: " + entry);
            }

            foreach (string? entry in replacedEntries.Take(50))
            {
                _spotifySavedStateHistoryBackupDifferences.Add("− WIRD ERSETZT: " + entry);
            }

            foreach (string? entry in unchangedEntries.Take(25))
            {
                _spotifySavedStateHistoryBackupDifferences.Add("= UNVERÄNDERT: " + entry);
            }

            int hiddenDifferenceCount = Math.Max(0, addedEntries.Count - 50) + Math.Max(0, replacedEntries.Count - 50) + Math.Max(0, unchangedEntries.Count - 25);
            if (hiddenDifferenceCount > 0)
            {
                _spotifySavedStateHistoryBackupDifferences.Add($"… {hiddenDifferenceCount} weitere Vergleichseinträge werden aus Übersichtsgründen nicht angezeigt.");
            }

            if (_spotifySavedStateHistoryBackupDifferences.Count == 0)
            {
                _spotifySavedStateHistoryBackupDifferences.Add("Keine Eintragsunterschiede vorhanden.");
            }

            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryBackupPreviewText.Text =
                $"Inhalt der Sicherung:\n" +
                $"• {state.Entries.Count} Verlaufseinträge, {backupFavorites} Favoriten, {backupNotes} Notizen\n" +
                $"• Zähler: gespeichert {state.SavedCount}, wiederhergestellt {state.RestoredCount}, verworfen {state.DiscardedCount}, bereinigt {state.CleanupCount}\n" +
                $"• Filter: Suche '{(string.IsNullOrWhiteSpace(state.SearchText) ? "–" : state.SearchText)}', Aktion #{state.ActionFilterIndex}, Sortierung #{state.SortIndex}, nur Favoriten {(state.FavoritesOnly ? "ja" : "nein")}\n\n" +
                $"Vergleich mit dem aktuellen Verlauf:\n" +
                $"• {common} identische Einträge\n" +
                $"• {onlyInBackup} Einträge würden hinzukommen\n" +
                $"• {onlyCurrent} aktuelle Einträge würden ersetzt\n" +
                $"• Favoriten: aktuell {_spotifySavedStateHistoryFavorites.Count}, Sicherung {backupFavorites}\n" +
                $"• Notizen: aktuell {currentNotes}, Sicherung {backupNotes}";

            if (showStatus)
            {
                WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = $"Sicherung vom {backup.LastWriteTime:dd.MM.yyyy HH:mm:ss} wurde erfolgreich analysiert.";
            }
        }
        catch (Exception exception)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryBackupPreviewText.Text = "Vorschau nicht verfügbar: " + exception.Message;
            _spotifySavedStateHistoryBackupDifferences.Clear();
            _spotifySavedStateHistoryBackupDifferences.Add("Vorschaufehler: " + exception.Message);
            AddTimedAutomationDiagnostic("Spotify: Sicherungsvorschau konnte nicht erstellt werden: " + exception.Message);
            if (showStatus)
            {
                WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Sicherungsvorschau fehlgeschlagen: " + exception.Message;
            }
        }
    }

    private void RestoreSelectedSpotifySavedStateHistoryBackup()
    {
        if (WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryBackupsList?.SelectedItem is not SpotifySavedStateHistoryBackupItem backup)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Bitte zuerst einen Wiederherstellungspunkt auswählen.";
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            $"Den Spotify-Zustandsverlauf aus der Sicherung vom {backup.LastWriteTime:dd.MM.yyyy HH:mm:ss} wiederherstellen?\n\nDer aktuelle Verlauf wird vorher automatisch gesichert.",
            "Spotify-Verlauf wiederherstellen",
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
            Directory.CreateDirectory(Path.GetDirectoryName(SpotifySavedStateHistoryPersistencePath)!);
            File.Copy(backup.FullPath, SpotifySavedStateHistoryPersistencePath, overwrite: true);
            LoadSpotifySavedStateHistoryPersistence();
            ApplySpotifySavedStateHistorySort();
            RefreshSpotifySavedStateHistoryFilter();
            RefreshSpotifySavedStateStatistics();
            UpdateSpotifySavedStateHistoryDetail();
            RefreshSpotifySavedStateHistoryBackups();
            AddTimedAutomationDiagnostic($"Spotify: Zustandsverlauf aus Sicherung vom {backup.LastWriteTime:dd.MM.yyyy HH:mm:ss} wiederhergestellt.");
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = $"Verlauf aus dem Wiederherstellungspunkt vom {backup.LastWriteTime:dd.MM.yyyy HH:mm:ss} geladen.";
        }
        catch (Exception exception)
        {
            AddTimedAutomationDiagnostic("Spotify: Verlaufssicherung konnte nicht wiederhergestellt werden: " + exception.Message);
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Wiederherstellung fehlgeschlagen: " + exception.Message;
        }
    }
}
