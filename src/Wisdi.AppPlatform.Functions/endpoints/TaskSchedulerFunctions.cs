using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Wisdi.AppPlatform.Tasks;

namespace Wisdi.AppPlatform.Generated;

public class TaskSchedulerFunctions(ITaskExecutionManager manager)
{
    /// <summary>
    /// Safety net for queued tasks. The primary trigger is a fire-and-forget self-POST from
    /// BackgroundTaskService, which silently does nothing if apiBaseUrl is wrong, if the app
    /// scaled out to another instance, or if the task was created from the --migrate CLI path.
    /// </summary>
    [Function("ScheduledTaskCheck")]
    public async Task Run([TimerTrigger("%backgroundTasks:checkSchedule%")] TimerInfo timer)
    {
        await manager.CheckAndStartTasksAsync();
    }
}
