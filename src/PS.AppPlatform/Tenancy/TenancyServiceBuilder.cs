using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
                services.Configure<ManagementDirectoryOptions>(configuration.GetSection(ManagementDirectoryOptions.SectionName));
                // Validate Url and Audience at startup
                var mgmtOpts = new ManagementDirectoryOptions();
                configuration.GetSection(ManagementDirectoryOptions.SectionName).Bind(mgmtOpts);
                if (string.IsNullOrWhiteSpace(mgmtOpts.Url) || string.IsNullOrWhiteSpace(mgmtOpts.Audience))
                    throw new InvalidOperationException("tenancy:management:url and tenancy:management:audience are required when tenancy:directory is 'management'.");
                services.AddHttpClient("ps-registry");
                services.AddSingleton<ManagementApiTenantDirectory>();
                services.AddSingleton<ITenantDirectory>(sp =>
                    new CachingTenantDirectory(
                        sp.GetRequiredService<IServiceScopeFactory>(),
                        sp.GetRequiredService<IOptions<TenancyOptions>>(),
                        sp.GetRequiredService<ILogger<CachingTenantDirectory>>(),
                        typeof(ManagementApiTenantDirectory)));
                break;
            case "local":
                // The app must register its own ITenantDirectory implementation (e.g. LocalRegistryTenantDirectory).
                // We register a sentinel so that forgetting to do so produces a clear startup error instead of a
                // cryptic "service not registered" DI exception.
                services.TryAdd(ServiceDescriptor.Scoped<ITenantDirectory>(_ =>
                    throw new InvalidOperationException(
                        "tenancy:directory is 'local' but no ITenantDirectory was registered. " +
                        "The app must call services.AddScoped<ITenantDirectory, LocalRegistryTenantDirectory>() " +
                        "(or another implementation) after AddPlatformTenancy.")));
                break;
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
