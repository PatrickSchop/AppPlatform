using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MultiTenantSample.Data;
using PS.AppPlatform.Data;

namespace MultiTenantSample.Api;

public class CountriesEndpoints(IDbContextFactory<AppDbContext> factory)
{
    public async Task<IActionResult> GetAllAsync(HttpRequest req, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var countries = await db.Countries.OrderBy(c => c.Name).ToListAsync(ct);
        return new OkObjectResult(countries);
    }
}
