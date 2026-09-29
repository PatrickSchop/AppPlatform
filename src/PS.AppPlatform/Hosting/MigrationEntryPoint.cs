using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PS.AppPlatform.Data;

namespace PS.AppPlatform.Hosting;

/// <summary>
/// Entry point for database migration operations via CLI (--migrate flag).
/// </summary>
public static class MigrationEntryPoint
{
    /// <summary>
    /// Returns true when args contain "--migrate".
    /// </summary>
    public static bool IsMigrationRun(string[] args)
    {
        return args.Contains("--migrate") || args.Contains("-migrate");
    }

    /// <summary>
    /// Builds a minimal service provider, runs migrations, writes results to the console
    /// and returns a process exit code (0 success, 1 failure).
    /// </summary>
    public static async Task<int> RunAsync<TContext>(
        string[] args,
        Action<PlatformAssemblies>? configureAssemblies = null)
        where TContext : PlatformDbContext
    {
        var settingsFile = ExtractSettingsFile(args);
        // The anchor must be the app, not this library: it locates the appsettings files
        // next to the entry assembly.
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddPlatformConfiguration(assembly, extraSettingsFile: settingsFile);

        var configuration = configBuilder.Build();

        var services = BuildMigrationServices<TContext>(configuration, configureAssemblies);

        var serviceProvider = services.BuildServiceProvider();
        var migrator = serviceProvider.GetRequiredService<IDatabaseMigrator>();

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

    /// <summary>
    /// The service graph a migration run needs. Separated so a test can assert it resolves
    /// without running an actual migration.
    /// </summary>
    internal static ServiceCollection BuildMigrationServices<TContext>(
        IConfiguration configuration,
        Action<PlatformAssemblies>? configureAssemblies = null)
        where TContext : PlatformDbContext
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var assemblies = new PlatformAssemblies();
        configureAssemblies?.Invoke(assemblies);

        services.AddSingleton(assemblies);
        // AzureIdentityProvider takes IConfiguration, so it has to be resolvable here.
        // Without this, --migrate throws before touching the database whenever the app
        // authenticates to SQL with a managed identity -- which is every deployed app.
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostingEnvironment, HostingEnvironment>();
        services.AddSingleton<IAzureIdentityProvider, AzureIdentityProvider>();
        services.AddPlatformData<TContext>(configuration);
        return services;
    }

    private static string? ExtractSettingsFile(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if ((args[i] == "--settingsFile" || args[i] == "-settingsFile") &&
                i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        return null;
    }
}

