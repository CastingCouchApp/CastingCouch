using CreatorControlSuite.Core.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace CreatorControlSuite.Modules.Twitch;

public sealed class TwitchModuleRegistration : IModuleRegistration
{
    public string ModuleId => "twitch";

    public void Register(IServiceCollection services)
    {
        services.AddHttpClient<ITwitchOAuthClient, TwitchOAuthClient>();
        services.AddHttpClient<ITwitchApiClient, TwitchApiClient>();
        services.AddHttpClient<IChatEmoteCatalog, ChatEmoteCatalog>();
        services.AddSingleton<IChatBadgeCatalog, ChatBadgeCatalog>();
        services.AddSingleton<ITwitchEventSubClient, TwitchEventSubClient>();
        services.AddSingleton<TwitchTokenRepository>();
        services.AddSingleton<TwitchModule>();
        services.AddStreamingModuleBinding<TwitchModule>();
    }
}
