using CreatorControlSuite.Core.Modules;
using CreatorControlSuite.Core.Music;
using Microsoft.Extensions.DependencyInjection;

namespace CreatorControlSuite.Modules.Spotify;

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
        services.AddSingleton<IMusicPlayer>(
            provider => provider.GetRequiredService<SpotifyMusicPlayer>());
        services.AddStreamingModuleBinding<SpotifyModule>();
    }
}
