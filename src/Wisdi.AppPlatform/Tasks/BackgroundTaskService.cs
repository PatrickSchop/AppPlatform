using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Wisdi.AppPlatform.Data;

namespace Wisdi.AppPlatform.Tasks;

public class BackgroundTaskService<TContext> : IBackgroundTaskManagementService
    where TContext : PlatformDbContext
{
    private readonly IDbContextFactory<TContext> _dbContextFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BackgroundTaskService<TContext>> _logger;
    private readonly string? _apiBaseUrl;
    private readonly int _leaseSeconds;

    public BackgroundTaskService(
        IDbContextFactory<TContext> dbContextFactory,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<BackgroundTaskService<TContext>> logger)
    {
        _dbContextFactory = dbContextFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _apiBaseUrl = configuration.GetValue<string>("backgroundTasks:apiBaseUrl");
        _leaseSeconds = configuration.GetValue("backgroundTasks:leaseSeconds", 300);
    }

    public async Task<Guid> CreateTaskAsync<T>(string taskType, T taskData, string description, bool requiresNotification, CancellationToken ct = default)
    {
        using var context = await _dbContextFactory.CreateDbContextAsync(ct);

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
            LeaseExpiresUtc = null
        };

        context.BackgroundTasks.Add(task);
        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Created background task {TaskId} of type {TaskType}", task.Id, taskType);

        TriggerTaskCheck();

        return task.Id;
    }

    public async Task<BackgroundTask?> GetTaskStatusAsync(Guid taskId, CancellationToken ct = default)
    {
        using var context = await _dbContextFactory.CreateDbContextAsync(ct);

        return await context.BackgroundTasks
            .FirstOrDefaultAsync(t => t.Id == taskId, ct);
    }

    public async Task<List<BackgroundTask>> GetAllTasksAsync(CancellationToken ct = default)
    {
        using var context = await _dbContextFactory.CreateDbContextAsync(ct);

        return await context.BackgroundTasks
            .OrderByDescending(t => t.CreatedDate)
            .ToListAsync(ct);
    }

    public async Task<bool> ResumeTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        using var context = await _dbContextFactory.CreateDbContextAsync(ct);

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

        TriggerTaskCheck();

        return true;
    }

    public async Task UpdateProgressAsync(Guid taskId, int percentage)
    {
        percentage = Math.Clamp(percentage, 0, 100);

        using var context = await _dbContextFactory.CreateDbContextAsync();

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
        using var context = await _dbContextFactory.CreateDbContextAsync();

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

            TriggerTaskCheck();
        }
    }

    public async Task RenewLeaseAsync(Guid taskId, CancellationToken ct = default)
    {
        using var context = await _dbContextFactory.CreateDbContextAsync(ct);
        var seconds = _leaseSeconds;
        await context.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE [dbo].[BackgroundTasks]
               SET [LeaseExpiresUtc] = DATEADD(second, {seconds}, GETUTCDATE()),
                   [UpdatedDate] = GETUTCDATE()
               WHERE [Id] = {taskId}", ct);
    }

    private void TriggerTaskCheck()
    {
        if (_apiBaseUrl is null)
        {
            _logger.LogDebug("backgroundTasks:apiBaseUrl is not configured; relying on the timer trigger to start queued tasks.");
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);

                var url = $"{_apiBaseUrl}/api/tasks/check";
                _logger.LogDebug("Triggering task check via HTTP: {Url}", url);

                var response = await httpClient.PostAsync(url, null);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Task check HTTP call returned status {StatusCode}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error triggering task check via HTTP");
            }
        });
    }
}
