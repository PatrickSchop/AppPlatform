namespace Wisdi.AppPlatform.Tasks;

public interface ITaskExecutionManager
{
    Task CheckAndStartTasksAsync(CancellationToken ct = default);
}
