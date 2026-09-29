using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using MultiTenantSample.Api;

namespace MultiTenantSample.Generated;

public class ProjectsFunctions(ProjectsEndpoints inner)
{
    [Function("GetProjects")]
    [Authorize]
    public Task<IActionResult> GetProjects(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/projects")] HttpRequest req)
        => inner.GetAllAsync(req, req.HttpContext.RequestAborted);

    [Function("CreateProject")]
    [Authorize(Roles = "editor")]
    public Task<IActionResult> CreateProject(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/projects")] HttpRequest req)
        => inner.CreateAsync(req, req.HttpContext.RequestAborted);

    [Function("StartProjectCount")]
    [Authorize]
    public Task<IActionResult> StartProjectCount(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/projects/count")] HttpRequest req)
        => inner.StartCountAsync(req, req.HttpContext.RequestAborted);
}
