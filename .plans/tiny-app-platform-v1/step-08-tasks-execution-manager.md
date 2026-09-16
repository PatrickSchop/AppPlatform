# Step 08 — Task execution manager

**Phase:** 1 — Core engine
**Depends on:** Step 07
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Port `TaskExecutionManager` — the SQL claim queue and reflection dispatch — using the
singletons from Step 07, and add orphan lease recovery (analysis §7.2).

## Reference material (read-only)

`C:\Dev\StockAnalysis\App\BackgroundTasks\TaskExecutionManager.cs` — read it in full before
starting. The claim SQL at lines 102-145 and the dispatch logic in `ExecuteTaskAsync` are
both subtle and should be ported with care rather than rewritten.

## Tasks

### 1. `Tasks/TaskExecutionManager.cs`

```csharp
public class TaskExecutionManager<TContext> : ITaskExecutionManager
    where TContext : PlatformDbContext
{
    public TaskExecutionManager(
        IBackgroundTaskManagementService taskService,
        ITaskHandlerRegistry handlerRegistry,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<TaskExecutionManager<TContext>> logger,
        IDbContextFactory<TContext> dbContextFactory,
        ExecutionManagerIdentity identity,
        TaskCheckGate gate);

    public Task CheckAndStartTasksAsync(CancellationToken ct = default);
}
```

Also add the non-generic interface, so endpoints and the timer shim are not generic:

```csharp
public interface ITaskExecutionManager
{
    Task CheckAndStartTasksAsync(CancellationToken ct = default);
}
```

**Differences from the source, all deliberate:**

| Source | Here | Why |
|---|---|---|
| `_executionManagerId = Guid.NewGuid()` in ctor | `identity.Id` | §7.1 — the id must be host-lifetime |
| `_checkLock = new SemaphoreSlim(1,1)` in ctor | injected `TaskCheckGate` | §7.1 — a per-scope semaphore serialises nothing |
| `IDbContextFactory<AppDbContext>` | `IDbContextFactory<TContext>` | §2.2 |
| no lease | reclaim + set `LeaseExpiresUtc` | §7.2 |

Keep `maxConcurrentTasks` from `backgroundTasks:maxConcurrentTasks`, default `4`.
Add `leaseSeconds` from `backgroundTasks:leaseSeconds`, default `300`.

Keep the startup log line reporting the manager id — with the §7.1 fix it should now appear
**once per process**, not once per request, which is itself a useful signal.

### 2. Orphan recovery (§7.2)

`CheckAndStartTasksAsync` acquires the gate, then does **reclaim first, claim second**,
inside the existing `CreateExecutionStrategy()` wrapper.

Add a `ReclaimExpiredLeasesAsync(TContext context)`:

```sql
UPDATE [dbo].[BackgroundTasks]
SET [Status]             = @ResumedStatus,
    [ExecutionManagerId] = NULL,
    [LeaseExpiresUtc]    = NULL,
    [StatusMessage]      = 'Reclaimed after the owning worker stopped responding.',
    [UpdatedDate]        = GETUTCDATE()
WHERE [Status] IN (@NotStartedStatus, @RunningStatus)
  AND [LeaseExpiresUtc] IS NOT NULL
  AND [LeaseExpiresUtc] < GETUTCDATE();
```

Use `ExecuteSqlInterpolatedAsync` with the `(int)BackgroundTaskStatus.X` values interpolated,
matching the style already used by the claim SQL. Log at Information with the affected row
count when it is greater than zero, and say nothing when it is zero — this runs often.

Reclaimed tasks go to `Resumed` rather than `New`, so the claim SQL prioritises them ahead
of fresh work. That is the existing ordering and it is the behaviour you want here.

**Note the deliberate limitation:** a task whose handler never reports progress will have
its lease expire and be reclaimed while still running, producing a duplicate execution.
Document this on `ITaskHandler<T>` and in `docs/background-tasks.md` (Step 14): handlers
that run longer than `leaseSeconds` **must** call `context.UpdateProgressAsync` periodically.
`leaseSeconds` defaulting to 300 is generous enough that this is rare, and the alternative —
never reclaiming — is the bug being fixed.

### 3. The claim SQL

Port `ClaimAndUpdateTasksAsync` almost verbatim. It is a single atomic
count-slots / `UPDATE ... OUTPUT INSERTED.*` statement with `WITH (UPDLOCK, ROWLOCK)` and
the `Resumed`-before-`New` ordering. Preserve all of that.

**One addition** — set the lease when claiming:

```sql
SET [Status]             = {(int)BackgroundTaskStatus.NotStarted},
    [ExecutionManagerId] = @ExecutionManagerId,
    [UpdatedDate]        = @UtcNow,
    [LeaseExpiresUtc]    = DATEADD(second, @LeaseSeconds, @UtcNow),
    [StartedDate]        = CASE WHEN [Status] = {(int)BackgroundTaskStatus.New} THEN @UtcNow ELSE [StartedDate] END
```

`[dbo].[BackgroundTasks]` stays hard-coded. Per analysis §2.2 this is fine under the
per-app-database model chosen in §5; schema parameterisation is explicitly out of scope.

### 4. `ExecuteTaskAsync`

Port verbatim. It is the reflection dispatch:
`GetHandlerType` → `GetInterface("ITaskHandler`1")` → generic argument → `JsonSerializer.Deserialize`
→ resolve handler from DI → build `TaskHandlerContext` → invoke `HandleAsync` → inspect the
final status.

Two required changes and one fix:

**a. Resolve the handler from a fresh scope.** The source calls
`_serviceProvider.GetService(handlerType)` on a provider captured by a *scoped* manager,
while the task runs fire-and-forget on a thread pool thread that outlives the request. That
is a disposed-scope bug waiting to happen. Instead:

```csharp
await using var scope = _serviceProvider.CreateAsyncScope();
var handler = scope.ServiceProvider.GetService(handlerType);
```

Register `IServiceScopeFactory` usage rather than capturing `IServiceProvider` if that reads
more clearly; either is fine as long as the handler gets its own scope for the whole
execution.

**b. Fix the three typos in the status messages** — `"Unable to execute the tasl"`,
`"An error occured"`, and the misleading `// Transition back to Paused on failure` comment
above a `Failed` transition. These strings reach the UI.

**c. Keep the fire-and-forget `_ = Task.Run(...)`** for starting each claimed task. It is how
the consumption plan gets parallelism without a hosted service. But add the missing
continuation so an exception escaping `ExecuteTaskAsync` itself cannot be swallowed silently:

```csharp
_ = Task.Run(async () => await ExecuteTaskAsync(task))
        .ContinueWith(t => _logger.LogError(t.Exception, "Unobserved failure executing task {TaskId}", task.Id),
                      TaskContinuationOptions.OnlyOnFaulted);
```

### 5. Registration

Add to `AddPlatformData<TContext>` (it needs `TContext`):

```csharp
services.AddScoped<TaskExecutionManager<TContext>>();
services.AddScoped<ITaskExecutionManager>(sp => sp.GetRequiredService<TaskExecutionManager<TContext>>());
```

## Tests to add

`tests/Wisdi.AppPlatform.Tests/TaskExecutionManagerTests.cs`.

The claim and reclaim SQL are raw T-SQL and cannot run on the in-memory provider, so this
step's unit tests cover the dispatch half only; the SQL is covered at Step 15.

1. A registered handler is invoked with correctly deserialised task data.
2. An unknown `TaskType` sets the task `Failed` with a message mentioning the task type.
3. A handler that throws leaves the task `Failed`, and the exception is logged, not rethrown.
4. A handler that returns without completing leaves the task `Failed` with
   `"Task ended without completing"`.
5. A handler calling `context.EndWithoutCompletingAsync()` leaves the task `Paused` and is
   **not** marked `Failed`.
6. A handler calling `context.CompleteAsync()` leaves the task `Completed`.
7. The handler is resolved from a scope that is still alive during `HandleAsync` — assert a
   scoped dependency injected into the handler is not disposed when `HandleAsync` runs.

Structure the manager so these are reachable: extract dispatch into an internal method that
takes a `BackgroundTask` directly, so tests bypass the claim SQL. Mark it `internal` and add
`[assembly: InternalsVisibleTo("Wisdi.AppPlatform.Tests")]` in the platform project.

## Verification

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release
dotnet test
```

**Expected:** zero warnings, zero errors, all tests pass.

## Done when

- [ ] Build clean, all tests pass
- [ ] The manager takes `ExecutionManagerIdentity` and `TaskCheckGate`; it creates neither
- [ ] Expired leases are reclaimed to `Resumed` before each claim attempt
- [ ] Claiming sets `LeaseExpiresUtc`
- [ ] Handlers resolve from their own scope, not the captured request scope
- [ ] `ITaskExecutionManager` is non-generic so endpoints can depend on it
- [ ] The lease/progress obligation is documented on `ITaskHandler<T>`

## Commit

```powershell
git add -A
git commit -m "Step 08: task execution manager with shared identity, claim SQL and orphan lease recovery (fixes 7.2)"
```
