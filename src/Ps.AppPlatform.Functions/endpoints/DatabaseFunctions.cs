using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using PS.AppPlatform.Endpoints;

namespace PS.AppPlatform.Generated;

public class DatabaseFunctions(IDatabaseEndpoints inner)
{
    [Function("InitializeDatabase")]
    [Authorize]
    public Task<IActionResult> Initialize(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/initializeDatabase")] HttpRequest req)
        => inner.InitializeAsync(req, req.HttpContext.RequestAborted);
}

