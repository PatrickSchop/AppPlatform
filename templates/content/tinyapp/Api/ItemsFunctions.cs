using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace TinyApp.Api;

/// <summary>The HTTP surface of ItemsEndpoints. Any registered user may read; only editors may write.</summary>
public class ItemsFunctions(ItemsEndpoints inner)
{
    [Function("GetItems")]
    [Authorize]
    public Task<IActionResult> GetItems(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/items")] HttpRequest req)
        => inner.GetAllAsync(req, req.HttpContext.RequestAborted);

    [Function("CreateItem")]
    [Authorize(Roles = "editor")]
    public Task<IActionResult> CreateItem(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/items")] HttpRequest req)
        => inner.CreateAsync(req, req.HttpContext.RequestAborted);
}
