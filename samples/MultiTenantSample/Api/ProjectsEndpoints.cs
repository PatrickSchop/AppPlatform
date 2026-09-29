using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MultiTenantSample.Data;
using MultiTenantSample.Tasks;
using PS.AppPlatform.Data;
using PS.AppPlatform.Tasks;

namespace MultiTenantSample.Api;

public class ProjectsEndpoints(IDbContextFactory<AppDbContext> factory, IBackgroundTaskService taskService)
{
    public async Task<IActionResult> GetAllAsync(HttpRequest req, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var projects = await db.Projects.OrderBy(p => p.Name).ToListAsync(ct);
        return new OkObjectResult(projects);
    }

    public async Task<IActionResult> CreateAsync(HttpRequest req, CancellationToken ct)
    {
        using var reader = new StreamReader(req.Body);
        var json = await reader.ReadToEndAsync(ct);
        var request = System.Text.Json.JsonSerializer.Deserialize<CreateProjectRequest>(
            json, System.Text.Json.JsonSerializerOptions.Web);
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
            return new BadRequestResult();

        // TenantId is intentionally not set here: the save interceptor stamps it from the
        // resolved request tenant.
        var project = new Project { Name = request.Name };

        await using var db = await factory.CreateDbContextAsync(ct);
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);

        return new OkObjectResult(project);
    }

    public async Task<IActionResult> StartCountAsync(HttpRequest req, CancellationToken ct)
    {
        var taskId = await taskService.CreateTaskAsync(
            "projectcount", new ProjectCountTaskData(), "Counting projects", requiresNotification: true, ct);
        return new OkObjectResult(new { taskId });
    }
}

public sealed record CreateProjectRequest(string Name);
