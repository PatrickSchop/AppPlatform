using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Wisdi.AppPlatform.Endpoints;

public interface IStaticContentEndpoints
{
    Task<IActionResult> HandleAsync(HttpRequest request, string path, CancellationToken ct);
}
