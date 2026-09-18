namespace PS.AppPlatform.Tasks;

public interface ITaskExecutionManager
{
    Task CheckAndStartTasksAsync(CancellationToken ct = default);
}

