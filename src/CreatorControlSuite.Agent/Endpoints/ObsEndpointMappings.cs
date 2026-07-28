using System.Text.Json;
using CreatorControlSuite.Modules.OBS;
using CreatorControlSuite.Modules.OBS.Models;
using static AgentUtilities;

internal sealed record ObsEndpointDependencies(
    Func<HttpRequest, bool> Authorized,
    AgentPermissions Permissions,
    Func<Task<IObsWebSocketClient>> ConnectAsync,
    string ObsPresetsPath);

internal static class ObsEndpointMappings
{
    internal static void MapObsEndpoints(
        this WebApplication app,
        ObsEndpointDependencies dependencies)
    {
        app.MapGet("/api/v1/obs/state", async (HttpRequest request) =>
            await WithObsControl(request, dependencies, async obs =>
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
                return AgentApiResults.BadRequest("sceneName fehlt");
            }

            return await WithObsControl(request, dependencies, async obs =>
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
                return AgentApiResults.BadRequest("inputName fehlt");
            }

            return await WithObsControl(request, dependencies, async obs =>
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
                return AgentApiResults.BadRequest("Ungültige Lautstärke");
            }

            return await WithObsControl(request, dependencies, async obs =>
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
                return AgentApiResults.BadRequest("Szene oder Quelle fehlt");
            }

            return await WithObsControl(request, dependencies, async obs =>
            {
                await obs.SetSceneItemEnabledAsync(payload.SceneName, payload.SourceName, payload.Enabled);
                return Results.Ok(new { accepted = true, payload.SceneName, payload.SourceName, payload.Enabled });
            });
        });

        app.MapGet("/api/v1/obs/filters", async (HttpRequest request, string sourceName) =>
            await WithObsControl(request, dependencies, async obs =>
                Results.Ok(await obs.GetSourceFilterListAsync(sourceName))));

        app.MapPost("/api/v1/obs/filter", async (HttpRequest request) =>
        {
            ObsFilterRequest? payload = await JsonSerializer.DeserializeAsync<ObsFilterRequest>(request.Body);
            if (payload is null || string.IsNullOrWhiteSpace(payload.SourceName) || string.IsNullOrWhiteSpace(payload.FilterName))
            {
                return AgentApiResults.BadRequest("Ungültiger Filter.");
            }

            return await WithObsControl(request, dependencies, async obs =>
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
                return AgentApiResults.BadRequest("Ungültige Transformation.");
            }

            return await WithObsControl(request, dependencies, async obs =>
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
                return AgentApiResults.BadRequest("Ungültige Lautstärkeüberblendung.");
            }

            return await WithObsControl(request, dependencies, async obs =>
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
            if (!dependencies.Authorized(request))
            {
                return AgentApiResults.Unauthorized();
            }

            if (!dependencies.Permissions.AllowedCommands.Contains("obs.control", StringComparer.OrdinalIgnoreCase))
            {
                return AgentApiResults.Forbidden("obs.control");
            }

            try
            {
                await using IObsWebSocketClient obs = await dependencies.ConnectAsync();
                (string CurrentProfile, IReadOnlyList<string> Profiles) = await obs.GetProfileListAsync();
                (string CurrentSceneCollection, IReadOnlyList<string> SceneCollections) = await obs.GetSceneCollectionListAsync();
                return Results.Ok(new { currentProfile = CurrentProfile, profiles = Profiles, currentSceneCollection = CurrentSceneCollection, sceneCollections = SceneCollections });
            }
            catch (Exception ex) { return AgentApiResults.InternalError(ex); }
        });

        app.MapPost("/api/v1/obs/configuration", async (HttpRequest request) =>
        {
            if (!dependencies.Authorized(request))
            {
                return AgentApiResults.Unauthorized();
            }

            if (!dependencies.Permissions.AllowedCommands.Contains("obs.control", StringComparer.OrdinalIgnoreCase))
            {
                return AgentApiResults.Forbidden("obs.control");
            }

            ObsConfigurationRequest? payload = await JsonSerializer.DeserializeAsync<ObsConfigurationRequest>(request.Body);
            if (payload is null || (string.IsNullOrWhiteSpace(payload.ProfileName) && string.IsNullOrWhiteSpace(payload.SceneCollectionName)))
            {
                return AgentApiResults.BadRequest("Profil oder Szenensammlung fehlt");
            }

            try
            {
                await using IObsWebSocketClient obs = await dependencies.ConnectAsync();
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
            catch (Exception ex) { return AgentApiResults.InternalError(ex); }
        });

        app.MapGet("/api/v1/obs/presets", (HttpRequest request) =>
        {
            if (!dependencies.Authorized(request))
            {
                return AgentApiResults.Unauthorized();
            }

            if (!dependencies.Permissions.AllowedCommands.Contains("obs.control", StringComparer.OrdinalIgnoreCase))
            {
                return AgentApiResults.Forbidden("obs.control");
            }

            List<ObsRemotePreset> presets = LoadObsPresets(dependencies.ObsPresetsPath);
            return Results.Ok(presets.OrderByDescending(x => x.CreatedAt).Select(x => new { x.Name, x.CreatedAt, x.ProfileName, x.SceneCollectionName, x.CurrentScene }).ToArray());
        });

        app.MapPost("/api/v1/obs/presets/save", async (HttpRequest request) =>
        {
            if (!dependencies.Authorized(request))
            {
                return AgentApiResults.Unauthorized();
            }

            if (!dependencies.Permissions.AllowedCommands.Contains("obs.control", StringComparer.OrdinalIgnoreCase))
            {
                return AgentApiResults.Forbidden("obs.control");
            }

            ObsPresetRequest? payload = await JsonSerializer.DeserializeAsync<ObsPresetRequest>(request.Body);
            if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
            {
                return AgentApiResults.BadRequest("name fehlt");
            }

            try
            {
                await using IObsWebSocketClient obs = await dependencies.ConnectAsync();
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
                List<ObsRemotePreset> presets = LoadObsPresets(dependencies.ObsPresetsPath);
                presets.RemoveAll(x => string.Equals(x.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
                presets.Add(preset);
                SaveObsPresets(dependencies.ObsPresetsPath, presets);
                return Results.Ok(new { accepted = true, preset.Name, preset.CreatedAt });
            }
            catch (Exception ex) { return AgentApiResults.InternalError(ex); }
        });

        app.MapPost("/api/v1/obs/presets/apply", async (HttpRequest request) =>
        {
            if (!dependencies.Authorized(request))
            {
                return AgentApiResults.Unauthorized();
            }

            if (!dependencies.Permissions.AllowedCommands.Contains("obs.control", StringComparer.OrdinalIgnoreCase))
            {
                return AgentApiResults.Forbidden("obs.control");
            }

            ObsPresetRequest? payload = await JsonSerializer.DeserializeAsync<ObsPresetRequest>(request.Body);
            if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
            {
                return AgentApiResults.BadRequest("name fehlt");
            }

            ObsRemotePreset? preset = LoadObsPresets(dependencies.ObsPresetsPath).FirstOrDefault(x => string.Equals(x.Name, payload.Name, StringComparison.OrdinalIgnoreCase));
            if (preset is null)
            {
                return AgentApiResults.NotFound("Preset nicht gefunden");
            }

            try
            {
                await using (IObsWebSocketClient obs = await dependencies.ConnectAsync())
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
                await using (IObsWebSocketClient obs = await dependencies.ConnectAsync())
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
            catch (Exception ex) { return AgentApiResults.InternalError(ex); }
        });

        app.MapPost("/api/v1/obs/presets/delete", async (HttpRequest request) =>
        {
            if (!dependencies.Authorized(request))
            {
                return AgentApiResults.Unauthorized();
            }

            if (!dependencies.Permissions.AllowedCommands.Contains("obs.control", StringComparer.OrdinalIgnoreCase))
            {
                return AgentApiResults.Forbidden("obs.control");
            }

            ObsPresetRequest? payload = await JsonSerializer.DeserializeAsync<ObsPresetRequest>(request.Body);
            if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
            {
                return AgentApiResults.BadRequest("name fehlt");
            }

            List<ObsRemotePreset> presets = LoadObsPresets(dependencies.ObsPresetsPath);
            int removed = presets.RemoveAll(x => string.Equals(x.Name, payload.Name, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                return AgentApiResults.NotFound("Preset nicht gefunden");
            }

            SaveObsPresets(dependencies.ObsPresetsPath, presets);
            return Results.Ok(new { accepted = true, payload.Name });
        });

        app.MapGet("/api/v1/obs/output", async (HttpRequest request) =>
        {
            if (!dependencies.Authorized(request))
            {
                return AgentApiResults.Unauthorized();
            }

            if (!dependencies.Permissions.AllowedCommands.Contains("obs.control", StringComparer.OrdinalIgnoreCase))
            {
                return AgentApiResults.Forbidden("obs.control");
            }

            try
            {
                await using IObsWebSocketClient obs = await dependencies.ConnectAsync();
                ObsStreamStatus stream = await obs.GetStreamStatusAsync();
                ObsOutputStatus record = await obs.GetRecordStatusAsync();
                IReadOnlyList<ObsTransitionInfo> transitions = await obs.GetSceneTransitionListAsync();
                return Results.Ok(new { streamActive = stream.OutputActive, streamReconnecting = stream.OutputReconnecting, recordActive = record.Active, recordPaused = record.Paused, transitions = transitions.Select(x => x.Name).ToArray() });
            }
            catch (Exception ex) { return AgentApiResults.InternalError(ex); }
        });

        app.MapPost("/api/v1/obs/output", async (HttpRequest request) =>
        {
            if (!dependencies.Authorized(request))
            {
                return AgentApiResults.Unauthorized();
            }

            if (!dependencies.Permissions.AllowedCommands.Contains("obs.control", StringComparer.OrdinalIgnoreCase))
            {
                return AgentApiResults.Forbidden("obs.control");
            }

            ObsOutputRequest? payload = await JsonSerializer.DeserializeAsync<ObsOutputRequest>(request.Body);
            if (payload is null || string.IsNullOrWhiteSpace(payload.Action))
            {
                return AgentApiResults.BadRequest("action fehlt");
            }

            try
            {
                await using IObsWebSocketClient obs = await dependencies.ConnectAsync();
                switch (payload.Action.Trim().ToLowerInvariant())
                {
                    case "stream.start": await obs.StartStreamAsync(); break;
                    case "stream.stop": await obs.StopStreamAsync(); break;
                    case "record.start": await obs.StartRecordAsync(); break;
                    case "record.stop": await obs.StopRecordAsync(); break;
                    case "record.pause": await obs.PauseRecordAsync(); break;
                    case "record.resume": await obs.ResumeRecordAsync(); break;
                    default: return AgentApiResults.BadRequest("unbekannte OBS-Ausgabeaktion");
                }
                return Results.Ok(new { accepted = true, action = payload.Action });
            }
            catch (Exception ex) { return AgentApiResults.InternalError(ex); }
        });

        app.MapPost("/api/v1/obs/transition", async (HttpRequest request) =>
        {
            if (!dependencies.Authorized(request))
            {
                return AgentApiResults.Unauthorized();
            }

            if (!dependencies.Permissions.AllowedCommands.Contains("obs.control", StringComparer.OrdinalIgnoreCase))
            {
                return AgentApiResults.Forbidden("obs.control");
            }

            ObsTransitionRequest? payload = await JsonSerializer.DeserializeAsync<ObsTransitionRequest>(request.Body);
            if (payload is null || string.IsNullOrWhiteSpace(payload.TransitionName))
            {
                return AgentApiResults.BadRequest("transitionName fehlt");
            }

            try
            {
                await using IObsWebSocketClient obs = await dependencies.ConnectAsync();
                await obs.SetCurrentSceneTransitionAsync(payload.TransitionName);
                if (payload.DurationMilliseconds is > 0 and <= 20000)
                {
                    await obs.SetCurrentSceneTransitionDurationAsync(payload.DurationMilliseconds);
                }

                return Results.Ok(new { accepted = true, transitionName = payload.TransitionName, durationMilliseconds = payload.DurationMilliseconds });
            }
            catch (Exception ex) { return AgentApiResults.InternalError(ex); }
        });

        app.MapGet("/api/v1/obs/preview", async (HttpRequest request) =>
        {
            if (!dependencies.Authorized(request))
            {
                return AgentApiResults.Unauthorized();
            }

            if (!dependencies.Permissions.AllowedCommands.Contains("obs.control", StringComparer.OrdinalIgnoreCase))
            {
                return AgentApiResults.Forbidden("obs.control");
            }

            try
            {
                await using IObsWebSocketClient obs = await dependencies.ConnectAsync();
                string scene = await obs.GetCurrentProgramSceneAsync();
                byte[] image = await obs.GetSourceScreenshotAsync(scene, 640, 360);
                return Results.File(image, "image/png");
            }
            catch (Exception ex) { return AgentApiResults.InternalError(ex); }
        });
    }

    private static async Task<IResult> WithObsControl(
        HttpRequest request,
        ObsEndpointDependencies dependencies,
        Func<IObsWebSocketClient, Task<IResult>> action)
    {
        if (!dependencies.Authorized(request))
        {
            return AgentApiResults.Unauthorized();
        }

        if (!dependencies.Permissions.AllowedCommands.Contains(
                "obs.control",
                StringComparer.OrdinalIgnoreCase))
        {
            return AgentApiResults.Forbidden("obs.control");
        }

        try
        {
            await using IObsWebSocketClient obs =
                await dependencies.ConnectAsync();
            return await action(obs);
        }
        catch (Exception ex)
        {
            return AgentApiResults.InternalError(ex);
        }
    }
}
