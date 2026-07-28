using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CreatorControlSuite.Tests;

public sealed class AgentApiHardeningTests
{
    [Fact]
    public async Task ErrorFactory_WritesStableProblemDetailsContract()
    {
        ProblemResponse response = await ExecuteAsync(
            AgentApiResults.Unauthorized());

        Assert.Equal(StatusCodes.Status401Unauthorized, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.ContentType);
        Assert.Equal("Authentifizierung erforderlich.", response.Title);
        Assert.Equal("agent.authentication_required", response.Code);
    }

    [Fact]
    public async Task InternalError_DoesNotExposeExceptionDetails()
    {
        const string Secret = "obs-password=super-secret";

        ProblemResponse response = await ExecuteAsync(
            AgentApiResults.InternalError(new InvalidOperationException(Secret)));

        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            response.StatusCode);
        Assert.DoesNotContain(Secret, response.RawBody, StringComparison.Ordinal);
        Assert.Equal("agent.internal_error", response.Code);
    }

    [Theory]
    [InlineData("/api/v1/pair", 4L * 1024)]
    [InlineData("/api/v1/update/stage", 140L * 1024 * 1024)]
    [InlineData("/api/v1/settings", 1L * 1024 * 1024)]
    [InlineData("/api/v1/obs/scene", 1L * 1024 * 1024)]
    public void RequestLimitPolicy_UsesEndpointSpecificLimits(
        string path,
        long expected)
    {
        Assert.Equal(expected, AgentRequestLimits.Resolve(path));
    }

    [Fact]
    public async Task RequestLimitPolicy_RejectsOversizedBodyBeforeEndpoint()
    {
        bool nextCalled = false;
        DefaultHttpContext context = CreateContext();
        context.Request.Path = "/api/v1/pair";
        context.Request.ContentLength = AgentRequestLimits.PairingBytes + 1;

        await AgentRequestLimits.EnforceAsync(
            context,
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

        Assert.False(nextCalled);
        Assert.Equal(
            StatusCodes.Status413PayloadTooLarge,
            context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using JsonDocument problem = await JsonDocument.ParseAsync(
            context.Response.Body);
        Assert.Equal(
            "agent.payload_too_large",
            problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ExceptionHandling_MapsMalformedJsonWithoutLeakingInput()
    {
        const string SensitiveInput = "password=do-not-return";
        DefaultHttpContext context = CreateContext();

        await AgentApiExceptionHandling.HandleAsync(
            context,
            _ => throw new JsonException(SensitiveInput));

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        string raw = await new StreamReader(context.Response.Body)
            .ReadToEndAsync();
        using JsonDocument problem = JsonDocument.Parse(raw);
        Assert.Equal(
            "agent.invalid_request",
            problem.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(
            SensitiveInput,
            raw,
            StringComparison.Ordinal);
    }

    private static async Task<ProblemResponse> ExecuteAsync(IResult result)
    {
        DefaultHttpContext context = CreateContext();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        string raw = await new StreamReader(context.Response.Body)
            .ReadToEndAsync();
        using JsonDocument problem = JsonDocument.Parse(raw);
        return new ProblemResponse(
            context.Response.StatusCode,
            context.Response.ContentType,
            problem.RootElement.GetProperty("title").GetString(),
            problem.RootElement.GetProperty("code").GetString(),
            raw);
    }

    private static DefaultHttpContext CreateContext()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Services.AddProblemDetails();
        WebApplication app = builder.Build();
        return new DefaultHttpContext
        {
            RequestServices = app.Services,
            Response = { Body = new MemoryStream() }
        };
    }

    private sealed record ProblemResponse(
        int StatusCode,
        string? ContentType,
        string? Title,
        string? Code,
        string RawBody);
}
