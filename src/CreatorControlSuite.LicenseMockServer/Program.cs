using CreatorControlSuite.Core.Licensing;
WebApplicationBuilder builder = WebApplication.CreateBuilder(args); builder.Services.AddSingleton<LocalLicenseServerMock>(); WebApplication app = builder.Build();
app.MapPost("/api/v1/licenses/activate", async (LicenseServerActivationRequest r, LocalLicenseServerMock s, CancellationToken ct) => await s.ActivateAsync(r, ct));
app.MapGet("/api/v1/licenses/status/{activationId}", async (string activationId, string installationId, LocalLicenseServerMock s, CancellationToken ct) => await s.CheckStatusAsync(activationId, installationId, ct));
app.MapPost("/api/v1/licenses/deactivate", async (LicenseServerDeactivationRequest r, LocalLicenseServerMock s, CancellationToken ct) => { await s.DeactivateAsync(r, ct); return Results.Ok(); });
app.MapGet("/health", () => Results.Ok(new { service = "CreatorControlSuite.LicenseMockServer", status = "ok" })); app.Run();
