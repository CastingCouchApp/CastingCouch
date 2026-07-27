using CreatorControlSuite.Core.Modules;

namespace CreatorControlSuite.Core.Diagnostics;

public sealed class DiagnosticService(IEnumerable<IStreamingModule> modules)
{
    private readonly IReadOnlyList<IStreamingModule> _modules = [.. modules];

    public async Task<IReadOnlyList<ModuleStatus>> RunAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<ModuleStatus>();

        foreach (IStreamingModule module in _modules)
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
