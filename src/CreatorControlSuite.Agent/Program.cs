using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CreatorControlSuite.Agent.Security;
using CreatorControlSuite.Core.Security;
using CreatorControlSuite.Core.Updates;
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
app.MapOperationsEndpoints(new OperationsEndpointDependencies(
    Authorized,
    permissions,
    () => agentSettings,
    async updated =>
    {
        agentSettings = updated;
        await agentSettingsStore.SaveAsync(updated);
    },
    commandHistory,
    startedAt,
    agentVersion,
    certificateFingerprint,
    agentLogPath));

app.MapSecurityEndpoints(new SecurityEndpointDependencies(
    Authenticate,
    credentialStore,
    credentials,
    () => pairingSession,
    (newCode, newSession) =>
    {
        pairingCode = newCode;
        pairingSession = newSession;
    },
    permissions,
    agentPort,
    certificateFingerprint,
    message => AppendAgentLog(agentLogPath, message)));

app.MapObsEndpoints(new ObsEndpointDependencies(
    Authorized,
    permissions,
    async () => await ConnectObsAsync(agentSettings),
    obsPresetsPath));

app.MapUpdateEndpoints(new UpdateEndpointDependencies(
    Authorized,
    permissions,
    () => agentSettings,
    releaseSignatureVerifier,
    agentVersion,
    dataDirectory,
    updateStatePath,
    maintenancePath,
    updateHistoryPath,
    message => AppendAgentLog(agentLogPath, message)));

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
