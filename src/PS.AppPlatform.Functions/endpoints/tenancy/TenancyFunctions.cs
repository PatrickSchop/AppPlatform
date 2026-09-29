using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using PS.AppPlatform.Endpoints;
using PS.AppPlatform.Tenancy;

namespace PS.AppPlatform.Generated;

/// <summary>
/// The login flow (MT-13) is built on this endpoint, so it needs an authenticated user but
/// no selected tenant: an unregistered user, and a user with several tenants and no header,
/// must both get a 200 response, not 403/409.
/// </summary>
public class TenancyFunctions(ITenancyEndpoints inner)
{
    [Function("GetMyTenants")]
    [Authorize]
    [TenantOptional]
    public Task<IActionResult> GetMyTenants(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/me/tenants")] HttpRequest req)
        => inner.GetMyTenantsAsync(req, req.HttpContext.RequestAborted);
}
