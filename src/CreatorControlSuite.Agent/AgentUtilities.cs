using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using CreatorControlSuite.Agent.Security;
using CreatorControlSuite.Modules.OBS;
using CreatorControlSuite.Modules.OBS.Models;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Https;

internal static class AgentUtilities
{
    internal static void ProcessLastUpdateResult(
        string resultPath,
        string statePath)
    {
        if (!File.Exists(resultPath))
        {
            return;
        }

        string result = File.ReadAllText(resultPath).Trim();
        AgentUpdateState previous = LoadUpdateState(statePath);
        string message = result switch
        {
            "automatic-rollback" =>
                "Health-Check fehlgeschlagen; automatisches Rollback wurde ausgeführt.",
            "healthy" => "Update erfolgreich; Health-Check bestanden.",
            _ => "Update wurde angewendet."
        };
        SaveUpdateState(
            statePath,
            previous with
            {
                Status = result == "automatic-rollback"
                    ? "rolled-back"
                    : "healthy",
                MaintenanceMode = false,
                Message = message
            });
        File.Delete(resultPath);
    }

    internal static void ConfigureAgentBuilder(
        WebApplicationBuilder builder,
        int port,
        X509Certificate2 certificate)
    {
        builder.Services.AddProblemDetails();
        builder.Services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter("pairing", limiter =>
            {
                limiter.PermitLimit = 10;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
                limiter.AutoReplenishment = true;
            });
            options.RejectionStatusCode =
                StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
                await AgentApiResults.TooManyRequests(
                        "Das Anfrage-Limit wurde überschritten.")
                    .ExecuteAsync(context.HttpContext);
        });
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = 140L * 1024 * 1024;
            options.ListenAnyIP(
                port,
                listen => listen.UseHttps(new HttpsConnectionAdapterOptions
                {
                    ServerCertificate = certificate
                }));
        });
    }

    internal static AsyncLocal<string?> ConfigureAgentPipeline(
        WebApplication app)
    {
        var correlationIdSlot = new AsyncLocal<string?>();
        app.Use(async (context, next) =>
        {
            string requested =
                context.Request.Headers["X-Correlation-ID"].ToString();
            string correlationId =
                Guid.TryParse(requested, out Guid parsed)
                    ? parsed.ToString("N")
                    : Guid.NewGuid().ToString("N");
            correlationIdSlot.Value = correlationId;
            context.TraceIdentifier = correlationId;
            context.Response.Headers["X-Correlation-ID"] = correlationId;
            try
            {
                await next();
            }
            finally
            {
                correlationIdSlot.Value = null;
            }
        });
        app.Use((context, next) =>
            AgentApiExceptionHandling.HandleAsync(context, next));
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(
                    "/api",
                    out PathString rest) &&
                !rest.StartsWithSegments("/v1"))
            {
                context.Request.Path = "/api/v1" + rest;
                context.Response.Headers["Deprecation"] = "true";
            }

            await next();
        });
        app.Use((context, next) =>
            AgentRequestLimits.EnforceAsync(context, next));
        app.UseRateLimiter();
        return correlationIdSlot;
    }

    internal static async Task RunDiscoveryAsync(
        int agentPort,
        string agentVersion)
    {
        using var udp = new System.Net.Sockets.UdpClient(47632);
        while (true)
        {
            try
            {
                System.Net.Sockets.UdpReceiveResult received =
                    await udp.ReceiveAsync();
                if (Encoding.UTF8.GetString(received.Buffer) !=
                    "CCS_DISCOVER_V1")
                {
                    continue;
                }

                string mac = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(item =>
                        item.OperationalStatus == OperationalStatus.Up &&
                        item.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Select(item => item.GetPhysicalAddress().ToString())
                    .FirstOrDefault(value => value.Length == 12) ?? "";
                byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    machineName = Environment.MachineName,
                    host = Environment.MachineName,
                    port = agentPort,
                    version = agentVersion,
                    macAddress = mac
                });
                await udp.SendAsync(
                    payload,
                    payload.Length,
                    received.RemoteEndPoint);
            }
            catch (Exception ex)
            {
                Console.WriteLine("LAN-Erkennung: " + ex.Message);
                await Task.Delay(1000);
            }
        }
    }

    internal static async Task<ObsWebSocketClient> ConnectObsAsync(
        AgentSettings settings)
    {
        var client = new ObsWebSocketClient();
        await client.ConnectAsync(new ObsConnectionOptions(
            settings.ObsWebSocketHost,
            settings.ObsWebSocketPort,
            settings.ObsWebSocketPassword,
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(6)));
        return client;
    }

    internal static void CopyDirectory(
        string source,
        string destination,
        Func<string, bool>? include = null)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(
                     source,
                     "*",
                     SearchOption.AllDirectories))
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

    internal static bool IsCompatibleVersion(
        string currentVersion,
        string minimumVersion)
    {
        if (string.IsNullOrWhiteSpace(minimumVersion))
        {
            return true;
        }

        static int AlphaNumber(string value)
        {
            const string Marker = "alpha";
            int index = value.IndexOf(
                Marker,
                StringComparison.OrdinalIgnoreCase);
            return index >= 0 &&
                   int.TryParse(value[(index + Marker.Length)..], out int number)
                ? number
                : 0;
        }

        return AlphaNumber(currentVersion) >= AlphaNumber(minimumVersion);
    }

    internal static List<AgentUpdateHistoryEntry> LoadUpdateHistory(
        string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<AgentUpdateHistoryEntry>>(
                File.ReadAllText(path)) ?? [];
        }
        catch
        {
            return [];
        }
    }

    internal static void AppendUpdateHistory(
        string path,
        AgentUpdateHistoryEntry entry)
    {
        List<AgentUpdateHistoryEntry> history = LoadUpdateHistory(path);
        history.Add(entry);
        if (history.Count > 250)
        {
            history =
            [
                .. history.OrderByDescending(item => item.At).Take(250)
            ];
        }

        WriteAtomically(path, history);
    }

    internal static AgentUpdateState LoadUpdateState(string path)
    {
        if (!File.Exists(path))
        {
            return AgentUpdateState.Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<AgentUpdateState>(
                       File.ReadAllText(path)) ??
                   AgentUpdateState.Empty;
        }
        catch
        {
            return AgentUpdateState.Empty with
            {
                Status = "error",
                Message = "Update-Statusdatei konnte nicht gelesen werden."
            };
        }
    }

    internal static void SaveUpdateState(
        string path,
        AgentUpdateState state) =>
        WriteAtomically(path, state);

    internal static void StartConfigured(
        string? configuredPath,
        string fallback)
    {
        string target =
            !string.IsNullOrWhiteSpace(configuredPath) &&
            File.Exists(configuredPath)
                ? configuredPath
                : fallback;
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    internal static string NewPairingCode() =>
        Random.Shared.Next(100000, 1000000)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal static PairingSession NewPairingSession(string code) =>
        new(
            code,
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(5),
            maximumFailedAttempts: 5);

    internal static List<ObsRemotePreset> LoadObsPresets(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ObsRemotePreset>>(
                File.ReadAllText(path)) ?? [];
        }
        catch
        {
            return [];
        }
    }

    internal static void SaveObsPresets(
        string path,
        List<ObsRemotePreset> presets) =>
        WriteAtomically(path, presets);

    internal static AgentPermissions LoadPermissions(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                return JsonSerializer.Deserialize<AgentPermissions>(
                           File.ReadAllText(path)) ??
                       AgentPermissions.Default;
            }
            catch
            {
                // Replace malformed legacy permissions with secure defaults.
            }
        }

        AgentPermissions defaults = AgentPermissions.Default;
        WriteAtomically(path, defaults);
        return defaults;
    }

    private static void WriteAtomically<T>(string path, T value)
    {
        string tempPath = path + ".tmp";
        File.WriteAllText(
            tempPath,
            JsonSerializer.Serialize(
                value,
                new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tempPath, path, true);
    }
}
