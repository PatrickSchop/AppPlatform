using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MultiTenantSample.Data;
using PS.AppPlatform.Data;

namespace MultiTenantSample.Api;

public class FeedbackEndpoints(
    IUnscopedDbContextFactory<AppDbContext> unscopedFactory,
    IScopedDbContextFactory<AppDbContext> scopedFactory)
{
    public async Task<IActionResult> SubmitAsync(HttpRequest req, string slug, CancellationToken ct)
    {
        using var reader = new StreamReader(req.Body);
        var json = await reader.ReadToEndAsync(ct);
        var request = System.Text.Json.JsonSerializer.Deserialize<SubmitFeedbackRequest>(
            json, System.Text.Json.JsonSerializerOptions.Web);
        if (request is null || string.IsNullOrWhiteSpace(request.Text))
            return new BadRequestResult();

        // Unscoped: the caller has no tenant of their own, so the alias lookup must run
        // without a tenant filter.
        await using var unscoped = await unscopedFactory.CreateDbContextAsync(ct);
        var alias = await unscoped.TenantAliases.FirstOrDefaultAsync(a => a.Slug == slug, ct);
        if (alias is null)
            return new NotFoundResult();

        // The endpoint resolved the tenant itself from the slug, so it writes into that
        // tenant explicitly. It never trusts a tenant the caller might claim.
        await using var db = scopedFactory.CreateForTenant(alias.TenantId);
        db.Feedback.Add(new Feedback { Text = request.Text });
        await db.SaveChangesAsync(ct);

        return new OkResult();
    }

    public async Task<IActionResult> GetAllAsync(HttpRequest req, CancellationToken ct)
    {
        await using var db = await scopedFactory.CreateDbContextAsync(ct);
        var feedback = await db.Feedback.OrderByDescending(f => f.CreatedUtc).ToListAsync(ct);
        return new OkObjectResult(feedback);
    }
}

public sealed record SubmitFeedbackRequest(string Text);
