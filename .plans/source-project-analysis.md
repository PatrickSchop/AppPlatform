# Extracting a reusable "tiny app" platform from StockAnalysis

## Context

This plan references the project in "C:\Dev\StockAnalysis"

StockAnalysis runs on a genuinely cheap hosting model: an Azure Functions
consumption app (`stockanalysis-api`) that serves both the REST API and the SPA
bundle, an Angular app hosted as blobs in `stockinfostorage`, and a database on a
shared Azure SQL server (`pschop-db`). Cost at idle is effectively zero.

The goal is to reuse that model for other small side projects without re-deriving
the setup each time (and without the shortcuts that get taken when each project
starts from scratch). Three things are wanted: a reusable core (ideally NuGet), a
shared-but-not-entangled Azure footprint, and front-end freedom to pick Angular or
React per app.

**Verdict: achievable.** The codebase already has a clean seam â€” `App/Host/`,
`App/Database/`, `App/BackgroundTasks/`, `App/StaticContent/` and `App/ServiceBuilder.cs`
contain no stock-domain knowledge, while `App/StockData/`, `App/Api/` and most of
`App/Models/` are entirely domain. Two real technical constraints shape the design
(Â§2), and the front-end concern turns out to be a non-issue (Â§4).

---

## 1. What is actually reusable

Confirmed domain-free, roughly 25 files:

| Area | Files | Notes |
|---|---|---|
| Bootstrap | [App/Program.cs](App/Program.cs), [App/ServiceBuilder.cs](App/ServiceBuilder.cs) | Reflection-discovered DI modules; assembly-relative config layering; dual-mode `--migrate` entry point |
| Host | [App/Host/](App/Host/) â€” `AzureIdentityProvider`, `HostingEnvironment`, `CORSMiddleware`, the two auth middleware adapters | `AzureIdentityProvider` (4-mode credential switch) is the single most reusable file in the repo |
| Data | [App/Database/](App/Database/) â€” `DatabaseConfiguration`, `AzureSqlTokenInterceptor`, `DatabaseMigrator`, `Models/Entity.cs` | Script-based migrations, managed-identity SQL tokens |
| Tasks | [App/BackgroundTasks/](App/BackgroundTasks/) (all but `TaskNotificationHub.cs`) | SQL-backed claim queue, reflection dispatch |
| Static | [App/StaticContent/](App/StaticContent/) | `IFilesProvider` â†’ local/blob, catch-all SPA route |
| Config API | [App/Api/WebAppConfiguration.cs](App/Api/WebAppConfiguration.cs) | Projects the `webApp` config section to `/configuration.json` |
| LLM | [App/StockData/UserInput/LlmTextParserBase.cs](App/StockData/UserInput/LlmTextParserBase.cs) | Generic typed LLM parsing with retry; no finance knowledge despite its location |

Explicitly **not** reusable: all of `App/StockData/` except `LlmTextParserBase`/
`ILlmTextParseClient`/`HtmlToXhtmlConverter`, all domain endpoints in `App/Api/`,
all of `App/Models/` except `Entity` and `BackgroundTask`.

Dead code to drop rather than port: `BackgroundTasks/TaskNotificationHub.cs` and
[App/Api/TaskNotificationHubEndpoint.cs](App/Api/TaskNotificationHubEndpoint.cs)
(SignalR stubs â€” `Host/ServiceBuilder.cs:19` says SignalR was removed and status is
polled), the `@microsoft/signalr` dependency, and [WebApp/Program.cs](WebApp/Program.cs)
+ the npm MSBuild targets in [WebApp/WebApp.csproj](WebApp/WebApp.csproj) (never
deployed â€” CI builds Angular directly).

---

## 2. The two constraints that shape the design

### 2.1 Azure Functions cannot see `[Function]` methods in a referenced assembly

This is the load-bearing finding, verified against the installed SDK rather than
assumed:

- `Microsoft.Azure.Functions.Worker.Sdk.targets:49-51` defaults
  `FunctionsEnableWorkerIndexing` to `true`, which turns on
  `FunctionsEnableMetadataSourceGen`.
- The generated `App/bin/Debug/net10.0/worker.config.json` confirms
  `"workerIndexing": "true"` â€” so at runtime the **host asks the worker** for its
  function list, and the answer comes from a Roslyn source generator
  (`Microsoft.Azure.Functions.Worker.Sdk.Generators`, shipped as an analyzer).
- A Roslyn generator only sees the **current compilation**. `[Function]` methods
  inside a NuGet package are therefore invisible.

There is a build-time fallback: the MSBuild `GenerateFunctionMetadata` task *does*
scan references (`ReferencePaths="@(ReferencePath)"`, targets:189-197) and emits a
per-entry `scriptFile`, which is why `App/obj/Debug/net10.0/functions.metadata` has
that field. Setting `<FunctionsEnableWorkerIndexing>false</FunctionsEnableWorkerIndexing>`
would make package-hosted functions work.

**Do not take that route.** It is the legacy path, and it costs
`canUsePlaceholder`, i.e. cold-start optimisation â€” which matters most on exactly
the zero-cost consumption plan this whole exercise is built around.

**Solution:** keep all logic in the library, and keep the ~10-line `[Function]`
shims in the consuming app's compilation. Inject them from the package rather than
scaffolding copies, so they still upgrade:

```xml
<!-- build/PS.AppPlatform.Functions.targets, imported automatically -->
<ItemGroup>
  <Compile Include="$(MSBuildThisFileDirectory)../endpoints/*.cs" />
</ItemGroup>
```

Each injected shim is trivial and delegates to a public service in the library:

```csharp
public class TaskEndpoints(IBackgroundTaskEndpoints inner)
{
    [Function("GetNotificationTasks")]
    public Task<IActionResult> GetNotifications(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/tasks/notifications")] HttpRequest req)
        => inner.GetNotificationsAsync(req);
}
```

This keeps default worker indexing, keeps cold starts fast, and means a core
upgrade ships new endpoints automatically. Types the shims touch must be `public`
(much of the current code is `internal`).

### 2.2 `AppDbContext` is concrete and single-assembly

[App/Database/AppDbContext.cs](App/Database/AppDbContext.cs) hard-codes all eleven
`DbSet`s, and `DiscoverEntityTypes()` scans `Assembly.GetExecutingAssembly()`. Same
pattern in `Program.BuildServices` (`Program.cs:172-190`) for `ServiceBuilder`
discovery.

**Solution:** the library ships `PlatformDbContext` holding only
`DbSet<BackgroundTask>` plus the `Entity`-subclass auto-configuration; apps derive
from it. Registration goes generic:

```csharp
services.AddPlatformData<AppDbContext>(configuration);   // TContext : PlatformDbContext
```

`TaskExecutionManager` then depends on `IDbContextFactory<TContext>` rather than
`IDbContextFactory<AppDbContext>`. Both reflection scans take an explicit assembly
list (core assembly + entry assembly + any registered extras) instead of
`GetExecutingAssembly()`.

Note the raw claim SQL in
[App/BackgroundTasks/TaskExecutionManager.cs:102-145](App/BackgroundTasks/TaskExecutionManager.cs#L102-L145)
hard-codes `[dbo].[BackgroundTasks]`. With the per-app-database model chosen in Â§5
this needs no change â€” schema parameterisation would only be required if apps
shared one database.

---

## 3. Recommended packaging: NuGet, reached in stages

NuGet is the right destination â€” it is the only option that lets a fix reach
already-built apps. The constraints above are real but both are solvable, so there
is no reason to fall back to a template repo or monorepo.

Build it in a new `PS.AppPlatform` repo:

```
src/PS.AppPlatform/            # engine; no [Function] attributes anywhere
    Hosting/      ServiceBuilder, PlatformHostBuilder, config layering,
                  HostingEnvironment, AzureIdentityProvider
    Auth/         Functions-native authorization (see Â§6)
    Data/         PlatformDbContext, DatabaseConfiguration,
                  AzureSqlTokenInterceptor, DatabaseMigrator + embedded core scripts
    Tasks/        ITaskHandler<T>, TaskExecutionManager, BackgroundTaskService,
                  registry, lease recovery
    StaticContent/ IFilesProvider, Local/Blob providers, SPA fallback
    Llm/          LlmTextParserBase<T>, ILlmTextParseClient
src/PS.AppPlatform.Functions/  # endpoint implementations (plain classes)
    endpoints/    [Function] shims, injected via build/*.targets  (Â§2.1)
templates/tinyapp/                # dotnet new tinyapp
infra/                            # shared bicep modules (Â§5)
clients/                          # TypeScript packages (Â§4)
```

**Sequencing** â€” each stage is independently useful, and stage 1 validates the
seam before any packaging work is spent:

1. **Extract behind a `ProjectReference`.** Create the platform projects, move the
   files listed in Â§1, fix the two constraints in Â§2, and make StockAnalysis the
   first consumer. Nothing is published yet; the seam is proven by StockAnalysis
   still running.
2. **Publish to GitHub Packages** (free for private feeds) and switch
   StockAnalysis to a `PackageReference`.
3. **`dotnet new tinyapp` template** â€” scaffolds `Program.cs`, csproj, appsettings
   layering, `Database/Scripts/100_InitialSchema.sql`, the GitHub Actions
   workflows and the bicep parameter file. Build a second throwaway app from it;
   that is the real test of the whole exercise.
4. **TypeScript client packages and front-end starters** (Â§4).

### Migration scripts across the package boundary

[App/Database/DatabaseMigrator.cs](App/Database/DatabaseMigrator.cs) reads `*.sql`
from a folder and runs **every script on every call**, relying on hand-written
`IF NOT EXISTS` guards, with no tracking table. Two changes when extracting:

- Core scripts (the `BackgroundTasks` table â€” today's `004`/`005`/`006`) ship as
  **embedded resources** in the package and always run first. Reserve `000-099`
  for core, `100+` for apps, so ordering across the boundary is well-defined.
- Add a `__SchemaVersions` tracking table so scripts run once. The current
  re-run-everything approach already has a latent drift bug: `004` creates the
  table without the `StatusMessage`/`ExecutionManagerId` columns that `005`/`006`
  add, which only works because they always run together.

---

## 4. Front-end flexibility â€” the concern does not apply

The worry was that background tasks would tie the platform to Angular. They do not.
The backend has **zero** Angular coupling, and the task mechanism is plain REST
polling, not SignalR:

- [WebApp/src/app/services/background-task.service.ts](WebApp/src/app/services/background-task.service.ts)
  polls `GET /api/tasks/notifications` on an adaptive interval (1s while a task is
  running, 30s idle, with a 10s "expect task start" boost). That is ~150 lines of
  ordinary TypeScript â€” `rxjs` is used only as a `setTimeout` wrapper.
- The SignalR hub is a stub that was never implemented
  ([TaskNotificationHubEndpoint.cs:30-43](App/Api/TaskNotificationHubEndpoint.cs#L30-L43)),
  and `@microsoft/signalr` has zero imports anywhere in `WebApp/src`.

So the entire front-end contract is four things: `GET /configuration.json`,
`GET /api/tasks/*`, the catch-all static route, and an `Authorization` header.
Nothing framework-specific.

**Ship a plain-TypeScript SDK plus thin per-framework adapters:**

| Package | Contents |
|---|---|
| `@PS/app-client` | Zero deps. `loadConfig()`, `ApiClient` (fetch + bearer + error mapping), `TaskPoller` (adaptive polling, callback/`EventTarget` based), `AuthClient` wrapping `@azure/msal-browser` |
| `@PS/app-client-angular` | `provideAppPlatform()`, `BackgroundTaskService` wrapping `TaskPoller`, HTTP interceptor |
| `@PS/app-client-react` | `PlatformProvider`, `useBackgroundTasks()`, `useApi()` |

`@azure/msal-browser` is itself framework-agnostic, so auth needs no per-framework
work either. Start with the Angular starter (port the existing `WebApp` minus the
stock domain â€” it already has a design system in `WebApp/src/styles/`), add a Vite
React starter in the same stage.

**One backend fix is required for React Router, and it is a bug today:**
[App/StaticContent/Static.cs:28-41](App/StaticContent/Static.cs#L28-L41) only falls
back to `index.html` for the *empty* path. Any deep link â€” `/dashboard`,
`/management` â€” returns 404 on hard refresh. The core `Static` handler must fall
back to `index.html` whenever the request has no file extension. While there, add
`.ico`/`.woff2`/`.map` to `InferContentType` and add `ETag`/`Cache-Control`.

---

## 5. Azure: shared server, per-app database and function

Matching the current topology, per app:

| Shared (created once) | Per app |
|---|---|
| Resource group `Applications` | Azure SQL **database** on `pschop-db` |
| SQL server `pschop-db` | Function App (consumption) |
| Storage account `stockinfostorage` | Blob container `web-<app>` |
| Entra tenant | User-assigned managed identity |
| Azure OpenAI account | Custom domain `<app>.PS.nl` |

Today **none** of this is in code â€” there is no bicep, ARM or terraform anywhere in
the repo, and every resource was created by hand. That is precisely the "shortcuts
get taken" risk. Add `infra/` bicep modules to the platform repo: `app.bicep` takes
an app name and creates the per-app column above, referencing the shared resources
by resource ID. A new app then becomes `dotnet new tinyapp` + one bicep deploy.

Both GitHub Actions workflows should move into the platform repo as reusable
workflows (`workflow_call`), parameterised by app name, so each app keeps a ~15-line
`deploy.yaml`. Fix two bugs while porting
[.github/workflows/build.yaml](.github/workflows/build.yaml): the inputs are
declared `dotnetversion`/`node_version` but read as `inputs.dotnet_version`
(so `setup-dotnet` silently gets an empty value), and the default `9.0.x` is stale
against `net10.0`.

**Auth reuse.** To genuinely share the authorization setup rather than repeat it,
use one shared app-registration pair (one SPA client + one API) with a **per-app
App Role** (`stock.user`, `recipes.user`, â€¦), and have the core enforce a required
role from config. One consent, one client ID, one line of config per app. The
trade-off â€” all apps share a token audience, so separation rests on the role check
â€” is acceptable at side-project scale but should be a deliberate choice.

---

## 6. Auth needs finishing, not just moving

The `feature/OIDC` work is incomplete and currently provides no protection. This
must be resolved during extraction, because every future app inherits it.

- **Only one `[Authorize]` exists in the whole backend** â€” on the static-content
  function ([Static.cs:25](App/StaticContent/Static.cs#L25)). Every `/api/*`
  endpoint is `AuthorizationLevel.Anonymous` with no `[Authorize]`, including
  `api/tasks/check` and `api/initializeDatabase`. The `RequireAuthenticatedUser`
  policy defined at `Program.cs:63-66` is never referenced.
- **The middleware approach is unlikely to work as written.**
  [App/Host/AuthorizationMiddleware.cs](App/Host/AuthorizationMiddleware.cs) wraps
  ASP.NET Core's `AuthorizationMiddleware`, which reads policy from **endpoint
  metadata**. In the isolated worker there is no ASP.NET `Endpoint` on the
  `HttpContext`, so `[Authorize]` attributes on `[Function]` methods are not seen.
  The core should instead read `[Authorize]`/`[AllowAnonymous]` off the target
  method via `FunctionContext.GetTargetFunctionMethod()` and evaluate policies
  directly through `IAuthorizationService` â€” and default to **deny** unless a
  function opts out.
- **Ordering is wrong.** CORS runs *after* authorization (`Program.cs:72-75`), so a
  401 never carries `Access-Control-Allow-Origin` and the browser reports an opaque
  CORS error instead. CORS must run first and answer `OPTIONS` before auth.
- Both middleware resolve services from the **root** provider rather than the
  per-invocation scope.
- `tenantId == clientId` in [App/appsettings.json](App/appsettings.json) â€” both are
  `6d91dfaa-â€¦`. Almost certainly a copy-paste error; audience validation will look
  for the tenant GUID as the audience.
- **The front-end cannot authenticate at all.** No MSAL, no interceptor, no guards
  in `WebApp`. Enabling enforcement today locks the SPA out. Â§4's `AuthClient`
  closes this.

### Security items to handle separately

A **live Azure Cognitive Services API key is committed** at
[App/appsettings.json:25](App/appsettings.json#L25) and is present in git history,
not just the working tree. Rotate it, and have the template keep secrets in Function
App settings or Key Vault references rather than `appsettings.json`.

---

## 7. Correctness fixes to make during extraction

These are latent bugs in code that is about to be shared by every future app.

1. **Scoped `TaskExecutionManager` breaks concurrency limiting.** The uncommitted
   `Singleton â†’ Scoped` change in
   [App/BackgroundTasks/ServiceBuilder.cs](App/BackgroundTasks/ServiceBuilder.cs)
   is a reasonable fix for captured `DbContext`s, but `_executionManagerId` is
   generated in the constructor
   ([TaskExecutionManager.cs:43](App/BackgroundTasks/TaskExecutionManager.cs#L43))
   and the claim SQL counts running tasks *for that id only*. Every HTTP request now
   gets a fresh id, so `maxConcurrentTasks` no longer caps anything globally, and
   the `SemaphoreSlim` serialises nothing. Make the manager id a host-lifetime
   singleton value injected into a scoped manager.
2. **No orphan recovery.** If the worker dies, rows stuck in `Running` under a dead
   `ExecutionManagerId` are never reclaimed. Add a lease timestamp and reclaim
   stale leases.
3. **The only scheduler trigger is a self-HTTP-POST.**
   `TriggerTaskCheckAsync` ([BackgroundTaskService.cs:177-201](App/BackgroundTasks/BackgroundTaskService.cs#L177-L201))
   POSTs to `backgroundTasks:apiBaseUrl` + `/api/tasks/check`, fire-and-forget with
   a 5s timeout. A wrong URL, a scale-out to another instance, or task creation
   from the `--migrate` CLI path all mean queued tasks silently never start. Add a
   timer trigger every few minutes as a safety net (the consumption plan already
   has `AzureWebJobsStorage`).
4. **Contract drift** â€” the Angular client calls two endpoints that do not exist:
   `POST /api/tasks` (`background-task.service.ts:140`, no server-side creator) and
   `POST /api/analysis/analyze`, whose handler is commented out at
   [AnalysisResultsManagement.cs:111-113](App/Api/AnalysisResultsManagement.cs#L111-L113)
   while the UI button calls it. Decide per endpoint: implement or delete.
5. **`ServiceBuilder` discovery order is non-deterministic** (reflection order).
   It works today only because all cross-module wiring goes through resolver
   lambdas. Document that as a contract, or add explicit ordering.
6. Dead `BuildConfiguration` hook on the `ServiceBuilder` base class â€” never called.
   Drop it or wire it.

---

## 8. Verification

Each stage has a concrete pass/fail, so the seam is proven before more is built on it.

**Stage 1 (extraction, `ProjectReference`)** â€” the regression gate is that
StockAnalysis is unchanged in behaviour:
- `dotnet build StockAnalysis.slnx` and `dotnet test App.Test/App.Test.csproj`.
- Inspect `App/obj/Debug/net10.0/functions.metadata` and confirm the platform
  endpoints (`GetNotificationTasks`, `GetTasks`, `CheckTasks`, `StaticContent`,
  `GetWebAppConfiguration`) are present with `"scriptFile": "App.dll"` â€” this is
  the direct check that Â§2.1's shim injection worked.
- `func start` in `App/`, then `npm start` in `WebApp/`. Verify: the dashboard
  loads; `GET /configuration.json` returns the `webApp` section; "update all
  stocks" creates a task and the navbar progress popover advances to completion.
- Deep-link check (the Â§4 fix): hard-refresh `http://localhost:7095/dashboard` and
  confirm `index.html` is served rather than a 404.
- Migration check: `dotnet exec App.dll --migrate --settingsFile appsettings.development.json`
  against a scratch LocalDB, twice â€” the second run must be a no-op and report
  nothing applied, proving `__SchemaVersions` works.

**Stage 3 (template)** â€” the real test of the whole exercise:
- `dotnet new tinyapp -n ScratchApp`, add one `Entity`, one `ITaskHandler<T>` and
  one endpoint, deploy to a scratch database on `pschop-db` via the bicep module,
  and confirm auth, background tasks and SPA hosting all work with no code copied
  from StockAnalysis. Target: under an hour from `dotnet new` to a deployed app.

**Stage 4 (front-end)** â€” build the React starter against the same running backend
and confirm background-task progress works with no backend change. That is the
proof that front-end flexibility is real rather than claimed.

---

## 9. Out of scope / open

- **~35 AI-generated status docs** at the repo root (`FINAL_DELIVERY_REPORT.md`,
  `REFACTORING_COMPLETE.md`, â€¦) and an auto-generated `README.md` that documents a
  single feature. Worth deleting as part of the cleanup, but it is not blocking.
- **Multi-tenancy** â€” no `UserId`/`TenantId` on any entity, no query filters. Remote
  branches `origin/feature/multi-tenancy-implementation` and
  `origin/cursor/orchestrate-multi-tenancy-implementation-phases-269d` suggest this
  was attempted before. Out of scope here; the per-app database model in Â§5 sidesteps
  it for now, but if any app needs per-user data the platform should grow a
  `UserScopedEntity` convention rather than each app inventing one.

