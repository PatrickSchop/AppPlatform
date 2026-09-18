namespace Wisdi.AppPlatform.Tasks;

/// <summary>
/// Handles the execution of a background task of type T.
/// </summary>
/// <remarks>
/// <para>
/// Handlers must call context.UpdateProgressAsync() or context.CompleteAsync() periodically if they
/// run longer than the configured leaseSeconds (default 300 seconds). Tasks that do not report progress
/// will have their lease expire and be reclaimed as orphaned, resulting in duplicate execution.
/// This is a deliberate trade-off: the platform cannot distinguish between a handler that is hung
/// and one that is simply taking a long time, so it reclaims expired leases rather than leave tasks
/// stuck permanently.
/// </para>
/// <para>
/// Handlers must explicitly complete the task by calling context.CompleteAsync() or leave it paused by
/// calling context.EndWithoutCompletingAsync(). If a handler returns without calling either,
/// the task is marked Failed automatically.
/// </para>
/// </remarks>
public interface ITaskHandler<T>
{
    Task HandleAsync(BackgroundTask task, T taskData, TaskHandlerContext context, CancellationToken cancellationToken);
}
