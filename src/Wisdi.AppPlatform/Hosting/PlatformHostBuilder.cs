using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Wisdi.AppPlatform.Hosting;

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

        foreach (var type in serviceBuilderTypes)
        {
            if (Activator.CreateInstance(type) is ServiceBuilder serviceBuilder)
            {
                serviceBuilder.BuildServices(services, configuration);
            }
        }

        return services;
    }

    private static bool HasPublicParameterlessConstructor(Type type)
    {
        return type.GetConstructor(System.Type.EmptyTypes) != null;
    }
}
