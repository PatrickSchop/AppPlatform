# Step MT-05 — Tenant-aware background tasks

**Phase:** 1 — Core tenancy
**Depends on:** MT-04
**Working directory:** `C:\Dev\AppPlatform`

## Goal

In a multi-tenant app a user sees only their tenant's tasks, and a handler runs **as** the
tenant that created the task. It can use the ordinary filtered factory and never needs to
know about tenancy.

## Why this is not optional

`GET /api/tasks`, `GET /api/tasks/{id}` and `GET /api/tasks/notifications` return every row
in `BackgroundTasks` (`src/PS.AppPlatform/Endpoints/BackgroundTaskEndpoints.cs`). Once two
tenants share an app, that is a cross-tenant data leak through a platform endpoint the app
author never wrote.

## Tasks

### 1. Core script `src/PS.AppPlatform/Data/Scripts/020_AddBackgroundTaskTenancy.sql`

```sql
IF COL_LENGTH('dbo.BackgroundTasks', 'TenantId') IS NULL
    ALTER TABLE [dbo].[BackgroundTasks] ADD [TenantId] UNIQUEIDENTIFIER NULL;
GO
IF COL_LENGTH('dbo.BackgroundTasks', 'CreatedByUserId') IS NULL
    ALTER TABLE [dbo].[BackgroundTasks] ADD [CreatedByUserId] UNIQUEIDENTIFIER NULL;
GO
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_BackgroundTasks_TenantId_CreatedDate')
    CREATE INDEX [IX_BackgroundTasks_TenantId_CreatedDate]
        ON [dbo].[BackgroundTasks] ([TenantId], [CreatedDate]);
```

Nullable on purpose: every existing row, and every task in a `None` app, has no tenant. It is
embedded automatically by the existing `Data\Scripts\*.sql` glob. **Update the Step 05
embedded-scripts test**, which asserts exactly two core scripts.

### 2. `BackgroundTask` model

Add `public Guid? TenantId { get; set; }` and `public Guid? CreatedByUserId { get; set; }`.
`BackgroundTask` stays an `Entity`, not a `TenantEntity`, because the tenant is nullable.

In `PlatformDbContext.ConfigurePlatformModel`, give `BackgroundTask` the same kind of
per-instance filter as MT-03, built with the same helper:

```csharp
// t => !this.TenantFilterEnabled || t.TenantId == this.CurrentTenantId
```

With the filter enabled, tasks with a null tenant (system tasks) are invisible to users.
That is intended.

### 3. `BackgroundTaskService<TContext>`

- Inject `IScopedDbContextFactory<TContext>`, `IUnscopedDbContextFactory<TContext>` and
  `ITenantContext?` (via `GetService`; absent in `None` mode).
- `CreateTaskAsync`: set `TenantId = tenantContext?.TenantId` and
  `CreatedByUserId = tenantContext?.UserId`; save through the **unscoped** factory, because
  the interceptor's `TenantEntity` rules do not apply to this entity.
- User-facing reads (`GetAllTasksAsync`, `GetTaskStatusAsync`, `ResumeTaskAsync`): the
  **scoped** factory. Resume therefore cannot touch another tenant's task.
- Internal writes (`UpdateStatusAsync`, `UpdateProgressAsync`, `RenewLeaseAsync`): the
  **unscoped** factory. They are called by the execution manager with no request tenant.

`IBackgroundTaskService` is the user-facing interface and `IBackgroundTaskManagementService`
the internal one. Keep that split and note it in XML docs.

### 4. `BackgroundTaskEndpoints`

`GetNotificationsAsync` queries through `IDbContextFactory<PlatformDbContext>`, which since
MT-03 is the scoped adapter, so it is already filtered. **Verify this with a test rather than
assuming it.** No other endpoint change is needed. `CheckAsync` stays as is, because the
execution manager is unscoped by design.

### 5. `TaskExecutionManager<TContext>`

- Claim and lease SQL: use `IUnscopedDbContextFactory<TContext>` (raw SQL; must see all tenants).
- In `ExecuteTaskAsync`, after `CreateAsyncScope()`, before resolving the handler:

  ```csharp
  if (currentTask.TenantId is Guid tid)
      scope.ServiceProvider.GetService<TenantContext>()?.SetSystem(tid);
  ```

  Handlers resolved from that scope, and any `IDbContextFactory<TContext>` they inject, are
  then filtered to the task's tenant. A task with no tenant runs with an unresolved context:
  filtered queries fail closed, and the handler must use the unscoped factory deliberately.
- `TaskHandlerContext` gains `Guid? TenantId` so handlers can read it without DI.

### 6. `BackgroundTaskResponse`

Unchanged. Do **not** expose `TenantId` or `CreatedByUserId` to the SPA; the client never
needs them and the ids are internal.

## Tests to add

Extend `BackgroundTaskServiceTests.cs` / `TaskExecutionManagerTests.cs`, or add
`TenantTaskTests.cs` (InMemory for filtering; claim SQL stays covered by the Gate A/D SQL run):

1. Create as A and as B; scoped `GetAllTasksAsync` for A returns only A's task.
2. `GetTaskStatusAsync(B's id)` in scope A → null (the endpoint then returns 404, not 403;
   existence is not disclosed).
3. `ResumeTaskAsync(B's paused task)` in scope A → false; B's row unchanged.
4. `GetNotificationsAsync` in scope A excludes B's notification task.
5. Handler execution: a task created for B runs a test handler that queries a `TenantEntity`
   through `IDbContextFactory<T>` and sees only B's rows.
6. `None` mode: tasks created with null tenant; all tasks listed, as before.
7. Embedded script list is now `000`, `010`, `020`.

## Verification

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release
dotnet test
cd samples\SampleApp
dotnet run -- --migrate     # applies 020 once
dotnet run -- --migrate     # up to date
func start                  # then create a word-count task via the API; it reaches Completed
```

**Expected:** all tests pass; `020_AddBackgroundTaskTenancy.sql` applies once; the `SampleApp`
task lifecycle from Gate A still completes.

## Done when

- [x] No platform endpoint can return another tenant's task
- [x] Handlers run inside their task's tenant scope
- [x] `None` apps behave exactly as before, including on an existing database

## Commit

```powershell
git add -A
git commit -m "MT-05: tenant-aware background tasks with tenant-scoped handler execution"
```
