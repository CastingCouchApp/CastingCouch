using CreatorControlSuite.Core.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace CreatorControlSuite.Modules.OBS;

public sealed class ObsModuleRegistration : IModuleRegistration
{
    public string ModuleId => "obs";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IObsWebSocketClient, ObsWebSocketClient>();
        services.AddSingleton<OBSModule>();
        services.AddStreamingModuleBinding<OBSModule>();
    }
}
