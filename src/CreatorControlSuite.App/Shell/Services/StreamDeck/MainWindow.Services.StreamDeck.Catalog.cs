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

public partial class MainWindow
{
    private string StreamDeckActionsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CreatorControlSuite", "StreamDeck", "Actions");

    private void RefreshStreamDeckActionsList()
    {
        Directory.CreateDirectory(StreamDeckActionsDirectory);
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionsFolderText.Text = StreamDeckActionsDirectory;

        List<StreamDeckCatalogEntry> entries =
        [
            .. Directory
                .EnumerateFiles(StreamDeckActionsDirectory, "*.cmd")
                .Select(StreamDeckCatalogApplicationService.ReadMetadata)
        ];

        string selectedProfile = (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckProfileFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
        string selectedPage = (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckPageFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
        StreamDeckCatalogProjection projection =
            StreamDeckCatalogApplicationService.ProjectCatalog(
                entries,
                selectedProfile,
                selectedPage);

        RebuildStreamDeckFilter(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckProfileFilterBox, projection.Profiles, "Alle Profile", selectedProfile);
        RebuildStreamDeckFilter(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckPageFilterBox, projection.Pages, "Alle Seiten", selectedPage);

        selectedProfile = (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckProfileFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
        selectedPage = (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckPageFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
        projection = StreamDeckCatalogApplicationService.ProjectCatalog(
            entries,
            selectedProfile,
            selectedPage);

        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckCreatedActionsList.Items.Clear();
        foreach (StreamDeckCatalogEntry entry in projection.Entries)
        {
            string displayTitle = ResolveStreamDeckDisplayTitle(entry);
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckCreatedActionsList.Items.Add(new ListBoxItem
            {
                Content = $"{(entry.Locked ? "🔒 " : string.Empty)}[{entry.Profile} / {entry.Page} / {entry.Slot}] {displayTitle}",
                Tag = entry.File
            });
        }

        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckOccupancyText.Text = projection.Conflicts == 0 ? $"{projection.OccupiedPositions} Positionen belegt" : $"{projection.OccupiedPositions} belegt · {projection.Conflicts} Konflikte";
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckOccupancyText.Foreground = projection.Conflicts == 0 ? Brushes.LightGreen : Brushes.OrangeRed;
        RebuildStreamDeckSlotGrid(entries, selectedProfile, selectedPage);
        RefreshSelectedStreamDeckActionDetails();
    }


    private void RebuildStreamDeckSlotGrid(
        IEnumerable<StreamDeckCatalogEntry> entries,
        string selectedProfile,
        string selectedPage)
    {
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckSlotGrid.Children.Clear();
        string profile = string.IsNullOrWhiteSpace(selectedProfile) ? "Standard" : selectedProfile;
        string page = string.IsNullOrWhiteSpace(selectedPage) ? "Hauptseite" : selectedPage;
        var lookup = entries.Where(e => string.Equals(e.Profile, profile, StringComparison.OrdinalIgnoreCase) && string.Equals(e.Page, page, StringComparison.OrdinalIgnoreCase) && e.Slot is >= 1 and <= 32)
            .GroupBy(e => e.Slot).ToDictionary(g => g.Key, g => g.ToList());
        for (int slot = 1; slot <= 32; slot++)
        {
            int currentSlot = slot;
            lookup.TryGetValue(
                slot,
                out List<StreamDeckCatalogEntry>? assigned);
            var button = new Button { Margin = new Thickness(2), MinHeight = 44, Tag = currentSlot, Content = assigned is null ? slot.ToString() : $"{slot}\n{ResolveStreamDeckDisplayTitle(assigned[0])}", ToolTip = assigned is null ? "Frei" : string.Join("\n", assigned.Select(e => e.Title)) };
            if (assigned is { Count: > 1 })
            {
                button.Background = Brushes.OrangeRed;
            }
            else if (assigned is { Count: 1 })
            {
                button.Background = Brushes.DarkSlateGray;
            }

            button.Click += async (_, _) => await MoveSelectedStreamDeckActionToSlotAsync(currentSlot, profile, page);
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckSlotGrid.Children.Add(button);
        }
    }

    private async Task MoveSelectedStreamDeckActionToSlotAsync(int slot, string profile, string page)
    {
        if (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckCreatedActionsList.SelectedItem is not ListBoxItem item || item.Tag is not string file)
        {
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Bitte zuerst eine Taste auswählen.";
            return;
        }
        string metadataPath = Path.ChangeExtension(file, ".json");
        if (!File.Exists(metadataPath))
        {
            return;
        }

        if (StreamDeckCatalogApplicationService.ReadMetadata(file).Locked) { ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Die Taste ist gesperrt. Bitte zuerst entsperren."; return; }
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath));
        var values = document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        var output = new Dictionary<string, object?>();
        foreach (KeyValuePair<string, JsonElement> pair in values)
        {
            output[pair.Key] = pair.Value;
        }

        output["profile"] = profile; output["page"] = page; output["slot"] = slot;
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = $"Taste auf {profile} / {page} / Position {slot} verschoben.";
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
        RefreshStreamDeckActionsList();
    }

    private async Task DuplicateSelectedStreamDeckProfileAsync()
    {
        string? selectedProfile = (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckProfileFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(selectedProfile)) { ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Bitte zuerst ein Profil filtern."; return; }
        string targetProfile = selectedProfile + " - Kopie";
        var files = Directory.EnumerateFiles(StreamDeckActionsDirectory, "*.cmd").Where(f => string.Equals(StreamDeckCatalogApplicationService.ReadMetadata(f).Profile, selectedProfile, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (string? file in files)
        {
            StreamDeckCatalogEntry entry =
                StreamDeckCatalogApplicationService.ReadMetadata(file);
            string target = Path.Combine(StreamDeckActionsDirectory, Path.GetFileNameWithoutExtension(file) + " - " + targetProfile + ".cmd");
            File.Copy(file, target, true);
            string metaPath = Path.ChangeExtension(file, ".json");
            if (!File.Exists(metaPath))
            {
                continue;
            }

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(metaPath));
            var output = document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
            output["profile"] = targetProfile;
            await File.WriteAllTextAsync(Path.ChangeExtension(target, ".json"), JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
        }
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = $"Profil kopiert: {selectedProfile} → {targetProfile} ({files.Count} Tasten).";
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
        RefreshStreamDeckActionsList();
    }

    private async Task ResolveStreamDeckConflictsAsync()
    {
        var entries = Directory.EnumerateFiles(StreamDeckActionsDirectory, "*.cmd").Select(StreamDeckCatalogApplicationService.ReadMetadata).OrderBy(e => e.Profile).ThenBy(e => e.Page).ThenBy(e => e.Slot).ToList();
        int changed = 0;
        foreach (IGrouping<(string, string), StreamDeckCatalogEntry> group in entries.GroupBy(e => (e.Profile.ToLowerInvariant(), e.Page.ToLowerInvariant())))
        {
            var used = new HashSet<int>();
            foreach (StreamDeckCatalogEntry entry in group)
            {
                int slot = entry.Slot;
                if (slot is < 1 or > 32 || !used.Add(slot))
                {
                    slot = Enumerable.Range(1, 32).FirstOrDefault(candidate => !used.Contains(candidate));
                    if (slot == 0)
                    {
                        continue;
                    }

                    used.Add(slot);
                    string metaPath = Path.ChangeExtension(entry.File, ".json");
                    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(metaPath));
                    var output = document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
                    output["slot"] = slot;
                    await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
                    changed++;
                }
            }
        }
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = changed == 0 ? "Keine Positionskonflikte gefunden." : $"{changed} Positionskonflikte automatisch gelöst.";
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
        RefreshStreamDeckActionsList();
    }

    private static void RebuildStreamDeckFilter(ComboBox box, IEnumerable<string> values, string allText, string selected)
    {
        var distinct = values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList();
        box.Items.Clear();
        box.Items.Add(new ComboBoxItem { Content = allText, Tag = string.Empty });
        foreach (string? value in distinct)
        {
            box.Items.Add(new ComboBoxItem { Content = value, Tag = value });
        }

        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag?.ToString(), selected, StringComparison.OrdinalIgnoreCase)) ?? box.Items[0];
    }

    private void DiagnoseStreamDeckActions()
    {
        Directory.CreateDirectory(StreamDeckActionsDirectory);
        var issues = new List<string>();
        var cmdFiles = Directory.EnumerateFiles(StreamDeckActionsDirectory, "*.cmd").ToList();
        string clientPath = Path.Combine(AppContext.BaseDirectory, "CreatorControlSuite.CommandClient.exe");
        if (!File.Exists(clientPath))
        {
            issues.Add("• CommandClient.exe wurde im Programmordner nicht gefunden.");
        }

        foreach (string? file in cmdFiles)
        {
            string metadataPath = Path.ChangeExtension(file, ".json");
            if (!File.Exists(metadataPath)) { issues.Add($"• {Path.GetFileName(file)}: Metadatendatei fehlt."); continue; }
            try
            {
                StreamDeckCatalogEntry entry =
                    StreamDeckCatalogApplicationService.ReadMetadata(file);
                StreamDeckToggleMetadata toggle =
                    StreamDeckCatalogApplicationService.ReadToggleMetadata(file);
                if (entry.Slot is < 1 or > 32)
                {
                    issues.Add($"• {entry.Title}: ungültige Position {entry.Slot}.");
                }

                if (string.IsNullOrWhiteSpace(entry.Command) || entry.Command == "–")
                {
                    issues.Add($"• {entry.Title}: Hauptbefehl fehlt.");
                }

                if (toggle.ToggleMode && string.IsNullOrWhiteSpace(entry.Condition))
                {
                    issues.Add($"• {entry.Title}: Toggle aktiv, aber keine Zustandsbindung gesetzt.");
                }

                if (toggle.ToggleMode && string.IsNullOrWhiteSpace(toggle.AlternateCommand))
                {
                    issues.Add($"• {entry.Title}: zweiter Toggle-Befehl fehlt.");
                }
            }
            catch (Exception ex) { issues.Add($"• {Path.GetFileName(metadataPath)}: {ex.Message}"); }
        }
        IEnumerable<IGrouping<string, StreamDeckCatalogEntry>> duplicates = cmdFiles.Select(StreamDeckCatalogApplicationService.ReadMetadata).GroupBy(e => $"{e.Profile}|{e.Page}|{e.Slot}", StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1);
        foreach (IGrouping<string, StreamDeckCatalogEntry>? group in duplicates)
        {
            issues.Add($"• Doppelbelegung {group.Key}: {string.Join(", ", group.Select(e => e.Title))}");
        }

        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckDiagnosticsBox.Text = issues.Count == 0 ? $"OK – {cmdFiles.Count} Aktion(en) geprüft. Keine Fehler gefunden." : $"{issues.Count} Problem(e) gefunden:\n" + string.Join("\n", issues);
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckDiagnosticsBox.Foreground = issues.Count == 0 ? Brushes.LightGreen : Brushes.OrangeRed;
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = issues.Count == 0 ? "Stream-Deck-Diagnose erfolgreich." : "Stream-Deck-Diagnose hat Probleme gefunden.";
    }

    private void RefreshSelectedStreamDeckActionDetails()
    {
        if (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckCreatedActionsList.SelectedItem is not ListBoxItem item || item.Tag is not string file)
        {
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckSelectedActionDetailsText.Text = "Keine Taste ausgewählt.";
            return;
        }

        StreamDeckCatalogEntry entry =
            StreamDeckCatalogApplicationService.ReadMetadata(file);
        StreamDeckToggleMetadata toggle =
            StreamDeckCatalogApplicationService.ReadToggleMetadata(file);
        StreamDeckExecutionPolicy policy =
            StreamDeckCatalogApplicationService.ReadExecutionPolicy(file);
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckSelectedActionDetailsText.Text = $"{entry.Title}\nProfil: {entry.Profile} · Seite: {entry.Page} · Position: {entry.Slot}\nStatus: {(entry.Locked ? "Gesperrt" : "Bearbeitbar")}\nBefehl AUS: {entry.Command}\nParameter AUS: {(string.IsNullOrWhiteSpace(entry.Parameter) ? "–" : entry.Parameter)}\nBefehl AN: {(toggle.ToggleMode ? toggle.AlternateCommand : "–")}\nParameter AN: {(string.IsNullOrWhiteSpace(toggle.AlternateParameter) ? "–" : toggle.AlternateParameter)}\nSchritte: {entry.Steps} · Verzögerung: {policy.DelayMs} ms · Wiederholungen: {policy.RetryCount} · Cooldown: {policy.CooldownMs} ms\nZustandsbindung: {(string.IsNullOrWhiteSpace(entry.Condition) ? "–" : entry.Condition)}\nAktuelle Beschriftung: {ResolveStreamDeckDisplayTitle(entry)}";
        ServicesPageViewHost.StreamDeckServiceViewHost.LockStreamDeckActionButton.Content = entry.Locked ? "TASTE ENTSPERREN" : "TASTE SPERREN";
    }

    private string StreamDeckRuntimeStateFile => Path.Combine(StreamDeckActionsDirectory, "streamdeck-runtime-state.json");

    private static bool IsStatusLampActive(System.Windows.Shapes.Ellipse lamp)
    {
        string value = lamp.Fill?.ToString() ?? string.Empty;
        return value.Contains("LightGreen", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("#FF90EE90", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("#FF5CB85C", StringComparison.OrdinalIgnoreCase);
    }

    private Dictionary<string, bool> GetStreamDeckRuntimeStates() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["stream.live"] = IsStatusLampActive(StreamDashboardLamp),
        ["obs.connected"] = IsStatusLampActive(ObsDashboardLamp),
        ["spotify.playing"] = IsStatusLampActive(SpotifyDashboardLamp)
    };

    private string ResolveStreamDeckDisplayTitle(StreamDeckCatalogEntry entry) =>
        StreamDeckCatalogApplicationService.ResolveDisplayTitle(
            entry,
            GetStreamDeckRuntimeStates());

    private async Task SyncStreamDeckRuntimeStateAsync(bool showConfirmation)
    {
        try
        {
            Directory.CreateDirectory(StreamDeckActionsDirectory);
            Dictionary<string, bool> states = GetStreamDeckRuntimeStates();
            var payload = new
            {
                updatedAt = DateTimeOffset.Now,
                stream = new { isLive = states["stream.live"] },
                obs = new { connected = states["obs.connected"] },
                spotify = new { isPlaying = states["spotify.playing"] }
            };
            await File.WriteAllTextAsync(StreamDeckRuntimeStateFile, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckLiveSyncStatusText.Text = $"Live-Sync: {DateTime.Now:HH:mm:ss}";
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckLiveSyncStatusText.Foreground = Brushes.LightGreen;
            RefreshStreamDeckActionsList();
            if (showConfirmation)
            {
                ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Stream-Deck-Zustände wurden synchronisiert.";
                ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
            }
        }
        catch (Exception ex)
        {
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckLiveSyncStatusText.Text = "Live-Sync fehlgeschlagen: " + ex.Message;
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckLiveSyncStatusText.Foreground = Brushes.IndianRed;
        }
    }

    private string StreamDeckExecutionLogFile => Path.Combine(StreamDeckActionsDirectory, "streamdeck-execution-log.jsonl");

    private async Task AppendStreamDeckExecutionLogAsync(string action, string mode, bool success, long durationMs, string message)
    {
        Directory.CreateDirectory(StreamDeckActionsDirectory);
        string line = JsonSerializer.Serialize(new { timestamp = DateTimeOffset.Now, action, mode, success, durationMs, message });
        await File.AppendAllTextAsync(StreamDeckExecutionLogFile, line + Environment.NewLine);
        RefreshStreamDeckExecutionLog();
    }

    private void RefreshStreamDeckExecutionLog()
    {
        if (!File.Exists(StreamDeckExecutionLogFile)) { ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckExecutionLogBox.Text = "Noch keine Aktion ausgeführt."; return; }
        IEnumerable<string> lines = File.ReadLines(StreamDeckExecutionLogFile).TakeLast(25).Reverse().Select(line =>
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                JsonElement r = doc.RootElement;
                string time = r.GetProperty("timestamp").GetDateTimeOffset().ToLocalTime().ToString("HH:mm:ss");
                return $"{time} · {(r.GetProperty("success").GetBoolean() ? "OK" : "FEHLER")} · {r.GetProperty("action").GetString()} · {r.GetProperty("mode").GetString()} · {r.GetProperty("durationMs").GetInt64()} ms · {r.GetProperty("message").GetString()}";
            }
            catch { return line; }
        });
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckExecutionLogBox.Text = string.Join(Environment.NewLine, lines);
    }

    private void ClearStreamDeckExecutionLog()
    {
        if (File.Exists(StreamDeckExecutionLogFile))
        {
            File.Delete(StreamDeckExecutionLogFile);
        }

        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckExecutionLogBox.Text = "Protokoll wurde geleert.";
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Stream-Deck-Ausführungsprotokoll geleert.";
    }

    private async Task SimulateSelectedStreamDeckActionAsync()
    {
        if (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckCreatedActionsList.SelectedItem is not ListBoxItem item || item.Tag is not string file || !File.Exists(file))
        {
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Bitte zuerst eine erstellte Taste auswählen.";
            return;
        }
        StreamDeckCatalogEntry entry =
            StreamDeckCatalogApplicationService.ReadMetadata(file);
        StreamDeckExecutionPolicy policy =
            StreamDeckCatalogApplicationService.ReadExecutionPolicy(file);
        int simulatedDuration = Math.Max(1, entry.Steps) * policy.DelayMs;
        await AppendStreamDeckExecutionLogAsync(entry.Title, "Simulation", true, simulatedDuration, $"{entry.Steps} Schritt(e), {policy.RetryCount} Wiederholung(en), Cooldown {policy.CooldownMs} ms");
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = $"Simulation erfolgreich: {entry.Title}";
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
    }

    private async Task TestSelectedStreamDeckActionAsync()
    {
        if (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckCreatedActionsList.SelectedItem is not ListBoxItem item || item.Tag is not string file || !File.Exists(file))
        {
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Bitte zuerst eine erstellte Taste auswählen.";
            return;
        }
        StreamDeckCatalogEntry entry =
            StreamDeckCatalogApplicationService.ReadMetadata(file);
        StreamDeckExecutionPolicy policy =
            StreamDeckCatalogApplicationService.ReadExecutionPolicy(file);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            bool simulation = ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckSimulationModeBox.IsChecked == true;
            bool success = false;
            string message;
            if (simulation)
            {
                await Task.Delay(Math.Min(1000, Math.Max(20, entry.Steps * policy.DelayMs)));
                success = true;
                message = "Testsimulation – keine externen Befehle ausgeführt.";
            }
            else
            {
                for (int attempt = 0; attempt <= policy.RetryCount && !success; attempt++)
                {
                    success = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden }) is not null;
                    if (!success && attempt < policy.RetryCount)
                    {
                        await Task.Delay(250);
                    }
                }
                message = success ? "Befehl gestartet; Rückmeldung gespeichert." : "Prozess konnte nicht gestartet werden.";
            }
            stopwatch.Stop();
            string feedbackPath = Path.Combine(StreamDeckActionsDirectory, "streamdeck-execution-feedback.json");
            await File.WriteAllTextAsync(feedbackPath, JsonSerializer.Serialize(new { action = entry.Title, success, durationMs = stopwatch.ElapsedMilliseconds, executedAt = DateTimeOffset.Now, message }, new JsonSerializerOptions { WriteIndented = true }));
            await AppendStreamDeckExecutionLogAsync(entry.Title, simulation ? "Simulation" : "Test", success, stopwatch.ElapsedMilliseconds, message);
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = success ? $"Test abgeschlossen: {entry.Title} · {stopwatch.ElapsedMilliseconds} ms" : "Test konnte nicht gestartet werden.";
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Foreground = success ? Brushes.LightGreen : Brushes.IndianRed;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await AppendStreamDeckExecutionLogAsync(entry.Title, "Test", false, stopwatch.ElapsedMilliseconds, ex.Message);
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Test fehlgeschlagen: " + ex.Message;
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Foreground = Brushes.IndianRed;
        }
    }

    private async Task DuplicateSelectedStreamDeckActionAsync()
    {
        if (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckCreatedActionsList.SelectedItem is not ListBoxItem item || item.Tag is not string file || !File.Exists(file))
        {
            return;
        }

        string baseName = Path.GetFileNameWithoutExtension(file) + " - Kopie";
        string target = Path.Combine(StreamDeckActionsDirectory, baseName + ".cmd");
        int counter = 2;
        while (File.Exists(target))
        {
            target = Path.Combine(StreamDeckActionsDirectory, $"{baseName} {counter++}.cmd");
        }

        File.Copy(file, target);
        string metadata = Path.ChangeExtension(file, ".json");
        if (File.Exists(metadata))
        {
            string json = await File.ReadAllTextAsync(metadata);
            await File.WriteAllTextAsync(Path.ChangeExtension(target, ".json"), json);
        }
        RefreshStreamDeckActionsList();
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Taste dupliziert: " + Path.GetFileNameWithoutExtension(target);
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
    }

    private string StreamDeckStateFile => Path.Combine(StreamDeckActionsDirectory, "streamdeck-state.json");

    private void ActivateSelectedStreamDeckView()
    {
        string? profile = (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckProfileFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        string? page = (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckPageFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(profile) || string.IsNullOrWhiteSpace(page))
        {
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Bitte zuerst ein Profil und eine Seite auswählen.";
            return;
        }
        Directory.CreateDirectory(StreamDeckActionsDirectory);
        File.WriteAllText(StreamDeckStateFile, JsonSerializer.Serialize(new { activeProfile = profile, activePage = page, changedAt = DateTimeOffset.Now }, new JsonSerializerOptions { WriteIndented = true }));
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = $"Aktiv: {profile} / {page}";
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
    }

    private async Task ToggleSelectedStreamDeckActionLockAsync()
    {
        if (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckCreatedActionsList.SelectedItem is not ListBoxItem item || item.Tag is not string file)
        {
            return;
        }

        string metadataPath = Path.ChangeExtension(file, ".json");
        if (!File.Exists(metadataPath))
        {
            return;
        }

        StreamDeckCatalogEntry entry =
            StreamDeckCatalogApplicationService.ReadMetadata(file);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath));
        var output = document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
        output["locked"] = !entry.Locked;
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = !entry.Locked ? "Taste gesperrt." : "Taste entsperrt.";
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
        RefreshStreamDeckActionsList();
    }

    private async Task QuickAssignSelectedStreamDeckActionAsync()
    {
        if (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckCreatedActionsList.SelectedItem is not ListBoxItem item || item.Tag is not string file) { ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Bitte zuerst eine Taste auswählen."; return; }
        StreamDeckCatalogEntry selected =
            StreamDeckCatalogApplicationService.ReadMetadata(file);
        if (selected.Locked) { ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Die Taste ist gesperrt."; return; }
        int free = StreamDeckCatalogApplicationService.FindFirstFreeSlot(
            Directory
                .EnumerateFiles(StreamDeckActionsDirectory, "*.cmd")
                .Select(StreamDeckCatalogApplicationService.ReadMetadata),
            selected.Profile,
            selected.Page,
            file);
        if (free == 0) { ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Auf dieser Seite ist kein freier Platz vorhanden."; return; }
        await MoveSelectedStreamDeckActionToSlotAsync(free, selected.Profile, selected.Page);
    }

    private void CompareStreamDeckProfiles()
    {
        IReadOnlyList<string> lines =
            StreamDeckCatalogApplicationService.CompareProfiles(
                Directory
                    .EnumerateFiles(StreamDeckActionsDirectory, "*.cmd")
                    .Select(StreamDeckCatalogApplicationService.ReadMetadata));
        if (lines.Count == 0) { ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckDiagnosticsBox.Text = "Für einen Vergleich werden mindestens zwei Profile benötigt."; return; }
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckDiagnosticsBox.Text = string.Join(Environment.NewLine, lines);
    }
}
