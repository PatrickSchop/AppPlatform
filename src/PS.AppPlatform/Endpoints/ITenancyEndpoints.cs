using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace PS.AppPlatform.Endpoints;

public interface ITenancyEndpoints
{
    Task<IActionResult> GetMyTenantsAsync(HttpRequest request, CancellationToken ct = default);
}
