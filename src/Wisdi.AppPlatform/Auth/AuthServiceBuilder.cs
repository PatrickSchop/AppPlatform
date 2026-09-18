using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PS.AppPlatform.Auth;

public static class AuthServiceBuilder
{
    /// <summary>
    /// Registers authentication and authorization middleware for the platform.
    /// Must be called during service collection setup before building the service provider.
    /// </summary>
    public static IServiceCollection AddPlatformAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddPlatformAuthentication(configuration);
        services.AddSingleton<CorsMiddleware>();
        services.AddSingleton<FunctionAuthorizationMiddleware>();
        services.AddSingleton<IFunctionsWorkerMiddleware, PlatformMiddlewareChain>();
        return services;
    }
}

