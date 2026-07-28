using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.RateLimiting;
using CreatorControlSuite.Agent.Security;
using CreatorControlSuite.Core.Security;
using CreatorControlSuite.Core.Updates;
using CreatorControlSuite.Modules.OBS;
using CreatorControlSuite.Modules.OBS.Models;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Primitives;
using static AgentUtilities;

string agentVersion = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
    .InformationalVersion?
    .Split('+', 2)[0]
    ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
    ?? "unknown";
const int agentPort = 47631;
string dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CreatorControlSuite", "Agent");
Directory.CreateDirectory(dataDirectory);
string keyPath = Path.Combine(dataDirectory, "agent-key.txt");
string certificatePath = Path.Combine(dataDirectory, "agent-certificate.pfx");
string permissionsPath = Path.Combine(dataDirectory, "agent-permissions.json");
string settingsPath = Path.Combine(dataDirectory, "agent-settings.json");
string secretsDirectory = Path.Combine(dataDirectory, "Secrets");
string obsPresetsPath = Path.Combine(dataDirectory, "obs-presets.json");
string agentLogPath = Path.Combine(dataDirectory, "agent.log");
string updateStatePath = Path.Combine(dataDirectory, "update-state.json");
string maintenancePath = Path.Combine(dataDirectory, "maintenance.flag");
string updateHistoryPath = Path.Combine(dataDirectory, "update-history.json");
string updatePublicKeyPath = Path.Combine(AppContext.BaseDirectory, "Keys", "update-public.pem");

ISecretStore secretStore = new WindowsDpapiSecretStore(secretsDirectory);
var credentialStore = new AgentCredentialStore(secretStore);
List<AgentCredential> credentials =
    (await credentialStore.LoadAndMigrateAsync(keyPath)).ToList();
IUpdateSignatureVerifier releaseSignatureVerifier =
    new RsaUpdateSignatureVerifier(updatePublicKeyPath);

X509Certificate2 certificate =
    await new AgentCertificateStore(certificatePath, secretStore)
        .LoadOrCreateAsync();
string certificateFingerprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
AgentPermissions permissions = LoadPermissions(permissionsPath);
var agentSettingsStore = new AgentSettingsStore(settingsPath, secretStore);
AgentSettings agentSettings = await agentSettingsStore.LoadAsync();
var commandHistory = new System.Collections.Concurrent.ConcurrentQueue<CommandHistoryEntry>();
string pairingCode = NewPairingCode();
PairingSession pairingSession = NewPairingSession(pairingCode);
DateTimeOffset startedAt = DateTimeOffset.UtcNow;
string lastUpdateResultPath = Path.Combine(dataDirectory, "last-update-result.txt");
ProcessLastUpdateResult(lastUpdateResultPath, updateStatePath);

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
ConfigureAgentBuilder(builder, agentPort, certificate);
WebApplication app = builder.Build();
AsyncLocal<string?> requestCorrelationId = ConfigureAgentPipeline(app);

Console.WriteLine($"Creator Control Agent {agentVersion} läuft verschlüsselt auf Port {agentPort}.");
Console.WriteLine($"Pairing-Code: {pairingCode}");
Console.WriteLine($"Zertifikat-Fingerabdruck: {certificateFingerprint}");
Console.WriteLine($"Berechtigungsdatei: {permissionsPath}");

AgentCredential? Authenticate(HttpRequest request) =>
    request.Headers.TryGetValue("X-CCS-Agent-Key", out StringValues value)
        ? AgentCredentialStore.Authenticate(credentials, value.ToString())
        : null;
bool Authorized(HttpRequest request) => Authenticate(request) is not null;
bool Running(string name) => Process.GetProcessesByName(name).Length > 0;

async Task<IResult> WithObsControl(HttpRequest request, AgentPermissions permissions, Func<IObsWebSocketClient, Task<IResult>> action)
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    if (!permissions.AllowedCommands.Contains("obs.control", StringComparer.OrdinalIgnoreCase))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    try
    {
        await using ObsWebSocketClient obs = await ConnectObsAsync(agentSettings);
        return await action(obs);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
}

app.MapGet("/api/v1/status", (HttpRequest request) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    var current = Process.GetCurrentProcess();
    return Results.Ok(new
    {
        machineName = Environment.MachineName,
        cpuPercent = 0d,
        memoryMb = current.WorkingSet64 / 1024d / 1024d,
        uptimeMinutes = (DateTimeOffset.UtcNow - startedAt).TotalMinutes,
        obsRunning = Running("obs64"),
        spotifyRunning = Running("Spotify"),
        streamerBotRunning = Running("Streamer.bot") || Running("Streamer.bot-x64"),
        version = agentVersion,
        transport = "HTTPS/TLS",
        certificateFingerprint,
        allowedCommands = permissions.AllowedCommands.OrderBy(x => x).ToArray()
    });
});

app.MapPost("/api/v1/command", async (HttpRequest request) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    CommandRequest? payload = await JsonSerializer.DeserializeAsync<CommandRequest>(request.Body);
    if (payload is null || string.IsNullOrWhiteSpace(payload.Command))
    {
        return Results.BadRequest("command fehlt");
    }

    string command = payload.Command.Trim().ToLowerInvariant();
    if (!permissions.AllowedCommands.Contains(command, StringComparer.OrdinalIgnoreCase))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    try
    {
        switch (command)
        {
            case "obs.start": StartConfigured(agentSettings.ObsPath, "obs64.exe"); break;
            case "obs.stop": foreach (Process p in Process.GetProcessesByName("obs64")) { p.CloseMainWindow(); } break;
            case "spotify.playpause": Process.Start(new ProcessStartInfo("spotify:playpause") { UseShellExecute = true }); break;
            case "streamerbot.start": StartConfigured(agentSettings.StreamerBotPath, "Streamer.bot.exe"); break;
            case "system.restart": Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5 /c \"Creator Control Suite Remote-Neustart\"") { UseShellExecute = false, CreateNoWindow = true }); break;
            case "system.shutdown": Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 5 /c \"Creator Control Suite Remote-Herunterfahren\"") { UseShellExecute = false, CreateNoWindow = true }); break;
            default: return Results.BadRequest("unbekannter Befehl");
        }
        DateTimeOffset acceptedAt = DateTimeOffset.UtcNow;
        commandHistory.Enqueue(new CommandHistoryEntry(acceptedAt, command, "accepted"));
        while (commandHistory.Count > 100)
        {
            commandHistory.TryDequeue(out _);
        }

        return Results.Ok(new { accepted = true, command, acceptedAt });
    }
    catch (Exception ex) { commandHistory.Enqueue(new CommandHistoryEntry(DateTimeOffset.UtcNow, command, "error: " + ex.Message)); return Results.Problem(ex.Message); }
});

app.MapPost("/api/v1/pair", async (HttpRequest request) =>
{
    if (request.ContentLength is > 4096)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status413PayloadTooLarge,
            title: "Pairing-Anfrage ist zu groß.");
    }

    PairingRequest? payload = await JsonSerializer.DeserializeAsync<PairingRequest>(request.Body);
    PairingAttemptResult attempt = pairingSession.TryConsume(
        payload?.Code,
        DateTimeOffset.UtcNow);
    if (attempt != PairingAttemptResult.Accepted)
    {
        AppendAgentLog(
            agentLogPath,
            $"Pairing abgelehnt ({attempt}) von {request.HttpContext.Connection.RemoteIpAddress}.");
        return attempt is PairingAttemptResult.Locked
            ? Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Pairing vorübergehend gesperrt.")
            : Results.Unauthorized();
    }

    AgentCredential credential = await credentialStore.AddAsync(
        payload?.DeviceName ?? "");
    credentials.Add(credential);
    pairingCode = NewPairingCode();
    pairingSession = NewPairingSession(pairingCode);
    AppendAgentLog(
        agentLogPath,
        $"Gerät '{credential.DisplayName}' gekoppelt ({credential.DeviceId}).");
    Console.WriteLine($"Gerät gekoppelt. Neuer Pairing-Code: {pairingCode}");
    return Results.Ok(new
    {
        deviceId = credential.DeviceId,
        machineName = Environment.MachineName,
        agentKey = credential.ApiKey,
        port = agentPort,
        certificateFingerprint,
        transport = "HTTPS/TLS",
        allowedCommands = permissions.AllowedCommands.OrderBy(x => x).ToArray()
    });
}).RequireRateLimiting("pairing");

app.MapPost("/api/v1/credentials/rotate", async (HttpRequest request) =>
{
    AgentCredential? current = Authenticate(request);
    if (current is null)
    {
        return Results.Unauthorized();
    }

    AgentCredential? rotated = await credentialStore.RotateAsync(current.DeviceId);
    if (rotated is null)
    {
        return Results.NotFound();
    }

    int index = credentials.FindIndex(item => item.DeviceId == current.DeviceId);
    if (index >= 0)
    {
        credentials[index] = rotated;
    }

    AppendAgentLog(agentLogPath, $"Agent-Schlüssel rotiert ({current.DeviceId}).");
    return Results.Ok(new
    {
        deviceId = rotated.DeviceId,
        agentKey = rotated.ApiKey
    });
});

app.MapPost("/api/v1/credentials/unpair", async (HttpRequest request) =>
{
    AgentCredential? current = Authenticate(request);
    if (current is null)
    {
        return Results.Unauthorized();
    }

    await credentialStore.DeleteAsync(current.DeviceId);
    credentials.RemoveAll(item => item.DeviceId == current.DeviceId);
    AppendAgentLog(agentLogPath, $"Gerät entkoppelt ({current.DeviceId}).");
    return Results.Ok(new { unpaired = true });
});


app.MapGet("/api/v1/obs/state", async (HttpRequest request) =>
    await WithObsControl(request, permissions, async obs =>
    {
        IReadOnlyList<ObsSceneInfo> scenes = await obs.GetSceneListAsync();
        string currentScene = await obs.GetCurrentProgramSceneAsync();
        IReadOnlyList<ObsInputInfo> inputs = await obs.GetInputListAsync();
        var audio = new List<object>();
        foreach (ObsInputInfo input in inputs)
        {
            try
            {
                ObsInputAudioState state = await obs.GetInputAudioStateAsync(input.Name);
                audio.Add(new { name = input.Name, muted = state.Muted, volumeDb = state.VolumeDb });
            }
            catch { }
        }
        IReadOnlyList<ObsSceneItemInfo> sceneItems = await obs.GetSceneItemListAsync(currentScene);
        return Results.Ok(new { connected = true, currentScene, scenes = scenes.Select(x => x.Name).ToArray(), audioInputs = audio, sceneItems = sceneItems.Select(x => new { sourceName = x.SourceName, enabled = x.Enabled }).ToArray() });
    }));

app.MapPost("/api/v1/obs/scene", async (HttpRequest request) =>
{
    ObsSceneRequest? payload = await JsonSerializer.DeserializeAsync<ObsSceneRequest>(request.Body);
    if (payload is null || string.IsNullOrWhiteSpace(payload.SceneName))
    {
        return Results.BadRequest("sceneName fehlt");
    }

    return await WithObsControl(request, permissions, async obs =>
    {
        await obs.SetCurrentProgramSceneAsync(payload.SceneName);
        return Results.Ok(new { accepted = true, sceneName = payload.SceneName });
    });
});

app.MapPost("/api/v1/obs/mute", async (HttpRequest request) =>
{
    ObsMuteRequest? payload = await JsonSerializer.DeserializeAsync<ObsMuteRequest>(request.Body);
    if (payload is null || string.IsNullOrWhiteSpace(payload.InputName))
    {
        return Results.BadRequest("inputName fehlt");
    }

    return await WithObsControl(request, permissions, async obs =>
    {
        await obs.SetInputMuteAsync(payload.InputName, payload.Muted);
        return Results.Ok(new { accepted = true, inputName = payload.InputName, muted = payload.Muted });
    });
});


app.MapPost("/api/v1/obs/volume", async (HttpRequest request) =>
{
    ObsVolumeRequest? payload = await JsonSerializer.DeserializeAsync<ObsVolumeRequest>(request.Body);
    if (payload is null || string.IsNullOrWhiteSpace(payload.InputName) || payload.VolumeDb is < -100 or > 26)
    {
        return Results.BadRequest("Ungültige Lautstärke");
    }

    return await WithObsControl(request, permissions, async obs =>
    {
        await obs.SetInputVolumeDbAsync(payload.InputName, payload.VolumeDb);
        return Results.Ok(new { accepted = true, payload.InputName, payload.VolumeDb });
    });
});

app.MapPost("/api/v1/obs/scene-item", async (HttpRequest request) =>
{
    ObsSceneItemRequest? payload = await JsonSerializer.DeserializeAsync<ObsSceneItemRequest>(request.Body);
    if (payload is null || string.IsNullOrWhiteSpace(payload.SceneName) || string.IsNullOrWhiteSpace(payload.SourceName))
    {
        return Results.BadRequest("Szene oder Quelle fehlt");
    }

    return await WithObsControl(request, permissions, async obs =>
    {
        await obs.SetSceneItemEnabledAsync(payload.SceneName, payload.SourceName, payload.Enabled);
        return Results.Ok(new { accepted = true, payload.SceneName, payload.SourceName, payload.Enabled });
    });
});

app.MapGet("/api/v1/obs/filters", async (HttpRequest request, string sourceName) =>
    await WithObsControl(request, permissions, async obs =>
        Results.Ok(await obs.GetSourceFilterListAsync(sourceName))));

app.MapPost("/api/v1/obs/filter", async (HttpRequest request) =>
{
    ObsFilterRequest? payload = await JsonSerializer.DeserializeAsync<ObsFilterRequest>(request.Body);
    if (payload is null || string.IsNullOrWhiteSpace(payload.SourceName) || string.IsNullOrWhiteSpace(payload.FilterName))
    {
        return Results.BadRequest();
    }

    return await WithObsControl(request, permissions, async obs =>
    {
        await obs.SetSourceFilterEnabledAsync(payload.SourceName, payload.FilterName, payload.Enabled);
        return Results.Ok(new { accepted = true });
    });
});

app.MapPost("/api/v1/obs/transform", async (HttpRequest request) =>
{
    ObsTransformRequest? payload = await JsonSerializer.DeserializeAsync<ObsTransformRequest>(request.Body);
    if (payload is null || string.IsNullOrWhiteSpace(payload.SceneName) || string.IsNullOrWhiteSpace(payload.SourceName))
    {
        return Results.BadRequest();
    }

    return await WithObsControl(request, permissions, async obs =>
    {
        if (payload.Reset)
        {
            await obs.ResetSceneItemTransformAsync(payload.SceneName, payload.SourceName);
        }
        else
        {
            await obs.SetSceneItemDetailedTransformAsync(
                payload.SceneName,
                payload.SourceName,
                payload.X,
                payload.Y,
                payload.Width,
                payload.Height,
                payload.Rotation,
                0,
                0,
                0,
                0);
        }

        return Results.Ok(new { accepted = true });
    });
});

app.MapPost("/api/v1/obs/volume-fade", async (HttpRequest request) =>
{
    ObsVolumeFadeRequest? payload = await JsonSerializer.DeserializeAsync<ObsVolumeFadeRequest>(request.Body);
    if (payload is null || string.IsNullOrWhiteSpace(payload.InputName))
    {
        return Results.BadRequest();
    }

    return await WithObsControl(request, permissions, async obs =>
    {
        ObsInputAudioState current = await obs.GetInputAudioStateAsync(payload.InputName);
        int duration = Math.Clamp(payload.DurationMilliseconds, 100, 30000);
        int steps = Math.Clamp(duration / 50, 2, 200);
        for (int i = 1; i <= steps; i++)
        {
            double value = current.VolumeDb + ((payload.TargetVolumeDb - current.VolumeDb) * i / steps);
            await obs.SetInputVolumeDbAsync(payload.InputName, value);
            await Task.Delay(Math.Max(10, duration / steps));
        }
        return Results.Ok(new { accepted = true });
    });
});

app.MapGet("/api/v1/obs/configuration", async (HttpRequest request) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    if (!permissions.AllowedCommands.Contains("obs.control", StringComparer.OrdinalIgnoreCase))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    try
    {
        await using ObsWebSocketClient obs = await ConnectObsAsync(agentSettings);
        (string CurrentProfile, IReadOnlyList<string> Profiles) = await obs.GetProfileListAsync();
        (string CurrentSceneCollection, IReadOnlyList<string> SceneCollections) = await obs.GetSceneCollectionListAsync();
        return Results.Ok(new { currentProfile = CurrentProfile, profiles = Profiles, currentSceneCollection = CurrentSceneCollection, sceneCollections = SceneCollections });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPost("/api/v1/obs/configuration", async (HttpRequest request) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    if (!permissions.AllowedCommands.Contains("obs.control", StringComparer.OrdinalIgnoreCase))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    ObsConfigurationRequest? payload = await JsonSerializer.DeserializeAsync<ObsConfigurationRequest>(request.Body);
    if (payload is null || (string.IsNullOrWhiteSpace(payload.ProfileName) && string.IsNullOrWhiteSpace(payload.SceneCollectionName)))
    {
        return Results.BadRequest("Profil oder Szenensammlung fehlt");
    }

    try
    {
        await using ObsWebSocketClient obs = await ConnectObsAsync(agentSettings);
        if (!string.IsNullOrWhiteSpace(payload.ProfileName))
        {
            await obs.SetCurrentProfileAsync(payload.ProfileName);
        }

        if (!string.IsNullOrWhiteSpace(payload.SceneCollectionName))
        {
            await obs.SetCurrentSceneCollectionAsync(payload.SceneCollectionName);
        }

        return Results.Ok(new { accepted = true, payload.ProfileName, payload.SceneCollectionName });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapGet("/api/v1/obs/presets", (HttpRequest request) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    if (!permissions.AllowedCommands.Contains("obs.control", StringComparer.OrdinalIgnoreCase))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    List<ObsRemotePreset> presets = LoadObsPresets(obsPresetsPath);
    return Results.Ok(presets.OrderByDescending(x => x.CreatedAt).Select(x => new { x.Name, x.CreatedAt, x.ProfileName, x.SceneCollectionName, x.CurrentScene }).ToArray());
});

app.MapPost("/api/v1/obs/presets/save", async (HttpRequest request) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    if (!permissions.AllowedCommands.Contains("obs.control", StringComparer.OrdinalIgnoreCase))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    ObsPresetRequest? payload = await JsonSerializer.DeserializeAsync<ObsPresetRequest>(request.Body);
    if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
    {
        return Results.BadRequest("name fehlt");
    }

    try
    {
        await using ObsWebSocketClient obs = await ConnectObsAsync(agentSettings);
        (string CurrentProfile, IReadOnlyList<string> Profiles) = await obs.GetProfileListAsync();
        (string CurrentSceneCollection, IReadOnlyList<string> SceneCollections) = await obs.GetSceneCollectionListAsync();
        string currentScene = await obs.GetCurrentProgramSceneAsync();
        IReadOnlyList<ObsInputInfo> inputs = await obs.GetInputListAsync();
        var audio = new List<ObsPresetAudio>();
        foreach (ObsInputInfo input in inputs)
        {
            try { ObsInputAudioState state = await obs.GetInputAudioStateAsync(input.Name); audio.Add(new ObsPresetAudio(input.Name, state.Muted, state.VolumeDb)); } catch { }
        }
        IReadOnlyList<ObsSceneItemInfo> items = await obs.GetSceneItemListAsync(currentScene);
        var preset = new ObsRemotePreset(payload.Name.Trim(), DateTimeOffset.UtcNow, CurrentProfile, CurrentSceneCollection, currentScene, [.. audio], [.. items.Select(x => new ObsPresetSceneItem(x.SourceName, x.Enabled))]);
        List<ObsRemotePreset> presets = LoadObsPresets(obsPresetsPath);
        presets.RemoveAll(x => string.Equals(x.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        presets.Add(preset);
        SaveObsPresets(obsPresetsPath, presets);
        return Results.Ok(new { accepted = true, preset.Name, preset.CreatedAt });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPost("/api/v1/obs/presets/apply", async (HttpRequest request) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    if (!permissions.AllowedCommands.Contains("obs.control", StringComparer.OrdinalIgnoreCase))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    ObsPresetRequest? payload = await JsonSerializer.DeserializeAsync<ObsPresetRequest>(request.Body);
    if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
    {
        return Results.BadRequest("name fehlt");
    }

    ObsRemotePreset? preset = LoadObsPresets(obsPresetsPath).FirstOrDefault(x => string.Equals(x.Name, payload.Name, StringComparison.OrdinalIgnoreCase));
    if (preset is null)
    {
        return Results.NotFound("Preset nicht gefunden");
    }

    try
    {
        await using (ObsWebSocketClient obs = await ConnectObsAsync(agentSettings))
        {
            (string CurrentProfile, IReadOnlyList<string> Profiles) = await obs.GetProfileListAsync();
            if (!string.IsNullOrWhiteSpace(preset.ProfileName) && !string.Equals(CurrentProfile, preset.ProfileName, StringComparison.Ordinal))
            {
                await obs.SetCurrentProfileAsync(preset.ProfileName);
            }

            (string CurrentSceneCollection, IReadOnlyList<string> SceneCollections) = await obs.GetSceneCollectionListAsync();
            if (!string.IsNullOrWhiteSpace(preset.SceneCollectionName) && !string.Equals(CurrentSceneCollection, preset.SceneCollectionName, StringComparison.Ordinal))
            {
                await obs.SetCurrentSceneCollectionAsync(preset.SceneCollectionName);
            }
        }
        await Task.Delay(600);
        await using (ObsWebSocketClient obs = await ConnectObsAsync(agentSettings))
        {
            if (!string.IsNullOrWhiteSpace(preset.CurrentScene))
            {
                await obs.SetCurrentProgramSceneAsync(preset.CurrentScene);
            }

            foreach (ObsPresetSceneItem item in preset.SceneItems)
            {
                try { await obs.SetSceneItemEnabledAsync(preset.CurrentScene, item.SourceName, item.Enabled); } catch { }
            }
            foreach (ObsPresetAudio input in preset.AudioInputs)
            {
                try { await obs.SetInputVolumeDbAsync(input.Name, input.VolumeDb); await obs.SetInputMuteAsync(input.Name, input.Muted); } catch { }
            }
        }
        return Results.Ok(new { accepted = true, preset.Name });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPost("/api/v1/obs/presets/delete", async (HttpRequest request) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    if (!permissions.AllowedCommands.Contains("obs.control", StringComparer.OrdinalIgnoreCase))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    ObsPresetRequest? payload = await JsonSerializer.DeserializeAsync<ObsPresetRequest>(request.Body);
    if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
    {
        return Results.BadRequest("name fehlt");
    }

    List<ObsRemotePreset> presets = LoadObsPresets(obsPresetsPath);
    int removed = presets.RemoveAll(x => string.Equals(x.Name, payload.Name, StringComparison.OrdinalIgnoreCase));
    if (removed == 0)
    {
        return Results.NotFound("Preset nicht gefunden");
    }

    SaveObsPresets(obsPresetsPath, presets);
    return Results.Ok(new { accepted = true, payload.Name });
});

app.MapGet("/api/v1/obs/output", async (HttpRequest request) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    if (!permissions.AllowedCommands.Contains("obs.control", StringComparer.OrdinalIgnoreCase))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    try
    {
        await using ObsWebSocketClient obs = await ConnectObsAsync(agentSettings);
        ObsStreamStatus stream = await obs.GetStreamStatusAsync();
        ObsOutputStatus record = await obs.GetRecordStatusAsync();
        IReadOnlyList<ObsTransitionInfo> transitions = await obs.GetSceneTransitionListAsync();
        return Results.Ok(new { streamActive = stream.OutputActive, streamReconnecting = stream.OutputReconnecting, recordActive = record.Active, recordPaused = record.Paused, transitions = transitions.Select(x => x.Name).ToArray() });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPost("/api/v1/obs/output", async (HttpRequest request) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    if (!permissions.AllowedCommands.Contains("obs.control", StringComparer.OrdinalIgnoreCase))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    ObsOutputRequest? payload = await JsonSerializer.DeserializeAsync<ObsOutputRequest>(request.Body);
    if (payload is null || string.IsNullOrWhiteSpace(payload.Action))
    {
        return Results.BadRequest("action fehlt");
    }

    try
    {
        await using ObsWebSocketClient obs = await ConnectObsAsync(agentSettings);
        switch (payload.Action.Trim().ToLowerInvariant())
        {
            case "stream.start": await obs.StartStreamAsync(); break;
            case "stream.stop": await obs.StopStreamAsync(); break;
            case "record.start": await obs.StartRecordAsync(); break;
            case "record.stop": await obs.StopRecordAsync(); break;
            case "record.pause": await obs.PauseRecordAsync(); break;
            case "record.resume": await obs.ResumeRecordAsync(); break;
            default: return Results.BadRequest("unbekannte OBS-Ausgabeaktion");
        }
        return Results.Ok(new { accepted = true, action = payload.Action });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPost("/api/v1/obs/transition", async (HttpRequest request) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    if (!permissions.AllowedCommands.Contains("obs.control", StringComparer.OrdinalIgnoreCase))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    ObsTransitionRequest? payload = await JsonSerializer.DeserializeAsync<ObsTransitionRequest>(request.Body);
    if (payload is null || string.IsNullOrWhiteSpace(payload.TransitionName))
    {
        return Results.BadRequest("transitionName fehlt");
    }

    try
    {
        await using ObsWebSocketClient obs = await ConnectObsAsync(agentSettings);
        await obs.SetCurrentSceneTransitionAsync(payload.TransitionName);
        if (payload.DurationMilliseconds is > 0 and <= 20000)
        {
            await obs.SetCurrentSceneTransitionDurationAsync(payload.DurationMilliseconds);
        }

        return Results.Ok(new { accepted = true, transitionName = payload.TransitionName, durationMilliseconds = payload.DurationMilliseconds });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapGet("/api/v1/obs/preview", async (HttpRequest request) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    if (!permissions.AllowedCommands.Contains("obs.control", StringComparer.OrdinalIgnoreCase))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    try
    {
        await using ObsWebSocketClient obs = await ConnectObsAsync(agentSettings);
        string scene = await obs.GetCurrentProgramSceneAsync();
        byte[] image = await obs.GetSourceScreenshotAsync(scene, 640, 360);
        return Results.File(image, "image/png");
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapGet("/api/v1/logs", (HttpRequest request, int? lines) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    int take = Math.Clamp(lines ?? 200, 20, 2000);
    if (!File.Exists(agentLogPath))
    {
        return Results.Ok(Array.Empty<string>());
    }

    return Results.Ok(File.ReadLines(agentLogPath).TakeLast(take).ToArray());
});

app.MapPost("/api/v1/update/stage", async (HttpRequest request) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    if (!permissions.AllowedCommands.Contains("updates.stage", StringComparer.OrdinalIgnoreCase))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    FileDeployRequest? payload = await JsonSerializer.DeserializeAsync<FileDeployRequest>(request.Body);
    if (payload is null ||
        string.IsNullOrWhiteSpace(payload.Base64Zip) ||
        payload.Manifest is null)
    {
        return Results.BadRequest("Update-Daten fehlen");
    }

    try
    {
        string target = string.IsNullOrWhiteSpace(agentSettings.UpdateStagingDirectory)
            ? Path.Combine(dataDirectory, "Updates", DateTime.Now.ToString("yyyyMMdd-HHmmss"))
            : Path.Combine(Path.GetFullPath(agentSettings.UpdateStagingDirectory), DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(target);
        string zipPath = Path.Combine(target, Path.GetFileName(payload.FileName ?? "update.zip"));
        await File.WriteAllBytesAsync(zipPath, Convert.FromBase64String(payload.Base64Zip));
        if (!string.Equals(
                payload.Manifest.ProductId,
                UpdateManifestCanonical.ProductId,
                StringComparison.Ordinal) ||
            !string.Equals(
                payload.Manifest.PackageFileName,
                Path.GetFileName(zipPath),
                StringComparison.OrdinalIgnoreCase) ||
            !releaseSignatureVerifier.VerifyManifest(payload.Manifest) ||
            !await releaseSignatureVerifier.VerifyPackageAsync(
                zipPath,
                payload.Manifest,
                request.HttpContext.RequestAborted))
        {
            File.Delete(zipPath);
            return Results.BadRequest(
                "Update-Manifest, Signatur oder Paket-Prüfsumme ist ungültig.");
        }

        string signedManifestPath = Path.Combine(target, "update-manifest.json");
        await File.WriteAllTextAsync(
            signedManifestPath,
            JsonSerializer.Serialize(payload.Manifest),
            request.HttpContext.RequestAborted);
        string packageDirectory = Path.Combine(target, "package");
        SafeZipExtractor.ExtractToDirectory(zipPath, packageDirectory);
        string checksum = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(zipPath)));
        string[] files = [.. Directory.EnumerateFiles(packageDirectory, "*", SearchOption.AllDirectories)];
        int fileCount = files.Length;
        string packageVersion = payload.Manifest.Version;
        var state = new AgentUpdateState("staged", payload.FileName ?? "update.zip", target, packageDirectory, "", DateTimeOffset.Now, null, "Update wurde mit Release-Signatur geprüft und bereitgestellt.", checksum, fileCount, false, false, null, packageVersion, payload.Manifest.MinimumVersion, payload.Manifest.Signature, true);
        SaveUpdateState(updateStatePath, state);
        AppendUpdateHistory(updateHistoryPath, new AgentUpdateHistoryEntry(DateTimeOffset.Now, "stage", packageVersion, checksum, true, "Update bereitgestellt"));
        AppendAgentLog(agentLogPath, $"Update-Paket '{payload.FileName}' in '{target}' bereitgestellt.");
        return Results.Ok(new { staged = true, target, packageDirectory, restartRequired = true });
    }
    catch (Exception ex) { AppendAgentLog(agentLogPath, "Update-Bereitstellung fehlgeschlagen: " + ex.Message); return Results.Problem(ex.Message); }
});

app.MapGet("/api/v1/update/status", (HttpRequest request) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    return Results.Ok(LoadUpdateState(updateStatePath));
});

app.MapGet("/api/v1/update/history", (HttpRequest request) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    return Results.Ok(LoadUpdateHistory(updateHistoryPath).OrderByDescending(entry => entry.At).Take(100));
});

app.MapPost("/api/v1/update/validate", (HttpRequest request) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    if (!permissions.AllowedCommands.Contains("updates.apply", StringComparer.OrdinalIgnoreCase))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    AgentUpdateState state = LoadUpdateState(updateStatePath);
    if (!Directory.Exists(state.PackageDirectory))
    {
        return Results.BadRequest("Kein Update-Paket vorhanden.");
    }

    string[] files = [.. Directory.EnumerateFiles(state.PackageDirectory, "*", SearchOption.AllDirectories)];
    bool hasExecutable = files.Any(path => path.EndsWith("CreatorControlSuite.App.exe", StringComparison.OrdinalIgnoreCase));
    string manifestPath = Path.Combine(state.StagingDirectory, "update-manifest.json");
    SignedUpdateManifest? manifest = File.Exists(manifestPath)
        ? JsonSerializer.Deserialize<SignedUpdateManifest>(File.ReadAllText(manifestPath))
        : null;
    string archivePath = Path.Combine(state.StagingDirectory, state.PackageName);
    bool signatureValid = manifest is not null &&
        releaseSignatureVerifier.VerifyManifest(manifest) &&
        releaseSignatureVerifier.VerifyPackageAsync(
            archivePath,
            manifest).GetAwaiter().GetResult();
    bool compatible = IsCompatibleVersion(agentVersion, state.MinimumAgentVersion);
    bool valid = files.Length > 0 && hasExecutable && signatureValid && compatible;
    string message = valid
        ? $"Paket geprüft: {files.Length} Dateien, Version {state.PackageVersion}, Manifest-Signatur gültig und Agent kompatibel."
        : $"Paketprüfung fehlgeschlagen: Programm={hasExecutable}, Signatur={signatureValid}, kompatibel={compatible}.";
    AgentUpdateState updated = state with { Status = valid ? "validated" : "invalid", FileCount = files.Length, Validated = valid, Message = message, SignatureValid = signatureValid };
    SaveUpdateState(updateStatePath, updated);
    AppendUpdateHistory(updateHistoryPath, new AgentUpdateHistoryEntry(DateTimeOffset.Now, "validate", state.PackageVersion, state.Sha256, valid, message));
    AppendAgentLog(agentLogPath, message);
    return valid ? Results.Ok(updated) : Results.BadRequest(message);
});

app.MapPost("/api/v1/update/apply", async (HttpRequest request) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    if (!permissions.AllowedCommands.Contains("updates.apply", StringComparer.OrdinalIgnoreCase))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    UpdateApplyRequest payload = await JsonSerializer.DeserializeAsync<UpdateApplyRequest>(request.Body) ?? new UpdateApplyRequest(false, true);
    AgentUpdateState state = LoadUpdateState(updateStatePath);
    if (!(string.Equals(state.Status, "staged", StringComparison.OrdinalIgnoreCase) || string.Equals(state.Status, "validated", StringComparison.OrdinalIgnoreCase)) || !Directory.Exists(state.PackageDirectory))
    {
        return Results.BadRequest("Es ist kein anwendbares Update bereitgestellt.");
    }

    try
    {
        string installDirectory = string.IsNullOrWhiteSpace(agentSettings.SuiteInstallDirectory) ? AppContext.BaseDirectory : Path.GetFullPath(agentSettings.SuiteInstallDirectory);
        string executable = string.IsNullOrWhiteSpace(agentSettings.SuiteExecutablePath) ? Path.Combine(installDirectory, "CreatorControlSuite.App.exe") : Path.GetFullPath(agentSettings.SuiteExecutablePath);
        string backupDirectory = Path.Combine(dataDirectory, "update-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(Path.GetDirectoryName(backupDirectory)!);
        CopyDirectory(installDirectory, backupDirectory, path => !path.Contains(Path.Combine("Agent", "Updates"), StringComparison.OrdinalIgnoreCase));
        string scriptPath = Path.Combine(dataDirectory, "apply-update-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".cmd");
        string processName = Path.GetFileNameWithoutExtension(executable);
        string restartLine = payload.RestartSuite ? $"if exist \"{executable}\" start \"\" \"{executable}\"" : "rem Suite-Neustart nicht angefordert";
        string resultPath = Path.Combine(dataDirectory, "last-update-result.txt");
        string healthBlock = payload.RestartSuite && payload.AutomaticRollback
            ? $"timeout /t 15 /nobreak >nul\r\ntasklist /FI \"IMAGENAME eq {processName}.exe\" | find /I \"{processName}.exe\" >nul\r\nif errorlevel 1 (\r\n  robocopy \"{backupDirectory}\" \"{installDirectory}\" /E /R:2 /W:1 /NFL /NDL /NJH /NJS\r\n  if exist \"{executable}\" start \"\" \"{executable}\"\r\n  echo automatic-rollback>\"{resultPath}\"\r\n) else (echo healthy>\"{resultPath}\")"
            : $"echo applied>\"{resultPath}\"";
        File.WriteAllText(maintenancePath, DateTimeOffset.Now.ToString("O"));
        string script = $"@echo off\r\ntimeout /t 3 /nobreak >nul\r\nrobocopy \"{state.PackageDirectory}\" \"{installDirectory}\" /E /R:2 /W:1 /NFL /NDL /NJH /NJS\r\n{restartLine}\r\n{healthBlock}\r\ndel /q \"{maintenancePath}\" 2>nul\r\n";
        await File.WriteAllTextAsync(scriptPath, script);
        SaveUpdateState(updateStatePath, state with { Status = "applying", BackupDirectory = backupDirectory, AppliedAt = DateTimeOffset.Now, Message = "Update wird im Wartungsmodus angewendet; anschließend folgt der Health-Check.", MaintenanceMode = true, AutomaticRollback = payload.AutomaticRollback });
        AppendUpdateHistory(updateHistoryPath, new AgentUpdateHistoryEntry(DateTimeOffset.Now, "apply", state.PackageVersion, state.Sha256, true, "Update-Anwendung gestartet"));
        AppendAgentLog(agentLogPath, $"Update-Anwendung vorbereitet. Backup: '{backupDirectory}'.");
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c start \"CCS Update\" /min \"{scriptPath}\"") { UseShellExecute = false, CreateNoWindow = true });
        _ = Task.Run(async () => { await Task.Delay(750); Environment.Exit(0); });
        return Results.Accepted(value: new { applying = true, backupDirectory, agentRestartRequired = true });
    }
    catch (Exception ex) { AppendAgentLog(agentLogPath, "Update-Anwendung fehlgeschlagen: " + ex.Message); return Results.Problem(ex.Message); }
});

app.MapPost("/api/v1/update/rollback", async (HttpRequest request) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    if (!permissions.AllowedCommands.Contains("updates.apply", StringComparer.OrdinalIgnoreCase))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    AgentUpdateState state = LoadUpdateState(updateStatePath);
    if (string.IsNullOrWhiteSpace(state.BackupDirectory) || !Directory.Exists(state.BackupDirectory))
    {
        return Results.BadRequest("Kein Rollback-Backup verfügbar.");
    }

    try
    {
        string installDirectory = string.IsNullOrWhiteSpace(agentSettings.SuiteInstallDirectory) ? AppContext.BaseDirectory : Path.GetFullPath(agentSettings.SuiteInstallDirectory);
        string executable = string.IsNullOrWhiteSpace(agentSettings.SuiteExecutablePath) ? Path.Combine(installDirectory, "CreatorControlSuite.App.exe") : Path.GetFullPath(agentSettings.SuiteExecutablePath);
        string scriptPath = Path.Combine(dataDirectory, "rollback-update-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".cmd");
        string script = $"@echo off\r\ntimeout /t 3 /nobreak >nul\r\nrobocopy \"{state.BackupDirectory}\" \"{installDirectory}\" /E /R:2 /W:1 /NFL /NDL /NJH /NJS\r\nif exist \"{executable}\" start \"\" \"{executable}\"\r\n";
        await File.WriteAllTextAsync(scriptPath, script);
        SaveUpdateState(updateStatePath, state with { Status = "rolling-back", AppliedAt = DateTimeOffset.Now, Message = "Rollback wird angewendet." });
        AppendUpdateHistory(updateHistoryPath, new AgentUpdateHistoryEntry(DateTimeOffset.Now, "rollback", state.PackageVersion, state.Sha256, true, "Rollback gestartet"));
        AppendAgentLog(agentLogPath, $"Rollback aus '{state.BackupDirectory}' gestartet.");
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c start \"CCS Rollback\" /min \"{scriptPath}\"") { UseShellExecute = false, CreateNoWindow = true });
        _ = Task.Run(async () => { await Task.Delay(750); Environment.Exit(0); });
        return Results.Accepted(value: new { rollingBack = true, agentRestartRequired = true });
    }
    catch (Exception ex) { AppendAgentLog(agentLogPath, "Rollback fehlgeschlagen: " + ex.Message); return Results.Problem(ex.Message); }
});

app.MapPost("/api/v1/settings", async (HttpRequest request) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    AgentSettings? updated = await JsonSerializer.DeserializeAsync<AgentSettings>(request.Body);
    if (updated is null || updated.ObsWebSocketPort is <= 0 or > 65535)
    {
        return Results.BadRequest("Ungültige Einstellungen");
    }

    agentSettings = updated;
    await agentSettingsStore.SaveAsync(agentSettings);
    return Results.Ok(new { saved = true });
});

app.MapGet("/api/v1/history", (HttpRequest request) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    return Results.Ok(commandHistory.Reverse().Take(50).ToArray());
});

_ = RunDiscoveryAsync(agentPort, agentVersion);

app.Run();

void AppendAgentLog(string path, string message)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    string correlation = requestCorrelationId.Value is null
        ? ""
        : $" correlationId={requestCorrelationId.Value}";
    File.AppendAllText(
        path,
        $"{DateTimeOffset.Now:O}{correlation} {SecretRedactor.Redact(message)}{Environment.NewLine}");
}
