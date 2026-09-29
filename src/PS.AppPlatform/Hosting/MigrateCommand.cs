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
        var result = await migrator.InitializeDatabaseAsync();

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
        return 0;
    }
}
