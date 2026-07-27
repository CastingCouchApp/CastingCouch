using CreatorControlSuite.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CreatorControlSuite.Core.Modules;

/// <summary>
/// Self-registration contract for streaming modules.
/// App composition root calls <see cref="Register"/> once per module.
/// </summary>
public interface IModuleRegistration
{
    string ModuleId { get; }

    void Register(IServiceCollection services);
}

/// <summary>
/// Optional settings contribution for module-owned defaults / section hooks.
/// </summary>
public interface IModuleSettingsContributor
{
    string ModuleId { get; }

    void ApplyDefaults(AppSettings settings);
}

public static class ModuleRegistrationExtensions
{
    public static IServiceCollection AddModuleRegistration<TRegistration>(
        this IServiceCollection services)
        where TRegistration : class, IModuleRegistration, new()
    {
        var registration = new TRegistration();
        registration.Register(services);
        services.AddSingleton<IModuleRegistration>(registration);

        if (registration is IModuleSettingsContributor contributor)
        {
            services.AddSingleton(contributor);
        }

        return services;
    }

    public static IServiceCollection AddStreamingModuleBinding<TModule>(
        this IServiceCollection services)
        where TModule : class, IStreamingModule
    {
        services.AddSingleton<IStreamingModule>(
            provider => provider.GetRequiredService<TModule>());
        return services;
    }
}
