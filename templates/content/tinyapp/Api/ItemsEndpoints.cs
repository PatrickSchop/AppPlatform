using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TinyApp.Data;

namespace TinyApp.Api;

public class ItemsEndpoints(IDbContextFactory<AppDbContext> factory)
{
    public async Task<IActionResult> GetAllAsync(HttpRequest req, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var items = await db.Items.OrderBy(i => i.Name).ToListAsync(ct);
        return new OkObjectResult(items);
    }

    public async Task<IActionResult> CreateAsync(HttpRequest req, CancellationToken ct)
    {
        using var reader = new StreamReader(req.Body);
        var json = await reader.ReadToEndAsync(ct);
        var request = System.Text.Json.JsonSerializer.Deserialize<CreateItemRequest>(
            json, System.Text.Json.JsonSerializerOptions.Web);
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
            return new BadRequestResult();

        // TenantId is intentionally not set: the save interceptor stamps it from the request's tenant.
        var item = new Item { Name = request.Name.Trim() };

        await using var db = await factory.CreateDbContextAsync(ct);
        db.Items.Add(item);
        await db.SaveChangesAsync(ct);

        return new OkObjectResult(item);
    }
}

public sealed record CreateItemRequest(string Name);
