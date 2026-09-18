using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PS.AppPlatform.Data;

namespace PS.AppPlatform.Endpoints;

public sealed class DatabaseEndpoints : IDatabaseEndpoints
{
    private readonly ILogger<DatabaseEndpoints> _logger;
    private readonly IConfiguration _configuration;
    private readonly IDatabaseMigrator _databaseMigrator;

    public DatabaseEndpoints(
        ILogger<DatabaseEndpoints> logger,
        IConfiguration configuration,
        IDatabaseMigrator databaseMigrator)
    {
        _logger = logger;
        _configuration = configuration;
        _databaseMigrator = databaseMigrator;
    }

    public async Task<IActionResult> InitializeAsync(HttpRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Database initialization requested");

        try
        {
            // Check if database migration via API is enabled (gate 1: config check)
            var apiMigrationEnabled = _configuration.GetValue<bool>("database:apiMigration:enable", false);

            if (!apiMigrationEnabled)
            {
                _logger.LogWarning("Database migration API is disabled in configuration");
                return new ObjectResult(new { error = "Database migration over the API is disabled." })
                {
                    StatusCode = 403
                };
            }

            // Call the migration service
            var result = await _databaseMigrator.InitializeDatabaseAsync(ct);

            if (result.Success)
            {
                return new OkObjectResult(new
                {
                    success = true,
                    message = result.Message,
                    appliedMigrations = result.AppliedMigrations,
                    canConnect = result.CanConnect
                });
            }
            else
            {
                return new ObjectResult(new
                {
                    success = false,
                    message = result.Message
                })
                {
                    StatusCode = 500
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing database");
            return new ObjectResult(new
            {
                success = false,
                message = ex.Message
            })
            {
                StatusCode = 500
            };
        }
    }
}

