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
    private async Task LoadRemoteObsConfigurationAsync()
    {
        MultiPcDeviceRecord? device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Kein Remote-Gerät ausgewählt."; return; }
        try
        {
            using HttpClient client = CreateTrustedAgentClient(device);
            client.DefaultRequestHeaders.Add("X-Agent-Key", device.AgentKey);
            RemoteObsConfiguration? data = await client.GetFromJsonAsync<RemoteObsConfiguration>($"https://{device.Host}:{GetMultiPcAgentPort()}/api/v1/obs/configuration");
            MultiPcObsProfilesBox.ItemsSource = data?.Profiles ?? [];
            MultiPcObsSceneCollectionsBox.ItemsSource = data?.SceneCollections ?? [];
            MultiPcObsProfilesBox.SelectedItem = data?.CurrentProfile;
            MultiPcObsSceneCollectionsBox.SelectedItem = data?.CurrentSceneCollection;
            MultiPcStatusText.Text = $"OBS-Konfiguration geladen: Profil {data?.CurrentProfile}, Sammlung {data?.CurrentSceneCollection}.";
        }
        catch (Exception ex) { MultiPcStatusText.Text = "OBS-Konfiguration konnte nicht geladen werden: " + ex.Message; }
    }

    private async Task ApplyRemoteObsConfigurationAsync(bool profile)
    {
        MultiPcDeviceRecord? device = GetSelectedRemoteDevice();
        if (device is null)
        {
            return;
        }

        string profileName = profile ? MultiPcObsProfilesBox.SelectedItem?.ToString() ?? "" : "";
        string collectionName = profile ? "" : MultiPcObsSceneCollectionsBox.SelectedItem?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(profileName) && string.IsNullOrWhiteSpace(collectionName))
        {
            return;
        }

        try
        {
            using HttpClient client = CreateTrustedAgentClient(device);
            client.DefaultRequestHeaders.Add("X-Agent-Key", device.AgentKey);
            HttpResponseMessage response = await client.PostAsJsonAsync($"https://{device.Host}:{GetMultiPcAgentPort()}/api/v1/obs/configuration", new { ProfileName = profileName, SceneCollectionName = collectionName });
            response.EnsureSuccessStatusCode();
            MultiPcStatusText.Text = profile ? $"OBS-Profil aktiviert: {profileName}" : $"OBS-Szenensammlung aktiviert: {collectionName}";
            await Task.Delay(750);
            await RefreshRemoteObsStateAsync();
        }
        catch (Exception ex) { MultiPcStatusText.Text = "OBS-Konfiguration konnte nicht aktiviert werden: " + ex.Message; }
    }

    private async Task LoadRemoteObsPresetsAsync()
    {
        MultiPcDeviceRecord? device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        try
        {
            using HttpClient client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/v1/obs/presets");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            using HttpResponseMessage response = await client.SendAsync(request);
            RemoteObsPresetInfo[]? presets = await response.Content.ReadFromJsonAsync<RemoteObsPresetInfo[]>();
            if (!response.IsSuccessStatusCode) { MultiPcStatusText.Text = "OBS-Presets konnten nicht geladen werden."; return; }
            MultiPcObsPresetsBox.ItemsSource = presets?.Select(x => x.Name + " · " + x.CreatedAt.LocalDateTime.ToString("g")).ToArray() ?? [];
            if (MultiPcObsPresetsBox.Items.Count > 0)
            {
                MultiPcObsPresetsBox.SelectedIndex = 0;
            }

            MultiPcStatusText.Text = $"{presets?.Length ?? 0} Remote-OBS-Preset(s) geladen.";
        }
        catch (Exception ex) { MultiPcStatusText.Text = "OBS-Presets konnten nicht geladen werden: " + ex.Message; }
    }

    private async Task SaveRemoteObsPresetAsync()
    {
        string name = MultiPcObsPresetNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) { MultiPcStatusText.Text = "Bitte einen Preset-Namen eingeben."; return; }
        await PostRemoteObsAsync("presets/save", new { name }, $"OBS-Preset „{name}“ wurde gespeichert");
        await LoadRemoteObsPresetsAsync();
    }

    private string? SelectedRemoteObsPresetName() => MultiPcObsPresetsBox.SelectedItem?.ToString()?.Split(" · ", StringSplitOptions.None)[0];

    private async Task ApplyRemoteObsPresetAsync()
    {
        string? name = SelectedRemoteObsPresetName();
        if (string.IsNullOrWhiteSpace(name)) { MultiPcStatusText.Text = "Bitte ein OBS-Preset auswählen."; return; }
        await PostRemoteObsAsync("presets/apply", new { name }, $"OBS-Preset „{name}“ wurde wiederhergestellt");
        await Task.Delay(750);
        await RefreshRemoteObsStateAsync();
        await LoadRemoteObsConfigurationAsync();
    }

    private async Task DeleteRemoteObsPresetAsync()
    {
        string? name = SelectedRemoteObsPresetName();
        if (string.IsNullOrWhiteSpace(name)) { MultiPcStatusText.Text = "Bitte ein OBS-Preset auswählen."; return; }
        await PostRemoteObsAsync("presets/delete", new { name }, $"OBS-Preset „{name}“ wurde gelöscht");
        await LoadRemoteObsPresetsAsync();
    }

    private async Task LoadRemoteAgentLogsAsync()
    {
        MultiPcDeviceRecord? device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        try
        {
            using HttpClient client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/v1/logs?lines=500");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            using HttpResponseMessage response = await client.SendAsync(request);
            string[]? lines = await response.Content.ReadFromJsonAsync<string[]>();
            MultiPcAgentLogsBox.Text = response.IsSuccessStatusCode ? string.Join(Environment.NewLine, lines ?? []) : await response.Content.ReadAsStringAsync();
            MultiPcStatusText.Text = response.IsSuccessStatusCode ? $"{lines?.Length ?? 0} Agent-Logzeilen geladen." : "Agent-Logs konnten nicht geladen werden.";
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Agent-Logs konnten nicht geladen werden: " + ex.Message; }
    }

    private async Task DeployRemotePackageAsync(string endpoint, string title, string successText)
    {
        MultiPcDeviceRecord? device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        string requiredPermission = endpoint.StartsWith("update", StringComparison.OrdinalIgnoreCase) ? "updates.stage" : "files.deploy";
        if (!(device.AllowedCommands ?? []).Contains(requiredPermission, StringComparer.OrdinalIgnoreCase))
        { MultiPcStatusText.Text = $"Der Agent hat die Berechtigung {requiredPermission} nicht freigegeben."; return; }
        var dialog = new OpenFileDialog { Title = title, Filter = "ZIP-Archive (*.zip)|*.zip", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(dialog.FileName);
            if (bytes.Length > 100 * 1024 * 1024) { MultiPcStatusText.Text = "Das Paket ist größer als 100 MB und wurde nicht übertragen."; return; }
            SignedUpdateManifest? manifest = endpoint.StartsWith("update", StringComparison.OrdinalIgnoreCase)
                ? await SignedUpdateManifestFile.LoadAdjacentAsync(dialog.FileName)
                : null;
            if (endpoint.StartsWith("update", StringComparison.OrdinalIgnoreCase) && manifest is null)
            {
                MultiPcStatusText.Text = "Zum Update-ZIP fehlt ein lesbares update-manifest.json im selben Ordner.";
                return;
            }
            using HttpClient client = CreateTrustedMultiPcClient(device);
            client.Timeout = TimeSpan.FromMinutes(5);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/v1/{endpoint}");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            request.Content = System.Net.Http.Json.JsonContent.Create(new { fileName = Path.GetFileName(dialog.FileName), base64Zip = Convert.ToBase64String(bytes), manifest });
            using HttpResponseMessage response = await client.SendAsync(request);
            string result = await response.Content.ReadAsStringAsync();
            MultiPcStatusText.Text = response.IsSuccessStatusCode ? $"{device.Name}: {successText}." : "Remote-Dateifehler: " + result;
            AddMultiPcHistory(device.Name, endpoint, response.IsSuccessStatusCode ? "erfolgreich" : "Fehler");
            if (response.IsSuccessStatusCode)
            {
                await LoadRemoteAgentLogsAsync();
            }
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Paket konnte nicht übertragen werden: " + ex.Message; }
    }

    private async Task LoadRemoteUpdateStatusAsync()
    {
        MultiPcDeviceRecord? device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        try
        {
            using HttpClient client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/v1/update/status");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            using HttpResponseMessage response = await client.SendAsync(request);
            RemoteUpdateState? state = await response.Content.ReadFromJsonAsync<RemoteUpdateState>();
            if (!response.IsSuccessStatusCode || state is null) { MultiPcUpdateStatusText.Text = "Update-Status konnte nicht geladen werden."; return; }
            MultiPcUpdateStatusText.Text = string.Join(Environment.NewLine,
                $"Status: {state.Status} · Paket: {state.PackageName}",
                $"Bereitgestellt: {(state.StagedAt == DateTimeOffset.MinValue ? "-" : state.StagedAt.LocalDateTime.ToString("g"))}",
                $"Backup: {(string.IsNullOrWhiteSpace(state.BackupDirectory) ? "-" : state.BackupDirectory)}",
                $"Prüfsumme: {(string.IsNullOrWhiteSpace(state.Sha256) ? "-" : state.Sha256)}",
                $"Dateien: {state.FileCount} · Paketversion: {state.PackageVersion} · Mindest-Agent: {state.MinimumAgentVersion}",
                $"Manifest-Signatur: {(state.SignatureValid ? "gültig" : state.Validated ? "ungültig" : "noch nicht geprüft")} · Validiert: {(state.Validated ? "ja" : "nein")} · Wartungsmodus: {(state.MaintenanceMode ? "aktiv" : "aus")}",
                state.Message);
            MultiPcStatusText.Text = "Remote-Update-Status wurde geladen.";
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Update-Status konnte nicht geladen werden: " + ex.Message; }
    }


    private async Task LoadRemoteUpdateHistoryAsync()
    {
        MultiPcDeviceRecord? device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        try
        {
            using HttpClient client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/v1/update/history");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            using HttpResponseMessage response = await client.SendAsync(request);
            RemoteUpdateHistoryEntry[]? history = await response.Content.ReadFromJsonAsync<RemoteUpdateHistoryEntry[]>();
            if (!response.IsSuccessStatusCode || history is null) { MultiPcStatusText.Text = "Update-Historie konnte nicht geladen werden."; return; }
            MultiPcUpdateHistoryList.ItemsSource = history.Select(entry => $"{entry.At.LocalDateTime:g} · {entry.Action} · {entry.PackageVersion} · {(entry.Success ? "OK" : "Fehler")} · {entry.Message}").ToArray();
            MultiPcStatusText.Text = $"{history.Length} Update-Einträge geladen.";
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Update-Historie konnte nicht geladen werden: " + ex.Message; }
    }

    private async Task ExecuteRemoteUpdateActionAsync(string action)
    {
        MultiPcDeviceRecord? device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        if (!(device.AllowedCommands ?? []).Contains("updates.apply", StringComparer.OrdinalIgnoreCase))
        { MultiPcStatusText.Text = "Der Agent hat die Berechtigung updates.apply nicht freigegeben."; return; }
        try
        {
            using HttpClient client = CreateTrustedMultiPcClient(device);
            client.Timeout = TimeSpan.FromMinutes(2);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/v1/update/{action}");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            request.Content = System.Net.Http.Json.JsonContent.Create(new { restartSuite = MultiPcRestartSuiteAfterUpdateCheckBox.IsChecked == true, automaticRollback = MultiPcAutomaticRollbackCheckBox.IsChecked == true });
            using HttpResponseMessage response = await client.SendAsync(request);
            string result = await response.Content.ReadAsStringAsync();
            MultiPcStatusText.Text = response.IsSuccessStatusCode
                ? action == "apply" ? "Remote-Update wird angewendet. Die Verbindung zum Agent kann kurz abbrechen." : action == "validate" ? "Remote-Updatepaket wurde geprüft." : "Remote-Rollback wird angewendet. Die Verbindung zum Agent kann kurz abbrechen."
                : "Remote-Updatefehler: " + result;
            AddMultiPcHistory(device.Name, "update/" + action, response.IsSuccessStatusCode ? "gestartet" : "Fehler");
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Remote-Updateaktion konnte nicht gestartet werden: " + ex.Message; }
    }


    private async Task StartRemoteUpdateRolloutAsync(string? scheduledPackagePath = null)
    {
        if (_multiPcRolloutCts is not null)
        {
            MultiPcStatusText.Text = "Es läuft bereits ein Update-Rollout.";
            return;
        }
        string selectedGroup = (MultiPcRolloutTargetGroupBox.Text ?? "Alle").Trim();
        MultiPcDeviceRecord[] targets = [.. _multiPcDevices
            .Where(device => (device.AllowedCommands ?? []).Contains("updates.stage", StringComparer.OrdinalIgnoreCase)
                          && (device.AllowedCommands ?? []).Contains("updates.apply", StringComparer.OrdinalIgnoreCase))
            .Where(device => string.IsNullOrWhiteSpace(selectedGroup) || selectedGroup.Equals("Alle", StringComparison.OrdinalIgnoreCase)
                          || (_multiPcRolloutGroups.TryGetValue(device.Id, out string? group) && group.Equals(selectedGroup, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase)];
        if (targets.Length == 0)
        {
            MultiPcStatusText.Text = "Für die gewählte Rollout-Gruppe wurde kein geeigneter Agent gefunden.";
            return;
        }
        string? packagePath = scheduledPackagePath;
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            var dialog = new OpenFileDialog { Title = "Update-ZIP für gestaffelten Rollout auswählen", Filter = "ZIP-Archive (*.zip)|*.zip", CheckFileExists = true, Multiselect = false };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            packagePath = dialog.FileName;
        }
        if (!File.Exists(packagePath)) { MultiPcStatusText.Text = "Das ausgewählte Update-Paket wurde nicht gefunden."; return; }
        byte[] bytes = await File.ReadAllBytesAsync(packagePath);
        if (bytes.Length > 100 * 1024 * 1024)
        {
            MultiPcStatusText.Text = "Das Update-Paket ist größer als 100 MB.";
            return;
        }
        SignedUpdateManifest? manifest;
        try
        {
            manifest = await SignedUpdateManifestFile.LoadAdjacentAsync(packagePath);
        }
        catch (Exception ex)
        {
            MultiPcStatusText.Text = "Das signierte Update-Manifest konnte nicht geladen werden: " + ex.Message;
            return;
        }
        if (manifest is null)
        {
            MultiPcStatusText.Text = "Zum Update-Paket wurde kein signiertes Manifest gefunden.";
            return;
        }

        int delaySeconds = int.TryParse(MultiPcRolloutDelayBox.Text, out int parsedDelay) ? Math.Clamp(parsedDelay, 0, 600) : 20;
        int canaryCount = int.TryParse(MultiPcCanaryCountBox.Text, out int parsedCanary) ? Math.Clamp(parsedCanary, 0, targets.Length) : Math.Min(1, targets.Length);
        int maxFailurePercent = int.TryParse(MultiPcMaxFailurePercentBox.Text, out int parsedFailure) ? Math.Clamp(parsedFailure, 0, 100) : 25;
        bool stopOnThreshold = MultiPcStopOnFailureThresholdCheckBox.IsChecked == true;
        var package = new RemoteUpdatePackage(Path.GetFileName(packagePath), bytes, manifest);
        var options = new RemoteUpdateRolloutOptions(
            canaryCount,
            TimeSpan.FromSeconds(delaySeconds),
            maxFailurePercent,
            stopOnThreshold,
            MultiPcRestartSuiteAfterUpdateCheckBox.IsChecked == true,
            MultiPcAutomaticRollbackCheckBox.IsChecked == true);
        _multiPcRolloutCts = new CancellationTokenSource();
        CancellationToken token = _multiPcRolloutCts.Token;
        _multiPcRolloutItems.Clear();
        MultiPcStartRolloutButton.IsEnabled = false;
        MultiPcStatusText.Text = $"Rollout '{selectedGroup}' an {targets.Length} Remote-PC(s) gestartet · Canary: {canaryCount}.";
        try
        {
            var progress = new Progress<RemoteUpdateRolloutProgress>(item =>
            {
                string status = item.Status switch
                {
                    "Staging" => "Paket wird übertragen …",
                    "StageFailed" => "FEHLER beim Bereitstellen",
                    "Validating" => "Paket wird validiert …",
                    "ValidationFailed" => "FEHLER bei Validierung",
                    "Applying" => "Installation wird gestartet …",
                    "InstallationStarted" => "Installation gestartet",
                    "ApplyFailed" => "FEHLER beim Installationsstart",
                    _ => item.Status
                };
                UpdateRolloutLine(item.DeviceName, $"{item.Phase} · {status}");
                int failurePercent = item.Attempted == 0
                    ? 0
                    : (int)Math.Round(item.Failed * 100d / item.Attempted);
                MultiPcStatusText.Text =
                    $"Rollout läuft · {item.Attempted}/{item.Total} bearbeitet · " +
                    $"{item.Succeeded} erfolgreich · {item.Failed} Fehler ({failurePercent} %).";
                if (item.Status is "InstallationStarted" or "ApplyFailed")
                {
                    AddMultiPcHistory(
                        item.DeviceName,
                        "rollout",
                        item.Status == "InstallationStarted"
                            ? $"{item.Phase}: Installation gestartet"
                            : $"{item.Phase}: Fehler");
                }
            });

            RemoteUpdateRolloutResult result =
                await _remoteUpdateRolloutService.RunAsync(
                    targets,
                    package,
                    options,
                    progress,
                    WaitForMaintenanceWindowAsync,
                    token);
            if (result.StopReason ==
                RemoteUpdateRolloutStopReason.FailureThresholdExceeded)
            {
                int failurePercent = result.Attempted == 0
                    ? 0
                    : (int)Math.Round(result.Failed * 100d / result.Attempted);
                MultiPcStatusText.Text =
                    $"Rollout automatisch gestoppt: Fehlerquote {failurePercent} % " +
                    $"überschreitet Grenzwert {maxFailurePercent} %.";
                AddMultiPcHistory(
                    "Rollout",
                    selectedGroup,
                    $"Automatischer Stopp bei {failurePercent} % Fehlerquote");
            }
            else if (result.StopReason ==
                     RemoteUpdateRolloutStopReason.CanaryFailed)
            {
                MultiPcStatusText.Text =
                    $"Canary-Phase beendet: {result.Failed} Fehler. " +
                    "Der weitere Rollout wurde aus Sicherheitsgründen gestoppt.";
                AddMultiPcHistory("Rollout", selectedGroup, "Canary-Stopp");
            }
            else if (result.Failed == 0)
            {
                MultiPcStatusText.Text =
                    $"Rollout erfolgreich abgeschlossen: {result.Succeeded}/{targets.Length} Geräte.";
            }
            else
            {
                MultiPcStatusText.Text =
                    $"Rollout abgeschlossen: {result.Succeeded} erfolgreich, " +
                    $"{result.Failed} fehlgeschlagen.";
            }
        }
        catch (OperationCanceledException)
        {
            MultiPcStatusText.Text = "Update-Rollout wurde abgebrochen. Bereits gestartete Installationen laufen weiter.";
        }
        catch (Exception ex)
        {
            MultiPcStatusText.Text = "Update-Rollout ist fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            _multiPcRolloutCts.Dispose();
            _multiPcRolloutCts = null;
            MultiPcStartRolloutButton.IsEnabled = true;
        }
    }


    private async Task ScheduleRemoteUpdateRolloutAsync()
    {
        if (_scheduledMultiPcRolloutCts is not null) { MultiPcStatusText.Text = "Es ist bereits ein Rollout geplant."; return; }
        DateTimeOffset? when = ParseRolloutSchedule(MultiPcRolloutScheduleBox.Text);
        if (when is null || when <= DateTimeOffset.Now) { MultiPcStatusText.Text = "Bitte einen zukünftigen Zeitpunkt eingeben, z. B. 'morgen 02:00' oder '21.07.2026 02:00'."; return; }
        var dialog = new OpenFileDialog { Title = "Update-ZIP für geplanten Rollout auswählen", Filter = "ZIP-Archive (*.zip)|*.zip", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        Directory.CreateDirectory(MultiPcScheduledPackagesDirectory);
        string storedPackagePath = Path.Combine(MultiPcScheduledPackagesDirectory, $"{when.Value:yyyyMMdd-HHmmss}-{Path.GetFileName(dialog.FileName)}");
        File.Copy(dialog.FileName, storedPackagePath, true);
        ScheduledMultiPcRolloutJob job = CaptureScheduledRolloutJob(when.Value, storedPackagePath);
        SaveScheduledRolloutJob(job);
        AddMultiPcHistory("Rollout", job.TargetGroup, $"geplant für {job.ScheduledAt.LocalDateTime:g}");
        await StartScheduledRolloutWaitAsync(job);
    }

    private ScheduledMultiPcRolloutJob CaptureScheduledRolloutJob(DateTimeOffset when, string packagePath) => new(
        when, packagePath, (MultiPcRolloutTargetGroupBox.Text ?? "Alle").Trim(), MultiPcRolloutDelayBox.Text ?? "20",
        MultiPcCanaryCountBox.Text ?? "1", MultiPcMaxFailurePercentBox.Text ?? "25", MultiPcStopOnFailureThresholdCheckBox.IsChecked == true,
        MultiPcUseMaintenanceWindowCheckBox.IsChecked == true, MultiPcMaintenanceStartBox.Text ?? "02:00", MultiPcMaintenanceEndBox.Text ?? "05:00");

    private void SaveScheduledRolloutJob(ScheduledMultiPcRolloutJob job)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MultiPcScheduledRolloutPath)!);
        File.WriteAllText(MultiPcScheduledRolloutPath, System.Text.Json.JsonSerializer.Serialize(job, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task RestoreScheduledRemoteUpdateRolloutAsync()
    {
        if (MultiPcResumePausedRolloutCheckBox.IsChecked != true || _scheduledMultiPcRolloutCts is not null || !File.Exists(MultiPcScheduledRolloutPath))
        {
            return;
        }

        try
        {
            ScheduledMultiPcRolloutJob? job = System.Text.Json.JsonSerializer.Deserialize<ScheduledMultiPcRolloutJob>(File.ReadAllText(MultiPcScheduledRolloutPath));
            if (job is null || !File.Exists(job.PackagePath)) { File.Delete(MultiPcScheduledRolloutPath); return; }
            ApplyScheduledRolloutJobToUi(job);
            AddMultiPcHistory("Rollout", job.TargetGroup, "Planung nach Suite-Neustart wiederhergestellt");
            await StartScheduledRolloutWaitAsync(job);
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Gespeicherter Rollout konnte nicht wiederhergestellt werden: " + ex.Message; }
    }

    private void ApplyScheduledRolloutJobToUi(ScheduledMultiPcRolloutJob job)
    {
        MultiPcRolloutTargetGroupBox.Text = job.TargetGroup;
        MultiPcRolloutDelayBox.Text = job.DelaySeconds;
        MultiPcCanaryCountBox.Text = job.CanaryCount;
        MultiPcMaxFailurePercentBox.Text = job.MaxFailurePercent;
        MultiPcStopOnFailureThresholdCheckBox.IsChecked = job.StopOnFailureThreshold;
        MultiPcUseMaintenanceWindowCheckBox.IsChecked = job.UseMaintenanceWindow;
        MultiPcMaintenanceStartBox.Text = job.MaintenanceStart;
        MultiPcMaintenanceEndBox.Text = job.MaintenanceEnd;
    }

    private async Task StartScheduledRolloutWaitAsync(ScheduledMultiPcRolloutJob job)
    {
        _scheduledMultiPcRolloutCts = new CancellationTokenSource();
        CancellationToken token = _scheduledMultiPcRolloutCts.Token;
        MultiPcScheduledRolloutStatusText.Text = $"Gespeichert: {job.ScheduledAt.LocalDateTime:g} · {Path.GetFileName(job.PackagePath)}";
        MultiPcStatusText.Text = "Der Update-Rollout wurde dauerhaft geplant.";
        try
        {
            TimeSpan delay = job.ScheduledAt - DateTimeOffset.Now;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, token);
            }

            MultiPcScheduledRolloutStatusText.Text = "Planung wird jetzt ausgeführt …";
            ApplyScheduledRolloutJobToUi(job);
            await StartRemoteUpdateRolloutAsync(job.PackagePath);
            AddMultiPcHistory("Rollout", job.TargetGroup, "gespeicherter Auftrag ausgeführt");
            if (File.Exists(MultiPcScheduledRolloutPath))
            {
                File.Delete(MultiPcScheduledRolloutPath);
            }
        }
        catch (OperationCanceledException) { MultiPcScheduledRolloutStatusText.Text = "Kein Rollout geplant."; }
        finally { _scheduledMultiPcRolloutCts?.Dispose(); _scheduledMultiPcRolloutCts = null; }
    }

    private void CancelScheduledRemoteUpdateRollout()
    {
        if (_scheduledMultiPcRolloutCts is null) { MultiPcStatusText.Text = "Aktuell ist kein Rollout geplant."; return; }
        _scheduledMultiPcRolloutCts.Cancel();
        try { if (File.Exists(MultiPcScheduledRolloutPath)) { File.Delete(MultiPcScheduledRolloutPath); } } catch { }
        AddMultiPcHistory("Rollout", "Planung", "aufgehoben");
        MultiPcStatusText.Text = "Die Rollout-Planung wurde aufgehoben.";
    }

    private DateTimeOffset? ParseRolloutSchedule(string? value)
    {
        string text = (value ?? string.Empty).Trim();
        if (text.StartsWith("morgen ", StringComparison.OrdinalIgnoreCase) && TimeOnly.TryParse(text[7..], out TimeOnly tomorrowTime))
        {
            return new DateTimeOffset(DateTime.Today.AddDays(1).Add(tomorrowTime.ToTimeSpan()));
        }

        if (DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.AssumeLocal, out DateTimeOffset parsed))
        {
            return parsed;
        }

        return null;
    }

    private async Task WaitForMaintenanceWindowAsync(CancellationToken token)
    {
        if (MultiPcUseMaintenanceWindowCheckBox.IsChecked != true)
        {
            return;
        }

        if (!TimeOnly.TryParse(MultiPcMaintenanceStartBox.Text, out TimeOnly start) || !TimeOnly.TryParse(MultiPcMaintenanceEndBox.Text, out TimeOnly end))
        {
            return;
        }

        while (true)
        {
            token.ThrowIfCancellationRequested();
            var now = TimeOnly.FromDateTime(DateTime.Now);
            bool inside = start <= end ? now >= start && now <= end : now >= start || now <= end;
            if (inside)
            {
                return;
            }

            MultiPcStatusText.Text = $"Rollout pausiert: Wartungsfenster {start:HH\\:mm}–{end:HH\\:mm}. Automatische Fortsetzung folgt.";
            await Task.Delay(TimeSpan.FromSeconds(30), token);
        }
    }

    private void LoadMultiPcRolloutGroups()
    {
        try
        {
            if (!File.Exists(MultiPcRolloutGroupsPath))
            {
                return;
            }

            Dictionary<string, string>? values = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(MultiPcRolloutGroupsPath));
            if (values is null)
            {
                return;
            }

            _multiPcRolloutGroups.Clear();
            foreach (KeyValuePair<string, string> pair in values.Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value)))
            {
                _multiPcRolloutGroups[pair.Key] = pair.Value.Trim();
            }
        }
        catch { }
    }

    private void SaveMultiPcRolloutGroups()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MultiPcRolloutGroupsPath)!);
            File.WriteAllText(MultiPcRolloutGroupsPath, System.Text.Json.JsonSerializer.Serialize(_multiPcRolloutGroups, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Rollout-Gruppen konnten nicht gespeichert werden: " + ex.Message; }
    }

    private void RefreshMultiPcRolloutGroupChoices()
    {
        string current = MultiPcRolloutTargetGroupBox.Text;
        string[] groups =
        [
            "Alle",
            .. _multiPcRolloutGroups.Values.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase),
        ];
        MultiPcRolloutTargetGroupBox.ItemsSource = groups;
        MultiPcRolloutTargetGroupBox.Text = string.IsNullOrWhiteSpace(current) ? "Alle" : current;
    }

    private void AssignSelectedDeviceToRolloutGroup()
    {
        MultiPcDeviceRecord? device = GetSelectedRemoteDevice();
        if (device is null)
        {
            MultiPcStatusText.Text = "Bitte zuerst einen Remote-PC auswählen.";
            return;
        }
        string group = (MultiPcDeviceRolloutGroupBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(group))
        {
            _multiPcRolloutGroups.Remove(device.Id);
            MultiPcStatusText.Text = $"{device.Name} wurde aus seiner Rollout-Gruppe entfernt.";
        }
        else
        {
            _multiPcRolloutGroups[device.Id] = group;
            MultiPcStatusText.Text = $"{device.Name} wurde der Rollout-Gruppe '{group}' zugeordnet.";
        }
        SaveMultiPcRolloutGroups();
        RefreshMultiPcRolloutGroupChoices();
    }

    private void CancelRemoteUpdateRollout()
    {
        if (_multiPcRolloutCts is null)
        {
            MultiPcStatusText.Text = "Aktuell läuft kein Rollout.";
            return;
        }
        _multiPcRolloutCts.Cancel();
    }

    private void UpdateRolloutLine(string deviceName, string status)
    {
        string prefix = deviceName + " · ";
        string? existing = _multiPcRolloutItems.FirstOrDefault(item => item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        string line = $"{deviceName} · {status}";
        if (existing is null)
        {
            _multiPcRolloutItems.Add(line);
        }
        else
        {
            _multiPcRolloutItems[_multiPcRolloutItems.IndexOf(existing)] = line;
        }
    }

    private async Task ExecuteUiActionAsync(Button button, string actionName, Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(action);

        bool wasEnabled = button.IsEnabled;
        try
        {
            button.IsEnabled = false;
            await action();
        }
        catch (Exception exception)
        {
            ShowError(actionName, exception);
        }
        finally
        {
            button.IsEnabled = wasEnabled;
        }
    }

    private void ShowError(string title, Exception exception)
    {
        string safeTitle = string.IsNullOrWhiteSpace(title) ? "Fehler" : title.Trim();
        _appLogger.Write(AppLogLevel.Error, "UI", $"{safeTitle}: {exception.Message}", exception);
        MessageBox.Show(exception.Message, safeTitle, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private sealed record RemoteUpdateHistoryEntry(DateTimeOffset At, string Action, string PackageVersion, string Sha256, bool Success, string Message);
    private sealed record ScheduledMultiPcRolloutJob(DateTimeOffset ScheduledAt, string PackagePath, string TargetGroup, string DelaySeconds, string CanaryCount, string MaxFailurePercent, bool StopOnFailureThreshold, bool UseMaintenanceWindow, string MaintenanceStart, string MaintenanceEnd);
    private sealed record MultiPcRolloutAuditEntry(DateTimeOffset Timestamp, string Device, string Action, string Result);

    private sealed record RemoteUpdateState(string Status, string PackageName, string StagingDirectory, string PackageDirectory, string BackupDirectory, DateTimeOffset StagedAt, DateTimeOffset? AppliedAt, string Message, string Sha256, int FileCount, bool Validated, bool MaintenanceMode, bool? AutomaticRollback, string PackageVersion, string MinimumAgentVersion, string ManifestSignature, bool SignatureValid);

    private sealed record RemoteObsPresetInfo(string Name, DateTimeOffset CreatedAt, string ProfileName, string SceneCollectionName, string CurrentScene);

    private sealed record RemoteObsConfiguration(string CurrentProfile, string[] Profiles, string CurrentSceneCollection, string[] SceneCollections);
}
