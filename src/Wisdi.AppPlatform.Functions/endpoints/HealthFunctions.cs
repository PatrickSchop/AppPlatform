using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using PS.AppPlatform.Endpoints;

namespace PS.AppPlatform.Generated;

[AllowAnonymous]
public class HealthFunctions(IHealthEndpoints inner)
{
    [Function("GetHealth")]
    public Task<IActionResult> GetHealth(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/health")] HttpRequest req)
        => inner.GetHealthAsync(req, req.HttpContext.RequestAborted);
}

