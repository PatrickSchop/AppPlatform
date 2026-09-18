using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Wisdi.AppPlatform.Endpoints;

namespace Wisdi.AppPlatform.Generated;

/// <summary>
/// AuthorizationLevel.Anonymous refers to the Functions host key check, which the platform
/// does not use; the [Authorize] attribute is what Step 10's middleware reads.
/// </summary>
public class BackgroundTaskFunctions(IBackgroundTaskEndpoints inner)
{
    [Function("GetNotificationTasks")]
    [Authorize]
    public Task<IActionResult> GetNotifications(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/tasks/notifications")] HttpRequest req)
        => inner.GetNotificationsAsync(req, req.HttpContext.RequestAborted);

    [Function("GetTasks")]
    [Authorize]
    public Task<IActionResult> GetAll(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/tasks")] HttpRequest req)
        => inner.GetAllAsync(req, req.HttpContext.RequestAborted);

    [Function("CreateTask")]
    [Authorize]
    public Task<IActionResult> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/tasks")] HttpRequest req)
        => inner.CreateAsync(req, req.HttpContext.RequestAborted);

    [Function("GetTaskStatus")]
    [Authorize]
    public Task<IActionResult> GetById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/tasks/{id}")] HttpRequest req,
        string id)
        => inner.GetByIdAsync(req, id, req.HttpContext.RequestAborted);

    [Function("CheckTasks")]
    [Authorize]
    public Task<IActionResult> Check(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/tasks/check")] HttpRequest req)
        => inner.CheckAsync(req, req.HttpContext.RequestAborted);
}
