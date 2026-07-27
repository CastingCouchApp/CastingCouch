using CreatorControlSuite.Core.Modules;
using CreatorControlSuite.Modules.Workflow.Models;

namespace CreatorControlSuite.Modules.Workflow;

public sealed class WorkflowModule(IStreamWorkflowService service) : IConnectableModule
{
    public string Id => "workflow";
    public string DisplayName => "Workflow";

    public IStreamWorkflowService Service { get; } = service;

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task<ModuleStatus> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        WorkflowState state = Service.State;

        return Task.FromResult(
            new ModuleStatus(
                Id,
                DisplayName,
                state.Phase == StreamPhase.Error
                    ? ModuleHealth.Error
                    : state.Phase == StreamPhase.Idle
                        ? ModuleHealth.Ready
                        : ModuleHealth.Connected,
                $"{state.Phase} · {state.Detail}",
                DateTimeOffset.Now));
    }
}
