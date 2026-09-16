# Step 12 — Endpoint services

**Phase:** 1 — Core engine
**Depends on:** Step 11
**Working directory:** `C:\Dev\AppPlatform`

## Goal

Implement every platform endpoint as a **plain injectable service** with no `[Function]`
attribute. Step 13 wraps these in shims. This split is the whole mechanism of analysis §2.1.

## The contract

Each service takes an `HttpRequest` and returns `IActionResult`, so the shim is a one-line
delegation with no logic of its own. Interfaces are non-generic, so shims never need type
parameters.

All files go in `src/Wisdi.AppPlatform/Endpoints/`, namespace `Wisdi.AppPlatform.Endpoints`.

## Reference material (read-only)

| Source | Becomes |
|---|---|
| `App\Api\GetNotificationTasks.cs` | `IBackgroundTaskEndpoints.GetNotificationsAsync` |
| `App\Api\TaskManagement.cs` | `GetAllAsync`, `GetByIdAsync`, `CheckAsync` |
| `App\Api\WebAppConfiguration.cs` | `IConfigurationEndpoints.GetWebAppConfigurationAsync` |
| `App\Api\InitializeDatabase.cs` | `IDatabaseEndpoints.InitializeAsync` |
| `App\StaticContent\Static.cs` | `StaticContentHandler` (already done, Step 09) |

**Do not port** `App\Api\TaskNotificationHubEndpoint.cs` — dead SignalR stub (analysis §1).

## Tasks

### 1. `Endpoints/IBackgroundTaskEndpoints.cs`

```csharp
public interface IBackgroundTaskEndpoints
{
    Task<IActionResult> GetNotificationsAsync(HttpRequest request, CancellationToken ct = default);
    Task<IActionResult> GetAllAsync(HttpRequest request, CancellationToken ct = default);
    Task<IActionResult> GetByIdAsync(HttpRequest request, string id, CancellationToken ct = default);
    Task<IActionResult> CreateAsync(HttpRequest request, CancellationToken ct = default);
    Task<IActionResult> CheckAsync(HttpRequest request, CancellationToken ct = default);
}
```

### 2. `Endpoints/BackgroundTaskEndpoints.cs`

Implements the above from `IBackgroundTaskService`, `ITaskExecutionManager` and
`ILogger<BackgroundTaskEndpoints>`.

Port the bodies from the source, with one shared change: extract the repeated anonymous
projection into a single `BackgroundTaskResponse` record, so the three read endpoints cannot
drift from each other.

```csharp
public sealed record BackgroundTaskResponse(
    Guid id, string taskType, string status, string statusMessage,
    int completionPercentage, string description, bool requiresNotification,
    DateTime createdDate, DateTime updatedDate, DateTime? startedDate, DateTime? completedDate);
```

Keep the lowercase property names — the existing Angular client (and the Step 23 TypeScript
client) reads exactly these. `status` is `Status.ToString()`, not the numeric value.

**`GetNotificationsAsync`** — port the source filter verbatim: tasks with
`RequiresNotification` that are either not in a terminal state, or completed within the last
12 hours. Move the 12 hours to `backgroundTasks:notificationWindowHours`, default `12`.

**`CreateAsync`** — this is **new**. Analysis §7.4 records contract drift: the Angular client
calls `POST /api/tasks` (`background-task.service.ts:140`) and no server-side creator exists.
The decision for the platform is **implement it**, because a generic task-creation endpoint
is exactly the kind of thing the platform should provide.

Request body:
```json
{ "taskType": "string", "taskData": {}, "description": "string", "requiresNotification": true }
```

Behaviour:
- 400 when `taskType` is missing or blank
- **400 when `taskType` is not in `ITaskHandlerRegistry`** — reject unknown types at the door
  rather than persisting a task that can only fail
- `taskData` is passed through as raw JSON; the platform does not know its shape
- 201 with `{ "taskId": "<guid>" }`

The second rule means `CreateAsync` needs `ITaskHandlerRegistry` injected as well.

**`CheckAsync`** — calls `ITaskExecutionManager.CheckAndStartTasksAsync()` and returns
`{ "message": "Task check completed" }`, as the source does.

**`GetByIdAsync`** — 400 on an unparseable GUID, 404 when absent, as the source does.

### 3. `Endpoints/IConfigurationEndpoints.cs` / `ConfigurationEndpoints.cs`

Port `App\Api\WebAppConfiguration.cs`. It projects the `webApp` configuration section to
JSON at `/configuration.json`, recursing through `IConfigurationSection` children and
returning `{}` when the section is absent. Port `ConvertConfigurationSectionToObject`
verbatim.

**One addition:** arrays. `IConfiguration` represents an array as children keyed `"0"`,
`"1"`, … and the source's dictionary projection turns `["a","b"]` into `{"0":"a","1":"b"}`.
Detect all-numeric consecutive keys starting at `"0"` and emit a JSON array instead. A
front-end config with a list in it is common enough that this will otherwise bite.

**Security note to put in the XML comment:** this endpoint is anonymous and its response is
public. Only non-secret values belong under `webApp`.

### 4. `Endpoints/IDatabaseEndpoints.cs` / `DatabaseEndpoints.cs`

Port `App\Api\InitializeDatabase.cs`. It runs `IDatabaseMigrator.InitializeDatabaseAsync()`.

**Guarding this one matters.** In the source it is `AuthorizationLevel.Anonymous` with no
`[Authorize]` — an anonymous caller can trigger a schema migration (analysis §6). Here:
- the shim carries `[Authorize]`, so Step 10's default-deny covers it, **and**
- the service itself re-checks `database:apiMigration:enable` and returns
  `403` with `{"error":"Database migration over the API is disabled."}` when false

Two independent gates, because the consequence of getting this wrong is schema changes from
the public internet. Default `enable` to `false`.

Return the `MigrationResult` as JSON: `success`, `message`, `appliedMigrations`, `canConnect`.
Never return `Error` (the full exception `ToString()`) to the caller — log it and return the
message only.

### 5. `Endpoints/IHealthEndpoints.cs` / `HealthEndpoints.cs`

**New** — not in the source, but every deployed app needs it and it should not be reinvented
per app.

`GET /api/health` returns `200` with:
```json
{ "status": "ok", "version": "0.1.0", "environment": "production", "database": "ok" }
```

`database` is `"ok"` / `"unavailable"` from `CanConnectAsync` with a 2-second timeout, or
`"skipped"` when `health:checkDatabase` is false (the default, so that a cold start is not
slowed by a SQL connection). `version` is the platform assembly's informational version.

This endpoint is in `AnonymousFunctions` by default — a health check that requires a token is
not much of a health check. Document that.

### 6. `Endpoints/EndpointsServiceBuilder.cs`

```csharp
services.AddScoped<IConfigurationEndpoints, ConfigurationEndpoints>();
services.AddScoped<IHealthEndpoints, HealthEndpoints>();
services.AddScoped<IBackgroundTaskEndpoints, BackgroundTaskEndpoints>();
services.AddScoped<IDatabaseEndpoints, DatabaseEndpoints>();
```

All four depend only on non-generic interfaces, so none of this needs `TContext`.

## Tests to add

`tests/Wisdi.AppPlatform.Tests/EndpointTests.cs`, with `DefaultHttpContext` and mocks:

1. `GetNotificationsAsync` excludes a completed task older than the window and includes one
   completed inside it; includes a running task regardless of age.
2. `GetByIdAsync("not-a-guid")` is 400; an unknown id is 404.
3. `CreateAsync` with a blank `taskType` is 400.
4. **`CreateAsync` with an unregistered `taskType` is 400** and no task is created.
5. `CreateAsync` with a registered type is 201 and the body has a parseable `taskId`.
6. `GetWebAppConfigurationAsync` returns `{}` when `webApp` is absent.
7. It projects nested sections to nested objects.
8. **It projects `webApp:origins:0/1` to a JSON array**, not an object.
9. `InitializeAsync` is 403 when `database:apiMigration:enable` is false, and the migrator is
   never called.
10. `InitializeAsync` never includes the `Error` field in its response body.
11. `GetHealthAsync` is 200 with `"status":"ok"` and reports `"skipped"` by default.

## Verification

```powershell
cd C:\Dev\AppPlatform
dotnet build --configuration Release
dotnet test
```

Then confirm the §2.1 invariant still holds — the Step 02 architecture test does this, but
check by eye as well:

```powershell
Select-String -Path src\Wisdi.AppPlatform\**\*.cs -Pattern '\[Function\('
```

**Expected:** build clean, all tests pass, and the `Select-String` returns **nothing**.

## Done when

- [ ] Build clean, all tests pass
- [ ] `Select-String` finds no `[Function(` anywhere in the engine
- [ ] Every endpoint interface is non-generic
- [ ] `POST /api/tasks` exists and rejects unregistered task types (closes §7.4 drift)
- [ ] Database initialisation is gated twice: `[Authorize]` on the shim and the config check
- [ ] `TaskNotificationHubEndpoint` was not ported
- [ ] Config projection handles arrays

## Commit

```powershell
git add -A
git commit -m "Step 12: platform endpoints as plain injectable services; add task creation and health endpoints"
```
