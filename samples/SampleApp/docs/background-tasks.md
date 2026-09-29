# Background Tasks

Background tasks allow long-running operations to be queued and processed asynchronously without blocking the API.

## Defining a Task Handler

Create a class that implements `ITaskHandler<TData>`:

```csharp
public class MyTaskHandler(IDbContextFactory<AppDbContext> factory, ILogger<MyTaskHandler> logger)
    : ITaskHandler<MyTaskData>
{
    public async Task HandleAsync(BackgroundTask task, MyTaskData data,
                                  TaskHandlerContext context, CancellationToken ct)
    {
        // Long-running work here
        await context.UpdateProgressAsync(progress);
        await context.CompleteAsync();
    }
}

public sealed record MyTaskData(string Parameter);
```

## Registering a Task Handler

In your `ServiceBuilder`, register the handler:

```csharp
public override void RegisterBackgroundTasks(IBackgroundTaskCollection tasks)
    => tasks.AddBackgroundTask<MyTaskHandler>("mytask");
```

The first argument is the task name used when creating tasks.

## Creating a Task

From an endpoint, create a task via `IBackgroundTaskService`:

```csharp
var taskId = await service.CreateTaskAsync("mytask", new MyTaskData("value"),
    "Display title", requiresNotification: true, ct);
```

## The Lease Obligation

⚠️ **Important:** If a handler takes longer than `backgroundTasks:leaseSeconds` (default 300 seconds)
to complete, you **must** call `context.UpdateProgressAsync()` periodically. This renews the lease
and prevents the platform from reclaiming your task as "orphaned" and re-running it.

Example:

```csharp
for (var i = 0; i < items.Count; i++)
{
    ProcessItem(items[i]);
    await context.UpdateProgressAsync((i + 1) * 100 / items.Count); // Renew lease
}
```

Without this, a handler that takes longer than the lease will be killed mid-work and re-executed,
causing duplicate processing.

## How a task actually runs

There is no standing worker process. A queued task (`New`/`Resumed`) is claimed and executed
when something invokes a check:

- **Polling drives it.** `GET /api/tasks`, `GET /api/tasks/{id}` and
  `GET /api/tasks/notifications` each run a claim-and-execute pass before responding. A
  frontend that polls for status is, by that same call, what makes the task progress.
- **A timer is the safety net**, not the primary path (`ScheduledTaskCheck`,
  `backgroundTasks:checkSchedule`, default every 5 minutes). It covers tasks nobody is
  watching -- created from `--migrate`, or abandoned after the app scaled to another instance.
- **`POST /api/tasks/check`** exists for a caller that wants to nudge processing without
  reading task state (a script, for example).

Nothing here keeps the app "running" between invocations; each pass only does work while an
HTTP call or the timer is already executing, so a Consumption-plan app still scales to zero
once nobody is polling.

## Polling for Progress

From the frontend, poll `GET /api/tasks/notifications` to receive tasks that have progress updates:

```javascript
const tasks = await (await fetch('/api/tasks/notifications')).json();
const myTask = tasks.find(t => t.id === taskId);
console.log(myTask.progressPercent, myTask.status);
```

Task statuses:
- `0` = New
- `1` = Resumed
- `2` = Completed
- `32` = Failed

The platform automatically handles cancellation via HTTP context abort tokens.
