using Microsoft.Extensions.DependencyInjection;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tenancy;

namespace PS.Management.Registry;

/// <summary>
/// IPostMigrationStep that registers the management app in its own registry.
/// Runs after every --migrate; idempotent.
/// </summary>
public sealed class SelfRegistrationStep(AppManifest manifest) : IPostMigrationStep
{
    public async Task<int> RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var registryService = services.GetRequiredService<RegistryService>();

        var registration = new ApplicationRegistration(
            manifest.Key,
            manifest.DisplayName,
            manifest.Tenancy,
            manifest.Roles,
            ServicePrincipalId: null);

        var result = await registryService.UpsertApplicationAsync(registration, ct);

        if (result.Created)
        {
            Console.WriteLine($"Self-registration: created application '{manifest.Key}' (id: {result.ApplicationId}).");
        }
        else
        {
            Console.Write($"Self-registration: application '{manifest.Key}' already registered.");
            if (result.RolesAdded.Count > 0)
                Console.Write($" Added roles: {string.Join(", ", result.RolesAdded)}.");
            if (result.RolesDeprecated.Count > 0)
                Console.Write($" Deprecated roles: {string.Join(", ", result.RolesDeprecated)}.");
            if (result.RolesReactivated.Count > 0)
                Console.Write($" Reactivated roles: {string.Join(", ", result.RolesReactivated)}.");
            Console.WriteLine();
        }

        return 0;
    }
}
