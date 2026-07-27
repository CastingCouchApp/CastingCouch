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

    public sealed class SpotifyModuleRegistration : IModuleRegistration
    {
        public string ModuleId => "spotify";

        public void Register(IServiceCollection services)
        {
            services.AddHttpClient<ISpotifyOAuthClient, SpotifyOAuthClient>();
            services.AddHttpClient<ISpotifyApiClient, SpotifyApiClient>();
            services.AddSingleton<SpotifyTokenRepository>();
            services.AddSingleton<SpotifyModule>();
            services.AddSingleton<SpotifyMusicPlayer>();
            services.AddSingleton<IMusicPlayer>(provider => provider.GetRequiredService<SpotifyMusicPlayer>());
            services.AddStreamingModuleBinding<SpotifyModule>();
        }
    }

    public sealed class YouTubeMusicModuleRegistration : IModuleRegistration
    {
        public string ModuleId => "ytmusic";

        public void Register(IServiceCollection services)
        {
            services.AddSingleton<YouTubeMusicBridge>();
            services.AddSingleton<YouTubeMusicModule>();
            services.AddSingleton<IMusicPlayer>(provider => provider.GetRequiredService<YouTubeMusicModule>());
            services.AddStreamingModuleBinding<YouTubeMusicModule>();
        }
    }

    public sealed class AlertsModuleRegistration : IModuleRegistration
    {
        public string ModuleId => "alerts";

        public void Register(IServiceCollection services)
        {
            services.AddSingleton<AlertDefinitionProvider>();
            services.AddSingleton<ObsAlertRenderer>();
            services.AddSingleton<IAlertRenderer>(provider =>
                provider.GetRequiredService<ObsAlertRenderer>());
            services.AddSingleton<IAlertEngine, AlertEngine>();
            services.AddSingleton<AlertsModule>();
            services.AddStreamingModuleBinding<AlertsModule>();
        }
    }

    public sealed class OverlayModuleRegistration : IModuleRegistration
    {
        public string ModuleId => "overlay";

        public void Register(IServiceCollection services)
        {
            services.AddSingleton<OverlayRealtimeHub>();
            services.AddSingleton<IOverlayRealtimeHub>(sp => sp.GetRequiredService<OverlayRealtimeHub>());
            services.AddSingleton<IOverlayDataService, OverlayDataService>();
            services.AddSingleton<IOverlayWebServer, OverlayWebServer>();
            services.AddSingleton<OverlayModule>();
            services.AddStreamingModuleBinding<OverlayModule>();
        }
    }

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

    public sealed class StreamDeckModuleRegistration : IModuleRegistration
    {
        public string ModuleId => "streamdeck";

        public void Register(IServiceCollection services)
        {
            services.AddSingleton<StreamDeckModule>();
            services.AddStreamingModuleBinding<StreamDeckModule>();
        }
    }
}
