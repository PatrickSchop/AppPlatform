using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PS.AppPlatform.Auth;
using PS.AppPlatform.Tenancy;

namespace PS.AppPlatform.Endpoints;

public sealed class TenancyEndpoints : ITenancyEndpoints
{
    private readonly ITenantDirectory _directory;
    private readonly ILogger<TenancyEndpoints> _logger;

    public TenancyEndpoints(ITenantDirectory directory, ILogger<TenancyEndpoints> logger)
    {
        _directory = directory;
        _logger = logger;
    }

    public async Task<IActionResult> GetMyTenantsAsync(HttpRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Getting tenant memberships");

        try
        {
            var identity = request.HttpContext.User.GetIdentityKey();
            if (identity == null)
            {
                return new UnauthorizedResult();
            }

            var memberships = await _directory.GetMembershipsAsync(identity, ct);
            if (memberships == null)
            {
                return new OkObjectResult(new MyTenantsResponse(false, null, []));
            }

            var tenants = memberships.Tenants
                .OrderBy(t => t.TenantName, StringComparer.OrdinalIgnoreCase)
                .Select(t => new MyTenantResponse(t.TenantId, t.TenantName, t.Roles))
                .ToList();

            return new OkObjectResult(new MyTenantsResponse(true, memberships.DisplayName, tenants));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tenant memberships");
            return new StatusCodeResult(500);
        }
    }
}
