using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Wisdi.AppPlatform.Data;

/// <summary>
/// Extensions for registering the platform data layer.
/// Note: The app calls AddPlatformData&lt;TContext&gt; explicitly from its Program.cs,
/// because the platform cannot know the app's context type. This is the single place
/// where the platform requires one line of per-app wiring.
/// </summary>
public static class PlatformDataExtensions
{
    /// <summary>
    /// Registers TContext as scoped, a scoped IDbContextFactory&lt;TContext&gt;, the
    /// managed-identity interceptor when configured, and the DatabaseMigrator.
    /// </summary>
    public static IServiceCollection AddPlatformData<TContext>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TContext : PlatformDbContext
    {
        var dbConfig = configuration.GetSection("database").Get<DatabaseConfiguration>();
        if (dbConfig == null || string.IsNullOrEmpty(dbConfig.ConnectionString))
        {
            throw new InvalidOperationException("Missing 'database' configuration section or connection string.");
        }

        if (dbConfig.UseManagedIdentity)
        {
            services.AddSingleton<AzureSqlTokenInterceptor>();
        }

        var configureDbContext = (IServiceProvider serviceProvider, DbContextOptionsBuilder options) =>
        {
            var connectionStringBuilder = new SqlConnectionStringBuilder(dbConfig.ConnectionString);

            options.UseSqlServer(
                connectionStringBuilder.ConnectionString,
                sqlOptions =>
                {
                    sqlOptions.CommandTimeout(30);
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null
                    );
                }
            );

            if (dbConfig.UseManagedIdentity)
            {
                var interceptor = serviceProvider.GetRequiredService<AzureSqlTokenInterceptor>();
                options.AddInterceptors(interceptor);
            }
        };

        services.AddDbContext<TContext>(configureDbContext);
        services.AddDbContextFactory<TContext>(configureDbContext, ServiceLifetime.Scoped);

        services.AddSingleton<IMigrationScriptProvider, EmbeddedMigrationScriptProvider>();
        services.AddSingleton<IMigrationScriptProvider, DirectoryMigrationScriptProvider>();
        services.AddScoped<DatabaseMigrator<TContext>>();
        services.AddScoped<IDatabaseMigrator>(sp => sp.GetRequiredService<DatabaseMigrator<TContext>>());

        return services;
    }
}
