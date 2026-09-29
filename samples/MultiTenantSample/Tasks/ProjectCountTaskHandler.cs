using Microsoft.EntityFrameworkCore;
using MultiTenantSample.Data;
using PS.AppPlatform.Data;
using PS.AppPlatform.Tasks;

namespace MultiTenantSample.Tasks;

public class ProjectCountTaskHandler(IDbContextFactory<AppDbContext> factory)
    : ITaskHandler<ProjectCountTaskData>
{
    public async Task HandleAsync(BackgroundTask task, ProjectCountTaskData data,
                                  TaskHandlerContext context, CancellationToken ct)
    {
        // factory is scoped to task.TenantId: TaskExecutionManager set it on this scope
        // before the handler was resolved, so this count never crosses tenants.
        await using var db = await factory.CreateDbContextAsync(ct);
        var count = await db.Projects.CountAsync(ct);

        var thisTask = await db.BackgroundTasks.FirstAsync(t => t.Id == task.Id, ct);
        thisTask.StatusMessage = $"{count} project(s)";
        await db.SaveChangesAsync(ct);

        await context.CompleteAsync();
    }
}

public sealed record ProjectCountTaskData();
