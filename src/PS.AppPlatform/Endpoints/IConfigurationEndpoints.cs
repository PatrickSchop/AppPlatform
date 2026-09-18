using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace PS.AppPlatform.Endpoints;

/// <summary>
/// Interface for application configuration endpoints.
/// WARNING: Configuration is public. Only non-secret values belong under webApp configuration.
/// </summary>
public interface IConfigurationEndpoints
{
    /// <summary>
    /// Get public web application configuration.
    /// GET /configuration.json
    /// </summary>
    Task<IActionResult> GetWebAppConfigurationAsync(HttpRequest request, CancellationToken ct = default);
}

