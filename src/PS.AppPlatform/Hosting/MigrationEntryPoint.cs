using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PS.AppPlatform.Data;

namespace PS.AppPlatform.Hosting;

/// <summary>
/// Entry point for database migration operations via CLI (--migrate flag).
/// Deprecated: use PlatformCommandLine instead.
/// </summary>
public static class MigrationEntryPoint
{
    /// <summary>
    /// Returns true when args contain "--migrate".
    /// </summary>
    public static bool IsMigrationRun(string[] args)
    {
        return PlatformCommandLine.IsCommandRun(args);
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
        return await PlatformCommandLine.RunAsync<TContext>(args, configureAssemblies);
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
        return PlatformCommandLine.BuildCommandServices<TContext>(configuration, configureAssemblies);
    }
}

