using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using MultiTenantSample.Api;

namespace MultiTenantSample.Generated;

public class ReportsFunctions(ReportsEndpoints inner)
{
    [Function("ProjectsPerTenant")]
    [Authorize(Roles = "reporter")]
    public Task<IActionResult> ProjectsPerTenant(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/reports/projects-per-tenant")] HttpRequest req)
        => inner.ProjectsPerTenantAsync(req, req.HttpContext.RequestAborted);
}
