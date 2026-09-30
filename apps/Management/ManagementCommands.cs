using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tenancy;
using PS.Management.Registry;

namespace PS.Management;

/// <summary>Registers management-app-specific commands and services into the command pipeline.</summary>
public static class ManagementCommands
{
    public static void Register(IServiceCollection services, IConfiguration configuration)
    {
        // Commands
        services.AddSingleton<IPlatformCommand, BootstrapAdminCommand>();

        // Services needed by commands / post-migration steps
        services.AddScoped<RegistryService>();
        services.AddScoped<MembershipQuery>();

        // Manifest needed by SelfRegistrationStep
        services.AddSingleton<AppManifest, Manifest>();

        // Post-migration steps
        services.AddSingleton<IPostMigrationStep, SelfRegistrationStep>();
    }
}
