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
    private void LoadMultiPcRegistry()
    {
        try
        {
            IReadOnlyList<MultiPcDeviceRecord> devices =
                _multiPcRegistry.LoadAsync().GetAwaiter().GetResult();
            _multiPcDevices.Clear();
            _multiPcDevices.AddRange(devices);
        }
        catch (Exception ex)
        {
            MultiPcStatusText.Text = $"Geräteliste konnte nicht geladen werden: {ex.Message}";
        }
    }

    private async Task SaveMultiPcRegistryAsync()
    {
        await _multiPcRegistry.SaveAsync(_multiPcDevices);
    }

    private void GenerateMultiPcPairingCode()
    {
        _multiPcPairingCode = "------";
        MultiPcPairingCodeText.Text = _multiPcPairingCode;
        MultiPcPairingInputBox.Clear();
        MultiPcCertificateFingerprintBox.Clear();
        MultiPcStatusText.Text =
            "Pairing-Code und SHA-256-Fingerprint direkt aus der Agent-Anzeige übernehmen.";
    }

    private async Task AddMultiPcDeviceAsync()
    {
        string name = MultiPcDeviceNameBox.Text.Trim();
        string host = MultiPcHostBox.Text.Trim();
        string code = MultiPcPairingInputBox.Text.Trim();
        string expectedFingerprint;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(host))
        {
            MultiPcStatusText.Text = "Gerätename und Host dürfen nicht leer sein.";
            return;
        }
        try
        {
            expectedFingerprint = CertificateFingerprint.Normalize(
                MultiPcCertificateFingerprintBox.Text);
        }
        catch (FormatException ex)
        {
            MultiPcStatusText.Text = ex.Message;
            return;
        }
        if (_multiPcDevices.Any(device => string.Equals(device.Host, host, StringComparison.OrdinalIgnoreCase)))
        {
            MultiPcStatusText.Text = "Dieses Gerät ist bereits registriert.";
            return;
        }
        try
        {
            MultiPcPairingResult pairing = await _multiPcPairingClient.PairAsync(
                host,
                GetMultiPcAgentPort(),
                code,
                Environment.MachineName,
                expectedFingerprint);
            _multiPcDevices.Add(new MultiPcDeviceRecord(pairing.DeviceId, name, host, DateTimeOffset.Now, pairing.AgentKey, pairing.CertificateFingerprint, pairing.AllowedCommands, MultiPcMacAddressBox.Text.Trim(), pairing.Port));
        }
        catch (Exception ex)
        {
            MultiPcStatusText.Text = $"Kopplung mit dem Remote-Agent fehlgeschlagen: {ex.Message}";
            return;
        }
        await SaveMultiPcRegistryAsync();
        GenerateMultiPcPairingCode();
        await RefreshMultiPcPageAsync();
        MultiPcStatusText.Text = $"{name} wurde TLS-verschlüsselt gekoppelt und als vertrauenswürdig gespeichert.";
    }

    private async Task RemoveSelectedMultiPcDeviceAsync()
    {
        int index = MultiPcDevicesList.SelectedIndex - 1;
        if (index < 0 || index >= _multiPcDevices.Count)
        {
            MultiPcStatusText.Text = "Bitte zuerst ein Gerät auswählen.";
            return;
        }
        MultiPcDeviceRecord removed = _multiPcDevices[index];
        _multiPcDevices.RemoveAt(index);
        await _multiPcRegistry.DeleteAsync(removed.Id);
        await RefreshMultiPcPageAsync();
        MultiPcStatusText.Text = $"{removed.Name} wurde entfernt.";
    }

    private async Task RefreshMultiPcPageAsync()
    {
        MultiPcLocalAgentStatusText.Text = $"AKTIV · {Environment.MachineName}";
        MultiPcDeviceCountText.Text = (_multiPcDevices.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        int online = 1;
        _multiPcDeviceItems.Clear();
        _multiPcDeviceItems.Add($"●  {Environment.MachineName} · Lokaler Hauptrechner · Online · {Environment.OSVersion.VersionString}");
        foreach (MultiPcDeviceRecord device in _multiPcDevices)
        {
            bool reachable = false;
            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                PingReply reply = await ping.SendPingAsync(device.Host, 650);
                reachable = reply.Status == System.Net.NetworkInformation.IPStatus.Success;
            }
            catch
            {
                reachable = false;
            }
            MultiPcAgentStatus? agent = await TryGetMultiPcAgentStatusAsync(device);
            if (agent is not null)
            {
                reachable = true;
            }

            if (reachable)
            {
                online++;
            }

            string agentInfo = agent is null ? (reachable ? "Ping erreichbar · TLS-Agent antwortet nicht" : "Offline/Agent fehlt") : $"TLS-Agent online · CPU {agent.CpuPercent:0}% · RAM {agent.MemoryMb:0} MB · {agent.MachineName}";
            _multiPcDeviceItems.Add($"{(reachable ? "●" : "○")}  {device.Name} · {device.Host} · {agentInfo} · gekoppelt {device.PairedAt.LocalDateTime:g}");
        }
        MultiPcOnlineCountText.Text = online.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void UpdateSelectedMultiPcDeviceText()
    {
        int index = MultiPcDevicesList.SelectedIndex - 1;
        MultiPcSelectedDeviceText.Text = index >= 0 && index < _multiPcDevices.Count
            ? $"Ausgewählt: {_multiPcDevices[index].Name} · {_multiPcDevices[index].Host}"
            : "Kein Remote-Gerät ausgewählt.";
        MultiPcTrustText.Text = index >= 0 && index < _multiPcDevices.Count
            ? $"TLS-Vertrauen: SHA-256 {_multiPcDevices[index].CertificateFingerprint} · Befehle: {string.Join(", ", _multiPcDevices[index].AllowedCommands ?? [])}"
            : "TLS-Vertrauen: kein Gerät ausgewählt";
    }

    private int GetMultiPcAgentPort() => int.TryParse(MultiPcAgentPortBox.Text, out int port) && port is > 0 and <= 65535 ? port : 47631;

    private int GetMultiPcAgentPort(MultiPcDeviceRecord device) => device.AgentPort is > 0 and <= 65535 ? device.AgentPort : GetMultiPcAgentPort();

    private MultiPcDeviceRecord? GetSelectedRemoteDevice()
    {
        int index = MultiPcDevicesList.SelectedIndex - 1;
        return index >= 0 && index < _multiPcDevices.Count ? _multiPcDevices[index] : null;
    }

    private async Task<MultiPcAgentStatus?> TryGetMultiPcAgentStatusAsync(MultiPcDeviceRecord device)
    {
        try
        {
            using HttpClient client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/v1/status");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            using HttpResponseMessage response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<MultiPcAgentStatus>();
        }
        catch { return null; }
    }

    private async Task SendMultiPcCommandAsync(string command)
    {
        MultiPcDeviceRecord? device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        try
        {
            if (!(device.AllowedCommands ?? []).Contains(command, StringComparer.OrdinalIgnoreCase))
            {
                MultiPcStatusText.Text = $"{device.Name}: Der Agent hat den Befehl ‘{command}’ nicht freigegeben. Berechtigungen werden in agent-permissions.json auf dem Ziel-PC verwaltet.";
                return;
            }
            using HttpClient client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/v1/command");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            request.Content = System.Net.Http.Json.JsonContent.Create(new { command });
            using HttpResponseMessage response = await client.SendAsync(request);
            string result = await response.Content.ReadAsStringAsync();
            MultiPcStatusText.Text = response.IsSuccessStatusCode ? $"{device.Name}: {command} wurde angenommen." : $"Agentfehler: {result}";
            AddMultiPcHistory(device.Name, command, response.IsSuccessStatusCode ? "angenommen" : "Fehler");
        }
        catch (Exception ex) { MultiPcStatusText.Text = $"Remote-Befehl fehlgeschlagen: {ex.Message}"; }
    }


    private async Task RefreshRemoteObsStateAsync()
    {
        MultiPcDeviceRecord? device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        try
        {
            using HttpClient client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/v1/obs/state");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            using HttpResponseMessage response = await client.SendAsync(request);
            string json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) { MultiPcStatusText.Text = "Remote-OBS konnte nicht geladen werden: " + json; return; }
            RemoteObsState? state = System.Text.Json.JsonSerializer.Deserialize<RemoteObsState>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            MultiPcObsScenesBox.ItemsSource = state?.Scenes ?? [];
            MultiPcObsAudioInputsBox.ItemsSource = state?.AudioInputs?.Select(x => x.Name + (x.Muted ? " · gemutet" : $" · {x.VolumeDb:0.0} dB")).ToArray() ?? [];
            MultiPcObsSceneItemsBox.ItemsSource = state?.SceneItems?.Select(x => x.SourceName + (x.Enabled ? " · sichtbar" : " · ausgeblendet")).ToArray() ?? [];
            MultiPcObsScenesBox.SelectedItem = state?.CurrentScene;
            if (MultiPcObsAudioInputsBox.SelectedIndex < 0 && MultiPcObsAudioInputsBox.Items.Count > 0)
            {
                MultiPcObsAudioInputsBox.SelectedIndex = 0;
            }

            if (MultiPcObsSceneItemsBox.SelectedIndex < 0 && MultiPcObsSceneItemsBox.Items.Count > 0)
            {
                MultiPcObsSceneItemsBox.SelectedIndex = 0;
            }

            MultiPcStatusText.Text = $"Remote-OBS verbunden · aktuelle Szene: {state?.CurrentScene ?? "unbekannt"}.";
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Remote-OBS-Fehler: " + ex.Message; }
    }

    private async Task SwitchRemoteObsSceneAsync()
    {
        string? scene = MultiPcObsScenesBox.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(scene)) { MultiPcStatusText.Text = "Bitte eine Remote-Szene auswählen."; return; }
        await PostRemoteObsAsync("scene", new { sceneName = scene }, $"Szene {scene} aktiviert");
        await RefreshRemoteObsStateAsync();
    }

    private async Task SetRemoteObsMuteAsync(bool muted)
    {
        string? raw = MultiPcObsAudioInputsBox.SelectedItem?.ToString();
        string? input = raw?.Split(" · ", StringSplitOptions.None)[0];
        if (string.IsNullOrWhiteSpace(input)) { MultiPcStatusText.Text = "Bitte eine Remote-Audioquelle auswählen."; return; }
        await PostRemoteObsAsync("mute", new { inputName = input, muted }, $"{input} {(muted ? "gemutet" : "entmutet")}");
        await RefreshRemoteObsStateAsync();
    }

    private async Task SetRemoteObsVolumeAsync()
    {
        string? raw = MultiPcObsAudioInputsBox.SelectedItem?.ToString();
        string? input = raw?.Split(" · ", StringSplitOptions.None)[0];
        if (string.IsNullOrWhiteSpace(input)) { MultiPcStatusText.Text = "Bitte eine Remote-Audioquelle auswählen."; return; }
        if (!double.TryParse(MultiPcObsVolumeBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double volumeDb))
        {
            MultiPcStatusText.Text = "Lautstärke bitte als dB-Wert eingeben, zum Beispiel -10."; return;
        }
        volumeDb = Math.Clamp(volumeDb, -100, 26);
        await PostRemoteObsAsync("volume", new { inputName = input, volumeDb }, $"Lautstärke von {input} auf {volumeDb:0.0} dB gesetzt");
        await RefreshRemoteObsStateAsync();
    }

    private async Task FadeRemoteObsVolumeAsync()
    {
        string? input = MultiPcObsAudioInputsBox.SelectedItem?.ToString()?.Split(" · ", StringSplitOptions.None)[0];
        if (string.IsNullOrWhiteSpace(input)) { MultiPcStatusText.Text = "Bitte eine Remote-Audioquelle auswählen."; return; }
        if (!double.TryParse(MultiPcObsVolumeBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double targetDb)) { MultiPcStatusText.Text = "Ungültiger dB-Wert."; return; }
        int duration = int.TryParse(MultiPcObsFadeDurationBox.Text, out int ms) ? Math.Clamp(ms, 100, 30000) : 1000;
        await PostRemoteObsAsync("volume-fade", new { inputName = input, targetVolumeDb = Math.Clamp(targetDb, -100, 26), durationMilliseconds = duration }, $"Lautstärke von {input} wird gefadet");
    }

    private async Task RefreshRemoteObsFiltersAsync()
    {
        MultiPcDeviceRecord? device = GetSelectedRemoteDevice();
        string? source = MultiPcObsAudioInputsBox.SelectedItem?.ToString()?.Split(" · ", StringSplitOptions.None)[0];
        if (device is null || string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        try { using HttpClient client = CreateTrustedMultiPcClient(device); using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/v1/obs/filters?sourceName={Uri.EscapeDataString(source)}"); request.Headers.Add("X-CCS-Agent-Key", device.AgentKey); using HttpResponseMessage response = await client.SendAsync(request); if (!response.IsSuccessStatusCode) { return; } RemoteObsFilter[]? filters = await response.Content.ReadFromJsonAsync<RemoteObsFilter[]>(); MultiPcObsFiltersBox.ItemsSource = filters?.Select(x => x.Name + (x.Enabled ? " · aktiv" : " · aus")).ToArray() ?? []; if (MultiPcObsFiltersBox.Items.Count > 0) { MultiPcObsFiltersBox.SelectedIndex = 0; } } catch { }
    }

    private async Task SetRemoteObsFilterAsync(bool enabled)
    {
        string? source = MultiPcObsAudioInputsBox.SelectedItem?.ToString()?.Split(" · ", StringSplitOptions.None)[0]; string? filter = MultiPcObsFiltersBox.SelectedItem?.ToString()?.Split(" · ", StringSplitOptions.None)[0];
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(filter)) { MultiPcStatusText.Text = "Bitte Quelle und Filter auswählen."; return; }
        await PostRemoteObsAsync("filter", new { sourceName = source, filterName = filter, enabled }, $"Filter {filter} {(enabled ? "aktiviert" : "deaktiviert")}"); await RefreshRemoteObsFiltersAsync();
    }

    private async Task ApplyRemoteObsTransformAsync(bool reset)
    {
        string? scene = MultiPcObsScenesBox.SelectedItem?.ToString(); string? source = MultiPcObsSceneItemsBox.SelectedItem?.ToString()?.Split(" · ", StringSplitOptions.None)[0];
        if (string.IsNullOrWhiteSpace(scene) || string.IsNullOrWhiteSpace(source)) { MultiPcStatusText.Text = "Bitte Szene und Quelle auswählen."; return; }
        static double Parse(string text, double fallback) => double.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value) ? value : fallback;
        await PostRemoteObsAsync("transform", new { sceneName = scene, sourceName = source, reset, x = Parse(MultiPcObsPosXBox.Text, 0), y = Parse(MultiPcObsPosYBox.Text, 0), width = Math.Max(1, Parse(MultiPcObsWidthBox.Text, 640)), height = Math.Max(1, Parse(MultiPcObsHeightBox.Text, 360)), rotation = Parse(MultiPcObsRotationBox.Text, 0) }, reset ? $"Transform von {source} zurückgesetzt" : $"Transform von {source} gesetzt");
    }

    private async Task SetRemoteObsSceneItemVisibilityAsync(bool enabled)
    {
        string? scene = MultiPcObsScenesBox.SelectedItem?.ToString();
        string? raw = MultiPcObsSceneItemsBox.SelectedItem?.ToString();
        string? source = raw?.Split(" · ", StringSplitOptions.None)[0];
        if (string.IsNullOrWhiteSpace(scene) || string.IsNullOrWhiteSpace(source))
        {
            MultiPcStatusText.Text = "Bitte eine Szene und eine Szenen-Quelle auswählen."; return;
        }
        await PostRemoteObsAsync("scene-item", new { sceneName = scene, sourceName = source, enabled }, $"{source} wurde {(enabled ? "eingeblendet" : "ausgeblendet")}");
        await RefreshRemoteObsStateAsync();
    }

    private async Task PostRemoteObsAsync(string endpoint, object payload, string successText)
    {
        MultiPcDeviceRecord? device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        try
        {
            using HttpClient client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/v1/obs/{endpoint}");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            request.Content = System.Net.Http.Json.JsonContent.Create(payload);
            using HttpResponseMessage response = await client.SendAsync(request);
            string result = await response.Content.ReadAsStringAsync();
            MultiPcStatusText.Text = response.IsSuccessStatusCode ? successText : "Remote-OBS-Fehler: " + result;
            AddMultiPcHistory(device.Name, "obs." + endpoint, response.IsSuccessStatusCode ? "angenommen" : "Fehler");
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Remote-OBS-Befehl fehlgeschlagen: " + ex.Message; }
    }

    private RemoteObsOutputState? _remoteObsOutputState;

    private async Task RefreshRemoteObsOutputStateAsync()
    {
        MultiPcDeviceRecord? device = GetSelectedRemoteDevice();
        if (device is null)
        {
            return;
        }
        try
        {
            using HttpClient client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/v1/obs/output");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            using HttpResponseMessage response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            _remoteObsOutputState = await response.Content.ReadFromJsonAsync<RemoteObsOutputState>();
            if (_remoteObsOutputState is null)
            {
                return;
            }

            MultiPcObsOutputStatusText.Text = $"Stream: {(_remoteObsOutputState.StreamActive ? "LIVE" : "offline")} · Aufnahme: {(_remoteObsOutputState.RecordActive ? (_remoteObsOutputState.RecordPaused ? "pausiert" : "läuft") : "aus")}";
            MultiPcObsTransitionsBox.ItemsSource = _remoteObsOutputState.Transitions;
            if (MultiPcObsTransitionsBox.SelectedIndex < 0 && _remoteObsOutputState.Transitions.Length > 0)
            {
                MultiPcObsTransitionsBox.SelectedIndex = 0;
            }
        }
        catch (Exception ex) { MultiPcObsOutputStatusText.Text = "OBS-Ausgabestatus nicht verfügbar: " + ex.Message; }
    }

    private async Task SendRemoteObsOutputActionAsync(string action)
    {
        MultiPcDeviceRecord? device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        try
        {
            using HttpClient client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/v1/obs/output");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            request.Content = System.Net.Http.Json.JsonContent.Create(new { action });
            using HttpResponseMessage response = await client.SendAsync(request);
            bool ok = response.IsSuccessStatusCode;
            MultiPcStatusText.Text = ok ? $"OBS-Aktion {action} wurde ausgeführt." : $"OBS-Aktion {action} wurde abgelehnt.";
            AddMultiPcHistory(device.Name, action, ok ? "ausgeführt" : "fehlgeschlagen");
            await RefreshRemoteObsOutputStateAsync();
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Remote-OBS-Aktion fehlgeschlagen: " + ex.Message; }
    }

    private async Task ToggleRemoteObsRecordPauseAsync()
    {
        await RefreshRemoteObsOutputStateAsync();
        if (_remoteObsOutputState is null || !_remoteObsOutputState.RecordActive) { MultiPcStatusText.Text = "Auf dem Remote-PC läuft keine Aufnahme."; return; }
        await SendRemoteObsOutputActionAsync(_remoteObsOutputState.RecordPaused ? "record.resume" : "record.pause");
    }

    private async Task ApplyRemoteObsTransitionAsync()
    {
        MultiPcDeviceRecord? device = GetSelectedRemoteDevice();
        string? transition = MultiPcObsTransitionsBox.SelectedItem?.ToString();
        if (device is null || string.IsNullOrWhiteSpace(transition)) { MultiPcStatusText.Text = "Bitte Gerät und Übergang auswählen."; return; }
        int duration = int.TryParse(MultiPcObsTransitionDurationBox.Text, out int value) ? Math.Clamp(value, 50, 20000) : 300;
        try
        {
            using HttpClient client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/v1/obs/transition");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            request.Content = System.Net.Http.Json.JsonContent.Create(new { transitionName = transition, durationMilliseconds = duration });
            using HttpResponseMessage response = await client.SendAsync(request);
            MultiPcStatusText.Text = response.IsSuccessStatusCode ? $"Übergang {transition} ({duration} ms) gesetzt." : "Übergang konnte nicht gesetzt werden.";
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Remote-Übergang fehlgeschlagen: " + ex.Message; }
    }

    private async Task RefreshRemoteObsPreviewAsync()
    {
        MultiPcDeviceRecord? device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        try
        {
            using HttpClient client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/v1/obs/preview");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            using HttpResponseMessage response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
            var image = new System.Windows.Media.Imaging.BitmapImage();
            using var stream = new MemoryStream(bytes);
            image.BeginInit(); image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
            MultiPcObsPreviewImage.Source = image;
            MultiPcStatusText.Text = "Remote-Programmvorschau wurde aktualisiert.";
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Remote-Vorschau fehlgeschlagen: " + ex.Message; }
    }

    private async Task SaveRemoteAgentSettingsAsync()
    {
        MultiPcDeviceRecord? device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        if (!int.TryParse(MultiPcRemoteObsPortBox.Text, out int obsPort) || obsPort is <= 0 or > 65535) { MultiPcStatusText.Text = "Ungültiger OBS-WebSocket-Port."; return; }
        try
        {
            using HttpClient client = CreateTrustedMultiPcClient(device);
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"https://{device.Host}:{GetMultiPcAgentPort(device)}/api/v1/settings");
            request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
            request.Content = System.Net.Http.Json.JsonContent.Create(new { obsPath = "", streamerBotPath = "", obsWebSocketHost = MultiPcRemoteObsHostBox.Text.Trim(), obsWebSocketPort = obsPort, obsWebSocketPassword = MultiPcRemoteObsPasswordBox.Password });
            using HttpResponseMessage response = await client.SendAsync(request);
            MultiPcStatusText.Text = response.IsSuccessStatusCode ? "Agent-Einstellungen gespeichert." : "Agent-Einstellungen konnten nicht gespeichert werden.";
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Agent-Einstellungen fehlgeschlagen: " + ex.Message; }
    }

    private async Task FetchMultiPcDiagnosticsAsync()
    {
        MultiPcDeviceRecord? device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        MultiPcAgentStatus? status = await TryGetMultiPcAgentStatusAsync(device);
        MultiPcStatusText.Text = status is null ? "Der Agent antwortet nicht oder der Schlüssel stimmt nicht." : $"{status.MachineName}: CPU {status.CpuPercent:0}% · RAM {status.MemoryMb:0} MB · Uptime {status.UptimeMinutes:0} Min. · OBS {(status.ObsRunning ? "läuft" : "aus")} · Spotify {(status.SpotifyRunning ? "läuft" : "aus")}.";
    }

    private System.Net.Http.HttpClient CreateTrustedMultiPcClient(MultiPcDeviceRecord device)
        => _multiPcAgentClient.CreateClient(
            device.Host,
            GetMultiPcAgentPort(device),
            device.AgentKey,
            device.CertificateFingerprint);

    private System.Net.Http.HttpClient CreateTrustedAgentClient(MultiPcDeviceRecord device)
        => CreateTrustedMultiPcClient(device);


    private void AddMultiPcHistory(string device, string action, string result)
    {
        DateTimeOffset timestamp = DateTimeOffset.Now;
        _multiPcHistoryItems.Insert(0, $"{timestamp:HH:mm:ss} · {device} · {action} · {result}");
        while (_multiPcHistoryItems.Count > 50)
        {
            _multiPcHistoryItems.RemoveAt(_multiPcHistoryItems.Count - 1);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MultiPcRolloutAuditPath)!);
            var entry = new MultiPcRolloutAuditEntry(timestamp, device, action, result);
            File.AppendAllText(MultiPcRolloutAuditPath, System.Text.Json.JsonSerializer.Serialize(entry) + Environment.NewLine);
        }
        catch { }
    }

    private void LoadMultiPcRolloutAudit()
    {
        try
        {
            _multiPcHistoryItems.Clear();
            if (!File.Exists(MultiPcRolloutAuditPath))
            {
                MultiPcStatusText.Text = "Es ist noch kein dauerhaftes Rollout-Auditprotokoll vorhanden.";
                return;
            }
            foreach (string? line in File.ReadLines(MultiPcRolloutAuditPath).Where(line => !string.IsNullOrWhiteSpace(line)).TakeLast(200).Reverse())
            {
                MultiPcRolloutAuditEntry? entry = System.Text.Json.JsonSerializer.Deserialize<MultiPcRolloutAuditEntry>(line);
                if (entry is not null)
                {
                    _multiPcHistoryItems.Add($"{entry.Timestamp.LocalDateTime:g} · {entry.Device} · {entry.Action} · {entry.Result}");
                }
            }
            MultiPcStatusText.Text = $"Auditprotokoll geladen: {_multiPcHistoryItems.Count} Einträge.";
        }
        catch (Exception ex) { MultiPcStatusText.Text = "Auditprotokoll konnte nicht geladen werden: " + ex.Message; }
    }

    private async Task WakeSelectedMultiPcDeviceAsync()
    {
        MultiPcDeviceRecord? device = GetSelectedRemoteDevice();
        if (device is null) { MultiPcStatusText.Text = "Bitte ein Remote-Gerät auswählen."; return; }
        string raw = (device.MacAddress ?? "").Replace(":", "").Replace("-", "").Replace(".", "");
        if (raw.Length != 12 || !raw.All(Uri.IsHexDigit)) { MultiPcStatusText.Text = "Für dieses Gerät ist keine gültige MAC-Adresse gespeichert."; return; }
        byte[] mac = Convert.FromHexString(raw);
        byte[] packet = new byte[6 + (16 * 6)];
        Array.Fill(packet, (byte)0xFF, 0, 6);
        for (int i = 0; i < 16; i++)
        {
            Buffer.BlockCopy(mac, 0, packet, 6 + (i * 6), 6);
        }

        using var udp = new System.Net.Sockets.UdpClient();
        udp.EnableBroadcast = true;
        await udp.SendAsync(packet, packet.Length, new System.Net.IPEndPoint(System.Net.IPAddress.Broadcast, 9));
        MultiPcStatusText.Text = $"Wake-on-LAN-Paket wurde an {device.Name} gesendet.";
        AddMultiPcHistory(device.Name, "wake-on-lan", "gesendet");
    }

    private async Task DiscoverMultiPcAgentsAsync()
    {
        MultiPcStatusText.Text = "Suche Creator Control Agents im lokalen Netzwerk…";
        var found = new List<MultiPcDiscoveryResponse>();
        try
        {
            using var udp = new System.Net.Sockets.UdpClient(0);
            udp.EnableBroadcast = true;
            byte[] request = System.Text.Encoding.UTF8.GetBytes("CCS_DISCOVER_V1");
            await udp.SendAsync(request, request.Length, new System.Net.IPEndPoint(System.Net.IPAddress.Broadcast, 47632));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    UdpReceiveResult response = await udp.ReceiveAsync(cts.Token);
                    string json = System.Text.Encoding.UTF8.GetString(response.Buffer);
                    MultiPcDiscoveryResponse? item = System.Text.Json.JsonSerializer.Deserialize<MultiPcDiscoveryResponse>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (item is not null && found.All(x => !string.Equals(x.Host, item.Host, StringComparison.OrdinalIgnoreCase)))
                    {
                        found.Add(item with { Host = response.RemoteEndPoint.Address.ToString() });
                    }
                }
                catch (OperationCanceledException) { break; }
            }
        }
        catch (Exception ex) { MultiPcStatusText.Text = $"LAN-Suche fehlgeschlagen: {ex.Message}"; return; }
        if (found.Count == 0) { MultiPcStatusText.Text = "Keine Agents gefunden. Prüfe Windows-Firewall und ob der Agent läuft."; return; }
        MultiPcDiscoveryResponse first = found[0];
        MultiPcDeviceNameBox.Text = first.MachineName;
        MultiPcHostBox.Text = first.Host;
        MultiPcAgentPortBox.Text = first.Port.ToString();
        MultiPcMacAddressBox.Text = first.MacAddress ?? "";
        MultiPcStatusText.Text = found.Count == 1 ? $"Agent {first.MachineName} gefunden und in das Kopplungsformular übernommen." : $"{found.Count} Agents gefunden. {first.MachineName} wurde übernommen.";
    }

    private sealed record MultiPcDiscoveryResponse(string MachineName, string Host, int Port, string Version, string? MacAddress);

    private sealed record RemoteObsAudioInput(string Name, bool Muted, double VolumeDb);
    private sealed record RemoteObsSceneItem(string SourceName, bool Enabled);
    private sealed record RemoteObsFilter(string Name, string Kind, bool Enabled, int Index);
    private sealed record RemoteObsState(bool Connected, string CurrentScene, string[] Scenes, RemoteObsAudioInput[] AudioInputs, RemoteObsSceneItem[] SceneItems);
    private sealed record RemoteObsOutputState(bool StreamActive, bool StreamReconnecting, bool RecordActive, bool RecordPaused, string[] Transitions);
    private sealed record MultiPcAgentStatus(string MachineName, double CpuPercent, double MemoryMb, double UptimeMinutes, bool ObsRunning, bool SpotifyRunning, bool StreamerBotRunning, string Version, string Transport, string CertificateFingerprint, string[] AllowedCommands);
}
