using Microsoft.Extensions.DependencyInjection;

namespace Wisdi.AppPlatform.Endpoints;

public static class EndpointsServiceBuilder
{
    /// <summary>
    /// Registers all platform endpoint services.
    /// These are plain services with no [Function] attributes;
    /// the shim layer (Step 13) wraps them in Azure Functions.
    /// </summary>
    public static void AddEndpointServices(this IServiceCollection services)
    {
        services.AddScoped<IConfigurationEndpoints, ConfigurationEndpoints>();
        services.AddScoped<IHealthEndpoints, HealthEndpoints>();
        services.AddScoped<IBackgroundTaskEndpoints, BackgroundTaskEndpoints>();
        services.AddScoped<IDatabaseEndpoints, DatabaseEndpoints>();
    }
}
