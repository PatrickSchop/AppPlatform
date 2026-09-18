using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Wisdi.AppPlatform.Endpoints;

namespace Wisdi.AppPlatform.Generated;

/// <summary>
/// The SPA fetches this before it has a bearer token, so it cannot be protected.
/// Only non-secret values belong under the webApp configuration key.
/// </summary>
[AllowAnonymous]
public class ConfigurationFunctions(IConfigurationEndpoints inner)
{
    [Function("GetWebAppConfiguration")]
    public Task<IActionResult> GetConfiguration(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "configuration.json")] HttpRequest req)
        => inner.GetConfigurationAsync(req, req.HttpContext.RequestAborted);
}
