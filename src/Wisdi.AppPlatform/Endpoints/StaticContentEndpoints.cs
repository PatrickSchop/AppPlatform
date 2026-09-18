using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Wisdi.AppPlatform.StaticContent;

namespace Wisdi.AppPlatform.Endpoints;

public class StaticContentEndpoints(StaticContentHandler handler) : IStaticContentEndpoints
{
    public Task<IActionResult> HandleAsync(HttpRequest request, string path, CancellationToken ct)
        => handler.HandleAsync(request, path, ct);
}
