using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using PS.AppPlatform.Data;
using PS.AppPlatform.Tasks;

namespace PS.AppPlatform.Endpoints;

/// <summary>
/// The read endpoints opportunistically run a claim-and-execute pass before returning, so
/// that a client polling for task status is what drives execution forward -- there is no
/// standing background loop and nothing keeps a Consumption-plan app "warm" between calls. A
/// task queued with nobody watching still runs eventually via the timer safety net
/// (TaskSchedulerFunctions), just not immediately.
/// </summary>
public sealed class BackgroundTaskEndpoints : IBackgroundTaskEndpoints
{
    private readonly IBackgroundTaskService _taskService;
    private readonly ITaskExecutionManager _executionManager;
    private readonly ITaskHandlerRegistry _taskHandlerRegistry;
    private readonly IDbContextFactory<PlatformDbContext> _dbContextFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BackgroundTaskEndpoints> _logger;

    public BackgroundTaskEndpoints(
        IBackgroundTaskService taskService,
        ITaskExecutionManager executionManager,
        ITaskHandlerRegistry taskHandlerRegistry,
        IDbContextFactory<PlatformDbContext> dbContextFactory,
        IConfiguration configuration,
        ILogger<BackgroundTaskEndpoints> logger)
    {
        _taskService = taskService;
        _executionManager = executionManager;
        _taskHandlerRegistry = taskHandlerRegistry;
        _dbContextFactory = dbContextFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IActionResult> GetNotificationsAsync(HttpRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Getting notification tasks");

        try
        {
            await _executionManager.CheckAndStartTasksAsync(ct);

            var notificationWindowHours = _configuration.GetValue<int>("backgroundTasks:notificationWindowHours", 12);
            var cutoffDate = DateTime.UtcNow.AddHours(-notificationWindowHours);

            using var context = await _dbContextFactory.CreateDbContextAsync(ct);

            var tasks = await context.BackgroundTasks
                .Where(t => t.RequiresNotification &&
                    ((t.Status != BackgroundTaskStatus.Completed && t.Status != BackgroundTaskStatus.Failed) ||
                     (t.CompletedDate.HasValue && t.CompletedDate.Value >= cutoffDate)))
                .OrderByDescending(t => t.CreatedDate)
                .ToListAsync(ct);

            var result = tasks.Select(MapToResponse).ToList();
            return new OkObjectResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting notification tasks");
            return new StatusCodeResult(500);
        }
    }

    public async Task<IActionResult> GetAllAsync(HttpRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Getting all tasks");

        try
        {
            await _executionManager.CheckAndStartTasksAsync(ct);

            var tasks = await _taskService.GetAllTasksAsync(ct);
            var result = tasks.Select(MapToResponse).ToList();
            return new OkObjectResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all tasks");
            return new StatusCodeResult(500);
        }
    }

    public async Task<IActionResult> GetByIdAsync(HttpRequest request, string id, CancellationToken ct = default)
    {
        _logger.LogInformation("Getting task status for {TaskId}", id);

        try
        {
            if (!Guid.TryParse(id, out var taskId))
            {
                return new BadRequestObjectResult(new { error = "Invalid task ID format" });
            }

            await _executionManager.CheckAndStartTasksAsync(ct);

            var task = await _taskService.GetTaskStatusAsync(taskId, ct);

            if (task == null)
            {
                return new NotFoundObjectResult(new { error = "Task not found" });
            }

            return new OkObjectResult(MapToResponse(task));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting task status");
            return new StatusCodeResult(500);
        }
    }

    public async Task<IActionResult> CreateAsync(HttpRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating new task");

        try
        {
            string body;
            using (var reader = new StreamReader(request.Body))
            {
                body = await reader.ReadToEndAsync(ct);
            }

            if (string.IsNullOrEmpty(body))
            {
                return new BadRequestObjectResult(new { error = "Request body is required" });
            }

            var createRequest = JsonSerializer.Deserialize<CreateTaskRequest>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (createRequest == null)
            {
                return new BadRequestObjectResult(new { error = "Invalid request format" });
            }

            // Validate taskType
            if (string.IsNullOrWhiteSpace(createRequest.TaskType))
            {
                return new BadRequestObjectResult(new { error = "taskType is required" });
            }

            // Check if task type is registered
            if (!_taskHandlerRegistry.IsRegistered(createRequest.TaskType))
            {
                return new BadRequestObjectResult(new { error = $"Unknown taskType: {createRequest.TaskType}" });
            }

            var taskId = await _taskService.CreateTaskAsync(
                createRequest.TaskType,
                createRequest.TaskData,
                createRequest.Description ?? "",
                createRequest.RequiresNotification,
                ct);

            return new CreatedResult("", new { taskId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating task");
            return new StatusCodeResult(500);
        }
    }

    public async Task<IActionResult> CheckAsync(HttpRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Manually triggering task check");

        try
        {
            await _executionManager.CheckAndStartTasksAsync(ct);
            return new OkObjectResult(new { message = "Task check completed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking tasks");
            return new StatusCodeResult(500);
        }
    }

    private static BackgroundTaskResponse MapToResponse(BackgroundTask task)
    {
        return new BackgroundTaskResponse(
            id: task.Id,
            taskType: task.TaskType,
            status: task.Status.ToString(),
            statusMessage: task.StatusMessage,
            completionPercentage: task.CompletionPercentage,
            description: task.Description,
            requiresNotification: task.RequiresNotification,
            createdDate: task.CreatedDate,
            updatedDate: task.UpdatedDate,
            startedDate: task.StartedDate,
            completedDate: task.CompletedDate);
    }

    private class CreateTaskRequest
    {
        public string? TaskType { get; set; }
        public JsonElement? TaskData { get; set; }
        public string? Description { get; set; }
        public bool RequiresNotification { get; set; }
    }
}

