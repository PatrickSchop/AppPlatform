using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PS.AppPlatform.Data;
using PS.AppPlatform.Hosting;

namespace PS.AppPlatform.Endpoints;

public sealed class HealthEndpoints : IHealthEndpoints
{
    private readonly ILogger<HealthEndpoints> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostingEnvironment _hostingEnvironment;
    private readonly IDatabaseMigrator? _databaseMigrator;

    public HealthEndpoints(
        ILogger<HealthEndpoints> logger,
        IConfiguration configuration,
        IHostingEnvironment hostingEnvironment,
        IDatabaseMigrator? databaseMigrator = null)
    {
        _logger = logger;
        _configuration = configuration;
        _hostingEnvironment = hostingEnvironment;
        _databaseMigrator = databaseMigrator;
    }

    public async Task<IActionResult> GetHealthAsync(HttpRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Health check requested");

        try
        {
            var databaseStatus = "skipped";
            var checkDatabase = _configuration.GetValue<bool>("health:checkDatabase", false);

            if (checkDatabase && _databaseMigrator != null)
            {
                // Check database with a timeout
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(2));
                    try
                    {
                        var canConnect = await _databaseMigrator.CanConnectAsync(cts.Token);
                        databaseStatus = canConnect ? "ok" : "unavailable";
                    }
                    catch (OperationCanceledException)
                    {
                        databaseStatus = "unavailable";
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error checking database health");
                        databaseStatus = "unavailable";
                    }
                }
            }

            var version = GetPlatformVersion();
            var environment = _hostingEnvironment.EnvironmentType.ToString().ToLower();

            return new OkObjectResult(new
            {
                status = "ok",
                version,
                environment,
                database = databaseStatus
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking health");
            return new ObjectResult(new { status = "error", message = ex.Message })
            {
                StatusCode = 500
            };
        }
    }

    private static string GetPlatformVersion()
    {
        var assembly = typeof(HealthEndpoints).Assembly;
        var versionAttribute = assembly.GetName().Version;
        return versionAttribute?.ToString() ?? "unknown";
    }
}

