# Step 06 â€” Background task contracts

**Phase:** 1 â€” Core engine
**Depends on:** Step 05
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Port the pure-contract half of the background task system: the entity, the status enum, the
service interfaces, the handler interface, the registry and the handler context. No
execution logic â€” that is Steps 07 and 08.

This step also fills in the two stubs left behind in Steps 03 and 04
(`IBackgroundTaskCollection`, `BackgroundTask`).

## Reference material (read-only)

All under `C:\Dev\StockAnalysis\App\`:
`Models\BackgroundTask.cs`, `BackgroundTasks\BackgroundTaskStatus.cs`,
`BackgroundTasks\ITaskHandler.cs`, `BackgroundTasks\ITaskHandlerRegistry.cs`,
`BackgroundTasks\TaskHandlerRegistry.cs`, `BackgroundTasks\IBackgroundTaskCollection.cs`,
`BackgroundTasks\BackgroundTaskCollection.cs`, `BackgroundTasks\TaskHandlerContext.cs`,
`BackgroundTasks\IBackgroundTaskService.cs`, `BackgroundTasks\IBackgroundTaskManagementService.cs`.

**Do not port** `BackgroundTasks\TaskNotificationHub.cs`. It is a dead SignalR stub
(analysis Â§1).

## Tasks

All files go in `src/PS.AppPlatform/Tasks/`, namespace `PS.AppPlatform.Tasks`.

### 1. `BackgroundTaskStatus.cs`

Port verbatim â€” the flag values are load-bearing in the claim SQL and in
`UpdateStatusAsync`, so do not renumber them.

```csharp
public enum BackgroundTaskStatus
{
    New = 1,
    Resumed = 2,
    NotStarted = 8,
    Running = 9,
    Paused = 16,
    Completed = 32,
    Failed = 33,

    ExecutingFlag = 8,
    CompletedFlag = 32
}
```

Add an XML comment explaining the scheme, because it is not obvious: `ExecutingFlag` and
`CompletedFlag` are bitmasks tested with `&` against the concrete values, which is why
`Running = 9` (`8|1`) and `Failed = 33` (`32|1`).

### 2. `BackgroundTask.cs`

Replace the Step 04 stub. Port from `App\Models\BackgroundTask.cs`, deriving from
`PS.AppPlatform.Data.Entity`, and **add one property** for Step 08:

```csharp
public DateTime? LeaseExpiresUtc { get; set; }
```

Everything else is unchanged: `TaskType`, `Status`, `StatusMessage`, `CompletionPercentage`,
`Description`, `RequiresNotification`, `TaskData`, `CreatedDate`, `UpdatedDate`,
`StartedDate`, `CompletedDate`, `ExecutionManagerId`.

### 3. `ITaskHandler.cs`

```csharp
public interface ITaskHandler<T>
{
    Task HandleAsync(BackgroundTask task, T taskData, TaskHandlerContext context, CancellationToken cancellationToken);
}
```

Unchanged from the source apart from the namespace.

### 4. `ITaskHandlerRegistry.cs` / `TaskHandlerRegistry.cs`

Port verbatim. `TaskHandlerRegistry` is a `Dictionary<string, Type>` with
`RegisterHandler<T>(name)`, `RegisterHandler(name, type)` and `GetHandlerType(name)`.

**One change:** construct the dictionary with `StringComparer.OrdinalIgnoreCase`. Task type
names come from the database as free text; a casing mismatch there currently produces
"no handler found" and a failed task, which is a miserable thing to debug.

### 5. `IBackgroundTaskCollection.cs` / `BackgroundTaskCollection.cs`

Replace the Step 03 placeholder. Port both. `BackgroundTaskCollection` must become
**`public`** (it is `internal` in the source) because `PlatformHostBuilder.AddPlatform`
hands it to app modules.

`RegisterBackgroundTaskHandlers(IServiceCollection)` keeps its source behaviour: register
each handler type as transient, then register a single `ITaskHandlerRegistry` singleton
built from all registrations.

**One change:** in `AddBackgroundTask`, throw `InvalidOperationException` if `taskName` is
already registered to a *different* handler type. The source silently overwrites via
`_handlers[taskName] = handlerType`, so two modules claiming the same name is a bug that
only shows up as the wrong handler running.

### 6. `IBackgroundTaskService.cs` / `IBackgroundTaskManagementService.cs`

Port both verbatim. Keep the two-interface split â€” the narrow `IBackgroundTaskService` for
app code, the wider `IBackgroundTaskManagementService : IBackgroundTaskService` adding
`UpdateProgressAsync` and the two `UpdateStatusAsync` overloads for the execution machinery.

Add one method to `IBackgroundTaskManagementService` for Step 08:

```csharp
/// <summary>Extends the lease on a running task so it is not reclaimed as orphaned.</summary>
Task RenewLeaseAsync(Guid taskId, CancellationToken ct = default);
```

### 7. `TaskHandlerContext.cs`

Port from the source and **remove the stray `using Grpc.Core;`** â€” it is an unused import
that would drag a package reference in for nothing.

Keep `GetTaskId()`, `UpdateProgressAsync(int)`, `FailAsync(string)`, `CompleteAsync()`,
`EndWithoutCompletingAsync()`, `WasEndedWithoutCompleting()`.

**One change (for Step 08):** `UpdateProgressAsync` also renews the lease.

```csharp
public async Task UpdateProgressAsync(int percentage)
{
    percentage = Math.Clamp(percentage, 0, 100);
    await _taskService.UpdateProgressAsync(_taskId, percentage);
    await _taskService.RenewLeaseAsync(_taskId);
}
```

This makes lease renewal free for any handler that already reports progress, which is the
convention the platform should encourage. Document in the XML comment that a long-running
handler which never reports progress **will** have its task reclaimed once the lease
expires, and that such handlers should call `UpdateProgressAsync` periodically.

## Tests to add

`tests/PS.AppPlatform.Tests/TaskContractTests.cs`:

1. `BackgroundTaskStatus.Running & BackgroundTaskStatus.ExecutingFlag` is non-zero;
   `Completed & ExecutingFlag` is zero; `Failed & CompletedFlag` is non-zero. These encode
   the assumptions `UpdateStatusAsync` relies on.
2. `TaskHandlerRegistry` resolves a handler registered as `"MyTask"` when looked up as
   `"mytask"`.
3. `BackgroundTaskCollection.AddBackgroundTask` throws when the same name is registered to
   a different type, and does **not** throw when registered to the same type twice.
4. `BackgroundTaskCollection.RegisterBackgroundTaskHandlers` puts the handler type in the
   service collection and produces a registry that resolves it.
5. `TaskHandlerContext.UpdateProgressAsync(150)` clamps to 100 and calls `RenewLeaseAsync`
   once (use an NSubstitute mock of `IBackgroundTaskManagementService`).

## Verification

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release
dotnet test
```

**Expected:** zero warnings, zero errors, all tests pass. The Step 04 test asserting
`Model.FindEntityType(typeof(BackgroundTask))` is not null should still pass now that
`BackgroundTask` is a real entity.

## Done when

- [ ] Build clean, all tests pass
- [ ] `TaskNotificationHub` was **not** ported
- [ ] `BackgroundTaskCollection` is `public`
- [ ] `TaskHandlerContext` has no `Grpc.Core` using
- [ ] `BackgroundTask.LeaseExpiresUtc` exists and matches the Step 05 column
- [ ] Duplicate task-name registration throws instead of overwriting

## Commit

```powershell
git add -A
git commit -m "Step 06: background task contracts, model, registry and handler context"
```

