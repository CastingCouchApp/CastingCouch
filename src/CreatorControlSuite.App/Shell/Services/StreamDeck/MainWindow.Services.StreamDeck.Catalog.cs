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

        var entries = Directory.EnumerateFiles(StreamDeckActionsDirectory, "*.cmd")
            .Select(file => ReadStreamDeckMetadata(file))
            .OrderBy(entry => entry.Profile)
            .ThenBy(entry => entry.Page)
            .ThenBy(entry => entry.Slot)
            .ThenBy(entry => entry.Title)
            .ToList();

        string selectedProfile = (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckProfileFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
        string selectedPage = (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckPageFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;

        RebuildStreamDeckFilter(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckProfileFilterBox, entries.Select(entry => entry.Profile), "Alle Profile", selectedProfile);
        RebuildStreamDeckFilter(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckPageFilterBox, entries
            .Where(entry => string.IsNullOrWhiteSpace(selectedProfile) || entry.Profile == selectedProfile)
            .Select(entry => entry.Page), "Alle Seiten", selectedPage);

        selectedProfile = (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckProfileFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
        selectedPage = (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckPageFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;

        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckCreatedActionsList.Items.Clear();
        foreach ((string File, string Title, string Command, string Parameter, string Profile, string Page, int Slot, int Steps, bool Locked, string Condition, string TrueLabel, string FalseLabel) entry in entries.Where(entry =>
                     (string.IsNullOrWhiteSpace(selectedProfile) || entry.Profile == selectedProfile) &&
                     (string.IsNullOrWhiteSpace(selectedPage) || entry.Page == selectedPage)))
        {
            string displayTitle = ResolveStreamDeckDisplayTitle(entry);
            ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckCreatedActionsList.Items.Add(new ListBoxItem
            {
                Content = $"{(entry.Locked ? "🔒 " : string.Empty)}[{entry.Profile} / {entry.Page} / {entry.Slot}] {displayTitle}",
                Tag = entry.File
            });
        }

        int occupied = entries.Select(entry => $"{entry.Profile}|{entry.Page}|{entry.Slot}").Distinct(StringComparer.OrdinalIgnoreCase).Count();
        int conflicts = entries.GroupBy(entry => $"{entry.Profile}|{entry.Page}|{entry.Slot}", StringComparer.OrdinalIgnoreCase).Count(group => group.Count() > 1);
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckOccupancyText.Text = conflicts == 0 ? $"{occupied} Positionen belegt" : $"{occupied} belegt · {conflicts} Konflikte";
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckOccupancyText.Foreground = conflicts == 0 ? Brushes.LightGreen : Brushes.OrangeRed;
        RebuildStreamDeckSlotGrid(entries, selectedProfile, selectedPage);
        RefreshSelectedStreamDeckActionDetails();
    }


    private void RebuildStreamDeckSlotGrid(IEnumerable<(string File, string Title, string Command, string Parameter, string Profile, string Page, int Slot, int Steps, bool Locked, string Condition, string TrueLabel, string FalseLabel)> entries, string selectedProfile, string selectedPage)
    {
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckSlotGrid.Children.Clear();
        string profile = string.IsNullOrWhiteSpace(selectedProfile) ? "Standard" : selectedProfile;
        string page = string.IsNullOrWhiteSpace(selectedPage) ? "Hauptseite" : selectedPage;
        var lookup = entries.Where(e => string.Equals(e.Profile, profile, StringComparison.OrdinalIgnoreCase) && string.Equals(e.Page, page, StringComparison.OrdinalIgnoreCase) && e.Slot is >= 1 and <= 32)
            .GroupBy(e => e.Slot).ToDictionary(g => g.Key, g => g.ToList());
        for (int slot = 1; slot <= 32; slot++)
        {
            int currentSlot = slot;
            lookup.TryGetValue(slot, out List<(string File, string Title, string Command, string Parameter, string Profile, string Page, int Slot, int Steps, bool Locked, string Condition, string TrueLabel, string FalseLabel)>? assigned);
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

        if (ReadStreamDeckMetadata(file).Locked) { ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Die Taste ist gesperrt. Bitte zuerst entsperren."; return; }
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
        var files = Directory.EnumerateFiles(StreamDeckActionsDirectory, "*.cmd").Where(f => string.Equals(ReadStreamDeckMetadata(f).Profile, selectedProfile, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (string? file in files)
        {
            (string File, string Title, string Command, string Parameter, string Profile, string Page, int Slot, int Steps, bool Locked, string Condition, string TrueLabel, string FalseLabel) entry = ReadStreamDeckMetadata(file);
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
        var entries = Directory.EnumerateFiles(StreamDeckActionsDirectory, "*.cmd").Select(ReadStreamDeckMetadata).OrderBy(e => e.Profile).ThenBy(e => e.Page).ThenBy(e => e.Slot).ToList();
        int changed = 0;
        foreach (IGrouping<(string, string), (string File, string Title, string Command, string Parameter, string Profile, string Page, int Slot, int Steps, bool Locked, string Condition, string TrueLabel, string FalseLabel)> group in entries.GroupBy(e => (e.Profile.ToLowerInvariant(), e.Page.ToLowerInvariant())))
        {
            var used = new HashSet<int>();
            foreach ((string File, string Title, string Command, string Parameter, string Profile, string Page, int Slot, int Steps, bool Locked, string Condition, string TrueLabel, string FalseLabel) entry in group)
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

    private static (string File, string Title, string Command, string Parameter, string Profile, string Page, int Slot, int Steps, bool Locked, string Condition, string TrueLabel, string FalseLabel) ReadStreamDeckMetadata(string file)
    {
        try
        {
            string metadataPath = Path.ChangeExtension(file, ".json");
            if (!File.Exists(metadataPath))
            {
                return (file, Path.GetFileNameWithoutExtension(file), "–", "", "Standard", "Hauptseite", 0, 1, false, "", "", "");
            }

            using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
            JsonElement root = document.RootElement;
            string GetString(string name, string fallback) => root.TryGetProperty(name, out JsonElement node) ? node.GetString() ?? fallback : fallback;
            int slot = root.TryGetProperty("slot", out JsonElement slotNode) && slotNode.TryGetInt32(out int slotValue) ? slotValue : 0;
            int steps = root.TryGetProperty("steps", out JsonElement stepsNode) && stepsNode.ValueKind == JsonValueKind.Array ? stepsNode.GetArrayLength() : 1;
            bool locked = root.TryGetProperty("locked", out JsonElement lockedNode) && lockedNode.ValueKind == JsonValueKind.True;
            return (file, GetString("title", Path.GetFileNameWithoutExtension(file)), GetString("command", "–"), GetString("parameter", ""), GetString("profile", "Standard"), GetString("page", "Hauptseite"), slot, Math.Max(1, steps), locked, GetString("condition", ""), GetString("trueLabel", ""), GetString("falseLabel", ""));
        }
        catch
        {
            return (file, Path.GetFileNameWithoutExtension(file), "–", "", "Standard", "Hauptseite", 0, 1, false, "", "", "");
        }
    }

    private static (bool ToggleMode, string AlternateCommand, string AlternateParameter) ReadStreamDeckToggleMetadata(string file)
    {
        try
        {
            string metadataPath = Path.ChangeExtension(file, ".json");
            if (!File.Exists(metadataPath))
            {
                return (false, "", "");
            }

            using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
            JsonElement root = document.RootElement;
            bool toggle = root.TryGetProperty("toggleMode", out JsonElement toggleNode) && toggleNode.ValueKind == JsonValueKind.True;
            string command = root.TryGetProperty("alternateCommand", out JsonElement commandNode) ? commandNode.GetString() ?? "" : "";
            string parameter = root.TryGetProperty("alternateParameter", out JsonElement parameterNode) ? parameterNode.GetString() ?? "" : "";
            return (toggle, command, parameter);
        }
        catch { return (false, "", ""); }
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
                (string File, string Title, string Command, string Parameter, string Profile, string Page, int Slot, int Steps, bool Locked, string Condition, string TrueLabel, string FalseLabel) entry = ReadStreamDeckMetadata(file);
                (bool ToggleMode, string AlternateCommand, string AlternateParameter) = ReadStreamDeckToggleMetadata(file);
                if (entry.Slot is < 1 or > 32)
                {
                    issues.Add($"• {entry.Title}: ungültige Position {entry.Slot}.");
                }

                if (string.IsNullOrWhiteSpace(entry.Command) || entry.Command == "–")
                {
                    issues.Add($"• {entry.Title}: Hauptbefehl fehlt.");
                }

                if (ToggleMode && string.IsNullOrWhiteSpace(entry.Condition))
                {
                    issues.Add($"• {entry.Title}: Toggle aktiv, aber keine Zustandsbindung gesetzt.");
                }

                if (ToggleMode && string.IsNullOrWhiteSpace(AlternateCommand))
                {
                    issues.Add($"• {entry.Title}: zweiter Toggle-Befehl fehlt.");
                }
            }
            catch (Exception ex) { issues.Add($"• {Path.GetFileName(metadataPath)}: {ex.Message}"); }
        }
        IEnumerable<IGrouping<string, (string File, string Title, string Command, string Parameter, string Profile, string Page, int Slot, int Steps, bool Locked, string Condition, string TrueLabel, string FalseLabel)>> duplicates = cmdFiles.Select(ReadStreamDeckMetadata).GroupBy(e => $"{e.Profile}|{e.Page}|{e.Slot}", StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1);
        foreach (IGrouping<string, (string File, string Title, string Command, string Parameter, string Profile, string Page, int Slot, int Steps, bool Locked, string Condition, string TrueLabel, string FalseLabel)>? group in duplicates)
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

        (string File, string Title, string Command, string Parameter, string Profile, string Page, int Slot, int Steps, bool Locked, string Condition, string TrueLabel, string FalseLabel) entry = ReadStreamDeckMetadata(file);
        (bool ToggleMode, string AlternateCommand, string AlternateParameter) = ReadStreamDeckToggleMetadata(file);
        (int DelayMs, int RetryCount, int CooldownMs) = ReadStreamDeckExecutionPolicy(file);
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckSelectedActionDetailsText.Text = $"{entry.Title}\nProfil: {entry.Profile} · Seite: {entry.Page} · Position: {entry.Slot}\nStatus: {(entry.Locked ? "Gesperrt" : "Bearbeitbar")}\nBefehl AUS: {entry.Command}\nParameter AUS: {(string.IsNullOrWhiteSpace(entry.Parameter) ? "–" : entry.Parameter)}\nBefehl AN: {(ToggleMode ? AlternateCommand : "–")}\nParameter AN: {(string.IsNullOrWhiteSpace(AlternateParameter) ? "–" : AlternateParameter)}\nSchritte: {entry.Steps} · Verzögerung: {DelayMs} ms · Wiederholungen: {RetryCount} · Cooldown: {CooldownMs} ms\nZustandsbindung: {(string.IsNullOrWhiteSpace(entry.Condition) ? "–" : entry.Condition)}\nAktuelle Beschriftung: {ResolveStreamDeckDisplayTitle(entry)}";
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

    private string ResolveStreamDeckDisplayTitle((string File, string Title, string Command, string Parameter, string Profile, string Page, int Slot, int Steps, bool Locked, string Condition, string TrueLabel, string FalseLabel) entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Condition))
        {
            return entry.Title;
        }

        Dictionary<string, bool> states = GetStreamDeckRuntimeStates();
        if (!states.TryGetValue(entry.Condition, out bool active))
        {
            return entry.Title;
        }

        string label = active ? entry.TrueLabel : entry.FalseLabel;
        return string.IsNullOrWhiteSpace(label) ? entry.Title : label;
    }

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

    private static (int DelayMs, int RetryCount, int CooldownMs) ReadStreamDeckExecutionPolicy(string file)
    {
        try
        {
            string metadataPath = Path.ChangeExtension(file, ".json");
            if (!File.Exists(metadataPath))
            {
                return (250, 1, 1000);
            }

            using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
            JsonElement root = document.RootElement;
            int ReadInt(string name, int fallback) => root.TryGetProperty(name, out JsonElement node) && node.TryGetInt32(out int value) ? value : fallback;
            return (Math.Clamp(ReadInt("stepDelayMs", 250), 0, 10000), Math.Clamp(ReadInt("retryCount", 1), 0, 5), Math.Clamp(ReadInt("cooldownMs", 1000), 0, 60000));
        }
        catch { return (250, 1, 1000); }
    }

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
        (string File, string Title, string Command, string Parameter, string Profile, string Page, int Slot, int Steps, bool Locked, string Condition, string TrueLabel, string FalseLabel) entry = ReadStreamDeckMetadata(file);
        (int DelayMs, int RetryCount, int CooldownMs) = ReadStreamDeckExecutionPolicy(file);
        int simulatedDuration = Math.Max(1, entry.Steps) * DelayMs;
        await AppendStreamDeckExecutionLogAsync(entry.Title, "Simulation", true, simulatedDuration, $"{entry.Steps} Schritt(e), {RetryCount} Wiederholung(en), Cooldown {CooldownMs} ms");
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
        (string File, string Title, string Command, string Parameter, string Profile, string Page, int Slot, int Steps, bool Locked, string Condition, string TrueLabel, string FalseLabel) entry = ReadStreamDeckMetadata(file);
        (int DelayMs, int RetryCount, _) = ReadStreamDeckExecutionPolicy(file);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            bool simulation = ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckSimulationModeBox.IsChecked == true;
            bool success = false;
            string message;
            if (simulation)
            {
                await Task.Delay(Math.Min(1000, Math.Max(20, entry.Steps * DelayMs)));
                success = true;
                message = "Testsimulation – keine externen Befehle ausgeführt.";
            }
            else
            {
                for (int attempt = 0; attempt <= RetryCount && !success; attempt++)
                {
                    success = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden }) is not null;
                    if (!success && attempt < RetryCount)
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

        (string File, string Title, string Command, string Parameter, string Profile, string Page, int Slot, int Steps, bool Locked, string Condition, string TrueLabel, string FalseLabel) entry = ReadStreamDeckMetadata(file);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath));
        var output = document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
        output["locked"] = !entry.Locked;
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = !entry.Locked ? "Taste gesperrt." : "Taste entsperrt.";
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
        RefreshStreamDeckActionsList();
    }

    private void BackupStreamDeckConfiguration()
    {
        Directory.CreateDirectory(StreamDeckActionsDirectory);
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Stream-Deck-Komplettbackup (*.zip)|*.zip", FileName = $"CreatorControlSuite-StreamDeck-Backup-{DateTime.Now:yyyyMMdd-HHmm}.zip" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (File.Exists(dialog.FileName))
        {
            File.Delete(dialog.FileName);
        }

        System.IO.Compression.ZipFile.CreateFromDirectory(StreamDeckActionsDirectory, dialog.FileName);
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Komplettbackup erstellt: " + dialog.FileName;
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
    }

    private void RestoreStreamDeckConfiguration()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Stream-Deck-Komplettbackup (*.zip)|*.zip" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        Directory.CreateDirectory(StreamDeckActionsDirectory);
        foreach (string file in Directory.EnumerateFiles(StreamDeckActionsDirectory))
        {
            File.Delete(file);
        }

        System.IO.Compression.ZipFile.ExtractToDirectory(dialog.FileName, StreamDeckActionsDirectory, true);
        RefreshStreamDeckActionsList();
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Stream-Deck-Konfiguration wiederhergestellt.";
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
    }

    private void ExportStreamDeckActionCatalog()
    {
        Directory.CreateDirectory(StreamDeckActionsDirectory);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Stream-Deck-Aktionskatalog (*.zip)|*.zip",
            FileName = $"CreatorControlSuite-StreamDeck-Actions-{DateTime.Now:yyyyMMdd-HHmm}.zip"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (File.Exists(dialog.FileName))
        {
            File.Delete(dialog.FileName);
        }

        System.IO.Compression.ZipFile.CreateFromDirectory(StreamDeckActionsDirectory, dialog.FileName);
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Aktionskatalog exportiert: " + dialog.FileName;
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
    }

    private void ImportStreamDeckActionCatalog()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Stream-Deck-Aktionskatalog (*.zip)|*.zip" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        Directory.CreateDirectory(StreamDeckActionsDirectory);
        System.IO.Compression.ZipFile.ExtractToDirectory(dialog.FileName, StreamDeckActionsDirectory, overwriteFiles: true);
        RefreshStreamDeckActionsList();
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Aktionskatalog importiert.";
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
    }


    private string StreamDeckTemplatesDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CreatorControlSuite", "StreamDeck", "Templates");

    private sealed record StreamDeckTemplateItem(string Name, string Path);

    private void RefreshStreamDeckTemplates()
    {
        Directory.CreateDirectory(StreamDeckTemplatesDirectory);
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckTemplateBox.ItemsSource = Directory.EnumerateFiles(StreamDeckTemplatesDirectory, "*.json")
            .OrderBy(Path.GetFileNameWithoutExtension)
            .Select(path => new StreamDeckTemplateItem(Path.GetFileNameWithoutExtension(path), path))
            .ToList();
    }

    private async Task SaveStreamDeckTemplateAsync()
    {
        string name = string.IsNullOrWhiteSpace(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckTemplateNameBox.Text) ? ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionTitleBox.Text.Trim() : ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckTemplateNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) { ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Bitte einen Vorlagennamen eingeben."; return; }
        string safe = string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        Directory.CreateDirectory(StreamDeckTemplatesDirectory);
        var data = new
        {
            name,
            title = ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionTitleBox.Text,
            command = (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCommandBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "workflow.prepare",
            parameter = ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionParameterBox.Text,
            multiAction = ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckMultiActionBox.Text,
            condition = (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckStateConditionBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty,
            trueLabel = ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckTrueLabelBox.Text,
            falseLabel = ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckFalseLabelBox.Text,
            toggleMode = ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckToggleModeBox.IsChecked == true,
            alternateCommand = (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckAlternateCommandBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty,
            alternateParameter = ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckAlternateParameterBox.Text,
            stepDelayMs = ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckStepDelayBox.Text,
            retryCount = ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRetryCountBox.Text,
            cooldownMs = ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckCooldownBox.Text
        };
        await File.WriteAllTextAsync(Path.Combine(StreamDeckTemplatesDirectory, safe + ".json"), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        RefreshStreamDeckTemplates();
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = $"Vorlage gespeichert: {name}";
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
    }

    private async Task LoadSelectedStreamDeckTemplateAsync()
    {
        if (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckTemplateBox.SelectedItem is not StreamDeckTemplateItem item || !File.Exists(item.Path)) { ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Bitte eine Vorlage auswählen."; return; }
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(item.Path));
        JsonElement r = doc.RootElement;
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionTitleBox.Text = r.TryGetProperty("title", out JsonElement v) ? v.GetString() ?? item.Name : item.Name;
        SelectComboBoxByTag(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCommandBox, r.TryGetProperty("command", out v) ? v.GetString() : null);
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionParameterBox.Text = r.TryGetProperty("parameter", out v) ? v.GetString() ?? "" : "";
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckMultiActionBox.Text = r.TryGetProperty("multiAction", out v) ? v.GetString() ?? "" : "";
        SelectComboBoxByTag(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckStateConditionBox, r.TryGetProperty("condition", out v) ? v.GetString() : null);
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckTrueLabelBox.Text = r.TryGetProperty("trueLabel", out v) ? v.GetString() ?? "" : "";
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckFalseLabelBox.Text = r.TryGetProperty("falseLabel", out v) ? v.GetString() ?? "" : "";
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckToggleModeBox.IsChecked = r.TryGetProperty("toggleMode", out v) && v.ValueKind == JsonValueKind.True;
        SelectComboBoxByTag(ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckAlternateCommandBox, r.TryGetProperty("alternateCommand", out v) ? v.GetString() : null);
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckAlternateParameterBox.Text = r.TryGetProperty("alternateParameter", out v) ? v.GetString() ?? "" : "";
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckStepDelayBox.Text = r.TryGetProperty("stepDelayMs", out v) ? v.ToString() : "250";
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckRetryCountBox.Text = r.TryGetProperty("retryCount", out v) ? v.ToString() : "1";
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckCooldownBox.Text = r.TryGetProperty("cooldownMs", out v) ? v.ToString() : "1000";
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = $"Vorlage geladen: {item.Name}";
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
    }

    private static void SelectComboBoxByTag(ComboBox box, string? tag)
    {
        foreach (ComboBoxItem entry in box.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(entry.Tag?.ToString(), tag ?? string.Empty, StringComparison.OrdinalIgnoreCase)) { box.SelectedItem = entry; return; }
        }
    }

    private void DeleteSelectedStreamDeckTemplate()
    {
        if (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckTemplateBox.SelectedItem is not StreamDeckTemplateItem item)
        {
            return;
        }

        if (File.Exists(item.Path))
        {
            File.Delete(item.Path);
        }

        RefreshStreamDeckTemplates();
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = $"Vorlage gelöscht: {item.Name}";
    }

    private void ExportSelectedStreamDeckAction()
    {
        if (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckCreatedActionsList.SelectedItem is not ListBoxItem item || item.Tag is not string file) { ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Bitte zuerst eine Taste auswählen."; return; }
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Stream-Deck-Taste (*.sdaction)|*.sdaction", FileName = Path.GetFileNameWithoutExtension(file) + ".sdaction" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        using ZipArchive archive = System.IO.Compression.ZipFile.Open(dialog.FileName, System.IO.Compression.ZipArchiveMode.Create);
        archive.CreateEntryFromFile(file, Path.GetFileName(file));
        string meta = Path.ChangeExtension(file, ".json"); if (File.Exists(meta))
        {
            archive.CreateEntryFromFile(meta, Path.GetFileName(meta));
        }

        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Taste exportiert: " + dialog.FileName;
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
    }

    private void ImportSingleStreamDeckAction()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Stream-Deck-Taste (*.sdaction)|*.sdaction" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        Directory.CreateDirectory(StreamDeckActionsDirectory);
        System.IO.Compression.ZipFile.ExtractToDirectory(dialog.FileName, StreamDeckActionsDirectory, true);
        RefreshStreamDeckActionsList();
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Einzelne Taste importiert.";
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Foreground = Brushes.LightGreen;
    }

    private async Task QuickAssignSelectedStreamDeckActionAsync()
    {
        if (ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckCreatedActionsList.SelectedItem is not ListBoxItem item || item.Tag is not string file) { ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Bitte zuerst eine Taste auswählen."; return; }
        (string File, string Title, string Command, string Parameter, string Profile, string Page, int Slot, int Steps, bool Locked, string Condition, string TrueLabel, string FalseLabel) selected = ReadStreamDeckMetadata(file);
        if (selected.Locked) { ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Die Taste ist gesperrt."; return; }
        var used = Directory.EnumerateFiles(StreamDeckActionsDirectory, "*.cmd").Select(ReadStreamDeckMetadata)
            .Where(e => e.File != file && string.Equals(e.Profile, selected.Profile, StringComparison.OrdinalIgnoreCase) && string.Equals(e.Page, selected.Page, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Slot).ToHashSet();
        int free = Enumerable.Range(1, 32).FirstOrDefault(slot => !used.Contains(slot));
        if (free == 0) { ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckActionCreateStatusText.Text = "Auf dieser Seite ist kein freier Platz vorhanden."; return; }
        await MoveSelectedStreamDeckActionToSlotAsync(free, selected.Profile, selected.Page);
    }

    private void CompareStreamDeckProfiles()
    {
        var entries = Directory.EnumerateFiles(StreamDeckActionsDirectory, "*.cmd").Select(ReadStreamDeckMetadata).ToList();
        var profiles = entries.Select(e => e.Profile).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        if (profiles.Count < 2) { ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckDiagnosticsBox.Text = "Für einen Vergleich werden mindestens zwei Profile benötigt."; return; }
        string baseline = profiles[0];
        var baseKeys = entries.Where(e => e.Profile == baseline).Select(e => $"{e.Page}|{e.Slot}|{e.Command}|{e.Parameter}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lines = new List<string> { $"Vergleichsbasis: {baseline}" };
        foreach (string? profile in profiles.Skip(1))
        {
            var keys = entries.Where(e => e.Profile == profile).Select(e => $"{e.Page}|{e.Slot}|{e.Command}|{e.Parameter}").ToHashSet(StringComparer.OrdinalIgnoreCase);
            lines.Add($"{profile}: {keys.Count} Tasten · +{keys.Except(baseKeys).Count()} hinzugefügt · -{baseKeys.Except(keys).Count()} fehlend");
        }
        ServicesPageViewHost.StreamDeckServiceViewHost.StreamDeckDiagnosticsBox.Text = string.Join(Environment.NewLine, lines);
    }
}
