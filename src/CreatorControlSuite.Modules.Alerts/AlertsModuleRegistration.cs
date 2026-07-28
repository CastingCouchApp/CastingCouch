using CreatorControlSuite.Core.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace CreatorControlSuite.Modules.Alerts;

public sealed class AlertsModuleRegistration : IModuleRegistration
{
    public string ModuleId => "alerts";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<AlertDefinitionProvider>();
        services.AddSingleton<ObsAlertRenderer>();
        services.AddSingleton<IAlertRenderer>(
            provider => provider.GetRequiredService<ObsAlertRenderer>());
        services.AddSingleton<IAlertEngine, AlertEngine>();
        services.AddSingleton<AlertsModule>();
        services.AddStreamingModuleBinding<AlertsModule>();
    }
}
