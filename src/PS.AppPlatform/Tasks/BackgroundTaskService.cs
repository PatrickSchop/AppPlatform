using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using PS.AppPlatform.Data;
using PS.AppPlatform.Tenancy;

namespace PS.AppPlatform.Tasks;

public class BackgroundTaskService<TContext> : IBackgroundTaskManagementService
    where TContext : PlatformDbContext
{
    private readonly IScopedDbContextFactory<TContext> _scopedFactory;
    private readonly IUnscopedDbContextFactory<TContext> _unscopedFactory;
    private readonly ITenantContext? _tenantContext;
    private readonly ILogger<BackgroundTaskService<TContext>> _logger;
    private readonly int _leaseSeconds;

    public BackgroundTaskService(
        IScopedDbContextFactory<TContext> scopedFactory,
        IUnscopedDbContextFactory<TContext> unscopedFactory,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<BackgroundTaskService<TContext>> logger)
    {
        _scopedFactory = scopedFactory;
        _unscopedFactory = unscopedFactory;
        _tenantContext = serviceProvider.GetService<ITenantContext>();
        _logger = logger;

        _leaseSeconds = configuration.GetValue("backgroundTasks:leaseSeconds", 300);
    }

    public async Task<Guid> CreateTaskAsync<T>(string taskType, T taskData, string description, bool requiresNotification, CancellationToken ct = default)
    {
        // Unscoped: the TenantEntity interceptor rules do not apply to BackgroundTask, and the
        // tenant/user come from the ambient ITenantContext, not from the query filter.
        using var context = await _unscopedFactory.CreateDbContextAsync(ct);

        var task = new BackgroundTask
        {
            Id = Guid.NewGuid(),
            TaskType = taskType,
            Status = BackgroundTaskStatus.New,
            CompletionPercentage = 0,
            Description = description,
            RequiresNotification = requiresNotification,
            TaskData = JsonSerializer.Serialize(taskData),
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow,
            ExecutionManagerId = null,
            LeaseExpiresUtc = null,
            TenantId = _tenantContext?.TenantId,
            CreatedByUserId = _tenantContext?.UserId
        };

        context.BackgroundTasks.Add(task);
        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Created background task {TaskId} of type {TaskType}", task.Id, taskType);

        return task.Id;
    }

    public async Task<BackgroundTask?> GetTaskStatusAsync(Guid taskId, CancellationToken ct = default)
    {
        using var context = await _scopedFactory.CreateDbContextAsync(ct);

        return await context.BackgroundTasks
            .FirstOrDefaultAsync(t => t.Id == taskId, ct);
    }

    public async Task<List<BackgroundTask>> GetAllTasksAsync(CancellationToken ct = default)
    {
        using var context = await _scopedFactory.CreateDbContextAsync(ct);

        return await context.BackgroundTasks
            .OrderByDescending(t => t.CreatedDate)
            .ToListAsync(ct);
    }

    public async Task<bool> ResumeTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        using var context = await _scopedFactory.CreateDbContextAsync(ct);

        var task = await context.BackgroundTasks
            .FirstOrDefaultAsync(t => t.Id == taskId, ct);

        if (task == null)
        {
            _logger.LogWarning("Task {TaskId} not found for resume", taskId);
            return false;
        }

        if (task.Status != BackgroundTaskStatus.Paused)
        {
            _logger.LogWarning("Task {TaskId} is not in the paused state", taskId);
            return false;
        }

        task.Status = BackgroundTaskStatus.Resumed;
        task.ExecutionManagerId = null;
        task.CompletionPercentage = 0;
        task.StartedDate = null;
        task.CompletedDate = null;
        task.UpdatedDate = DateTime.UtcNow;

        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Resumed task {TaskId}", taskId);

        return true;
    }

    public async Task UpdateProgressAsync(Guid taskId, int percentage)
    {
        percentage = Math.Clamp(percentage, 0, 100);

        using var context = await _unscopedFactory.CreateDbContextAsync();

        var task = await context.BackgroundTasks
            .FirstOrDefaultAsync(t => t.Id == taskId);

        if (task != null)
        {
            task.CompletionPercentage = percentage;
            task.UpdatedDate = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }
    }

    public async Task UpdateStatusAsync(Guid taskId, BackgroundTaskStatus status)
    {
        await UpdateStatusAsync(taskId, status, null);
    }

    public async Task UpdateStatusAsync(Guid taskId, BackgroundTaskStatus status, string? message)
    {
        using var context = await _unscopedFactory.CreateDbContextAsync();

        var task = await context.BackgroundTasks
            .FirstOrDefaultAsync(t => t.Id == taskId);

        if (task != null)
        {
            task.Status = status;
            task.UpdatedDate = DateTime.UtcNow;
            if (message != null)
            {
                task.StatusMessage = message;
            }

            if ((status & BackgroundTaskStatus.ExecutingFlag) == 0)
            {
                task.ExecutionManagerId = null;
                task.LeaseExpiresUtc = null;
            }
            if ((status & BackgroundTaskStatus.CompletedFlag) > 0)
            {
                task.CompletedDate = task.UpdatedDate;
                task.CompletionPercentage = 100;
            }

            await context.SaveChangesAsync();

            _logger.LogInformation("Updated task {TaskId} status to {Status}", taskId, status);
        }
    }

    public async Task RenewLeaseAsync(Guid taskId, CancellationToken ct = default)
    {
        using var context = await _unscopedFactory.CreateDbContextAsync(ct);
        var seconds = _leaseSeconds;
        await context.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE [dbo].[BackgroundTasks]
               SET [LeaseExpiresUtc] = DATEADD(second, {seconds}, GETUTCDATE()),
                   [UpdatedDate] = GETUTCDATE()
               WHERE [Id] = {taskId}", ct);
    }
}

