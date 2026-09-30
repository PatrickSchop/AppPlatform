using Microsoft.Extensions.DependencyInjection;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tenancy;

namespace PS.Management.Registry;

/// <summary>
/// --bootstrap-admin --oid &lt;guid&gt; --tid &lt;guid&gt; --name &lt;display name&gt;
/// Idempotent: makes the given identity an admin in management/Default/Default.
/// Requires --migrate to have run first so the management application exists.
/// </summary>
public sealed class BootstrapAdminCommand : IPlatformCommand
{
    public string Name => "bootstrap-admin";

    public async Task<int> RunAsync(PlatformCommandArgs args, IServiceProvider services, CancellationToken ct)
    {
        var oidStr = args.Get("oid");
        var tidStr = args.Get("tid");
        var name = args.Get("name");

        if (string.IsNullOrWhiteSpace(oidStr))
        {
            Console.Error.WriteLine("Error: --oid is required.");
            return 2;
        }

        if (!Guid.TryParse(oidStr, out _))
        {
            Console.Error.WriteLine($"Error: --oid '{oidStr}' is not a valid GUID.");
            return 2;
        }

        if (string.IsNullOrWhiteSpace(tidStr))
        {
            Console.Error.WriteLine("Error: --tid is required.");
            return 2;
        }

        if (!Guid.TryParse(tidStr, out _))
        {
            Console.Error.WriteLine($"Error: --tid '{tidStr}' is not a valid GUID.");
            return 2;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Error.WriteLine("Error: --name is required.");
            return 2;
        }

        var identity = new IdentityKey(oidStr, tidStr);

        var registryService = services.GetRequiredService<RegistryService>();

        try
        {
            var userId = await registryService.EnsureAdminAsync(identity, name, ct);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Bootstrap admin complete. User id: {userId}");
            Console.ResetColor();
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }
}
