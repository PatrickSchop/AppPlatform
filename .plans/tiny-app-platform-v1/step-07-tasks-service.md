# Step 07 — Background task service

**Phase:** 1 — Core engine
**Depends on:** Step 06
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Port `BackgroundTaskService` generically over `TContext`, and fix analysis §7.1 — the
execution manager id must be **host-lifetime**, not per-request.

## The problem being solved (analysis §7.1)

In the source, `TaskExecutionManager` is registered `Scoped` (a reasonable fix for captured
`DbContext`s) but generates `_executionManagerId = Guid.NewGuid()` in its **constructor**
(`TaskExecutionManager.cs:43`), and the claim SQL counts running tasks *for that id only*.
So every HTTP request gets a fresh id, the running count is always zero,
`maxConcurrentTasks` caps nothing globally, and the `SemaphoreSlim` — also per-instance —
serialises nothing.

The fix is to separate the two lifetimes: the **identity** and the **gate** are singletons;
the **manager** stays scoped so its `DbContext` is not captured.

This step builds the two singletons and the service. Step 08 builds the manager that uses
them.

## Tasks

### 1. `Tasks/ExecutionManagerIdentity.cs`

```csharp
namespace Wisdi.AppPlatform.Tasks;

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
```

### 2. `Tasks/TaskCheckGate.cs`

```csharp
/// <summary>
/// Process-wide gate serialising CheckAndStartTasksAsync. Singleton for the same reason
/// as ExecutionManagerIdentity: a per-scope semaphore serialises nothing.
/// </summary>
public sealed class TaskCheckGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public Task<IDisposable> AcquireAsync(CancellationToken ct = default);
    public void Dispose();
}
```

`AcquireAsync` waits on the semaphore and returns a disposable that releases it, so callers
write `using var _ = await gate.AcquireAsync();`. Release exactly once even if the returned
disposable is disposed twice — guard with an `int` and `Interlocked.Exchange`.

### 3. `Tasks/BackgroundTaskService.cs`

Port `App\BackgroundTasks\BackgroundTaskService.cs`, generic over the context:

```csharp
public class BackgroundTaskService<TContext> : IBackgroundTaskManagementService
    where TContext : PlatformDbContext
{
    public BackgroundTaskService(
        IDbContextFactory<TContext> dbContextFactory,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<BackgroundTaskService<TContext>> logger);
}
```

Port these members with **no behaviour change**:
`CreateTaskAsync<T>`, `GetTaskStatusAsync`, `GetAllTasksAsync`, `ResumeTaskAsync`,
`UpdateProgressAsync`, both `UpdateStatusAsync` overloads.

Keep the flag logic in `UpdateStatusAsync` exactly as written — it is subtle and correct:

```csharp
if ((status & BackgroundTaskStatus.ExecutingFlag) == 0)
    task.ExecutionManagerId = null;
if ((status & BackgroundTaskStatus.CompletedFlag) > 0)
{
    task.CompletedDate = task.UpdatedDate;
    task.CompletionPercentage = 100;
}
```

**Add** to that block, for Step 08:

```csharp
if ((status & BackgroundTaskStatus.ExecutingFlag) == 0)
    task.LeaseExpiresUtc = null;   // alongside clearing ExecutionManagerId
```

### 4. Changes to make while porting

**a. `apiBaseUrl` must not be required at construction.**

The source throws from the constructor when `backgroundTasks:apiBaseUrl` is missing. That
makes the service unconstructable in tests, in the `--migrate` CLI path, and in any app that
would rather rely on the Step 13 timer trigger. Read it into a nullable field instead and
have `TriggerTaskCheck` skip with a single warning when it is null:

```csharp
if (_apiBaseUrl is null)
{
    _logger.LogDebug("backgroundTasks:apiBaseUrl is not configured; relying on the timer trigger to start queued tasks.");
    return;
}
```

**b. `RenewLeaseAsync`** — new, required by the Step 06 interface:

```csharp
public async Task RenewLeaseAsync(Guid taskId, CancellationToken ct = default)
{
    using var context = await _dbContextFactory.CreateDbContextAsync(ct);
    var seconds = _leaseSeconds;
    await context.Database.ExecuteSqlInterpolatedAsync(
        $@"UPDATE [dbo].[BackgroundTasks]
           SET [LeaseExpiresUtc] = DATEADD(second, {seconds}, GETUTCDATE()),
               [UpdatedDate] = GETUTCDATE()
           WHERE [Id] = {taskId}", ct);
}
```

A direct `UPDATE` rather than load-modify-save, because this runs on every progress report
and must be cheap and non-conflicting.

`_leaseSeconds` comes from `backgroundTasks:leaseSeconds`, default `300`.

**c. `TriggerTaskCheckAsync` keeps its fire-and-forget shape** — including the 5-second
timeout and swallowed exceptions. It is a best-effort optimisation, and Step 13's timer
trigger is the correctness backstop (analysis §7.3). Rename it to `TriggerTaskCheck`
(no `Async` suffix) since it returns `void` and awaits nothing; the source name is
misleading.

**d. `CreateTaskAsync` sets the new columns:** `ExecutionManagerId = null`,
`LeaseExpiresUtc = null`. Explicit is better than relying on defaults here.

### 5. `Tasks/TasksServiceBuilder.cs`

The platform's task module. It cannot register the generic service itself — it does not know
`TContext` — so split the registration:

```csharp
public sealed class TasksServiceBuilder : ServiceBuilder
{
    public override void BuildServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ExecutionManagerIdentity>();
        services.AddSingleton<TaskCheckGate>();
    }
}
```

and add the context-dependent half to `AddPlatformData<TContext>` in
`Data/PlatformDataExtensions.cs`:

```csharp
services.AddScoped<BackgroundTaskService<TContext>>();
services.AddScoped<IBackgroundTaskService>(sp => sp.GetRequiredService<BackgroundTaskService<TContext>>());
services.AddScoped<IBackgroundTaskManagementService>(sp => sp.GetRequiredService<BackgroundTaskService<TContext>>());
```

All three resolve the **same** scoped instance, as the source intends.

## Tests to add

`tests/Wisdi.AppPlatform.Tests/BackgroundTaskServiceTests.cs`, using the EF in-memory
provider and a `TestDbContext : PlatformDbContext`:

1. `CreateTaskAsync` persists a row with `Status = New`, `CompletionPercentage = 0`,
   a serialised `TaskData`, and null `ExecutionManagerId` / `LeaseExpiresUtc`.
2. `UpdateStatusAsync(id, Completed)` sets `CompletedDate`, `CompletionPercentage = 100`,
   and clears `ExecutionManagerId` **and** `LeaseExpiresUtc`.
3. `UpdateStatusAsync(id, Running)` does **not** clear `ExecutionManagerId`.
4. `UpdateStatusAsync(id, Failed, "boom")` sets `StatusMessage` to `"boom"` and, because
   `Failed` carries `CompletedFlag`, sets `CompletedDate`.
5. `ResumeTaskAsync` on a `Paused` task sets `Resumed` and nulls `ExecutionManagerId`,
   `StartedDate`, `CompletedDate`; on a `Running` task it returns `false` and changes nothing.
6. Constructing the service with **no** `backgroundTasks:apiBaseUrl` does not throw.
7. `ExecutionManagerIdentity` resolved twice from one provider is the same instance with
   the same `Id`; resolved from two different **scopes** it is still the same instance.
   This is the §7.1 regression test — name it so that is obvious, e.g.
   `ExecutionManagerId_is_stable_across_scopes`.

`RenewLeaseAsync` uses raw SQL and cannot run on the in-memory provider — cover it at
Step 15 instead, and note that in a comment.

## Verification

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release
dotnet test
```

**Expected:** zero warnings, zero errors, all tests pass.

## Done when

- [ ] Build clean, all tests pass
- [ ] `ExecutionManagerIdentity` and `TaskCheckGate` are singletons; the service stays scoped
- [ ] `ExecutionManagerId_is_stable_across_scopes` passes — §7.1 is fixed and guarded
- [ ] A missing `apiBaseUrl` no longer throws at construction
- [ ] `LeaseExpiresUtc` is cleared whenever `ExecutionManagerId` is

## Commit

```powershell
git add -A
git commit -m "Step 07: generic BackgroundTaskService; execution manager id and check gate are host-lifetime (fixes 7.1)"
```
