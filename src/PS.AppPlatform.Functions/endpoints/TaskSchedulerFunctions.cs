using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using PS.AppPlatform.Tasks;

namespace PS.AppPlatform.Generated;

public class TaskSchedulerFunctions(ITaskExecutionManager manager)
{
    /// <summary>
    /// Safety net for queued tasks. The primary driver is opportunistic: the task read
    /// endpoints (BackgroundTaskEndpoints) run a claim-and-execute pass before responding, so
    /// a client polling for status drives execution. This timer covers everything that has no
    /// one polling -- a task created from the --migrate CLI path, or one nobody has checked on
    /// since the app scaled to another instance.
    /// </summary>
    [Function("ScheduledTaskCheck")]
    public async Task Run([TimerTrigger("%backgroundTasks:checkSchedule%")] TimerInfo timer)
    {
        await manager.CheckAndStartTasksAsync();
    }
}

