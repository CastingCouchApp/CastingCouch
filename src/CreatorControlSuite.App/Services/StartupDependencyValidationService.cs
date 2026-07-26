using CreatorControlSuite.Core.Diagnostics;
using CreatorControlSuite.Core.Updates;
using Microsoft.Extensions.DependencyInjection;

namespace CreatorControlSuite.App.Services;

public sealed class StartupDependencyValidationService : IStartupDependencyValidationService
{
    private readonly IServiceProvider _services;
    public StartupDependencyValidationService(IServiceProvider services) => _services = services;

    public Task<IReadOnlyList<string>> ValidateAsync(CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        try { _services.GetRequiredService<IUpdateService>(); }
        catch (Exception ex) { failures.Add(typeof(IUpdateService).FullName + ": " + ex.Message); }
        return Task.FromResult<IReadOnlyList<string>>(failures);
    }
}
