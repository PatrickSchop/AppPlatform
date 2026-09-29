using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using MultiTenantSample.Api;

namespace MultiTenantSample.Generated;

public class FeedbackFunctions(FeedbackEndpoints inner)
{
    // Anonymous: the caller has no token. The endpoint links the slug to a tenant itself and
    // writes with CreateForTenant, so it never needs a tenant header it couldn't trust anyway.
    [Function("SubmitFeedback")]
    [AllowAnonymous]
    public Task<IActionResult> SubmitFeedback(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/public/{slug}/feedback")] HttpRequest req,
        string slug)
        => inner.SubmitAsync(req, slug, req.HttpContext.RequestAborted);

    [Function("GetFeedback")]
    [Authorize]
    public Task<IActionResult> GetFeedback(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/feedback")] HttpRequest req)
        => inner.GetAllAsync(req, req.HttpContext.RequestAborted);
}
