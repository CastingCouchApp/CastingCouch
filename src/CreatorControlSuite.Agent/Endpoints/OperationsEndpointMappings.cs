using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using CreatorControlSuite.Agent.Security;
using static AgentUtilities;

internal sealed record OperationsEndpointDependencies(
    Func<HttpRequest, bool> Authorized,
    AgentPermissions Permissions,
    Func<AgentSettings> Settings,
    Func<AgentSettings, Task> SaveSettings,
    ConcurrentQueue<CommandHistoryEntry> CommandHistory,
    DateTimeOffset StartedAt,
    string AgentVersion,
    string CertificateFingerprint,
    string AgentLogPath);

internal static class OperationsEndpointMappings
{
    internal static void MapOperationsEndpoints(
        this WebApplication app,
        OperationsEndpointDependencies dependencies)
    {
        bool Running(string name) => Process.GetProcessesByName(name).Length > 0;

        app.MapGet("/api/v1/status", (HttpRequest request) =>
        {
            if (!dependencies.Authorized(request))
            {
                return AgentApiResults.Unauthorized();
            }

            var current = Process.GetCurrentProcess();
            return Results.Ok(new
            {
                machineName = Environment.MachineName,
                cpuPercent = 0d,
                memoryMb = current.WorkingSet64 / 1024d / 1024d,
                uptimeMinutes = (DateTimeOffset.UtcNow - dependencies.StartedAt).TotalMinutes,
                obsRunning = Running("obs64"),
                spotifyRunning = Running("Spotify"),
                streamerBotRunning = Running("Streamer.bot") || Running("Streamer.bot-x64"),
                version = dependencies.AgentVersion,
                transport = "HTTPS/TLS",
                dependencies.CertificateFingerprint,
                allowedCommands = dependencies.Permissions.AllowedCommands.OrderBy(x => x).ToArray()
            });
        });

        app.MapPost("/api/v1/command", async (HttpRequest request) =>
        {
            if (!dependencies.Authorized(request))
            {
                return AgentApiResults.Unauthorized();
            }

            CommandRequest? payload = await JsonSerializer.DeserializeAsync<CommandRequest>(request.Body);
            if (payload is null || string.IsNullOrWhiteSpace(payload.Command))
            {
                return AgentApiResults.BadRequest("command fehlt");
            }

            string command = payload.Command.Trim().ToLowerInvariant();
            if (!dependencies.Permissions.AllowedCommands.Contains(command, StringComparer.OrdinalIgnoreCase))
            {
                return AgentApiResults.Forbidden(command);
            }

            try
            {
                switch (command)
                {
                    case "obs.start": StartConfigured(dependencies.Settings().ObsPath, "obs64.exe"); break;
                    case "obs.stop": foreach (Process p in Process.GetProcessesByName("obs64")) { p.CloseMainWindow(); } break;
                    case "spotify.playpause": Process.Start(new ProcessStartInfo("spotify:playpause") { UseShellExecute = true }); break;
                    case "streamerbot.start": StartConfigured(dependencies.Settings().StreamerBotPath, "Streamer.bot.exe"); break;
                    case "system.restart": Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5 /c \"Creator Control Suite Remote-Neustart\"") { UseShellExecute = false, CreateNoWindow = true }); break;
                    case "system.shutdown": Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 5 /c \"Creator Control Suite Remote-Herunterfahren\"") { UseShellExecute = false, CreateNoWindow = true }); break;
                    default: return AgentApiResults.BadRequest("unbekannter Befehl");
                }
                DateTimeOffset acceptedAt = DateTimeOffset.UtcNow;
                dependencies.CommandHistory.Enqueue(new CommandHistoryEntry(acceptedAt, command, "accepted"));
                while (dependencies.CommandHistory.Count > 100)
                {
                    dependencies.CommandHistory.TryDequeue(out _);
                }

                return Results.Ok(new { accepted = true, command, acceptedAt });
            }
            catch (Exception ex)
            {
                dependencies.CommandHistory.Enqueue(
                    new CommandHistoryEntry(
                        DateTimeOffset.UtcNow,
                        command,
                        "error"));
                return AgentApiResults.InternalError(ex);
            }
        });

        app.MapGet("/api/v1/logs", (HttpRequest request, int? lines) =>
        {
            if (!dependencies.Authorized(request))
            {
                return AgentApiResults.Unauthorized();
            }

            int take = Math.Clamp(lines ?? 200, 20, 2000);
            if (!File.Exists(dependencies.AgentLogPath))
            {
                return Results.Ok(Array.Empty<string>());
            }

            return Results.Ok(File.ReadLines(dependencies.AgentLogPath).TakeLast(take).ToArray());
        });

        app.MapPost("/api/v1/settings", async (HttpRequest request) =>
        {
            if (!dependencies.Authorized(request))
            {
                return AgentApiResults.Unauthorized();
            }

            AgentSettings? updated = await JsonSerializer.DeserializeAsync<AgentSettings>(request.Body);
            if (updated is null || updated.ObsWebSocketPort is <= 0 or > 65535)
            {
                return AgentApiResults.BadRequest("Ungültige Einstellungen");
            }

            await dependencies.SaveSettings(updated);
            return Results.Ok(new { saved = true });
        });

        app.MapGet("/api/v1/history", (HttpRequest request) =>
        {
            if (!dependencies.Authorized(request))
            {
                return AgentApiResults.Unauthorized();
            }

            return Results.Ok(dependencies.CommandHistory.Reverse().Take(50).ToArray());
        });
    }
}
