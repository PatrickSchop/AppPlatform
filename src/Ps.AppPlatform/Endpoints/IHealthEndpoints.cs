using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace PS.AppPlatform.Endpoints;

/// <summary>
/// Interface for health check endpoints.
/// This endpoint should typically be in AnonymousFunctions â€” a health check that requires a token is not useful.
/// </summary>
public interface IHealthEndpoints
{
    /// <summary>
    /// Get health status of the application.
    /// GET /api/health
    /// Returns 200 with status, version, environment, and database availability.
    /// </summary>
    Task<IActionResult> GetHealthAsync(HttpRequest request, CancellationToken ct = default);
}

