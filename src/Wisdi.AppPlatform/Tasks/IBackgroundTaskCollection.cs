namespace Wisdi.AppPlatform.Tasks;

/// <summary>
/// Interface for collecting background task handler registrations.
/// </summary>
public interface IBackgroundTaskCollection
{
    /// <summary>
    /// Adds a background task handler registration.
    /// </summary>
    /// <param name="taskName">The type identifier of the task</param>
    /// <param name="handlerType">The type of the handler that will process the task</param>
    void AddBackgroundTask(string taskName, Type handlerType);

    void AddBackgroundTask<T>(string taskName) where T : class;
}
