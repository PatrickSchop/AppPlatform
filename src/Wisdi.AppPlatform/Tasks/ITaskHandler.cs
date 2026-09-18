namespace Wisdi.AppPlatform.Tasks;

public interface ITaskHandler<T>
{
    Task HandleAsync(BackgroundTask task, T taskData, TaskHandlerContext context, CancellationToken cancellationToken);
}
