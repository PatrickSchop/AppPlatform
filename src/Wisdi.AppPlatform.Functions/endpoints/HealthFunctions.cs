using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Wisdi.AppPlatform.Endpoints;

namespace Wisdi.AppPlatform.Generated;

[AllowAnonymous]
public class HealthFunctions(IHealthEndpoints inner)
{
    [Function("GetHealth")]
    public Task<IActionResult> GetHealth(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/health")] HttpRequest req)
        => inner.GetHealthAsync(req, req.HttpContext.RequestAborted);
}
