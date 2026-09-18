namespace PS.AppPlatform.Tasks;

/// <summary>
/// Public interface for starting tasks and monitoring task status.
/// Available to regular application code.
/// </summary>
public interface IBackgroundTaskService
{
    /// <summary>
    /// Creates a new background task.
    /// </summary>
    Task<Guid> CreateTaskAsync<T>(string taskType, T taskData, string description, bool requiresNotification, CancellationToken ct = default);

    /// <summary>
    /// Resumes a paused task.
    /// </summary>
    Task<bool> ResumeTaskAsync(Guid taskId, CancellationToken ct = default);

    /// <summary>
    /// Gets the status of a specific task by ID.
    /// </summary>
    Task<BackgroundTask?> GetTaskStatusAsync(Guid taskId, CancellationToken ct = default);

    /// <summary>
    /// Gets all tasks ordered by creation date descending.
    /// </summary>
    Task<List<BackgroundTask>> GetAllTasksAsync(CancellationToken ct = default);
}

