using CreatorControlSuite.Core.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace CreatorControlSuite.Modules.Workflow;

public sealed class WorkflowModuleRegistration : IModuleRegistration
{
    public string ModuleId => "workflow";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IStreamWorkflowService, StreamWorkflowService>();
        services.AddSingleton<WorkflowModule>();
        services.AddStreamingModuleBinding<WorkflowModule>();
    }
}
