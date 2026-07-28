using System.Text.Json;
using CreatorControlSuite.Agent.Security;
using static AgentUtilities;

internal sealed record SecurityEndpointDependencies(
    Func<HttpRequest, AgentCredential?> Authenticate,
    AgentCredentialStore CredentialStore,
    List<AgentCredential> Credentials,
    Func<PairingSession> PairingSession,
    Action<string, PairingSession> RotatePairing,
    AgentPermissions Permissions,
    int AgentPort,
    string CertificateFingerprint,
    Action<string> Log);

internal static class SecurityEndpointMappings
{
    internal static void MapSecurityEndpoints(
        this WebApplication app,
        SecurityEndpointDependencies dependencies)
    {
        app.MapPost("/api/v1/pair", async (HttpRequest request) =>
        {
            if (request.ContentLength is > 4096)
            {
                return AgentApiResults.PayloadTooLarge(
                    AgentRequestLimits.PairingBytes);
            }

            PairingRequest? payload = await JsonSerializer.DeserializeAsync<PairingRequest>(request.Body);
            PairingAttemptResult attempt = dependencies.PairingSession().TryConsume(
                payload?.Code,
                DateTimeOffset.UtcNow);
            if (attempt != PairingAttemptResult.Accepted)
            {
                dependencies.Log(
                    $"Pairing abgelehnt ({attempt}) von {request.HttpContext.Connection.RemoteIpAddress}.");
                return attempt is PairingAttemptResult.Locked
                    ? AgentApiResults.TooManyRequests(
                        "Pairing ist vorübergehend gesperrt.")
                    : AgentApiResults.Unauthorized();
            }

            AgentCredential credential = await dependencies.CredentialStore.AddAsync(
                payload?.DeviceName ?? "");
            dependencies.Credentials.Add(credential);
            string pairingCode = NewPairingCode();
            dependencies.RotatePairing(
                pairingCode,
                NewPairingSession(pairingCode));
            dependencies.Log(
                $"Gerät '{credential.DisplayName}' gekoppelt ({credential.DeviceId}).");
            Console.WriteLine($"Gerät gekoppelt. Neuer Pairing-Code: {pairingCode}");
            return Results.Ok(new
            {
                deviceId = credential.DeviceId,
                machineName = Environment.MachineName,
                agentKey = credential.ApiKey,
                port = dependencies.AgentPort,
                dependencies.CertificateFingerprint,
                transport = "HTTPS/TLS",
                allowedCommands = dependencies.Permissions.AllowedCommands.OrderBy(x => x).ToArray()
            });
        }).RequireRateLimiting("pairing");

        app.MapPost("/api/v1/credentials/rotate", async (HttpRequest request) =>
        {
            AgentCredential? current = dependencies.Authenticate(request);
            if (current is null)
            {
                return AgentApiResults.Unauthorized();
            }

            AgentCredential? rotated = await dependencies.CredentialStore.RotateAsync(current.DeviceId);
            if (rotated is null)
            {
                return AgentApiResults.NotFound(
                    "Das gekoppelte Gerät wurde nicht gefunden.");
            }

            int index = dependencies.Credentials.FindIndex(item => item.DeviceId == current.DeviceId);
            if (index >= 0)
            {
                dependencies.Credentials[index] = rotated;
            }

            dependencies.Log($"Agent-Schlüssel rotiert ({current.DeviceId}).");
            return Results.Ok(new
            {
                deviceId = rotated.DeviceId,
                agentKey = rotated.ApiKey
            });
        });

        app.MapPost("/api/v1/credentials/unpair", async (HttpRequest request) =>
        {
            AgentCredential? current = dependencies.Authenticate(request);
            if (current is null)
            {
                return AgentApiResults.Unauthorized();
            }

            await dependencies.CredentialStore.DeleteAsync(current.DeviceId);
            dependencies.Credentials.RemoveAll(item => item.DeviceId == current.DeviceId);
            dependencies.Log($"Gerät entkoppelt ({current.DeviceId}).");
            return Results.Ok(new { unpaired = true });
        });
    }
}
