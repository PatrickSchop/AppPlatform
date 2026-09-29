namespace PS.AppPlatform.Tasks;

/// <summary>
/// Internal interface for managing task state.
/// Available only to TaskExecutionManager and related classes. These writes go through the
/// unscoped factory: the execution manager calls them with no request tenant of its own, and
/// the tenant/user were already stamped on create.
/// </summary>
public interface IBackgroundTaskManagementService : IBackgroundTaskService
{
    /// <summary>
    /// Updates the progress percentage of a task.
    /// </summary>
    Task UpdateProgressAsync(Guid taskId, int percentage);

    /// <summary>
    /// Updates the status of a task.
    /// </summary>
    Task UpdateStatusAsync(Guid taskId, BackgroundTaskStatus status, string reason);

    Task UpdateStatusAsync(Guid taskId, BackgroundTaskStatus status);

    /// <summary>
    /// Extends the lease on a running task so it is not reclaimed as orphaned.
    /// </summary>
    Task RenewLeaseAsync(Guid taskId, CancellationToken ct = default);
}

