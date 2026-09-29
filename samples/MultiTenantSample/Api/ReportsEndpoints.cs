using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MultiTenantSample.Data;
using PS.AppPlatform.Data;

namespace MultiTenantSample.Api;

public class ReportsEndpoints(IUnscopedDbContextFactory<AppDbContext> factory)
{
    public async Task<IActionResult> ProjectsPerTenantAsync(HttpRequest req, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var counts = await db.Projects
            .GroupBy(p => p.TenantId)
            .Select(g => new { tenantId = g.Key, count = g.Count() })
            .ToListAsync(ct);
        return new OkObjectResult(counts);
    }
}
