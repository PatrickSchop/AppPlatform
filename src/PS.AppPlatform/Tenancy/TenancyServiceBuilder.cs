using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PS.AppPlatform.Endpoints;
using PS.AppPlatform.Hosting;

namespace PS.AppPlatform.Tenancy;

public static class TenancyServiceBuilder
{
    public static IServiceCollection AddPlatformTenancy(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(sd => sd.ServiceType == typeof(AppManifest)))
        {
            return services;
        }

        var options = new TenancyOptions();
        configuration.GetSection(TenancyOptions.SectionName).Bind(options);

        var platformAssemblies = services.BuildServiceProvider().GetRequiredService<PlatformAssemblies>();

        var manifest = DiscoverManifest(platformAssemblies);

        if (manifest == null)
        {
            if (options.Mode != TenancyMode.None)
            {
                throw new InvalidOperationException(
                    $"TenancyMode.{options.Mode} requires an AppManifest subclass but none was found.");
            }

            services.TryAdd(ServiceDescriptor.Singleton(typeof(TenancyMode), TenancyMode.None));
            return services;
        }

        manifest.Validate();

        if (options.Mode != manifest.Tenancy)
        {
            throw new InvalidOperationException(
                $"Configuration tenancy:mode is {options.Mode} but {manifest.GetType().Name} declares {manifest.Tenancy}.");
        }

        services.Configure<TenancyOptions>(configuration.GetSection(TenancyOptions.SectionName));
        services.AddSingleton(manifest);
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<TenantResolver>();
        services.AddScoped<ITenancyEndpoints, TenancyEndpoints>();
        services.AddMemoryCache();

        switch (options.Directory)
        {
            case "config":
                services.AddSingleton<ITenantDirectory, ConfigTenantDirectory>();
                break;
            case "management":
                throw new NotSupportedException("ManagementApiTenantDirectory arrives in MT-09.");
            case "local":
                throw new NotSupportedException("LocalRegistryTenantDirectory arrives in MT-08.");
            default:
                throw new InvalidOperationException($"Unknown tenancy directory: {options.Directory}");
        }

        services.TryAdd(ServiceDescriptor.Singleton(typeof(TenancyMode), manifest.Tenancy));

        return services;
    }

    private static AppManifest? DiscoverManifest(PlatformAssemblies platformAssemblies)
    {
        var manifestType = typeof(AppManifest);
        var manifests = new List<AppManifest>();

        foreach (var assembly in platformAssemblies.All)
        {
            var types = assembly.GetTypes()
                .Where(t => !t.IsAbstract && manifestType.IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null);

            foreach (var type in types)
            {
                if (Activator.CreateInstance(type) is AppManifest instance)
                {
                    manifests.Add(instance);
                }
            }
        }

        if (manifests.Count > 1)
        {
            var names = string.Join(", ", manifests.Select(m => m.GetType().Name));
            throw new InvalidOperationException($"Multiple AppManifest classes found: {names}");
        }

        return manifests.FirstOrDefault();
    }
}
