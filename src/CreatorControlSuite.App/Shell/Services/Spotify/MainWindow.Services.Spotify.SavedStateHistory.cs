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
    private void AddSpotifySavedStateHistory(string message)
    {
        _spotifySavedStateHistory.Insert(0, $"{DateTime.Now:HH:mm:ss} · {message}");
        while (_spotifySavedStateHistory.Count > 100)
        {
            _spotifySavedStateHistory.RemoveAt(_spotifySavedStateHistory.Count - 1);
        }

        RefreshSpotifySavedStateStatistics();
        SaveSpotifySavedStateHistoryPersistence();
    }

    private bool SpotifySavedStateHistoryMatchesFilter(object item)
    {
        if (item is not string entry)
        {
            return false;
        }

        string search = WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistorySearchBox?.Text?.Trim() ?? "";
        string note = _spotifySavedStateHistoryNotes.TryGetValue(entry, out string? savedNote) ? savedNote : "";
        if (!string.IsNullOrWhiteSpace(search) &&
            entry.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
            note.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        if (WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryFavoritesOnlyBox?.IsChecked == true && !_spotifySavedStateHistoryFavorites.Contains(entry))
        {
            return false;
        }

        string action = (WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryActionFilterBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
        return action switch
        {
            "save" => entry.Contains("gespeichert", StringComparison.OrdinalIgnoreCase),
            "restore" => entry.Contains("wiederhergestellt", StringComparison.OrdinalIgnoreCase),
            "discard" => entry.Contains("verworfen", StringComparison.OrdinalIgnoreCase),
            "cleanup" => entry.Contains("Bereinigung", StringComparison.OrdinalIgnoreCase) || entry.Contains("bereinigt", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private void RefreshSpotifySavedStateHistoryFilter()
    {
        _spotifySavedStateHistoryView?.Refresh();
        RefreshSpotifySavedStateStatistics();
    }

    private void ResetSpotifySavedStateHistoryFilter()
    {
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistorySearchBox.Text = "";
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryActionFilterBox.SelectedIndex = 0;
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistorySortBox.SelectedIndex = 0;
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryFavoritesOnlyBox.IsChecked = false;
        RefreshSpotifySavedStateHistoryFilter();
    }

    private void ApplySpotifySavedStateHistorySort()
    {
        if (_spotifySavedStateHistoryView is not ListCollectionView listView)
        {
            return;
        }

        string mode = (WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistorySortBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "newest";
        listView.CustomSort = new SpotifySavedStateHistoryComparer(mode);
        UpdateSpotifySavedStateHistoryDetail();
        RefreshSpotifySavedStateStatistics();
    }

    private void UpdateSpotifySavedStateHistoryDetail()
    {
        if (WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryDetailText is null)
        {
            return;
        }

        List<string> selectedEntries = WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryList?.SelectedItems.Cast<object>().OfType<string>().ToList() ?? [];
        if (selectedEntries.Count == 0)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryDetailText.Text = "Kein Verlaufseintrag ausgewählt.";
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryNoteBox.Text = "";
            WorkflowPageViewHost.TimedAutomationViewHost.ToggleSpotifySavedStateHistoryFavoriteButton.Content = "ALS FAVORIT MARKIEREN";
            return;
        }
        if (selectedEntries.Count > 1)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryDetailText.Text = $"{selectedEntries.Count} Verlaufseinträge ausgewählt. Die Sammelaktionen können diese Auswahl exportieren oder entfernen.";
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryNoteBox.Text = "";
            WorkflowPageViewHost.TimedAutomationViewHost.ToggleSpotifySavedStateHistoryFavoriteButton.Content = "ALS FAVORIT MARKIEREN";
            return;
        }

        string entry = selectedEntries[0];
        int separator = entry.IndexOf(" · ", StringComparison.Ordinal);
        string time = separator >= 0 ? entry[..separator] : "Unbekannt";
        string message = separator >= 0 ? entry[(separator + 3)..] : entry;
        int groupSeparator = message.IndexOf(':');
        string group = groupSeparator > 0 ? message[..groupSeparator].Trim() : "Allgemein";
        string action = groupSeparator > 0 ? message[(groupSeparator + 1)..].Trim() : message;
        string favorite = _spotifySavedStateHistoryFavorites.Contains(entry) ? "Ja" : "Nein";
        string note = _spotifySavedStateHistoryNotes.TryGetValue(entry, out string? savedNote) ? savedNote : "";
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryDetailText.Text = $"Zeit: {time}\nGruppe: {group}\nAktion: {action}\nFavorit: {favorite}";
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryNoteBox.Text = note;
        WorkflowPageViewHost.TimedAutomationViewHost.ToggleSpotifySavedStateHistoryFavoriteButton.Content = favorite == "Ja" ? "FAVORIT ENTFERNEN" : "ALS FAVORIT MARKIEREN";
    }

    private void ToggleSpotifySavedStateHistoryFavorite()
    {
        List<string> selected = GetSelectedSpotifySavedStateHistory();
        if (selected.Count != 1)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Bitte genau einen Verlaufseintrag auswählen.";
            return;
        }
        string entry = selected[0];
        if (!_spotifySavedStateHistoryFavorites.Add(entry))
        {
            _spotifySavedStateHistoryFavorites.Remove(entry);
        }

        UpdateSpotifySavedStateHistoryDetail();
        RefreshSpotifySavedStateHistoryFilter();
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = _spotifySavedStateHistoryFavorites.Contains(entry) ? "Eintrag als Favorit markiert." : "Favoritenmarkierung entfernt.";
        SaveSpotifySavedStateHistoryPersistence();
    }

    private void SaveSpotifySavedStateHistoryNote()
    {
        List<string> selected = GetSelectedSpotifySavedStateHistory();
        if (selected.Count != 1)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Bitte genau einen Verlaufseintrag für die Notiz auswählen.";
            return;
        }
        string entry = selected[0];
        string note = WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryNoteBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(note))
        {
            _spotifySavedStateHistoryNotes.Remove(entry);
        }
        else
        {
            _spotifySavedStateHistoryNotes[entry] = note;
        }

        RefreshSpotifySavedStateHistoryFilter();
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = string.IsNullOrWhiteSpace(note) ? "Notiz entfernt." : "Notiz gespeichert.";
        SaveSpotifySavedStateHistoryPersistence();
    }

    private List<string> GetFilteredSpotifySavedStateHistory()
    {
        if (_spotifySavedStateHistoryView is null)
        {
            return [.. _spotifySavedStateHistory];
        }

        return [.. _spotifySavedStateHistoryView.Cast<object>().OfType<string>()];
    }

    private List<string> GetSelectedSpotifySavedStateHistory()
    {
        return WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryList?.SelectedItems.Cast<object>().OfType<string>().ToList() ?? [];
    }

    private void SelectVisibleSpotifySavedStateHistory()
    {
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryList.SelectedItems.Clear();
        foreach (string entry in GetFilteredSpotifySavedStateHistory())
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryList.SelectedItems.Add(entry);
        }

        UpdateSpotifySavedStateHistoryDetail();
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = $"{WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryList.SelectedItems.Count} sichtbare Verlaufseinträge ausgewählt.";
    }

    private void RemoveSelectedSpotifySavedStateHistory()
    {
        List<string> selected = GetSelectedSpotifySavedStateHistory();
        if (selected.Count == 0)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Bitte zuerst mindestens einen Verlaufseintrag auswählen.";
            return;
        }

        foreach (string entry in selected)
        {
            _spotifySavedStateHistory.Remove(entry);
            _spotifySavedStateHistoryFavorites.Remove(entry);
            _spotifySavedStateHistoryNotes.Remove(entry);
        }
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryList.SelectedItems.Clear();
        UpdateSpotifySavedStateHistoryDetail();
        RefreshSpotifySavedStateStatistics();
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = $"{selected.Count} ausgewählte Verlaufseinträge entfernt.";
        SaveSpotifySavedStateHistoryPersistence();
        AddTimedAutomationDiagnostic($"Spotify: {selected.Count} ausgewählte Zustandsverlaufseinträge entfernt.");
    }

    private void ExportSelectedSpotifySavedStateHistory()
    {
        List<string> entries = GetSelectedSpotifySavedStateHistory();
        if (entries.Count == 0)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Für den JSON-Export wurde kein Verlaufseintrag ausgewählt.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Spotify-Zustandsverlauf (*.json)|*.json",
            FileName = $"spotify-zustandsverlauf-auswahl-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            AddExtension = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var export = new SpotifySavedStateHistoryExport(
                2, DateTimeOffset.UtcNow, _spotifySavedStateSaveCount, _spotifySavedStateRestoreCount,
                _spotifySavedStateDiscardCount, _spotifySavedStateCleanupCount, entries,
                [.. entries.Where(_spotifySavedStateHistoryFavorites.Contains)],
                _spotifySavedStateHistoryNotes.Where(pair => entries.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value));
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true }));
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = $"{entries.Count} ausgewählte Einträge als JSON exportiert: {Path.GetFileName(dialog.FileName)}";
            AddTimedAutomationDiagnostic($"Spotify: {entries.Count} ausgewählte Zustandsverlaufseinträge als JSON exportiert.");
        }
        catch (Exception exception)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Auswahl-Export fehlgeschlagen: " + exception.Message;
            AddTimedAutomationDiagnostic("Spotify: JSON-Auswahlexport fehlgeschlagen: " + exception.Message);
        }
    }

    private void ExportSelectedSpotifySavedStateHistoryCsv()
    {
        List<string> entries = GetSelectedSpotifySavedStateHistory();
        if (entries.Count == 0)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Für den CSV-Export wurde kein Verlaufseintrag ausgewählt.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV-Datei (*.csv)|*.csv",
            FileName = $"spotify-zustandsverlauf-auswahl-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            string csv = BuildSpotifySavedStateHistoryCsv(entries);
            File.WriteAllText(dialog.FileName, csv, new UTF8Encoding(true));
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = $"{entries.Count} ausgewählte Einträge als CSV exportiert: {Path.GetFileName(dialog.FileName)}";
            AddTimedAutomationDiagnostic($"Spotify: {entries.Count} ausgewählte Zustandsverlaufseinträge als CSV exportiert.");
        }
        catch (Exception exception)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "CSV-Auswahlexport fehlgeschlagen: " + exception.Message;
            AddTimedAutomationDiagnostic("Spotify: CSV-Auswahlexport fehlgeschlagen: " + exception.Message);
        }
    }

    private string BuildSpotifySavedStateHistoryCsv(IEnumerable<string> entries)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Zeit;Aktion;Favorit;Notiz");
        foreach (string entry in entries)
        {
            int separator = entry.IndexOf(" · ", StringComparison.Ordinal);
            string time = separator >= 0 ? entry[..separator] : "";
            string action = separator >= 0 ? entry[(separator + 3)..] : entry;
            csv.Append('"').Append(time.Replace("\"", "\"\"")).Append("\";\"")
               .Append(action.Replace("\"", "\"\"")).Append("\";\"")
               .Append(_spotifySavedStateHistoryFavorites.Contains(entry) ? "Ja" : "Nein").Append("\";\"")
               .Append((_spotifySavedStateHistoryNotes.TryGetValue(entry, out string? note) ? note : "").Replace("\"", "\"\"")).AppendLine("\"");
        }
        return csv.ToString();
    }

    private void ExportSpotifySavedStateHistoryCsv()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV-Datei (*.csv)|*.csv",
            FileName = $"spotify-zustandsverlauf-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            List<string> entries = GetFilteredSpotifySavedStateHistory();
            File.WriteAllText(dialog.FileName, BuildSpotifySavedStateHistoryCsv(entries), new UTF8Encoding(true));
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = $"{entries.Count} gefilterte Verlaufseinträge als CSV exportiert: {Path.GetFileName(dialog.FileName)}";
            AddTimedAutomationDiagnostic($"Spotify: {entries.Count} gefilterte Zustandsverlaufseinträge als CSV exportiert.");
        }
        catch (Exception exception)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "CSV-Export fehlgeschlagen: " + exception.Message;
            AddTimedAutomationDiagnostic("Spotify: CSV-Export des Zustandsverlaufs fehlgeschlagen: " + exception.Message);
        }
    }

    private void ExportSpotifySavedStateHistory()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Spotify-Zustandsverlauf (*.json)|*.json",
            FileName = $"spotify-zustandsverlauf-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            AddExtension = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var export = new SpotifySavedStateHistoryExport(
                2,
                DateTimeOffset.UtcNow,
                _spotifySavedStateSaveCount,
                _spotifySavedStateRestoreCount,
                _spotifySavedStateDiscardCount,
                _spotifySavedStateCleanupCount,
                [.. _spotifySavedStateHistory],
                [.. _spotifySavedStateHistoryFavorites],
                new Dictionary<string, string>(_spotifySavedStateHistoryNotes));
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true }));
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = $"Verlauf exportiert: {Path.GetFileName(dialog.FileName)}";
            AddTimedAutomationDiagnostic($"Spotify: Zustandsverlauf mit {_spotifySavedStateHistory.Count} Einträgen exportiert.");
        }
        catch (Exception exception)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Export fehlgeschlagen: " + exception.Message;
            AddTimedAutomationDiagnostic("Spotify: Export des Zustandsverlaufs fehlgeschlagen: " + exception.Message);
        }
    }

    private void ImportSpotifySavedStateHistory()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Spotify-Zustandsverlauf (*.json)|*.json|JSON (*.json)|*.json",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            SpotifySavedStateHistoryExport? import = JsonSerializer.Deserialize<SpotifySavedStateHistoryExport>(File.ReadAllText(dialog.FileName));
            if (import is null || import.FormatVersion is < 1 or > 2 || import.Entries is null)
            {
                throw new InvalidDataException("Die Datei besitzt kein unterstütztes Verlaufsformat.");
            }

            _spotifySavedStateHistory.Clear();
            _spotifySavedStateHistoryFavorites.Clear();
            _spotifySavedStateHistoryNotes.Clear();
            foreach (string? entry in import.Entries.Where(entry => !string.IsNullOrWhiteSpace(entry)).Take(100))
            {
                _spotifySavedStateHistory.Add(entry);
            }

            _spotifySavedStateSaveCount = Math.Max(0, import.SavedCount);
            _spotifySavedStateRestoreCount = Math.Max(0, import.RestoredCount);
            _spotifySavedStateDiscardCount = Math.Max(0, import.DiscardedCount);
            _spotifySavedStateCleanupCount = Math.Max(0, import.CleanupCount);
            foreach (string entry in import.FavoriteEntries ?? [])
            {
                if (_spotifySavedStateHistory.Contains(entry))
                {
                    _spotifySavedStateHistoryFavorites.Add(entry);
                }
            }

            foreach (KeyValuePair<string, string> pair in import.Notes ?? [])
            {
                if (_spotifySavedStateHistory.Contains(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                {
                    _spotifySavedStateHistoryNotes[pair.Key] = pair.Value;
                }
            }

            RefreshSpotifySavedStateStatistics();
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = $"Verlauf importiert: {_spotifySavedStateHistory.Count} Einträge aus {Path.GetFileName(dialog.FileName)}.";
            AddTimedAutomationDiagnostic($"Spotify: Zustandsverlauf mit {_spotifySavedStateHistory.Count} Einträgen importiert.");
            SaveSpotifySavedStateHistoryPersistence();
        }
        catch (Exception exception)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Import fehlgeschlagen: " + exception.Message;
            AddTimedAutomationDiagnostic("Spotify: Import des Zustandsverlaufs fehlgeschlagen: " + exception.Message);
        }
    }

    private void ClearSpotifySavedStateHistory()
    {
        _spotifySavedStateHistory.Clear();
        _spotifySavedStateHistoryFavorites.Clear();
        _spotifySavedStateHistoryNotes.Clear();
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryList.SelectedItem = null;
        UpdateSpotifySavedStateHistoryDetail();
        AddTimedAutomationDiagnostic("Spotify: Verlauf der gespeicherten Zustände wurde geleert.");
        RefreshSpotifySavedStateStatistics();
        SaveSpotifySavedStateHistoryPersistence();
    }

    private void RefreshSpotifySavedStateStatistics()
    {
        if (WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateStatisticsText is null)
        {
            return;
        }

        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateStatisticsText.Text =
            $"Gespeichert: {_spotifySavedStateSaveCount} · Wiederhergestellt: {_spotifySavedStateRestoreCount} · " +
            $"Verworfen: {_spotifySavedStateDiscardCount} · Automatisch bereinigt: {_spotifySavedStateCleanupCount} · " +
            $"Aktuell vorhanden: {_spotifySavedStateStore.Count}";
        int visibleCount = _spotifySavedStateHistoryView?.Cast<object>().Count() ?? _spotifySavedStateHistory.Count;
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = _spotifySavedStateHistory.Count == 0
            ? "Noch keine Zustandsaktionen in dieser Programmsitzung."
            : visibleCount == _spotifySavedStateHistory.Count
                ? $"{_spotifySavedStateHistory.Count} Einträge · neuester Eintrag oben."
                : $"{visibleCount} von {_spotifySavedStateHistory.Count} Einträgen sichtbar · Filter aktiv.";
    }
}
