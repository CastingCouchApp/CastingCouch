using CreatorControlSuite.Core.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace CreatorControlSuite.Modules.StreamDeck;

public sealed class StreamDeckModuleRegistration : IModuleRegistration
{
    public string ModuleId => "streamdeck";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<StreamDeckModule>();
        services.AddStreamingModuleBinding<StreamDeckModule>();
    }
}
