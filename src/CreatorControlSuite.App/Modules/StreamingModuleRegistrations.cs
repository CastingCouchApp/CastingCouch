using CreatorControlSuite.Core.Modules;
using CreatorControlSuite.Core.Music;
using CreatorControlSuite.Modules.Alerts;
using CreatorControlSuite.Modules.OBS;
using CreatorControlSuite.Modules.Overlay;
using CreatorControlSuite.Modules.Spotify;
using CreatorControlSuite.Modules.StreamDeck;
using CreatorControlSuite.Modules.Twitch;
using CreatorControlSuite.Modules.Workflow;
using CreatorControlSuite.Modules.YouTubeMusic;
using Microsoft.Extensions.DependencyInjection;

namespace CreatorControlSuite.App.Modules;

public static class StreamingModuleRegistrations
{
    public static IServiceCollection AddStreamingModules(this IServiceCollection services)
    {
        services.AddModuleRegistration<ObsModuleRegistration>();
        services.AddModuleRegistration<TwitchModuleRegistration>();
        services.AddModuleRegistration<SpotifyModuleRegistration>();
        services.AddModuleRegistration<YouTubeMusicModuleRegistration>();
        services.AddModuleRegistration<AlertsModuleRegistration>();
        services.AddModuleRegistration<OverlayModuleRegistration>();
        services.AddModuleRegistration<WorkflowModuleRegistration>();
        services.AddModuleRegistration<StreamDeckModuleRegistration>();

        services.AddSingleton<IMusicPlayerRouter, MusicPlayerRouter>();
        return services;
    }

}
