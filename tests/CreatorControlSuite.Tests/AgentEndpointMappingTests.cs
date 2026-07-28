using System.Collections.Concurrent;
using System.Text;
using CreatorControlSuite.Agent.Security;
using CreatorControlSuite.Core.Security;
using CreatorControlSuite.Core.Updates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CreatorControlSuite.Tests;

public sealed class AgentEndpointMappingTests
{
    [Fact]
    public async Task ObsEndpoints_ExposeVersionedMethodAndPathContract()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        await using WebApplication app = builder.Build();
        app.MapObsEndpoints(new ObsEndpointDependencies(
            _ => true,
            AgentPermissions.Default,
            () => throw new InvalidOperationException(
                "Der Mapping-Test darf keine OBS-Verbindung öffnen."),
            "obs-presets.json"));

        string[] actual = Routes(app);

        string[] expected =
        [
            "GET /api/v1/obs/configuration",
            "GET /api/v1/obs/filters",
            "GET /api/v1/obs/output",
            "GET /api/v1/obs/presets",
            "GET /api/v1/obs/preview",
            "GET /api/v1/obs/state",
            "POST /api/v1/obs/configuration",
            "POST /api/v1/obs/filter",
            "POST /api/v1/obs/mute",
            "POST /api/v1/obs/output",
            "POST /api/v1/obs/presets/apply",
            "POST /api/v1/obs/presets/delete",
            "POST /api/v1/obs/presets/save",
            "POST /api/v1/obs/scene",
            "POST /api/v1/obs/scene-item",
            "POST /api/v1/obs/transform",
            "POST /api/v1/obs/transition",
            "POST /api/v1/obs/volume",
            "POST /api/v1/obs/volume-fade"
        ];

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task UpdateEndpoints_ExposeVersionedMethodAndPathContract()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        await using WebApplication app = builder.Build();
        app.MapUpdateEndpoints(new UpdateEndpointDependencies(
            _ => true,
            AgentPermissions.Default,
            () => new AgentSettings(),
            new RejectingUpdateSignatureVerifier(),
            "8.0.0-alpha102",
            "agent-data",
            "update-state.json",
            "maintenance.flag",
            "update-history.json",
            _ => { }));

        Assert.Equal(
            [
                "GET /api/v1/update/history",
                "GET /api/v1/update/status",
                "POST /api/v1/update/apply",
                "POST /api/v1/update/rollback",
                "POST /api/v1/update/stage",
                "POST /api/v1/update/validate"
            ],
            Routes(app));
    }

    [Fact]
    public async Task OperationsEndpoints_ExposeVersionedMethodAndPathContract()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        await using WebApplication app = builder.Build();
        app.MapOperationsEndpoints(new OperationsEndpointDependencies(
            _ => true,
            AgentPermissions.Default,
            () => new AgentSettings(),
            _ => Task.CompletedTask,
            new ConcurrentQueue<CommandHistoryEntry>(),
            DateTimeOffset.UtcNow,
            "8.0.0-alpha102",
            "FINGERPRINT",
            "agent.log"));

        Assert.Equal(
            [
                "GET /api/v1/history",
                "GET /api/v1/logs",
                "GET /api/v1/status",
                "POST /api/v1/command",
                "POST /api/v1/settings"
            ],
            Routes(app));
    }

    [Fact]
    public async Task SecurityEndpoints_ExposeVersionedMethodAndPathContract()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        await using WebApplication app = builder.Build();
        app.MapSecurityEndpoints(new SecurityEndpointDependencies(
            _ => null,
            new AgentCredentialStore(new MemorySecretStore()),
            [],
            () => AgentUtilities.NewPairingSession("123456"),
            (_, _) => { },
            AgentPermissions.Default,
            47631,
            "FINGERPRINT",
            _ => { }));

        Assert.Equal(
            [
                "POST /api/v1/credentials/rotate",
                "POST /api/v1/credentials/unpair",
                "POST /api/v1/pair"
            ],
            Routes(app));
    }

    [Fact]
    public async Task ProtectedEndpointGroups_RejectMissingCredential()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        await using WebApplication app = builder.Build();
        app.MapOperationsEndpoints(new OperationsEndpointDependencies(
            _ => false,
            AgentPermissions.Default,
            () => new AgentSettings(),
            _ => Task.CompletedTask,
            new ConcurrentQueue<CommandHistoryEntry>(),
            DateTimeOffset.UtcNow,
            "8.0.0-alpha102",
            "FINGERPRINT",
            "agent.log"));
        app.MapObsEndpoints(new ObsEndpointDependencies(
            _ => false,
            AgentPermissions.Default,
            () => throw new InvalidOperationException(
                "Unautorisierte Anfragen dürfen OBS nicht öffnen."),
            "obs-presets.json"));
        app.MapUpdateEndpoints(new UpdateEndpointDependencies(
            _ => false,
            AgentPermissions.Default,
            () => new AgentSettings(),
            new RejectingUpdateSignatureVerifier(),
            "8.0.0-alpha102",
            "agent-data",
            "update-state.json",
            "maintenance.flag",
            "update-history.json",
            _ => { }));

        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            await InvokeAsync(app, "GET", "/api/v1/status"));
        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            await InvokeAsync(app, "GET", "/api/v1/obs/state"));
        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            await InvokeAsync(app, "GET", "/api/v1/update/status"));
    }

    [Fact]
    public async Task SensitiveEndpointGroups_RequireExplicitPermission()
    {
        var noPermissions = new AgentPermissions([]);
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        await using WebApplication app = builder.Build();
        app.MapObsEndpoints(new ObsEndpointDependencies(
            _ => true,
            noPermissions,
            () => throw new InvalidOperationException(
                "Verbotene Anfragen dürfen OBS nicht öffnen."),
            "obs-presets.json"));
        app.MapUpdateEndpoints(new UpdateEndpointDependencies(
            _ => true,
            noPermissions,
            () => new AgentSettings(),
            new RejectingUpdateSignatureVerifier(),
            "8.0.0-alpha102",
            "agent-data",
            "update-state.json",
            "maintenance.flag",
            "update-history.json",
            _ => { }));

        Assert.Equal(
            StatusCodes.Status403Forbidden,
            await InvokeAsync(app, "GET", "/api/v1/obs/state"));
        Assert.Equal(
            StatusCodes.Status403Forbidden,
            await InvokeAsync(app, "POST", "/api/v1/update/stage"));
    }

    [Fact]
    public async Task CommandFailure_DoesNotExposeExceptionDetailsInHistory()
    {
        const string Secret = "obs-password=must-not-leak";
        var history = new ConcurrentQueue<CommandHistoryEntry>();
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddProblemDetails();
        await using WebApplication app = builder.Build();
        app.MapOperationsEndpoints(new OperationsEndpointDependencies(
            _ => true,
            new AgentPermissions(["obs.start"]),
            () => throw new InvalidOperationException(Secret),
            _ => Task.CompletedTask,
            history,
            DateTimeOffset.UtcNow,
            "8.0.0-alpha102",
            "FINGERPRINT",
            "agent.log"));

        int statusCode = await InvokeAsync(
            app,
            "POST",
            "/api/v1/command",
            """{"Command":"obs.start"}""");

        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            statusCode);
        CommandHistoryEntry entry = Assert.Single(history);
        Assert.Equal("error", entry.Result);
        Assert.DoesNotContain(Secret, entry.Result, StringComparison.Ordinal);
    }

    private static string[] Routes(WebApplication app) =>
    [
        .. ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint =>
            {
                HttpMethodMetadata methods =
                    endpoint.Metadata.GetRequiredMetadata<HttpMethodMetadata>();
                return $"{Assert.Single(methods.HttpMethods)} " +
                       endpoint.RoutePattern.RawText;
            })
            .OrderBy(value => value, StringComparer.Ordinal)
    ];

    private static async Task<int> InvokeAsync(
        WebApplication app,
        string method,
        string path,
        string? json = null)
    {
        RouteEndpoint endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate =>
                candidate.RoutePattern.RawText == path &&
                candidate.Metadata
                    .GetRequiredMetadata<HttpMethodMetadata>()
                    .HttpMethods
                    .Contains(method, StringComparer.Ordinal));
        var context = new DefaultHttpContext
        {
            RequestServices = app.Services
        };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Body = json is null
            ? new MemoryStream()
            : new MemoryStream(Encoding.UTF8.GetBytes(json));
        if (json is not null)
        {
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = context.Request.Body.Length;
        }
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);
        return context.Response.StatusCode;
    }

    private sealed class RejectingUpdateSignatureVerifier
        : IUpdateSignatureVerifier
    {
        public bool VerifyManifest(SignedUpdateManifest manifest) => false;

        public Task<bool> VerifyPackageAsync(
            string packagePath,
            SignedUpdateManifest manifest,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class MemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _values =
            new(StringComparer.Ordinal);

        public Task SaveAsync(
            string key,
            string value,
            CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> LoadAsync(
            string key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.GetValueOrDefault(key));

        public Task DeleteAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }
}
