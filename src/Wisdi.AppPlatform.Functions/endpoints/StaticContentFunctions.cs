using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using PS.AppPlatform.Endpoints;

namespace PS.AppPlatform.Generated;

/// <summary>
/// The SPA shell is public by design. Security is enforced by the API, not by withholding
/// the bundle: every /api/* endpoint is default-deny (see Auth/FunctionAuthorizationMiddleware),
/// so an unauthenticated visitor can load the app and reach no data at all. Serving static
/// HTML and JS to anyone is harmless; handling 401/403 gracefully is the front-end's job.
/// This differs deliberately from the source, where Static.cs:25 carries [Authorize].
///
/// The platform's security model, stated once here and enforced everywhere:
/// The API is the security boundary. No data is reachable unauthorized, regardless of who
/// downloaded the bundle. The SPA is public; every /api/* endpoint is default-deny. A
/// front-end that mishandles a 403 is a bug, not a vulnerability.
/// </summary>
[AllowAnonymous]
public class StaticContentFunctions(IStaticContentEndpoints inner)
{
    [Function("StaticContent")]
    public Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "{*path}")] HttpRequest req,
        string path = "")
        => inner.HandleAsync(req, path, req.HttpContext.RequestAborted);
}

