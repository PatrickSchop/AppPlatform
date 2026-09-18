using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PS.AppPlatform.StaticContent;

namespace PS.AppPlatform.Endpoints;

public class StaticContentEndpoints(StaticContentHandler handler) : IStaticContentEndpoints
{
    public Task<IActionResult> HandleAsync(HttpRequest request, string path, CancellationToken ct)
        => handler.HandleAsync(request, path, ct);
}

