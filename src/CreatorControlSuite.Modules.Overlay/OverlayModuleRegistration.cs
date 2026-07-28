using CreatorControlSuite.Core.Modules;
using CreatorControlSuite.Modules.Overlay.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace CreatorControlSuite.Modules.Overlay;

public sealed class OverlayModuleRegistration : IModuleRegistration
{
    public string ModuleId => "overlay";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<OverlayRealtimeHub>();
        services.AddSingleton<IOverlayRealtimeHub>(
            provider => provider.GetRequiredService<OverlayRealtimeHub>());
        services.AddSingleton<IOverlayDataService, OverlayDataService>();
        services.AddSingleton<IOverlayLayoutStore, OverlayLayoutStore>();
        services.AddSingleton<IOverlayExtensionStore, OverlayExtensionStore>();
        services.AddSingleton<IOverlayWebServer, OverlayWebServer>();
        services.AddSingleton<OverlayModule>();
        services.AddStreamingModuleBinding<OverlayModule>();
    }
}
