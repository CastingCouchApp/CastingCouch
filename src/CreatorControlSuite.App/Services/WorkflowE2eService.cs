using System.Diagnostics;
using CreatorControlSuite.Core.Diagnostics;
using CreatorControlSuite.Core.Licensing;
using CreatorControlSuite.Modules.Workflow;
namespace CreatorControlSuite.App.Services;

public sealed class WorkflowE2eService(WorkflowModule workflow, IFeatureGate gate) : IWorkflowE2eService
{
    private readonly WorkflowModule _workflow = workflow; private readonly IFeatureGate _gate = gate;

    public async Task<WorkflowE2eReport> RunAsync(CancellationToken ct = default)
    {
        await _gate.RequireAsync(FeatureCatalog.Workflow, ct); DateTimeOffset started = DateTimeOffset.Now; var steps = new List<WorkflowE2eStepResult>();
        await Step("Vorbereiten", x => _workflow.Service.PrepareAsync(x), steps, ct);
        await Step("Live", x => _workflow.Service.GoLiveAsync(x), steps, ct);
        await Step("Pause", x => _workflow.Service.PauseAsync(x), steps, ct);
        await Step("Fortsetzen", x => _workflow.Service.ResumeAsync(x), steps, ct);
        await Step("Ende", x => _workflow.Service.EndAsync(x), steps, ct);
        return new(started, DateTimeOffset.Now, steps.All(x => x.Success), steps);
    }
    private static async Task Step(string name, Func<CancellationToken, Task> action, ICollection<WorkflowE2eStepResult> results, CancellationToken ct)
    { var sw = Stopwatch.StartNew(); try { await action(ct); results.Add(new(name, true, sw.Elapsed, "OK")); } catch (Exception ex) { results.Add(new(name, false, sw.Elapsed, ex.Message)); throw; } }
}
