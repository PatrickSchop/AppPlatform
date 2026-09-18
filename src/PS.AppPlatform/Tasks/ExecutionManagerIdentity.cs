namespace PS.AppPlatform.Tasks;

/// <summary>
/// The host-lifetime identity of this worker instance. Registered as a singleton so that
/// every scoped TaskExecutionManager in the process shares one id.
/// </summary>
/// <remarks>
/// This exists because the concurrency cap is enforced by counting rows in
/// BackgroundTasks where ExecutionManagerId equals this id. If the id were generated
/// per scope (as it was before extraction), the count would always be zero and
/// maxConcurrentTasks would cap nothing.
/// </remarks>
public sealed class ExecutionManagerIdentity
{
    public Guid Id { get; } = Guid.NewGuid();
}

