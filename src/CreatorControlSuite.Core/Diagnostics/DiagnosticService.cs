using CreatorControlSuite.Core.Modules;

namespace CreatorControlSuite.Core.Diagnostics;

public sealed class DiagnosticService
{
    private readonly IReadOnlyList<IStreamingModule> _modules;

    public DiagnosticService(IEnumerable<IStreamingModule> modules)
    {
        _modules = modules.ToList();
    }

    public async Task<IReadOnlyList<ModuleStatus>> RunAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<ModuleStatus>();

        foreach (var module in _modules)
        {
            try
            {
                results.Add(await module.GetStatusAsync(cancellationToken));
            }
            catch (Exception exception)
            {
                results.Add(new ModuleStatus(
                    module.Id,
                    module.DisplayName,
                    ModuleHealth.Error,
                    exception.Message,
                    DateTimeOffset.Now));
            }
        }

        return results;
    }
}
