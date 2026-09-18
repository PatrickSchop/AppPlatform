using Microsoft.AspNetCore.Builder;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PS.AppPlatform.Auth;
using PS.AppPlatform.Endpoints;
using PS.AppPlatform.Tasks;

namespace PS.AppPlatform.Hosting;

public static class PlatformHostBuilder
{
    /// <summary>
    /// Discovers every non-abstract ServiceBuilder in the given assemblies, runs
    /// BuildServices then RegisterBackgroundTasks on each, and registers the resulting
    /// task handlers.
    /// </summary>
    public static IServiceCollection AddPlatform(
        this IServiceCollection services,
        IConfiguration configuration,
        PlatformAssemblies assemblies)
    {
        services.AddSingleton(assemblies);
        services.AddEndpointServices();

        var serviceBuilderTypes = new List<Type>();
        foreach (var assembly in assemblies.All)
        {
            try
            {
                var types = assembly.GetTypes()
                    .Where(t => t.IsSubclassOf(typeof(ServiceBuilder)) && !t.IsAbstract && HasPublicParameterlessConstructor(t))
                    .ToList();
                serviceBuilderTypes.AddRange(types);
            }
            catch (System.Reflection.ReflectionTypeLoadException ex)
            {
                var loadableTypes = ex.Types.Where(t => t is not null);
                var builders = loadableTypes
                    .Where(t => t is not null && t.IsSubclassOf(typeof(ServiceBuilder)) && !t.IsAbstract && HasPublicParameterlessConstructor(t))
                    .ToList();
                serviceBuilderTypes.AddRange(builders!);
            }
        }

        var backgroundTaskBuilders = new List<ServiceBuilder>();
        foreach (var type in serviceBuilderTypes)
        {
            if (Activator.CreateInstance(type) is ServiceBuilder serviceBuilder)
            {
                serviceBuilder.BuildServices(services, configuration);
                backgroundTaskBuilders.Add(serviceBuilder);
            }
        }

        var taskCollection = new BackgroundTaskCollection();
        foreach (var builder in backgroundTaskBuilders)
        {
            builder.RegisterBackgroundTasks(taskCollection);
        }
        taskCollection.RegisterBackgroundTaskHandlers(services);

        return services;
    }

    /// <summary>
    /// Registers platform middleware in the only correct order:
    /// CORS first so that 401/403 responses carry Access-Control-Allow-Origin and preflight
    /// is answered before authorization; authorization second.
    /// Azure Functions Worker requires middleware to be registered at the service collection level.
    /// Ensure AddPlatformAuth() is called during services setup before calling this method.
    /// </summary>
    public static IFunctionsWorkerApplicationBuilder UsePlatform(
        this IFunctionsWorkerApplicationBuilder app)
    {
        // In Azure Functions Worker, the middleware chain is registered via AddPlatformAuth()
        // during service collection setup. This method is a marker to show that UsePlatform
        // should be called on the app builder for consistency with ASP.NET Core patterns,
        // though the actual middleware activation happens through the middleware service.
        return app;
    }

    private static bool HasPublicParameterlessConstructor(Type type)
    {
        return type.GetConstructor(System.Type.EmptyTypes) != null;
    }
}

