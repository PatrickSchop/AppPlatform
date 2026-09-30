using Microsoft.Extensions.DependencyInjection;
using PS.AppPlatform.Data;

namespace PS.AppPlatform.Hosting;

/// <summary>The --migrate command, migrating the database to the latest schema.</summary>
public sealed class MigrateCommand : IPlatformCommand
{
    public string Name => "migrate";

    public async Task<int> RunAsync(PlatformCommandArgs args, IServiceProvider services, CancellationToken ct)
    {
        var migrator = services.GetRequiredService<IDatabaseMigrator>();
        var result = await migrator.InitializeDatabaseAsync(ct);

        if (!result.Success)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Migration failed: {result.Message}");
            if (!string.IsNullOrEmpty(result.Error))
            {
                Console.WriteLine($"Error: {result.Error}");
            }

            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Migration successful: {result.Message}");
        if (result.AppliedMigrations.Count > 0)
        {
            Console.WriteLine("Applied migrations:");
            foreach (var migration in result.AppliedMigrations)
            {
                Console.WriteLine($"  - {migration}");
            }
        }

        Console.ResetColor();

        var violations = await migrator.ValidateTenancyConventionsAsync(ct);
        if (violations.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\nTenancy convention violations:");
            foreach (var violation in violations)
            {
                Console.WriteLine($"  - {violation}");
            }

            Console.ResetColor();
            return 1;
        }

        var steps = services.GetServices<IPostMigrationStep>();
        foreach (var step in steps)
        {
            var exitCode = await step.RunAsync(services, ct);
            if (exitCode != 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Post-migration step {step.GetType().Name} failed with exit code {exitCode}.");
                Console.ResetColor();
                return exitCode;
            }
        }

        return 0;
    }
}
