using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Wisdi.AppPlatform.Endpoints;

/// <summary>
/// Interface for database management endpoints.
/// Database initialization is gated by authorization and configuration.
/// </summary>
public interface IDatabaseEndpoints
{
    /// <summary>
    /// Initialize or migrate the database.
    /// POST /api/initializeDatabase
    /// Requires [Authorize] attribute in the calling function.
    /// Returns 403 if database:apiMigration:enable is false.
    /// </summary>
    Task<IActionResult> InitializeAsync(HttpRequest request, CancellationToken ct = default);
}
