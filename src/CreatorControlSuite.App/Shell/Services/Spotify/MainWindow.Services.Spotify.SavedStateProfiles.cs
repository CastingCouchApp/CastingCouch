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
    private void LoadSpotifyHistoryRestoreProfiles()
    {
        _spotifyHistoryRestoreProfiles.Clear();
        _spotifyHistoryRestoreProfiles.Add(new SpotifyHistoryRestoreProfile("Nur Verlauf zusammenführen", true, false, false, false, false, true, true));
        _spotifyHistoryRestoreProfiles.Add(new SpotifyHistoryRestoreProfile("Verlauf + Favoriten", true, true, false, false, false, true, true));
        _spotifyHistoryRestoreProfiles.Add(new SpotifyHistoryRestoreProfile("Alles vollständig ersetzen", true, true, true, true, true, false, true));
        try
        {
            if (File.Exists(SpotifyHistoryRestoreProfilesPath))
            {
                List<SpotifyHistoryRestoreProfile> custom = JsonSerializer.Deserialize<List<SpotifyHistoryRestoreProfile>>(File.ReadAllText(SpotifyHistoryRestoreProfilesPath)) ?? [];
                foreach (SpotifyHistoryRestoreProfile? profile in custom.Where(profile => !string.IsNullOrWhiteSpace(profile.Name)))
                {
                    _spotifyHistoryRestoreProfiles.Add(profile with { IsBuiltIn = false });
                }
            }
        }
        catch (Exception exception)
        {
            AddTimedAutomationDiagnostic("Spotify: Wiederherstellungsprofile konnten nicht geladen werden: " + exception.Message);
        }
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifyHistoryRestoreProfileBox.SelectedIndex = _spotifyHistoryRestoreProfiles.Count > 0 ? 0 : -1;
    }

    private void ApplySelectedSpotifyHistoryRestoreProfile()
    {
        if (WorkflowPageViewHost.TimedAutomationViewHost.SpotifyHistoryRestoreProfileBox.SelectedItem is not SpotifyHistoryRestoreProfile profile)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Bitte zuerst ein Wiederherstellungsprofil auswählen.";
            return;
        }
        WorkflowPageViewHost.TimedAutomationViewHost.RestoreSpotifyHistoryEntriesBox.IsChecked = profile.Entries;
        WorkflowPageViewHost.TimedAutomationViewHost.RestoreSpotifyHistoryFavoritesBox.IsChecked = profile.Favorites;
        WorkflowPageViewHost.TimedAutomationViewHost.RestoreSpotifyHistoryNotesBox.IsChecked = profile.Notes;
        WorkflowPageViewHost.TimedAutomationViewHost.RestoreSpotifyHistoryCountersBox.IsChecked = profile.Counters;
        WorkflowPageViewHost.TimedAutomationViewHost.RestoreSpotifyHistoryFiltersBox.IsChecked = profile.Filters;
        WorkflowPageViewHost.TimedAutomationViewHost.MergeSpotifyHistoryEntriesBox.IsChecked = profile.MergeEntries;
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = $"Wiederherstellungsprofil ‚{profile.Name}‘ angewendet.";
    }

    private void SaveSpotifyHistoryRestoreProfile()
    {
        string name = WorkflowPageViewHost.TimedAutomationViewHost.SpotifyHistoryRestoreProfileNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Bitte einen Namen für das Wiederherstellungsprofil eingeben.";
            return;
        }
        var profile = new SpotifyHistoryRestoreProfile(name,
            WorkflowPageViewHost.TimedAutomationViewHost.RestoreSpotifyHistoryEntriesBox.IsChecked == true, WorkflowPageViewHost.TimedAutomationViewHost.RestoreSpotifyHistoryFavoritesBox.IsChecked == true,
            WorkflowPageViewHost.TimedAutomationViewHost.RestoreSpotifyHistoryNotesBox.IsChecked == true, WorkflowPageViewHost.TimedAutomationViewHost.RestoreSpotifyHistoryCountersBox.IsChecked == true,
            WorkflowPageViewHost.TimedAutomationViewHost.RestoreSpotifyHistoryFiltersBox.IsChecked == true, WorkflowPageViewHost.TimedAutomationViewHost.MergeSpotifyHistoryEntriesBox.IsChecked == true);
        SpotifyHistoryRestoreProfile? existing = _spotifyHistoryRestoreProfiles.FirstOrDefault(item => !item.IsBuiltIn && item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            _spotifyHistoryRestoreProfiles.Remove(existing);
        }

        _spotifyHistoryRestoreProfiles.Add(profile);
        PersistSpotifyHistoryRestoreProfiles();
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifyHistoryRestoreProfileBox.SelectedItem = profile;
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = $"Wiederherstellungsprofil ‚{name}‘ gespeichert.";
    }

    private void DeleteSpotifyHistoryRestoreProfile()
    {
        if (WorkflowPageViewHost.TimedAutomationViewHost.SpotifyHistoryRestoreProfileBox.SelectedItem is not SpotifyHistoryRestoreProfile profile)
        {
            return;
        }

        if (profile.IsBuiltIn)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Integrierte Wiederherstellungsprofile können nicht gelöscht werden.";
            return;
        }
        _spotifyHistoryRestoreProfiles.Remove(profile);
        PersistSpotifyHistoryRestoreProfiles();
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifyHistoryRestoreProfileBox.SelectedIndex = _spotifyHistoryRestoreProfiles.Count > 0 ? 0 : -1;
        WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = $"Wiederherstellungsprofil ‚{profile.Name}‘ gelöscht.";
    }

    private void ExportSpotifyHistoryRestoreProfiles()
    {
        try
        {
            var customProfiles = _spotifyHistoryRestoreProfiles.Where(profile => !profile.IsBuiltIn).ToList();
            if (customProfiles.Count == 0)
            {
                WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Es sind keine eigenen Wiederherstellungsprofile zum Exportieren vorhanden.";
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Spotify-Wiederherstellungsprofile exportieren",
                Filter = "JSON-Datei (*.json)|*.json|Alle Dateien (*.*)|*.*",
                FileName = $"spotify-wiederherstellungsprofile-{DateTime.Now:yyyy-MM-dd}.json",
                DefaultExt = ".json",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            var exportModel = new
            {
                Format = "CreatorControlSuite.SpotifyHistoryRestoreProfiles",
                Version = 1,
                ExportedAt = DateTimeOffset.Now,
                Profiles = customProfiles
            };
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(exportModel, new JsonSerializerOptions { WriteIndented = true }));
            AddTimedAutomationDiagnostic($"Spotify: {customProfiles.Count} Wiederherstellungsprofil(e) exportiert: {dialog.FileName}");
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = $"{customProfiles.Count} eigene Wiederherstellungsprofile wurden exportiert.";
        }
        catch (Exception exception)
        {
            AddTimedAutomationDiagnostic("Spotify: Wiederherstellungsprofile konnten nicht exportiert werden: " + exception.Message);
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Profil-Export fehlgeschlagen: " + exception.Message;
        }
    }

    private void ImportSpotifyHistoryRestoreProfiles()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Spotify-Wiederherstellungsprofile prüfen",
                Filter = "JSON-Datei (*.json)|*.json|Alle Dateien (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            List<SpotifyHistoryRestoreProfile> imported = ReadSpotifyHistoryRestoreProfilesImport(dialog.FileName);
            _pendingSpotifyHistoryRestoreProfileImport = imported;
            _pendingSpotifyHistoryRestoreProfileImportPath = dialog.FileName;
            _spotifyHistoryRestoreProfileImportPreview.Clear();

            int added = 0;
            int updated = 0;
            int unchanged = 0;
            foreach (SpotifyHistoryRestoreProfile profile in imported)
            {
                SpotifyHistoryRestoreProfile? existing = _spotifyHistoryRestoreProfiles.FirstOrDefault(item => !item.IsBuiltIn && item.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    added++;
                    _spotifyHistoryRestoreProfileImportPreview.Add(new SpotifyHistoryRestoreProfileImportItem
                    {
                        Profile = profile,
                        Status = "+ NEU",
                        Description = DescribeSpotifyHistoryRestoreProfile(profile),
                        ActionOptions = ["Importieren", "Überspringen"],
                        SelectedAction = "Importieren",
                        CanSelect = true
                    });
                }
                else if (existing == profile)
                {
                    unchanged++;
                    _spotifyHistoryRestoreProfileImportPreview.Add(new SpotifyHistoryRestoreProfileImportItem
                    {
                        Profile = profile,
                        Status = "= UNVERÄNDERT",
                        Description = "Keine Änderung erforderlich",
                        ActionOptions = ["Überspringen"],
                        SelectedAction = "Überspringen",
                        CanSelect = false
                    });
                }
                else
                {
                    updated++;
                    _spotifyHistoryRestoreProfileImportPreview.Add(new SpotifyHistoryRestoreProfileImportItem
                    {
                        Profile = profile,
                        Status = "~ KONFLIKT",
                        Description = DescribeSpotifyHistoryRestoreProfile(profile),
                        ActionOptions = ["Überschreiben", "Als Kopie importieren", "Überspringen"],
                        SelectedAction = "Überschreiben",
                        CanSelect = true
                    });
                }
            }

            WorkflowPageViewHost.TimedAutomationViewHost.SpotifyHistoryRestoreProfileImportPreviewText.Text =
                $"Datei: {Path.GetFileName(dialog.FileName)} · {added} neu · {updated} aktualisieren · {unchanged} unverändert. " +
                "Für jedes Profil kann eine Importregel gewählt werden. Erst mit ‚IMPORT ÜBERNEHMEN‘ werden Änderungen gespeichert.";
            WorkflowPageViewHost.TimedAutomationViewHost.ConfirmSpotifyHistoryRestoreProfilesImportButton.IsEnabled = added + updated > 0;
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = $"Profil-Import geprüft: {added} neu, {updated} zu aktualisieren, {unchanged} unverändert.";
            AddTimedAutomationDiagnostic($"Spotify: Profil-Import geprüft: {added} neu, {updated} aktualisieren, {unchanged} unverändert ({dialog.FileName}).");
        }
        catch (Exception exception)
        {
            ResetPendingSpotifyHistoryRestoreProfileImport();
            AddTimedAutomationDiagnostic("Spotify: Wiederherstellungsprofil-Importprüfung fehlgeschlagen: " + exception.Message);
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Profil-Importprüfung fehlgeschlagen: " + exception.Message;
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifyHistoryRestoreProfileImportPreviewText.Text = "Importdatei konnte nicht geprüft werden: " + exception.Message;
        }
    }

    private List<SpotifyHistoryRestoreProfile> ReadSpotifyHistoryRestoreProfilesImport(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        JsonElement profilesElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            profilesElement = root;
        }
        else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Profiles", out JsonElement wrappedProfiles))
        {
            if (root.TryGetProperty("Format", out JsonElement formatElement))
            {
                string? format = formatElement.GetString();
                if (!string.IsNullOrWhiteSpace(format) && !format.Equals("CreatorControlSuite.SpotifyHistoryRestoreProfiles", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Die Datei besitzt eine unbekannte Formatkennung.");
                }
            }
            if (root.TryGetProperty("Version", out JsonElement versionElement) && versionElement.TryGetInt32(out int version) && version > 1)
            {
                throw new InvalidDataException($"Die Profilversion {version} wird von dieser Suite-Version noch nicht unterstützt.");
            }

            profilesElement = wrappedProfiles;
        }
        else
        {
            throw new InvalidDataException("Die Datei enthält keine gültige Profilliste.");
        }

        List<SpotifyHistoryRestoreProfile> imported = JsonSerializer.Deserialize<List<SpotifyHistoryRestoreProfile>>(profilesElement.GetRawText()) ?? [];
        imported = [.. imported
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Name))
            .Select(profile => profile with { Name = profile.Name.Trim(), IsBuiltIn = false })
            .GroupBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())];
        if (imported.Count == 0)
        {
            throw new InvalidDataException("Die ausgewählte Datei enthält keine verwendbaren Profile.");
        }

        return imported;
    }

    private void ConfirmSpotifyHistoryRestoreProfilesImport()
    {
        if (_pendingSpotifyHistoryRestoreProfileImport.Count == 0)
        {
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Bitte zuerst eine Importdatei prüfen.";
            return;
        }

        try
        {
            int replaced = 0;
            int added = 0;
            int unchanged = 0;
            SpotifyHistoryRestoreProfile? lastChanged = null;
            var actionableItems = _spotifyHistoryRestoreProfileImportPreview
                .Where(item => item.CanSelect && !item.SelectedAction.Equals("Überspringen", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (actionableItems.Count == 0)
            {
                WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Alle Profile sind auf ‚Überspringen‘ eingestellt.";
                return;
            }

            int copied = 0;
            foreach (SpotifyHistoryRestoreProfileImportItem? importItem in actionableItems)
            {
                SpotifyHistoryRestoreProfile profile = importItem.Profile;
                SpotifyHistoryRestoreProfile? existing = _spotifyHistoryRestoreProfiles.FirstOrDefault(item => !item.IsBuiltIn && item.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));
                if (existing == profile)
                {
                    unchanged++;
                    continue;
                }

                if (importItem.SelectedAction.Equals("Als Kopie importieren", StringComparison.OrdinalIgnoreCase))
                {
                    string copyName = CreateUniqueSpotifyHistoryRestoreProfileName(profile.Name + " (Import)");
                    SpotifyHistoryRestoreProfile copy = profile with { Name = copyName, IsBuiltIn = false };
                    _spotifyHistoryRestoreProfiles.Add(copy);
                    lastChanged = copy;
                    copied++;
                    continue;
                }

                if (existing is not null)
                {
                    _spotifyHistoryRestoreProfiles.Remove(existing);
                    replaced++;
                }
                else
                {
                    added++;
                }
                _spotifyHistoryRestoreProfiles.Add(profile);
                lastChanged = profile;
            }

            PersistSpotifyHistoryRestoreProfiles();
            if (lastChanged is not null)
            {
                WorkflowPageViewHost.TimedAutomationViewHost.SpotifyHistoryRestoreProfileBox.SelectedItem = lastChanged;
            }

            string fileName = Path.GetFileName(_pendingSpotifyHistoryRestoreProfileImportPath);
            AddTimedAutomationDiagnostic($"Spotify: Wiederherstellungsprofile übernommen: {added} neu, {replaced} überschrieben, {copied} als Kopie, {unchanged} unverändert ({fileName}).");
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = $"Profil-Import übernommen: {added} neu, {replaced} überschrieben, {copied} als Kopie. Übersprungene Profile blieben unverändert.";
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifyHistoryRestoreProfileImportPreviewText.Text = $"Import aus {fileName} wurde erfolgreich übernommen.";
            WorkflowPageViewHost.TimedAutomationViewHost.ConfirmSpotifyHistoryRestoreProfilesImportButton.IsEnabled = false;
            _pendingSpotifyHistoryRestoreProfileImport = [];
            _pendingSpotifyHistoryRestoreProfileImportPath = "";
        }
        catch (Exception exception)
        {
            AddTimedAutomationDiagnostic("Spotify: Wiederherstellungsprofile konnten nicht übernommen werden: " + exception.Message);
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Profil-Import fehlgeschlagen: " + exception.Message;
        }
    }


    private string CreateUniqueSpotifyHistoryRestoreProfileName(string requestedName)
    {
        string baseName = string.IsNullOrWhiteSpace(requestedName) ? "Importiertes Profil" : requestedName.Trim();
        string candidate = baseName;
        int suffix = 2;
        while (_spotifyHistoryRestoreProfiles.Any(profile => profile.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName} {suffix++}";
        }

        return candidate;
    }

    private static string DescribeSpotifyHistoryRestoreProfile(SpotifyHistoryRestoreProfile profile)
    {
        var parts = new List<string>();
        if (profile.Entries)
        {
            parts.Add(profile.MergeEntries ? "Verlauf zusammenführen" : "Verlauf ersetzen");
        }

        if (profile.Favorites)
        {
            parts.Add("Favoriten");
        }

        if (profile.Notes)
        {
            parts.Add("Notizen");
        }

        if (profile.Counters)
        {
            parts.Add("Statistik");
        }

        if (profile.Filters)
        {
            parts.Add("Filter/Sortierung");
        }

        return parts.Count == 0 ? "keine Bereiche aktiviert" : string.Join(", ", parts);
    }

    private void ResetPendingSpotifyHistoryRestoreProfileImport()
    {
        _pendingSpotifyHistoryRestoreProfileImport = [];
        _pendingSpotifyHistoryRestoreProfileImportPath = "";
        _spotifyHistoryRestoreProfileImportPreview.Clear();
        WorkflowPageViewHost.TimedAutomationViewHost.ConfirmSpotifyHistoryRestoreProfilesImportButton?.IsEnabled = false;
    }

    private void PersistSpotifyHistoryRestoreProfiles()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SpotifyHistoryRestoreProfilesPath)!);
            var custom = _spotifyHistoryRestoreProfiles.Where(profile => !profile.IsBuiltIn).ToList();
            File.WriteAllText(SpotifyHistoryRestoreProfilesPath, JsonSerializer.Serialize(custom, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            AddTimedAutomationDiagnostic("Spotify: Wiederherstellungsprofile konnten nicht gespeichert werden: " + exception.Message);
            WorkflowPageViewHost.TimedAutomationViewHost.SpotifySavedStateHistoryStatusText.Text = "Profil konnte nicht gespeichert werden: " + exception.Message;
        }
    }
}
