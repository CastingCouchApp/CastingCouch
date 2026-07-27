using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using CreatorControlSuite.Modules.OBS;
using CreatorControlSuite.Modules.OBS.Models;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Primitives;

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
string obsPresetsPath = Path.Combine(dataDirectory, "obs-presets.json");
string agentLogPath = Path.Combine(dataDirectory, "agent.log");
string updateStatePath = Path.Combine(dataDirectory, "update-state.json");
string maintenancePath = Path.Combine(dataDirectory, "maintenance.flag");
string updateHistoryPath = Path.Combine(dataDirectory, "update-history.json");

string agentKey = File.Exists(keyPath)
    ? File.ReadAllText(keyPath).Trim()
    : Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
if (!File.Exists(keyPath))
{
    File.WriteAllText(keyPath, agentKey);
}

X509Certificate2 certificate = LoadOrCreateCertificate(certificatePath);
string certificateFingerprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);
AgentPermissions permissions = LoadPermissions(permissionsPath);
AgentSettings agentSettings = LoadSettings(settingsPath);
var commandHistory = new System.Collections.Concurrent.ConcurrentQueue<CommandHistoryEntry>();
string pairingCode = NewPairingCode();
DateTimeOffset startedAt = DateTimeOffset.UtcNow;
string lastUpdateResultPath = Path.Combine(dataDirectory, "last-update-result.txt");
if (File.Exists(lastUpdateResultPath))
{
    string result = File.ReadAllText(lastUpdateResultPath).Trim();
    AgentUpdateState previous = LoadUpdateState(updateStatePath);
    string message = result == "automatic-rollback" ? "Health-Check fehlgeschlagen; automatisches Rollback wurde ausgeführt."
        : result == "healthy" ? "Update erfolgreich; Health-Check bestanden."
        : "Update wurde angewendet.";
    SaveUpdateState(updateStatePath, previous with { Status = result == "automatic-rollback" ? "rolled-back" : "healthy", MaintenanceMode = false, Message = message });
    File.Delete(lastUpdateResultPath);
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(agentPort, listen => listen.UseHttps(new HttpsConnectionAdapterOptions
    {
        ServerCertificate = certificate
    }));
});
WebApplication app = builder.Build();

Console.WriteLine($"Creator Control Agent {agentVersion} läuft verschlüsselt auf Port {agentPort}.");
Console.WriteLine($"Pairing-Code: {pairingCode}");
Console.WriteLine($"Zertifikat-Fingerabdruck: {certificateFingerprint}");
Console.WriteLine($"Berechtigungsdatei: {permissionsPath}");

bool Authorized(HttpRequest request) => request.Headers.TryGetValue("X-CCS-Agent-Key", out StringValues value) && CryptographicOperations.FixedTimeEquals(
    System.Text.Encoding.UTF8.GetBytes(value.ToString()), System.Text.Encoding.UTF8.GetBytes(agentKey));
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

app.MapGet("/api/status", (HttpRequest request) =>
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

app.MapPost("/api/command", async (HttpRequest request) =>
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

app.MapGet("/api/pair", (string code) =>
{
    if (!string.Equals(code, pairingCode, StringComparison.Ordinal))
    {
        return Results.Unauthorized();
    }

    pairingCode = NewPairingCode();
    Console.WriteLine($"Gerät gekoppelt. Neuer Pairing-Code: {pairingCode}");
    return Results.Ok(new
    {
        machineName = Environment.MachineName,
        agentKey,
        port = agentPort,
        certificateFingerprint,
        transport = "HTTPS/TLS",
        allowedCommands = permissions.AllowedCommands.OrderBy(x => x).ToArray()
    });
});


app.MapGet("/api/obs/state", async (HttpRequest request) =>
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

app.MapPost("/api/obs/scene", async (HttpRequest request) =>
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

app.MapPost("/api/obs/mute", async (HttpRequest request) =>
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


app.MapPost("/api/obs/volume", async (HttpRequest request) =>
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

app.MapPost("/api/obs/scene-item", async (HttpRequest request) =>
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

app.MapGet("/api/obs/filters", async (HttpRequest request, string sourceName) =>
    await WithObsControl(request, permissions, async obs =>
        Results.Ok(await obs.GetSourceFilterListAsync(sourceName))));

app.MapPost("/api/obs/filter", async (HttpRequest request) =>
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

app.MapPost("/api/obs/transform", async (HttpRequest request) =>
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

app.MapPost("/api/obs/volume-fade", async (HttpRequest request) =>
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

app.MapGet("/api/obs/configuration", async (HttpRequest request) =>
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

app.MapPost("/api/obs/configuration", async (HttpRequest request) =>
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

app.MapGet("/api/obs/presets", (HttpRequest request) =>
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

app.MapPost("/api/obs/presets/save", async (HttpRequest request) =>
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

app.MapPost("/api/obs/presets/apply", async (HttpRequest request) =>
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

app.MapPost("/api/obs/presets/delete", async (HttpRequest request) =>
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

app.MapGet("/api/obs/output", async (HttpRequest request) =>
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

app.MapPost("/api/obs/output", async (HttpRequest request) =>
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

app.MapPost("/api/obs/transition", async (HttpRequest request) =>
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

app.MapGet("/api/obs/preview", async (HttpRequest request) =>
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

app.MapGet("/api/logs", (HttpRequest request, int? lines) =>
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

app.MapPost("/api/overlay/deploy", async (HttpRequest request) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    if (!permissions.AllowedCommands.Contains("files.deploy", StringComparer.OrdinalIgnoreCase))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    FileDeployRequest? payload = await JsonSerializer.DeserializeAsync<FileDeployRequest>(request.Body);
    if (payload is null || string.IsNullOrWhiteSpace(payload.Base64Zip))
    {
        return Results.BadRequest("ZIP-Daten fehlen");
    }

    try
    {
        string target = string.IsNullOrWhiteSpace(agentSettings.OverlayDirectory)
            ? Path.Combine(dataDirectory, "Overlays") : Path.GetFullPath(agentSettings.OverlayDirectory);
        Directory.CreateDirectory(target);
        string backup = Path.Combine(dataDirectory, "overlay-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            CopyDirectory(target, backup);
        }

        string temp = Path.Combine(dataDirectory, "overlay-upload.zip");
        await File.WriteAllBytesAsync(temp, Convert.FromBase64String(payload.Base64Zip));
        SafeExtractZip(temp, target);
        File.Delete(temp);
        AppendAgentLog(agentLogPath, $"Overlay-Paket '{payload.FileName}' nach '{target}' verteilt.");
        return Results.Ok(new { deployed = true, target, backup });
    }
    catch (Exception ex) { AppendAgentLog(agentLogPath, "Overlay-Verteilung fehlgeschlagen: " + ex.Message); return Results.Problem(ex.Message); }
});

app.MapPost("/api/update/stage", async (HttpRequest request) =>
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
    if (payload is null || string.IsNullOrWhiteSpace(payload.Base64Zip))
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
        string packageDirectory = Path.Combine(target, "package");
        SafeExtractZip(zipPath, packageDirectory);
        string checksum = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(zipPath)));
        string[] files = [.. Directory.EnumerateFiles(packageDirectory, "*", SearchOption.AllDirectories)];
        int fileCount = files.Length;
        string packageVersion = DetectPackageVersion(packageDirectory);
        string manifestPayload = $"{payload.FileName}|{checksum}|{fileCount}|{packageVersion}|{agentVersion}";
        string manifestSignature = Convert.ToHexString(HMACSHA256.HashData(Convert.FromHexString(agentKey), System.Text.Encoding.UTF8.GetBytes(manifestPayload)));
        var state = new AgentUpdateState("staged", payload.FileName ?? "update.zip", target, packageDirectory, "", DateTimeOffset.Now, null, "Update wurde bereitgestellt und mit dem Agent-Schlüssel signiert.", checksum, fileCount, false, false, null, packageVersion, agentVersion, manifestSignature, false);
        SaveUpdateState(updateStatePath, state);
        AppendUpdateHistory(updateHistoryPath, new AgentUpdateHistoryEntry(DateTimeOffset.Now, "stage", packageVersion, checksum, true, "Update bereitgestellt"));
        AppendAgentLog(agentLogPath, $"Update-Paket '{payload.FileName}' in '{target}' bereitgestellt.");
        return Results.Ok(new { staged = true, target, packageDirectory, restartRequired = true });
    }
    catch (Exception ex) { AppendAgentLog(agentLogPath, "Update-Bereitstellung fehlgeschlagen: " + ex.Message); return Results.Problem(ex.Message); }
});

app.MapGet("/api/update/status", (HttpRequest request) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    return Results.Ok(LoadUpdateState(updateStatePath));
});

app.MapGet("/api/update/history", (HttpRequest request) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    return Results.Ok(LoadUpdateHistory(updateHistoryPath).OrderByDescending(entry => entry.At).Take(100));
});

app.MapPost("/api/update/validate", (HttpRequest request) =>
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
    string manifestPayload = $"{state.PackageName}|{state.Sha256}|{state.FileCount}|{state.PackageVersion}|{state.MinimumAgentVersion}";
    string expectedSignature = Convert.ToHexString(HMACSHA256.HashData(Convert.FromHexString(agentKey), System.Text.Encoding.UTF8.GetBytes(manifestPayload)));
    bool signatureValid = !string.IsNullOrWhiteSpace(state.ManifestSignature) && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expectedSignature), Convert.FromHexString(state.ManifestSignature));
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

app.MapPost("/api/update/apply", async (HttpRequest request) =>
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

app.MapPost("/api/update/rollback", async (HttpRequest request) =>
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

app.MapPost("/api/settings", async (HttpRequest request) =>
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
    File.WriteAllText(settingsPath, JsonSerializer.Serialize(agentSettings, new JsonSerializerOptions { WriteIndented = true }));
    return Results.Ok(new { saved = true });
});

app.MapGet("/api/history", (HttpRequest request) =>
{
    if (!Authorized(request))
    {
        return Results.Unauthorized();
    }

    return Results.Ok(commandHistory.Reverse().Take(50).ToArray());
});

_ = Task.Run(async () =>
{
    using var udp = new System.Net.Sockets.UdpClient(47632);
    while (true)
    {
        try
        {
            System.Net.Sockets.UdpReceiveResult received = await udp.ReceiveAsync();
            if (System.Text.Encoding.UTF8.GetString(received.Buffer) != "CCS_DISCOVER_V1")
            {
                continue;
            }

            string mac = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(x => x.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && x.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                .Select(x => x.GetPhysicalAddress().ToString())
                .FirstOrDefault(x => x.Length == 12) ?? "";
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new { machineName = Environment.MachineName, host = Environment.MachineName, port = agentPort, version = agentVersion, macAddress = mac });
            await udp.SendAsync(payload, payload.Length, received.RemoteEndPoint);
        }
        catch (Exception ex) { Console.WriteLine("LAN-Erkennung: " + ex.Message); await Task.Delay(1000); }
    }
});

app.Run();


static async Task<ObsWebSocketClient> ConnectObsAsync(AgentSettings settings)
{
    var client = new ObsWebSocketClient();
    await client.ConnectAsync(new ObsConnectionOptions(settings.ObsWebSocketHost, settings.ObsWebSocketPort, settings.ObsWebSocketPassword, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6)));
    return client;
}

static void AppendAgentLog(string path, string message)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
}

static void CopyDirectory(string source, string destination, Func<string, bool>? include = null)
{
    Directory.CreateDirectory(destination);
    foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
    {
        string relative = Path.GetRelativePath(source, file);
        if (include is not null && !include(relative))
        {
            continue;
        }

        string target = Path.Combine(destination, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file, target, true);
    }
}


static string DetectPackageVersion(string packageDirectory)
{
    string? changelog = Directory.EnumerateFiles(packageDirectory, "CHANGELOG-8.0.0-alpha*.md", SearchOption.AllDirectories)
        .Select(Path.GetFileNameWithoutExtension)
        .OrderByDescending(name => name, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault();
    return changelog?.Replace("CHANGELOG-", "", StringComparison.OrdinalIgnoreCase) ?? "unbekannt";
}

static bool IsCompatibleVersion(string currentVersion, string minimumVersion)
{
    if (string.IsNullOrWhiteSpace(minimumVersion))
    {
        return true;
    }

    static int AlphaNumber(string value)
    {
        string marker = "alpha";
        int index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return index >= 0 && int.TryParse(value[(index + marker.Length)..], out int number) ? number : 0;
    }
    return AlphaNumber(currentVersion) >= AlphaNumber(minimumVersion);
}

static List<AgentUpdateHistoryEntry> LoadUpdateHistory(string path)
{
    if (!File.Exists(path))
    {
        return [];
    }

    try { return JsonSerializer.Deserialize<List<AgentUpdateHistoryEntry>>(File.ReadAllText(path)) ?? []; }
    catch { return []; }
}

static void AppendUpdateHistory(string path, AgentUpdateHistoryEntry entry)
{
    List<AgentUpdateHistoryEntry> history = LoadUpdateHistory(path);
    history.Add(entry);
    if (history.Count > 250)
    {
        history = [.. history.OrderByDescending(item => item.At).Take(250)];
    }

    string temp = path + ".tmp";
    File.WriteAllText(temp, JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true }));
    File.Move(temp, path, true);
}

static AgentUpdateState LoadUpdateState(string path)
{
    if (!File.Exists(path))
    {
        return AgentUpdateState.Empty;
    }

    try { return JsonSerializer.Deserialize<AgentUpdateState>(File.ReadAllText(path)) ?? AgentUpdateState.Empty; }
    catch { return AgentUpdateState.Empty with { Status = "error", Message = "Update-Statusdatei konnte nicht gelesen werden." }; }
}

static void SaveUpdateState(string path, AgentUpdateState state)
{
    string temp = path + ".tmp";
    File.WriteAllText(temp, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    File.Move(temp, path, true);
}

static void SafeExtractZip(string zipPath, string destination)
{
    Directory.CreateDirectory(destination);
    string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
    using ZipArchive archive = ZipFile.OpenRead(zipPath);
    foreach (ZipArchiveEntry entry in archive.Entries)
    {
        string target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Unsicherer ZIP-Pfad");
        }

        if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        entry.ExtractToFile(target, true);
    }
}

static void StartConfigured(string? configuredPath, string fallback)
{
    string target = !string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath) ? configuredPath : fallback;
    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
}

static AgentSettings LoadSettings(string path)
{
    if (File.Exists(path))
    {
        try { return JsonSerializer.Deserialize<AgentSettings>(File.ReadAllText(path)) ?? new AgentSettings(); } catch { }
    }
    var settings = new AgentSettings();
    File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    return settings;
}

static string NewPairingCode() => Random.Shared.Next(100000, 1000000).ToString(System.Globalization.CultureInfo.InvariantCulture);

static X509Certificate2 LoadOrCreateCertificate(string path)
{
    if (File.Exists(path))
    {
        return X509CertificateLoader.LoadPkcs12FromFile(path, null, X509KeyStorageFlags.Exportable);
    }

    using var rsa = RSA.Create(3072);
    var request = new CertificateRequest("CN=CreatorControlSuite.Agent", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
    request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
    request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
    using X509Certificate2 generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
    byte[] bytes = generated.Export(X509ContentType.Pfx);
    File.WriteAllBytes(path, bytes);
    return X509CertificateLoader.LoadPkcs12(bytes, null, X509KeyStorageFlags.Exportable);
}

static List<ObsRemotePreset> LoadObsPresets(string path)
{
    if (!File.Exists(path))
    {
        return [];
    }

    try { return JsonSerializer.Deserialize<List<ObsRemotePreset>>(File.ReadAllText(path)) ?? []; }
    catch { return []; }
}

static void SaveObsPresets(string path, List<ObsRemotePreset> presets)
{
    string tempPath = path + ".tmp";
    File.WriteAllText(tempPath, JsonSerializer.Serialize(presets, new JsonSerializerOptions { WriteIndented = true }));
    File.Move(tempPath, path, true);
}

static AgentPermissions LoadPermissions(string path)
{
    if (File.Exists(path))
    {
        try { return JsonSerializer.Deserialize<AgentPermissions>(File.ReadAllText(path)) ?? AgentPermissions.Default; }
        catch { }
    }
    AgentPermissions defaults = AgentPermissions.Default;
    File.WriteAllText(path, JsonSerializer.Serialize(defaults, new JsonSerializerOptions { WriteIndented = true }));
    return defaults;
}

internal sealed record CommandRequest(string Command);
internal sealed record ObsSceneRequest(string SceneName);
internal sealed record ObsMuteRequest(string InputName, bool Muted);
internal sealed record ObsVolumeRequest(string InputName, double VolumeDb);
internal sealed record ObsSceneItemRequest(string SceneName, string SourceName, bool Enabled);
internal sealed record ObsFilterRequest(string SourceName, string FilterName, bool Enabled);
internal sealed record ObsTransformRequest(string SceneName, string SourceName, bool Reset, double X, double Y, double Width, double Height, double Rotation);
internal sealed record ObsConfigurationRequest(string ProfileName, string SceneCollectionName);
internal sealed record ObsPresetRequest(string Name);
internal sealed record ObsPresetAudio(string Name, bool Muted, double VolumeDb);
internal sealed record ObsPresetSceneItem(string SourceName, bool Enabled);
internal sealed record ObsRemotePreset(string Name, DateTimeOffset CreatedAt, string ProfileName, string SceneCollectionName, string CurrentScene, ObsPresetAudio[] AudioInputs, ObsPresetSceneItem[] SceneItems);
internal sealed record ObsVolumeFadeRequest(string InputName, double TargetVolumeDb, int DurationMilliseconds);
internal sealed record ObsOutputRequest(string Action);
internal sealed record ObsTransitionRequest(string TransitionName, int DurationMilliseconds);
internal sealed record AgentPermissions(string[] AllowedCommands)
{
    public static AgentPermissions Default { get; } = new(["obs.start", "obs.stop", "obs.control", "spotify.playpause", "streamerbot.start", "files.deploy", "updates.stage", "updates.apply"]);
}

internal sealed record AgentSettings(string ObsPath = "", string StreamerBotPath = "", string ObsWebSocketHost = "127.0.0.1", int ObsWebSocketPort = 4455, string ObsWebSocketPassword = "", string OverlayDirectory = "", string UpdateStagingDirectory = "", string SuiteInstallDirectory = "", string SuiteExecutablePath = "");
internal sealed record FileDeployRequest(string FileName, string Base64Zip);
internal sealed record CommandHistoryEntry(DateTimeOffset At, string Command, string Result);

internal sealed record UpdateApplyRequest(bool RestartSuite, bool AutomaticRollback);
internal sealed record AgentUpdateHistoryEntry(DateTimeOffset At, string Action, string PackageVersion, string Sha256, bool Success, string Message);
internal sealed record AgentUpdateState(string Status, string PackageName, string StagingDirectory, string PackageDirectory, string BackupDirectory, DateTimeOffset StagedAt, DateTimeOffset? AppliedAt, string Message, string Sha256, int FileCount, bool Validated, bool MaintenanceMode, bool? AutomaticRollback, string PackageVersion, string MinimumAgentVersion, string ManifestSignature, bool SignatureValid)
{
    public static AgentUpdateState Empty { get; } = new("none", "", "", "", "", DateTimeOffset.MinValue, null, "Kein Update bereitgestellt.", "", 0, false, false, null, "", "", "", false);
}
