using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using PS.AppPlatform.Auth;
using PS.AppPlatform.Tenancy;

namespace PS.Management.Api;

[TenantOptional]
public class RegistryFunctions(RegistryEndpoints inner)
{
    [Function("RegisterApplication")]
    [Authorize(Policy = PlatformPolicies.AuthenticatedOnly)]
    public Task<IActionResult> RegisterApplication(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "api/registry/v1/applications/{key}")] HttpRequest req, string key)
        => inner.RegisterAsync(req, key, req.HttpContext.RequestAborted);

    [Function("GetUserMemberships")]
    [Authorize(Policy = PlatformPolicies.AuthenticatedOnly)]
    public Task<IActionResult> GetUserMemberships(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/registry/v1/applications/{key}/users/{oid}/memberships")] HttpRequest req, string key, string oid)
        => inner.GetMembershipsAsync(req, key, oid, req.HttpContext.RequestAborted);
}
