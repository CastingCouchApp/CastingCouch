using CreatorControlSuite.Core.Modules;
using CreatorControlSuite.Core.Music;
using Microsoft.Extensions.DependencyInjection;

namespace CreatorControlSuite.Modules.YouTubeMusic;

public sealed class YouTubeMusicModuleRegistration : IModuleRegistration
{
    public string ModuleId => "ytmusic";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<YouTubeMusicBridge>();
        services.AddSingleton<YouTubeMusicModule>();
        services.AddSingleton<IMusicPlayer>(
            provider => provider.GetRequiredService<YouTubeMusicModule>());
        services.AddStreamingModuleBinding<YouTubeMusicModule>();
    }
}
