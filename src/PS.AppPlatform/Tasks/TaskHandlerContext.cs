namespace PS.AppPlatform.Tasks;

public class TaskHandlerContext
{
    private readonly IBackgroundTaskManagementService _taskService;
    private readonly Guid _taskId;
    private bool _endedWithoutCompleting = false;

    public TaskHandlerContext(IBackgroundTaskManagementService taskService, Guid taskId, Guid? tenantId = null)
    {
        _taskService = taskService;
        _taskId = taskId;
        TenantId = tenantId;
    }

    /// <summary>The tenant the task was created for, or null for a system task.</summary>
    public Guid? TenantId { get; }

    public Guid GetTaskId()
    {
        return _taskId;
    }

    /// <summary>
    /// Updates the progress percentage of the task and renews the lease to prevent the task
    /// from being reclaimed as orphaned. A long-running handler that never reports progress
    /// will have its task reclaimed once the lease expires; such handlers should call this
    /// method periodically.
    /// </summary>
    public async Task UpdateProgressAsync(int percentage)
    {
        percentage = Math.Clamp(percentage, 0, 100);
        await _taskService.UpdateProgressAsync(_taskId, percentage);
        await _taskService.RenewLeaseAsync(_taskId);
    }

    public async Task FailAsync(string reason)
    {
        await _taskService.UpdateStatusAsync(_taskId, BackgroundTaskStatus.Failed, reason);
    }

    public async Task CompleteAsync()
    {
        await _taskService.UpdateStatusAsync(_taskId, BackgroundTaskStatus.Completed);
    }

    public async Task EndWithoutCompletingAsync()
    {
        _endedWithoutCompleting = true;
        await _taskService.UpdateStatusAsync(_taskId, BackgroundTaskStatus.Paused);
    }

    public bool WasEndedWithoutCompleting()
    {
        return _endedWithoutCompleting;
    }
}

