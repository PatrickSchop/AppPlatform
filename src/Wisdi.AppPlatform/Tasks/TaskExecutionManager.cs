using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text.Json;
using Wisdi.AppPlatform.Data;

namespace Wisdi.AppPlatform.Tasks;

public class TaskExecutionManager<TContext> : ITaskExecutionManager
    where TContext : PlatformDbContext
{
    private readonly IBackgroundTaskManagementService _taskService;
    private readonly ITaskHandlerRegistry _handlerRegistry;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TaskExecutionManager<TContext>> _logger;
    private readonly IDbContextFactory<TContext> _dbContextFactory;
    private readonly int _maxConcurrentTasks;
    private readonly int _leaseSeconds;
    private readonly ExecutionManagerIdentity _identity;
    private readonly TaskCheckGate _gate;

    public TaskExecutionManager(
        IBackgroundTaskManagementService taskService,
        ITaskHandlerRegistry handlerRegistry,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<TaskExecutionManager<TContext>> logger,
        IDbContextFactory<TContext> dbContextFactory,
        ExecutionManagerIdentity identity,
        TaskCheckGate gate)
    {
        _taskService = taskService;
        _handlerRegistry = handlerRegistry;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _identity = identity;
        _gate = gate;

        _maxConcurrentTasks = configuration.GetValue("backgroundTasks:maxConcurrentTasks", 4);
        _leaseSeconds = configuration.GetValue("backgroundTasks:leaseSeconds", 300);

        _logger.LogInformation("TaskExecutionManager initialized with ID {ExecutionManagerId}", _identity.Id);
    }

    public async Task CheckAndStartTasksAsync(CancellationToken ct = default)
    {
        using var gateRelease = await _gate.AcquireAsync(ct);

        try
        {
            using var context = await _dbContextFactory.CreateDbContextAsync(ct);

            var strategy = context.Database.CreateExecutionStrategy();
            var claimedTasks = await strategy.ExecuteAsync(async () =>
            {
                await ReclaimExpiredLeasesAsync(context, ct);
                var tasks = await ClaimAndUpdateTasksAsync(context, ct);
                return tasks;
            });

            foreach (var task in claimedTasks)
            {
                _ = Task.Run(async () => await ExecuteTaskAsync(task, ct))
                    .ContinueWith(t => _logger.LogError(t.Exception, "Unobserved failure executing task {TaskId}", task.Id),
                                  TaskContinuationOptions.OnlyOnFaulted);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking and starting tasks");
        }
    }

    private async Task ReclaimExpiredLeasesAsync(TContext context, CancellationToken ct = default)
    {
        var reclaimedCount = await context.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE [dbo].[BackgroundTasks]
               SET [Status]             = {(int)BackgroundTaskStatus.Resumed},
                   [ExecutionManagerId] = NULL,
                   [LeaseExpiresUtc]    = NULL,
                   [StatusMessage]      = 'Reclaimed after the owning worker stopped responding.',
                   [UpdatedDate]        = GETUTCDATE()
               WHERE [Status] IN ({(int)BackgroundTaskStatus.NotStarted}, {(int)BackgroundTaskStatus.Running})
                 AND [LeaseExpiresUtc] IS NOT NULL
                 AND [LeaseExpiresUtc] < GETUTCDATE()", ct);

        if (reclaimedCount > 0)
        {
            _logger.LogInformation("Reclaimed {Count} expired leases", reclaimedCount);
        }
    }

    private async Task<List<BackgroundTask>> ClaimAndUpdateTasksAsync(TContext context, CancellationToken ct = default)
    {
        FormattableString sql = $@"
DECLARE @ExecutionManagerId UNIQUEIDENTIFIER = {_identity.Id:D};
DECLARE @MaxConcurrentTasks INT = {_maxConcurrentTasks};
DECLARE @LeaseSeconds INT = {_leaseSeconds};
DECLARE @UtcNow DATETIME2 = GETUTCDATE();

-- Calculate running count and available slots
DECLARE @RunningCount INT = (
    SELECT COUNT(*)
    FROM [dbo].[BackgroundTasks]
    WHERE [ExecutionManagerId] = @ExecutionManagerId
    AND [Status] = {(int)BackgroundTaskStatus.Running}
);

DECLARE @AvailableSlots INT = @MaxConcurrentTasks - @RunningCount;

-- If no slots available, return empty set
IF @AvailableSlots <= 0
BEGIN
    SELECT TOP 0 * FROM [dbo].[BackgroundTasks];
END
ELSE
BEGIN
    -- Atomically update and return the claimed tasks in a single operation
    UPDATE [dbo].[BackgroundTasks]
    SET
        [Status] = {(int)BackgroundTaskStatus.NotStarted},
        [ExecutionManagerId] = @ExecutionManagerId,
        [UpdatedDate] = @UtcNow,
        [LeaseExpiresUtc] = DATEADD(second, @LeaseSeconds, @UtcNow),
        [StartedDate] = CASE WHEN [Status] = {(int)BackgroundTaskStatus.New} THEN @UtcNow ELSE [StartedDate] END
    OUTPUT INSERTED.*
    FROM (
        SELECT TOP (@AvailableSlots)
            [Id]
        FROM [dbo].[BackgroundTasks] WITH (UPDLOCK, ROWLOCK)
        WHERE [Status] IN ({(int)BackgroundTaskStatus.Resumed}, {(int)BackgroundTaskStatus.New})
        ORDER BY CASE WHEN [Status] = {(int)BackgroundTaskStatus.Resumed} THEN 0 ELSE 1 END,
                 [CreatedDate]
    ) AS TasksToClaim
    WHERE [dbo].[BackgroundTasks].[Id] = TasksToClaim.[Id];
END;";

        var claimedTasks = await context.BackgroundTasks
            .FromSqlInterpolated(sql)
            .ToListAsync(ct);

        if (claimedTasks.Count > 0)
        {
            _logger.LogInformation("Claimed {Count} tasks for ExecutionManager {ExecutionManagerId}", claimedTasks.Count, _identity.Id);
        }
        else
        {
            _logger.LogDebug("No tasks available to claim for ExecutionManager {ExecutionManagerId}", _identity.Id);
        }

        return claimedTasks;
    }

    internal async Task ExecuteTaskInternalAsync(BackgroundTask task, CancellationToken ct = default)
    {
        await ExecuteTaskAsync(task, ct);
    }

    private async Task ExecuteTaskAsync(BackgroundTask task, CancellationToken ct = default)
    {
        try
        {
            await _taskService.UpdateStatusAsync(task.Id, BackgroundTaskStatus.Running);

            var currentTask = await _taskService.GetTaskStatusAsync(task.Id);
            if (currentTask == null)
            {
                _logger.LogError("Task {TaskId} not found after transition to Running", task.Id);
                await _taskService.UpdateStatusAsync(task.Id, BackgroundTaskStatus.Failed, "Task ID not found");
                return;
            }

            _logger.LogInformation("Executing task {TaskId} of type {TaskType}", currentTask.Id, currentTask.TaskType);

            var handlerType = _handlerRegistry.GetHandlerType(currentTask.TaskType);
            if (handlerType == null)
            {
                _logger.LogError("Handler type not found for task type {TaskType}", currentTask.TaskType);
                await _taskService.UpdateStatusAsync(currentTask.Id, BackgroundTaskStatus.Failed, $"No handler found for task type '{currentTask.TaskType}'");
                return;
            }

            var handlerInterfaceType = handlerType.GetInterface("ITaskHandler`1");
            if (handlerInterfaceType == null)
            {
                _logger.LogError("Handler type {HandlerType} does not implement ITaskHandler<T>", handlerType.Name);
                await _taskService.UpdateStatusAsync(currentTask.Id, BackgroundTaskStatus.Failed, "Invalid handler type");
                return;
            }

            var taskDataType = handlerInterfaceType.GetGenericArguments()[0];

            object? taskData;
            try
            {
                taskData = JsonSerializer.Deserialize(currentTask.TaskData, taskDataType);
                if (taskData == null)
                {
                    _logger.LogError("Failed to deserialize task data for task {TaskId}", currentTask.Id);
                    await _taskService.UpdateStatusAsync(currentTask.Id, BackgroundTaskStatus.Failed, "Invalid task data");
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deserializing task data for task {TaskId}", currentTask.Id);
                await _taskService.UpdateStatusAsync(currentTask.Id, BackgroundTaskStatus.Failed, "Could not load task data");
                return;
            }

            await using var scope = _serviceProvider.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetService(handlerType);
            if (handler == null)
            {
                _logger.LogError("Handler {HandlerType} not registered in DI", handlerType.Name);
                await _taskService.UpdateStatusAsync(currentTask.Id, BackgroundTaskStatus.Failed, "No handler registered");
                return;
            }

            var context = new TaskHandlerContext(_taskService, currentTask.Id);

            var handleMethod = handlerInterfaceType.GetMethod("HandleAsync");
            if (handleMethod == null)
            {
                _logger.LogError("HandleAsync method not found on handler {HandlerType}", handlerType.Name);
                await _taskService.UpdateStatusAsync(currentTask.Id, BackgroundTaskStatus.Failed, "Invalid handler");
                return;
            }

            using var cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = cancellationTokenSource.Token;

            try
            {
                var handleTask = (Task)handleMethod.Invoke(handler, new[] { currentTask, taskData, context, cancellationToken })!;
                await handleTask;

                if (context.WasEndedWithoutCompleting())
                {
                    _logger.LogInformation("Task {TaskId} ended without completing as requested by handler", currentTask.Id);
                    return;
                }

                var updatedTask = await _taskService.GetTaskStatusAsync(currentTask.Id);
                if (updatedTask != null && updatedTask.Status != BackgroundTaskStatus.Completed)
                {
                    _logger.LogWarning("Task {TaskId} completed but status was not set to Completed, setting to Failed", currentTask.Id);
                    await _taskService.UpdateStatusAsync(currentTask.Id, BackgroundTaskStatus.Failed, "Task ended without completing");
                }
                else
                {
                    _logger.LogInformation("Task {TaskId} completed successfully", currentTask.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing task {TaskId}", currentTask.Id);
                await _taskService.UpdateStatusAsync(currentTask.Id, BackgroundTaskStatus.Failed, "An error occurred while executing the task");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in task execution for task {TaskId}", task.Id);
            try
            {
                await _taskService.UpdateStatusAsync(task.Id, BackgroundTaskStatus.Failed, "Unable to execute the task");
            }
            catch (Exception updateEx)
            {
                _logger.LogError(updateEx, "Error updating task status to Failed for task {TaskId}", task.Id);
            }
        }
    }
}
